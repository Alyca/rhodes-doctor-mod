// 归溟 / 潮（7 环·归溟幽灵鲨/劳伦缇娜）专属自绘视觉特效（纯视觉，不影响数值结算）。
//
// 对应干员：【归溟幽灵鲨】（Specter the Unchained，劳伦缇娜 Laurentina）——阿戈尔深海猎人二期工程荣光，死灵学派：
//   · 核心意象：深海洋流漩涡、狂暴大洋暗流光带、深海浪涌与生命同调逆流回溯、怒涛冲击环。
//
// 视觉体系（四位一体）：
//   1. 【迅捷施法瞬间（ReturnToAbyssCastAnim）】：
//      施法一瞬脚下深海潮汐螺旋升腾炸开。
//   2. 【增益常驻跟随（ReturnToAbyssAnim，继承 MarkFxBase，持续 1 轮/级）】：
//      由 MarkFxTracker 订阅 RhodesDoctorBuffReturnToAbyss（Guids.ReturnToAbyssBuff）；
//      脚底自旋呼吸的深海洋流漩涡；周身螺旋缠绕升腾的大洋暗流光带与悬浮浪沫水粒；0.3s 平滑淡出。
//   3. 【近战命中吸血（ReturnToAbyssHealAnim）】：
//      命中吸血 2d6 时，幽蓝浪涌自目标处逆流回流施法者。
//   4. 【暴击/击杀续期恶灵（ReturnToAbyssRefundAnim）】：
//      每轮首暴/首杀返还 1 轮恶灵形态时，深海怒涛暴涨，荡开一道炽白浪沫冲击环！
//
// 渲染技术规范：
//   · 色彩体系：深渊海蓝（AbyssalBlue）、苍翠洋流绿（OceanicTeal）、炽白浪沫（WaveFoamWhite）、深海暗涌（DeepOceanViolet）。
//   · 加法材质下禁止使用完全透空的纯黑，保证 45° 俯视远景下清晰完整。
//   · 材质统一复用 MarkFxKit.Mat（底模 SwordStreakFx.StreakMaterialPlain），严禁 Shader.Find 与原版 PrefabLink。
//   · 零运行时 GC，组件自管理生命周期与超时 Destroy。
//   · 锚点防角色模型深度遮挡，线宽与尺度适配信仰 45° 俯视镜头。
//
// 注：所有具体视觉几何、粒子物理与缓动算法由 Macroscope 自主设计与交付。
using System;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Buffs;
using UnityEngine;
using static RhodesDoctor.MarkFxKit;

namespace RhodesDoctor
{
  internal static class ReturnToAbyssFx
  {
    // ── 阿戈尔深海猎人·调色盘 ──
    // 加法材质下颜色越暗越透明，禁止使用纯黑以免完全透空。
    internal static readonly Color AbyssalBlue      = new Color(0.12f, 0.45f, 0.95f, 1f); // 深渊海蓝·主调狂暴洋流
    internal static readonly Color OceanicTeal     = new Color(0.15f, 0.85f, 0.75f, 1f); // 苍翠洋流绿·生命同调光晕
    internal static readonly Color WaveFoamWhite    = new Color(0.92f, 0.98f, 1.00f, 1f); // 炽白浪沫·高光水粒与怒涛波芯
    internal static readonly Color DeepOceanViolet  = new Color(0.38f, 0.18f, 0.75f, 1f); // 深海暗涌·外层暗流卷带

    /// <summary>
    /// 归溟常驻跟踪器：由 MarkFxWatcher 订阅 RhodesDoctorBuffReturnToAbyss。
    /// 施法者身上洋流缠身（脚底漩涡 + 周身螺旋洋流光带 + 浪沫水粒）全程跟随。
    /// </summary>
    internal static readonly MarkFxTracker Tracker = new MarkFxTracker(
      "RhodesDoctorBuffReturnToAbyss", "归溟·潮（大洋流）", go => go.AddComponent<ReturnToAbyssAnim>(),
      Guids.ReturnToAbyssBuff);

