// 运行时"克隆 + 重染"特效：把借来的原版 prefab 实例化一份，只改副本的着色，原特效不碰。
// 这是 docs/fx-recolor.md 里 SmokeFx 那套做法的通用版：SmokeFx 只重染成灰白、跟区域同生同灭；
// 这里参数化目标色 + 可选自删时长 + 可选"自适应尺寸"，供"施法落点一次性爆发"（如剑雨染橙金）复用。
using System;
using System.Collections.Generic;
using Kingmaker.ResourceLinks;
using UnityEngine;

namespace RhodesDoctor
{
  internal static class FxTint
  {
    /// <summary>
    /// 只把**名字匹配 <paramref name="nameContains"/> 的粒子系统**（以及同名的粒子渲染器上的
    /// 材质 / 拖尾材质）重新染成 rgb；其余部件保持 <see cref="Apply"/> 已经染好的颜色不动。
    ///
    /// 用途：同一份借来的 prefab 里**不同部件要不同颜色**时（例：骑兵突进的"地面那条拖尾"
    /// 要比"周身那圈火花"更淡，否则底部发深）。`Apply` 是一把梭全染，这个是在它之后再"点染"一部分。
    /// 名字用不区分大小写的包含匹配（原版 prefab 的粒子名如 `Trail_cycle` / `Sparks_Body (2)`）。
    /// </summary>
    internal static void ApplyTo(GameObject go, Color rgb, string nameContains)
    {
      if (go == null || string.IsNullOrEmpty(nameContains)) return;
      var key = nameContains.ToLowerInvariant();
      bool Matches(string n) =>
        !string.IsNullOrEmpty(n) && n.ToLowerInvariant().IndexOf(key, StringComparison.Ordinal) >= 0;

      foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
      {
        try
        {
          if (ps == null || !Matches(ps.name)) continue;
          var main = ps.main;
          main.startColor = Tinted(main.startColor, rgb);
          var col = ps.colorOverLifetime;
          if (col.enabled) col.color = Tinted(col.color, rgb);
          var cbs = ps.colorBySpeed;
          if (cbs.enabled) cbs.color = Tinted(cbs.color, rgb);
          var trails = ps.trails;
          if (trails.enabled)
          {
            try { trails.colorOverLifetime = Tinted(trails.colorOverLifetime, rgb); } catch { }
            try { trails.colorOverTrail = Tinted(trails.colorOverTrail, rgb); } catch { }
          }
          Dbg.Log($"[RhodesDoctor] 特效点染：粒子 '{ps.name}' → {rgb}。");
        }
        catch { }
      }
      foreach (var psr in go.GetComponentsInChildren<ParticleSystemRenderer>(true))
      {
        try
        {
          if (psr == null || !Matches(psr.name)) continue;
          NeutralizeMaterial(psr.material, rgb, 0f, true);
          NeutralizeMaterial(psr.trailMaterial, rgb, 0f, true);
        }
        catch { }
      }
    }

    // 去色/染色贴图按 (源贴图, 参数) 缓存；失败也缓存 null，避免反复报错。
    // 参数是 float（去色提亮 lift）或 Color（染色目标色），用 object 统一容纳。
    private static readonly Dictionary<(Texture tex, object param), Texture2D> TexCache = new();

    // ── P2-A：反射结果按 Type 缓存（高频施放时避免每个组件现做 GetFields/GetProperties）──
    // 键是组件 Type；值为该类**可染色的** Color/Gradient 字段与属性清单。
    // 同一 prefab 的克隆体组件类型固定，缓存命中率极高（剑雨 18 份克隆 × N 组件是主要受益点）。
    private static readonly Dictionary<Type, (System.Reflection.FieldInfo[] Fields,
      System.Reflection.PropertyInfo[] Props)> ColorMemberCache = new();

    // 只缓存"material 可读写属性是否存在"，避免每次克隆对每个非 Renderer 组件反射查属性。
    private static readonly Dictionary<Type, System.Reflection.PropertyInfo> MaterialPropCache = new();

    private const System.Reflection.BindingFlags PubInst =
      System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;

    /// <summary>取"名字像颜色"的可读写 Color/Gradient 字段与属性（按 Type 缓存，失败缓存空数组）。</summary>
    private static (System.Reflection.FieldInfo[] Fields, System.Reflection.PropertyInfo[] Props)
      ColorMembers(Type t)
    {
      if (ColorMemberCache.TryGetValue(t, out var cached)) return cached;
      System.Reflection.FieldInfo[] fields;
      System.Reflection.PropertyInfo[] props;
      try
      {
        var allFields = t.GetFields(PubInst);
        var fl = new List<System.Reflection.FieldInfo>(allFields.Length);
        foreach (var f in allFields)
          if ((f.FieldType == typeof(Color) || f.FieldType == typeof(Gradient)) && LooksLikeColorName(f.Name))
            fl.Add(f);
        fields = fl.ToArray();
        var allProps = t.GetProperties(PubInst);
        var pl = new List<System.Reflection.PropertyInfo>(allProps.Length);
        foreach (var p in allProps)
          if (p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0
            && p.PropertyType == typeof(Color) && LooksLikeColorName(p.Name))
            pl.Add(p);
        props = pl.ToArray();
      }
      catch
      {
        fields = new System.Reflection.FieldInfo[0];
        props = new System.Reflection.PropertyInfo[0];
      }
      var result = (fields, props);
      ColorMemberCache[t] = result;
      return result;
    }

    /// <summary>取"公开实例、类型为 Material"的 material 属性（按 Type 缓存；无则缓存 null）。</summary>
    private static System.Reflection.PropertyInfo MaterialProp(Type t)
    {
      if (MaterialPropCache.TryGetValue(t, out var cached)) return cached;
      System.Reflection.PropertyInfo p = null;
      try { p = t.GetProperty("material", PubInst); } catch { }
      MaterialPropCache[t] = p;
      return p;
    }


    // 每个素材首次生成时打印一次"缩放前"的尺寸与渲染器明细，便于标定 FitToMeters（只打一次，不刷屏）。
    private static readonly HashSet<string> SizeLogged = new();

    /// <summary>
    /// 实例化一个借来的 prefab、重染成 rgb 色，lifetime 秒后自删（lifetime ≤ 0 则不自动删）。
    /// link 为空 / 加载失败返回 null（调用方无需处理）。
    /// </summary>
    internal static GameObject Spawn(PrefabLink link, Vector3 point, Color rgb, float lift, float lifetime)
      => Spawn(link, point, Quaternion.identity, rgb, lift, lifetime, 0f, 0f);

    /// <summary>
    /// 同上，可指定朝向与缩放（scale ≤ 0 表示用 prefab 原始大小）。
    /// 缩放用于把成片的区域特效（刃障等）缩小到法术范围。
    /// </summary>
    internal static GameObject Spawn(PrefabLink link, Vector3 point, Quaternion rotation,
      Color rgb, float lift, float lifetime, float scale)
      => Spawn(link, point, rotation, rgb, lift, lifetime, scale, 0f);

