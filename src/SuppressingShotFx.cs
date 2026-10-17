// 牵制射击（0 环·克洛丝）的专属自绘特效：飞射弩箭 + 命中针刺 + 持续压制准心（纯视觉）。
// 取代原来借的原版精神系紫圈爆发（与醉梦完全重样、脱离压制射击意象）。
//
// 结构分两部分：
//   1. 施法射击一瞬（SuppressingShotShotAnim）：
//      施法者身前 → 目标胸口的琥珀金弩矢曳光（高亮白芯）+ 命中 4 向针刺爆闪。
//   2. 减益常驻期间（SuppressingShotAnim，继承 MarkFxBase）：
//      由 MarkFxTracker 订阅 RhodesDoctorBuffSuppressingShot（直到目标下一次攻击后移除）；
//      目标头顶正对镜头的"压制锁定"（向下箭头 / 楔形准星 + 两侧定位刻线 + 头部微旋光环），
//      醒目表达"命中率被压制（-1 / -2 攻击检定罚值）"的意象；攻击后减益移除即 0.3 s 平滑淡出。
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
    internal static readonly Color White     = new Color(1.00f, 0.96f, 0.90f, 1f); // 箭芯/针刺
    internal static readonly Color PaleAmber = new Color(1.00f, 0.80f, 0.40f, 0.65f);

    /// <summary>
    /// 减益常驻跟踪器：按增益名或 GUID 认领。
    /// GUID = Guids.Buff(4)（Specs[4].Key == "SuppressingShot"）。
    /// </summary>
    internal static readonly MarkFxTracker Tracker = new MarkFxTracker(
      "RhodesDoctorBuffSuppressingShot", "牵制射击·压制标记", go => go.AddComponent<SuppressingShotAnim>(),
      Guids.Buff(4));

    /// <summary>施法一瞬：自施法者向目标射出一道高亮弩矢，命中点展开针刺光斑。</summary>
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
        Main.Mod.Logger.Error("[RhodesDoctor] 牵制射击弩箭特效失败（效果照常）：\n" + e);
      }
    }
  }

  /// <summary>弩箭飞射与命中针刺爆闪动画（约 0.35 秒）。</summary>
  public class SuppressingShotShotAnim : MonoBehaviour
  {
    private const float FlyDur = 0.14f;
    private const float TotalDur = 0.35f;

    private Vector3 _from, _to;
    private float _t;
    private LineRenderer _tracer, _core, _hitRing;
    private readonly LineRenderer[] _hitPins = new LineRenderer[4];

    internal bool Init(Vector3 from, Vector3 to)
    {
      var mA = Mat(SuppressingShotFx.Amber);
      var mW = Mat(SuppressingShotFx.White);
      var mH = Mat(SuppressingShotFx.Hot);
      if (mA == null || mW == null || mH == null) return false;

      _from = from;
      _to = to;

      _tracer  = Line(transform, "RD_SS_Tracer", mA, 2, false, 0.08f);
      _core    = Line(transform, "RD_SS_Core",   mW, 2, false, 0.035f);
      _hitRing = Line(transform, "RD_SS_HitRing", mH, 32, true, 0.04f);
      for (int i = 0; i < 4; i++)
        _hitPins[i] = Line(transform, "RD_SS_Pin", mW, 2, false, 0.045f);

      return true;
    }

    private void Update()
    {
      _t += Time.deltaTime;
      if (_t >= TotalDur) { Destroy(gameObject); return; }

      // ── 1. 弩矢飞行阶段（0 ~ 0.14s）──
      float kHead = Mathf.Clamp01(_t / FlyDur);
      float kTail = Mathf.Clamp01((_t - 0.045f) / (FlyDur - 0.045f));
      if (_t <= FlyDur)
      {
        var head = Vector3.Lerp(_from, _to, kHead);
        var tail = Vector3.Lerp(_from, _to, kTail);
        foreach (var lr in new[] { _tracer, _core })
        {
          lr.enabled = true;
          lr.SetPosition(0, tail);
          lr.SetPosition(1, head);
        }
        SetA(_tracer, SuppressingShotFx.Amber, 1f);
        SetA(_core,   SuppressingShotFx.White, 1f);
      }
      else
      {
        _tracer.enabled = false;
        _core.enabled   = false;
      }

      // ── 2. 命中爆发阶段（0.12 ~ 0.35s）：4 根十字针刺刺出 + 细环扩散 ──
      float hitT = _t - 0.12f;
      if (hitT >= 0f)
      {
        CamBasis(out var right, out var up);
        float p = Mathf.Clamp01(hitT / (TotalDur - 0.12f));
        float alpha = 1f - p;
        float r = Mathf.Lerp(0.15f, 0.70f, 1f - (1f - p) * (1f - p));

        _hitRing.enabled = true;
        BillCircle(_hitRing, _to, r, right, up);
        SetA(_hitRing, SuppressingShotFx.Hot, alpha * 0.9f);

        for (int i = 0; i < 4; i++)
        {
          _hitPins[i].enabled = true;
          float angle = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f; // 斜向 45 度十字
          var dir = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
          _hitPins[i].SetPosition(0, _to + dir * (r * 0.3f));
          _hitPins[i].SetPosition(1, _to + dir * (r * 1.3f));
          SetA(_hitPins[i], SuppressingShotFx.White, alpha);
        }
      }
      else
      {
        _hitRing.enabled = false;
        for (int i = 0; i < 4; i++) _hitPins[i].enabled = false;
      }
    }
  }

  /// <summary>
  /// 牵制射击减益常驻特效（跟增益同生同灭）：
  /// 目标头顶悬浮朝下的压制标记（向下楔形准星 + 两侧夹角约束刻线 + 头顶微转光环）。
  /// </summary>
  public class SuppressingShotAnim : MarkFxBase
  {
    private LineRenderer _chevron;   // 向下的楔形/箭头（压制意象）
    private LineRenderer _tickLeft;  // 左侧夹角标线
    private LineRenderer _tickRight; // 右侧夹角标线
    private LineRenderer _halo;      // 头顶平躺自旋光环
    private LineRenderer _pinDot;    // 准心中心点

    protected override bool Build(Buff buff)
    {
      var mA = Mat(SuppressingShotFx.Amber);
      var mW = Mat(SuppressingShotFx.White);
      var mH = Mat(SuppressingShotFx.Hot);
      if (mA == null || mW == null || mH == null) return false;

      _chevron   = Line(transform, "RD_SS_Chevron",   mA, 3, false, 0.045f);
      _tickLeft  = Line(transform, "RD_SS_TickL",     mH, 2, false, 0.035f);
      _tickRight = Line(transform, "RD_SS_TickR",     mH, 2, false, 0.035f);
      _halo      = Flat(transform, "RD_SS_Halo",      mA, RingSeg, true, 0.030f);
      _pinDot    = Line(transform, "RD_SS_Dot",       mW, 12, true, 0.040f);

      return true;
    }

    protected override void Tick(float dt)
    {
      CamBasis(out var right, out var up);

      var headPos = Unit.Position + Vector3.up * (Height + 0.35f);
      float appear = Mathf.Clamp01(T / 0.15f) * Master;
      float pulse = 0.82f + 0.18f * Mathf.Sin(T * 3.8f);

      // ── 1. 头顶平躺光环（微旋）──
      Circle(_halo, Unit.Position + Vector3.up * Height, 0.45f, 0f);
      SetA(_halo, SuppressingShotFx.Amber, 0.55f * appear * pulse);

      // ── 2. 正对相机的向下楔形压制准星 ──
      // 顶点朝下指向目标头部，象征"攻击命中被下压"
      float size = 0.32f;
      var tip   = headPos + up * -0.12f;
      var left  = headPos + right * -size + up * size;
      var rightPt = headPos + right * size + up * size;

      _chevron.SetPosition(0, left);
      _chevron.SetPosition(1, tip);
      _chevron.SetPosition(2, rightPt);
      SetA(_chevron, SuppressingShotFx.Amber, 0.95f * appear * pulse);

      // ── 3. 左右两侧夹角约束刻线 ──
      var leftOuter = left + right * -0.18f;
      _tickLeft.SetPosition(0, leftOuter);
      _tickLeft.SetPosition(1, left);
      SetA(_tickLeft, SuppressingShotFx.Hot, 0.85f * appear);

      var rightOuter = rightPt + right * 0.18f;
      _tickRight.SetPosition(0, rightOuter);
      _tickRight.SetPosition(1, rightPt);
      SetA(_tickRight, SuppressingShotFx.Hot, 0.85f * appear);

      // ── 4. 楔形中心高亮白点 ──
      BillCircle(_pinDot, headPos + up * (size * 0.45f), 0.035f, right, up);
      SetA(_pinDot, SuppressingShotFx.White, appear * pulse);
    }
  }
}