    /// <summary>
    /// 迅捷施法瞬间：脚下深海潮汐螺旋升腾炸开。
    /// </summary>
    internal static void PlayCast(UnitEntityData caster)
    {
      try
      {
        if (caster?.View == null) return;
        var go = new GameObject("RD_ReturnToAbyssCast") { layer = caster.View.gameObject.layer };
        var anim = go.AddComponent<ReturnToAbyssCastAnim>();
        if (!anim.Init(caster)) UnityEngine.Object.Destroy(go);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 归溟施法潮汐特效失败（逻辑照常）：\n" + e);
      }
    }

    /// <summary>
    /// 近战命中吸血：幽蓝浪涌自目标处逆流回流施法者。
    /// </summary>
    internal static void PlayHeal(UnitEntityData source, UnitEntityData caster)
    {
      try
      {
        if (caster?.View == null) return;
        var go = new GameObject("RD_ReturnToAbyssHeal") { layer = caster.View.gameObject.layer };
        var anim = go.AddComponent<ReturnToAbyssHealAnim>();
        if (!anim.Init(source, caster)) UnityEngine.Object.Destroy(go);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 归溟命中吸血回流特效失败（逻辑照常）：\n" + e);
      }
    }

    /// <summary>
    /// 每轮首暴/首杀续期恶灵：深海怒涛暴涨，荡开一道炽白浪沫冲击环！
    /// </summary>
    internal static void PlayRefund(UnitEntityData caster)
    {
      try
      {
        if (caster?.View == null) return;
        var go = new GameObject("RD_ReturnToAbyssRefund") { layer = caster.View.gameObject.layer };
        var anim = go.AddComponent<ReturnToAbyssRefundAnim>();
        if (!anim.Init(caster)) UnityEngine.Object.Destroy(go);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 归溟恶灵返还怒涛冲击特效失败（逻辑照常）：\n" + e);
      }
    }
  }

  /// <summary>
  /// 归溟施法潮汐升腾动画（ReturnToAbyssCastAnim）：
  /// 迅捷施法瞬间，脚下深海潮汐三道洋流螺旋升腾并炸开炽白浪沫光环。
  /// </summary>
  public class ReturnToAbyssCastAnim : MonoBehaviour
  {
    private const float Dur = 0.70f;
    private const int Arms = 3, SpN = 32;
    private float _t, _h = 2f, _r = 0.8f;
    private UnitEntityData _caster;
    private readonly LineRenderer[] _spirals = new LineRenderer[Arms];
    private LineRenderer _ring, _ringCore;

    internal bool Init(UnitEntityData caster)
    {
      if (caster?.View == null) return false;
      var blue = Mat(ReturnToAbyssFx.AbyssalBlue);
      if (blue == null) return false;
      _caster = caster;
      transform.position = caster.Position;
      _h = ModelHeight(caster);
      _r = Mathf.Clamp(caster.View.Corpulence * 1.6f, 0.7f, 3.0f);
      var teal = Mat(ReturnToAbyssFx.OceanicTeal);
      for (int i = 0; i < Arms; i++)
        _spirals[i] = Line(transform, "Spiral" + i, i == 1 ? teal : blue, SpN, false, 0.14f);
      _ring = Flat(transform, "Ring", blue, RingSeg, true, 0.22f);
      _ringCore = Flat(transform, "RingCore", Mat(ReturnToAbyssFx.WaveFoamWhite), RingSeg, true, 0.08f);
      return true;
    }

    private void Update()
    {
      _t += Time.deltaTime;
      if (_t >= Dur || _caster == null || _caster.View == null)
      {
        Destroy(gameObject);
        return;
      }
      float k = _t / Dur;
      float e = 1f - (1f - k) * (1f - k) * (1f - k); // EaseOut3
      float a = k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f;
      Vector3 c = _caster.Position;
      transform.position = c;
      float top = _h * 1.3f * e, spin = _t * 9f;
      for (int i = 0; i < Arms; i++)
      {
        var lr = _spirals[i];
        float ph = spin + i * Mathf.PI * 2f / Arms;
        for (int j = 0; j < SpN; j++)
        {
          float u = j / (float)(SpN - 1);
          float rad = _r * (0.35f + 0.9f * u) * (0.6f + 0.6f * e);
          float ang = ph + u * Mathf.PI * 3f;
          lr.SetPosition(j, c + new Vector3(Mathf.Cos(ang) * rad, 0.05f + u * top, Mathf.Sin(ang) * rad));
        }
        SetA(lr, i == 1 ? ReturnToAbyssFx.OceanicTeal : ReturnToAbyssFx.AbyssalBlue, a);
      }
      float rr = _r * (0.4f + 1.4f * e);
      Circle(_ring, c, rr, 0.06f);
      Circle(_ringCore, c, rr, 0.08f);
      SetA(_ring, ReturnToAbyssFx.AbyssalBlue, a * 0.9f);
      SetA(_ringCore, ReturnToAbyssFx.WaveFoamWhite, a);
    }
  }

