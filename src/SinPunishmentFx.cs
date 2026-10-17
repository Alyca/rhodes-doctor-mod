// 罪罚（6 环·斥罪）的专属自绘特效：审判法槌 + 律法荆棘印章 + 惩戒落槌审判（纯视觉）。
// 严格还原《明日方舟》斥罪（拉维妮娅·法尔科内）作为叙拉古法官的核心武器与技能特色：
// 【审判法槌】（Judge's Gavel）与【律法荆棘】（Thorns of Law）。
//
// 视觉构成：
//   1. 减益常驻期间（SinPunishmentAnim，继承 MarkFxBase）：
//      由 MarkFxTracker 订阅 RhodesDoctorBuffSinPunishment（持续 1 轮/级）；
//      · 头顶正对镜头的【金色正义法槌】：自绘立体的法官长柄与厚重双面平头槌身，
//        悬浮于罪人头顶微微蓄势晃动（律法正义悬顶）；
//      · 脚底平躺的【律法荆棘印章】：金色法庭双环印章，带有叙拉古律法荆棘刻线与四方法庭方标。
//   2. 减益反噬触发时（SinPunishmentSmiteAnim）：
//      当目标造成伤害触发反噬时，空中显现巨大金色法槌以雷霆万钧之势【重重落槌劈砸】！
//      伴随垂直审判圣光柱与地面四散炸裂的金色荆棘冲击波，呈现绝对正义的执法重击。
using System;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Buffs;
using UnityEngine;
using static RhodesDoctor.MarkFxKit;

namespace RhodesDoctor
{
  internal static class SinPunishmentFx
  {
    internal static readonly Color JusticeGold  = new Color(1.00f, 0.82f, 0.20f, 1f); // 叙拉古法官金
    internal static readonly Color RadiantWhite = new Color(1.00f, 0.98f, 0.95f, 1f); // 纯洁律法白
    internal static readonly Color ThornRed     = new Color(0.90f, 0.22f, 0.28f, 1f); // 斥罪荆棘绯红
    internal static readonly Color DeepGold     = new Color(0.85f, 0.60f, 0.12f, 0.70f);

    /// <summary>
    /// 减益常驻跟踪器：按增益名或 GUID 认领。
    /// GUID = Guids.SinPunishmentBuff（具名常量）。
    /// </summary>
    internal static readonly MarkFxTracker Tracker = new MarkFxTracker(
      "RhodesDoctorBuffSinPunishment", "罪罚·审判法槌", go => go.AddComponent<SinPunishmentAnim>(),
      Guids.SinPunishmentBuff);

    /// <summary>
    /// 当罪罚的反噬触发时播放：巨大金色法槌凌空砸落，激荡律法荆棘与审判冲击波！
    /// </summary>
    internal static void PlayRetributionSmite(UnitEntityData target)
    {
      try
      {
        if (target?.View == null) return;
        var go = new GameObject("RD_SinPunishmentSmite") { layer = target.View.gameObject.layer };
        var anim = go.AddComponent<SinPunishmentSmiteAnim>();
        if (!anim.Init(target)) UnityEngine.Object.Destroy(go);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 罪罚法槌惩戒特效失败（效果照常）：\n" + e);
      }
    }
  }

  /// <summary>
  /// 罪罚减益常驻特效（跟增益同生同灭）：
  ///   · 头顶立体审判法槌（<see cref="SinGavelRig"/>）：以柄端为支点做"缓抬 → 急落 → 余震"的庄严蓄势节拍（周期 3 s）；
  ///   · 脚下叙拉古法庭律法荆棘印章：金 / 白双环 + 两圈反向缓旋、齿尖斜挑的交错荆棘
  ///     + 内接方印与四正向法庭刻度；每次落槌印章微微一震并荡出一道白环。
  /// </summary>
  public class SinPunishmentAnim : MarkFxBase
  {
    private const int ThornN = 14;

    private SinGavelRig _gavel;
    private LineRenderer _sealOuter, _sealInner, _sealSquare, _thornA, _thornB, _tapRing;
    private readonly LineRenderer[] _ticks = new LineRenderer[4];

