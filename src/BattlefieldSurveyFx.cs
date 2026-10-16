// 战场测绘（0 环·自身预言）的专属特效：PRTS 战场扫描（纯视觉，不影响结算）。
// 取代原来借的"真实打击"（自身闪一下金光）。
//
// ⚠ 生命周期 = **跟着增益走**（作者 10-16 定案）：原来是"施法时一次性演 2.4 秒"，
//   作者反馈"特效它没有持续 / 要持续一分钟才结束"⇒ 现在由 `MarkFxTracker` 订阅
//   `RhodesDoctorBuffBattlefieldSurvey`（战场测绘自己的增益，10 轮 = 1 分钟），
//   **增益在，扫描就在**；增益到期 / 被驱散 → 0.3 s 淡出（所以"一分钟"不用写死，
//   时长由增益自己的持续决定；将来改轮数也不用动这里）。
//
// 节奏：每 `CycleDur` 秒一轮扫描（两道扫描波 + 雷达线一圈），**循环到增益结束**；
//   30 尺边界环与敌人标点持续存在（标点每轮波扫到时重新"闪一下"）。
//
// 线条基础件与材质复用 MarkFxKit（SwordStreakFx.StreakMaterialPlain，实机验证能上屏）。
using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Buffs;
using UnityEngine;
using static RhodesDoctor.MarkFxKit;

namespace RhodesDoctor
{
  internal static class BattlefieldSurveyFx
  {
    internal static readonly Color Cyan  = new Color(0.25f, 0.85f, 1.00f, 1f);
    internal static readonly Color White = new Color(0.85f, 1.00f, 1.00f, 1f);
    internal static readonly Color Amber = new Color(1.00f, 0.72f, 0.20f, 1f);

    /// <summary>扫描半径：30 尺。</summary>
    internal const float Range = 9.1f;
    internal const int MaxContacts = 12;

    /// <summary>
    /// 增益事件订阅（<see cref="MarkFxKit"/> 那套）：增益加上 → 生成；移除 → 0.3 s 淡出。
    /// 名字按 `CreateSpellBuff` 规则；GUID = <c>Guids.Buff(2)</c>（`Specs[2].Key == "BattlefieldSurvey"`，
    /// `CreateSpellBuff(s, i)` 用的就是 Specs 下标）。两条取"或"，理由见 <see cref="MarkFxTracker.Is"/>。
    /// </summary>
    internal static readonly MarkFxTracker Tracker = new MarkFxTracker(
      "RhodesDoctorBuffBattlefieldSurvey", "战场测绘·扫描特效", go => go.AddComponent<BattlefieldSurveyAnim>(),
      Guids.Buff(2));

    /// <summary>30 尺内、活着、可见的敌人（隐形的不标，免得变相揭示）。</summary>
    internal static List<UnitEntityData> Contacts(UnitEntityData caster)
    {
      var list = new List<UnitEntityData>();
      try
      {
        // 单位池统一走 UnitScan：它是 `Game.Instance.State.Units` ∪ 跨场景队伍 ∪ Party，
        // 而且**挡掉了 `Descriptor == null` 的半成品单位**——直接遍历 State.Units 时，
        // 那种单位一碰 `Descriptor.*` 就抛异常，把整个动作序列打断（见 UnitScan.cs 的注释）。
        foreach (var u in UnitScan.Take().Units)
        {
          if (list.Count >= MaxContacts) break;
          if (u == null || u == caster || !u.IsInGame || u.View == null) continue;
          if (u.Descriptor.State.IsDead) continue;
          if (!u.IsEnemy(caster)) continue;
          if (u.Descriptor.State.HasCondition(UnitCondition.Invisible)) continue;
          // ⚠ 这个池子是**全局**的：别的区域残留的单位也可能在**世界坐标**上撞进 9.1 m，
          //   所以只认与施法者**同一场景状态**的单位（同场战斗必然一致）。
          if (caster.HoldingState != null && u.HoldingState != caster.HoldingState) continue;
          var d = u.Position - caster.Position; d.y = 0f;
          if (d.magnitude <= Range) list.Add(u);
        }
      }
      catch { }
      return list;
    }
  }

