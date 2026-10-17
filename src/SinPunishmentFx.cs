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
  /// 罪罚减益常驻特效：
  /// 头顶自绘法官正义法槌（蓄势微晃悬顶）+ 脚底律法荆棘法庭印章。
  /// </summary>
  public class SinPunishmentAnim : MarkFxBase
  {
    // 法官之槌各部件
    private LineRenderer _gavelHead;    // 厚实双斜面槌头（闭合框）
    private LineRenderer _gavelHandle;  // 槌柄
    private LineRenderer _gavelRing;    // 槌柄铜箍饰环
    private LineRenderer _gavelGlow;    // 槌头高光刃线

    // 脚底律法荆棘法庭印章
    private LineRenderer _sealOuter;    // 外环
    private LineRenderer _sealInner;    // 内环
    private readonly LineRenderer[] _thorns = new LineRenderer[4]; // 四向交错荆棘刻线

    protected override bool Build(Buff buff)
    {
      var mG = Mat(SinPunishmentFx.JusticeGold);
      var mW = Mat(SinPunishmentFx.RadiantWhite);
      var mR = Mat(SinPunishmentFx.ThornRed);
      if (mG == null || mW == null || mR == null) return false;

      // 1. 头顶法槌
      _gavelHead   = Line(transform, "RD_SP_GavelHead",   mG, 5, true,  0.055f);
      _gavelHandle = Line(transform, "RD_SP_GavelHandle", mW, 2, false, 0.040f);
      _gavelRing   = Line(transform, "RD_SP_GavelRing",   mG, 8, true,  0.035f);
      _gavelGlow   = Line(transform, "RD_SP_GavelGlow",   mW, 2, false, 0.045f);

      // 2. 地面律法荆棘法印
      _sealOuter = Flat(transform, "RD_SP_SealOut", mG, RingSeg, true, 0.045f);
      _sealInner = Flat(transform, "RD_SP_SealIn",  mW, RingSeg, true, 0.025f);
      for (int i = 0; i < 4; i++)
        _thorns[i] = Flat(transform, "RD_SP_Thorn" + i, mR, 5, false, 0.035f);

      return true;
    }

    protected override void Tick(float dt)
    {
      CamBasis(out var right, out var up);

      float appear = Mathf.Clamp01(T / 0.22f) * Master;
      float pulse = 0.85f + 0.15f * Mathf.Sin(T * 2.6f);

      // ════════════ 1. 头顶正义法槌（斜倾悬顶，蓄势微晃） ════════════
      var topPos = Unit.Position + Vector3.up * (Height + 0.55f);
      float rockAngle = Mathf.Sin(T * 2.0f) * 0.15f + 0.40f; // 约 25度 ~ 35度倾斜角

      var dirHead   = right * Mathf.Cos(rockAngle) + up * Mathf.Sin(rockAngle);
      var dirHandle = -right * Mathf.Sin(rockAngle) + up * Mathf.Cos(rockAngle);

      // 槌柄（长约 0.55m）
      var handleBase = topPos + dirHandle * 0.40f;
      var handleHead = topPos - dirHandle * 0.12f;
      _gavelHandle.SetPosition(0, handleBase);
      _gavelHandle.SetPosition(1, handleHead);
      SetA(_gavelHandle, SinPunishmentFx.RadiantWhite, appear * 0.90f);

      // 槌柄金色铜箍
      var ringPos = topPos + dirHandle * 0.12f;
      BillCircle(_gavelRing, ringPos, 0.05f, right, up);
      SetA(_gavelRing, SinPunishmentFx.JusticeGold, appear * pulse);

      // 槌头矩形双面平头（长 0.38m, 宽 0.18m）
      float hLen = 0.20f;
      float hWid = 0.09f;
      var c0 = handleHead - dirHead * hLen + dirHandle * hWid;
      var c1 = handleHead + dirHead * hLen + dirHandle * hWid;
      var c2 = handleHead + dirHead * hLen - dirHandle * hWid;
      var c3 = handleHead - dirHead * hLen - dirHandle * hWid;

      _gavelHead.SetPosition(0, c0);
      _gavelHead.SetPosition(1, c1);
      _gavelHead.SetPosition(2, c2);
      _gavelHead.SetPosition(3, c3);
      _gavelHead.SetPosition(4, c0);
      SetA(_gavelHead, SinPunishmentFx.JusticeGold, appear * pulse);

      // 槌面敲击刃线（白金高亮）
      _gavelGlow.SetPosition(0, c0);
      _gavelGlow.SetPosition(1, c3);
      SetA(_gavelGlow, SinPunishmentFx.RadiantWhite, appear * 1f);

      // ════════════ 2. 脚底律法荆棘印章 ════════════
      var gPos = Unit.Position;
      float rOut = Radius * 1.30f;
      float rIn  = Radius * 0.92f;

      Circle(_sealOuter, gPos, rOut, 0.05f);
      SetA(_sealOuter, SinPunishmentFx.JusticeGold, appear * 0.85f * pulse);

      Circle(_sealInner, gPos, rIn, 0.06f);
      SetA(_sealInner, SinPunishmentFx.RadiantWhite, appear * 0.65f);

      // 四向尖锐律法荆棘突刺标
      for (int i = 0; i < 4; i++)
      {
        float a = i * Mathf.PI * 0.5f + (T * 0.10f);
        var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        var norm = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));

        var t0 = gPos + dir * (rOut * 0.70f);
        var t1 = gPos + dir * (rOut * 1.05f) - norm * 0.12f;
        var t2 = gPos + dir * (rOut * 1.25f); // 荆棘外刺尖
        var t3 = gPos + dir * (rOut * 1.05f) + norm * 0.12f;

        _thorns[i].SetPosition(0, t0);
        _thorns[i].SetPosition(1, t1);
        _thorns[i].SetPosition(2, t2);
        _thorns[i].SetPosition(3, t3);
        _thorns[i].SetPosition(4, t0);
        SetA(_thorns[i], SinPunishmentFx.ThornRed, appear * 0.85f * pulse);
      }
    }
  }

  /// <summary>
  /// 罪罚反噬爆发一瞬：巨型金色法槌九天力劈砸落 + 律法裁决圣光震荡波（约 0.55 秒）。
  /// </summary>
  public class SinPunishmentSmiteAnim : MonoBehaviour
  {
    private const float Dur = 0.55f;
    private UnitEntityData _target;
    private float _t;

    private LineRenderer _giantGavelHead;
    private LineRenderer _giantGavelHandle;
    private LineRenderer _smiteBeam;
    private LineRenderer _impactSealWave;
    private readonly LineRenderer[] _shatterThorns = new LineRenderer[4];

    internal bool Init(UnitEntityData target)
    {
      var mG = Mat(SinPunishmentFx.JusticeGold);
      var mW = Mat(SinPunishmentFx.RadiantWhite);
      var mR = Mat(SinPunishmentFx.ThornRed);
      if (mG == null || mW == null || mR == null) return false;

      _target = target;

      // 巨型落击法槌
      _giantGavelHead   = Line(transform, "RD_SP_BigGavelHead",   mG, 5, true,  0.10f);
      _giantGavelHandle = Line(transform, "RD_SP_BigGavelHandle", mW, 2, false, 0.08f);
      _smiteBeam        = Line(transform, "RD_SP_SmiteBeam",      mG, 2, false, 0.40f);

      // 地面冲击法印与碎裂荆棘
      _impactSealWave   = Flat(transform, "RD_SP_ImpSeal", mG, RingSeg, true, 0.06f);
      for (int i = 0; i < 4; i++)
        _shatterThorns[i] = Flat(transform, "RD_SP_ShatterThorn" + i, mR, 2, false, 0.05f);

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

      CamBasis(out var right, out var up);

      // ── 1. 巨型法槌砸击动画（0 ~ 0.14s 极速自天空猛烈劈下） ──
      float strikeK = Mathf.Clamp01(_t / 0.12f);
      float easeDown = strikeK * strikeK; // 加速下砸

      var skyCenter = pos + Vector3.up * 8.0f;
      var hitCenter = pos + Vector3.up * (h * 0.7f);
      var currentGavelPos = Vector3.Lerp(skyCenter, hitCenter, easeDown);

      // 法槌在下砸过程中从后仰转为平击
      float gavelAngle = Mathf.Lerp(1.1f, 0.0f, easeDown);
      var gHeadDir   = right * Mathf.Cos(gavelAngle) + up * Mathf.Sin(gavelAngle);
      var gHandleDir = -right * Mathf.Sin(gavelAngle) + up * Mathf.Cos(gavelAngle);

      float bLen = 0.45f;
      float bWid = 0.22f;
      _giantGavelHead.SetPosition(0, currentGavelPos - gHeadDir * bLen + gHandleDir * bWid);
      _giantGavelHead.SetPosition(1, currentGavelPos + gHeadDir * bLen + gHandleDir * bWid);
      _giantGavelHead.SetPosition(2, currentGavelPos + gHeadDir * bLen - gHandleDir * bWid);
      _giantGavelHead.SetPosition(3, currentGavelPos - gHeadDir * bLen - gHandleDir * bWid);
      _giantGavelHead.SetPosition(4, currentGavelPos - gHeadDir * bLen + gHandleDir * bWid);

      _giantGavelHandle.SetPosition(0, currentGavelPos + gHandleDir * 0.95f);
      _giantGavelHandle.SetPosition(1, currentGavelPos);

      // 法槌在命中后迅速淡出，将能量注入冲击波
      float gavelAlpha = (1f - Mathf.Clamp01((_t - 0.10f) / 0.20f));
      SetA(_giantGavelHead,   SinPunishmentFx.JusticeGold,  gavelAlpha);
      SetA(_giantGavelHandle, SinPunishmentFx.RadiantWhite, gavelAlpha);

      // ── 2. 贯地审判金光柱 ──
      _smiteBeam.SetPosition(0, pos + Vector3.up * 9.0f);
      _smiteBeam.SetPosition(1, pos + Vector3.up * 0.05f);
      _smiteBeam.widthMultiplier = Mathf.Lerp(0.45f, 0.05f, p);
      SetA(_smiteBeam, SinPunishmentFx.JusticeGold, alpha);

      // ── 3. 落槌震荡法印冲击波与炸裂荆棘（0.10s 触地后瞬间爆发） ──
      float waveT = _t - 0.10f;
      if (waveT > 0f)
      {
        float wp = Mathf.Clamp01(waveT / (Dur - 0.10f));
        float ease = 1f - (1f - wp) * (1f - wp);
        float rWave = Mathf.Lerp(0.4f, 2.7f, ease);
        float waveA = (1f - wp) * 0.95f;

        _impactSealWave.enabled = true;
        Circle(_impactSealWave, pos, rWave, 0.08f);
        SetA(_impactSealWave, SinPunishmentFx.JusticeGold, waveA);

        for (int i = 0; i < 4; i++)
        {
          _shatterThorns[i].enabled = true;
          float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
          var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
          _shatterThorns[i].SetPosition(0, pos + dir * (rWave * 0.5f) + Vector3.up * 0.1f);
          _shatterThorns[i].SetPosition(1, pos + dir * (rWave * 1.25f) + Vector3.up * 0.1f);
          SetA(_shatterThorns[i], SinPunishmentFx.ThornRed, waveA);
        }
      }
      else
      {
        _impactSealWave.enabled = false;
        for (int i = 0; i < 4; i++) _shatterThorns[i].enabled = false;
      }
    }
  }
}
