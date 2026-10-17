// 梦魇（4 环·夜魔/格罗莉亚）专属自绘视觉特效（纯视觉，不影响数值结算）。
//
// 对应干员：【夜魔】（Nightmare，格罗莉亚 Gloria）——罗德岛 5 星术师干员，罕见的双重人格感染者：
//   · 白天/主人格·格罗莉亚：温驯柔弱，白蓝光辉；
//   · 黑夜/里人格·夜魔：狂暴残忍，深紫幽暗梦魇。
// 技能对应：二技能【夜魇魔影】（Nightmare's Shadow）——在敌人脚下与周身植入如影随形的梦魇恶咒。
//
// 视觉体系（三位一体）：
//   1. 【施法植入（Cast / Mind Shock）】：
//      施法一瞬（NightterrorCastAnim），心智冲击波贯穿目标头部，双重人格微裂痕闪烁。
//   2. 【意志豁免失败 · 恐慌逃跑 2 轮（NightterrorPanicAnim，继承 MarkFxBase）】：
//      目标陷入极度恐慌仓皇逃离时（持续 2 轮），由 MarkFxTracker 订阅 RhodesDoctorBuffNightterrorPanic；
//      头顶盘旋浮现狂乱扭曲的【夜魇魔面/恐惧之眼】，脚底泛起深紫幽暗梦魇漩涡，
//      身周数道暗影怨灵之触在周围疯狂撕扯纠缠；到期或驱散后平滑淡出。
//   3. 【意志豁免成功 · 战栗 1 轮（NightterrorPassAnim）】：
//      意志豁免通过时（NightterrorPassAnim），仅在胸口与周身泛起微弱的幽蓝震颤波纹与轻度心智微光，迅速敛去。
//
// 渲染技术规范：
//   · 材质统一复用 MarkFxKit.Mat（底模 SwordStreakFx.StreakMaterialPlain），严禁 Shader.Find 与原版 PrefabLink。
//   · 零运行时 GC，数组/缓冲区在 Init/Build 中预分配，Tick/Update 不做 new 操作。
//   · 锚点防角色模型深度遮挡，线宽与尺度适配信仰 45° 俯视镜头。
//
// 注：基础脚手架已就绪，所有具体视觉几何、粒子物理与缓动算法全权交由 Macroscope 在 PR Check-Run 中自主完成。
using System;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Buffs;
using UnityEngine;
using static RhodesDoctor.MarkFxKit;

namespace RhodesDoctor
{
  internal static class NightterrorFx
  {
    // ── 夜魔双重人格·调色盘 ──
    // 加法材质下颜色越暗越透明，禁止使用纯黑以免完全透空。
    internal static readonly Color NightmarePurple = new Color(0.68f, 0.22f, 0.95f, 1f); // 夜魔里人格·幽魅深紫
    internal static readonly Color GloriaBlue      = new Color(0.42f, 0.75f, 1.00f, 1f); // 格罗莉亚主人格·冰蓝/温柔光辉
    internal static readonly Color MindWhite       = new Color(0.95f, 0.95f, 1.00f, 1f); // 心智裂光·高光内芯
    internal static readonly Color ShadowViolet    = new Color(0.40f, 0.12f, 0.65f, 1f); // 幽暗梦魇底色·残影外晕

    /// <summary>
    /// 恐慌状态（2 轮）常驻跟踪器：由 MarkFxWatcher 订阅 RhodesDoctorBuffNightterrorPanic。
    /// 目标仓皇逃跑时，夜魇魔影（头顶魔面/魔眼 + 脚下梦魇漩涡 + 怨灵触手）全程跟随。
    /// </summary>
    internal static readonly MarkFxTracker PanicTracker = new MarkFxTracker(
      "RhodesDoctorBuffNightterrorPanic", "梦魇·夜魇魔影（恐慌）", go => go.AddComponent<NightterrorPanicAnim>(),
      Guids.NightterrorPanicBuff);

    /// <summary>
    /// 施法一瞬：心智冲击波贯穿目标头部，双重人格微裂痕闪烁。
    /// </summary>
    internal static void PlayCast(UnitEntityData caster, UnitEntityData target)
    {
      try
      {
        if (target?.View == null) return;
        var go = new GameObject("RD_NightterrorCast") { layer = target.View.gameObject.layer };
        var anim = go.AddComponent<NightterrorCastAnim>();
        if (!anim.Init(caster, target)) UnityEngine.Object.Destroy(go);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 梦魇施法冲击特效失败（逻辑照常）：\n" + e);
      }
    }