    /// <summary>
    /// 同上，并支持"按实测自适应尺寸"：fitToMeters &gt; 0 时先量 prefab 自身的水平包围盒，
    /// 再把这一份缩放到水平跨度刚好等于 fitToMeters 米，然后才重染。fitToMeters 优先于 scale。
    ///
    /// ⚠️ **只适合网格/贴地类特效，粒子含量高的素材不要用**：包围盒取的是全部渲染器 `bounds` 的并集，
    /// 而粒子系统的 `bounds` 常被吹得远大于可见粒子——实测刃障那个 prefab 报 348 米，
    /// 画面上可见的散布只有约 25 米（差一个数量级）。这种素材用 fitToMeters 会缩到几乎看不见，
    /// 应该改为**固定 scale** 并按实测反推（剑雨就是这么处理的，见 SpellsSetup 的 SwordRain 分支）。
    /// 首次生成时会打一条 `[特效尺寸]` 日志（缩放前的总尺寸 + 每个渲染器的明细），
    /// 拿它对比"画面上实际多大"就能判断包围盒有没有被吹大。
    /// </summary>
    internal static GameObject Spawn(PrefabLink link, Vector3 point, Quaternion rotation,
      Color rgb, float lift, float lifetime, float scale, float fitToMeters)
      => Spawn(link, point, rotation, rgb, lift, lifetime, scale, fitToMeters, 0f);

    /// <summary>
    /// 同上，外加 <paramref name="fitLongestMeters"/>：按包围盒**三轴里最长的那一条**缩放。
    ///
    /// 为什么需要它（剑雨踩过的坑）：<c>fitToMeters</c> 量的是**水平面**（x/z 取大），
    /// 只适合"铺在地上"的特效。**竖着的物件**（比如立起来的剑）长度在 **Y 轴**上，
    /// 用水平面去量就只量到剑身的厚度，于是把厚度缩放到目标值——整把剑被放大好几倍。
    /// 竖直物件一律用 fitLongestMeters。
    /// </summary>
    internal static GameObject Spawn(PrefabLink link, Vector3 point, Quaternion rotation,
      Color rgb, float lift, float lifetime, float scale, float fitToMeters, float fitLongestMeters)
    {
      try
      {
        if (link == null || !link.Exists()) return null;
        var prefab = link.Load(false, true);
        if (prefab == null) return null;
        var go = UnityEngine.Object.Instantiate(prefab, point, rotation);
        // 先量原始尺寸、再缩放——顺序不能反，否则日志记到的是缩放后的值，没法反推 Scale。
        LogOriginalSizeOnce(link, go);
        if (fitLongestMeters > 0f)
        {
          float extent = MeasureLongestExtent(go);
          if (extent > 0.0001f) go.transform.localScale *= fitLongestMeters / extent;
        }
        else if (fitToMeters > 0f)
        {
          float extent = MeasurePlanExtent(go);
          if (extent > 0.0001f) go.transform.localScale *= fitToMeters / extent;
        }
        else if (scale > 0f)
        {
          go.transform.localScale = Vector3.one * scale;
        }
        Apply(go, rgb, lift);
        if (lifetime > 0f) UnityEngine.Object.Destroy(go, lifetime);
        return go;
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 重染特效生成失败：\n" + e);
        return null;
      }
    }

