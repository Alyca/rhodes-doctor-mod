// 终末狙击（7 环·黑）的专属特效：红外激光 + 准心锁定 + 开火曳光（纯视觉）。
// 取代原来借的"火之烙印"（头顶一团火）。
// 标记增益名按 CreateExtraBuff 规则：RhodesDoctorBuffFinalSnipeMark。
// 开火由 ContextActionFinalSnipe 结算时调用 Fire()；隐形哑火则不调用（只淡出准心）。
using System;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Buffs;
using UnityEngine;
using static RhodesDoctor.MarkFxKit;

namespace RhodesDoctor
{
  internal static class FinalSnipeFx
  {
    internal static readonly Color Red   = new Color(1.00f, 0.10f, 0.08f, 1f);
    internal static readonly Color Hot   = new Color(1.00f, 0.55f, 0.45f, 1f);
    internal static readonly Color White = new Color(1.00f, 0.92f, 0.90f, 1f);

    /// <summary>
    /// GUID 兜底 = <c>Guids.FinalSnipeMarkBuff</c>（具名常量，不像火力侦察那样依赖 Specs 下标）。
    /// 标记是 `CreateExtraBuff("FinalSnipeMark", Guids.FinalSnipeMarkBuff, …)` 建的
    /// （终末狙击是 Kind.Special，不走 `CreateSpellBuff`）。
    /// </summary>
    internal static readonly MarkFxTracker Tracker = new MarkFxTracker(
      "RhodesDoctorBuffFinalSnipeMark", "终末狙击·红外锁定", go => go.AddComponent<FinalSnipeAnim>(),
      Guids.FinalSnipeMarkBuff);

    /// <summary>开火：枪口 → 目标的曳光 + 命中闪光环，并立刻收掉准心。</summary>
    internal static void Fire(UnitEntityData caster, UnitEntityData target)
    {
      try
      {
        if (target?.View == null) return;
        Tracker.Get(target)?.FadeOut();
        var to = target.Position + Vector3.up * (ModelHeight(target) * 0.65f);
        var from = caster?.View != null
          ? caster.Position + Vector3.up * (ModelHeight(caster) * 0.7f)
          : to + Vector3.up * 8f;
        var go = new GameObject("RD_FinalSnipeShot") { layer = target.View.gameObject.layer };
        var a = go.AddComponent<FinalSnipeShotAnim>();
        if (!a.Init(from, to)) UnityEngine.Object.Destroy(go);
      }
      catch (Exception e) { Main.Mod.Logger.Error("[RhodesDoctor] 终末狙击开火特效失败（效果照常）：\n" + e); }
    }
  }

  public class FinalSnipeAnim : MarkFxBase
  {
    private const float LockDur = 0.45f;
    private Buff _buff;
    private UnitEntityData _caster;
    private float _casterH = 1.8f;
    private LineRenderer _laser, _dot, _reticle, _ground;
    private readonly LineRenderer[] _tick = new LineRenderer[4];

    protected override bool Build(Buff buff)
    {
      var mR = Mat(FinalSnipeFx.Red);
      var mH = Mat(FinalSnipeFx.Hot);
      if (mR == null || mH == null)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 终末狙击·红外锁定放弃：StreakMaterialPlain 无可用材质。");
        return false;
      }
      SetCaster(buff);
      _laser   = Line(transform, "RD_FS_Laser", mR, 2, false, 0.018f);
      _dot     = Line(transform, "RD_FS_Dot", mH, 12, true, 0.05f);
      _reticle = Line(transform, "RD_FS_Reticle", mR, 48, true, 0.03f);
      for (int i = 0; i < 4; i++) _tick[i] = Line(transform, "RD_FS_Tick", mR, 2, false, 0.035f);
      _ground  = Flat(transform, "RD_FS_Ground", mR, RingSeg, true, 0.025f);
      return true;
    }

    internal override void Replay(Buff buff) { base.Replay(buff); SetCaster(buff); }

    private void SetCaster(Buff buff)
    {
      if (buff != null) _buff = buff;
      var c = buff?.Context?.MaybeCaster;
      if (c == null) return;
      _caster = c;
      _casterH = ModelHeight(c);
    }

