// C 类"持续地面区域"特效：把借来的区域 FX **克隆一份、重染成目标色**、放到区域位置，跟区域同生同灭。
//
// 做法与 SmokeFx / BeaconFx 同源（IAreaEffectHandler 订阅 + Instantiate + FxTint 重染 + 字典管生命周期），
// 区别是这里做成了**注册表**：以后再加一个 C 类地面法术只要 `Register` 一行，
// 不用再抄一份类出来（冰封领域 / 儿时的舞乐 / 天灾降临 都走这条）。
//
// 为什么必须克隆重染、不能直接 `area.Fx = link`：
//   `area.Fx` 是引擎直接播的 PrefabLink，**没法改色**（见 docs/fx-recolor.md §三）。
//   而原版**没有**天然就是"钙化的苍白石壳"那个颜色的地面素材——土系素材要么是泥（致命大地）、
//   要么是黑曜石（黑的）、要么是尖石（灰但形状是刺）。要那个观感只能自己染。
//   重染只落在克隆体上，原版素材一律不碰。
//
// 生命周期：区域生成 → 生成；区域结束/被驱散 → 销毁。天然对齐"离开区域即结束"的规则。
using System;
using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using Kingmaker.ResourceLinks;
using UnityEngine;

namespace RhodesDoctor
{
  internal static class AreaGroundFx
  {
    /// <summary>
    /// 构建标记：每次改视觉都会换一个值，并打进日志。
    /// 用途：**判断用户这次截图到底跑的是哪一版**——DLL 是启动时加载的，
    /// 没重启的话跑的还是旧版，会把结论带偏（钙质化为此白折腾了好几轮）。
    /// </summary>
    /// <summary>
    /// 法术半径的**兜底值**（米）。正常走**运行时读游戏自己的值**：
    /// `area.Blueprint.Size` 是 `Kingmaker.Utility.Feet` 类型，自带 `.Meters`
    /// （游戏常量 `Feet.FeetToMetersRatio = 0.3048`）——**不要自己写换算、更不要量像素**。
    /// 这个常量只在读不到蓝图时兜底（钙质化 = 20 尺 ≈ 6.096 m）。
    /// </summary>
    private const float AreaRadiusFallback = 20f * 0.3048f;

    /// <summary>
    /// ⚠️⚠️⚠️ **这层云的 `startSize` 是"半径"，不是"直径"** ⚠️⚠️⚠️
    ///
    /// 这是本项目最贵的一个低级失误（2026-10-09，来回折腾近十轮）：
    /// 我按"直径"给 `startSize`（`2 × 区域半径`），结果**每次都大了一倍**——
    /// 屏幕上特效铺满整片场地、远超法术作用圈，而日志里的数字看着"完全正确"，
    /// 于是一直在错误的基础上微调（0.8/0.88/1.06 来回改），越改越乱。
    /// 正确值 = 把之前的值**除以 2**（0.88 → 0.44）后立刻正常。
    ///
    /// ⇒ **凡是给粒子设尺寸，先确认它是"半径"还是"直径"**：
    ///    本 FX（`Waves`）实测按**半径**；不同 prefab 可能不同，
    ///    **拿不准就用热键 `[` `]` 现场试一次，不要靠推算。**
    ///
    /// 公式（已按"半径"语义重写，不再出现 ×2，从代码上杜绝再犯）：
    /// ```
    /// 云 startSize = 区域半径(米) × CloudSizeFactor
    /// ```
    /// `0.88` 只是"粒子边缘柔和渐隐、可见范围比设定值略小"的补偿。
    /// </summary>
    private const float CloudSizeFactor = 0.88f;

    /// <summary>云从 0 长到最大直径要几秒（用户要求：**1 秒内扩到最大**）。</summary>
    private const float CloudGrowSeconds = 1f;

    /// <summary>
    /// 云的粒子寿命（秒）。⚠️ 它决定"长大过程被摊多长"——`sizeOverLifetime` 是按**寿命百分比**走的，
    /// 之前把寿命设成 40 s，等于让云花 40 秒才长大（用户："出现太慢、扩大太缓慢"）。
    /// 现在给 1.4 s：长大占前 1 s，剩下 0.4 s 维持满尺寸后自然淡出，由持续发射补上新的。
    /// </summary>
    private const float CloudLifetime = 1.4f;

    private const string BuildTag = "calc-20261009-1920-fadeout";

    private sealed class Spec
    {
      internal PrefabLink Link;
      internal Color Tint;
      internal float TextureLift;
      internal float Scale;
      /// <summary>
      /// 是否执行"钙质化专用"的部件手术（关 hole/fountain/puddle/cycle、销毁外扩白环 groundfire、
      /// 把 wave 云铺满法术范围、压投影贴花 Alpha）。
      ///
      /// ⚠️ 那一段全是针对**黑曜流碎壳**那一份素材、进游戏实测调出来的（dev-notes §7.11.13）：
      /// 按名字关粒子、销毁"外扩白环"、压某层贴花 Alpha……**只对那一份素材成立**。
      /// 别的 C 类素材（冰封领域 / 儿时舞乐 / 天灾降临）跑它会**误删自己的网格/贴花**
      /// （尤其 `DestroyNonParticleRenderers` 会无差别销毁全部非粒子渲染器）。
      /// 所以默认 false；只有钙质化登记时显式打开，其余素材走"只染色 + 缩放"。
      /// </summary>
      internal bool CalcProfile;
    }

    /// <summary>自己的区域 GUID（去横线小写）→ 素材与染色参数。</summary>
    private static readonly Dictionary<string, Spec> Registry = new();

    /// <summary>已生成的克隆体（区域销毁时据此删除）。</summary>
    private static readonly Dictionary<AreaEffectEntityData, GameObject> Live = new();

    /// <summary>素材 prefab 缓存（只读模板，绝不改它）。</summary>
    private static readonly Dictionary<string, GameObject> Templates = new();