    /// <summary>
    /// **原样**实例化借来的 prefab 到世界坐标 <paramref name="point"/>：不重染、保留灯、只放大**粒子本身**、自动销毁。
    /// 用于"素材本身就对、只是要摆位置 / 放大"的场合（墨染山河的召唤闪光）。
    ///
    /// ⚠ **不要用根节点缩放**（<see cref="Spawn"/> 的 <c>scale</c> 那条路）：Unity 默认 <c>Local</c> 缩放模式下，
    /// 根缩放会按比例**抬高** FX 里高于根节点的部件、却**不放大粒子本身**——结果是"特效没变大、位置反而升到半空"。
    /// 墨染山河（素材 30142fb7，65 个粒子系统、包围盒 351×44×345 米）2026-10-10 实测踩到。
    /// 放大改走 <see cref="ScaleParticleSizes"/>（只动 <c>startSize</c> / 发射形状半径），位置不动。
    ///
    /// <paramref name="rootScale"/>（2026-10-11 新增）：**网格型**爆发（粒子缩放动不了的那种，如闪光弹借的阳炎爆）
    /// 用根缩放才缩得动，所以这里两者一起给——<c>rootScale</c> 缩网格、<c>particleSizeScale</c> 缩粒子。
    /// </summary>
    internal static GameObject SpawnPlaced(PrefabLink link, Vector3 point, Quaternion rotation,
      float particleSizeScale, float lifetime, float rootScale = 1f)
    {
      try
      {
        if (link == null || !link.Exists()) return null;
        var prefab = link.Load(false, true);
        if (prefab == null) return null;
        var go = UnityEngine.Object.Instantiate(prefab, point, rotation);
        // ⚠ 关键（2026-10-11 修闪光弹）：Unity 粒子默认 `Local` 缩放模式，**根缩放对粒子无效**
        //（也不吃 `curveMultiplier` 之外的常量尺寸），所以只给根缩放/粒子缩放都"看着没变"。
        // 根缩放 != 1 时先把粒子的 scalingMode 改成 `Hierarchy`，根缩放才会同时缩粒子与网格。
        if (rootScale > 0f && !Mathf.Approximately(rootScale, 1f))
        {
          foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
          {
            try
            {
              var m = ps.main;
              if (m.scalingMode != ParticleSystemScalingMode.Hierarchy)
                m.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            catch { }
          }
        }
        go.transform.localScale = Vector3.one * (rootScale <= 0f ? 1f : rootScale);
        LogOriginalSizeOnce(link, go);
        ScaleParticleSizes(go, particleSizeScale);
        // 素材可能 playOnAwake=false（由游戏 FX 管理器负责播放）——单独实例化时必须自己 Play()。
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
          try { if (!ps.isPlaying) ps.Play(true); } catch { }
        if (lifetime > 0f) UnityEngine.Object.Destroy(go, lifetime);
        return go;
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 摆位特效生成失败：\n" + e);
        return null;
      }
    }

    /// <summary>
    /// 只放大**粒子本身**：<c>startSize</c>（用 <c>curveMultiplier</c> 覆盖曲线/常量各模式）+ 发射形状半径。
    /// **不碰根节点缩放**，所以特效"原地变大"、不会被抬高。factor=1 时什么都不做。
    /// </summary>
    internal static void ScaleParticleSizes(GameObject go, float factor)
    {
      if (go == null || Mathf.Approximately(factor, 1f)) return;
      int n = 0;
      foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
      {
        try
        {
          if (ps == null) continue;
          var main = ps.main;
          var sz = main.startSize;
          sz.curveMultiplier *= factor;
          main.startSize = sz;
          var shape = ps.shape;
          if (shape.enabled) shape.radius *= factor;
          n++;
        }
        catch { }
      }
      Dbg.Log($"[RhodesDoctor] 粒子尺寸×{factor:F2}（{n} 个粒子系统，未动根缩放）。");
    }

    /// <summary>水平面（x/z）最大跨度，米。给"铺在地上"的特效当宽度用。</summary>
    private static float MeasurePlanExtent(GameObject go)
    {
      var size = MeasureBounds(go).size;
      return Mathf.Max(size.x, size.z);
    }

    /// <summary>三轴里最长的一条，米。给"竖着的单个物件"（剑等）当长度用。</summary>
    private static float MeasureLongestExtent(GameObject go)
    {
      var s = MeasureBounds(go).size;
      return Mathf.Max(s.x, Mathf.Max(s.y, s.z));
    }

    /// <summary>合并全部渲染器的世界包围盒；无渲染器时为零尺寸。</summary>
    internal static Bounds MeasureBounds(GameObject go)
    {
      bool has = false;
      var bounds = new Bounds(go.transform.position, Vector3.zero);
      foreach (var r in go.GetComponentsInChildren<Renderer>(true))
      {
        if (r == null) continue;
        if (!has) { bounds = r.bounds; has = true; }
        else bounds.Encapsulate(r.bounds);
      }
      return bounds;
    }

    /// <summary>
    /// 实例化一个借来的 prefab、重染成 rgb 色，并**挂到 parent 下**（localPosition 归零）。
    /// 用于"跟着单位走"的常驻特效（如骑兵突进的加速术拖尾）：挂到单位 View 下，
    /// 单位移动时特效跟着走，增益移除时由调用方销毁。
    /// 贴图也**染成 rgb**（保留亮度），确保藏在贴图里的着色（如加速术底部的拖尾）一并变蓝。
    /// link 为空 / 加载失败返回 null（调用方无需处理）。
    /// </summary>
    internal static GameObject SpawnAttached(PrefabLink link, Transform parent, Color rgb, float lift,
      bool keepLights = false, float alphaScale = 1f)
    {
      try
      {
        if (link == null || !link.Exists() || parent == null) return null;
        var prefab = link.Load(false, true);
        if (prefab == null) return null;
        var go = UnityEngine.Object.Instantiate(prefab, parent);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        DumpColorsOnce(link, go);
        // alphaScale：整份特效的"透明度倍率"（1 = 原样）——用于"平均地减弱"（降低不透明度）。
        AlphaScale = alphaScale <= 0f ? 1f : alphaScale;
        try { Apply(go, rgb, lift, tintTextures: true, keepLights: keepLights); }
        finally { AlphaScale = 1f; }
        // 修正挂载特效中的拖尾/粒子世界空间模拟，防止人物转向时拖尾僵硬旋转
        FixAttachedTrails(go, link);
        // 素材里的粒子系统可能 playOnAwake=false（由游戏 FX 管理器负责播放）——
        // 单独实例化时必须自己 Play()，否则"有粒子系统但不发射"。
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
          try { if (!ps.isPlaying) ps.Play(true); } catch { }
        DumpPostOnce(link, go);
        return go;
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 挂载重染特效生成失败：\n" + e);
        return null;
      }
    }

    /// <summary>
    /// 原样实例化并挂到 parent 下（**不重染**），返回克隆体。
    /// 用于"素材本身就对、只是要让它跟着单位"的场合（如光亮术的常驻光球）。
    /// </summary>
    internal static GameObject SpawnAttachedRaw(PrefabLink link, Transform parent, Vector3 localPos)
    {
      try
      {
        if (link == null || !link.Exists() || parent == null) return null;
        var prefab = link.Load(false, true);
        if (prefab == null) return null;
        var go = UnityEngine.Object.Instantiate(prefab, parent);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        // 修正挂载特效中的拖尾/粒子世界空间模拟，防止人物转向时拖尾僵硬旋转
        FixAttachedTrails(go, link);
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
          try { if (!ps.isPlaying) ps.Play(true); } catch { }
        return go;
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 原样挂载特效失败：\n" + e);
        return null;
      }
    }

    private static readonly string[] TrailKeywords =
    {
      "trail", "ribbon", "streak", "spark", "smoke", "haze", "mist",
      "dust", "fog", "steam", "foot"
    };

    private static bool IsTrailName(string name)
    {
      if (string.IsNullOrEmpty(name)) return false;
      for (int i = 0; i < TrailKeywords.Length; i++)
      {
        if (name.IndexOf(TrailKeywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
          return true;
      }
      return false;
    }

    private static bool IsTrailPrefab(string assetId)
    {
      if (string.IsNullOrEmpty(assetId)) return false;
      var clean = assetId.Replace("-", "").ToLowerInvariant();
      // 91ef30ab58fa0d3449d4d2ccc20cb0f8 = HasteTrail (haste00_trail)
      // 5d6fcb6c26ba59440afc491496f101cf = PlasmaAura (BloodHazeBuff)
      // 4a09849206b02f04e8d0e704e0e56f4e = BloodragerStandartRageBuff
      // d6a8c6aead8ec1e4b9362cff1c2373a2 = SlowAura (slow00_cycle)
      return clean == "91ef30ab58fa0d3449d4d2ccc20cb0f8"
          || clean == "5d6fcb6c26ba59440afc491496f101cf"
          || clean == "4a09849206b02f04e8d0e704e0e56f4e"
          || clean == "d6a8c6aead8ec1e4b9362cff1c2373a2"
          || (FxLibrary.HasteTrail != null && clean == FxLibrary.HasteTrail.AssetId?.Replace("-", "").ToLowerInvariant())
          || (FxLibrary.PlasmaAura != null && clean == FxLibrary.PlasmaAura.AssetId?.Replace("-", "").ToLowerInvariant())
          || (FxLibrary.SlowAura != null && clean == FxLibrary.SlowAura.AssetId?.Replace("-", "").ToLowerInvariant());
    }

    /// <summary>
    /// 修复挂载特效中所有拖尾/粒子随人物转向旋转的物理问题：
    /// 1. 拖尾模块（ParticleSystem.trails）：必须开启 worldSpace = true，
    ///    且 attachRibbonsToTransform = false，确保丝带轨迹记录在世界坐标；
    /// 2. 模拟空间（ParticleSystem.main.simulationSpace）：
    ///    - 若为已知拖尾/雾气素材（加速拖尾 HasteTrail、血雾 PlasmaAura、缓慢 SlowAura 等）；
    ///    - 或粒子系统开启了 trails 模块；
    ///    - 或粒子系统/GameObject 名字含 trail / ribbon / streak / spark / haze / mist / smoke / dust / fog / steam / foot；
    ///    强制切换为 ParticleSystemSimulationSpace.World。
    ///    这样单位无论是奔跑拐弯还是原地转向，已经发射在空中的粒子与拖尾都稳固留在世界坐标中，
    ///    不会像硬木棍一样僵硬旋转。
    /// 3. SnapToLocator 原生约束：原版如果标记了 DontRotate，挂载下补齐 WorldRotationLock，
    ///    记录并锁定原始俯仰朝向，确保不被单位转向强行带偏。
    /// </summary>
    internal static void FixAttachedTrails(GameObject go, PrefabLink link = null)
    {
      if (go == null) return;
      try
      {
        string assetId = link?.AssetId ?? "";
        bool isKnownTrailPrefab = IsTrailPrefab(assetId);

        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
          try
          {
            if (ps == null) continue;
            var main = ps.main;
            var trails = ps.trails;

            bool isTrailSystem = isKnownTrailPrefab
              || trails.enabled
              || IsTrailName(ps.name)
              || (ps.transform.parent != null && IsTrailName(ps.transform.parent.name));

            if (trails.enabled)
            {
              trails.worldSpace = true;
              trails.attachRibbonsToTransform = false;
            }

            if (isTrailSystem)
            {
              main.simulationSpace = ParticleSystemSimulationSpace.World;
            }
          }
          catch { }
        }

        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
        {
          try
          {
            if (mb == null) continue;
            var t = mb.GetType();
            if (t.Name == "SnapToLocator")
            {
              var field = t.GetField("DontRotate");
              if (field != null && (bool)field.GetValue(mb))
              {
                if (mb.gameObject.GetComponent<WorldRotationLock>() == null)
                  mb.gameObject.AddComponent<WorldRotationLock>().Capture(go.transform);
              }
            }
          }
          catch { }
        }
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Warning($"[RhodesDoctor] 挂载拖尾空间修正失败: {e.Message}");
      }
    }

    /// <summary>
    /// 把克隆体里所有着色来源改成 rgb：
    /// · 粒子 startColor / colorOverLifetime / colorBySpeed / trails 只换 RGB、保留 Alpha（覆盖全部渐变模式）；
    /// · TrailRenderer / LineRenderer 的起止色与渐变只换 RGB、保留 Alpha；
    /// · 每个材质的全部颜色属性换 RGB、保留原 Alpha；类颜色名的 Vector 属性同步换 RGB；
    /// · 每个材质的全部贴图属性：<paramref name="tintTextures"/> 为 true 时**染成 rgb**（保留亮度），
    ///   否则去色成灰白（SmokeFx 那套）——都不改原贴图；
    /// · 自带脚本里名字含 color/tint/glow/emission 的 Color/Gradient 字段一并换 RGB；
    /// · 删自带灯光与音源（避免残留原色的光）。
    /// </summary>
    /// <param name="textureLift">
    /// 贴图染色的"提亮"（只在 <paramref name="tintTextures"/> 为 true 时有意义）：
    /// 染贴图时用的是 <c>new = rgb × lerp(原像素亮度, 1, textureLift)</c>，
    /// 所以**素材越暗、这一项越关键**——默认 0.40 会保留原素材的暗部
    /// （例：黑曜石那种近黑的地面，染完主体仍是黑的，只有发光裂缝变白）。
    /// 想把**暗素材整体提成浅色**（如"钙质化的苍白石壳"）就调高，0.85~0.9 基本能抹平原亮度。
    /// </param>
    /// <param name="tintAllTextures">
    /// true = 连"疑似数据贴图"（名字含 mask/flow/distort/erosion 的）一起染，只保留法线/凹凸不染。
    /// 需要"整体换色"时打开——原版有些 FX 的底色就藏在 `Flow` 类贴图里，默认跳过会让它**染不动**。
    /// </param>
    internal static void Apply(GameObject go, Color rgb, float lift, bool tintTextures = false,
      float textureLift = 0.40f, bool tintAllTextures = false, bool keepLights = false)
    {
      foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
      {
        try
        {
          var main = ps.main;
          main.startColor = Tinted(main.startColor, rgb);
        }
        catch { }
        try
        {
          var col = ps.colorOverLifetime;
          if (col.enabled) col.color = Tinted(col.color, rgb);
        }
        catch { }
        try
        {
          var cbs = ps.colorBySpeed;
          if (cbs.enabled) cbs.color = Tinted(cbs.color, rgb);
        }
        catch { }
        try
        {
          var trails = ps.trails;
          if (trails.enabled)
          {
            try { trails.colorOverLifetime = Tinted(trails.colorOverLifetime, rgb); } catch { }
            try { trails.colorOverTrail = Tinted(trails.colorOverTrail, rgb); } catch { }
          }
        }
        catch { }
      }

      // 缎带类拖尾（加速术底部那条很可能就是它）：顶点渐变与材质相乘，
      // 只染材质不染渐变的话，绿色顶点色 × 蓝色材质 = 还是偏绿。
      foreach (var tr in go.GetComponentsInChildren<TrailRenderer>(true))
      {
        try
        {
          if (tr == null) continue;
          tr.startColor = WithRgb(tr.startColor, rgb);
          tr.endColor = WithRgb(tr.endColor, rgb);
          tr.colorGradient = TintedGradient(tr.colorGradient, rgb);
        }
        catch { }
      }
      foreach (var lr in go.GetComponentsInChildren<LineRenderer>(true))
      {
        try
        {
          if (lr == null) continue;
          lr.startColor = WithRgb(lr.startColor, rgb);
          lr.endColor = WithRgb(lr.endColor, rgb);
          lr.colorGradient = TintedGradient(lr.colorGradient, rgb);
        }
        catch { }
      }

      foreach (var psr in go.GetComponentsInChildren<ParticleSystemRenderer>(true))
      {
        try { NeutralizeMaterial(psr.material, rgb, lift, tintTextures, textureLift, tintAllTextures); } catch { }
        try { NeutralizeMaterial(psr.trailMaterial, rgb, lift, tintTextures, textureLift, tintAllTextures); } catch { }
      }
      foreach (var r in go.GetComponentsInChildren<Renderer>(true))
      {
        Material[] mats = null;
        try { mats = r.materials; } catch { continue; }
        if (mats == null) continue;
        foreach (var mat in mats)
          try { NeutralizeMaterial(mat, rgb, lift, tintTextures, textureLift, tintAllTextures); } catch { }
      }
      // 非 Renderer 的材质持有者（如 Projector 之类）：反射找 material/materials 属性，
      // 克隆后再染、写回——只改克隆体，原材质不碰。
      foreach (var comp in go.GetComponentsInChildren<Component>(true))
      {
        try
        {
          if (comp == null || comp is Renderer) continue;
          var t = comp.GetType();
          var p1 = MaterialProp(t);
          if (p1 != null && p1.CanRead && p1.CanWrite && p1.PropertyType == typeof(Material))
          {
            try
            {
              var src = p1.GetValue(comp, null) as Material;
              if (src != null)
              {
                var clone = new Material(src);
                NeutralizeMaterial(clone, rgb, lift, tintTextures, textureLift, tintAllTextures);
                p1.SetValue(comp, clone, null);
              }
            }
            catch { }
          }
        }
        catch { }
      }
      TintCustomScripts(go, rgb);
      WhitenMeshes(go);

      foreach (var l in go.GetComponentsInChildren<Light>(true))
      {
        // 默认删灯（避免残留原色的光）；keepLights=true 时**保留灯并染成目标色**。
        // ⚠ 还要给强度：很多"发光"法术的灯 **intensity 默认是 0**（由游戏脚本按 buff 点亮），
        // 克隆出来是"关着的"——只保留灯不给强度 = 一盏黑灯，照样看不见。
        if (keepLights)
        {
          l.color = new Color(rgb.r, rgb.g, rgb.b, 1f);
          if (l.intensity <= 0.01f) l.intensity = 1.5f;
        }
        else UnityEngine.Object.Destroy(l);
      }
      foreach (var a in go.GetComponentsInChildren<AudioSource>(true)) UnityEngine.Object.Destroy(a);
    }

    /// <summary>
    /// 网格顶点色漂白：管线里唯一没"新建"的东西。
    /// 对象/材质/贴图三层都是克隆再改、原版不碰；但网格（mesh）之前是共用原资产、根本没动。
    /// 若绿烘在网格顶点色里（拖尾常用手法：渐变直接烘进顶点色），前面粒子/材质/贴图全染蓝也没用——
    /// 绿顶点 × 蓝材质 = 还是绿，这正好对上"周身蓝了、底部不动"的症状。
    /// 做法：只对**带顶点色且偏离白色**的网格克隆一份、RGB 漂白（保留 Alpha 渐隐）、挂回克隆体；
    /// 无顶点色的网格原样跳过。原 mesh 资产不碰。
    /// </summary>
    private static void WhitenMeshes(GameObject go)
    {
      foreach (var psr in go.GetComponentsInChildren<ParticleSystemRenderer>(true))
      {
        try
        {
          if (psr == null) continue;
          var src = psr.mesh;
          if (src == null) continue;
          var white = WhitenedClone(src, psr.name);
          if (white != null) psr.mesh = white;
        }
        catch { }
      }
      foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
      {
        try
        {
          if (mf == null) continue;
          var src = mf.sharedMesh;
          if (src == null) continue;
          var white = WhitenedClone(src, mf.name);
          if (white != null) mf.mesh = white;
        }
        catch { }
      }
    }

    /// <summary>顶点色偏离白才克隆漂白；无顶点色/已是白色返回 null（调用方跳过）。漂白结果打一行日志作证据。</summary>
    private static Mesh WhitenedClone(Mesh src, string owner)
    {
      try
      {
        var cols = src.colors;
        if (cols == null || cols.Length == 0) return null;
        bool offWhite = false;
        foreach (var c in cols)
        {
          if (Mathf.Abs(c.r - 1f) > 0.05f || Mathf.Abs(c.g - 1f) > 0.05f || Mathf.Abs(c.b - 1f) > 0.05f)
          { offWhite = true; break; }
        }
        if (!offWhite) return null;
        var clone = UnityEngine.Object.Instantiate(src);
        var px = clone.colors;
        for (int i = 0; i < px.Length; i++) px[i] = new Color(1f, 1f, 1f, px[i].a);
        clone.colors = px;
        Dbg.Log($"[RhodesDoctor][顶点漂白] '{owner}' 网格 '{src.name}'"
          + $" 顶点色[{cols.Length}]首={cols[0]} →已漂白（原 mesh 不动）。");
        return clone;
      }
      catch { return null; }
    }

    /// <summary>
    /// 当前这次染色的"透明度倍率"（1 = 原样）。由 <see cref="Apply"/> 设置、用完复位；
    /// 所有把颜色写进去的地方都乘上它——用于"**平均地**减弱整个特效"（降低不透明度），
    /// 而不是只调某个颜色通道（那样会偏色）。单线程同步使用，非重入。
    /// </summary>
    private static float AlphaScale = 1f;

    private static Color WithRgb(Color old, Color rgb)
      => new Color(rgb.r, rgb.g, rgb.b, old.a * AlphaScale);

    /// <summary>
    /// 自带脚本里的颜色字段：Owlcat 有些 prefab 用脚本每帧驱动颜色，一次性改材质会被它下帧盖回去。
    /// 这里把名字含 color/tint/glow/emission 的 Color/Gradient 公开字段·属性直接换成目标色（保留 Alpha），
    /// 改的是克隆体上的脚本实例，原 prefab 不碰。
    /// </summary>
    private static void TintCustomScripts(GameObject go, Color rgb)
    {
      foreach (var comp in go.GetComponentsInChildren<Component>(true))
      {
        try
        {
          if (comp == null || comp is Transform || comp is Renderer
            || comp is ParticleSystem || comp is Light || comp is AudioSource) continue;
          var t = comp.GetType();
          if (t.Assembly == typeof(UnityEngine.Object).Assembly) continue; // 只动游戏/自定义脚本
          var (fields, props) = ColorMembers(t);
          foreach (var f in fields)
          {
            try
            {
              if (f.FieldType == typeof(Color))
                f.SetValue(comp, WithRgb((Color)f.GetValue(comp), rgb));
              else if (f.FieldType == typeof(Gradient))
                f.SetValue(comp, TintedGradient(f.GetValue(comp) as Gradient, rgb));
            }
            catch { }
          }
          foreach (var p in props)
          {
            try
            {
              p.SetValue(comp, WithRgb((Color)p.GetValue(comp, null), rgb), null);
            }
            catch { }
          }
        }
        catch { }
      }
    }

    private static bool LooksLikeColorName(string name)
    {
      if (string.IsNullOrEmpty(name)) return false;
      var s = name.ToLowerInvariant();
      return s.Contains("color") || s.Contains("colour") || s.Contains("tint")
        || s.Contains("glow") || s.Contains("emission");
    }

    /// <summary>
    /// 每个素材首次挂载前打印一次全部着色来源（粒子各模块、缎带渐变、材质颜色/贴图、自定义脚本），
    /// 用于定位"到底哪条链路还保持原色"。只打一次，不刷屏。
    /// </summary>
    private static readonly HashSet<string> ColorLogged = new();

    [System.Diagnostics.Conditional("RD_VERBOSE")]

    internal static void DumpColorsOnce(PrefabLink link, GameObject go,
      string label = "染色前", bool force = false)
    {
      var id = (link?.AssetId ?? "?") + "|" + label;
      if (!force && !ColorLogged.Add(id)) return;
      try
      {
        Dbg.Log($"[RhodesDoctor][染色诊断] 素材 {id} 着色来源（{label}）：");
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
          try
          {
            var main = ps.main;
            string extra = $" sim={main.simulationSpace}";
            try
            {
              var cbs = ps.colorBySpeed;
              extra += $" cbs.enabled={cbs.enabled}";
            }
            catch { }
            try
            {
              var trails = ps.trails;
              extra += $" trails.enabled={trails.enabled}";
              if (trails.enabled)
              {
                extra += $" trailWorld={trails.worldSpace} attachRibbon={trails.attachRibbonsToTransform}";
              }
              try { extra += $" inherit={trails.inheritParticleColor}"; } catch { }
              try { extra += $" trailOL({trails.colorOverLifetime.mode})={trails.colorOverLifetime.color}"; } catch { }
              try { extra += $" trailT({trails.colorOverTrail.mode})={trails.colorOverTrail.color}"; } catch { }
            }
            catch { }
            var col = ps.colorOverLifetime;
            Dbg.Log($"[RhodesDoctor][染色诊断]   粒子 '{ps.name}' "
              + $"start({main.startColor.mode})={main.startColor.color} "
              + $"colOL.enabled={col.enabled}{extra}");
          }
          catch (Exception e)
          {
            Dbg.Log($"[RhodesDoctor][染色诊断]   粒子 '{ps?.name}' 读取失败：{e.Message}");
          }
        }
        foreach (var tr in go.GetComponentsInChildren<TrailRenderer>(true))
        {
          try
          {
            Dbg.Log($"[RhodesDoctor][染色诊断]   缎带 '{tr.name}' "
              + $"start={tr.startColor} end={tr.endColor} time={tr.time:F2}");
          }
          catch (Exception e)
          {
            Dbg.Log($"[RhodesDoctor][染色诊断]   缎带 '{tr?.name}' 读取失败：{e.Message}");
          }
        }
        foreach (var lr in go.GetComponentsInChildren<LineRenderer>(true))
        {
          try
          {
            Dbg.Log($"[RhodesDoctor][染色诊断]   线条 '{lr.name}' "
              + $"start={lr.startColor} end={lr.endColor}");
          }
          catch (Exception e)
          {
            Dbg.Log($"[RhodesDoctor][染色诊断]   线条 '{lr?.name}' 读取失败：{e.Message}");
          }
        }
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
          Material[] mats = null;
          try { mats = r.materials; } catch { continue; }
          if (mats == null) continue;
          foreach (var mat in mats)
          {
            if (mat == null) continue;
            var sh = mat.shader;
            var sb = new System.Text.StringBuilder();
            if (sh != null)
            {
              for (int i = 0; i < sh.GetPropertyCount(); i++)
              {
                try
                {
                  var nm = sh.GetPropertyName(i);
                  var ty = sh.GetPropertyType(i);
                  if (ty == UnityEngine.Rendering.ShaderPropertyType.Color)
                    sb.Append(nm).Append('=').Append(mat.GetColor(nm)).Append("; ");
                  else if (ty == UnityEngine.Rendering.ShaderPropertyType.Texture)
                  {
                    var t = mat.GetTexture(nm);
                    string info = t == null ? "null"
                      : t.name + ((t is Texture2D tt && !tt.isReadable) ? "(不可读)" : "(可读)");
                    sb.Append(nm).Append('=').Append(info).Append("; ");
                  }
                }
                catch { }
              }
            }
            Dbg.Log($"[RhodesDoctor][染色诊断]   材质 shader={sh?.name} {sb}");
          }
        }
      }
      catch (Exception e)
      {
        Dbg.Log("[RhodesDoctor][染色诊断] 失败：\n" + e);
      }
    }

