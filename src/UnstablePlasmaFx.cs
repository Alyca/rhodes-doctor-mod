// 不稳定血浆（2 环·华法琳）专属自绘 3D 程序化视觉特效（纯视觉，不影响结算）。
// 彻底解耦原版“血雾”PlasmaAura，忠实还原《明日方舟》华法琳（血魔医生）招牌视觉意象：
//
// 视觉体系（4 维立体构成，Macroscope 第二轮数学动画精修）：
//   1. 【三元环绕立体菱形血晶（Tri-Orbital Polyhedral Blood Crystals）】：
//      三枚锐利修长的菱形八面体立体线框血晶呈 120° 正三角形环绕受增益者胸腔（约 Height * 0.55f）公转；
//      晶轴前倾并绕径向微摆，赤道基底自旋，贯穿晶核微光轴线，沿解析轨道拖曳 0.35s 渐隐微粒（头粗尾细）；
//   2. 【二段式心率脉搏电波环（Cardiac ECG Pulse Wave）】：
//      真实心脏“收缩-舒张”二段式脉冲（S1: 0~0.18s 立方 ease-out, S2: 0.22~0.38s, 舒张: 0.38~1.0s），
//      分段线性高频 QRS 心电折线（P-QRS-T 真实波形）自胸腔向外激荡，导出心搏包络 _beat 联动全场；
//   3. 【升腾血魔双螺旋（Ascending Blood Helix）】：
//      两股互呈反相位的猩红与试剂金微光流线自足底螺旋攀升至头顶（1.1 倍身高），
//      呈拟人瓶颈曲线（足底 0.70R → 腰部收束至 0.44R → 头顶散逸扩张至 1.0R）；
//   4. 【地面试剂同心环（Ground Reagent Glyph）】：
//      脚底同心双环与四向刻度针刺，随心搏包络微幅呼吸起伏与同频微胀。
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
    private const int CrystalSegments = 13;     // 八面体线框欧拉一笔画：12 条棱 13 点
    private const int TrailSegments = 10;       // 晶体拖尾微粒采样
    private const int EcgSegments = 48;         // 心率脉冲圆环折线采样
    private const int HelixSegments = 32;       // 升腾螺旋采样
    private const int GroundRingSegments = 40;  // 地面基座圆环采样

    // ── 3D 渲染线元组件 ──
    // 1. 三枚环绕立体菱形血晶
    private readonly LineRenderer[] _crystals = new LineRenderer[CrystalCount];
    private readonly LineRenderer[] _crystalAxes = new LineRenderer[CrystalCount]; // 晶核轴线
    private readonly LineRenderer[] _crystalTrails = new LineRenderer[CrystalCount]; // 拖尾微粒

    // 2. 心率脉冲电波
    private LineRenderer _pulseWave;
    private LineRenderer _pulseGlow;

    // 3. 升腾双螺旋流光
    private LineRenderer _helixA;
    private LineRenderer _helixB;

    // 4. 地面试剂同心环
    private LineRenderer _groundRing;
    private LineRenderer _groundRingInner;
    private readonly LineRenderer[] _groundNeedles = new LineRenderer[4]; // 四向刻度针刺

    // 心搏包络（0~1），供地面刻印随心跳呼吸；由 UpdateCardiacPulse 每帧写入
    private float _beat;

    private const float CycleSec = 1.0f; // 心动周期
    private const int EcgLobes = 3;      // 周向 QRS 复合波个数（sin(kθ) 的 k）

    protected override bool Build(Buff buff)
    {
      var mBlood = Mat(UnstablePlasmaFx.ArterialBlood);
      var mPulse = Mat(UnstablePlasmaFx.AdrenalineRed);
      var mAmber = Mat(UnstablePlasmaFx.ReagentAmber);
      var mWhite = Mat(UnstablePlasmaFx.CrystalWhite);
      var mDark  = Mat(UnstablePlasmaFx.ShadowBlood);

      if (mBlood == null || mPulse == null || mAmber == null || mWhite == null || mDark == null)
        return false;

      // 1. 初始化三枚立体八面体血晶与拖尾
      for (int i = 0; i < CrystalCount; i++)
      {
        _crystals[i] = Line(transform, $"RD_UP_Crystal_{i}", mBlood, CrystalSegments, false, 0.040f);
        _crystalAxes[i] = Line(transform, $"RD_UP_CrystalAxis_{i}", mWhite, 2, false, 0.022f);
        _crystalTrails[i] = Line(transform, $"RD_UP_CrystalTrail_{i}", mPulse, TrailSegments, false, 0.035f);
        _crystalTrails[i].widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.1f); // 头粗尾细
      }

      // 2. 初始化心率脉冲电波
      _pulseWave = Line(transform, "RD_UP_PulseWave", mPulse, EcgSegments, true, 0.055f);
      _pulseGlow = Line(transform, "RD_UP_PulseGlow", mAmber, EcgSegments, true, 0.030f);

      // 3. 初始化双螺旋流光
      _helixA = Line(transform, "RD_UP_HelixA", mBlood, HelixSegments, false, 0.038f);
      _helixB = Line(transform, "RD_UP_HelixB", mAmber, HelixSegments, false, 0.032f);

      // 4. 初始化地面试剂同心环与针刺
      _groundRing = Flat(transform, "RD_UP_GroundRing", mDark, GroundRingSegments, true, 0.040f);
      _groundRingInner = Flat(transform, "RD_UP_GroundRingInner", mBlood, GroundRingSegments, true, 0.025f);
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

      // ── 模块 1：三元环绕立体血晶（八面体线框 + 3D 倾角 + 自旋 + 拖尾）──
      UpdateCrystals(root, alpha);

      // ── 模块 2：心电脉冲波（lub-dub 心脏搏动节律 + QRS 复合波调制，写入 _beat）──
      UpdateCardiacPulse(root, alpha);

      // ── 模块 3：升腾血魔双螺旋（DNA 式分子链旋转攀升，腰部收束瓶颈曲线）──
      UpdateHelices(root, alpha);

      // ── 模块 4：地面试剂同心环（随心搏包络呼吸微胀）──
      UpdateGroundGlyph(root, alpha);
    }

    /// <summary>更新环绕的三枚立体菱形血晶（八面体线框 + 3D 倾角 + 自旋 + 拖尾）</summary>
    private void UpdateCrystals(Vector3 root, float alpha)
    {
      float orbitRadius = Radius * 1.05f;
      float chestY = Height * 0.55f;
      const float orbitSpeed = 1.35f;
      float halfH = Mathf.Clamp(Height * 0.11f, 0.16f, 0.45f); // 纵向半长（修长）
      float halfW = halfH * 0.40f;                            // 赤道半宽（锐利）

      for (int i = 0; i < CrystalCount; i++)
      {
        float phase = i * (Mathf.PI * 2f / CrystalCount); // 120° 相位分隔
        Vector3 center = CrystalCenter(root, T, phase, i, orbitRadius, chestY, orbitSpeed);

        // 3D 倾角：晶轴向公转切向前倾并绕径向微摆，三枚相位错开
        float orbitA = phase + T * orbitSpeed;
        Vector3 radial = new Vector3(Mathf.Cos(orbitA), 0f, Mathf.Sin(orbitA));
        Vector3 tangent = new Vector3(-radial.z, 0f, radial.x);
        float tiltA = 0.35f + 0.15f * Mathf.Sin(T * 1.7f + i * 2.094f);
        float tiltB = 0.20f * Mathf.Sin(T * 1.1f + i * 1.3f);
        Vector3 axis = (Vector3.up * Mathf.Cos(tiltA) + tangent * Mathf.Sin(tiltA) + radial * tiltB).normalized;

        // 自旋：赤道正交基 (e0, e1) 绕晶轴旋转
        Vector3 e0 = Vector3.Cross(axis, radial);
        if (e0.sqrMagnitude < 1e-4f) e0 = Vector3.Cross(axis, Vector3.forward);
        e0.Normalize();
        Vector3 e1 = Vector3.Cross(axis, e0);
        float spin = T * 3.0f + i * 1.5f;
        float cs = Mathf.Cos(spin), sn = Mathf.Sin(spin);
        Vector3 u = (e0 * cs + e1 * sn) * halfW;
        Vector3 v = (e1 * cs - e0 * sn) * halfW;

        Vector3 top = center + axis * halfH;
        Vector3 bot = center - axis * halfH;
        Vector3 q0 = center + u, q1 = center + v, q2 = center - u, q3 = center - v;

        // 八面体 12 条棱欧拉回路（13 点一笔画）：T-Q0-Q1-Q2-Q3-Q0-B-Q1-T-Q2-B-Q3-T
        var lr = _crystals[i];
        lr.SetPosition(0, top);  lr.SetPosition(1, q0);   lr.SetPosition(2, q1);
        lr.SetPosition(3, q2);   lr.SetPosition(4, q3);   lr.SetPosition(5, q0);
        lr.SetPosition(6, bot);  lr.SetPosition(7, q1);   lr.SetPosition(8, top);
        lr.SetPosition(9, q2);   lr.SetPosition(10, bot); lr.SetPosition(11, q3);
        lr.SetPosition(12, top);

        // 晶核纵向微光轴线（略长于晶体，贯穿双尖）
        _crystalAxes[i].SetPosition(0, center + axis * (halfH * 1.25f));
        _crystalAxes[i].SetPosition(1, center - axis * (halfH * 1.25f));

        // 拖尾微粒：沿解析轨道回溯过去 0.35 s 的晶心位置（零 GC）
        var trail = _crystalTrails[i];
        for (int k = 0; k < TrailSegments; k++)
        {
          float back = k * (0.35f / (TrailSegments - 1));
          trail.SetPosition(k, CrystalCenter(root, T - back, phase, i, orbitRadius, chestY, orbitSpeed));
        }

        SetA(lr, UnstablePlasmaFx.ArterialBlood, alpha * 0.95f);
        SetA(_crystalAxes[i], UnstablePlasmaFx.CrystalWhite, alpha * (0.6f + 0.4f * Mathf.PingPong(T * 4f + i, 1f)));
        var tc = UnstablePlasmaFx.AdrenalineRed;
        trail.startColor = new Color(tc.r, tc.g, tc.b, Mathf.Clamp01(alpha * 0.55f));
        trail.endColor = new Color(tc.r, tc.g, tc.b, 0f);
      }
    }

    /// <summary>血晶在时刻 t 的晶心（公转 + 三相错位正弦沉浮），供本体与拖尾共用。</summary>
    private Vector3 CrystalCenter(Vector3 root, float t, float phase, int i, float orbitR, float chestY, float speed)
    {
      float a = phase + t * speed;
      float bob = Mathf.Sin(t * 2.5f + i * 2.094f) * (Height * 0.08f);
      return root + new Vector3(Mathf.Cos(a) * orbitR, chestY + bob, Mathf.Sin(a) * orbitR);
    }

    /// <summary>心率电波（lub-dub）：S1 0–0.18s，S2 0.22–0.38s，舒张 0.38–1.0s</summary>
    private void UpdateCardiacPulse(Vector3 root, float alpha)
    {
      float c = Mathf.Repeat(T, CycleSec);
      float intensity, radius, spike;

      if (c < 0.18f)
      {
        // 第一心音 S1（收缩期）：自胸口高速炸开（立方 ease-out），尖锐 QRS 全幅
        float p = c / 0.18f;
        float ease = 1f - (1f - p) * (1f - p) * (1f - p);
        intensity = Mathf.Lerp(0.15f, 1f, Mathf.Sin(Mathf.Min(1f, p * 4f) * Mathf.PI * 0.5f)) * (1f - 0.55f * p);
        radius = Mathf.Lerp(Radius * 0.5f, Radius * 1.45f, ease);
        spike = intensity;
      }
      else if (c < 0.22f)
      {
        // S1 → S2 间隙：余波熄灭
        float g = (c - 0.18f) / 0.04f;
        intensity = 0.45f * (1f - g);
        radius = Radius * 1.45f;
        spike = 0f;
      }
      else if (c < 0.38f)
      {
        // 第二心音 S2（重搏回波）：次级脉冲，幅度与锯齿减弱
        float p = (c - 0.22f) / 0.16f;
        float ease = 1f - (1f - p) * (1f - p);
        intensity = 0.65f * Mathf.Sin(p * Mathf.PI);
        radius = Mathf.Lerp(Radius * 0.7f, Radius * 1.6f, ease);
        spike = 0.6f * intensity;
      }
      else
      {
        // 舒张期：平滑回缩至起搏半径并微弱淡入，与下一拍 S1 起点（0.5R, 0.15）无缝衔接
        float p = (c - 0.38f) / 0.62f;
        float s = p * p * (3f - 2f * p);
        intensity = 0.15f * s;
        radius = Mathf.Lerp(Radius * 1.6f, Radius * 0.5f, s);
        spike = 0f;
      }
      _beat = intensity;

      float spikeAmp = Radius * 0.22f * spike;
      Vector3 center = root + Vector3.up * (Height * 0.55f);

      for (int i = 0; i < EcgSegments; i++)
      {
        float angle = i * (Mathf.PI * 2f / EcgSegments);
        // QRS 调制：以与 sin(kθ) 同频的相位 u 驱动 P-QRS-T 心电波形（取代平滑正弦）
        float u = Mathf.Repeat(angle * EcgLobes / (Mathf.PI * 2f) + T * 0.25f, 1f);
        float r = radius + Qrs(u) * spikeAmp;

        float cs = Mathf.Cos(angle), sn = Mathf.Sin(angle);
        _pulseWave.SetPosition(i, center + new Vector3(cs * r, 0f, sn * r));
        _pulseGlow.SetPosition(i, center + new Vector3(cs * r * 0.96f, 0.02f, sn * r * 0.96f));
      }

      SetA(_pulseWave, UnstablePlasmaFx.AdrenalineRed, alpha * intensity * 0.90f);
      SetA(_pulseGlow, UnstablePlasmaFx.ReagentAmber, alpha * intensity * 0.70f);
    }

    /// <summary>单个心电复合波（u∈[0,1)）：基线 0，Q 谷 −0.3，R 峰 +1，S 谷 −0.4，T 波 +0.2；分段线性保证尖锐</summary>
    private static float Qrs(float u)
    {
      if (u < 0.30f) return 0f;
      if (u < 0.35f) return -0.3f * (u - 0.30f) / 0.05f;                     // Q 下行
      if (u < 0.48f) return Mathf.Lerp(-0.3f, 1f, (u - 0.35f) / 0.13f);      // R 上冲
      if (u < 0.61f) return Mathf.Lerp(1f, -0.4f, (u - 0.48f) / 0.13f);      // R → S
      if (u < 0.68f) return Mathf.Lerp(-0.4f, 0f, (u - 0.61f) / 0.07f);      // S 回基线
      if (u < 0.78f) return 0f;
      if (u < 0.95f) return 0.2f * Mathf.Sin((u - 0.78f) / 0.17f * Mathf.PI); // T 波
      return 0f;
    }

    /// <summary>更新自足底升腾至头顶的双螺旋血能（腰部收束瓶颈曲线）</summary>
    private void UpdateHelices(Vector3 root, float alpha)
    {
      float hTotal = Height * 1.1f;
      float turns = 2.5f;
      float speed = 2.4f;

      for (int i = 0; i < HelixSegments; i++)
      {
        float norm = (float)i / (HelixSegments - 1);
        float y = norm * hTotal;
        // 瓶颈半径曲线：足底 0.70R → 腰部(n≈0.5) 收束至 ≈0.44R → 头顶散逸扩张至 1.0R
        float r = Radius * (0.70f - 0.28f * Mathf.Sin(norm * Mathf.PI) + 0.30f * norm * norm * norm * norm);

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

    /// <summary>更新脚底试剂同心环 + 四向刻度针刺（随心跳微幅呼吸，低对比度）</summary>
    private void UpdateGroundGlyph(Vector3 root, float alpha)
    {
      float glow = 0.35f + 0.25f * _beat;                  // 亮度随心搏起伏
      float outerR = Radius * 0.95f * (1f + 0.05f * _beat); // 外环半径微胀 ≤5%
      float innerR = Radius * 0.62f * (1f + 0.03f * _beat); // 内环同步微胀

      Circle(_groundRing, root, outerR, 0.03f);
      SetA(_groundRing, UnstablePlasmaFx.ShadowBlood, alpha * glow);
      Circle(_groundRingInner, root, innerR, 0.03f);
      SetA(_groundRingInner, UnstablePlasmaFx.ArterialBlood, alpha * glow * 0.6f);

      for (int i = 0; i < 4; i++)
      {
        float angle = i * (Mathf.PI * 0.5f) + T * 0.2f;
        Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        _groundNeedles[i].SetPosition(0, root + dir * innerR + Vector3.up * 0.03f);
        _groundNeedles[i].SetPosition(1, root + dir * (outerR * 1.15f) + Vector3.up * 0.03f);
        SetA(_groundNeedles[i], UnstablePlasmaFx.ReagentAmber, alpha * glow * 0.9f);
      }
    }
  }
}