    /// <summary>
    /// 登记一个"C 类地面区域"的视觉。**必须在蓝图注入阶段调用**（早于任何施法）。
    /// link 为 null（借不到素材）时不登记——该区域保持"没有地面特效"，功能完全不受影响。
    /// </summary>
    /// <param name="areaGuid">自己那个区域蓝图的 GUID（<c>Guids.XXXArea</c>）。</param>
    /// <param name="link">借来的区域常驻 FX（<c>FxLibrary</c> 里的属性）。</param>
    /// <param name="tint">目标色：**这个才是屏幕上看到的颜色**（原素材的色相会被整个替换掉）。</param>
    /// <param name="textureLift">
    /// **贴图提亮**（0~1，默认 0.85）。贴图染色是 <c>rgb × lerp(原像素亮度, 1, textureLift)</c>，
    /// 所以**素材越暗、这一项越关键**：黑曜石那种近黑的地面，用默认的 0.40 染完**主体仍是黑的**
    /// （只有发光裂缝被染成白的），看着就是"黑壳 + 白线"。要"整体变浅"就调到 **0.85~0.9**（基本抹平原亮度）。
    /// 反过来，如果素材本来是亮色、想保留明暗层次，就调低。
    /// </param>
    /// <param name="scale">额外缩放。素材半径与法术半径相等时填 1（免缩放）。</param>
    internal static void Register(string areaGuid, PrefabLink link, Color tint,
      float textureLift = 0.85f, float scale = 1f, bool calcProfile = false)
    {
      try
      {
        var key = Norm(areaGuid);
        if (string.IsNullOrEmpty(key)) return;
        if (link == null)
        {
          Dbg.Log($"[RhodesDoctor] 地面特效未登记（{key}）：素材借不到，该区域将没有地面特效。");
          return;
        }
        Registry[key] = new Spec
        {
          Link = link, Tint = tint, TextureLift = textureLift, Scale = scale, CalcProfile = calcProfile,
        };
        Dbg.Log($"[RhodesDoctor] 地面特效已登记：{key} → tint={tint}"
          + $" 贴图提亮={textureLift:F2} scale={scale} 钙质化档={calcProfile}"
          + $" 色彩空间={QualitySettings.activeColorSpace}");
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 地面特效登记失败：\n" + e);
      }
    }

