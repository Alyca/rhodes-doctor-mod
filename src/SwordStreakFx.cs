// 剑雨的"一缕金光"：不借任何原版粒子素材，直接在落下的剑身上挂一条 TrailRenderer 细光带。
//
// 为什么自己画（别再回头去借粒子素材）：
//   · 借"阳炎之刃"投射物外观（ProjectileRefs.Mythic4lvlAngel_BladeOfTheSun00）实测是一根
//     **数百米量级的巨型光柱**：Player.log 的 `[特效尺寸]` 记到 原始包围盒=345.6x62.3x343.8 米，
//     内含约 40 个粒子系统（Rays / RingWave / FireBody / ProjectorCollision …）。
//   · 用 FitToMeters 把它缩到 1.4 米**画面上仍是一整根橙色大光柱**——粒子系统的可见尺寸不跟着
//     根节点缩放走（包围盒自适应对粒子素材本来就不可靠，见 dev-notes §0）。18 把剑各来一条
//     → 就是那根又粗又亮的柱子。**这条路别再试了。**
//   · 要"淡一点、只是一缕金光"，唯一可控的做法是自己画一条窄光带：宽度 / 长度 / 颜色 / 淡出
//     全在自己手里，不受任何素材的隐藏尺寸摆布。
//
// 材质模板（两级兜底，任一成功即可；都失败就不加光尾、剑身照常，只打一行日志）：
//   ① 内置 shader（不加载任何素材，最轻）：Shader.Find 逐个试。只挑**透明类**的，
//      "Unlit/Color" 这种不透明的会画出一根实心棍，故意不列。
//   ② ①全落空时，克隆一个**确认能加载的金色特效**里的粒子材质（混合模式与贴图一起继承）：
//      先"阳炎爆"（圣光·金），再"阳炎之刃"投射物外观。只读模板，不实例化那个 prefab。
//      名称里带 distort / flow / noise / smoke / dust 的材质跳过——那些要抓屏或本来就发暗。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RhodesDoctor
{
  internal static class SwordStreakFx
  {
    /// <summary>内置 shader 候选（只列透明/发光类的，见文件头）：加法混合在最前，"光"感最好。</summary>
    private static readonly string[] BuiltinShaders =
    {
      "Legacy Shaders/Particles/Additive",
      "Legacy Shaders/Particles/Alpha Blended",
      "Sprites/Default",
      "Particles/Standard Unlit",
      "Mobile/Particles/Additive",
      "Mobile/Particles/Alpha Blended",
    };

    private static Material _template;
    private static bool _resolved;
    private static bool _templateFromVanilla;
    private static bool _logged;

    /// <summary>成品材质按颜色缓存：每次施法 18 把剑共用一份，别一把剑 new 一个（会漏）。</summary>
    private static readonly Dictionary<Color, Material> Mats = new();

    /// <summary>
    /// 给一把剑挂一条金色细光尾（跟着剑一路落下，剑停住后 <paramref name="seconds"/> 秒内淡掉）。
    /// host 为空 / 找不到可用材质时静默跳过，只打一行日志。
    /// </summary>
    internal static void Attach(GameObject host, Color color, float seconds, float width)
    {
      if (host == null) return;
      var mat = MaterialFor(color);
      if (mat == null) return;
      try
      {
        // 光带挂在一个"世界缩放归一"的子物件上，**不是直接挂在剑身上**：
        // TrailRenderer 的 startWidth 是**局部**单位，会再乘一次宿主的世界缩放（lossyScale）。
        // 武器模型的根节点缩放很随意（可能是 0.01，几何靠子网格的 100 顶回来），直接挂上去时
        // 13 cm 会被乘成 1 mm。实测这把剑的 lossyScale 是 0.7416（正常），所以**它不是本次
        // "看不到光"的原因**——但归一化仍然保留：这样传入的米数就是画面上真实的米数，不用再猜。
        // 同理 holder.layer 也跟着剑身走（新建 GameObject 默认在 Default 层，未必和原版特效同层）。
        var holder = new GameObject("RhodesDoctorStreak");
        var ht = holder.transform;
        ht.SetParent(host.transform, worldPositionStays: false);
        ht.localPosition = Vector3.zero;
        ht.localRotation = Quaternion.identity;
        // 跟着剑身上的图层走（见上）。图层在归一化那段一起处理。
        holder.layer = host.layer;
        var ls = host.transform.lossyScale;
        ht.localScale = new Vector3(
          Mathf.Abs(ls.x) > 1e-4f ? 1f / ls.x : 1f,
          Mathf.Abs(ls.y) > 1e-4f ? 1f / ls.y : 1f,
          Mathf.Abs(ls.z) > 1e-4f ? 1f / ls.z : 1f);
        LogScaleOnce(ls, host.layer, width, seconds);

        var tr = holder.AddComponent<TrailRenderer>();
        tr.sharedMaterial = mat;              // 共享材质（见 Mats 的注释）
        tr.time = Mathf.Max(0.05f, seconds);
        tr.startWidth = width;
        tr.endWidth = 0f;                     // 尾端收成尖
        tr.minVertexDistance = 0.06f;
        tr.numCapVertices = 2;
        tr.numCornerVertices = 2;
        tr.autodestruct = false;
        tr.emitting = true;
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tr.receiveShadows = false;
        // 两端都淡出：TrailRenderer 的渐变哪头是"头"在各版本里说法不一，两端都收 0 就不怕搞反——
        // 无论从哪头看，都是一缕两端渐隐的金光。
        var g = new Gradient();
        g.SetKeys(
          new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
          new[]
          {
            new GradientAlphaKey(0f, 0f),
            new GradientAlphaKey(0.80f, 0.2f),
            new GradientAlphaKey(0.45f, 0.7f),
            new GradientAlphaKey(0f, 1f),
          });
        tr.colorGradient = g;
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 金光尾拖生成失败：\n" + e);
      }
    }

    /// <summary>
    /// 给别的特效复用这份"已被实机验证能渲染"的金色材质（剑雨命中特效 <see cref="SwordHitFx"/> 用）。
    /// </summary>
    internal static Material StreakMaterial(Color color) => MaterialFor(color);

    private static readonly Dictionary<Color, Material> PlainMats = new Dictionary<Color, Material>();

    /// <summary>
    /// 同一份模板（混合模式/shader 照旧），但 **_MainTex 一律换成自生成的白色软边贴图**，
    /// 所以最终颜色就是传入的 color，不会被原版贴图的色相带偏（王者冲锋 10-16：金色 × 偏青贴图 = 发绿）。
    /// </summary>
    internal static Material StreakMaterialPlain(Color color)
    {
      if (PlainMats.TryGetValue(color, out var cached)) return cached;
      Material mat = null;
      var tpl = Template();
      if (tpl != null)
      {
        try
        {
          mat = new Material(tpl) { name = "RhodesDoctorStreakPlain" };
          Tint(mat, color);
          if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", RibbonTexture());
        }
        catch (Exception e)
        {
          Main.Mod.Logger.Error("[RhodesDoctor] 纯色光带材质生成失败：\n" + e);
          mat = null;
        }
      }
      PlainMats[color] = mat;
      return mat;
    }

    private static Material MaterialFor(Color color)
    {
      if (Mats.TryGetValue(color, out var cached)) return cached;
      Material mat = null;
      var tpl = Template();
      if (tpl != null)
      {
        try
        {
          mat = new Material(tpl) { name = "RhodesDoctorStreak" };  // 克隆模板：混合模式/贴图一起继承
          Tint(mat, color);
          // 只有"内置 shader"这条路需要自己塞软边贴图（它的 _MainTex 默认是纯白，画出来是硬边一条）。
          // 克隆来的原版材质**不要动 _MainTex**：那是原版作者调好的光晕/渐变贴图，换掉反而变丑。
          if (!_templateFromVanilla && mat.HasProperty("_MainTex"))
            mat.SetTexture("_MainTex", RibbonTexture());
        }
        catch (Exception e)
        {
          Main.Mod.Logger.Error("[RhodesDoctor] 金光尾拖材质生成失败：\n" + e);
          mat = null;
        }
      }
      Mats[color] = mat;   // 负缓存：失败也别每次施法都重试一遍
      return mat;
    }

    private static Texture2D _ribbon;

    /// <summary>光带贴图：横跨宽度方向两侧淡出、中间实，做出"一缕光"的软边。生成一次共用。</summary>
    private static Texture2D RibbonTexture()
    {
      if (_ribbon != null) return _ribbon;
      const int w = 8, h = 64;
      var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "RhodesDoctorStreakTex" };
      var px = new Color[w * h];
      for (int y = 0; y < h; y++)
      {
        float v = (y + 0.5f) / h;                 // 0..1：横跨带宽
        float a = Mathf.Sin(v * Mathf.PI);        // 两侧 0、中间 1
        a *= a;                                   // 再收一点，中间更实、边缘更虚
        for (int x = 0; x < w; x++) px[y * w + x] = new Color(1f, 1f, 1f, a);
      }
      tex.SetPixels(px);
      tex.wrapMode = TextureWrapMode.Clamp;
      tex.filterMode = FilterMode.Bilinear;
      tex.Apply(false);
      _ribbon = tex;
      return _ribbon;
    }

    /// <summary>① 克隆原版 FX 材质 → ② 内置 shader。结果缓存，第一行日志会写明用的哪一个。</summary>
    private static Material Template()
    {
      if (_resolved) return _template;
      _resolved = true;

      // ① **首选：克隆一个"确认在游戏里能渲染"的原版 FX 材质**。
      //    判断依据：剑雨上一版借用的那个巨型光柱素材（SwordRainTrail）用户亲眼看到过 → 它的材质
      //    一定是"能上屏的"，shader 也一定在构建里且变体没被裁掉。
      //    而 Shader.Find 那条路已经**两次什么都没画出来**（日志里 shader/材质都正常、宽度也够，
      //    画面上却空无一物），所以不再把它当首选，只留作兜底。
      _template = CloneVanillaFxMaterial();
      if (_template != null)
      {
        _templateFromVanilla = true;
        Log("金光尾拖使用原版克隆材质：'" + _template.name + "' shader=" + _template.shader.name
          + "（isSupported=" + _template.shader.isSupported + "）");
        return _template;
      }

      foreach (var name in BuiltinShaders)
      {
        var sh = Shader.Find(name);
        if (sh == null) continue;
        _template = new Material(sh) { name = "RhodesDoctorStreak" };
        Log("金光尾拖使用内置 shader：" + name + "（isSupported=" + sh.isSupported + "）");
        return _template;
      }
      Log("金光尾拖：找不到可用 shader / 材质（光尾不显示，剑身照常）。");
      return _template;
    }

    /// <summary>
    /// 从一个**确认能渲染**的原版特效里挑一个粒子材质，`new Material(它)` 克隆下来。
    /// 顺序："阳炎之刃"投射物外观（用户亲眼见过它上屏，最可靠）→ "阳炎爆"（圣光·金）。
    /// 只读模板，不实例化那个 prefab。
    /// </summary>
    private static Material CloneVanillaFxMaterial()
    {
      var links = new[]
      {
        SafeLink(() => FxLibrary.SwordRainTrail),
        SafeLink(() => FxLibrary.Holy),
      };
      foreach (var link in links)
      {
        if (link == null) continue;
        try
        {
          if (!link.Exists()) continue;
          var tpl = link.Load(false, true);
          if (tpl == null) continue;
          foreach (var r in tpl.GetComponentsInChildren<ParticleSystemRenderer>(true))
          {
            var src = r.sharedMaterial;
            if (src == null || src.shader == null) continue;
            var n = (src.shader.name ?? "") + " " + (src.name ?? "");
            if (!LooksLikeLight(n)) continue;
            Log("金光尾拖材质来源：'" + src.name + "' shader=" + src.shader.name);
            return new Material(src) { name = "RhodesDoctorStreak" };
          }
        }
        catch (Exception e)
        {
          Main.Mod.Logger.Error("[RhodesDoctor] 金光尾拖克隆材质失败：\n" + e);
        }
      }
      return null;
    }

    private static Kingmaker.ResourceLinks.PrefabLink SafeLink(Func<Kingmaker.ResourceLinks.PrefabLink> get)
    {
      try { return get(); }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 金光尾拖取素材失败：\n" + e);
        return null;
      }
    }

    /// <summary>像"光"的材质：带 particle/additive/glow 之类字样，且不是抓屏/脏烟类。</summary>
    private static bool LooksLikeLight(string s)
    {
      s = s.ToLowerInvariant();
      if (s.Contains("distort") || s.Contains("flow") || s.Contains("noise")
        || s.Contains("smoke") || s.Contains("dust") || s.Contains("fog")) return false;
      return s.Contains("particle") || s.Contains("additive") || s.Contains("glow")
        || s.Contains("flare") || s.Contains("light") || s.Contains("spark") || s.Contains("ray");
    }

    private static void Tint(Material mat, Color c)
    {
      if (mat.HasProperty("_Color")) mat.color = c;
      if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", c);
      if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
    }

    private static void Log(string msg)
    {
      if (_logged) return;
      _logged = true;
      Dbg.Log("[RhodesDoctor] " + msg);
    }

    private static bool _scaleLogged;

    /// <summary>把剑身的世界缩放 / 图层 / 实际宽度打一行日志：只为验证"宽度和图层有没问题"（只打一次）。</summary>
    private static void LogScaleOnce(Vector3 ls, int layer, float width, float seconds)
    {
      if (_scaleLogged) return;
      _scaleLogged = true;
      Dbg.Log("[RhodesDoctor] 金光尾拖：剑身 lossyScale=("
        + ls.x.ToString("F4") + ", " + ls.y.ToString("F4") + ", " + ls.z.ToString("F4")
        + ") layer=" + layer + "，光带宽度=" + width.ToString("F3")
        + " m（已做世界缩放归一，实际就是该值）、时长=" + seconds.ToString("F2") + " s。");
    }
  }
}