  /// <summary>
  /// 归溟大洋流常驻动画（ReturnToAbyssAnim，继承 MarkFxBase）：
  /// 增益持续期间（1 轮/级）跟随施法者：
  ///   · 脚底自旋呼吸的深海洋流漩涡（4 臂洋流 + 双层呼吸漩涡环）；
  ///   · 周身螺旋缠绕升腾的大洋暗流光带（3 条暗流）与悬浮浪沫水粒（10 粒波沫）；
  ///   · 到期或被驱散后 0.3s 平滑淡出，零运行时 GC。
  /// </summary>
  public class ReturnToAbyssAnim : MarkFxBase
  {
    private const int Ribbons = 3, RibN = 40, Drops = 10, VortexArms = 4, VortexN = 28;
    private LineRenderer _vortexRing, _vortexOuter;
    private readonly LineRenderer[] _vortex = new LineRenderer[VortexArms];
    private readonly LineRenderer[] _ribbons = new LineRenderer[Ribbons];
    private readonly LineRenderer[] _drops = new LineRenderer[Drops];
    private readonly float[] _dropSeed = new float[Drops];

    protected override bool Build(Buff buff)
    {
      var blue = Mat(ReturnToAbyssFx.AbyssalBlue);
      if (blue == null) return false;
      var teal = Mat(ReturnToAbyssFx.OceanicTeal);
      var foam = Mat(ReturnToAbyssFx.WaveFoamWhite);
      var violet = Mat(ReturnToAbyssFx.DeepOceanViolet);
      _vortexRing = Flat(transform, "VortexRing", teal, RingSeg, true, 0.12f);
      _vortexOuter = Flat(transform, "VortexOuter", violet, RingSeg, true, 0.18f);
      for (int i = 0; i < VortexArms; i++)
        _vortex[i] = Flat(transform, "VortexArm" + i, i % 2 == 0 ? blue : teal, VortexN, false, 0.11f);
      for (int i = 0; i < Ribbons; i++)
        _ribbons[i] = Line(transform, "Ribbon" + i, i == 2 ? violet : (i == 0 ? blue : teal), RibN, false, 0.13f);
      for (int i = 0; i < Drops; i++)
      {
        _drops[i] = Line(transform, "Foam" + i, foam, 2, false, 0.09f);
        _dropSeed[i] = i * 0.6180339f;
      }
      return true;
    }