    internal static void OnAreaSpawned(AreaEffectEntityData area)
    {
      try
      {
        var spec = Lookup(area);
        if (spec == null) return;
        var template = Template(spec);
        if (template == null) return;
        Prune();

        var go = UnityEngine.Object.Instantiate(template, area.Position, Quaternion.identity);
        if (spec.Scale > 0f && Math.Abs(spec.Scale - 1f) > 0.0001f)
          go.transform.localScale *= spec.Scale;
        // 跟区域同层，避免被地形遮挡 / 渲染层级不对。
        int layer = area.View != null ? area.View.gameObject.layer : 0;
        if (layer != 0)
          foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.gameObject.layer = layer;
        // 首次生成时打一份"着色来源"清单（每个素材只打一次）：
        // 万一还有哪一层染不动，日志里能直接看到它的材质/贴图叫什么名字，不用再猜。
        FxTint.DumpColorsOnce(spec.Link, go);
        try
        {
          // lift（去色提亮）只对 Desaturated 那条路有意义；这里走 tintTextures=true，所以传 0。
          // tintAllTextures=true：**连"疑似数据贴图"（含 Flow/Mask 的）也染**——原版有些 FX 的
          // "地面残留层"底色就藏在 `Flow` 类贴图里，默认跳过会让它永远染不动
          // （症状：初始爆发变浅了、地上那层还是黑的）。
          FxTint.Apply(go, spec.Tint, 0f, tintTextures: true,
            textureLift: spec.TextureLift, tintAllTextures: true);
        }
        catch (Exception e)
        {
          Main.Mod.Logger.Error("[RhodesDoctor] 地面特效重染失败（保持原色上屏）：\n" + e);
        }
        // 染色后再打一次同样的清单：**对着"染色后"那几行看哪个属性没变成目标色，
        // 就能一眼定位"还有哪一层染不动"**（上次的教训：只看"染色前"看不出问题在哪）。
        FxTint.DumpColorsOnce(spec.Link, go, "染色后", force: true);

        // 关掉"外扩的光圈"：那圈细白环是 `ProjectorDecal (1)_Cycle` 那两个粒子
        // （一颗粒子从小扩到大 → 淡出 → 下一颗重来）。用户觉得这个观感不好（"这个效果不太好"），
        // 只保留中心那团灰白斑即可 → 直接停掉它们的发射并关掉渲染器。
        // （注：`MakeLoopsContinuous` 曾把这两个粒子"错开重叠"做成连续动画，效果是对的，
        //   但用户最终选择不要这圈，所以现在不调用了——方法留着备用。）
        // ⚠️ 2026-10-09：这里曾经做过两件事——"关掉外扩的波纹环"（DisableEmitters）
        // 和"把地面云改成持续发射"（MakePersistent）。**都撤了**：按名字猜部件连着错，
        // 结果是把环留成永久、把云弄没了（用户："还是不行，而且我自始至终都没看到白云"）。
        // 两个方法都还在类里（未调用），等**拿到 LogInventory 的粒子清单**、
        // 确认了"哪个是环、哪个是云"之后再精确动手。
        // ── 2026-10-09 关键更正：**那圈环不是粒子** ─────────────────────────────
        // 用户指出这个法术是"几段特效衔接"：**喷溅 → 白云扩大消失 → 最后剩这圈环**。
        // 而 `LogInventory` 列的 13 个全是**粒子系统**、里面根本没有环的候选——
        // 所以环是**非粒子**的部件（网格 / 贴花 / 投影器），`DisableEmitters` 再怎么关都碰不到它
        // （这就是"关了 GroundFireBig 环还在"的真正原因）。
        // ⇒ 环交给下面的 DisableNonParticleRenderers；粒子这边只关"会盖住云"的三个。
        // ── 2026-10-09 第 N 次：改用**销毁**而不是"关渲染器" ──────────────────────
        // 日志（构建标记已确认是新版）显示：克隆体里除了 `Waves`（云）与 `GroundFireBig`，
        // **全部渲染器都已关**，环却依旧在 ⇒ 用户判断对了：**"关渲染器"这个处理方式可能根本无效**
        // （贴花/投影类的东西未必受 renderer.enabled 管）。
        // 所以改成**直接销毁物体**：把 GroundFireBig 与那唯一的 `Decal` 网格整块删掉，只留 Waves。
        // 安全阀：若某个物体上**还挂着别的粒子**（如 Waves），就不销毁、只关渲染器，避免误杀云。
        // ⚠️ 以下整段是**钙质化（黑曜流碎壳）专用**的部件手术，只有 CalcProfile 打开时执行；
        //    别的 C 类素材走 else（只染色 + 缩放）。为什么必须门控见 Spec.CalcProfile 的注释。
        if (spec.CalcProfile)
        {
          foreach (var kw in new[] { "hole", "fountain", "puddle", "cycle" })
            DisableEmitters(go, kw);
          DestroyEmitters(go, "groundfire");
          DestroyNonParticleRenderers(go);

          // ── 云的两项要求（2026-10-09 用户提出）──────────────────────────────────
          //   ① **一直留到法术结束**：把云的粒子寿命拉长 + 保证持续发射（原来 burst 打一发就淡掉）。
          //   ② **铺满法术作用范围**：按**发射形状的半径**几何换算缩放（**不用包围盒**——
          //      docs 里记过粒子包围盒会被吹大一个数量级，不可信），把云的覆盖面撑到 20 尺半径。
          //   `areaRadiusMeters` = 法术半径；`fallbackScale` 用于发射形状没有半径参数时兜底。
          // ⚠️ 注意：下面给的是"区域**半径** × 系数"——因为本 FX 的 startSize 按半径解释（见常量注释）。
          // 半径**运行时读游戏自己的值**：`BlueprintAbilityAreaEffect.Size` 是 `Feet`，
          // 自带 `.Meters`（游戏常量 FeetToMetersRatio = 0.3048）。读不到才用兜底值。
          float areaRadius = AreaRadiusFallback;
          try
          {
            var bp = area?.Blueprint;
            if (bp != null && bp.Size.Meters > 0.01f) areaRadius = bp.Size.Meters;
            Dbg.Log($"[RhodesDoctor] 区域半径取自蓝图：Size={bp?.Size.Value} 尺"
              + $" = {bp?.Size.Meters:F3} m  格子数={bp?.RadiusInCells}"
              + $"（游戏 CellSize=3 m）→ 云直径={2f * areaRadius:F3} m");
          }
          catch (Exception e)
          {
            Main.Mod.Logger.Error("[RhodesDoctor] 读区域半径失败，用兜底值：\n" + e);
          }
          FitCloudToArea(go, "wave", areaRadius, areaRadius * CloudSizeFactor);
          // 延迟读**粒子实际数据**（位置 / 尺寸），算出真实可见半径——不靠截图猜（仅 RD_VERBOSE 开启，避免正式版劫持 [ ] 热键）。
#if RD_VERBOSE
          try { go.AddComponent<CloudExtentLog>(); } catch { }
#endif

          Dbg.Log($"[RhodesDoctor] 地面特效构建={BuildTag}（关 hole/fountain/puddle/cycle，留 wave）");

          DisableNonParticleRenderers(go);
        }
        else
        {
          // 通用 C 类素材：只染色 + 缩放，不做任何"按名字的部件手术"
          //（名字没把握前别乱动，见 dev-notes §7.11.11）。
          // EnableHierarchyScaling：让根节点缩放对粒子生效——Unity 默认 Local 模式下粒子尺寸不跟缩放走，
          // 所以 `Register` 传的 scale 对粒子无效（dev-notes §7.11.9）。scale=1 时观感不变，安全。
          EnableHierarchyScaling(go);
          Dbg.Log($"[RhodesDoctor] 地面特效构建={BuildTag}（通用：仅染色 + 缩放，无部件手术）");
        }

        LogInventory(go);

        // ★ 决定性排查：环**不在我们的克隆体里**（日志证明：克隆体只有 13 粒子 + 1 个 Decal 网格，
        //   全都开关过，环依旧在）。那它只能在**游戏自己给区域画的那层视图**上。
        //   这里把 `area.View` 这棵树上的**全部渲染器 / 粒子 / 子物体**列出来——不猜，直接看名字。
        LogAreaView(area);


        // ⚠️ 关键一步：**"投影贴花"那种层，黑不在 RGB 里，而在贴图的 Alpha 里。**
        // 2026-10-09 实测：把该层所有颜色属性与贴图 RGB 全染成目标色之后，屏幕**一点没变**——
        // 因为这类 shader 用**贴图 Alpha 当"压暗量"**（`ground × (1-a)` 之类），
        // 而 FxTint 染贴图时**保留 Alpha**。所以想让它变淡，只能压 Alpha。
        //
        // ⚠️ 但**不能全压、更不能压太狠**：2026-10-09 实测压到 0.22 时**整片残留直接消失**
        // （用户："现在钙质化的残留特效没了"）——因为那层贴花的**可见性本身**就由 Alpha 决定，
        // 它只负责"压暗"，不负责"上色"（所以 RGB 染色对它无效）。
        // 现在的参数：**只压主贴图 `_BaseMap`（那块"暗块"）、保留 `_Noise*`（花纹/白线）**，
        // 且只压到 **0.55**（暗度约减半）→ 形状回来、不再是黑壳。
        // 命中数会打日志：**命中 0 说明这一层不在这棵树上**（那就要换思路：换素材或换做法）。
        if (spec.CalcProfile)
        try
        {
          FxTint.CutAlphaByShader(go, "Decals/Projector", 0.55f, propNameContains: "basemap");
        }
        catch (Exception e)
        {
          Main.Mod.Logger.Error("[RhodesDoctor] 投影贴花压 Alpha 失败：\n" + e);
        }
        // 再延迟补染两次：有些 prefab 的"残留层"是**运行后才挂上来的子物件**，克隆那一刻还不在树上，
        // 一次性染不到它（这是"初始对了、残留还是黑的"的另一个可能原因）。补染是幂等的。
        try
        {
          var retint = go.AddComponent<DelayedRetint>();
          retint.Tint = spec.Tint;
          retint.TextureLift = spec.TextureLift;
          retint.CutAlpha = spec.CalcProfile;
        }
        catch (Exception e)
        {
          Main.Mod.Logger.Error("[RhodesDoctor] 地面特效延迟补染挂载失败（不影响首次染色）：\n" + e);
        }
        // 移动领域（影之疆域）：区域跟着载体走，克隆体必须**每帧跟住区域位置**——
        // `OnAreaSpawned` 只在生成那一刻定过位，不跟就会把特效留在原地。
        // 静态区域也挂它（位置不变，无副作用）。
        try
        {
          var follow = go.AddComponent<FollowArea>();
          follow.Area = area;
        }
        catch (Exception e)
        {
          Main.Mod.Logger.Error("[RhodesDoctor] 地面特效跟随挂载失败（不影响生成）：\n" + e);
        }

        Live[area] = go;
        Dbg.Log("[RhodesDoctor] 地面区域特效已生成（克隆重染）。");
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 地面特效生成失败：\n" + e);
      }
    }

    internal static void OnAreaDestroyed(AreaEffectEntityData area)
    {
      try
      {
        if (Live.TryGetValue(area, out var go))
        {
          Live.Remove(area);
          if (go != null)
          {
            // 不直接销毁：**先渐隐再删**（直接删是"啪"地消失，用户觉得突兀）。
            try
            {
              var f = go.GetComponent<FadeOutThenDestroy>();
              if (f == null) f = go.AddComponent<FadeOutThenDestroy>();
              f.Begin();
            }
            catch { UnityEngine.Object.Destroy(go); }
          }
        }
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 地面特效清理失败：\n" + e);
      }
    }

    private static Spec Lookup(AreaEffectEntityData area)
    {
      var guid = area?.Blueprint?.AssetGuid.ToString();
      if (string.IsNullOrEmpty(guid)) return null;
      return Registry.TryGetValue(Norm(guid), out var spec) ? spec : null;
    }