  /// <summary>
  /// PRTS 战场扫描本体。继承 <see cref="MarkFxBase"/>：跟随 / 每秒自检增益 / 淡出都由基类管，
  /// 这里只负责 <see cref="Build"/>（建线）与 <see cref="Tick"/>（每帧摆位）。
  /// </summary>
  public class BattlefieldSurveyAnim : MarkFxBase
  {
    // 一轮扫描的周期：两道扫描波（0 → 0.9 s，第二道晚 0.15 s）+ 雷达线一圈（0.1 → 1.5 s），
    // 剩下约 1.1 s 让边界环与标点"喘口气"，然后下一轮。
    private const float CycleDur = 2.6f;
    private const float WaveDur = 0.9f, WaveGap = 0.15f;
    private const float SweepStart = 0.1f, SweepDur = 1.4f;
    private const int Dashes = 12, DashPts = 6, Fan = 6;
    private const float Gy = 0.05f;

    /// <summary>
    /// 边界虚线环的**线宽**（米）。⚠ 这里踩过坑（作者 10-16：**"1.5 秒后我虚线都没看到"**）：
    /// 同样是 3.5 cm 的线，**在 1.9 m 处看得见、在 9.1 m 处就是亚像素**——而
    /// `StreakMaterialPlain` 用的是**软边贴图**（`sin²` 横跨带宽），有效亮芯只有宽度的 1/3 左右。
    /// 现在对齐**已实机确认可见**的同类元素：重构的扫描环也是 9.1 m 半径，线宽 **0.35 m**
    /// （`MassRestoreFx.PlayReconstruction` → `Ring(..., width: 0.35f, ...)`）。**改这个数之前先想"屏幕上几个像素"。**
    /// </summary>
    private const float BoundWidth = 0.30f;

    /// <summary>
    /// 边界环离**施法者脚下**的高度（米）——**固定值，不贴地形**（作者 10-16 要求把贴地去掉）。
    /// 为什么曾一度贴地形：半径 9.1 m，地面常常和施法者脚下不同高，固定高度会被地形吃掉一块；
    /// 但贴地形会让环随地面起伏（看着是"歪"的），作者看过后要求去掉。
    /// ⇒ 只留一个固定抬升（避开地面贴花 / z-fighting）。**若某处地形抬高把它吃掉，就调大这个数。**
    /// </summary>
    private const float BoundY = 0.15f;

    private sealed class Contact
    {
      public UnitEntityData Unit; public float T0, H;
      public LineRenderer Ring, Diamond;
    }

    private float _facing;
    private LineRenderer _pop;
    private readonly LineRenderer[] _wave = new LineRenderer[2];
    private readonly LineRenderer[] _hair = new LineRenderer[4];
    private readonly LineRenderer[] _dash = new LineRenderer[Dashes];
    private readonly LineRenderer[] _fan = new LineRenderer[Fan];
    private readonly List<Contact> _contacts = new();

    private bool _diagLogged;

    private static float EaseOut(float k) => 1f - (1f - k) * (1f - k);

    protected override bool Build(Buff buff)
    {
      var mC = Mat(BattlefieldSurveyFx.Cyan);
      var mW = Mat(BattlefieldSurveyFx.White);
      var mA = Mat(BattlefieldSurveyFx.Amber);
      if (mC == null || mW == null || mA == null)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 战场测绘扫描放弃：StreakMaterialPlain 无可用材质。");
        return false;
      }

      // 施法者朝向 = 雷达线的起始角（增益持有者就是施法者：个人距离的自增益）
      var f = Unit.OrientationDirection; f.y = 0f;
      _facing = f.sqrMagnitude > 1e-4f ? Mathf.Atan2(f.z, f.x) : 0f;

      _pop = Flat(transform, "RD_BS_Pop", mW, RingSeg, true, 0.05f);
      _wave[0] = Flat(transform, "RD_BS_Wave", mC, RingSeg, true, 0.05f);
      _wave[1] = Flat(transform, "RD_BS_Wave2", mW, RingSeg, true, 0.025f);
      for (int i = 0; i < 4; i++) _hair[i] = Flat(transform, "RD_BS_Hair", mC, 2, false, 0.04f);
      for (int i = 0; i < Dashes; i++) _dash[i] = Flat(transform, "RD_BS_Dash", mC, DashPts, false, BoundWidth);
      for (int i = 0; i < Fan; i++) _fan[i] = Flat(transform, "RD_BS_Sweep", i == 0 ? mW : mC, 2, false, i == 0 ? 0.045f : 0.03f);

