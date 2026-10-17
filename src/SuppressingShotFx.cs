// 牵制射击（0 环·克洛丝）的专属自绘特效：克洛丝标志性【二连射】弩矢 + 连续针刺爆闪 + 压制锁定准星（纯视觉）。
// 严格还原《明日方舟》克洛丝（“在这里哦~ Kokodayo~”）的核心特色——招牌连发弩箭与精准压制：
//
// 结构分两部分：
//   1. 施法射击一瞬（SuppressingShotShotAnim）：
//      还原克洛丝招牌【二连射】（Double-tap Crossbow）：
//      第一发弩矢（t=0.00s）疾速破空，第二发追猎弩矢（t=0.06s）紧随其后双发齐射！
//      双道高亮琥珀曳光（亮白锐利箭芯）如流星般贯向目标胸口，并在命中点接连绽开双重针刺爆闪与环形冲击。
//   2. 减益常驻期间（SuppressingShotAnim，继承 MarkFxBase）：
//      由 MarkFxTracker 订阅 RhodesDoctorBuffSuppressingShot（直到目标下一次攻击后移除）；
//      目标头顶正对相机的【压制锁定准星】：
//      环形锁敌光圈 + 四向收束准星刻线 + 向下压制警示箭头 + 头顶微转光环，
//      醒目表达“命中率受压制（-1 / -2 攻击检定罚值）”的受创受挫意象；攻击消耗后 0.3s 平滑淡出。
using System;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Buffs;
using UnityEngine;
using static RhodesDoctor.MarkFxKit;

namespace RhodesDoctor
{
  internal static class SuppressingShotFx
  {
    internal static readonly Color Amber     = new Color(1.00f, 0.72f, 0.18f, 1f); // 克洛丝暖琥珀
    internal static readonly Color Hot       = new Color(1.00f, 0.88f, 0.45f, 1f); // 弩矢高亮
    internal static readonly Color White     = new Color(1.00f, 0.98f, 0.92f, 1f); // 箭芯/针刺爆闪
    internal static readonly Color DeepAmber = new Color(1.00f, 0.50f, 0.10f, 1f); // 尾羽深橙

    /// <summary>
    /// 减益常驻跟踪器：按增益名或 GUID 认领。
    /// GUID = Guids.Buff(4)（Specs[4].Key == "SuppressingShot"）。
    /// </summary>
    internal static readonly MarkFxTracker Tracker = new MarkFxTracker(
      "RhodesDoctorBuffSuppressingShot", "牵制射击·压制标记", go => go.AddComponent<SuppressingShotAnim>(),
      Guids.Buff(4));

    /// <summary>施法一瞬：自施法者向目标射出克洛丝标志性的双发连射弩矢，接连命中爆发针刺。</summary>
    internal static void PlayShot(UnitEntityData caster, UnitEntityData target)
    {
      try
      {
        if (target?.View == null) return;
        var to = target.Position + Vector3.up * (ModelHeight(target) * 0.65f);
        var from = caster?.View != null
          ? caster.Position + Vector3.up * (ModelHeight(caster) * 0.65f)
          : to + Vector3.up * 4f;

        var go = new GameObject("RD_SuppressingShotBolt") { layer = target.View.gameObject.layer };
        var anim = go.AddComponent<SuppressingShotShotAnim>();
        if (!anim.Init(from, to)) UnityEngine.Object.Destroy(go);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 牵制射击二连射特效失败（效果照常）：\n" + e);
      }
    }
  }

  /// <summary>
  /// 克洛丝招牌【二连射】弩矢飞射与接续命中双爆闪动画（约 0.45 秒）。
  /// </summary>
  public class SuppressingShotShotAnim : MonoBehaviour
  {
    private const float TotalDur = 0.45f;
    private const float Bolt1Fly = 0.13f;
    private const float Bolt2Start = 0.06f;
    private const float Bolt2Fly = 0.13f;

    private Vector3 _from1, _to1;
    private Vector3 _from2, _to2;
    private float _t;

    // ── 第一箭 ──
    private LineRenderer _tracer1, _core1, _hitRing1;
    private readonly LineRenderer[] _hitPins1 = new LineRenderer[4];

    // ── 第二箭（二连射紧随其后）──
    private LineRenderer _tracer2, _core2, _hitRing2;
    private readonly LineRenderer[] _hitPins2 = new LineRenderer[4];

