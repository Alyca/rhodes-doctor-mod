// 罪罚（6 环·斥罪）的专属自绘特效：法官天平 + 律令法阵 + 律法裁决惩击（纯视觉）。
// 彻底取代原来借的原版"降咒"黑雾（像死灵法师施法、与斥罪法官/律法/正义惩击完全背离）。
//
// 视觉体系：
//   1. 减益常驻期间（SinPunishmentAnim，继承 MarkFxBase）：
//      由 MarkFxTracker 订阅 RhodesDoctorBuffSinPunishment（持续 1 轮/级）；
//      · 头顶正对镜头的【正义天平】（立柱 + 顶环 + 横梁慢速倾斜权衡微摆 + 悬吊吊索 + 两端托盘弧线）；
//      · 脚下平躺的【律法裁决法阵】（双同心圆金环 + 四方审判方标），神圣庄严。
//   2. 减益反噬触发时（SinPunishmentSmiteAnim）：
//      当目标造成伤害触发"同等伤害一半反噬"时，一道金色律令惩击光柱自九天直贯而下，
//      地面激荡审判冲击波与四散金斑，将"律法正义惩击"展现得淋漓尽致。
using System;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Buffs;
using UnityEngine;
using static RhodesDoctor.MarkFxKit;

namespace RhodesDoctor
{
  internal static class SinPunishmentFx
  {
    internal static readonly Color Gold         = new Color(1.00f, 0.82f, 0.22f, 1f); // 律法正义金
    internal static readonly Color RadiantWhite = new Color(0.98f, 0.98f, 1.00f, 1f); // 纯洁裁决白
    internal static readonly Color HotAmber     = new Color(1.00f, 0.65f, 0.15f, 1f); // 惩戒之炎
    internal static readonly Color DeepGold     = new Color(0.85f, 0.58f, 0.10f, 0.65f);

    /// <summary>
    /// 减益常驻跟踪器：按增益名或 GUID 认领。
    /// GUID = Guids.SinPunishmentBuff（具名常量）。
    /// </summary>
    internal static readonly MarkFxTracker Tracker = new MarkFxTracker(
      "RhodesDoctorBuffSinPunishment", "罪罚·正义天平", go => go.AddComponent<SinPunishmentAnim>(),
      Guids.SinPunishmentBuff);

