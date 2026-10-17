// 王冠的回响（10 环·特雷西娅）的专属自绘特效：黑冠显现 + 60尺粉红微尘与记忆光澜（纯视觉）。
// 摒弃生硬的蛛网/织网机械线条，严格还原《明日方舟》特雷西娅（魔王/纯黑的王冠）的标志性演出：
// 漫天轻柔升腾的【粉红微尘】（Memory Stardust）与灵魂光澜，平静而宏大地抚平全战场敌方魔力。
//
// 视觉构成：
//   1. 施法者头顶【魔王之冠（黑冠）】：
//      纤细优美的五尖冠冕轮廓 + 冠心悬浮白金菱形晶核，周身缠绕着升腾的微尘光晕。
//   2. 60 尺【全战场粉红微尘涟漪（Pink Dust Waves）】：
//      三道如水面轻漾的宽幅粉红与白金色宏大涟漪自中心缓出扩散至全场（18.3 米），
//      伴随 28 团在空间中缓缓漂浮、升腾、律动的粉红微尘星芒群，营造特雷西娅专属的幽邃与神圣感。
//   3. 敌方【增益消散微尘灵涡】：
//      范围内每个敌方目标周身绽开柔和的粉红微尘环与向上升腾的光屑，
//      象征其身上的法术增益化作情感与记忆的粉红微尘，消散于天地间。
using System;
using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using UnityEngine;
using static RhodesDoctor.MarkFxKit;

namespace RhodesDoctor
{
  internal static class CrownEchoFx
  {
    // 特雷西娅标志性粉红微尘配色体系
    internal static readonly Color PinkDust       = new Color(1.00f, 0.45f, 0.72f, 1f); // 标志性粉红微尘
    internal static readonly Color RoseStardust   = new Color(1.00f, 0.70f, 0.88f, 1f); // 记忆光澜粉白
    internal static readonly Color SoftWhite      = new Color(1.00f, 0.95f, 0.98f, 1f); // 纯净星芒白
    internal static readonly Color CrownBlackRose = new Color(0.85f, 0.22f, 0.55f, 1f); // 黑冠冠冕深粉
    internal static readonly Color CrownGold      = new Color(1.00f, 0.85f, 0.50f, 1f); // 冠心金辉

    internal const float DefaultRadius = 18.288f; // 60 尺 ≈ 18.3 米
    internal const int MaxEnemiesReact = 10;
    internal const int DustMoteCount = 28; // 全场漂浮粉红微尘星芒数量

    /// <summary>获取 60 尺内活着、可见的敌方单位（用于消散微尘演出，隐形的不标避免透视）。</summary>
    internal static List<UnitEntityData> GetEnemiesInRange(UnitEntityData caster, float range)
    {
      var list = new List<UnitEntityData>();
      try
      {
        foreach (var u in UnitScan.Take().Units)
        {
          if (list.Count >= MaxEnemiesReact) break;
          if (u == null || u == caster || !u.IsInGame || u.View == null) continue;
          if (u.Descriptor.State.IsDead) continue;
          if (u.Descriptor.State.HasCondition(UnitCondition.Invisible)) continue;
          if (!u.IsEnemy(caster)) continue;
          if (caster.HoldingState != null && u.HoldingState != caster.HoldingState) continue;
          var d = u.Position - caster.Position; d.y = 0f;
          if (d.magnitude <= range) list.Add(u);
        }
      }
      catch { }
      return list;
    }

    /// <summary>播放王冠的回响 10 环专属特效（纯视觉，不影响结算）。</summary>
    internal static void Play(UnitEntityData caster, Vector3 center, float radius = DefaultRadius)
    {
      try
      {
        if (caster?.View == null) return;
        var enemies = GetEnemiesInRange(caster, radius);
        var go = new GameObject("RD_CrownEchoFx") { layer = caster.View.gameObject.layer };
        var anim = go.AddComponent<CrownEchoAnim>();
        if (!anim.Init(caster, center, radius, enemies)) UnityEngine.Object.Destroy(go);
        Dbg.Log($"[RhodesDoctor] 王冠的回响·特雷西娅粉红微尘与黑冠显现（作用于 {enemies.Count} 个敌方单位）。");
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 王冠的回响特效失败（效果照常）：\n" + e);
      }
    }
  }

  /// <summary>
  /// 王冠的回响特效主体：黑冠悬浮 + 60 尺粉红微尘扩散与升腾星芒 + 敌方消散微尘灵环（约 3.0 秒）。
  /// </summary>
  public class CrownEchoAnim : MonoBehaviour
  {
    private const float Dur = 3.0f;
    private const float FadeStart = 2.2f;