    protected override void Tick(float dt)
    {
      Vector3 c = Unit.Position;
      transform.position = c;
      float intro = Mathf.Clamp01(T / 0.35f);
      intro = 1f - (1f - intro) * (1f - intro) * (1f - intro);
      float a = Master * intro;
      float r = Radius * (1f + 0.12f * Mathf.Sin(T * 3.2f)); // breathing

      Circle(_vortexRing, c, r * 0.95f, 0.05f);
      Circle(_vortexOuter, c, r * 1.15f, 0.04f);
      SetA(_vortexRing, ReturnToAbyssFx.OceanicTeal, a * 0.85f);
      SetA(_vortexOuter, ReturnToAbyssFx.DeepOceanViolet, a * 0.75f);

      float spin = T * 2.4f;
      for (int i = 0; i < VortexArms; i++)
      {
        var lr = _vortex[i];
        float ph = spin + i * Mathf.PI * 0.5f;
        for (int j = 0; j < VortexN; j++)
        {
          float u = j / (float)(VortexN - 1);
          float rad = r * (0.15f + 0.85f * u);
          float ang = ph + u * Mathf.PI * 1.6f;
          lr.SetPosition(j, c + new Vector3(Mathf.Cos(ang) * rad, 0.06f, Mathf.Sin(ang) * rad));
        }
        SetA(lr, i % 2 == 0 ? ReturnToAbyssFx.AbyssalBlue : ReturnToAbyssFx.OceanicTeal, a * 0.9f);
      }

      float h = Height * 1.05f;
      for (int i = 0; i < Ribbons; i++)
      {
        var lr = _ribbons[i];
        float ph = -T * 2.2f + i * Mathf.PI * 2f / Ribbons;
        for (int j = 0; j < RibN; j++)
        {
          float u = j / (float)(RibN - 1);
          float rad = r * 0.75f * (1f - 0.35f * u) + 0.06f * Mathf.Sin(T * 4f + u * 9f);
          float ang = ph + u * Mathf.PI * 2.5f;
          lr.SetPosition(j, c + new Vector3(Mathf.Cos(ang) * rad, 0.1f + u * h, Mathf.Sin(ang) * rad));
        }
        SetA(lr, i == 2 ? ReturnToAbyssFx.DeepOceanViolet : (i == 0 ? ReturnToAbyssFx.AbyssalBlue : ReturnToAbyssFx.OceanicTeal), a * 0.8f);
      }

      for (int i = 0; i < Drops; i++)
      {
        float p = Mathf.Repeat(T * 0.4f + _dropSeed[i], 1f);
        float ang = _dropSeed[i] * 17f - T * 1.5f;
        float rad = r * (0.6f + 0.3f * Mathf.Sin(_dropSeed[i] * 11f));
        var pos = c + new Vector3(Mathf.Cos(ang) * rad, 0.15f + p * h, Mathf.Sin(ang) * rad);
        _drops[i].SetPosition(0, pos);
        _drops[i].SetPosition(1, pos + Vector3.up * 0.14f);
        SetA(_drops[i], ReturnToAbyssFx.WaveFoamWhite, a * Mathf.Sin(p * Mathf.PI));
      }
    }
  }

  /// <summary>
  /// 归溟命中吸血逆流动画（ReturnToAbyssHealAnim）：
  /// 近战命中造成伤害并吸血时，三道波动幽蓝洋流自目标处逆流回流施法者，施法者身上泛起浪沫吸收环。
  /// </summary>
  public class ReturnToAbyssHealAnim : MonoBehaviour
  {
    private const float Dur = 0.60f;
    private const int Streams = 3, N = 24;
    private float _t, _h = 2f;
    private UnitEntityData _source;
    private UnitEntityData _caster;
    private Vector3 _from;
    private readonly LineRenderer[] _streams = new LineRenderer[Streams];
    private LineRenderer _absorb;

    internal bool Init(UnitEntityData source, UnitEntityData caster)
    {
      if (caster?.View == null) return false;
      var blue = Mat(ReturnToAbyssFx.AbyssalBlue);
      if (blue == null) return false;
      _source = source;
      _caster = caster;
      _h = ModelHeight(caster);
      _from = (source?.View != null ? source.Position : caster.Position) + Vector3.up * (_h * 0.55f);
      transform.position = _from;
      var teal = Mat(ReturnToAbyssFx.OceanicTeal);
      for (int i = 0; i < Streams; i++)
        _streams[i] = Line(transform, "Stream" + i, i == 1 ? teal : blue, N, false, i == 1 ? 0.10f : 0.15f);
      _absorb = Flat(transform, "Absorb", Mat(ReturnToAbyssFx.WaveFoamWhite), RingSeg, true, 0.1f);
      return true;
    }

