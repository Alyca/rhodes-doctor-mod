// 不稳定血浆（2 环·华法琳）专属自绘 3D 程序化视觉特效（纯视觉，不影响结算）。
// 彻底解耦原版“血雾”PlasmaAura，忠实还原《明日方舟》华法琳（血魔医生）招牌视觉意象：
//
// 视觉体系（4 维立体构成）：
//   1. 【三元环绕血晶（Tri-Orbital Blood Crystals）】：
//      三枚锐利菱形血晶呈 120° 正三角形环绕躯干公转自旋，伴随独立相位的高低沉浮与晶尖微倾；
//   2. 【心率脉搏电波环（Cardiac ECG Pulse Wave）】：
//      模拟心脏“收缩-舒张”的二段式脉冲（lub-dub 节奏），以高频心电折线（QRS波）自胸腔向外激荡；
//   3. 【升腾血魔双螺旋（Ascending Blood Helix）】：
//      两股互呈反相位的猩红与试剂金微光流线自足底螺旋盘旋升腾至头顶，象征药剂注射与生命燃烧；
//   4. 【地面试剂刻印环（Ground Reagent Glyph）】：
//      脚底平铺的同心血环与四向刻度针刺，标示法术影响范围基准。
using System;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Buffs;
using UnityEngine;
using static RhodesDoctor.MarkFxKit;

namespace RhodesDoctor
{
  internal static class UnstablePlasmaFx
  {
    // 华法琳血魔试剂调色盘（纯代码加法混合发光体系）
    internal static readonly Color ArterialBlood = new Color(0.92f, 0.08f, 0.16f, 1f); // 标志性动脉猩红
    internal static readonly Color AdrenalineRed = new Color(1.00f, 0.25f, 0.30f, 1f); // 脉冲强音鲜红
    internal static readonly Color ReagentAmber  = new Color(1.00f, 0.80f, 0.32f, 1f); // 肾上腺素/试剂流金
    internal static readonly Color CrystalWhite  = new Color(1.00f, 0.94f, 0.96f, 1f); // 晶核高亮锐芒
    internal static readonly Color ShadowBlood   = new Color(0.50f, 0.05f, 0.10f, 1f); // 深色血雾基底

    /// <summary>
    /// 增益跟踪器：按蓝图名或 GUID 过滤。
    /// 蓝图名：RhodesDoctorBuffUnstablePlasma
    /// GUID：Guids.Buff(13)
    /// </summary>
    internal static readonly MarkFxTracker Tracker = new MarkFxTracker(
      "RhodesDoctorBuffUnstablePlasma", "不稳定血浆·血魔活性", go => go.AddComponent<UnstablePlasmaAnim>(),
      Guids.Buff(13));
  }

  /// <summary>
  /// 不稳定血浆常驻动画控制组件（跟随增益同生同灭，纯视觉）：
  /// 继承 MarkFxBase，由 Tracker 自动管理生成、单位跟随与 0.3s 平滑淡出。
  /// </summary>
  public class UnstablePlasmaAnim : MarkFxBase
  {
    // ── 几何体采样与常量配置 ──
    private const int CrystalCount = 3;
    private const int CrystalSegments = 5;      // 菱形晶体 5 点闭环（顶-右-底-左-顶）
    private const int EcgSegments = 48;         // 心率脉冲圆环折线采样
    private const int HelixSegments = 32;       // 升腾螺旋采样
    private const int GroundRingSegments = 40;  // 地面基座圆环采样

    // ── 3D 渲染线元组件 ──
    // 1. 三枚环绕菱形血晶
    private readonly LineRenderer[] _crystals = new LineRenderer[CrystalCount];
    private readonly LineRenderer[] _crystalAxes = new LineRenderer[CrystalCount]; // 晶核轴线

    // 2. 心率脉冲电波
    private LineRenderer _pulseWave;
    private LineRenderer _pulseGlow;

    // 3. 升腾双螺旋流光
    private LineRenderer _helixA;
    private LineRenderer _helixB;

    // 4. 地面试剂阵
    private LineRenderer _groundRing;
    private readonly LineRenderer[] _groundNeedles = new LineRenderer[4]; // 四向刻度针刺