    private UnitEntityData _caster;
    private Vector3 _center;
    private float _radius;
    private float _t;
    private readonly List<UnitEntityData> _enemies = new();

    // ── 1. 黑冠（魔王之冠）──
    private LineRenderer _crownBase;     // 冠底光环
    private LineRenderer _crownSpikes;   // 五尖冠冕
    private LineRenderer _crownCore;     // 悬浮菱形核
    private LineRenderer _crownDustRing; // 环绕王冠的粉红微尘环

    // ── 2. 全场粉红微尘扩散波 ──
    private LineRenderer _wave1, _wave2, _wave3;
    private LineRenderer _boundaryRing;

    // ── 3. 全场漂浮微尘星芒群 ──
    private struct MoteSeed
    {
      public float Angle, Distance, BaseY, DriftSpeed, SwaySpeed, Radius;
    }
    private readonly MoteSeed[] _moteSeeds = new MoteSeed[CrownEchoFx.DustMoteCount];
    private readonly LineRenderer[] _dustMotes = new LineRenderer[CrownEchoFx.DustMoteCount];

    // ── 4. 敌方增益消散灵环 ──
    private sealed class EnemyDust
    {
      public UnitEntityData Unit;
      public LineRenderer Ring1;
      public LineRenderer Ring2;
      public LineRenderer RiseDust;
    }
    private readonly List<EnemyDust> _enemyDusts = new();

    internal bool Init(UnitEntityData caster, Vector3 center, float radius, List<UnitEntityData> enemies)
    {
      var mPink   = Mat(CrownEchoFx.PinkDust);
      var mRose   = Mat(CrownEchoFx.RoseStardust);
      var mWhite  = Mat(CrownEchoFx.SoftWhite);
      var mCrown  = Mat(CrownEchoFx.CrownBlackRose);
      var mGold   = Mat(CrownEchoFx.CrownGold);
      if (mPink == null || mRose == null || mWhite == null || mCrown == null || mGold == null) return false;

      _caster = caster;
      _center = center;
      _radius = radius;
      if (enemies != null) _enemies.AddRange(enemies);

      // 1. 黑冠
      _crownBase     = Flat(transform, "RD_CE_CrownBase",     mCrown, RingSeg, true, 0.05f);
      _crownSpikes   = Line(transform, "RD_CE_CrownSpikes",   mCrown, 11,     true, 0.055f);
      _crownCore     = Line(transform, "RD_CE_CrownCore",     mGold,  4,      true, 0.040f);
      _crownDustRing = Line(transform, "RD_CE_CrownDustRing", mPink,  RingSeg, true, 0.035f);

      // 2. 60 尺粉红微尘扩散波（如晨雾涟漪般柔和宽阔）
      _wave1        = Flat(transform, "RD_CE_DustWave1", mPink,  RingSeg, true, 0.45f);
      _wave2        = Flat(transform, "RD_CE_DustWave2", mRose,  RingSeg, true, 0.35f);
      _wave3        = Flat(transform, "RD_CE_DustWave3", mWhite, RingSeg, true, 0.20f);
      _boundaryRing = Flat(transform, "RD_CE_BoundRing", mPink,  RingSeg, true, 0.08f);

      // 3. 漂浮粉红微尘种子（随机分布在全场各个半径与高度）
      var rng = new System.Random(42);
      for (int i = 0; i < CrownEchoFx.DustMoteCount; i++)
      {
        float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
        float dist = Mathf.Lerp(1.5f, radius * 0.95f, Mathf.Sqrt((float)rng.NextDouble()));
        float baseY = Mathf.Lerp(0.3f, 2.5f, (float)rng.NextDouble());
        float drift = Mathf.Lerp(0.4f, 1.2f, (float)rng.NextDouble());
        float sway = Mathf.Lerp(1.5f, 3.5f, (float)rng.NextDouble());
        float r = Mathf.Lerp(0.08f, 0.22f, (float)rng.NextDouble());

        _moteSeeds[i] = new MoteSeed
        {
          Angle = angle, Distance = dist, BaseY = baseY,
          DriftSpeed = drift, SwaySpeed = sway, Radius = r
        };
        // 微尘使用正对镜头的微型十字/菱形光斑
        _dustMotes[i] = Line(transform, "RD_CE_Mote" + i, (i % 2 == 0 ? mPink : mRose), 4, true, 0.035f);
      }

      // 4. 敌方目标消散灵环
      for (int i = 0; i < _enemies.Count; i++)
      {
        var e = _enemies[i];
        if (e == null) continue;
        var ed = new EnemyDust
        {
          Unit = e,
          Ring1 = Flat(transform, "RD_CE_ERingA" + i, mPink,  RingSeg, true, 0.040f),
          Ring2 = Line(transform, "RD_CE_ERingB" + i, mRose,  RingSeg, true, 0.035f),
          RiseDust = Line(transform, "RD_CE_EDust" + i, mWhite, 4, true, 0.035f)
        };
        _enemyDusts.Add(ed);
      }

      return true;
    }