    private static GameObject Template(Spec spec)
    {
      var id = spec?.Link?.AssetId;
      if (string.IsNullOrEmpty(id)) return null;
      if (Templates.TryGetValue(id, out var cached)) return cached;
      GameObject go = null;
      try
      {
        if (spec.Link.Exists()) go = spec.Link.Load(false, true);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error($"[RhodesDoctor] 地面特效模板加载失败（{id}）：\n{e}");
      }
      if (go == null) Dbg.Log($"[RhodesDoctor] 地面特效模板加载返回 null（{id}）。");
      Templates[id] = go; // 负缓存：失败也记着，免得每个区域都报一次
      return go;
    }

    private static string Norm(string guid) =>
      string.IsNullOrEmpty(guid) ? "" : guid.Replace("-", "").ToLowerInvariant();

    private static void Prune()
    {
      var dead = new List<AreaEffectEntityData>();
      foreach (var kv in Live)
        if (kv.Key == null || kv.Value == null) dead.Add(kv.Key);
      foreach (var k in dead) Live.Remove(k);
    }
    /// <summary>
    /// 把"循环波纹"改成**连续**：名字匹配 <paramref name="nameContains"/> 的粒子系统，
    /// 让相邻两颗在时间上**错开重叠**，这样"上一颗还没淡完，下一颗已经开始扩"，圈就不会断。
    ///
    /// 两种发射方式分别处理：
    ///   · **持续发射**（`rateOverTime &gt; 0`）→ 发射速率 ×<paramref name="overlap"/>（粒子更密，自然重叠）；
    ///   · **爆发发射**（`burstCount &gt; 0`）→ 把每个 burst 复制成 <paramref name="overlap"/> 份，
    ///     按 `周期 / overlap` 依次错开（周期取 burst 的 `repeatInterval`，没有就退到 `startLifetime`）。
    ///
    /// ⚠️ 只是"更接近连续"，不是完美无缝；重叠数给 2 就够补上那个空档，给太多会过曝/变乱。
    /// 改了会打日志（原值 → 新值），效果不对时把 <paramref name="overlap"/> 调回 1 即可复原。
    /// </summary>
    private static void MakeLoopsContinuous(GameObject go, string nameContains, int overlap)
    {
      if (go == null || overlap < 2 || string.IsNullOrEmpty(nameContains)) return;
      var key = nameContains.ToLowerInvariant();
      foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
      {
        try
        {
          if (ps == null) continue;
          if ((ps.name ?? "").ToLowerInvariant().IndexOf(key, StringComparison.Ordinal) < 0) continue;
          var em = ps.emission;

          // ① 持续发射：直接加倍速率
          var rate = em.rateOverTime;
          if (rate.constantMax > 0.0001f)
          {
            var nr = new ParticleSystem.MinMaxCurve(rate.constantMax * overlap, rate.constantMax * overlap);
            em.rateOverTime = nr;
            Dbg.Log($"[RhodesDoctor] 连续化：粒子 '{ps.name}' 速率"
              + $" {rate.constantMax:F2} → {rate.constantMax * overlap:F2}");
            continue;
          }

          // ② 爆发发射：把每个 burst 复制若干份、在周期内错开
          int n = em.burstCount;
          if (n <= 0) continue;
          var src = new ParticleSystem.Burst[n];
          em.GetBursts(src);
          // 周期：优先用 burst 自己的 repeatInterval（cycleCount>1 才有意义），否则退到寿命
          float period = 0f;
          foreach (var b in src)
            if (b.cycleCount > 1 && b.repeatInterval > 0.01f && b.repeatInterval > period)
              period = b.repeatInterval;
          if (period <= 0.01f) period = Mathf.Max(0.2f, ps.main.startLifetime.constantMax);

          var dst = new List<ParticleSystem.Burst>();
          for (int k = 0; k < overlap; k++)
            foreach (var b in src)
              dst.Add(new ParticleSystem.Burst(
                b.time + period * k / overlap, b.minCount, b.maxCount, b.cycleCount, b.repeatInterval));
          em.SetBursts(dst.ToArray());
          Dbg.Log($"[RhodesDoctor] 连续化：粒子 '{ps.name}' burst {n} 份 → {dst.Count} 份"
            + $"（周期 {period:F2}s，错开 {overlap} 份）");
        }
        catch (Exception e)
        {
          Main.Mod.Logger.Error("[RhodesDoctor] 连续化失败（保持原样）：\n" + e);
        }
      }
    }
    /// <summary>
    /// 关掉名字匹配 <paramref name="nameContains"/> 的粒子系统：停止发射、清掉已有粒子、关掉渲染器。
    /// 用于"这个部件不要"的场合（例：钙质化外扩的那圈细白环 = `ProjectorDecal (1)_Cycle`）。
    /// 只动渲染器，**不销毁 GameObject**——留着层级，免得影响同一棵树上别的部件。
    /// </summary>
    private static void DisableEmitters(GameObject go, string nameContains)
    {
      if (go == null || string.IsNullOrEmpty(nameContains)) return;
      var key = nameContains.ToLowerInvariant();
      int n = 0;
      foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
      {
        try
        {
          if (ps == null) continue;
          if ((ps.name ?? "").ToLowerInvariant().IndexOf(key, StringComparison.Ordinal) < 0) continue;
          var em = ps.emission;
          em.enabled = false;
          ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
          var psr = ps.GetComponent<ParticleSystemRenderer>();
          if (psr != null) psr.enabled = false;
          n++;
          Dbg.Log($"[RhodesDoctor] 已关掉粒子 '{ps.name}'（名字含 '{nameContains}'）。");
        }
        catch (Exception e)
        {
          Main.Mod.Logger.Error("[RhodesDoctor] 关粒子失败（保持原样）：\n" + e);
        }
      }
      Dbg.Log($"[RhodesDoctor] 关粒子汇总：名字含 '{nameContains}' 的命中 {n} 个。");
    }

    /// <summary>
    /// 把克隆体里**全部粒子系统**的缩放模式改成 <see cref="ParticleSystemScalingMode.Hierarchy"/>。
    ///
    /// 为什么需要：Unity 默认的 `Local` 模式下**粒子尺寸不跟根节点缩放走**，所以 `Register` 传的
    /// `scale` 对粒子无效——这正是 dev-notes §7.11.9 那条"粒子缩不小"老坑的成因。
    /// 改成 `Hierarchy` 后，根节点的 `localScale` 才是对粒子有效的缩放旋钮。
    /// **`scale = 1` 时观感不变**，所以对"素材尺寸已与法术半径相等、免缩放"的素材也安全。
    ///
    /// 用途：冰封领域（30 尺素材对 30 尺区域，scale=1）、儿时舞乐（20 尺素材 → 40 尺，scale=2）、
    /// 天灾降临（40 尺素材 → 80 尺，scale=2）这三个"通用档"素材走这里；
    /// 钙质化不走（它靠 <see cref="FitCloudToArea"/> 给 wave 单独设 Hierarchy，行为保持不变）。
    /// </summary>
    private static void EnableHierarchyScaling(GameObject go)
    {
      if (go == null) return;
      int n = 0;
      foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
      {
        try
        {
          if (ps == null) continue;
          var main = ps.main;
          main.scalingMode = ParticleSystemScalingMode.Hierarchy;
          n++;
        }
        catch { }
      }
      Dbg.Log($"[RhodesDoctor] 通用档：{n} 个粒子系统已设为 Hierarchy 缩放模式（让 scale 生效）。");
    }