    /// <summary>
    /// 意志豁免失败：即刻恐惧冲击（可选瞬发爆发，配合 PanicTracker 常驻跟随）。
    /// </summary>
    internal static void PlayFail(UnitEntityData target)
    {
      try
      {
        if (target?.View == null) return;
        // 预留给 Macroscope 根据需要扩展瞬时爆发
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 梦魇豁免失败爆发特效失败（逻辑照常）：\n" + e);
      }
    }

    /// <summary>
    /// 意志豁免成功：心智震颤顿挫与轻度幽蓝余波（持续约 0.8s 迅速敛去）。
    /// </summary>
    internal static void PlayPass(UnitEntityData target)
    {
      try
      {
        if (target?.View == null) return;
        var go = new GameObject("RD_NightterrorPass") { layer = target.View.gameObject.layer };
        var anim = go.AddComponent<NightterrorPassAnim>();
        if (!anim.Init(target)) UnityEngine.Object.Destroy(go);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 梦魇豁免成功震颤特效失败（逻辑照常）：\n" + e);
      }
    }
  }

  /// <summary>
  /// 梦魇施法冲击动画（NightterrorCastAnim）：
  /// 施法命中一瞬，自施法者向目标/在目标头部爆发心智惊骇冲击与双色微裂隙。
  /// </summary>
  public class NightterrorCastAnim : MonoBehaviour
  {
    private const float Dur = 0.60f;
    private const int CrackN = 6, CrackPts = 5;
    private static readonly Color P = NightterrorFx.NightmarePurple, B = NightterrorFx.GloriaBlue,
      W = NightterrorFx.MindWhite, V = NightterrorFx.ShadowViolet;
    private float _t, _h = 2f, _casterH = 1.8f;
    private UnitEntityData _target, _caster;
    private LineRenderer _beam, _beamCore, _halo, _ringP, _ringB, _flash;
    private readonly LineRenderer[] _crack = new LineRenderer[CrackN];
    private readonly float[] _crackAng = new float[CrackN];
    private readonly float[] _crackLen = new float[CrackN];
    private readonly float[] _jit = new float[CrackN * CrackPts];

    internal bool Init(UnitEntityData caster, UnitEntityData target)
    {
      if (target?.View == null) return false;
      var mP = Mat(P); var mB = Mat(B); var mW = Mat(W); var mV = Mat(V);
      if (mP == null || mB == null || mW == null || mV == null)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 梦魇施法冲击放弃：StreakMaterialPlain 无可用材质。");
        return false;
      }
      _target = target;
      _h = ModelHeight(target);
      if (caster?.View != null && caster != target) { _caster = caster; _casterH = ModelHeight(caster); }
      transform.position = target.Position + Vector3.up * (_h * 0.9f);

      _beam     = Line(transform, "RD_NT_Beam", mP, 2, false, 0.22f);
      _beamCore = Line(transform, "RD_NT_BeamCore", mW, 2, false, 0.07f);
      _halo     = Line(transform, "RD_NT_Halo", mV, 48, true, 0.20f);
      _ringP    = Line(transform, "RD_NT_RingP", mP, 48, true, 0.12f);
      _ringB    = Line(transform, "RD_NT_RingB", mB, 48, true, 0.09f);
      _flash    = Line(transform, "RD_NT_Flash", mW, 24, true, 0.10f);
      for (int i = 0; i < CrackN; i++)
      {
        _crack[i] = Line(transform, "RD_NT_Crack", (i & 1) == 0 ? mP : mB, CrackPts, false, 0.06f);
        _crackAng[i] = (i + UnityEngine.Random.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / CrackN;
        _crackLen[i] = UnityEngine.Random.Range(0.55f, 0.95f);
        for (int p = 0; p < CrackPts; p++)
          _jit[i * CrackPts + p] = p == 0 ? 0f : UnityEngine.Random.Range(-0.18f, 0.18f);
      }
      return true;
    }

    private static float Out3(float x) { x = 1f - Mathf.Clamp01(x); return 1f - x * x * x; }

