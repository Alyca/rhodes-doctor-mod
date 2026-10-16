// 紧急撤离（0 环·接触传送）的专属特效：PRTS 战术撤离（纯视觉、一次性 ≈1.3 s，不影响结算）。
// 取代原来借的"仪式·任意门"闪光。
// 由 ContextActionTeleportAllyToCaster 在 Translocate 之后调用（此时起点 / 落点都已知）。
// 线条基础件与材质复用 MarkFxKit（SwordStreakFx.StreakMaterialPlain，实机验证能上屏）。
using System;
using Kingmaker.EntitySystem.Entities;
using UnityEngine;
using static RhodesDoctor.MarkFxKit;

namespace RhodesDoctor
{
  internal static class EmergencyExtractionFx
  {
    internal static readonly Color Cyan  = new Color(0.25f, 0.85f, 1.00f, 1f);
    internal static readonly Color White = new Color(0.85f, 1.00f, 1.00f, 1f);

    internal static void Play(UnitEntityData unit, Vector3 from, Vector3 to)
    {
      try
      {
        if (unit?.View == null) return;
        var go = new GameObject("RD_EmergencyExtraction") { layer = unit.View.gameObject.layer };
        var a = go.AddComponent<EmergencyExtractionAnim>();
        if (!a.Init(unit, from, to)) { UnityEngine.Object.Destroy(go); return; }
        Dbg.Log("[RhodesDoctor] 紧急撤离·战术撤离特效已生成。");
      }
      catch (Exception e) { Main.Mod.Logger.Error("[RhodesDoctor] 紧急撤离特效失败（效果照常）：\n" + e); }
    }
  }

  public class EmergencyExtractionAnim : MonoBehaviour
  {
    private const float Dur = 1.3f, FadeStart = 1.0f;
    private const int Rings = 3, PathPts = 24, PulsePts = 6;
    private const float Gy = 0.05f;

    private UnitEntityData _unit;
    private Vector3 _from, _to, _pa, _pb, _ctrl;
    private float _h, _r, _t;
    private LineRenderer _oGround, _oBeam, _path, _pulse, _dGround;
    private readonly LineRenderer[] _oRing = new LineRenderer[Rings];
    private readonly LineRenderer[] _dRing = new LineRenderer[Rings];
    private readonly LineRenderer[] _br = new LineRenderer[4];

    private static float EaseOut(float k) => 1f - (1f - k) * (1f - k);
    private static float Win(float t, float a, float b) => Mathf.Clamp01((t - a) / (b - a));

    internal bool Init(UnitEntityData unit, Vector3 from, Vector3 to)
    {
      var mC = Mat(EmergencyExtractionFx.Cyan);
      var mW = Mat(EmergencyExtractionFx.White);
      if (mC == null || mW == null) return false;

      _unit = unit; _from = from; _to = to;
      _h = ModelHeight(unit);
      _r = Mathf.Clamp((unit.View != null ? unit.View.Corpulence : 0.5f) * 1.3f, 0.6f, 2.0f);

      // 引导弧线：胸口高度 → 胸口高度，中点上抬（距离越远弧越高）
      _pa = from + Vector3.up * (_h * 0.5f);
      _pb = to + Vector3.up * (_h * 0.5f);
      float dist = Vector3.Distance(from, to);
      _ctrl = (_pa + _pb) * 0.5f + Vector3.up * Mathf.Clamp(dist * 0.5f, 0.8f, 3f);

      _oGround = Flat(transform, "RD_EE_OGround", mC, RingSeg, true, 0.05f);
      _oBeam   = Line(transform, "RD_EE_OBeam", mW, 2, false, 0.06f);
      for (int i = 0; i < Rings; i++)
      {
        _oRing[i] = Flat(transform, "RD_EE_ORing", mC, RingSeg, true, 0.035f);
        _dRing[i] = Flat(transform, "RD_EE_DRing", mW, RingSeg, true, 0.035f);
      }
      _path    = Line(transform, "RD_EE_Path", mC, PathPts, false, 0.035f);
      _pulse   = Line(transform, "RD_EE_Pulse", mW, PulsePts, false, 0.07f);
      _dGround = Flat(transform, "RD_EE_DGround", mW, RingSeg, true, 0.05f);
      for (int i = 0; i < 4; i++) _br[i] = Flat(transform, "RD_EE_Bracket", mC, 3, false, 0.05f);
      return true;
    }

    /// <summary>二次贝塞尔：起点胸口 → 上抬控制点 → 落点胸口。</summary>
    private Vector3 Bez(float s)
    {
      float u = 1f - s;
      return u * u * _pa + 2f * u * s * _ctrl + s * s * _pb;
    }