    /// <summary>
    /// 把"一次性爆发"的粒子改成**持续发射**，让地面那层云不再淡成一小块、而是**全程铺着**。
    ///
    /// 做法：把 `emission` 里的 burst 全部折算成一个持续速率，然后清掉 burst：
    /// `rate = 每次爆发的粒子总数 / 粒子寿命 × density`（`density` = 平均同时存在几层云，2 就是两层叠着）。
    /// 这样"旧的还没淡完、新的已经在冒"，观感是连续的一层云，而不是"噗一下 → 淡掉 → 没了"。
    ///
    /// ⚠️ 只处理**有 burst 的**粒子；本来就是持续发射的不管。
    /// ⚠️ **已经被 <see cref="DisableEmitters"/> 关掉的粒子会跳过**（靠"渲染器还开着吗"判断），
    /// 免得把不想要的部件又给打开。
    /// </summary>
    private static void MakePersistent(GameObject go, float density)
    {
      if (go == null || density <= 0f) return;
      foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
      {
        try
        {
          if (ps == null) continue;
          var psr = ps.GetComponent<ParticleSystemRenderer>();
          if (psr != null && !psr.enabled) continue;   // 已关掉的部件不动
          // 让"根节点缩放"能真的放大粒子（默认 Local 模式下粒子尺寸不跟缩放走，
          // 这也是文档里"粒子缩不小"那条的成因）。改成 Hierarchy 后，clone 的 scale 才是有效旋钮。
          // scale = 1 时观感不变，所以这一步是安全的。
          var main0 = ps.main;
          main0.scalingMode = ParticleSystemScalingMode.Hierarchy;

          var em = ps.emission;
          int n = em.burstCount;
          if (n <= 0) continue;                        // 本来就是持续发射
          var src = new ParticleSystem.Burst[n];
          em.GetBursts(src);
          float total = 0f;
          foreach (var b in src) total += Mathf.Max(b.minCount, b.maxCount);
          float life = Mathf.Max(0.5f, ps.main.startLifetime.constantMax);
          float rate = Mathf.Max(0.1f, total / life * density);
          em.rateOverTime = new ParticleSystem.MinMaxCurve(rate);
          em.SetBursts(new ParticleSystem.Burst[0]);
          Dbg.Log($"[RhodesDoctor] 持续化：粒子 '{ps.name}' burst {n} 份(共 {total:F0} 颗)"
            + $" → 速率 {rate:F2}/秒（寿命 {life:F1}s ×{density:F1}）");
        }
        catch (Exception e)
        {
          Main.Mod.Logger.Error("[RhodesDoctor] 持续化失败（保持原样）：\n" + e);
        }
      }
    }

    /// <summary>
    /// 把"云"调成：**施法瞬间就出现 / 1 秒内扩到最大 / 铺满法术范围 / 全程不消失**。
    ///
    /// 日志实测（`形状=Circle(Circle.radius=0.01m)`）：这层云是**点发射**，
    /// 尺寸完全由**粒子自身尺寸**决定，不是靠发射范围铺开。所以：
    ///
    /// | 要求 | 手段 |
    /// |---|---|
    /// | 施法瞬间出现 | `main.startDelay = 0`；尺寸曲线**从 0 起步但 1 秒内就长满**，所以肉眼上"立刻就有" |
    /// | 1 秒内扩到最大 | ① `startLifetime = 1.4s`（**这条最关键**：`sizeOverLifetime` 按寿命百分比走，寿命给长了长大就被摊慢）② `sizeOverLifetime` 曲线改成"前 1 s 从 0 升到 1、之后保持 1" |
    /// | 铺满法术范围 | `main.startSize = 法术直径(12.192 m)`（点发射时**粒子尺寸就是覆盖面**） |
    /// | 全程不消失 | `rateOverTime ≥ 3/s`（单颗只活 1.4 s，靠持续发射接力）+ 保留原有的起手 burst |
    ///
    /// ⚠️ 之前用"发射形状半径"做几何换算是**无效**的——形状半径只有 0.01 m，算出来是兜底值。
    /// 教训：**先看日志里的形状类型/半径，再决定用哪条公式**，不要假定发射形状是铺开的。
    /// </summary>
    private static void FitCloudToArea(GameObject go, string nameContains,
      float areaRadiusMeters, float cloudDiameter)
    {
      if (go == null || string.IsNullOrEmpty(nameContains)) return;
      var key = nameContains.ToLowerInvariant();
      foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
      {
        try
        {
          if (ps == null) continue;
          if ((ps.name ?? "").ToLowerInvariant().IndexOf(key, StringComparison.Ordinal) < 0) continue;

          var main = ps.main;
          float oldLife = main.startLifetime.constantMax;
          float oldSize = main.startSize.constantMax;
          float oldDelay = main.startDelay.constantMax;

          // ★ 关键：**开循环**。原素材是"播一次就停"（`looping=false`），
          // 所以按它自带的 `duration`（2~3 秒）发射完就**不再冒新的**，云随即消失
          // （用户："白云才有二三秒就没了"）。改成循环后，只要克隆体还在（= 区域还在），
          // 云就一直被接力发射 → **一直铺着，直到法术效果结束**。
          main.loop = true;
          main.duration = Mathf.Max(main.duration, 60f);
          main.startDelay = new ParticleSystem.MinMaxCurve(0f);          // 立刻出现
          main.startLifetime = new ParticleSystem.MinMaxCurve(CloudLifetime);
          main.startSize = new ParticleSystem.MinMaxCurve(cloudDiameter); // 点发射：尺寸=覆盖面
          main.scalingMode = ParticleSystemScalingMode.Hierarchy;

          // 长大曲线：前 CloudGrowSeconds 秒从 0 升到 1，之后保持 1
          var sol = ps.sizeOverLifetime;
          sol.enabled = true;
          var curve = new AnimationCurve();
          float t = Mathf.Clamp01(CloudGrowSeconds / CloudLifetime);
          curve.AddKey(0f, 0f);
          curve.AddKey(t, 1f);
          curve.AddKey(1f, 1f);
          sol.size = new ParticleSystem.MinMaxCurve(1f, curve);

          // 持续发射：单颗只活 1.4 s，速率不够就会"闪一下没了"
          var em = ps.emission;
          float oldRate = em.rateOverTime.constantMax;
          if (oldRate < 3f) em.rateOverTime = new ParticleSystem.MinMaxCurve(3f);

          Dbg.Log($"[RhodesDoctor] 云调整：'{ps.name}'"
            + $" 延迟 {oldDelay:F2}s→0  寿命 {oldLife:F1}s→{CloudLifetime:F1}s"
            + $"  尺寸 {oldSize:F2}→{cloudDiameter:F2}（startSize，按半径语义=区域半径×系数）"
            + $"  长大 {CloudGrowSeconds:F1}s  速率 {oldRate:F2}→{em.rateOverTime.constantMax:F2}"
            + $"  循环=开（原 looping 见注释）");
        }
        catch (Exception e)
        {
          Main.Mod.Logger.Error("[RhodesDoctor] 云调整失败（保持原样）：\n" + e);
        }
      }
    }