    private static void NeutralizeMaterial(Material mat, Color rgb, float lift, bool tintTextures,
      float textureLift = 0.40f, bool tintAllTextures = false)
    {
      if (mat == null) return;
      var sh = mat.shader;
      if (sh == null) return;
      for (int i = 0; i < sh.GetPropertyCount(); i++)
      {
        var nm = sh.GetPropertyName(i);
        var ty = sh.GetPropertyType(i);
        if (ty == UnityEngine.Rendering.ShaderPropertyType.Color)
        {
          var old = mat.GetColor(nm);
          mat.SetColor(nm, new Color(rgb.r, rgb.g, rgb.b, old.a * AlphaScale));
        }
        else if (ty == UnityEngine.Rendering.ShaderPropertyType.Vector)
        {
          if (!LooksLikeColorVector(nm)) continue;
          var v = mat.GetVector(nm);
          mat.SetVector(nm, new Vector4(rgb.r, rgb.g, rgb.b, v.w * AlphaScale));
        }
        else if (ty == UnityEngine.Rendering.ShaderPropertyType.Texture)
        {
          if (LooksLikeNormalMap(nm)) continue;                          // 法线/凹凸：永不染
          if (!tintAllTextures && LooksLikeDataMap(nm)) continue;        // 其余"数据图"按需跳过
          var t = mat.GetTexture(nm);
          if (t == null) continue;
          var g = tintTextures ? TintedTexture(t, rgb, textureLift) : Desaturated(t, lift);
          if (g != null) mat.SetTexture(nm, g);
        }
      }
    }