    private void Update()
    {
      _t += Time.deltaTime;
      if (_t >= Dur) { Destroy(gameObject); return; }
      float master = _t < FadeStart ? 1f : 1f - Win(_t, FadeStart, Dur);
      var C = EmergencyExtractionFx.Cyan;
      var W = EmergencyExtractionFx.White;

      // ── ① 起点"拆解"：地面圈收拢 + 三道环升到头顶 + 竖直光束一闪 ──
      float ko = Win(_t, 0f, 0.5f);
      Circle(_oGround, _from, Mathf.Lerp(_r, 0.15f, EaseOut(ko)), Gy);
      SetA(_oGround, C, 0.85f * (1f - ko));
      for (int i = 0; i < Rings; i++)
      {
        float k = Win(_t, i * 0.08f, 0.35f + i * 0.08f);
        bool on = k > 0f && k < 1f;
        _oRing[i].enabled = on;
        if (!on) continue;
        Circle(_oRing[i], _from, _r * Mathf.Lerp(0.9f, 0.3f, k), Gy + _h * EaseOut(k));
        SetA(_oRing[i], C, 0.8f * Mathf.Sin(k * Mathf.PI));
      }
      float kb = Win(_t, 0.15f, 0.55f);
      _oBeam.enabled = kb > 0f && kb < 1f;
      if (_oBeam.enabled)
      {
        _oBeam.SetPosition(0, _from + Vector3.up * Gy);
        _oBeam.SetPosition(1, _from + Vector3.up * (_h * 1.6f));
        _oBeam.widthMultiplier = 0.06f * (1f - kb);
        SetA(_oBeam, W, 0.9f * Mathf.Sin(kb * Mathf.PI));
      }

      // ── ② 引导弧线：0.05~0.45 s 长出，0.6~0.95 s 淡出；白色亮点沿线跑 ──
      float kg = EaseOut(Win(_t, 0.05f, 0.45f));
      bool pathOn = _t >= 0.05f;
      _path.enabled = pathOn;
      if (pathOn)
      {
        for (int p = 0; p < PathPts; p++) _path.SetPosition(p, Bez(kg * p / (PathPts - 1f)));
        SetA(_path, C, 0.7f * (1f - Win(_t, 0.6f, 0.95f)) * master);
      }
      _pulse.enabled = _t >= 0.05f && _t < 0.6f;
      if (_pulse.enabled)
      {
        float head = kg, tail = Mathf.Max(0f, head - 0.18f);
        for (int p = 0; p < PulsePts; p++)
          _pulse.SetPosition(p, Bez(Mathf.Lerp(tail, head, p / (PulsePts - 1f))));
        SetA(_pulse, W, 1f - Win(_t, 0.45f, 0.6f));
      }

      // ── ③ 落点（跟随目标）：四角框滑入 → 三道环从头顶降到脚底 → 落地扩散环 ──
      var d = (_unit != null && _unit.View != null) ? _unit.Position : _to;
      float ks = EaseOut(Win(_t, 0f, 0.3f));
      float s = _r * 1.15f * Mathf.Lerp(1.8f, 1f, ks);
      float len = s * 0.35f;
      for (int i = 0; i < 4; i++)
      {
        float sx = (i == 0 || i == 3) ? 1f : -1f;
        float sz = (i < 2) ? 1f : -1f;
        var corner = d + new Vector3(sx * s, Gy, sz * s);
        _br[i].SetPosition(0, corner - new Vector3(sx * len, 0f, 0f));
        _br[i].SetPosition(1, corner);
        _br[i].SetPosition(2, corner - new Vector3(0f, 0f, sz * len));
        SetA(_br[i], C, 0.9f * ks * master);
      }
      for (int i = 0; i < Rings; i++)
      {
        float k = Win(_t, 0.35f + i * 0.08f, 0.70f + i * 0.08f);
        bool on = k > 0f && k < 1f;
        _dRing[i].enabled = on;
        if (!on) continue;
        Circle(_dRing[i], d, _r * Mathf.Lerp(0.3f, 0.9f, k), Gy + _h * (1f - EaseOut(k)));
        SetA(_dRing[i], W, 0.85f * Mathf.Sin(k * Mathf.PI));
      }
      float kp = Win(_t, 0.75f, 1.1f);
      _dGround.enabled = kp > 0f && kp < 1f;
      if (_dGround.enabled)
      {
        Circle(_dGround, d, Mathf.Lerp(_r * 0.4f, _r * 1.6f, EaseOut(kp)), Gy);
        SetA(_dGround, W, 0.9f * (1f - kp));
      }
    }
  }
}