    /// <summary>
    /// **销毁**名字匹配的粒子系统所在物体（而不是只关渲染器）。
    ///
    /// 为什么需要：钙质化的环，把渲染器 `enabled = false` 之后**照样在屏幕上**
    /// （日志已确认那些渲染器确实都是"关"状态）。所以"关"这个手段对它无效，必须**删掉物体**。
    /// ⚠️ 安全阀：**若该物体上还挂着别的粒子系统**（比如云 `Waves`），就**不销毁**、
    /// 只把匹配的那个关掉——避免把云一起误杀。
    /// </summary>
    private static void DestroyEmitters(GameObject go, string nameContains)
    {
      if (go == null || string.IsNullOrEmpty(nameContains)) return;
      var key = nameContains.ToLowerInvariant();
      int destroyed = 0, skipped = 0;
      foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
      {
        try
        {
          if (ps == null) continue;
          if ((ps.name ?? "").ToLowerInvariant().IndexOf(key, StringComparison.Ordinal) < 0) continue;
          var host = ps.gameObject;
          // 安全阀：这个物体上还有别的粒子吗？
          bool hasOther = false;
          foreach (var other in host.GetComponents<ParticleSystem>())
            if (other != ps && other != null) { hasOther = true; break; }
          if (hasOther)
          {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var em0 = ps.emission;
            em0.enabled = false;
            var psr0 = ps.GetComponent<ParticleSystemRenderer>();
            if (psr0 != null) psr0.enabled = false;
            skipped++;
            Dbg.Log($"[RhodesDoctor] 销毁跳过（同物体还有别的粒子）：'{ps.name}' → 改为关渲染器。");
            continue;
          }
          Dbg.Log($"[RhodesDoctor] 销毁粒子物体：'{ps.name}'");
          UnityEngine.Object.Destroy(host);
          destroyed++;
        }
        catch (Exception e)
        {
          Main.Mod.Logger.Error("[RhodesDoctor] 销毁粒子失败：\n" + e);
        }
      }
      Dbg.Log($"[RhodesDoctor] 销毁汇总：名字含 '{nameContains}' → 销毁 {destroyed} 个 / 改关 {skipped} 个。");
    }

    /// <summary>**销毁**所有非粒子渲染器所在的物体（网格 / 缎带 / 线条）。安全阀同上。</summary>
    private static void DestroyNonParticleRenderers(GameObject go)
    {
      if (go == null) return;
      int n = 0;
      foreach (var r in go.GetComponentsInChildren<Renderer>(true))
      {
        try
        {
          if (r == null || r is ParticleSystemRenderer) continue;
          Dbg.Log($"[RhodesDoctor] 销毁非粒子渲染器物体：{r.GetType().Name} '{r.name}'");
          UnityEngine.Object.Destroy(r.gameObject);
          n++;
        }
        catch { }
      }
      Dbg.Log($"[RhodesDoctor] 销毁非粒子渲染器汇总：{n} 个。");
    }

    /// <summary>
    /// 关掉克隆体里**所有非粒子渲染器**（MeshRenderer / SkinnedMeshRenderer / TrailRenderer /
    /// LineRenderer …）以及任何名字含 `Projector` 的组件，**只留粒子系统**。
    ///
    /// 为什么需要：钙质化那圈"外扩的白环"**不是粒子**（13 个粒子系统里没有它），
    /// 所以 `DisableEmitters` 怎么关都没用。用户的原话点破了结构：
    /// **"这是几个特效衔接：喷溅 → 白云扩大消失 → 最后剩这圈环"** ——
    /// 环是链条最后一段，属于**非粒子**部件。要云（粒子）不要环，就按这条线切。
    /// 逐个打日志（类型 / 名字 / shader / 网格名），便于事后核对到底关了什么。
    /// </summary>
    private static void DisableNonParticleRenderers(GameObject go)
    {
      if (go == null) return;
      int n = 0;
      foreach (var r in go.GetComponentsInChildren<Renderer>(true))
      {
        try
        {
          if (r == null || r is ParticleSystemRenderer) continue;  // 粒子留着
          string mesh = "";
          var mf = r.GetComponent<MeshFilter>();
          if (mf != null && mf.sharedMesh != null) mesh = mf.sharedMesh.name;
          string sh = r.sharedMaterial != null && r.sharedMaterial.shader != null
            ? r.sharedMaterial.shader.name : "?";
          Dbg.Log($"[RhodesDoctor] 关非粒子渲染器：{r.GetType().Name} '{r.name}'"
            + $" shader={sh} 网格={mesh}");
          r.enabled = false;
          n++;
        }
        catch { }
      }
      foreach (var comp in go.GetComponentsInChildren<Component>(true))
      {
        try
        {
          if (comp == null || comp is Renderer || comp is Transform) continue;
          var t = comp.GetType();
          if (t.Name.IndexOf("Projector", StringComparison.OrdinalIgnoreCase) < 0) continue;
          var p = t.GetProperty("enabled");
          if (p != null && p.CanWrite && p.PropertyType == typeof(bool))
          {
            p.SetValue(comp, false, null);
            Dbg.Log($"[RhodesDoctor] 关非粒子渲染器：{t.Name}（组件）");
            n++;
          }
        }
        catch { }
      }
      Dbg.Log($"[RhodesDoctor] 非粒子渲染器汇总：关掉 {n} 个（粒子系统一律保留）。");
    }

