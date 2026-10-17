// 王冠的回响（10 环·特雷西娅）的专属自绘特效：黑冠显现 + 60尺精神织网 + 敌方增益剥离丝线（纯视觉）。
// 彻底取代原来借的原版"幻影杀手"黑紫鬼影（完全脱离黑冠/特雷西娅精神织网概念）。
//
// 10 环规格（全场 60 尺 = 18.3 米半径）：
//   1. 施法者头顶【黑冠（魔王之冠）】：
//      悬浮于施法者头顶 3.4 米处，自绘标志性五角锋锐王冠轮廓（中央主峰 + 两侧副峰与外角）
//      + 王冠底环 + 冠心悬浮菱形晶核，暗紫绯红与白金交织脉动。
//   2. 60 尺【精神织网（Mental Loom）】：
//      自中心以 12 道辐射丝线射向全战场（360度各 30度），并在 4.5m / 9.0m / 13.5m / 18.0m
//      处连接成 4 层十二边形同心同频的精神织网；伴随两道绯红与亮白共鸣扩散波，
//      丝线带有波长起伏的谐波明暗，宛如特雷西娅操纵全战场情感与意识的心灵之网。
//   3. 范围敌人【增益剥离索取】：
//      自黑冠垂下一道道绯红精神丝线精准牵引圈内每一个被影响的敌人，
//      目标周身绽开双层逆向回旋的剥离灵环，将夺取/驱散的法术增益化作丝线抽回。
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
    internal static readonly Color RoseMagenta     = new Color(0.92f, 0.18f, 0.58f, 1f); // 特雷西娅绯红/品红
    internal static readonly Color SpiritWhite     = new Color(1.00f, 0.90f, 0.96f, 1f); // 精神丝线亮白
    internal static readonly Color TwilightViolet  = new Color(0.55f, 0.12f, 0.75f, 1f); // 萨卡兹深暗紫
    internal static readonly Color CrownGold       = new Color(1.00f, 0.84f, 0.42f, 1f); // 王冠金辉

    internal const float DefaultRadius = 18.288f; // 60 尺 ≈ 18.3 米
    internal const int MaxEnemiesTethered = 10;

    /// <summary>获取 60 尺内活着且可见的敌人列表（用于丝线牵引剥离演出）。</summary>
    internal static List<UnitEntityData> GetEnemiesInRange(UnitEntityData caster, float range)
    {
      var list = new List<UnitEntityData>();
      try
      {
        foreach (var u in UnitScan.Take().Units)
        {
          if (list.Count >= MaxEnemiesTethered) break;
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

    /// <summary>播放王冠的回响 10 环史诗特效（纯视觉，不影响结算）。</summary>
    internal static void Play(UnitEntityData caster, Vector3 center, float radius = DefaultRadius)
    {
      try
      {
        if (caster?.View == null) return;
        var enemies = GetEnemiesInRange(caster, radius);
        var go = new GameObject("RD_CrownEchoFx") { layer = caster.View.gameObject.layer };
        var anim = go.AddComponent<CrownEchoAnim>();
        if (!anim.Init(caster, center, radius, enemies)) UnityEngine.Object.Destroy(go);
        Dbg.Log($"[RhodesDoctor] 王冠的回响·黑冠精神织网已展开（连结 {enemies.Count} 个敌方单位）。");
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 王冠的回响特效失败（效果照常）：\n" + e);
      }
    }
  }

  /// <summary>
  /// 王冠的回响特效主体：黑冠悬浮 + 60尺精神织网扩散 + 敌方丝线剥离（约 3.2 秒）。
  /// </summary>
  public class CrownEchoAnim : MonoBehaviour
  {
    private const float Dur = 3.2f;
    private const float FadeStart = 2.4f;
    private const int Radials = 12;
    private const int WebLayers = 4;

    private UnitEntityData _caster;
    private Vector3 _center;
    private float _radius;
    private float _t;
    private readonly List<UnitEntityData> _enemies = new();

    // ── 1. 黑冠（魔王之冠）──
    private LineRenderer _crownBase;     // 冠底圆环
    private LineRenderer _crownSpikes;   // 五尖冠冕尖峰
    private LineRenderer _crownCore;     // 冠心悬浮菱形
    private LineRenderer _crownPillar;   // 头顶光柱引导

    // ── 2. 60 尺精神织网 ──
    private LineRenderer _wave1, _wave2; // 扩散波
    private LineRenderer _boundary;      // 外周边界虚线
    private readonly LineRenderer[] _radialLines = new LineRenderer[Radials];
    private readonly LineRenderer[] _webRings = new LineRenderer[WebLayers];

    // ── 3. 敌方丝线索取 ──
    private sealed class TetherData
    {
      public UnitEntityData Unit;
      public LineRenderer Line;
      public LineRenderer Spiral;
    }
    private readonly List<TetherData> _tethers = new();

    internal bool Init(UnitEntityData caster, Vector3 center, float radius, List<UnitEntityData> enemies)
    {
      var mRose   = Mat(CrownEchoFx.RoseMagenta);
      var mWhite  = Mat(CrownEchoFx.SpiritWhite);
      var mViolet = Mat(CrownEchoFx.TwilightViolet);
      var mGold   = Mat(CrownEchoFx.CrownGold);
      if (mRose == null || mWhite == null || mViolet == null || mGold == null) return false;

      _caster = caster;
      _center = center;
      _radius = radius;
      if (enemies != null) _enemies.AddRange(enemies);

      // 1. 黑冠
      _crownBase   = Flat(transform, "RD_CE_CrownBase",   mGold, RingSeg, true, 0.05f);
      _crownSpikes = Line(transform, "RD_CE_CrownSpikes", mRose, 11,     true, 0.06f);
      _crownCore   = Line(transform, "RD_CE_CrownCore",   mWhite, 4,     true, 0.04f);
      _crownPillar = Line(transform, "RD_CE_CrownPillar", mViolet, 2,    false, 0.08f);

      // 2. 精神织网
      _wave1    = Flat(transform, "RD_CE_Wave1", mRose,  RingSeg, true, 0.35f);
      _wave2    = Flat(transform, "RD_CE_Wave2", mWhite, RingSeg, true, 0.15f);
      _boundary = Flat(transform, "RD_CE_Bound", mGold,  RingSeg, true, 0.08f);

      for (int i = 0; i < Radials; i++)
        _radialLines[i] = Flat(transform, "RD_CE_Radial" + i, mRose, 2, false, 0.045f);

      for (int l = 0; l < WebLayers; l++)
        _webRings[l] = Flat(transform, "RD_CE_WebRing" + l, mWhite, Radials, true, 0.040f);

      // 3. 敌方索取丝线
      for (int i = 0; i < _enemies.Count; i++)
      {
        var e = _enemies[i];
        if (e == null) continue;
        var td = new TetherData
        {
          Unit = e,
          Line = Line(transform, "RD_CE_Tether" + i, mRose, 2, false, 0.04f),
          Spiral = Line(transform, "RD_CE_Spiral" + i, mWhite, RingSeg, true, 0.035f)
        };
        _tethers.Add(td);
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

      // ════════════ 1. 头顶黑冠 ════════════
      float crownAppear = Mathf.Clamp01(_t / 0.35f) * master;
      float crownPulse  = 0.85f + 0.15f * Mathf.Sin(_t * 3.5f);
      var crownCenter   = casterPos + Vector3.up * (casterH + 1.2f);

      // 贯穿头顶的淡紫导光
      _crownPillar.SetPosition(0, casterPos + Vector3.up * casterH);
      _crownPillar.SetPosition(1, crownCenter + Vector3.up * 0.4f);
      SetA(_crownPillar, CrownEchoFx.TwilightViolet, crownAppear * 0.45f * crownPulse);

      // 冠底光环
      Circle(_crownBase, crownCenter, 0.52f, 0f);
      SetA(_crownBase, CrownEchoFx.CrownGold, crownAppear * 0.85f);

      // 五尖冠冕（面对相机立起）
      // 形状：5 个向上三角形山峰，中间最高，两侧对称递降
      float cw = 0.52f;
      float hCenter = 0.58f;
      float hMid    = 0.42f;
      float hSide   = 0.28f;
      float baseH   = -0.05f;

      var p0  = crownCenter - right * cw            + up * baseH;
      var p1  = crownCenter - right * cw            + up * hSide;   // 左外尖
      var p2  = crownCenter - right * (cw * 0.65f)  + up * (baseH + 0.08f);
      var p3  = crownCenter - right * (cw * 0.45f)  + up * hMid;    // 左副尖
      var p4  = crownCenter - right * (cw * 0.18f)  + up * (baseH + 0.10f);
      var p5  = crownCenter                         + up * hCenter; // 中央主峰
      var p6  = crownCenter + right * (cw * 0.18f)  + up * (baseH + 0.10f);
      var p7  = crownCenter + right * (cw * 0.45f)  + up * hMid;    // 右副尖
      var p8  = crownCenter + right * (cw * 0.65f)  + up * (baseH + 0.08f);
      var p9  = crownCenter + right * cw            + up * hSide;   // 右外尖
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
      SetA(_crownSpikes, CrownEchoFx.RoseMagenta, crownAppear * crownPulse);

      // 冠心悬浮白金菱形
      float dSize = 0.09f * crownPulse;
      var dC = crownCenter + up * 0.20f;
      _crownCore.SetPosition(0, dC + up * dSize);
      _crownCore.SetPosition(1, dC + right * (dSize * 0.7f));
      _crownCore.SetPosition(2, dC - up * dSize);
      _crownCore.SetPosition(3, dC - right * (dSize * 0.7f));
      SetA(_crownCore, CrownEchoFx.SpiritWhite, crownAppear * 0.95f);

      // ════════════ 2. 60 尺精神织网扩散 ════════════
      // 扩散波 1：0 ~ 1.2s 扩至 18.3m
      float wave1Progress = Mathf.Clamp01(_t / 1.1f);
      float r1 = Mathf.Lerp(0.5f, _radius, 1f - (1f - wave1Progress) * (1f - wave1Progress));
      Circle(_wave1, casterPos, r1, 0.08f);
      SetA(_wave1, CrownEchoFx.RoseMagenta, (1f - wave1Progress) * 0.9f * master);

      // 扩散波 2（白银光晕，略迟 0.15s）
      float wave2T = _t - 0.15f;
      if (wave2T > 0f)
      {
        float w2p = Mathf.Clamp01(wave2T / 1.1f);
        float r2 = Mathf.Lerp(0.5f, _radius * 1.02f, 1f - (1f - w2p) * (1f - w2p));
        _wave2.enabled = true;
        Circle(_wave2, casterPos, r2, 0.10f);
        SetA(_wave2, CrownEchoFx.SpiritWhite, (1f - w2p) * 0.8f * master);
      }
      else _wave2.enabled = false;

      // 60 尺边界金圈（18.3 米处慢速自转）
      Circle(_boundary, casterPos, _radius, 0.12f);
      SetA(_boundary, CrownEchoFx.CrownGold, Mathf.Clamp01(_t / 0.8f) * 0.7f * master);

      // 12 道辐射丝线（从中心辐射向外）
      float webGrowth = Mathf.Clamp01(_t / 0.7f); // 0.7s 内织出完整大网
      float currentR = _radius * (1f - (1f - webGrowth) * (1f - webGrowth));

      for (int i = 0; i < Radials; i++)
      {
        float angle = i * Mathf.PI * 2f / Radials;
        var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        _radialLines[i].SetPosition(0, casterPos + Vector3.up * 0.06f);
        _radialLines[i].SetPosition(1, casterPos + dir * currentR + Vector3.up * 0.06f);

        // 丝线谐波闪烁（像心灵神经脉动）
        float waveFlicker = 0.65f + 0.35f * Mathf.Sin(_t * 4.5f - i * 0.5f);
        SetA(_radialLines[i], CrownEchoFx.RoseMagenta, waveFlicker * master * 0.8f);
      }

      // 4 层同心十二边形横向交织线
      float[] ringFractions = { 0.25f, 0.50f, 0.75f, 1.00f };
      for (int l = 0; l < WebLayers; l++)
      {
        float targetRingR = _radius * ringFractions[l];
        if (currentR < targetRingR * 0.5f)
        {
          _webRings[l].enabled = false;
          continue;
        }
        _webRings[l].enabled = true;
        float actualRingR = Mathf.Min(currentR, targetRingR);

        for (int i = 0; i < Radials; i++)
        {
          float angle = i * Mathf.PI * 2f / Radials;
          var pt = casterPos + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * actualRingR
                   + Vector3.up * 0.07f;
          _webRings[l].SetPosition(i, pt);
        }
        float ringPulse = 0.55f + 0.45f * Mathf.Sin(_t * 3f + l * 1.2f);
        SetA(_webRings[l], CrownEchoFx.SpiritWhite, ringPulse * master * 0.75f);
      }

      // ════════════ 3. 敌方丝线剥离与回抽 ════════════
      float tetherAppear = Mathf.Clamp01((_t - 0.35f) / 0.4f) * master;
      for (int i = 0; i < _tethers.Count; i++)
      {
        var td = _tethers[i];
        if (td.Unit == null || td.Unit.View == null || !td.Unit.IsInGame)
        {
          td.Line.enabled = false;
          td.Spiral.enabled = false;
          continue;
        }

        var uPos = td.Unit.Position;
        float uH = ModelHeight(td.Unit);
        var uTarget = uPos + Vector3.up * (uH * 0.65f);

        // 自黑冠连向敌人胸口的绯红精神丝线
        td.Line.enabled = tetherAppear > 0f;
        td.Line.SetPosition(0, crownCenter);
        td.Line.SetPosition(1, uTarget);
        float tetherPulse = 0.7f + 0.3f * Mathf.Sin(_t * 6f + i * 1.5f);
        SetA(td.Line, CrownEchoFx.RoseMagenta, tetherAppear * tetherPulse);

        // 敌人周身剥离回旋灵环（逆向旋转）
        td.Spiral.enabled = tetherAppear > 0f;
        float spiralAngle = -_t * 4.5f + i * 2f;
        BillCircle(td.Spiral, uTarget, 0.65f, right, up, spiralAngle);
        SetA(td.Spiral, CrownEchoFx.SpiritWhite, tetherAppear * 0.85f);
      }
    }
  }
}