      foreach (var u in BattlefieldSurveyFx.Contacts(Unit))
      {
        var d = u.Position - Unit.Position; d.y = 0f;
        _contacts.Add(new Contact
        {
          Unit = u,
          T0 = d.magnitude / BattlefieldSurveyFx.Range * WaveDur, // 第一轮波扫到它的时刻
          H = ModelHeight(u),
          Ring = Flat(transform, "RD_BS_Ping", mA, RingSeg, true, 0.04f),
          Diamond = Line(transform, "RD_BS_Blip", mA, 4, true, 0.04f),
        });
      }
      return true;
    }

    protected override void Tick(float dt)
    {
      var c = Unit.Position;
      float R = BattlefieldSurveyFx.Range;
      float master = Master;
      float cyc = Mathf.Repeat(T, CycleDur);   // 本轮扫描已过的秒数（波/雷达线按它循环）

      // ① 脚下弹出的小环（只在开局那一下）
      float kp = Mathf.Clamp01(T / 0.3f);
      Circle(_pop, c, Mathf.Lerp(0.3f, 1.1f, EaseOut(kp)), Gy);
      SetA(_pop, BattlefieldSurveyFx.White, 0.9f * (1f - kp) * master);

      // ② 两道扫描波扩到 30 尺（每轮一次）
      for (int k = 0; k < 2; k++)
      {
        float t0 = k * WaveGap;
        float kw = Mathf.Repeat(T - t0, CycleDur) / WaveDur;
        bool on = T >= t0 && kw < 1f;
        _wave[k].enabled = on;
        if (!on) continue;
        Circle(_wave[k], c, Mathf.Lerp(0.5f, R, EaseOut(kw)), Gy);
        SetA(_wave[k], k == 0 ? BattlefieldSurveyFx.Cyan : BattlefieldSurveyFx.White,
          (k == 0 ? 0.8f : 0.45f) * (1f - kw * kw) * master);
      }

      // ③ 四根方位刻线（以施法者朝向为基准，常驻）
      float hairA = Mathf.Clamp01((T - 0.15f) / 0.2f) * master;
      for (int i = 0; i < 4; i++)
      {
        float a = _facing + i * Mathf.PI * 0.5f;
        var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        _hair[i].SetPosition(0, c + dir * 1.15f + Vector3.up * Gy);
        _hair[i].SetPosition(1, c + dir * 1.60f + Vector3.up * Gy);
        SetA(_hair[i], BattlefieldSurveyFx.Cyan, 0.8f * hairA);
      }

      // ④ 30 尺边界虚线环（常驻 + 缓慢旋转）
      // ⚠ 半径 9.1 m ⇒ 线宽/亮度必须按**屏幕像素**给（见 BoundWidth），否则整圈是亚像素、等于没画。
      //   高度是**固定值**（施法者脚 + BoundY），**不贴地形**（作者 10-16 要求去掉贴地）。
      float bndA = Mathf.Clamp01((T - 0.35f) / 0.25f) * master;
      float rot = T * 0.25f, span = Mathf.PI * 2f / Dashes * 0.62f;
      for (int i = 0; i < Dashes; i++)
      {
        float a0 = rot + i * Mathf.PI * 2f / Dashes;
        for (int p = 0; p < DashPts; p++)
        {
          float a = a0 + span * p / (DashPts - 1f);
          _dash[i].SetPosition(p, new Vector3(
            c.x + Mathf.Cos(a) * R, c.y + BoundY, c.z + Mathf.Sin(a) * R));
        }
        SetA(_dash[i], BattlefieldSurveyFx.Cyan, 0.9f * bndA);
      }
      if (!_diagLogged && T >= 1.0f) { _diagLogged = true; LogBoundDiag(c); }

      // ⑤ 雷达扫描线：每轮从朝向起顺时针一圈，后面拖扇形余辉
      float ks = Mathf.Repeat(T - SweepStart, CycleDur) / SweepDur;
      float sweepA = (T >= SweepStart && ks < 1f) ? 0.9f : 0f;
      float ang = _facing - ks * Mathf.PI * 2f;
      for (int i = 0; i < Fan; i++)
      {
        float a = ang + i * 0.07f; // 角度递减地转，余辉在 + 方向
        var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        _fan[i].SetPosition(0, c + dir * 0.4f + Vector3.up * Gy);
        _fan[i].SetPosition(1, c + dir * R + Vector3.up * Gy);
        float fall = 1f - i / (float)Fan;
        SetA(_fan[i], i == 0 ? BattlefieldSurveyFx.White : BattlefieldSurveyFx.Cyan,
          sweepA * fall * fall * master);
      }

      // ⑥ 敌人标点：第一轮波扫到时出现，之后**每轮波重新扫到时再闪一下**（常驻到最后）
      // ⚠ 单独 try/catch：基类的 Tick 一旦抛异常会**把整份特效销毁**，而这里要跑一整分钟，
      //   不能让一个已经死掉/被移除的单位把整场扫描带走。
      try
      {
        CamBasis(out var camRight, out _);
        var right = FlatRight(camRight);
        foreach (var ct in _contacts)
        {
          bool on = T >= ct.T0 && ct.Unit != null && ct.Unit.View != null;
          ct.Ring.enabled = on; ct.Diamond.enabled = on;
          if (!on) continue;
          float cw = Mathf.Repeat(T - ct.T0, CycleDur);   // 本轮到"波扫过它"之后的秒数
          float k = Mathf.Clamp01(cw / 0.35f);
          var p = ct.Unit.Position;
          float blink = k < 1f ? (Mathf.Repeat(cw * 14f, 1f) > 0.5f ? 1f : 0.4f) : 1f;
          Circle(ct.Ring, p, Mathf.Lerp(1.4f, 0.6f, EaseOut(k)), Gy);
          SetA(ct.Ring, BattlefieldSurveyFx.Amber, 0.9f * blink * master);

          var top = p + Vector3.up * (ct.H + 0.35f);
          const float d = 0.16f;
          ct.Diamond.SetPosition(0, top + Vector3.up * d);
          ct.Diamond.SetPosition(1, top + right * d * 0.75f);
          ct.Diamond.SetPosition(2, top - Vector3.up * d * 1.3f);
          ct.Diamond.SetPosition(3, top - right * d * 0.75f);
          SetA(ct.Diamond, BattlefieldSurveyFx.Amber, 0.95f * Mathf.Clamp01(k * 2f) * master);
        }
      }
      catch { /* 单个标点出问题不影响整场扫描 */ }
    }