    /// <summary>名字含 color/tint/emission/glow 的 Vector 属性很可能是颜色，按 RGB 处理（保留 W）。</summary>
    private static bool LooksLikeColorVector(string propName)
    {
      var s = propName.ToLowerInvariant();
      return s.Contains("color") || s.Contains("colour") || s.Contains("tint")
        || s.Contains("emission") || s.Contains("glow");
    }

    /// <summary>
    /// 每个素材首次生成时打印一次"缩放前"的水平/垂直尺寸与各渲染器明细，便于标定 Scale 或 FitToMeters。
    /// 必须是缩放前的值，否则日志里是缩放后的结果，反推不出素材本身多大。
    /// 明细的意义：粒子系统的 bounds 常被吹得远大于可见粒子，网格才是真实几何；
    /// 只有看清是谁撑着包围盒，才知道自适应缩放会不会把特效缩没。
    /// </summary>
    [System.Diagnostics.Conditional("RD_VERBOSE")]
    private static void LogOriginalSizeOnce(PrefabLink link, GameObject go)
    {
      var id = link?.AssetId ?? "?";
      if (!SizeLogged.Add(id)) return;
      try
      {
        var m = MeasureBounds(go).size;
        const float ft = 3.28084f;
        Dbg.Log($"[RhodesDoctor][特效尺寸] 素材 {id} 原始包围盒={m.x:F2}x{m.y:F2}x{m.z:F2} 米"
          + $"（≈{m.x * ft:F1}x{m.y * ft:F1}x{m.z * ft:F1} 尺）");
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
          if (r == null) continue;
          var s = r.bounds.size;
          string kind = r is ParticleSystemRenderer ? "粒子"
            : r is MeshRenderer ? "网格"
            : r.GetType().Name;
          string extra = "";
          var ps = r.GetComponent<ParticleSystem>();
          if (ps != null)
          {
            var main = ps.main;
            extra = $" startSize={main.startSize.constantMin:F2}~{main.startSize.constantMax:F2}"
              + $" 存活={main.startLifetime.constantMin:F1}~{main.startLifetime.constantMax:F1}s";
          }
          Dbg.Log($"[RhodesDoctor][特效尺寸]   - {kind} '{r.name}' ="
            + $"{s.x:F2}x{s.y:F2}x{s.z:F2} 米{extra}");
        }
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor][特效尺寸] 计算失败：\n" + e);
      }
    }

    /// <summary>渐变模式只打首个色键（MinMaxGradient.color 在渐变模式下是无意义的垃圾值）。</summary>
    private static string FmtGrad(ParticleSystem.MinMaxGradient g)
    {
      try
      {
        switch (g.mode)
        {
          case ParticleSystemGradientMode.Color:
            return "Color=" + g.color;
          case ParticleSystemGradientMode.TwoColors:
            return "Two=" + g.colorMin + "/" + g.colorMax;
          case ParticleSystemGradientMode.Gradient:
          {
            var gr = g.gradient;
            var keys = gr?.colorKeys;
            return keys != null && keys.Length > 0 ? $"Grad[{keys.Length}]首键={keys[0].color}@{keys[0].time:F2}" : "Grad[空]";
          }
          case ParticleSystemGradientMode.TwoGradients:
          {
            var a = g.gradientMin?.colorKeys;
            var b = g.gradientMax?.colorKeys;
            string sa = a != null && a.Length > 0 ? a[0].color.ToString() : "空";
            string sb = b != null && b.Length > 0 ? b[0].color.ToString() : "空";
            return $"TwoGrad首键={sa}/{sb}";
          }
          default:
            return g.mode + "=" + g.color;
        }
      }
      catch (Exception e) { return "读取失败:" + e.Message; }
    }

    /// <summary>
    /// 染色后复核（只打一次）：确认拖尾双通道与主色是不是真的写成了目标色。
    /// 若这里已是蓝、画面仍是绿，则绿条不是这个 prefab 的——另有来源（如叠加的原版加速术）。
    /// </summary>
    private static readonly HashSet<string> PostLogged = new();

    [System.Diagnostics.Conditional("RD_VERBOSE")]

    private static void DumpPostOnce(PrefabLink link, GameObject go)
    {
      var id = link?.AssetId ?? "?";
      if (!PostLogged.Add(id)) return;
      try
      {
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
          var extra = "";
          try
          {
            var trails = ps.trails;
            if (trails.enabled)
              extra = $" inherit={trails.inheritParticleColor}"
                + $" trailOL={FmtGrad(trails.colorOverLifetime)}"
                + $" trailT={FmtGrad(trails.colorOverTrail)}";
          }
          catch { }
          string emis = "";
          try
          {
            var em = ps.emission;
            emis = $" emis.enabled={em.enabled} bursts={em.burstCount}";
          }
          catch { }
          Dbg.Log($"[RhodesDoctor][染色复核] 粒子 '{ps.name}' start={FmtGrad(ps.main.startColor)}{extra}{emis}");
        }
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
          try
          {
            var sb = new System.Text.StringBuilder();
            Material[] mats = null;
            try { mats = r.materials; } catch { }
            if (mats != null) foreach (var mat in mats)
            {
              if (mat == null) { sb.Append("[null] "); continue; }
              string col = "";
              try { col = mat.HasProperty("_Color") ? mat.GetColor("_Color").ToString() : "无_Color"; } catch { }
              sb.Append($"[{mat.name} {mat.shader?.name} _Color={col}] ");
            }
            Dbg.Log($"[RhodesDoctor][染色复核] 渲染器 {r.GetType().Name} '{r.name}'"
              + $" enabled={r.enabled} active={r.gameObject.activeInHierarchy} 材质={sb}");
          }
          catch { }
        }
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor][染色复核] 失败：\n" + e);
      }
    }

    /// <summary>法线/凹凸：**任何情况下都不染**——那是几何数据，染了会把光照搞平。</summary>
    private static bool LooksLikeNormalMap(string propName)
    {
      var s = propName.ToLowerInvariant();
      return s.Contains("normal") || s.Contains("bump");
    }

    /// <summary>
    /// 遮罩 / 流动 / 扭曲 / 侵蚀：默认跳过（它们是"数据"不是"颜色"，去色会破坏其通道语义）。
    ///
    /// ⚠️ **但这条默认值踩过坑**：原版"黑曜流"那类 FX 的**地面残留层**，
    /// 底色就藏在一个名字带 `Flow` 的贴图里——跳过它就等于"残留层永远染不动"，
    /// 表现为**初始爆发变浅了、地上那层还是黑的**（钙质化 2026-10-09 的实测症状）。
    /// 所以给 <see cref="Apply"/> 加了 `tintAllTextures`：需要"整体换色"时打开它，
    /// 只保留法线/凹凸不染。
    /// </summary>
    private static bool LooksLikeDataMap(string propName)
    {
      var s = propName.ToLowerInvariant();
      return s.Contains("mask") || s.Contains("flow")
        || s.Contains("distort") || s.Contains("erosion");
    }

    /// <summary>
    /// 把克隆体上**所有材质的颜色属性**的 alpha 统一设成 <paramref name="alpha"/>（RGB 不动）。
    /// 用于"区域结束时让特效渐隐"——直接删克隆体是"啪"地消失，太突兀。
    /// 覆盖 Renderer 材质 + 非 Renderer 的材质持有者（Projector 等）。
    /// </summary>
    internal static void SetAlpha(GameObject go, float alpha)
    {
      if (go == null) return;
      try
      {
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
          Material[] mats = null;
          try { mats = r.materials; } catch { continue; }
          if (mats == null) continue;
          foreach (var m in mats) ApplyAlpha(m, alpha);
        }
        foreach (var comp in go.GetComponentsInChildren<Component>(true))
        {
          try
          {
            if (comp == null || comp is Renderer) continue;
            var t = comp.GetType();
            var p1 = MaterialProp(t);
            if (p1 == null || !p1.CanRead || !p1.CanWrite || p1.PropertyType != typeof(Material)) continue;
            var m = p1.GetValue(comp, null) as Material;
            if (m == null) continue;
            if (ApplyAlpha(m, alpha)) p1.SetValue(comp, m, null);
          }
          catch { }
        }
      }
      catch { }
    }

    private static bool ApplyAlpha(Material mat, float alpha)
    {
      try
      {
        if (mat == null || mat.shader == null) return false;
        var sh = mat.shader;
        for (int i = 0; i < sh.GetPropertyCount(); i++)
        {
          if (sh.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Color) continue;
          var nm = sh.GetPropertyName(i);
          var c = mat.GetColor(nm);
          c.a = alpha;
          mat.SetColor(nm, c);
        }
        return true;
      }
      catch { return false; }
    }

    // ── "暗色贴花"专用：只压 Alpha，不动 RGB ──────────────────────────────────────
    //
    // 背景（钙质化 2026-10-09 实测）：原版有些"地面残留层"是**投影贴花 / 泥潭**，
    // 它的黑**不在 RGB 里**——把材质色和贴图 RGB 全染成目标色之后，屏幕上那一层**一点没变**。
    // 原因：这类 shader 用**贴图的 Alpha**当"压暗量"（`ground × (1-a)` 之类），
    // 而 <see cref="TintedTexture"/> 只换 RGB、**保留 Alpha**，所以怎么染 RGB 都白搭。
    // ⇒ 想让它变淡，只能**压 Alpha**。下面这两个方法就是干这个的。

    private static readonly Dictionary<(Texture tex, float scale), Texture2D> AlphaCache = new();

    /// <summary>复制一张贴图，**RGB 原样保留、只把 Alpha 乘上 scale**（原贴图不动）。</summary>
    private static Texture2D ScaledAlpha(Texture src, float scale)
    {
      var key = (src, scale);
      if (AlphaCache.TryGetValue(key, out var cached)) return cached;
      Texture2D outTex = null;
      try
      {
        int w = src.width, h = src.height;
        Color[] px = ReadPixels(src, w, h);
        if (px != null)
        {
          for (int i = 0; i < px.Length; i++)
          {
            var c = px[i];
            px[i] = new Color(c.r, c.g, c.b, Mathf.Clamp01(c.a * scale));
          }
          outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
          outTex.SetPixels(px);
          outTex.Apply(false);
          outTex.name = (src.name ?? "tex") + "_rdalpha";
        }
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error($"[RhodesDoctor] 贴图 Alpha 压缩失败（{src?.name}）：\n{e}");
      }
      AlphaCache[key] = outTex;
      return outTex;
    }

    /// <summary>
    /// 把**所有 shader 名匹配 <paramref name="shaderContains"/> 的材质**的贴图 Alpha 压到
    /// <paramref name="alphaScale"/> 倍（只动 Alpha，RGB 不动）。返回命中的材质数（用于日志判断有没有找到目标）。
    /// 覆盖 Renderer 的材质，以及非 Renderer 的材质持有者（如 Projector）。
    /// </summary>
    /// <param name="propNameContains">
    /// 只压**属性名含该关键字**的贴图（如 `"basemap"`）；留空 = 压该材质的全部贴图。
    /// ⚠ 为什么需要它：把该材质的**全部**贴图都压 Alpha 会连"花纹本身"一起压没
    /// （钙质化实测：压到 0.22 时整片残留直接消失）。这类贴花通常
    /// **主贴图（`_BaseMap`）= 要淡掉的"暗块"，噪声贴图（`_Noise*`）= 花纹**，
    /// 所以只压主贴图、保留噪声，就能"花纹还在、暗块变淡"。
    /// </param>
    internal static int CutAlphaByShader(GameObject go, string shaderContains, float alphaScale,
      string propNameContains = null)
    {
      if (go == null || string.IsNullOrEmpty(shaderContains)) return 0;
      int hit = 0;
      foreach (var r in go.GetComponentsInChildren<Renderer>(true))
      {
        Material[] mats = null;
        try { mats = r.materials; } catch { continue; }
        if (mats == null) continue;
        foreach (var m in mats)
          if (CutOne(m, shaderContains, alphaScale, propNameContains)) hit++;
      }
      foreach (var comp in go.GetComponentsInChildren<Component>(true))
      {
        try
        {
          if (comp == null || comp is Renderer) continue;
          var t = comp.GetType();
          var p1 = MaterialProp(t);
          if (p1 == null || !p1.CanRead || !p1.CanWrite || p1.PropertyType != typeof(Material)) continue;
          var m = p1.GetValue(comp, null) as Material;
          if (m == null) continue;
          if (CutOne(m, shaderContains, alphaScale, propNameContains))
          {
            hit++;
            p1.SetValue(comp, m, null); // Projector 之类：写回实例
          }
        }
        catch { }
      }
      Dbg.Log($"[RhodesDoctor] 压 Alpha：shader 含 '{shaderContains}' 的材质命中 {hit} 个"
        + $"，Alpha×{alphaScale:F2}。");
      return hit;
    }

    private static bool CutOne(Material mat, string shaderContains, float alphaScale,
      string propNameContains)
    {
      try
      {
        if (mat == null || mat.shader == null) return false;
        var sn = mat.shader.name ?? "";
        if (sn.ToLowerInvariant().IndexOf(shaderContains.ToLowerInvariant(), StringComparison.Ordinal) < 0)
          return false;
        var sh = mat.shader;
        for (int i = 0; i < sh.GetPropertyCount(); i++)
        {
          if (sh.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
          var nm = sh.GetPropertyName(i);
          if (LooksLikeNormalMap(nm)) continue;
          if (!string.IsNullOrEmpty(propNameContains)
            && nm.ToLowerInvariant().IndexOf(propNameContains.ToLowerInvariant(), StringComparison.Ordinal) < 0)
            continue;
          var t = mat.GetTexture(nm);
          if (t == null) continue;
          var g = ScaledAlpha(t, alphaScale);
          if (g != null) mat.SetTexture(nm, g);
        }
        return true;
      }
      catch { return false; }
    }

    /// <summary>读一张贴图的像素（可读直接读，否则经 RenderTexture 回读）。失败返回 null。</summary>
    private static Color[] ReadPixels(Texture src, int w, int h)
    {
      if (src is Texture2D readable && readable.isReadable) return readable.GetPixels();
      var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
      var prev = RenderTexture.active;
      Texture2D tmp = null;
      // P1：try/finally 确保异常时也复位 active 与释放临时 RT，避免 RenderTexture 泄漏。
      try
      {
        Graphics.Blit(src, rt);
        RenderTexture.active = rt;
        tmp = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tmp.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tmp.Apply(false);
        return tmp.GetPixels();
      }
      finally
      {
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        if (tmp != null) UnityEngine.Object.Destroy(tmp);
      }
    }

    /// <summary>复制一张贴图并去色成灰白（原贴图不动）。优先 GetPixels，否则经 RenderTexture 回读。</summary>
    private static Texture2D Desaturated(Texture src, float lift)
    {
      var key = (src, (object)lift);
      if (TexCache.TryGetValue(key, out var cached)) return cached;
      Texture2D outTex = null;
      try
      {
        int w = src.width, h = src.height;
        Color[] px;
        if (src is Texture2D readable && readable.isReadable)
        {
          px = readable.GetPixels();
        }
        else
        {
          // P1：经 ReadPixels 走 RenderTexture 回读（内含 try/finally，确保 active 复位 + 临时 RT 释放）。
          px = ReadPixels(src, w, h);
        }
        for (int i = 0; i < px.Length; i++)
        {
          var p = px[i];
          float lum = 0.299f * p.r + 0.587f * p.g + 0.114f * p.b;
          float v = Mathf.Lerp(lum, 1f, lift);
          px[i] = new Color(v, v, v, p.a);
        }
        outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        outTex.SetPixels(px);
        outTex.Apply(false);
        outTex.name = (src.name ?? "tex") + "_rdtint";
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error($"[RhodesDoctor] 特效贴图去色失败（{src?.name}）：\n{e}");
      }
      TexCache[key] = outTex;
      return outTex;
    }

    /// <summary>
    /// 复制一张贴图并**染成 rgb**（保留亮度起伏、但有保底亮度，原贴图不动）。
    /// 每个像素：先 <c>v = Lerp(lum, 1, 0.4)</c> 再 <c>new = rgb * v</c>，保留 Alpha。
    /// 保底亮度的原因：直接 <c>rgb * lum</c> 会把原贴图的暗部压成接近全黑
    /// （加速术拖尾实测：染完像一条黑烟），提底后暗部仍显目标色、纹理明暗还在。
    /// 用于"藏在贴图里的着色"也要跟着变色的场合（如加速术底部的拖尾）。
    /// 优先 GetPixels，否则经 RenderTexture 回读；失败返回 null（调用方保持原贴图）。
    /// </summary>
    private static Texture2D TintedTexture(Texture src, Color rgb, float lift)
    {
      var key = (src, (object)(rgb.r, rgb.g, rgb.b, lift));
      if (TexCache.TryGetValue(key, out var cached)) return cached;
      Texture2D outTex = null;
      try
      {
        int w = src.width, h = src.height;
        Color[] px;
        if (src is Texture2D readable && readable.isReadable)
        {
          px = readable.GetPixels();
        }
        else
        {
          // P1：经 ReadPixels 走 RenderTexture 回读（内含 try/finally，确保 active 复位 + 临时 RT 释放）。
          px = ReadPixels(src, w, h);
        }
        for (int i = 0; i < px.Length; i++)
        {
          var p = px[i];
          float lum = 0.299f * p.r + 0.587f * p.g + 0.114f * p.b;
          float v = Mathf.Lerp(lum, 1f, lift);
          px[i] = new Color(rgb.r * v, rgb.g * v, rgb.b * v, p.a);
        }
        outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        outTex.SetPixels(px);
        outTex.Apply(false);
        outTex.name = (src.name ?? "tex") + "_rdtint";
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error($"[RhodesDoctor] 特效贴图染色失败（{src?.name}）：\n{e}");
      }
      TexCache[key] = outTex;
      return outTex;
    }

    private static ParticleSystem.MinMaxGradient Tinted(ParticleSystem.MinMaxGradient g, Color rgb)
    {
      switch (g.mode)
      {
        case ParticleSystemGradientMode.Color:
        {
          var c = g.color; c.r = rgb.r; c.g = rgb.g; c.b = rgb.b; c.a *= AlphaScale;
          return new ParticleSystem.MinMaxGradient(c);
        }
        case ParticleSystemGradientMode.TwoColors:
        {
          var a = g.colorMin; a.r = rgb.r; a.g = rgb.g; a.b = rgb.b; a.a *= AlphaScale;
          var b = g.colorMax; b.r = rgb.r; b.g = rgb.g; b.b = rgb.b; b.a *= AlphaScale;
          return new ParticleSystem.MinMaxGradient(a, b);
        }
        case ParticleSystemGradientMode.Gradient:
          return new ParticleSystem.MinMaxGradient(TintedGradient(g.gradient, rgb));
        case ParticleSystemGradientMode.TwoGradients:
          return new ParticleSystem.MinMaxGradient(
            TintedGradient(g.gradientMin, rgb), TintedGradient(g.gradientMax, rgb));
        default:
          return new ParticleSystem.MinMaxGradient(new Color(rgb.r, rgb.g, rgb.b, rgb.a * AlphaScale));
      }
    }

    private static Gradient TintedGradient(Gradient src, Color rgb)
    {
      var dst = new Gradient();
      if (src == null) return dst;
      var colors = src.colorKeys;
      for (int i = 0; i < colors.Length; i++)
      {
        var k = colors[i];
        k.color = new Color(rgb.r, rgb.g, rgb.b, k.color.a);
        colors[i] = k;
      }
      // 渐变的不透明度由 alphaKeys 决定（colorKeys 的 a 常被忽略），所以"平均减弱"要连 alphaKeys 一起乘。
      var alphas = src.alphaKeys;
      for (int i = 0; i < alphas.Length; i++)
      {
        var k = alphas[i];
        k.alpha *= AlphaScale;
        alphas[i] = k;
      }
      dst.SetKeys(colors, alphas);
      return dst;
    }
  }

  /// <summary>
  /// 锁定世界旋转为解耦朝向（不随父节点旋转）。
  /// 记录相对 FX 根（即去掉单位朝向后）的原始朝向，用于原版 Prefab 中标记了 SnapToLocator.DontRotate 的部件，
  /// 既保留其预制体设计的俯仰/横滚角度，又防止挂到单位 View 下后被单位转身强行带转。
  /// </summary>
  internal sealed class WorldRotationLock : MonoBehaviour
  {
    private Quaternion _authored = Quaternion.identity;

    /// <summary>记录相对 FX 根（即去掉单位朝向后）的原始朝向。</summary>
    internal void Capture(Transform root)
    {
      _authored = root != null ? Quaternion.Inverse(root.rotation) * transform.rotation : transform.rotation;
    }

    private void LateUpdate()
    {
      transform.rotation = _authored;
    }
  }
}