    private void Update()
    {
      _t += Time.deltaTime;
      if (_t >= Dur || _target == null || _target.View == null)
      {
        Destroy(gameObject);
        return;
      }
      try
      {
        float k = _t / Dur;
        float fade = 1f - k * k;
        CamBasis(out var right, out var up);
        var head = _target.Position + Vector3.up * (_h * 0.9f);
        transform.position = head;

        // ① 贯穿：施法者头部 → 目标头部 → 穿出 0.8 m（0.12 s 射穿，之后 0.3 s 收束）
        var from = _caster != null && _caster.View != null
          ? _caster.Position + Vector3.up * (_casterH * 0.85f)
          : head + Vector3.up * 3f;
        var dir = head - from;
        float len = dir.magnitude;
        dir = len > 1e-3f ? dir / len : Vector3.down;
        var tip = from + dir * ((len + 0.8f) * Out3(_t / 0.12f));
        float beamA = Mathf.Clamp01(1f - (_t - 0.10f) / 0.30f);
        _beam.SetPosition(0, from);
        _beam.SetPosition(1, tip);
        _beamCore.SetPosition(0, from);
        _beamCore.SetPosition(1, tip);
        _beam.widthMultiplier = 0.22f * beamA;
        _beamCore.widthMultiplier = 0.07f * beamA;
        SetA(_beam, P, beamA);
        SetA(_beamCore, W, beamA);

        // ② 心智冲击波：紫/蓝双环外扩，≈20 Hz 双人格交替主导
        float hit = Mathf.Clamp01((_t - 0.08f) / (Dur - 0.08f));
        float r = Mathf.Lerp(0.15f, 1.8f, Out3(hit));
        bool purpleTurn = Mathf.Sin(_t * 125f) > 0f;
        float waveA = hit > 0f ? (1f - hit) * (1f - hit) : 0f;
        BillCircle(_halo, head, r * 1.12f, right, up);
        BillCircle(_ringP, head, r, right, up, _t * 3f);
        BillCircle(_ringB, head, r * 0.82f, right, up, -_t * 4f);
        SetA(_halo, V, 0.6f * waveA);
        SetA(_ringP, P, waveA * (purpleTurn ? 1f : 0.35f));
        SetA(_ringB, B, waveA * (purpleTurn ? 0.35f : 1f));

        // ③ 白炽心智裂光：命中点闪爆
        float fk = Mathf.Clamp01((_t - 0.08f) / 0.20f);
        BillCircle(_flash, head, Mathf.Lerp(0.05f, 0.5f, Out3(fk)), right, up);
        SetA(_flash, W, fk > 0f ? 1f - fk : 0f);

        // ④ 双人格微裂痕：自头部向外撕开的锯齿裂隙，紫/蓝交替闪烁
        float grow = Out3((_t - 0.08f) / 0.18f);
        for (int i = 0; i < CrackN; i++)
        {
          var lr = _crack[i];
          float a = _crackAng[i];
          var d = right * Mathf.Cos(a) + up * Mathf.Sin(a);
          var n = up * Mathf.Cos(a) - right * Mathf.Sin(a);
          float len2 = _crackLen[i] * grow;
          for (int p = 0; p < CrackPts; p++)
          {
            float u = p / (float)(CrackPts - 1);
            lr.SetPosition(p, head + d * (len2 * u) + n * (_jit[i * CrackPts + p] * len2));
          }
          bool isPurple = (i & 1) == 0;
          float flick = isPurple == purpleTurn ? 1f : 0.45f;
          SetA(lr, isPurple ? P : B, grow > 0f ? fade * flick : 0f);
        }
      }
      catch (Exception ex)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 梦魇施法冲击特效更新失败（已移除）：\n" + ex);
        Destroy(gameObject);
      }
    }
  }

  /// <summary>
  /// 梦魇恐慌常驻动画（NightterrorPanicAnim，继承 MarkFxBase）：
  /// 意志豁免失败后，目标陷入恐慌仓皇逃跑（持续 2 轮）期间的夜魇恶咒：
  ///   · 头顶盘旋狂乱扭曲的【夜魇魔面/恐惧之眼】（正对相机，幽魅紫与格罗莉亚冰蓝交织）；
  ///   · 脚下深紫幽暗【梦魇漩涡】；
  ///   · 身周数道狂乱撕扯纠缠的【怨灵暗影之触】。
  /// </summary>
  public class NightterrorPanicAnim : MarkFxBase
  {
    private const int EyePts = 40, VortexArms = 3, ArmPts = 24, TendrilN = 5, TendPts = 14, TearPts = 6;
    private static readonly Color P = NightterrorFx.NightmarePurple, B = NightterrorFx.GloriaBlue,
      W = NightterrorFx.MindWhite, V = NightterrorFx.ShadowViolet;

    private LineRenderer _eyeP, _eyeB, _iris, _pupil, _maskHalo, _rim;
    private readonly LineRenderer[] _tear = new LineRenderer[2];
    private readonly LineRenderer[] _arm = new LineRenderer[VortexArms];
    private readonly LineRenderer[] _tend = new LineRenderer[TendrilN];
    private readonly float[] _tendPhase = new float[TendrilN];
    private readonly float[] _tendSpeed = new float[TendrilN];

    protected override bool Build(Buff buff)
    {
      var mP = Mat(P); var mB = Mat(B); var mW = Mat(W); var mV = Mat(V);
      if (mP == null || mB == null || mW == null || mV == null)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 梦魇·夜魇魔影放弃：StreakMaterialPlain 无可用材质。");
        return false;
      }
      // 头顶夜魇魔眼：紫/蓝两道错位眼廓（人格撕裂）+ 紫虹膜 + 白炽竖瞳 + 幽紫魔面外晕 + 冰蓝泪痕
      _eyeP     = Line(transform, "RD_NT_EyeP", mP, EyePts, true, 0.075f);
      _eyeB     = Line(transform, "RD_NT_EyeB", mB, EyePts, true, 0.06f);
      _iris     = Line(transform, "RD_NT_Iris", mP, 32, true, 0.06f);
      _pupil    = Line(transform, "RD_NT_Pupil", mW, 2, false, 0.08f);
      _maskHalo = Line(transform, "RD_NT_MaskHalo", mV, 48, true, 0.16f);
      for (int i = 0; i < 2; i++) _tear[i] = Line(transform, "RD_NT_Tear", mB, TearPts, false, 0.05f);
      // 脚下梦魇漩涡：三条贴地螺旋臂 + 外缘脉动环
      for (int i = 0; i < VortexArms; i++)
        _arm[i] = Flat(transform, "RD_NT_VortexArm", (i & 1) == 0 ? mP : mV, ArmPts, false, 0.10f);
      _rim = Flat(transform, "RD_NT_VortexRim", mP, RingSeg, true, 0.08f);
      // 暗影怨灵之触：自地面螺旋攀升，根粗梢细
      var taper = AnimationCurve.Linear(0f, 1f, 1f, 0.15f);
      for (int i = 0; i < TendrilN; i++)
      {
        var lr = Line(transform, "RD_NT_Tendril", (i & 1) == 0 ? mV : mP, TendPts, false, 0.16f);
        lr.widthCurve = taper;
        _tend[i] = lr;
        _tendPhase[i] = i * Mathf.PI * 2f / TendrilN + UnityEngine.Random.Range(-0.4f, 0.4f);
        _tendSpeed[i] = UnityEngine.Random.Range(1.4f, 2.4f) * ((i & 1) == 0 ? 1f : -1f);
      }
      return true;
    }

    protected override void Tick(float dt)
    {
      CamBasis(out var right, out var up);
      var feet = Unit.Position;
      float ink = 1f - Mathf.Clamp01(T / 0.35f);
      float appear = (1f - ink * ink * ink) * Master;                 // EaseOut3 入场 × 0.3 s 淡出
      float twitch = Mathf.Sin(T * 23f) * Mathf.Sin(T * 7.3f);        // 不规则抽搐（恐慌）

      // ── 头顶夜魇魔眼（正对相机） ──
      float sc = Mathf.Lerp(0.5f, 1f, appear) * Mathf.Clamp(Radius * 0.55f, 0.45f, 1.1f);
      var eye = feet + Vector3.up * (Height + 0.45f + 0.08f * Mathf.Sin(T * 2.2f)) + right * (0.04f * twitch);
      float ew = 0.62f * sc, eh = 0.30f * sc;
      float split = (0.05f + 0.035f * Mathf.Sin(T * 11f)) * sc;       // 紫/蓝人格错位撕扯
      float open = 0.75f + 0.25f * Mathf.Sin(T * 3.1f);               // 眼睑痉挛开合
      for (int i = 0; i < EyePts; i++)
      {
        float a = i * Mathf.PI * 2f / EyePts;
        float c = Mathf.Cos(a);
        float lid = Mathf.Sin(a) * Mathf.Sqrt(1f - Mathf.Abs(c)) * eh * open;
        float warp = 1f + 0.12f * Mathf.Sin(T * 9f + a * 3f);         // 狂乱扭曲
        var o = right * (c * ew) + up * (lid * warp);
        _eyeP.SetPosition(i, eye + o + right * split + up * (split * 0.4f));
        _eyeB.SetPosition(i, eye + o - right * split - up * (split * 0.4f));
      }
      bool purpleTurn = Mathf.Sin(T * 5.7f) > 0f;                     // 双人格交替主导
      SetA(_eyeP, P, appear * (purpleTurn ? 1f : 0.55f));
      SetA(_eyeB, B, appear * (purpleTurn ? 0.55f : 1f));

      var look = eye + right * (ew * 0.28f * Mathf.Sin(T * 2.7f + 1.3f * twitch)); // 瞳孔惊惶游移
      float ir = eh * 0.62f * open;
      BillCircle(_iris, look, ir, right, up, -T * 4f);
      SetA(_iris, P, 0.9f * appear);
      float dilate = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(T * 6.5f));
      _pupil.SetPosition(0, look + up * (ir * 0.95f * dilate));
      _pupil.SetPosition(1, look - up * (ir * 0.95f * dilate));
      SetA(_pupil, W, appear);
      BillCircle(_maskHalo, eye, ew * (1.18f + 0.06f * Mathf.Sin(T * 4f)), right, up, T * 0.8f);
      SetA(_maskHalo, V, 0.55f * appear);

      for (int t = 0; t < 2; t++)
      {
        float side = t == 0 ? -1f : 1f;
        var root = eye + right * (side * ew * 0.35f) - up * (eh * 0.55f * open);
        float drip = 0.35f + 0.25f * Mathf.Abs(Mathf.Sin(T * 1.7f + t * 1.9f));
        var lr = _tear[t];
        for (int p = 0; p < TearPts; p++)
        {
          float u = p / (float)(TearPts - 1);
          lr.SetPosition(p, root - up * (drip * u * sc) + right * (side * 0.05f * Mathf.Sin(T * 8f + u * 6f) * u));
        }
        SetA(lr, B, 0.8f * appear);
      }

      // ── 脚下梦魇漩涡（贴地旋转、呼吸） ──
      float breathe = 1f + 0.08f * Mathf.Sin(T * 3.3f);
      float vr = Radius * 1.1f * breathe;
      float spin = T * 2.6f;
      for (int i = 0; i < VortexArms; i++)
      {
        var lr = _arm[i];
        float a0 = i * Mathf.PI * 2f / VortexArms - spin;
        for (int p = 0; p < ArmPts; p++)
        {
          float u = p / (float)(ArmPts - 1);
          float a = a0 + u * Mathf.PI * 2.2f;
          float r = vr * (0.12f + 0.88f * u);
          lr.SetPosition(p, feet + Vector3.right * (Mathf.Cos(a) * r) + Vector3.forward * (Mathf.Sin(a) * r) + Vector3.up * 0.06f);
        }
        SetA(lr, (i & 1) == 0 ? P : V, 0.85f * appear);
      }
      Circle(_rim, feet, vr, 0.05f);
      SetA(_rim, P, (0.55f + 0.25f * Mathf.Sin(T * 6f)) * appear);

      // ── 暗影怨灵之触（螺旋攀升、撕扯纠缠） ──
      for (int i = 0; i < TendrilN; i++)
      {
        var lr = _tend[i];
        float th = _tendPhase[i] + T * _tendSpeed[i];
        float dirSign = _tendSpeed[i] > 0f ? 1f : -1f;
        for (int p = 0; p < TendPts; p++)
        {
          float u = p / (float)(TendPts - 1);
          float a = th + u * 1.8f * dirSign + 0.45f * Mathf.Sin(T * 5f + i * 1.7f + u * 6f);
          float r = Radius * (1.05f - 0.5f * u) + 0.15f * Mathf.Sin(T * 7f + u * 9f + i);
          float y = u * Height * (0.95f + 0.15f * Mathf.Sin(T * 3f + i));
          lr.SetPosition(p, feet + Vector3.right * (Mathf.Cos(a) * r) + Vector3.forward * (Mathf.Sin(a) * r) + Vector3.up * y);
        }
        float lash = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(T * 4.3f + i * 2.1f));
        SetA(lr, (i & 1) == 0 ? V : P, lash * appear);
      }
    }
  }

  /// <summary>
  /// 梦魇豁免成功心智震颤动画（NightterrorPassAnim）：
  /// 意志豁免通过时（战栗 1 轮），仅受创一瞬心智震颤顿挫与轻度幽蓝余波（约 0.8s 迅速敛去）。
  /// </summary>
  public class NightterrorPassAnim : MonoBehaviour
  {
    private const float Dur = 0.80f;
    private const int RippleN = 3;
    private static readonly Color B = NightterrorFx.GloriaBlue, W = NightterrorFx.MindWhite;
    private float _t, _h = 2f, _r = 0.8f;
    private UnitEntityData _target;
    private LineRenderer _glow, _ground;
    private readonly LineRenderer[] _ripple = new LineRenderer[RippleN];

    internal bool Init(UnitEntityData target)
    {
      if (target?.View == null) return false;
      var mB = Mat(B);
      var mW = Mat(W);
      if (mB == null || mW == null)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 梦魇豁免震颤放弃：StreakMaterialPlain 无可用材质。");
        return false;
      }
      _target = target;
      _h = ModelHeight(target);
      _r = Mathf.Clamp(target.View.Corpulence * 1.6f, 0.7f, 3.0f);
      transform.position = target.Position + Vector3.up * (_h * 0.5f);
      for (int i = 0; i < RippleN; i++) _ripple[i] = Line(transform, "RD_NT_PassRipple", mB, 48, true, 0.10f);
      _glow   = Line(transform, "RD_NT_PassGlow", mW, 24, true, 0.07f);
      _ground = Flat(transform, "RD_NT_PassGround", mB, RingSeg, true, 0.07f);
      return true;
    }

    private void Update()
    {
      _t += Time.deltaTime;
      if (_t >= Dur || _target == null || _target.View == null)
      {
        Destroy(gameObject);
        return;
      }
      try
      {
        float k = _t / Dur;
        float fade = (1f - k) * (1f - k);
        CamBasis(out var right, out var up);
        // 阻尼振荡：受创一瞬的心智顿挫（左右抖 → 迅速收敛）
        float judder = Mathf.Sin(_t * 70f) * 0.08f * Mathf.Exp(-_t * 7f);
        var feet = _target.Position;
        var chest = feet + Vector3.up * (_h * 0.55f) + right * judder;
        transform.position = chest;

        // 三道错峰外扩的幽蓝震颤波纹（变细 + 二次衰减）
        for (int i = 0; i < RippleN; i++)
        {
          float rk = Mathf.Clamp01((_t - i * 0.12f) / 0.5f);
          float ease = 1f - (1f - rk) * (1f - rk) * (1f - rk);
          var lr = _ripple[i];
          BillCircle(lr, chest, Mathf.Lerp(0.15f, _r * 1.1f, ease), right, up, i * 0.7f);
          lr.widthMultiplier = Mathf.Lerp(0.10f, 0.035f, rk);
          SetA(lr, B, rk <= 0f ? 0f : 0.75f * (1f - rk) * (1f - rk));
        }
        // 胸口轻度心智微光（0.35 s 敛去）
        float g = Mathf.Clamp01(1f - _t / 0.35f);
        BillCircle(_glow, chest, 0.12f + 0.04f * Mathf.Sin(_t * 50f), right, up);
        SetA(_glow, W, 0.85f * g * g);
        // 脚下淡蓝余波
        float ke = 1f - (1f - k) * (1f - k) * (1f - k);
        Circle(_ground, feet, Mathf.Lerp(_r * 0.5f, _r * 1.2f, ke), 0.05f);
        SetA(_ground, B, 0.5f * fade);
      }
      catch (Exception ex)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 梦魇豁免震颤特效更新失败（已移除）：\n" + ex);
        Destroy(gameObject);
      }
    }
  }
}