    // ───────────── 调试自检 ─────────────

    /// <summary>
    /// 一次性自检（**release 也可见**，每次施法一行）：环有多少顶点真的在屏幕内、相机离环多远。
    /// 起因：作者两次反馈"30 尺虚线环没出来"却查不出原因 ⇒ 让日志自己把答案说出来，而不是继续靠推测定位
    /// （"在画面外"与"根本没画"在这一行里长得完全不一样）。
    /// 地面采样那几项随"贴地形"一起去掉了（作者 10-16 要求固定高度）。
    /// </summary>
    private void LogBoundDiag(Vector3 c)
    {
      try
      {
        var cam = Camera.main;
        int onScreen = 0;
        float near = float.MaxValue;
        for (int i = 0; i < RingSeg; i++)
        {
          float a = i * Mathf.PI * 2f / RingSeg;
          var p = new Vector3(c.x + Mathf.Cos(a) * BattlefieldSurveyFx.Range,
            c.y + BoundY, c.z + Mathf.Sin(a) * BattlefieldSurveyFx.Range);
          if (cam != null)
          {
            var v = cam.WorldToViewportPoint(p);
            if (v.z > 0f && v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f) onScreen++;
            near = Mathf.Min(near, Vector3.Distance(cam.transform.position, p));
          }
        }
        Main.Mod.Logger.Log($"[RhodesDoctor] 战场测绘·边界环自检：半径={BattlefieldSurveyFx.Range:F1} m"
          + $" 线宽={BoundWidth:F2} m 高度=脚+{BoundY:F2} m（固定、不贴地形）；屏幕内顶点 {onScreen}/{RingSeg}"
          + $"；相机距环最近 {(cam != null ? near.ToString("F1") : "无相机")} m。");
      }
      catch (Exception e) { Main.Mod.Logger.Error("[RhodesDoctor] 边界环自检失败：\n" + e); }
    }
  }
}