    protected override bool Build(Buff buff)
    {
      var mG = Mat(SinPunishmentFx.JusticeGold);
      var mW = Mat(SinPunishmentFx.RadiantWhite);
      var mR = Mat(SinPunishmentFx.ThornRed);
      var mD = Mat(SinPunishmentFx.DeepGold);
      if (mG == null || mW == null || mR == null || mD == null) return false;

      _gavel = new SinGavelRig(transform, "RD_SP_Gavel", mG, mW, mD, 1f);

      _sealOuter  = Flat(transform, "RD_SP_SealOut", mG, RingSeg,    true, 0.050f);
      _sealInner  = Flat(transform, "RD_SP_SealIn",  mW, RingSeg,    true, 0.030f);
      _sealSquare = Flat(transform, "RD_SP_SealSq",  mG, 4,          true, 0.030f);
      _thornA     = Flat(transform, "RD_SP_ThornA",  mR, ThornN * 2, true, 0.040f);
      _thornB     = Flat(transform, "RD_SP_ThornB",  mG, ThornN * 2, true, 0.030f);
      _tapRing    = Flat(transform, "RD_SP_TapRing", mW, RingSeg,    true, 0.060f);
      for (int i = 0; i < 4; i++)
        _ticks[i] = Flat(transform, "RD_SP_Tick" + i, mW, 2, false, 0.035f);

      return true;
    }

    protected override void Tick(float dt)
    {
      CamBasis(out var right, out var up);
      float appear = Mathf.Clamp01(T / 0.25f) * Master;
      float settle = SinFxMath.EaseOut3(Mathf.Clamp01(T / 0.45f)); // 入场：法槌自上方压下、印章盖下
      Cadence(T, out float lift, out float tap);

      // ════ 1. 头顶审判法槌：以柄端为支点，缓抬蓄势 → 急落 → 余震 ════
      float theta = 0.30f + 0.22f * lift;
      var v = -right * Mathf.Sin(theta) + up * Mathf.Cos(theta);
      var grip = Unit.Position + Vector3.up * (Height + 1.25f + 0.6f * (1f - settle) - 0.05f * tap);
      _gavel.Draw(grip - v * SinGavelRig.GripToHead, theta, 1f, right, up, appear, tap);

      // ════ 2. 脚下律法荆棘印章 ════
      var g = Unit.Position;
      float stamp = Mathf.Lerp(1.25f, 1f, settle) * (1f + 0.06f * tap);
      float rOut = Radius * 1.25f * stamp, rIn = Radius * 0.88f * stamp;
      float spin = T * 0.08f;
      float sealA = appear * (0.80f + 0.20f * tap);

      Circle(_sealOuter, g, rOut, 0.05f);
      SetA(_sealOuter, SinPunishmentFx.JusticeGold, sealA * 0.90f);
      Circle(_sealInner, g, rIn, 0.06f);
      SetA(_sealInner, SinPunishmentFx.RadiantWhite, sealA * 0.70f);

      // 外圈荆棘：两圈反向缓旋，长短交替、齿尖朝相反方向斜挑 → 锐利交错
      SinFxMath.Thorns(_thornA, g, rOut, ThornN, spin, 0.24f, 0.35f, 0.055f);
      SinFxMath.Thorns(_thornB, g, rOut, ThornN, -spin * 1.3f + Mathf.PI / ThornN, 0.15f, -0.30f, 0.065f);
      SetA(_thornA, SinPunishmentFx.ThornRed, sealA * 0.90f);
      SetA(_thornB, SinPunishmentFx.JusticeGold, sealA * 0.60f);

      // 内圈：四方法庭刻度——内接方印 + 四正向刻线
      float q = -spin * 0.5f;
      for (int i = 0; i < 4; i++)
      {
        float a = q + Mathf.PI * 0.25f + i * Mathf.PI * 0.5f;
        _sealSquare.SetPosition(i, g + new Vector3(Mathf.Cos(a) * rIn * 0.80f, 0.07f, Mathf.Sin(a) * rIn * 0.80f));
        float b = q + i * Mathf.PI * 0.5f;
        var d = new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b));
        _ticks[i].SetPosition(0, g + d * (rIn * 0.40f) + Vector3.up * 0.07f);
        _ticks[i].SetPosition(1, g + d * (rIn * 0.96f) + Vector3.up * 0.07f);
        SetA(_ticks[i], SinPunishmentFx.RadiantWhite, sealA * 0.85f);
      }
      SetA(_sealSquare, SinPunishmentFx.JusticeGold, sealA * 0.75f);