    private void Update()
    {
      _t += Time.deltaTime;
      if (_t >= Dur || _caster == null || _caster.View == null)
      {
        Destroy(gameObject);
        return;
      }
      if (_source?.View != null) _from = _source.Position + Vector3.up * (_h * 0.55f);
      Vector3 to = _caster.Position + Vector3.up * (_h * 0.55f);
      Vector3 d = to - _from;
      Vector3 side = Vector3.Cross(d, Vector3.up);
      side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.right;
      float k = _t / Dur;
      float head = Mathf.Clamp01(k * 1.5f); head *= head; // EaseInQuad
      float tail = Mathf.Clamp01(head - 0.5f + k * 0.5f);
      float a = k < 0.75f ? 1f : 1f - (k - 0.75f) / 0.25f;
      for (int i = 0; i < Streams; i++)
      {
        var lr = _streams[i];
        float off = (i - 1) * 0.35f;
        for (int j = 0; j < N; j++)
        {
          float u = Mathf.Lerp(tail, head, j / (float)(N - 1));
          float bulge = Mathf.Sin(u * Mathf.PI);
          float wave = Mathf.Sin(u * 12f - _t * 20f + i * 2f) * 0.12f * bulge;
          lr.SetPosition(j, _from + d * u + side * (off * bulge + wave) + Vector3.up * (0.6f * bulge));
        }
        SetA(lr, i == 1 ? ReturnToAbyssFx.OceanicTeal : ReturnToAbyssFx.AbyssalBlue, a);
      }
      float ab = Mathf.Clamp01((k - 0.45f) / 0.55f);
      float r = 0.4f + 0.9f * (1f - (1f - ab) * (1f - ab));
      Circle(_absorb, _caster.Position, r, 0.07f);
      SetA(_absorb, ReturnToAbyssFx.WaveFoamWhite, ab > 0f ? (1f - ab) : 0f);
    }
  }

  /// <summary>
  /// 归溟恶灵形态续期怒涛冲击环动画（ReturnToAbyssRefundAnim）：
  /// 每轮首暴或首杀返还 1 轮恶灵持续时间时，深海怒涛暴涨，白热核心环与深蓝外环激荡扩展，10 道洋流浪冠升腾。
  /// </summary>
  public class ReturnToAbyssRefundAnim : MonoBehaviour
  {
    private const float Dur = 0.80f;
    private const int Crests = 10;
    private float _t, _r = 0.8f;
    private UnitEntityData _caster;
    private LineRenderer _core, _outer, _trail;
    private readonly LineRenderer[] _crests = new LineRenderer[Crests];

    internal bool Init(UnitEntityData caster)
    {
      if (caster?.View == null) return false;
      var foam = Mat(ReturnToAbyssFx.WaveFoamWhite);
      if (foam == null) return false;
      _caster = caster;
      transform.position = caster.Position;
      _r = Mathf.Clamp(caster.View.Corpulence * 1.6f, 0.7f, 3.0f);
      _core = Flat(transform, "Core", foam, RingSeg, true, 0.28f);
      _outer = Flat(transform, "Outer", Mat(ReturnToAbyssFx.AbyssalBlue), RingSeg, true, 0.45f);
      _trail = Flat(transform, "Trail", Mat(ReturnToAbyssFx.DeepOceanViolet), RingSeg, true, 0.3f);
      var teal = Mat(ReturnToAbyssFx.OceanicTeal);
      for (int i = 0; i < Crests; i++)
        _crests[i] = Line(transform, "Crest" + i, teal, 2, false, 0.12f);
      return true;
    }

    private void Update()
    {
      _t += Time.deltaTime;
      if (_t >= Dur || _caster == null || _caster.View == null)
      {
        Destroy(gameObject);
        return;
      }
      float k = _t / Dur;
      float e = 1f - (1f - k) * (1f - k) * (1f - k); // EaseOut3
      float a = 1f - k * k;
      Vector3 c = _caster.Position;
      transform.position = c;
      float r = _r * (0.5f + 3.0f * e);
      Circle(_core, c, r, 0.08f);
      Circle(_outer, c, r * 1.06f, 0.06f);
      Circle(_trail, c, r * 0.82f, 0.05f);
      SetA(_core, ReturnToAbyssFx.WaveFoamWhite, a);
      SetA(_outer, ReturnToAbyssFx.AbyssalBlue, a * 0.85f);
      SetA(_trail, ReturnToAbyssFx.DeepOceanViolet, a * 0.7f);
      float crestH = 1.1f * Mathf.Sin(Mathf.Min(k * 2.2f, 1f) * Mathf.PI) + 0.2f;
      for (int i = 0; i < Crests; i++)
      {
        float ang = i * Mathf.PI * 2f / Crests + k * 1.2f;
        var p = c + new Vector3(Mathf.Cos(ang) * r, 0.06f, Mathf.Sin(ang) * r);
        _crests[i].SetPosition(0, p);
        _crests[i].SetPosition(1, p + Vector3.up * crestH);
        SetA(_crests[i], ReturnToAbyssFx.OceanicTeal, a);
      }
    }
  }
}