    protected override bool Build(Buff buff)
    {
      var mBlood = Mat(UnstablePlasmaFx.ArterialBlood);
      var mPulse = Mat(UnstablePlasmaFx.AdrenalineRed);
      var mAmber = Mat(UnstablePlasmaFx.ReagentAmber);
      var mWhite = Mat(UnstablePlasmaFx.CrystalWhite);
      var mDark  = Mat(UnstablePlasmaFx.ShadowBlood);

      if (mBlood == null || mPulse == null || mAmber == null || mWhite == null)
        return false;

      // 1. 初始化三枚环绕血晶
      for (int i = 0; i < CrystalCount; i++)
      {
        _crystals[i] = Line(transform, $"RD_UP_Crystal_{i}", mBlood, CrystalSegments, true, 0.045f);
        _crystalAxes[i] = Line(transform, $"RD_UP_CrystalAxis_{i}", mWhite, 2, false, 0.025f);
      }

      // 2. 初始化心率脉冲电波
      _pulseWave = Line(transform, "RD_UP_PulseWave", mPulse, EcgSegments, true, 0.055f);
      _pulseGlow = Line(transform, "RD_UP_PulseGlow", mAmber, EcgSegments, true, 0.030f);

      // 3. 初始化双螺旋流光
      _helixA = Line(transform, "RD_UP_HelixA", mBlood, HelixSegments, false, 0.038f);
      _helixB = Line(transform, "RD_UP_HelixB", mAmber, HelixSegments, false, 0.032f);

      // 4. 初始化地面试剂阵
      _groundRing = Flat(transform, "RD_UP_GroundRing", mDark, GroundRingSegments, true, 0.040f);
      for (int i = 0; i < 4; i++)
      {
        _groundNeedles[i] = Flat(transform, $"RD_UP_Needle_{i}", mAmber, 2, false, 0.035f);
      }

      return true;
    }

    protected override void Tick(float dt)
    {
      if (Unit?.View == null) return;
      var root = Unit.Position;
      float alpha = Master;

      // ── 模块 1：三元环绕血晶（公转 + 自旋 + 沉浮）──
      UpdateCrystals(root, alpha);

      // ── 模块 2：心电脉冲波（lub-dub 心脏搏动节律）──
      UpdateCardiacPulse(root, alpha);

      // ── 模块 3：升腾血魔双螺旋（DNA 式分子链旋转攀升）──
      UpdateHelices(root, alpha);

      // ── 模块 4：地面基座刻印 ──
      UpdateGroundGlyph(root, alpha);
    }

    /// <summary>更新环绕的三枚尖锐菱形血晶</summary>
    private void UpdateCrystals(Vector3 root, float alpha)
    {
      float orbitRadius = Radius * 1.05f;
      float chestY = Height * 0.55f;
      float orbitSpeed = 1.35f;

      CamBasis(out var camRight, out var camUp);

      for (int i = 0; i < CrystalCount; i++)
      {
        float baseAngle = i * (Mathf.PI * 2f / CrystalCount);
        float currentAngle = baseAngle + T * orbitSpeed;

        // 浮沉微动与倾角
        float bob = Mathf.Sin(T * 2.5f + i * 2.094f) * (Height * 0.08f);
        Vector3 center = root + new Vector3(Mathf.Cos(currentAngle) * orbitRadius, chestY + bob, Mathf.Sin(currentAngle) * orbitRadius);

        // 菱形几何构建（面向镜头并带有独立自旋）
        float spinAngle = T * 3.0f + i * 1.5f;
        Vector3 right = (camRight * Mathf.Cos(spinAngle) + camUp * Mathf.Sin(spinAngle)).normalized;
        Vector3 up = Vector3.Cross(Camera.main != null ? Camera.main.transform.forward : Vector3.forward, right).normalized;

        float halfW = 0.09f;
        float halfH = 0.22f;

        Vector3 pTop    = center + up * halfH;
        Vector3 pRight  = center + right * halfW;
        Vector3 pBottom = center - up * halfH;
        Vector3 pLeft   = center - right * halfW;

        _crystals[i].SetPosition(0, pTop);
        _crystals[i].SetPosition(1, pRight);
        _crystals[i].SetPosition(2, pBottom);
        _crystals[i].SetPosition(3, pLeft);
        _crystals[i].SetPosition(4, pTop);

        _crystalAxes[i].SetPosition(0, pTop);
        _crystalAxes[i].SetPosition(1, pBottom);

        SetA(_crystals[i], UnstablePlasmaFx.ArterialBlood, alpha * 0.95f);
        SetA(_crystalAxes[i], UnstablePlasmaFx.CrystalWhite, alpha * (0.6f + 0.4f * Mathf.PingPong(T * 4f + i, 1f)));
      }
    }