      // 落槌回响：每次落槌自内环荡出一道白环
      Circle(_tapRing, g, Mathf.Lerp(rIn, rOut * 1.45f, 1f - tap), 0.08f);
      SetA(_tapRing, SinPunishmentFx.RadiantWhite, appear * tap * 0.80f);
    }

    /// <summary>庄严蓄势节拍（周期 3 s）：缓抬 1.65 s（smoothstep）→ 急落 0.21 s（二次加速）→ 余震回弹；tap = 落槌后的闪亮衰减。</summary>
    private static void Cadence(float t, out float lift, out float tap)
    {
      lift = 0f; tap = 0f;
      const float start = 0.45f, period = 3.0f;
      if (t < start) return;
      float p = Mathf.Repeat(t - start, period) / period;
      if (p < 0.55f) lift = SinFxMath.Smooth(p / 0.55f);
      else if (p < 0.62f) { float k = (p - 0.55f) / 0.07f; lift = 1f - k * k; }
      else
      {
        float s = (p - 0.62f) * period; // 落槌后经过的秒数
        lift = -0.12f * Mathf.Exp(-s * 6f) * Mathf.Sin(s * 26f);
        tap = Mathf.Exp(-s * 6f);
      }
    }
  }

  /// <summary>
  /// 正对镜头的立体审判法槌（常驻与落槌共用，按 scale 缩放）。
  /// 局部坐标：u = 槌头轴向，v = 木柄轴向（槌头 → 柄端）。
  /// 构成：倒角八边形槌身前后两层（沿固定"光向"错开 → 厚度）+ 两端椭圆平头敲击面 + 两道槌箍 + 顶棱高光
  ///       + 锥形实心木柄 + 柄颈铜箍 + 柄端圆头。
  /// </summary>
  internal sealed class SinGavelRig
  {
    internal const float HeadHalfLen = 0.24f;  // 槌头半长
    internal const float HeadHalfH   = 0.10f;  // 槌头半高
    internal const float HandleLen   = 0.62f;
    internal const float GripToHead  = HeadHalfH + HandleLen + 0.04f; // 柄端圆头中心 → 槌头中心
    private const int FacePts = 16;

    private readonly LineRenderer _front, _back, _faceL, _faceR, _band1, _band2, _shine, _handle, _collar, _pommel;

    internal SinGavelRig(Transform parent, string tag, Material gold, Material white, Material deep, float scale)
    {
      _back   = Line(parent, tag + "_Back",   deep,  8,       true,  0.040f * scale);
      _handle = Line(parent, tag + "_Handle", deep,  2,       false, 0.060f * scale);
      _front  = Line(parent, tag + "_Front",  gold,  8,       true,  0.055f * scale);
      _faceL  = Line(parent, tag + "_FaceL",  white, FacePts, true,  0.040f * scale);
      _faceR  = Line(parent, tag + "_FaceR",  white, FacePts, true,  0.040f * scale);
      _band1  = Line(parent, tag + "_Band1",  gold,  2,       false, 0.045f * scale);
      _band2  = Line(parent, tag + "_Band2",  gold,  2,       false, 0.045f * scale);
      _shine  = Line(parent, tag + "_Shine",  white, 2,       false, 0.022f * scale);
      _collar = Line(parent, tag + "_Collar", gold,  2,       false, 0.050f * scale);
      _pommel = Line(parent, tag + "_Pommel", gold,  12,      true,  0.040f * scale);
      _handle.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.70f)); // 槌头端粗、柄端细
    }

    /// <param name="head">槌头中心</param>
    /// <param name="theta">倾角：0 = 木柄竖直朝上；π/2 = 木柄水平朝左、槌头竖立（下端面朝下）</param>
    /// <param name="flash">敲击面闪亮（0~1）</param>
    internal void Draw(Vector3 head, float theta, float scale, Vector3 right, Vector3 up, float alpha, float flash)
    {
      var u = right * Mathf.Cos(theta) + up * Mathf.Sin(theta);
      var v = -right * Mathf.Sin(theta) + up * Mathf.Cos(theta);
      float hl = HeadHalfLen * scale, hr = HeadHalfH * scale, ch = 0.03f * scale;
      Vector3 P(float a, float b) => head + u * a + v * b;

      // 槌身：倒角八边形前层 + 错层后层（厚度感）
      Oct(_front, head, u, v, hl, hr, ch, Vector3.zero);
      Oct(_back,  head, u, v, hl, hr, ch, (-right * 0.035f + up * 0.030f) * scale);

      // 两端平头敲击面：透视下的窄椭圆
      for (int i = 0; i < FacePts; i++)
      {
        float a = i * Mathf.PI * 2f / FacePts;
        float cu = Mathf.Cos(a) * 0.035f * scale, cv = Mathf.Sin(a) * hr * 0.92f;
        _faceL.SetPosition(i, P(-hl - 0.012f * scale + cu, cv));
        _faceR.SetPosition(i, P( hl + 0.012f * scale + cu, cv));
      }

      // 两道槌箍（略凸出槌身）+ 顶棱高光
      float bandU = 0.075f * scale, bandV = hr + 0.015f * scale;
      _band1.SetPosition(0, P(-bandU, -bandV)); _band1.SetPosition(1, P(-bandU, bandV));
      _band2.SetPosition(0, P( bandU, -bandV)); _band2.SetPosition(1, P( bandU, bandV));
      _shine.SetPosition(0, P(-hl + ch * 1.5f, hr * 0.55f));
      _shine.SetPosition(1, P( hl - ch * 1.5f, hr * 0.55f));

      // 木柄 + 柄颈铜箍 + 柄端圆头
      _handle.SetPosition(0, P(0f, hr + 0.02f * scale));
      _handle.SetPosition(1, P(0f, hr + HandleLen * scale));
      _collar.SetPosition(0, P(-0.045f * scale, hr + 0.06f * scale));
      _collar.SetPosition(1, P( 0.045f * scale, hr + 0.06f * scale));
      BillCircle(_pommel, P(0f, GripToHead * scale), 0.04f * scale, right, up);

      SetA(_back,   SinPunishmentFx.DeepGold,     alpha * 0.55f);
      SetA(_handle, SinPunishmentFx.DeepGold,     alpha * 0.95f);
      SetA(_front,  SinPunishmentFx.JusticeGold,  alpha);
      SetA(_band1,  SinPunishmentFx.JusticeGold,  alpha);
      SetA(_band2,  SinPunishmentFx.JusticeGold,  alpha);
      SetA(_collar, SinPunishmentFx.JusticeGold,  alpha * 0.90f);
      SetA(_pommel, SinPunishmentFx.JusticeGold,  alpha * 0.90f);
      SetA(_faceL,  SinPunishmentFx.RadiantWhite, alpha * (0.70f + 0.30f * flash));
      SetA(_faceR,  SinPunishmentFx.RadiantWhite, alpha * (0.70f + 0.30f * flash));
      SetA(_shine,  SinPunishmentFx.RadiantWhite, alpha * (0.55f + 0.45f * flash));
    }

    private static void Oct(LineRenderer lr, Vector3 c, Vector3 u, Vector3 v, float hl, float hr, float ch, Vector3 off)
    {
      c += off;
      lr.SetPosition(0, c + u * (-hl + ch) + v * hr);
      lr.SetPosition(1, c + u * ( hl - ch) + v * hr);
      lr.SetPosition(2, c + u * hl + v * ( hr - ch));
      lr.SetPosition(3, c + u * hl + v * (-hr + ch));
      lr.SetPosition(4, c + u * ( hl - ch) - v * hr);
      lr.SetPosition(5, c + u * (-hl + ch) - v * hr);
      lr.SetPosition(6, c - u * hl + v * (-hr + ch));
      lr.SetPosition(7, c - u * hl + v * ( hr - ch));
    }
  }

  internal static class SinFxMath
  {
    internal static float Smooth(float x) => x * x * (3f - 2f * x);
    internal static float EaseOut3(float x) { float k = 1f - x; return 1f - k * k * k; }

    /// <summary>平躺荆棘环：n 根刺，基点在半径 r 上，刺尖向外伸出 len（长短交替），并沿周向斜挑 skew 个刺距。</summary>
    internal static void Thorns(LineRenderer lr, Vector3 c, float r, int n, float phase, float len, float skew, float y)
    {
      float step = Mathf.PI * 2f / n;
      for (int i = 0; i < n; i++)
      {
        float a0 = phase + i * step;
        float tipR = r * (1f + len * (i % 2 == 0 ? 1f : 0.6f));
        float aTip = a0 + step * (0.5f + skew);
        lr.SetPosition(2 * i,     c + new Vector3(Mathf.Cos(a0) * r, y, Mathf.Sin(a0) * r));
        lr.SetPosition(2 * i + 1, c + new Vector3(Mathf.Cos(aTip) * tipR, y, Mathf.Sin(aTip) * tipR));
      }
    }
  }

  /// <summary>
  /// 罪罚反噬一瞬（约 1.05 秒）：九天落槌重砸。
  ///   0.00–0.22 s：目标上方 4.5 m 显现 3 倍巨型金槌，后仰蓄满；
  ///   0.22–0.34 s：三次方急加速抡下（带破空弧光），同时整体从天而降，下端面砸在罪人头顶；
  ///   0.34 s 起：砸实震颤并压入半寸 → 法庭圣光柱爆宽后收束、正对镜头白色爆闪、
  ///              叙拉古法印 1.35 倍盖下压实、金 / 绯红两道冲击波与十枚带倒钩的金色荆棘四散飞射。
  /// </summary>
  public class SinPunishmentSmiteAnim : MonoBehaviour
  {
    private const float Dur = 1.05f;
    private const float TWind = 0.22f;   // 显现 + 后仰蓄力结束
    private const float THit = 0.34f;    // 落槌触地
    private const float Scale = 3.0f;
    private const float SkyLift = 4.5f;
    private const float WindTheta = Mathf.PI + 0.35f;
    private const int ShardCount = 10;
    private const int ArcPts = 20;

    private UnitEntityData _target;
    private float _t, _h;
    private SinGavelRig _gavel;
    private LineRenderer _arc, _beamOuter, _beamCore, _flash, _shock1, _shock2, _sealOuter, _sealInner, _sealSquare;
    private readonly LineRenderer[] _shards = new LineRenderer[ShardCount];
    private readonly float[] _shardAngle = new float[ShardCount];
    private readonly float[] _shardReach = new float[ShardCount];

    internal bool Init(UnitEntityData target)
    {
      var mG = Mat(SinPunishmentFx.JusticeGold);
      var mW = Mat(SinPunishmentFx.RadiantWhite);
      var mR = Mat(SinPunishmentFx.ThornRed);
      var mD = Mat(SinPunishmentFx.DeepGold);
      if (mG == null || mW == null || mR == null || mD == null) return false;

      _target = target;
      _h = ModelHeight(target);

      _gavel     = new SinGavelRig(transform, "RD_SP_BigGavel", mG, mW, mD, Scale);
      _arc       = Line(transform, "RD_SP_SwingArc",  mG, ArcPts, false, 0.35f);
      _arc.widthCurve = new AnimationCurve(new Keyframe(0f, 0.1f), new Keyframe(1f, 1f));
      _beamOuter = Line(transform, "RD_SP_BeamOuter", mG, 2, false, 1f);
      _beamOuter.widthCurve = new AnimationCurve(new Keyframe(0f, 0.45f), new Keyframe(1f, 1f)); // 天端细、地端宽
      _beamCore  = Line(transform, "RD_SP_BeamCore",  mW, 2, false, 1f);
      _flash     = Line(transform, "RD_SP_Flash",     mW, RingSeg, true, 0.12f);
      _shock1    = Flat(transform, "RD_SP_Shock1",     mG, RingSeg, true, 0.16f);
      _shock2    = Flat(transform, "RD_SP_Shock2",     mR, RingSeg, true, 0.10f);
      _sealOuter = Flat(transform, "RD_SP_ImpSealOut", mG, RingSeg, true, 0.07f);
      _sealInner = Flat(transform, "RD_SP_ImpSealIn",  mW, RingSeg, true, 0.04f);
      _sealSquare = Flat(transform, "RD_SP_ImpSealSq", mG, 4, true, 0.05f);
      for (int i = 0; i < ShardCount; i++)
      {
        _shards[i] = Flat(transform, "RD_SP_Shard" + i, (i & 1) == 0 ? mG : mR, 3, false, 0.07f);
        _shardAngle[i] = i * Mathf.PI * 2f / ShardCount + 0.18f * Mathf.Sin(i * 12.9898f); // 均布 + 固定抖动
        _shardReach[i] = 2.6f + 0.25f * ((i * 7) % 5);                                     // 2.6–3.6 m 长短参差
      }
      return true;
    }

    private void Update()
    {
      _t += Time.deltaTime;
      if (_t >= Dur || _target == null || _target.View == null) { Destroy(gameObject); return; }
      try { Draw(); }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 罪罚落槌特效更新失败（已移除）：\n" + e);
        Destroy(gameObject);
      }
    }

    private void Draw()
    {
      CamBasis(out var right, out var up);
      var pos = _target.Position;
      float since = _t - THit;                                  // 触地后经过的秒数（< 0 = 尚未落槌）
      float L = SinGavelRig.GripToHead * Scale;
      var strike = pos + Vector3.up * (_h * 0.95f);             // 下端面砸落点：罪人头顶
      var headHit = strike + up * (SinGavelRig.HeadHalfLen * Scale);
      var gripRest = headHit - right * L;                       // θ = π/2 时槌头恰在 headHit

      // ── 1. 九天巨槌：显现蓄力 → 三次方急加速抡下 → 砸实震颤 ──
      float theta, sky, gavelA;
      if (_t < TWind)
      {
        float k = SinFxMath.Smooth(_t / TWind);
        theta = Mathf.Lerp(Mathf.PI + 0.10f, WindTheta, k);   // 后仰再拉满
        sky = SkyLift;
        gavelA = k;
      }
      else if (since < 0f)
      {
        float k = (_t - TWind) / (THit - TWind);
        float acc = k * k * k;                                  // 急加速
        theta = Mathf.Lerp(WindTheta, Mathf.PI * 0.5f - 0.10f, acc);
        sky = Mathf.Lerp(SkyLift, 0f, acc);
        gavelA = 1f;
      }
      else
      {
        theta = Mathf.PI * 0.5f - 0.10f * Mathf.Exp(-since * 9f) * Mathf.Cos(since * 32f);
        sky = -0.10f * (1f - Mathf.Exp(-since * 12f));          // 压入半寸
        gavelA = 1f - SinFxMath.Smooth(Mathf.Clamp01((since - 0.12f) / 0.40f));
      }
      var grip = gripRest + Vector3.up * sky;
      var vAxis = -right * Mathf.Sin(theta) + up * Mathf.Cos(theta);
      float flash = since >= 0f ? Mathf.Exp(-since * 8f) : 0f;
      _gavel.Draw(grip - vAxis * L, theta, Scale, right, up, gavelA, flash);

      // 破空弧光：槌头掠过的弧（落槌期间亮起，触地后 0.15 s 消散）
      float arcA = _t < TWind ? 0f : 0.85f * (since < 0f ? 1f : 1f - Mathf.Clamp01(since / 0.15f));
      _arc.enabled = arcA > 0f;
      if (_arc.enabled)
      {
        for (int i = 0; i < ArcPts; i++)
        {
          float th = Mathf.Lerp(WindTheta, theta, i / (float)(ArcPts - 1));
          var vi = -right * Mathf.Sin(th) + up * Mathf.Cos(th);
          _arc.SetPosition(i, grip - vi * L);
        }
        var ac = SinPunishmentFx.JusticeGold;
        _arc.startColor = new Color(ac.r, ac.g, ac.b, 0f);
        _arc.endColor   = new Color(ac.r, ac.g, ac.b, arcA);
      }

      // ── 2. 贯穿天地的法庭圣光柱：落槌前一线引光，触地瞬间爆宽，随后收束 ──
      float beamW, beamA;
      if (since < 0f)
      {
        beamW = 0.06f;
        beamA = 0.35f * Mathf.Clamp01((_t - 0.12f) / (THit - 0.12f));
      }
      else
      {
        beamW = 0.10f + 1.10f * Mathf.Exp(-since * 5f);
        beamA = 1f - SinFxMath.Smooth(Mathf.Clamp01(since / (Dur - THit)));
      }
      Beam(_beamOuter, pos, beamW, SinPunishmentFx.JusticeGold, beamA * 0.85f);
      Beam(_beamCore,  pos, beamW * 0.35f, SinPunishmentFx.RadiantWhite, beamA);

      // ── 3. 触地爆闪（正对镜头） ──
      bool flashOn = since >= 0f && since < 0.25f;
      _flash.enabled = flashOn;
      if (flashOn)
      {
        float k = since / 0.25f;
        BillCircle(_flash, strike, Mathf.Lerp(0.4f, 2.4f, SinFxMath.EaseOut3(k)), right, up);
        SetA(_flash, SinPunishmentFx.RadiantWhite, (1f - k) * 0.95f);
      }

      // ── 4. 地面：法印砸印 + 两道冲击波 + 四散金色荆棘 ──
      bool hit = since >= 0f;
      _shock1.enabled = _sealOuter.enabled = _sealInner.enabled = _sealSquare.enabled = hit;
      _shock2.enabled = since > 0.08f;
      for (int i = 0; i < ShardCount; i++) _shards[i].enabled = hit;
      if (!hit) return;

      float life = Mathf.Clamp01(since / (Dur - THit));
      float k1 = Mathf.Clamp01(since / 0.45f);
      Circle(_shock1, pos, Mathf.Lerp(0.3f, 3.6f, SinFxMath.EaseOut3(k1)), 0.08f);
      SetA(_shock1, SinPunishmentFx.JusticeGold, (1f - k1) * 0.95f);
      float k2 = Mathf.Clamp01((since - 0.08f) / 0.50f);
      Circle(_shock2, pos, Mathf.Lerp(0.2f, 2.6f, SinFxMath.EaseOut3(k2)), 0.10f);
      SetA(_shock2, SinPunishmentFx.ThornRed, (1f - k2) * 0.80f);

      // 叙拉古法印：触地瞬间以 1.35 倍盖下、0.12 s 压实到 1 倍，随后淡去
      float stamp = Mathf.Lerp(1.35f, 1f, SinFxMath.EaseOut3(Mathf.Clamp01(since / 0.12f)));
      float rS = 1.6f * stamp, sealA = (1f - life) * 0.90f;
      Circle(_sealOuter, pos, rS, 0.06f);
      Circle(_sealInner, pos, rS * 0.72f, 0.07f);
      for (int i = 0; i < 4; i++)
      {
        float a = Mathf.PI * 0.25f + i * Mathf.PI * 0.5f;
        _sealSquare.SetPosition(i, pos + new Vector3(Mathf.Cos(a) * rS * 0.62f, 0.07f, Mathf.Sin(a) * rS * 0.62f));
      }
      SetA(_sealOuter,  SinPunishmentFx.JusticeGold,  sealA);
      SetA(_sealInner,  SinPunishmentFx.RadiantWhite, sealA * 0.80f);
      SetA(_sealSquare, SinPunishmentFx.JusticeGold,  sealA * 0.80f);

      // 金色荆棘碎刺：带倒钩的尖刺沿放射方向急速飞散（快出慢停），金 / 绯红交替
      float ks = SinFxMath.EaseOut3(Mathf.Clamp01(since / 0.40f));
      float shardA = 1f - SinFxMath.Smooth(Mathf.Clamp01((since - 0.15f) / 0.45f));
      var lift = Vector3.up * 0.12f;
      for (int i = 0; i < ShardCount; i++)
      {
        var d = new Vector3(Mathf.Cos(_shardAngle[i]), 0f, Mathf.Sin(_shardAngle[i]));
        var side = new Vector3(-d.z, 0f, d.x) * ((i & 1) == 0 ? 1f : -1f);
        float tip = Mathf.Lerp(0.4f, _shardReach[i], ks);
        float len = 0.20f + 0.55f * (1f - 0.5f * ks);
        _shards[i].SetPosition(0, pos + lift + d * (tip - len));
        _shards[i].SetPosition(1, pos + lift + d * tip);
        _shards[i].SetPosition(2, pos + lift + d * (tip - len * 0.45f) + side * (len * 0.30f));
        SetA(_shards[i], (i & 1) == 0 ? SinPunishmentFx.JusticeGold : SinPunishmentFx.ThornRed, shardA);
      }
    }

    private static void Beam(LineRenderer lr, Vector3 ground, float width, Color c, float a)
    {
      lr.SetPosition(0, ground + Vector3.up * 14f);
      lr.SetPosition(1, ground + Vector3.up * 0.03f);
      lr.widthMultiplier = width;
      SetA(lr, c, a);
    }
  }}