    private void Update()
    {
      _t += Time.deltaTime;
      if (_t >= Dur || _caster == null || _caster.View == null) { Destroy(gameObject); return; }

      float master = _t < FadeStart ? 1f : 1f - Mathf.Clamp01((_t - FadeStart) / (Dur - FadeStart));
      var casterPos = _caster.Position;
      float casterH = ModelHeight(_caster);

      CamBasis(out var right, out var up);

      // ════════════ 1. 头顶黑冠与粉红微尘光晕 ════════════
      float crownAppear = Mathf.Clamp01(_t / 0.35f) * master;
      float crownPulse  = 0.88f + 0.12f * Mathf.Sin(_t * 3.2f);
      var crownCenter   = casterPos + Vector3.up * (casterH + 1.15f);

      // 冠底光环
      Circle(_crownBase, crownCenter, 0.50f, 0f);
      SetA(_crownBase, CrownEchoFx.CrownBlackRose, crownAppear * 0.85f);

      // 环绕王冠的粉红微尘柔光环（慢速旋转）
      BillCircle(_crownDustRing, crownCenter + up * 0.18f, 0.65f, right, up, _t * 0.8f);
      SetA(_crownDustRing, CrownEchoFx.PinkDust, crownAppear * 0.70f * crownPulse);

      // 五尖冠冕
      float cw = 0.50f;
      float hCenter = 0.54f;
      float hMid    = 0.38f;
      float hSide   = 0.24f;
      float baseH   = -0.04f;

      var p0  = crownCenter - right * cw            + up * baseH;
      var p1  = crownCenter - right * cw            + up * hSide;
      var p2  = crownCenter - right * (cw * 0.65f)  + up * (baseH + 0.06f);
      var p3  = crownCenter - right * (cw * 0.45f)  + up * hMid;
      var p4  = crownCenter - right * (cw * 0.18f)  + up * (baseH + 0.08f);
      var p5  = crownCenter                         + up * hCenter;
      var p6  = crownCenter + right * (cw * 0.18f)  + up * (baseH + 0.08f);
      var p7  = crownCenter + right * (cw * 0.45f)  + up * hMid;
      var p8  = crownCenter + right * (cw * 0.65f)  + up * (baseH + 0.06f);
      var p9  = crownCenter + right * cw            + up * hSide;
      var p10 = crownCenter + right * cw            + up * baseH;

      _crownSpikes.SetPosition(0,  p0);
      _crownSpikes.SetPosition(1,  p1);
      _crownSpikes.SetPosition(2,  p2);
      _crownSpikes.SetPosition(3,  p3);
      _crownSpikes.SetPosition(4,  p4);
      _crownSpikes.SetPosition(5,  p5);
      _crownSpikes.SetPosition(6,  p6);
      _crownSpikes.SetPosition(7,  p7);
      _crownSpikes.SetPosition(8,  p8);
      _crownSpikes.SetPosition(9,  p9);
      _crownSpikes.SetPosition(10, p10);
      SetA(_crownSpikes, CrownEchoFx.CrownBlackRose, crownAppear * crownPulse);

      // 冠心悬浮白金菱形
      float dSize = 0.08f * crownPulse;
      var dC = crownCenter + up * 0.18f;
      _crownCore.SetPosition(0, dC + up * dSize);
      _crownCore.SetPosition(1, dC + right * (dSize * 0.7f));
      _crownCore.SetPosition(2, dC - up * dSize);
      _crownCore.SetPosition(3, dC - right * (dSize * 0.7f));
      SetA(_crownCore, CrownEchoFx.CrownGold, crownAppear * 0.95f);

      // ════════════ 2. 60 尺粉红微尘扩散光澜 ════════════
      // 第一道主粉红光澜（0 ~ 1.3s 扩至 18.3m）
      float w1 = Mathf.Clamp01(_t / 1.3f);
      float r1 = Mathf.Lerp(0.5f, _radius, 1f - (1f - w1) * (1f - w1));
      Circle(_wave1, casterPos, r1, 0.08f);
      SetA(_wave1, CrownEchoFx.PinkDust, (1f - w1) * 0.85f * master);

      // 第二道玫瑰粉微尘光澜（延迟 0.18s）
      float w2 = Mathf.Clamp01((_t - 0.18f) / 1.3f);
      if (_t > 0.18f)
      {
        _wave2.enabled = true;
        float r2 = Mathf.Lerp(0.5f, _radius * 0.96f, 1f - (1f - w2) * (1f - w2));
        Circle(_wave2, casterPos, r2, 0.10f);
        SetA(_wave2, CrownEchoFx.RoseStardust, (1f - w2) * 0.75f * master);
      }
      else _wave2.enabled = false;

      // 第三道微光核心涟漪（延迟 0.35s，近程泛光）
      float w3 = Mathf.Clamp01((_t - 0.35f) / 1.1f);
      if (_t > 0.35f)
      {
        _wave3.enabled = true;
        float r3 = Mathf.Lerp(0.3f, _radius * 0.65f, 1f - (1f - w3) * (1f - w3));
        Circle(_wave3, casterPos, r3, 0.12f);
        SetA(_wave3, CrownEchoFx.SoftWhite, (1f - w3) * 0.60f * master);
      }
      else _wave3.enabled = false;

      // 60 尺外周粉金边界环（平滑淡入淡出）
      Circle(_boundaryRing, casterPos, _radius, 0.15f);
      SetA(_boundaryRing, CrownEchoFx.PinkDust, Mathf.Clamp01(_t / 0.8f) * 0.55f * master);

      // ════════════ 3. 全场升腾漂浮的粉红微尘星芒群 ════════════
      for (int i = 0; i < CrownEchoFx.DustMoteCount; i++)
      {
        var seed = _moteSeeds[i];
        // 微尘随着时间缓缓上升并随风微晃
        float currentY = seed.BaseY + _t * seed.DriftSpeed;
        float swayX = Mathf.Sin(_t * seed.SwaySpeed + seed.Angle) * 0.25f;
        float swayZ = Mathf.Cos(_t * seed.SwaySpeed * 0.8f + seed.Angle) * 0.25f;

        var motePos = casterPos
          + new Vector3(Mathf.Cos(seed.Angle) * seed.Distance + swayX, currentY, Mathf.Sin(seed.Angle) * seed.Distance + swayZ);

        // 微尘正对镜头呈现柔和的小星芒
        float s = seed.Radius * (0.8f + 0.2f * Mathf.Sin(_t * 4f + i));
        _dustMotes[i].SetPosition(0, motePos + up * s);
        _dustMotes[i].SetPosition(1, motePos + right * s);
        _dustMotes[i].SetPosition(2, motePos - up * s);
        _dustMotes[i].SetPosition(3, motePos - right * s);

        float moteAlpha = Mathf.Sin(Mathf.Clamp01(_t / Dur) * Mathf.PI) * master * 0.85f;
        SetA(_dustMotes[i], (i % 2 == 0 ? CrownEchoFx.PinkDust : CrownEchoFx.RoseStardust), moteAlpha);
      }

      // ════════════ 4. 敌方目标增益消散灵环与微尘 ════════════
      float enemyAppear = Mathf.Clamp01((_t - 0.25f) / 0.35f) * master;
      for (int i = 0; i < _enemyDusts.Count; i++)
      {
        var ed = _enemyDusts[i];
        if (ed.Unit == null || ed.Unit.View == null || !ed.Unit.IsInGame)
        {
          ed.Ring1.enabled = false;
          ed.Ring2.enabled = false;
          ed.RiseDust.enabled = false;
          continue;
        }

        var uPos = ed.Unit.Position;
        float uH = ModelHeight(ed.Unit);
        var uChest = uPos + Vector3.up * (uH * 0.60f);

        // 脚底粉红微尘柔光环
        ed.Ring1.enabled = enemyAppear > 0f;
        Circle(ed.Ring1, uPos, 1.1f, 0.06f);
        SetA(ed.Ring1, CrownEchoFx.PinkDust, enemyAppear * 0.70f);

        // 胸口正对镜头的玫瑰色灵涡微旋
        ed.Ring2.enabled = enemyAppear > 0f;
        BillCircle(ed.Ring2, uChest, 0.75f, right, up, _t * 2.5f);
        SetA(ed.Ring2, CrownEchoFx.RoseStardust, enemyAppear * 0.85f);

        // 目标头顶升腾的消散粉红微尘星芒
        ed.RiseDust.enabled = enemyAppear > 0f;
        var dustTop = uPos + Vector3.up * (uH + 0.3f + _t * 0.6f);
        float starS = 0.12f;
        ed.RiseDust.SetPosition(0, dustTop + up * starS);
        ed.RiseDust.SetPosition(1, dustTop + right * starS);
        ed.RiseDust.SetPosition(2, dustTop - up * starS);
        ed.RiseDust.SetPosition(3, dustTop - right * starS);
        SetA(ed.RiseDust, CrownEchoFx.SoftWhite, enemyAppear * 0.90f);
      }
    }
  }
}