    /// <summary>更新模拟心脏跳动节律的心率电波</summary>
    private void UpdateCardiacPulse(Vector3 root, float alpha)
    {
      // 心跳周期 1.0 秒：0.0s~0.18s 第一心音爆发，0.22s~0.38s 第二心音回响，余下时间舒张
      float cycle = T % 1.0f;
      float pulseIntensity = 0f;
      float pulseRadius = Radius * 0.6f;

      if (cycle < 0.20f)
      {
        float p = cycle / 0.20f;
        pulseIntensity = Mathf.Sin(p * Mathf.PI);
        pulseRadius = Mathf.Lerp(Radius * 0.5f, Radius * 1.4f, p);
      }
      else if (cycle >= 0.22f && cycle < 0.40f)
      {
        float p = (cycle - 0.22f) / 0.18f;
        pulseIntensity = Mathf.Sin(p * Mathf.PI) * 0.65f;
        pulseRadius = Mathf.Lerp(Radius * 0.7f, Radius * 1.6f, p);
      }

      float waveY = Height * 0.5f;
      Vector3 center = root + Vector3.up * waveY;

      for (int i = 0; i < EcgSegments; i++)
      {
        float angle = i * (Mathf.PI * 2f / EcgSegments);
        // 心电图尖锐锯齿调制：在圆环特定弧度上产生高频 QRS 波尖峰
        float ecgJitter = Mathf.Sin(angle * 6f + T * 8f) * (pulseIntensity * 0.12f);
        float r = pulseRadius + ecgJitter;

        Vector3 pt = center + new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
        _pulseWave.SetPosition(i, pt);
        _pulseGlow.SetPosition(i, pt + Vector3.up * (Mathf.Sin(angle * 12f) * 0.04f));
      }

      SetA(_pulseWave, UnstablePlasmaFx.AdrenalineRed, alpha * pulseIntensity * 0.90f);
      SetA(_pulseGlow, UnstablePlasmaFx.ReagentAmber, alpha * pulseIntensity * 0.70f);
    }

    /// <summary>更新自足底升腾至头顶的双螺旋血能</summary>
    private void UpdateHelices(Vector3 root, float alpha)
    {
      float hTotal = Height * 1.05f;
      float turns = 2.5f;
      float speed = 2.4f;

      for (int i = 0; i < HelixSegments; i++)
      {
        float norm = (float)i / (HelixSegments - 1);
        float y = norm * hTotal;
        // 随高度变化收放的瓶颈半径曲线（腰部略微向内收束）
        float r = Radius * 0.75f * (0.85f + 0.35f * Mathf.Sin(norm * Mathf.PI));

        float thetaA = norm * (Mathf.PI * 2f * turns) - T * speed;
        float thetaB = thetaA + Mathf.PI; // 反向对称相位

        Vector3 posA = root + new Vector3(Mathf.Cos(thetaA) * r, y, Mathf.Sin(thetaA) * r);
        Vector3 posB = root + new Vector3(Mathf.Cos(thetaB) * r, y, Mathf.Sin(thetaB) * r);

        _helixA.SetPosition(i, posA);
        _helixB.SetPosition(i, posB);
      }

      SetA(_helixA, UnstablePlasmaFx.ArterialBlood, alpha * 0.75f);
      SetA(_helixB, UnstablePlasmaFx.ReagentAmber, alpha * 0.60f);
    }

    /// <summary>更新脚底微光刻印</summary>
    private void UpdateGroundGlyph(Vector3 root, float alpha)
    {
      float groundR = Radius * 0.95f;
      Circle(_groundRing, root, groundR, 0.03f);
      SetA(_groundRing, UnstablePlasmaFx.ShadowBlood, alpha * 0.50f);

      for (int i = 0; i < 4; i++)
      {
        float angle = i * (Mathf.PI * 0.5f) + T * 0.2f;
        Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        _groundNeedles[i].SetPosition(0, root + dir * (groundR * 0.75f) + Vector3.up * 0.03f);
        _groundNeedles[i].SetPosition(1, root + dir * (groundR * 1.15f) + Vector3.up * 0.03f);
        SetA(_groundNeedles[i], UnstablePlasmaFx.ReagentAmber, alpha * 0.45f);
      }
    }
  }
}