    internal bool Init(Vector3 from, Vector3 to)
    {
      var mA = Mat(SuppressingShotFx.Amber);
      var mW = Mat(SuppressingShotFx.White);
      var mH = Mat(SuppressingShotFx.Hot);
      var mD = Mat(SuppressingShotFx.DeepAmber);
      if (mA == null || mW == null || mH == null || mD == null) return false;

      // 为两发弩箭构造微微交错的弹道（更具立体双发感）
      CamBasis(out var right, out var up);
      _from1 = from - right * 0.04f + up * 0.02f;
      _to1   = to   - right * 0.03f;

      _from2 = from + right * 0.06f - up * 0.02f;
      _to2   = to   + right * 0.04f + up * 0.03f;

      // 第一箭
      _tracer1  = Line(transform, "RD_SS_Tracer1", mA, 2, false, 0.075f);
      _core1    = Line(transform, "RD_SS_Core1",   mW, 2, false, 0.032f);
      _hitRing1 = Line(transform, "RD_SS_HitRing1", mH, 24, true, 0.040f);
      for (int i = 0; i < 4; i++)
        _hitPins1[i] = Line(transform, "RD_SS_Pin1_" + i, mW, 2, false, 0.040f);

      // 第二箭
      _tracer2  = Line(transform, "RD_SS_Tracer2", mD, 2, false, 0.080f);
      _core2    = Line(transform, "RD_SS_Core2",   mW, 2, false, 0.035f);
      _hitRing2 = Line(transform, "RD_SS_HitRing2", mA, 24, true, 0.045f);
      for (int i = 0; i < 4; i++)
        _hitPins2[i] = Line(transform, "RD_SS_Pin2_" + i, mH, 2, false, 0.045f);

      return true;
    }

    private void Update()
    {
      _t += Time.deltaTime;
      if (_t >= TotalDur) { Destroy(gameObject); return; }

      CamBasis(out var right, out var up);

      // ════════════ 1. 第一箭：0.00s ~ 0.13s 飞射，0.11s ~ 0.32s 命中 ════════════
      if (_t <= Bolt1Fly)
      {
        float kHead = Mathf.Clamp01(_t / Bolt1Fly);
        float kTail = Mathf.Clamp01((_t - 0.04f) / (Bolt1Fly - 0.04f));
        var head = Vector3.Lerp(_from1, _to1, kHead);
        var tail = Vector3.Lerp(_from1, _to1, kTail);

        _tracer1.enabled = true;
        _core1.enabled   = true;
        _tracer1.SetPosition(0, tail);
        _tracer1.SetPosition(1, head);
        _core1.SetPosition(0, tail);
        _core1.SetPosition(1, head);
        SetA(_tracer1, SuppressingShotFx.Amber, 1f);
        SetA(_core1,   SuppressingShotFx.White, 1f);
      }
      else
      {
        _tracer1.enabled = false;
        _core1.enabled   = false;
      }

      float hit1T = _t - (Bolt1Fly - 0.02f);
      if (hit1T >= 0f && hit1T < 0.22f)
      {
        float p = Mathf.Clamp01(hit1T / 0.22f);
        float a = 1f - p;
        float r = Mathf.Lerp(0.12f, 0.60f, 1f - (1f - p) * (1f - p));

        _hitRing1.enabled = true;
        BillCircle(_hitRing1, _to1, r, right, up);
        SetA(_hitRing1, SuppressingShotFx.Hot, a * 0.9f);

        for (int i = 0; i < 4; i++)
        {
          _hitPins1[i].enabled = true;
          float angle = i * Mathf.PI * 0.5f; // 正十字针刺
          var dir = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
          _hitPins1[i].SetPosition(0, _to1 + dir * (r * 0.25f));
          _hitPins1[i].SetPosition(1, _to1 + dir * (r * 1.15f));
          SetA(_hitPins1[i], SuppressingShotFx.White, a);
        }
      }
      else
      {
        _hitRing1.enabled = false;
        for (int i = 0; i < 4; i++) _hitPins1[i].enabled = false;
      }

      // ════════════ 2. 第二箭（二连射）：0.06s ~ 0.19s 飞射，0.17s ~ 0.42s 命中 ════════════
      float bolt2Time = _t - Bolt2Start;
      if (bolt2Time >= 0f && bolt2Time <= Bolt2Fly)
      {
        float kHead2 = Mathf.Clamp01(bolt2Time / Bolt2Fly);
        float kTail2 = Mathf.Clamp01((bolt2Time - 0.04f) / (Bolt2Fly - 0.04f));
        var head2 = Vector3.Lerp(_from2, _to2, kHead2);
        var tail2 = Vector3.Lerp(_from2, _to2, kTail2);

        _tracer2.enabled = true;
        _core2.enabled   = true;
        _tracer2.SetPosition(0, tail2);
        _tracer2.SetPosition(1, head2);
        _core2.SetPosition(0, tail2);
        _core2.SetPosition(1, head2);
        SetA(_tracer2, SuppressingShotFx.DeepAmber, 1f);
        SetA(_core2,   SuppressingShotFx.White, 1f);
      }
      else
      {
        _tracer2.enabled = false;
        _core2.enabled   = false;
      }

      float hit2T = _t - (Bolt2Start + Bolt2Fly - 0.02f);
      if (hit2T >= 0f && hit2T < 0.24f)
      {
        float p2 = Mathf.Clamp01(hit2T / 0.24f);
        float a2 = 1f - p2;
        float r2 = Mathf.Lerp(0.15f, 0.72f, 1f - (1f - p2) * (1f - p2));

        _hitRing2.enabled = true;
        BillCircle(_hitRing2, _to2, r2, right, up);
        SetA(_hitRing2, SuppressingShotFx.Amber, a2 * 0.95f);

        for (int i = 0; i < 4; i++)
        {
          _hitPins2[i].enabled = true;
          float angle = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f; // 斜向 45 度十字（交错爆闪）
          var dir = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
          _hitPins2[i].SetPosition(0, _to2 + dir * (r2 * 0.30f));
          _hitPins2[i].SetPosition(1, _to2 + dir * (r2 * 1.30f));
          SetA(_hitPins2[i], SuppressingShotFx.Hot, a2);
        }
      }
      else
      {
        _hitRing2.enabled = false;
        for (int i = 0; i < 4; i++) _hitPins2[i].enabled = false;
      }
    }
  }