    /// <summary>
    /// 当罪罚的反噬触发时播放：正义天平倾斜裁决，一柱审判圣光贯地而下！
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
        Main.Mod.Logger.Error("[RhodesDoctor] 罪罚反噬惩击特效失败（效果照常）：\n" + e);
      }
    }
  }

  /// <summary>
  /// 罪罚减益常驻特效：
  /// 头顶自绘正义天平（横梁微倾微摆权衡罪与罚）+ 脚底律令法阵。
  /// </summary>
  public class SinPunishmentAnim : MarkFxBase
  {
    // 天平各部
    private LineRenderer _pillar;     // 中央立柱
    private LineRenderer _apexRing;   // 柱顶提环
    private LineRenderer _beam;       // 横梁
    private LineRenderer _chainsL;    // 左吊绳（两根呈八字）
    private LineRenderer _chainsR;    // 右吊绳
    private LineRenderer _panL;       // 左托盘（平滑微弧）
    private LineRenderer _panR;       // 右托盘

    // 地面法阵
    private LineRenderer _groundOuter;
    private LineRenderer _groundInner;
    private readonly LineRenderer[] _lawTicks = new LineRenderer[4];

    protected override bool Build(Buff buff)
    {
      var mG = Mat(SinPunishmentFx.Gold);
      var mW = Mat(SinPunishmentFx.RadiantWhite);
      var mH = Mat(SinPunishmentFx.HotAmber);
      if (mG == null || mW == null || mH == null) return false;

      // 1. 头顶天平
      _pillar   = Line(transform, "RD_SP_Pillar",   mG, 2,  false, 0.045f);
      _apexRing = Line(transform, "RD_SP_ApexRing", mW, 16, true,  0.030f);
      _beam     = Line(transform, "RD_SP_Beam",     mG, 2,  false, 0.050f);
      _chainsL  = Line(transform, "RD_SP_ChainsL",  mW, 3,  false, 0.025f);
      _chainsR  = Line(transform, "RD_SP_ChainsR",  mW, 3,  false, 0.025f);
      _panL     = Line(transform, "RD_SP_PanL",     mH, 12, false, 0.040f);
      _panR     = Line(transform, "RD_SP_PanR",     mH, 12, false, 0.040f);

      // 2. 地面律令光环
      _groundOuter = Flat(transform, "RD_SP_GOuter", mG, RingSeg, true, 0.040f);
      _groundInner = Flat(transform, "RD_SP_GInner", mW, RingSeg, true, 0.025f);
      for (int i = 0; i < 4; i++)
        _lawTicks[i] = Flat(transform, "RD_SP_GTick", mG, 3, false, 0.045f);

      return true;
    }

    protected override void Tick(float dt)
    {
      CamBasis(out var right, out var up);

      float appear = Mathf.Clamp01(T / 0.25f) * Master;
      float pulse = 0.85f + 0.15f * Mathf.Sin(T * 2.2f);

      // ── 1. 头顶正义天平 ──
      var topPos = Unit.Position + Vector3.up * (Height + 0.55f);

      // 顶端提环
      BillCircle(_apexRing, topPos, 0.07f, right, up);
      SetA(_apexRing, SinPunishmentFx.RadiantWhite, appear * pulse);

      // 立柱（自顶向下）
      float pillarLen = 0.52f;
      var beamCenter = topPos - up * 0.12f;
      var pillarBase = topPos - up * pillarLen;
      _pillar.SetPosition(0, topPos);
      _pillar.SetPosition(1, pillarBase);
      SetA(_pillar, SinPunishmentFx.Gold, appear * 0.95f);

      // 横梁轻微倾斜摆动（模拟审判者对罪罚的权衡）
      float tiltAngle = Mathf.Sin(T * 1.6f) * 0.14f; // 约 ±8 度缓慢晃动
      float halfSpan = 0.44f;
      var beamDir = right * Mathf.Cos(tiltAngle) + up * Mathf.Sin(tiltAngle);
      var tipL = beamCenter - beamDir * halfSpan;
      var tipR = beamCenter + beamDir * halfSpan;

      _beam.SetPosition(0, tipL);
      _beam.SetPosition(1, tipR);
      SetA(_beam, SinPunishmentFx.Gold, appear * pulse);

      // 吊绳与托盘（左右各一）
      DrawPanAndChains(tipL, _chainsL, _panL, right, up, appear, 0.20f);
      DrawPanAndChains(tipR, _chainsR, _panR, right, up, appear, 0.20f);

      // ── 2. 脚底律令法阵 ──
      var gPos = Unit.Position;
      float rOut = Radius * 1.35f;
      float rIn  = Radius * 0.95f;

      Circle(_groundOuter, gPos, rOut, 0.05f);
      SetA(_groundOuter, SinPunishmentFx.Gold, appear * 0.85f * pulse);

      Circle(_groundInner, gPos, rIn, 0.06f);
      SetA(_groundInner, SinPunishmentFx.RadiantWhite, appear * 0.65f);

      // 四方审判直角方标
      for (int i = 0; i < 4; i++)
      {
        float a = i * Mathf.PI * 0.5f + (T * 0.15f); // 极慢自转
        var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        var norm = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
        var p0 = gPos + dir * (rOut * 1.08f) - norm * 0.18f;
        var p1 = gPos + dir * (rOut * 1.08f);
        var p2 = gPos + dir * (rOut * 0.85f);
        _lawTicks[i].SetPosition(0, p0);
        _lawTicks[i].SetPosition(1, p1);
        _lawTicks[i].SetPosition(2, p2);
        SetA(_lawTicks[i], SinPunishmentFx.Gold, appear * 0.9f);
      }
    }

    private void DrawPanAndChains(Vector3 tip, LineRenderer chains, LineRenderer pan,
      Vector3 right, Vector3 up, float appear, float panRadius)
    {
      float chainLen = 0.32f;
      var panCenter = tip - Vector3.up * chainLen;

      // 吊绳呈倒 V 形吊住托盘两端
      var pLeft = panCenter - right * panRadius;
      var pRight = panCenter + right * panRadius;
      chains.SetPosition(0, pLeft);
      chains.SetPosition(1, tip);
      chains.SetPosition(2, pRight);
      SetA(chains, SinPunishmentFx.RadiantWhite, appear * 0.75f);

      // 托盘是一道向下的微弧（开口朝上盛托）
      int pts = pan.positionCount;
      for (int i = 0; i < pts; i++)
      {
        float s = (float)i / (pts - 1); // 0 ~ 1
        float x = Mathf.Lerp(-panRadius, panRadius, s);
        // 抛物线微凹
        float y = (x * x / (panRadius * panRadius) - 1f) * 0.06f;
        pan.SetPosition(i, panCenter + right * x + up * y);
      }
      SetA(pan, SinPunishmentFx.HotAmber, appear * 0.95f);
    }
  }

  /// <summary>
  /// 罪罚反噬爆发一瞬：正义惩击圣光柱直插目标 + 地面震荡金环（约 0.55 秒）。
  /// </summary>
  public class SinPunishmentSmiteAnim : MonoBehaviour
  {
    private const float Dur = 0.55f;
    private UnitEntityData _target;
    private float _t;
    private LineRenderer _smiteOuter, _smiteCore, _impactWave, _sparkRing;

    internal bool Init(UnitEntityData target)
    {
      var mG = Mat(SinPunishmentFx.Gold);
      var mW = Mat(SinPunishmentFx.RadiantWhite);
      var mH = Mat(SinPunishmentFx.HotAmber);
      if (mG == null || mW == null || mH == null) return false;

      _target = target;
      _smiteOuter = Line(transform, "RD_SP_SmiteOuter", mG, 2,  false, 0.40f);
      _smiteCore  = Line(transform, "RD_SP_SmiteCore",  mW, 2,  false, 0.14f);
      _impactWave = Flat(transform, "RD_SP_SmiteWave",  mH, RingSeg, true,  0.06f);
      _sparkRing  = Flat(transform, "RD_SP_SparkRing",  mW, RingSeg, true,  0.035f);

      return true;
    }

    private void Update()
    {
      _t += Time.deltaTime;
      if (_t >= Dur || _target == null || _target.View == null) { Destroy(gameObject); return; }

      float p = _t / Dur;
      float alpha = 1f - p;
      var pos = _target.Position;
      float h = ModelHeight(_target);

      // 1. 垂直惩击光束（从天而降，前 0.15s 迅速砸落）
      float strikeProgress = Mathf.Clamp01(_t / 0.12f);
      var sky = pos + Vector3.up * 10f;
      var ground = pos + Vector3.up * 0.05f;
      var bottom = Vector3.Lerp(sky, ground, strikeProgress);

      foreach (var beam in new[] { _smiteOuter, _smiteCore })
      {
        beam.enabled = true;
        beam.SetPosition(0, sky);
        beam.SetPosition(1, bottom);
      }
      _smiteOuter.widthMultiplier = Mathf.Lerp(0.40f, 0.08f, p);
      _smiteCore.widthMultiplier  = Mathf.Lerp(0.15f, 0.02f, p);
      SetA(_smiteOuter, SinPunishmentFx.Gold, alpha);
      SetA(_smiteCore,  SinPunishmentFx.RadiantWhite, alpha);

      // 2. 地面爆发冲击波（0.10s 接触地面后向外扩散至 2.5m）
      float waveT = _t - 0.08f;
      if (waveT > 0f)
      {
        float wp = Mathf.Clamp01(waveT / (Dur - 0.08f));
        float ease = 1f - (1f - wp) * (1f - wp);
        float rWave = Mathf.Lerp(0.3f, 2.6f, ease);
        float waveA = (1f - wp) * 0.95f;

        _impactWave.enabled = true;
        Circle(_impactWave, pos, rWave, 0.08f);
        SetA(_impactWave, SinPunishmentFx.HotAmber, waveA);

        _sparkRing.enabled = true;
        Circle(_sparkRing, pos, rWave * 0.75f, 0.10f);
        SetA(_sparkRing, SinPunishmentFx.RadiantWhite, waveA * 0.8f);
      }
      else
      {
        _impactWave.enabled = false;
        _sparkRing.enabled  = false;
      }
    }
  }
}