    /// <summary>
    /// 列出**游戏自己给区域画的那层视图**（`area.View`）上的全部渲染器 / 粒子 / 子物体。
    ///
    /// 为什么需要：钙质化那圈白环，日志已经证明**不在我们的克隆体里**
    /// （克隆体只有 13 个粒子 + 1 个 `Decal` 网格，全都开关过，环始终在）。
    /// 那么它只能来自区域自身的视图——这里把它的组成全部打出来，**看名字定位，不猜**。
    /// </summary>
    [System.Diagnostics.Conditional("RD_VERBOSE")]
    private static void LogAreaView(AreaEffectEntityData area)
    {
      try
      {
        var view = area?.View;
        if (view == null)
        {
          Dbg.Log("[RhodesDoctor] 区域视图：area.View 为 null。");
          return;
        }
        var root = view.gameObject;
        var sb = new System.Text.StringBuilder();

        int rn = 0;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
          if (r == null) continue;
          rn++;
          string mesh = "";
          var mf = r.GetComponent<MeshFilter>();
          if (mf != null && mf.sharedMesh != null) mesh = mf.sharedMesh.name;
          var smr = r as SkinnedMeshRenderer;
          if (smr != null && smr.sharedMesh != null) mesh = smr.sharedMesh.name;
          string sh = r.sharedMaterial != null && r.sharedMaterial.shader != null
            ? r.sharedMaterial.shader.name : "?";
          sb.Append("\n  [R").Append(rn).Append("] ").Append(r.GetType().Name).Append(" '")
            .Append(Path(r.transform)).Append("'  enabled=").Append(r.enabled)
            .Append("  shader=").Append(sh).Append("  网格=").Append(mesh);
        }
        Dbg.Log($"[RhodesDoctor] 区域视图 渲染器（共 {rn} 个）：{sb}");

        var sb2 = new System.Text.StringBuilder();
        int pn = 0;
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
        {
          if (ps == null) continue;
          pn++;
          var psr = ps.GetComponent<ParticleSystemRenderer>();
          sb2.Append("\n  [P").Append(pn).Append("] ").Append(Path(ps.transform))
            .Append("  渲染器=").Append(psr != null && psr.enabled ? "开" : "关")
            .Append("  速率=").Append(ps.emission.rateOverTime.constantMax.ToString("F2"))
            .Append("  寿命=").Append(ps.main.startLifetime.constantMax.ToString("F1")).Append("s");
        }
        Dbg.Log($"[RhodesDoctor] 区域视图 粒子（共 {pn} 个）：{sb2}");

        var sb3 = new System.Text.StringBuilder();
        int gn = 0;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
          if (t == null || t == root.transform) continue;
          gn++;
          sb3.Append("\n  [G").Append(gn).Append("] ").Append(Path(t));
        }
        Dbg.Log($"[RhodesDoctor] 区域视图 子物体（共 {gn} 个）：{sb3}");
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 区域视图清单失败：\n" + e);
      }
    }

    /// <summary>打印节点在层级里的路径，便于对照。</summary>
    private static string Path(Transform t)
    {
      try
      {
        var s = t.name;
        var p = t.parent;
        int depth = 0;
        while (p != null && depth < 4) { s = p.name + "/" + s; p = p.parent; depth++; }
        return s;
      }
      catch { return t != null ? t.name : "?"; }
    }

    /// <summary>
    /// 打一份粒子的**完整清单**（只打一次）：名字 / 渲染器开没开 / burst 数 / 持续速率 / 寿命。
    /// 用途：屏幕上出现"不该有的部件"时，对照这份清单就能定位是哪一个粒子，
    /// **不用再靠猜名字**（钙质化的粗白圈就是猜错名字的代价）。
    /// </summary>
    [System.Diagnostics.Conditional("RD_VERBOSE")]
    private static void LogInventory(GameObject go)
    {
      try
      {
        var sb = new System.Text.StringBuilder();
        int n = 0;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
          if (ps == null) continue;
          n++;
          var psr = ps.GetComponent<ParticleSystemRenderer>();
          var em = ps.emission;
          sb.Append("\n  [").Append(n).Append("] ").Append(ps.name)
            .Append("  渲染器=").Append(psr != null && psr.enabled ? "开" : "关")
            .Append("  burst=").Append(em.burstCount)
            .Append("  速率=").Append(em.rateOverTime.constantMax.ToString("F2"))
            .Append("  寿命=").Append(ps.main.startLifetime.constantMax.ToString("F1")).Append("s");
        }
        Dbg.Log($"[RhodesDoctor] 地面特效粒子清单（共 {n} 个）：{sb}");

        // 非粒子渲染器也要列——钙质化的"环"就藏在这里（粒子清单里根本没有它）。
        var sb2 = new System.Text.StringBuilder();
        int m = 0;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
          if (r == null || r is ParticleSystemRenderer) continue;
          m++;
          var mf = r.GetComponent<MeshFilter>();
          string mesh = mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "";
          string sh = r.sharedMaterial != null && r.sharedMaterial.shader != null
            ? r.sharedMaterial.shader.name : "?";
          sb2.Append("\n  [").Append(m).Append("] ").Append(r.GetType().Name).Append(" '")
            .Append(r.name).Append("'  enabled=").Append(r.enabled)
            .Append("  shader=").Append(sh).Append("  网格=").Append(mesh);
        }
        Dbg.Log($"[RhodesDoctor] 地面特效非粒子渲染器（共 {m} 个）：{sb2}");
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 粒子清单打印失败：\n" + e);
      }
    }

  }

  /// <summary>
  /// 延迟补染：挂在克隆体上，在几个时间点再跑一遍 <see cref="FxTint.Apply"/>。
  ///
  /// 为什么需要：有些原版 FX 的"地面残留层"是**运行后才挂上来的子物件**（或延迟启用的渲染器），
  /// 克隆那一刻它还不在这棵树上，一次性染色够不到它——症状就是**"初始爆发变浅了、地上那层还是黑的"**。
  /// 补染是幂等的（贴图有缓存、网格漂白过一次后第二次会跳过），多跑几遍没有副作用。
  /// </summary>
  /// <summary>
  /// 让地面特效**跟住区域位置**（移动领域用：影之疆域跟着目标友军走）。
  /// 挂在克隆体上，`LateUpdate` 里把位置对齐到 `AreaEffectEntityData.Position`。
  /// 静态区域也挂——位置不变，无副作用；区域没了就自删。
  /// </summary>
  internal sealed class FollowArea : MonoBehaviour
  {
    internal AreaEffectEntityData Area;

    private void LateUpdate()
    {
      if (Area == null) { UnityEngine.Object.Destroy(this); return; }
      transform.position = Area.Position;
    }
  }

  internal sealed class DelayedRetint : MonoBehaviour
  {
    internal Color Tint;
    internal float TextureLift;
    /// <summary>是否在补染后一并"压投影贴花 Alpha"（只有钙质化那套素材需要；见 AreaGroundFx.Spec.CalcProfile）。</summary>
    internal bool CutAlpha;

    /// <summary>补染时间点（秒）。挑三个覆盖"刚挂上 / 淡入完 / 稳定后"。</summary>
    private static readonly float[] At = { 0.35f, 1.2f, 3.0f };

    private int _next;
    private float _t;

    private void Update()
    {
      _t += Time.deltaTime;
      while (_next < At.Length && _t >= At[_next])
      {
        _next++;
        try
        {
          FxTint.Apply(gameObject, Tint, 0f, tintTextures: true,
            textureLift: TextureLift, tintAllTextures: true);
          // 补染会把贴图重新生成一遍，所以"压 Alpha"要跟着再压一次（幂等）。
          // 只有钙质化那套素材需要（CalcProfile）；其余素材不碰贴花 Alpha。
          if (CutAlpha)
            FxTint.CutAlphaByShader(gameObject, "Decals/Projector", 0.55f, propNameContains: "basemap");
        }
        catch (Exception e)
        {
          Main.Mod.Logger.Error("[RhodesDoctor] 地面特效延迟补染失败：\n" + e);
        }
      }
      if (_next >= At.Length) UnityEngine.Object.Destroy(this);
    }
  }

  /// <summary>
  /// 延迟读**云的实际粒子数据**（`ParticleSystem.GetParticles`），算出真实可见半径并打日志。
  ///
  /// 为什么需要：`startSize` 设了多少，**不等于屏幕上看到多大**——粒子可能被速度带开、
  /// 或有额外的尺寸/拉伸设置。截图量像素又受相机与软边影响。
  /// **直接读粒子数据是唯一客观的口径**：
  ///   可见半径 ≈ 最远粒子中心距 + 最大粒子尺寸/2
  /// </summary>
  /// <summary>
  /// 区域结束时的**渐隐**：停止发射 → 在 <see cref="Seconds"/> 秒内把所有材质 alpha 从当前值线性降到 0 → 销毁。
  ///
  /// 为什么需要：克隆体原本是"区域结束即销毁"，画面上就是**"啪"地消失**（用户："有点突兀"）。
  /// 渐隐让云自然地散掉。
  /// </summary>
  internal sealed class FadeOutThenDestroy : MonoBehaviour
  {
    internal float Seconds = 1.2f;

    private bool _started;
    private float _t;
    private float _a0 = 1f;

    internal void Begin()
    {
      if (_started) return;
      _started = true;
      _t = 0f;
      try
      {
        // 先停发射：不再冒新的，只让已有的自然演完
        foreach (var ps in gameObject.GetComponentsInChildren<ParticleSystem>(true))
        {
          if (ps == null) continue;
          ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
      }
      catch { }
      FxTint.SetAlpha(gameObject, 1f);   // 记录起点为不透明
    }

    private void Update()
    {
      if (!_started) return;
      _t += Time.deltaTime;
      float k = Seconds > 0.01f ? Mathf.Clamp01(_t / Seconds) : 1f;
      FxTint.SetAlpha(gameObject, Mathf.Lerp(_a0, 0f, k));
      if (k >= 1f)
      {
        UnityEngine.Object.Destroy(gameObject);
      }
    }
  }

  /// <summary>
  /// 云的"实测 + 热键调参"组件（挂在克隆体上，随区域销毁）。
  ///
  /// **为什么要热键**：尺寸换算我连续算错（`Feet.Meters` → `startSize` 这条链上必有一处假设不对），
  /// 与其一轮轮猜，不如**让你在游戏里直接调到对为止**，我读日志拿最终数值写死。
  ///
  /// 热键（施法后、云还在时按）：
  ///   · `[`  云变小（×0.9）
  ///   · `]`  云变大（×1.1）
  /// 每按一次都会打日志，形如：
  ///   `云调参：直径 10.73m → 9.66m（系数 ×0.90，累计 ×0.81）`
  /// **调到你觉得正好铺满红圈，把最后一行日志发我（或我自己读），我把值写死。**
  /// </summary>
  internal sealed class CloudExtentLog : MonoBehaviour
  {
    private static readonly float[] At = { 2f, 6f, 12f };
    private int _next;
    private float _t;
    private float _cum = 1f;      // 累计调节倍率

    private void Update()
    {
      _t += Time.deltaTime;

      // 热键：实时调云的大小
      try
      {
        bool smaller = Input.GetKeyDown(KeyCode.LeftBracket);
        bool bigger = Input.GetKeyDown(KeyCode.RightBracket);
        if (smaller || bigger)
        {
          float k = smaller ? 0.9f : 1.1f;
          _cum *= k;
          foreach (var ps in gameObject.GetComponentsInChildren<ParticleSystem>(true))
          {
            if (ps == null) continue;
            if ((ps.name ?? "").ToLowerInvariant().IndexOf("wave", StringComparison.Ordinal) < 0) continue;
            var main = ps.main;
            float cur = main.startSize.constantMax;
            float nv = Mathf.Max(0.2f, cur * k);
            main.startSize = new ParticleSystem.MinMaxCurve(nv);
            Dbg.Log($"[RhodesDoctor] 云调参：直径 {cur:F2}m → {nv:F2}m"
              + $"（本次 ×{k:F2}，累计 ×{_cum:F3}）  ← 觉得对了就把这行发我");
          }
        }
      }
      catch { }

      // 实测
      if (_next < At.Length && _t >= At[_next])
      {
        _next++;
        try
        {
          foreach (var ps in gameObject.GetComponentsInChildren<ParticleSystem>(true))
          {
            if (ps == null) continue;
            if ((ps.name ?? "").ToLowerInvariant().IndexOf("wave", StringComparison.Ordinal) < 0) continue;
            var buf = new ParticleSystem.Particle[Mathf.Max(1, ps.particleCount)];
            int n = ps.GetParticles(buf);
            float maxR = 0f, maxSize = 0f, maxR2 = 0f;
            for (int i = 0; i < n; i++)
            {
              var v = buf[i].position;
              float r = Mathf.Sqrt(v.x * v.x + v.z * v.z);
              if (r > maxR) maxR = r;
              float sz = buf[i].GetCurrentSize(ps);
              if (sz > maxSize) maxSize = sz;
              if (r + sz * 0.5f > maxR2) maxR2 = r + sz * 0.5f;
            }
            Dbg.Log($"[RhodesDoctor] 云实测(t={_t:F0}s)：粒子数={n}"
              + $" 最远中心距={maxR:F2} 最大粒子={maxSize:F2}"
              + $" ⇒ 直径≈{maxR2 * 2f:F2}（设定 startSize={ps.main.startSize.constantMax:F2}）");
          }
        }
        catch (Exception e) { Main.Mod.Logger.Error("[RhodesDoctor] 读云尺寸失败：\n" + e); }
      }
      if (_next >= At.Length && _t > At[At.Length - 1] + 0.5f) UnityEngine.Object.Destroy(this);
    }
  }

  /// <summary>区域生成/结束的事件订阅（常驻，Main.Load 里订阅一次）。</summary>
  internal sealed class AreaGroundFxWatcher : IAreaEffectHandler
  {
    public void HandleAreaEffectSpawned(AreaEffectEntityData entityData)
      => AreaGroundFx.OnAreaSpawned(entityData);
    public void HandleAreaEffectDestroyed(AreaEffectEntityData entityData)
      => AreaGroundFx.OnAreaDestroyed(entityData);
  }
}