  /// <summary>
  /// 牵制射击减益常驻特效（跟增益同生同灭）：
  /// 目标头顶悬浮朝下的压制锁定准星（环形锁敌圈 + 四向收束准星刻线 + 向下惩罚箭头 + 头顶琥珀光环）。
  /// 形象体现克洛丝神射手精准锁定压制、令目标攻击失准的实战意象。
  /// </summary>
  public class SuppressingShotAnim : MarkFxBase
  {
    private LineRenderer _halo;         // 头顶平躺自旋光环
    private LineRenderer _reticleCircle;// 正对相机的瞄准圆环
    private readonly LineRenderer[] _crossTicks = new LineRenderer[4]; // 四向收束刻线
    private LineRenderer _downArrow;    // 向下的压制指示箭头（V型）
    private LineRenderer _centerDot;    // 准心核心点

    protected override bool Build(Buff buff)
    {
      var mA = Mat(SuppressingShotFx.Amber);
      var mW = Mat(SuppressingShotFx.White);
      var mH = Mat(SuppressingShotFx.Hot);
      if (mA == null || mW == null || mH == null) return false;

      _halo          = Flat(transform, "RD_SS_Halo",    mA, RingSeg, true, 0.030f);
      _reticleCircle = Line(transform, "RD_SS_Reticle", mH, RingSeg, true, 0.035f);
      for (int i = 0; i < 4; i++)
        _crossTicks[i] = Line(transform, "RD_SS_Cross_" + i, mW, 2, false, 0.038f);
      _downArrow     = Line(transform, "RD_SS_Arrow",   mA, 3, false, 0.045f);
      _centerDot     = Line(transform, "RD_SS_Dot",     mW, 12, true, 0.035f);

      return true;
    }

    protected override void Tick(float dt)
    {
      CamBasis(out var right, out var up);

      var headPos = Unit.Position + Vector3.up * (Height + 0.38f);
      float appear = Mathf.Clamp01(T / 0.15f) * Master;
      float pulse = 0.82f + 0.18f * Mathf.Sin(T * 4.0f);

      // ── 1. 头顶平躺光环 ──
      Circle(_halo, Unit.Position + Vector3.up * Height, 0.45f, 0f);
      SetA(_halo, SuppressingShotFx.Amber, 0.50f * appear * pulse);

      // ── 2. 正对相机的瞄准圆环 ──
      float rRing = 0.26f;
      BillCircle(_reticleCircle, headPos, rRing, right, up);
      SetA(_reticleCircle, SuppressingShotFx.Hot, 0.85f * appear * pulse);

      // ── 3. 四向收束准星刻线（上下左右）──
      for (int i = 0; i < 4; i++)
      {
        float angle = i * Mathf.PI * 0.5f;
        var dir = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
        _crossTicks[i].SetPosition(0, headPos + dir * (rRing * 0.65f));
        _crossTicks[i].SetPosition(1, headPos + dir * (rRing * 1.35f));
        SetA(_crossTicks[i], SuppressingShotFx.White, 0.90f * appear);
      }

      // ── 4. 向下的压制指示箭头（指向头部，表达攻击命中惩罚）──
      float arrowTop = -0.06f;
      float arrowBottom = -0.22f;
      float arrowWidth = 0.12f;
      _downArrow.SetPosition(0, headPos + up * arrowTop - right * arrowWidth);
      _downArrow.SetPosition(1, headPos + up * arrowBottom);
      _downArrow.SetPosition(2, headPos + up * arrowTop + right * arrowWidth);
      SetA(_downArrow, SuppressingShotFx.Amber, 0.95f * appear * pulse);

      // ── 5. 准心核心高亮白点 ──
      BillCircle(_centerDot, headPos, 0.035f, right, up);
      SetA(_centerDot, SuppressingShotFx.White, appear * pulse);
    }
  }
}
