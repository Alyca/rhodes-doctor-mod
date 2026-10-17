// 归溟 / 潮（7 环·归溟幽灵鲨/劳伦缇娜）专属自绘视觉特效（纯视觉，不影响数值结算）。
//
// 对应干员：【归溟幽灵鲨】（Specter the Unchained，劳伦缇娜 Laurentina）——阿戈尔深海猎人二期工程荣光，死灵学派：
//   · 核心意象：深海洋流漩涡、狂暴大洋暗流光带、深海浪涌与生命同调逆流回溯、怒涛冲击环。
//
// 视觉体系（四位一体）：
//   1. 【迅捷施法瞬间（ReturnToAbyssCastAnim）】：
//      施法一瞬脚下深海潮汐螺旋升腾炸开（必须体现真实潮汐喷薄与破浪水流厚度）。
//   2. 【增益常驻跟随（ReturnToAbyssAnim，继承 MarkFxBase，持续 1 轮/级）】：
//      由 MarkFxTracker 订阅 RhodesDoctorBuffReturnToAbyss（Guids.ReturnToAbyssBuff）；
//      脚底自旋呼吸的深海洋流漩涡；周身螺旋缠绕升腾的大洋暗流光带与悬浮浪沫水粒；0.3s 平滑淡出。
//   3. 【近战命中吸血（ReturnToAbyssHealAnim）】：
//      命中吸血 2d6 时，汹涌水瀑与深海暗涌自目标处逆流回流施法者。
//   4. 【暴击/击杀续期恶灵（ReturnToAbyssRefundAnim）】：
//      每轮首暴/首杀返还 1 轮恶灵形态时，深海怒涛暴涨，荡开一道排山倒海的炽白浪沫冲击环！
//
// 渲染技术规范：
//   · 色彩体系：深渊海蓝（AbyssalBlue）、苍翠洋流绿（OceanicTeal）、炽白浪沫（WaveFoamWhite）、深海暗涌（DeepOceanViolet）。
//   · 加法材质下禁止使用完全透空的纯黑，保证 45° 俯视远景下清晰完整。
//   · 材质统一复用 MarkFxKit.Mat（底模 SwordStreakFx.StreakMaterialPlain），严禁 Shader.Find 与原版 PrefabLink。
//   · 零运行时 GC，组件自管理生命周期与超时 Destroy。
//   · 锚点防角色模型深度遮挡，线宽与尺度适配信仰 45° 俯视镜头。
//
// 【CRITICAL MANDATE — 作者实测反馈（2026-10-17）】：
//   上一版因线条感过重、缺乏水与潮汐的厚度与流体质感而被全盘否定。
//   本文件已重置为骨架，全权交由 Macroscope 重新设计并输出具备真实流体厚度、潮涌起伏与浪花翻滚质感的自绘代码。
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
  /// 迅捷施法瞬间，脚下深海潮汐地脉喷薄爆发，三道厚重水柱摇曳升腾，双层螺旋洋流与破浪白沫环向外推开。
  /// </summary>
  public class ReturnToAbyssCastAnim : MonoBehaviour
  {
    private const float Dur = 0.70f;
    private const int Cols = 3, ColN = 24, Spir = 2, SpN = 36;
    private float _t, _h = 2f, _r = 0.8f;
    private UnitEntityData _caster;
    private readonly LineRenderer[] _cols = new LineRenderer[Cols];
    private readonly LineRenderer[] _spir = new LineRenderer[Spir];
    private LineRenderer _surf, _foam;

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
      var violet = Mat(ReturnToAbyssFx.DeepOceanViolet);
      var geyser = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.6f, 0.75f), new Keyframe(1f, 0.15f));
      var swell = new AnimationCurve(new Keyframe(0f, 0.15f), new Keyframe(0.4f, 1f), new Keyframe(1f, 0.2f));
      for (int i = 0; i < Cols; i++)
      {
        _cols[i] = Line(transform, "Column" + i, i == 0 ? violet : (i == 1 ? blue : teal), ColN, false, 0.5f - i * 0.08f);
        _cols[i].widthCurve = geyser;
      }
      for (int i = 0; i < Spir; i++)
      {
        _spir[i] = Line(transform, "Spiral" + i, i == 0 ? teal : blue, SpN, false, 0.3f);
        _spir[i].widthCurve = swell;
      }
      _surf = Flat(transform, "Surf", blue, RingSeg, true, 0.45f);
      _foam = Flat(transform, "Foam", Mat(ReturnToAbyssFx.WaveFoamWhite), RingSeg, true, 0.25f);
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
      float e = 1f - (1f - k) * (1f - k) * (1f - k);
      float a = k < 0.55f ? 1f : 1f - (k - 0.55f) / 0.45f;
      Vector3 c = _caster.Position;
      transform.position = c;
      float top = _h * 1.5f * Mathf.Sin(Mathf.Min(k * 1.6f, 1f) * Mathf.PI * 0.5f);
      for (int i = 0; i < Cols; i++)
      {
        var lr = _cols[i];
        float ox = Mathf.Cos(i * 2.094f) * _r * 0.25f, oz = Mathf.Sin(i * 2.094f) * _r * 0.25f;
        for (int j = 0; j < ColN; j++)
        {
          float u = j / (float)(ColN - 1);
          float sway = 0.12f * Mathf.Sin(u * 7f - _t * 14f + i * 2f);
          lr.SetPosition(j, c + new Vector3(ox * (1f + u) + sway, 0.05f + u * top * (1f - 0.15f * i), oz * (1f + u) - sway));
        }
        lr.widthMultiplier = (0.5f - i * 0.08f) * (0.85f + 0.25f * Mathf.Sin(_t * 18f + i));
        SetA(lr, i == 0 ? ReturnToAbyssFx.DeepOceanViolet : (i == 1 ? ReturnToAbyssFx.AbyssalBlue : ReturnToAbyssFx.OceanicTeal), a * 0.75f);
      }
      for (int i = 0; i < Spir; i++)
      {
        var lr = _spir[i];
        float ph = _t * 8f + i * Mathf.PI;
        for (int j = 0; j < SpN; j++)
        {
          float u = j / (float)(SpN - 1);
          float rad = _r * (0.4f + 0.8f * u) * (0.6f + 0.5f * e);
          float ang = ph + u * Mathf.PI * 3f;
          lr.SetPosition(j, c + new Vector3(Mathf.Cos(ang) * rad, 0.05f + u * top * 0.9f, Mathf.Sin(ang) * rad));
        }
        SetA(lr, i == 0 ? ReturnToAbyssFx.OceanicTeal : ReturnToAbyssFx.AbyssalBlue, a * 0.7f);
      }
      float rr = _r * (0.4f + 1.6f * e);
      Circle(_surf, c, rr, 0.05f);
      Circle(_foam, c, rr * 1.04f, 0.08f + 0.06f * Mathf.Sin(k * Mathf.PI));
      _surf.widthMultiplier = 0.45f * (1f - 0.5f * k);
      SetA(_surf, ReturnToAbyssFx.AbyssalBlue, a * 0.8f);
      SetA(_foam, ReturnToAbyssFx.WaveFoamWhite, a);
    }
  }

  /// <summary>
  /// 归溟大洋流常驻动画（ReturnToAbyssAnim，继承 MarkFxBase）：
  /// 增益持续期间（1 轮/级）跟随施法者：
  ///   · 脚底自旋呼吸的深海洋流漩涡；
  ///   · 周身螺旋缠绕升腾的大洋暗流与悬浮浪沫水粒；
  ///   · 到期或被驱散后 0.3s 平滑淡出。
  /// 【作者实测反馈】：必须体现真实流体厚度、水流密度与潮汐起伏，绝不可只有单线几何，全权交由 Macroscope 重构。
  /// </summary>
  public class ReturnToAbyssAnim : MarkFxBase
  {
    private const int Sheets = 3, SheetN = 36, Ribbons = 4, RibN = 48, Drops = 14;
    private readonly LineRenderer[] _sheets = new LineRenderer[Sheets];
    private readonly LineRenderer[] _ribbons = new LineRenderer[Ribbons];
    private readonly LineRenderer[] _drops = new LineRenderer[Drops];
    private readonly float[] _seed = new float[Drops];
    private static readonly Color[] RibCol = { ReturnToAbyssFx.AbyssalBlue, ReturnToAbyssFx.OceanicTeal, ReturnToAbyssFx.DeepOceanViolet, ReturnToAbyssFx.AbyssalBlue };

    protected override bool Build(Buff buff)
    {
      var blue = Mat(ReturnToAbyssFx.AbyssalBlue);
      if (blue == null) return false;
      var teal = Mat(ReturnToAbyssFx.OceanicTeal);
      var violet = Mat(ReturnToAbyssFx.DeepOceanViolet);
      var foam = Mat(ReturnToAbyssFx.WaveFoamWhite);
      var swell = new AnimationCurve(new Keyframe(0f, 0.15f), new Keyframe(0.35f, 1f), new Keyframe(0.7f, 0.8f), new Keyframe(1f, 0.1f));
      for (int i = 0; i < Sheets; i++)
      {
        _sheets[i] = Flat(transform, "Whirl" + i, i == 0 ? violet : (i == 1 ? blue : teal), SheetN, false, 0.45f - i * 0.1f);
        _sheets[i].widthCurve = swell;
      }
      for (int i = 0; i < Ribbons; i++)
      {
        _ribbons[i] = Line(transform, "Current" + i, i == 2 ? violet : (i == 1 ? teal : blue), RibN, false, 0.32f);
        _ribbons[i].widthCurve = swell;
      }
      for (int i = 0; i < Drops; i++)
      {
        _drops[i] = Line(transform, "Foam" + i, foam, 2, false, 0.14f);
        _seed[i] = i * 0.6180339f;
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
      float tide = Mathf.Sin(T * 1.8f);               // tidal surge / ebb
      float r = Radius * (1f + 0.15f * tide);

      for (int s = 0; s < Sheets; s++)
      {
        var lr = _sheets[s];
        float spin = T * (1.6f + s * 0.7f) + s * 2.1f;
        for (int j = 0; j < SheetN; j++)
        {
          float u = j / (float)(SheetN - 1);
          float rad = r * (1.25f - s * 0.25f) * (0.2f + 0.8f * u);
          float ang = spin + u * Mathf.PI * 2.2f;
          float y = 0.04f + s * 0.03f + 0.05f * Mathf.Sin(u * 10f - T * 5f);
          lr.SetPosition(j, c + new Vector3(Mathf.Cos(ang) * rad, y, Mathf.Sin(ang) * rad));
        }
        lr.widthMultiplier = (0.45f - s * 0.1f) * (1f + 0.3f * tide);
        SetA(lr, s == 0 ? ReturnToAbyssFx.DeepOceanViolet : (s == 1 ? ReturnToAbyssFx.AbyssalBlue : ReturnToAbyssFx.OceanicTeal), a * (0.55f + 0.15f * s));
      }

      float h = Height * (1.0f + 0.12f * tide);
      for (int i = 0; i < Ribbons; i++)
      {
        var lr = _ribbons[i];
        float ph = -T * 2.0f + i * Mathf.PI * 0.5f;
        for (int j = 0; j < RibN; j++)
        {
          float u = j / (float)(RibN - 1);
          float wave = 0.12f * Mathf.Sin(u * 8f - T * 6f + i) + 0.05f * Mathf.Sin(u * 21f + T * 9f);
          float rad = r * 0.8f * (1f - 0.3f * u) + wave;
          float ang = ph + u * Mathf.PI * 2.6f;
          lr.SetPosition(j, c + new Vector3(Mathf.Cos(ang) * rad, 0.1f + u * h + wave * 0.5f, Mathf.Sin(ang) * rad));
        }
        lr.widthMultiplier = 0.32f * (0.8f + 0.35f * Mathf.Sin(T * 3f + i * 1.7f));
        SetA(lr, RibCol[i], a * 0.7f);
      }

      for (int i = 0; i < Drops; i++)
      {
        float p = Mathf.Repeat(T * 0.55f + _seed[i], 1f);
        float ang = _seed[i] * 17f - T * 1.5f;
        float rad = r * (0.7f + 0.35f * p);
        // ballistic splash arc: rise then fall
        var pos = c + new Vector3(Mathf.Cos(ang) * rad, 0.1f + h * 0.6f * 4f * p * (1f - p), Mathf.Sin(ang) * rad);
        _drops[i].SetPosition(0, pos);
        _drops[i].SetPosition(1, pos + new Vector3(0f, 0.12f * (1f - 2f * p), 0f));
        SetA(_drops[i], ReturnToAbyssFx.WaveFoamWhite, a * Mathf.Sin(p * Mathf.PI));
      }
    }
  }

  /// <summary>
  /// 归溟命中吸血逆流动画（ReturnToAbyssHealAnim）：
  /// 近战命中吸血时，三层厚重水瀑（主洋流 + 逆浪底层 + 浪峰白沫）自目标汹涌逆溯奔流至施法者，涌入时在施法者脚底炸开破浪水花。
  /// </summary>
  public class ReturnToAbyssHealAnim : MonoBehaviour
  {
    private const float Dur = 0.60f;
    private const int Layers = 3, N = 32;
    private float _t, _h = 2f;
    private UnitEntityData _source;
    private UnitEntityData _caster;
    private Vector3 _from;
    private readonly LineRenderer[] _body = new LineRenderer[Layers];
    private LineRenderer _splash;

    internal bool Init(UnitEntityData source, UnitEntityData caster)
    {
      if (caster?.View == null) return false;
      var blue = Mat(ReturnToAbyssFx.AbyssalBlue);
      if (blue == null) return false;
      _source = source;
      _caster = caster;
      _h = ModelHeight(caster);
      _from = (source?.View != null ? source.Position : caster.Position) + Vector3.up * (_h * 0.5f);
      transform.position = _from;
      var torrent = new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.3f, 1f), new Keyframe(0.75f, 0.9f), new Keyframe(1f, 0.35f));
      _body[0] = Line(transform, "Torrent", blue, N, false, 0.5f);
      _body[1] = Line(transform, "Undertow", Mat(ReturnToAbyssFx.OceanicTeal), N, false, 0.35f);
      _body[2] = Line(transform, "Crest", Mat(ReturnToAbyssFx.WaveFoamWhite), N, false, 0.25f);
      for (int i = 0; i < Layers; i++) _body[i].widthCurve = torrent;
      _splash = Flat(transform, "Splash", Mat(ReturnToAbyssFx.WaveFoamWhite), RingSeg, true, 0.3f);
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
      if (_source?.View != null) _from = _source.Position + Vector3.up * (_h * 0.5f);
      Vector3 to = _caster.Position + Vector3.up * (_h * 0.5f);
      Vector3 d = to - _from;
      Vector3 side = Vector3.Cross(d, Vector3.up);
      side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.right;
      float k = _t / Dur;
      float head = Mathf.Clamp01(k * 1.6f); head = 1f - (1f - head) * (1f - head);
      float tail = Mathf.Clamp01((k - 0.35f) / 0.65f);
      float a = k < 0.7f ? 1f : 1f - (k - 0.7f) / 0.3f;
      for (int i = 0; i < Layers; i++)
      {
        var lr = _body[i];
        for (int j = 0; j < N; j++)
        {
          float u = Mathf.Lerp(tail, head, j / (float)(N - 1));
          float arc = Mathf.Sin(u * Mathf.PI);
          float churn = 0.18f * Mathf.Sin(u * 10f - _t * 22f + i * 1.3f) * arc;
          float lift = 0.5f * arc + 0.08f * i + 0.06f * Mathf.Sin(u * 15f + _t * 18f);
          lr.SetPosition(j, _from + d * u + side * churn + Vector3.up * lift);
        }
        lr.widthMultiplier = (i == 0 ? 0.5f : (i == 1 ? 0.35f : 0.25f)) * (0.85f + 0.2f * Mathf.Sin(_t * 25f + i));
        SetA(lr, i == 0 ? ReturnToAbyssFx.AbyssalBlue : (i == 1 ? ReturnToAbyssFx.OceanicTeal : ReturnToAbyssFx.WaveFoamWhite), a * (i == 2 ? 0.8f : 0.7f));
      }
      float sp = Mathf.Clamp01((k - 0.5f) / 0.5f);
      Circle(_splash, _caster.Position, 0.4f + 1.1f * (1f - (1f - sp) * (1f - sp)), 0.06f);
      _splash.widthMultiplier = 0.3f * (1f - sp);
      SetA(_splash, ReturnToAbyssFx.WaveFoamWhite, sp > 0f ? 1f - sp : 0f);
    }
  }

  /// <summary>
  /// 归溟恶灵形态续期怒涛冲击环动画（ReturnToAbyssRefundAnim）：
  /// 每轮首暴或首杀返还 1 轮恶灵持续时间时，三重巨大海啸涌浪（0.5m 宽）层层排山倒海扩张，浪头翻卷着炽白浪唇并留下一大片浩瀚海潮水幕。
  /// </summary>
  public class ReturnToAbyssRefundAnim : MonoBehaviour
  {
    private const float Dur = 0.80f;
    private const int Waves = 3, N = 64;
    private float _t, _r = 0.8f;
    private UnitEntityData _caster;
    private readonly LineRenderer[] _waves = new LineRenderer[Waves];
    private readonly LineRenderer[] _lips = new LineRenderer[Waves];
    private LineRenderer _wash;

    internal bool Init(UnitEntityData caster)
    {
      if (caster?.View == null) return false;
      var foam = Mat(ReturnToAbyssFx.WaveFoamWhite);
      if (foam == null) return false;
      _caster = caster;
      transform.position = caster.Position;
      _r = Mathf.Clamp(caster.View.Corpulence * 1.6f, 0.7f, 3.0f);
      var blue = Mat(ReturnToAbyssFx.AbyssalBlue);
      var teal = Mat(ReturnToAbyssFx.OceanicTeal);
      var violet = Mat(ReturnToAbyssFx.DeepOceanViolet);
      for (int i = 0; i < Waves; i++)
      {
        _waves[i] = Line(transform, "Bore" + i, i == 0 ? blue : (i == 1 ? teal : violet), N, true, 0.5f - i * 0.08f);
        _lips[i] = Line(transform, "Lip" + i, foam, N, true, 0.25f);
      }
      _wash = Flat(transform, "Wash", teal, RingSeg, true, 0.5f);
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
      Vector3 c = _caster.Position;
      transform.position = c;
      for (int i = 0; i < Waves; i++)
      {
        float kk = Mathf.Clamp01((k - i * 0.12f) / 0.76f);
        float e = 1f - (1f - kk) * (1f - kk) * (1f - kk);
        float r = _r * (0.4f + 3.2f * e);
        float crest = 0.55f * Mathf.Sin(Mathf.Min(kk * 1.4f, 1f) * Mathf.PI) * (1f - 0.25f * i);
        float a = kk <= 0f ? 0f : 1f - kk * kk;
        var w = _waves[i]; var lip = _lips[i];
        for (int j = 0; j < N; j++)
        {
          float ang = j * Mathf.PI * 2f / N;
          float rip = 0.1f * Mathf.Sin(ang * 7f + _t * 12f + i) + 0.05f * Mathf.Sin(ang * 13f - _t * 9f);
          float rr = r + rip;
          float y = 0.05f + crest * (0.7f + 0.3f * Mathf.Sin(ang * 5f - _t * 10f));
          float cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
          w.SetPosition(j, c + new Vector3(cs * rr, y * 0.5f, sn * rr));
          lip.SetPosition(j, c + new Vector3(cs * (rr + 0.08f), y, sn * (rr + 0.08f)));
        }
        w.widthMultiplier = (0.5f - i * 0.08f) * (1f - 0.4f * kk);
        lip.widthMultiplier = 0.25f * (1f - 0.5f * kk);
        SetA(w, i == 0 ? ReturnToAbyssFx.AbyssalBlue : (i == 1 ? ReturnToAbyssFx.OceanicTeal : ReturnToAbyssFx.DeepOceanViolet), a * 0.75f);
        SetA(lip, ReturnToAbyssFx.WaveFoamWhite, a);
      }
      float we = Mathf.Clamp01((k - 0.25f) / 0.75f);
      Circle(_wash, c, _r * (0.6f + 2.4f * we), 0.04f);
      _wash.widthMultiplier = 0.5f * (1f - we);
      SetA(_wash, ReturnToAbyssFx.OceanicTeal, we > 0f ? 0.6f * (1f - we) : 0f);
    }
  }
}