    protected override void Tick(float dt)
    {
      CamBasis(out var right, out var up);
      var aim = Unit.Position + Vector3.up * (Height * 0.65f);
      float appear = Mathf.Clamp01(T / 0.12f) * Master;
      float k = Mathf.Clamp01(T / LockDur);
      float e = 1f - (1f - k) * (1f - k);

      // 准心：大圈旋转收缩到位 = "锁定"；脉动随时间加快（一轮 6 s 内 1.5 → 5 Hz），像倒计时
      float baseR = Mathf.Clamp(Radius * 0.5f, 0.35f, 1.2f);
      float r = baseR * Mathf.Lerp(2.6f, 1f, e);
      float rot = Mathf.Lerp(Mathf.PI * 0.25f, 0f, e);
      float hz = Mathf.Lerp(1.5f, 5f, Mathf.Clamp01(T / 6f));
      float pulse = 0.75f + 0.25f * Mathf.Sin(T * hz * Mathf.PI * 2f);

      BillCircle(_reticle, aim, r, right, up, rot);
      SetA(_reticle, FinalSnipeFx.Red, 0.9f * pulse * appear);
      for (int i = 0; i < 4; i++)
      {
        float a = rot + i * Mathf.PI * 0.5f;
        var dir = right * Mathf.Cos(a) + up * Mathf.Sin(a);
        _tick[i].SetPosition(0, aim + dir * (r * 0.55f));
        _tick[i].SetPosition(1, aim + dir * (r * 1.35f));
        SetA(_tick[i], FinalSnipeFx.Red, 0.95f * appear);
      }
      BillCircle(_dot, aim, 0.025f, right, up);
      SetA(_dot, FinalSnipeFx.Hot, pulse * appear);

      // 脚下细红圈
      Circle(_ground, Unit.Position, Radius * 0.9f, 0.05f);
      SetA(_ground, FinalSnipeFx.Red, 0.4f * appear);

      // 红外激光：施法者枪口高度 → 目标胸口，入场 0.25 s 内"射出去"，之后轻微闪烁
      // ⚠ 施法者在"增益刚加上"那一刻可能还解析不出来（`buff.Context.MaybeCaster` 为 null）
      //   → 每帧补试一次，认到就固定；一直认不到才不画（避免"激光整场不出现"这种静默失效）。
      if (_caster == null || _caster.View == null) SetCaster(_buff);
      if (_caster != null && _caster.View != null)
      {
        var from = _caster.Position + Vector3.up * (_casterH * 0.7f);
        float reach = Mathf.Clamp01(T / 0.25f);
        _laser.enabled = true;
        _laser.SetPosition(0, from);
        _laser.SetPosition(1, Vector3.Lerp(from, aim, reach));
        float flicker = 0.55f + 0.15f * Mathf.Sin(T * 41f) + 0.10f * Mathf.Sin(T * 17f);
        SetA(_laser, FinalSnipeFx.Red, flicker * appear);
      }
      else _laser.enabled = false;
    }
  }

  /// <summary>开火一瞬：红色粗曳光 + 白色芯 + 命中点白色闪光环，0.45 s 收尽。</summary>
  public class FinalSnipeShotAnim : MonoBehaviour
  {
    private const float Dur = 0.45f;
    private LineRenderer _tracer, _core, _flash;
    private Vector3 _to;
    private float _t;

    internal bool Init(Vector3 from, Vector3 to)
    {
      var mR = Mat(FinalSnipeFx.Red);
      var mW = Mat(FinalSnipeFx.White);
      if (mR == null || mW == null)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 终末狙击曳光放弃：StreakMaterialPlain 无可用材质。");
        return false;
      }
      _to = to;
      _tracer = Line(transform, "RD_FS_Tracer", mR, 2, false, 0.14f);
      _core   = Line(transform, "RD_FS_TracerCore", mW, 2, false, 0.05f);
      _flash  = Line(transform, "RD_FS_Flash", mW, 32, true, 0.07f);
      foreach (var lr in new[] { _tracer, _core })
      {
        lr.SetPosition(0, from);
        lr.SetPosition(1, to);
      }
      return true;
    }

    private void Update()
    {
      _t += Time.deltaTime;
      float k = Mathf.Clamp01(_t / Dur);
      float fade = 1f - k;
      _tracer.widthMultiplier = 0.14f * fade;
      _core.widthMultiplier = 0.05f * fade;
      SetA(_tracer, FinalSnipeFx.Red, 0.9f * fade);
      SetA(_core, FinalSnipeFx.White, fade);
      CamBasis(out var right, out var up);
      BillCircle(_flash, _to, Mathf.Lerp(0.1f, 1.3f, 1f - fade * fade), right, up);
      SetA(_flash, FinalSnipeFx.White, fade);
      if (k >= 1f) Destroy(gameObject);
    }
  }
}
