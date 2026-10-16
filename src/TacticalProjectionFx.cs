// 战术标记（0 环）的专属特效：PRTS 战场标定 / 战术投影（纯视觉，不影响结算）。
//
// 取代原来借的"天界印记"（教会十字金光，与 PRTS 风格不符）。
// 形状全部自绘（LineRenderer），材质用 SwordStreakFx.StreakMaterialPlain（实机验证能上屏）。
// 生命周期：订阅 IUnitBuffHandler，按增益蓝图名 / GUID 过滤（RhodesDoctorBuffTacticalMark，见 Is()）；
//   加上 → 生成并跟随目标；移除（被下一次攻击消耗 / 到期 / 驱散）→ 0.3 s 淡出。
//   另每秒自检一次"增益是否还在"，防止漏事件导致残留。
using System;
using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using Kingmaker.UnitLogic.Buffs;
using UnityEngine;

namespace RhodesDoctor
{
  internal static class TacticalProjectionFx
  {
    /// <summary>CreateSpellBuff 的命名规则：$"RhodesDoctorBuff{s.Key}"。</summary>
    internal const string BuffName = "RhodesDoctorBuffTacticalMark";

    /// <summary>
    /// 增益蓝图 GUID（归一化：去横线、小写）——**名字那一路的兜底**。
    /// 值 = <c>Guids.Buff(0)</c>：`CreateSpellBuff(s, i)` 用的是 **Specs 下标** i，
    /// 而 `Specs[0].Key == "TacticalMark"`（见 SpellsSetup.Specs）。⚠ 若日后重排 Specs，这里要跟着改。
    ///
    /// 为什么要两条：本项目其它常驻特效（TintedAuraFx / MedicalDroneFx）**一律按 GUID 过滤**
    /// （GUID 是存档里真实生效的键）；名字那一路要依赖 BlueprintCore 把 `New(name, guid)` 的 name
    /// 落到 `Blueprint.name` 上。两条取"或"：任一对上就认，哪条口径变了都不会让整条特效静默哑掉。
    /// </summary>
    internal static readonly string BuffGuid = Guids.Buff(0).Replace("-", "").ToLowerInvariant();

    internal static readonly Color Cyan  = new Color(0.25f, 0.85f, 1.00f, 1f); // PRTS 主色
    internal static readonly Color White = new Color(0.85f, 1.00f, 1.00f, 1f); // 高光 / 扫描线
    internal static readonly Color Amber = new Color(1.00f, 0.72f, 0.20f, 1f); // 头顶"目标"菱形

    private static readonly Dictionary<UnitEntityData, TacticalReticleAnim> Live = new();

    internal static bool Is(Buff buff)
    {
      var bp = buff?.Blueprint;
      if (bp == null) return false;
      if (bp.name == BuffName) return true;
      try
      {
        var guid = bp.AssetGuid.ToString();
        return !string.IsNullOrEmpty(guid)
          && guid.Replace("-", "").ToLowerInvariant() == BuffGuid;
      }
      catch { return false; }
    }

    internal static bool UnitHasMark(UnitEntityData unit, Buff except = null)
    {
      try
      {
        if (unit?.Buffs == null) return false;
        foreach (var b in unit.Buffs.RawFacts)
          if (b != except && Is(b)) return true;
      }
      catch { }
      return false;
    }

    internal static void OnBuffAdded(Buff buff)
    {
      try
      {
        if (!Is(buff)) return;
        var unit = buff.Owner?.Unit;
        if (unit?.View == null) return;
        if (Live.TryGetValue(unit, out var old) && old != null) { old.Replay(); return; }

        var go = new GameObject("RD_TacticalProjection") { layer = unit.View.gameObject.layer };
        var anim = go.AddComponent<TacticalReticleAnim>();
        if (!anim.Init(unit)) { UnityEngine.Object.Destroy(go); return; }
        Live[unit] = anim;
        Dbg.Log("[RhodesDoctor] 战术标记·战术投影已生成。");
      }
      catch (Exception e) { Main.Mod.Logger.Error("[RhodesDoctor] 战术投影生成失败（效果照常）：\n" + e); }
    }

    internal static void OnBuffRemoved(Buff buff)
    {
      try
      {
        if (!Is(buff)) return;
        var unit = buff.Owner?.Unit;
        if (unit == null) return;
        if (UnitHasMark(unit, buff)) return; // 还有另一份战术标记（两次施放）就不收
        if (Live.TryGetValue(unit, out var a)) { Live.Remove(unit); if (a != null) a.FadeOut(); }
      }
      catch (Exception e) { Main.Mod.Logger.Error("[RhodesDoctor] 战术投影清理失败：\n" + e); }
    }

    internal static void Forget(UnitEntityData unit, TacticalReticleAnim anim)
    {
      if (unit != null && Live.TryGetValue(unit, out var a) && a == anim) Live.Remove(unit);
    }
  }

  /// <summary>战术投影本体：跟随单位，自绘地面准星 + 扫描环 + 头顶菱形。</summary>
  public class TacticalReticleAnim : MonoBehaviour
  {
    private const int RingSeg = 64;
    private const int Dashes = 6;
    private const int DashPts = 8;
    private const float IntroDur = 0.35f;
    private const float FadeDur = 0.30f;
    private const float ScanPeriod = 1.4f;

    private UnitEntityData _unit;
    private float _r, _h, _t, _fadeT = -1f, _checkT;
    private LineRenderer _intro, _ring, _scan, _diamond;
    private readonly LineRenderer[] _dash = new LineRenderer[Dashes];
    private readonly LineRenderer[] _corner = new LineRenderer[4];

    internal bool Init(UnitEntityData unit)
    {
      _unit = unit;
      var mCyan = SwordStreakFx.StreakMaterialPlain(TacticalProjectionFx.Cyan);
      var mWhite = SwordStreakFx.StreakMaterialPlain(TacticalProjectionFx.White);
      var mAmber = SwordStreakFx.StreakMaterialPlain(TacticalProjectionFx.Amber);
      if (mCyan == null || mWhite == null || mAmber == null)
      {
        // 素材这条路在本 mod 翻过车（Shader.Find 空白、粒子素材缩不小），失败必须留痕，别静默。
        Main.Mod.Logger.Error("[RhodesDoctor] 战术投影放弃：StreakMaterialPlain 无可用材质"
          + "（剑雨/重构用的是同一份，若它失败则该材质解析整体不可用）。");
        return false;
      }

      float corp = unit.View != null ? unit.View.Corpulence : 0.5f;
      _r = Mathf.Clamp(corp * 1.6f, 0.7f, 3.0f);
      _h = ModelHeight(unit);

      _intro = Flat("RD_TP_Intro", mWhite, RingSeg, true, 0.06f);
      _ring  = Flat("RD_TP_Ring",  mCyan,  RingSeg, true, 0.035f);
      _scan  = Flat("RD_TP_Scan",  mWhite, RingSeg, true, 0.025f);
      for (int i = 0; i < Dashes; i++) _dash[i] = Flat("RD_TP_Dash", mCyan, DashPts, false, 0.05f);
      for (int i = 0; i < 4; i++) _corner[i] = Flat("RD_TP_Corner", mCyan, 3, false, 0.06f);
      _diamond = NewLine("RD_TP_Diamond", mAmber, 4, true, 0.045f, LineAlignment.View);
      return true;
    }

    /// <summary>同一目标被再次标记：重播入场。</summary>
    internal void Replay() { _t = 0f; _fadeT = -1f; }

    internal void FadeOut() { if (_fadeT < 0f) _fadeT = 0f; }

    private void OnDestroy() => TacticalProjectionFx.Forget(_unit, this);

    private void Update()
    {
      float dt = Time.deltaTime;
      _t += dt;
      if (_unit == null || _unit.View == null) { Destroy(gameObject); return; }

      // 每秒自检：增益已不在（漏了移除事件）→ 淡出
      _checkT += dt;
      if (_checkT >= 1f)
      {
        _checkT = 0f;
        if (_fadeT < 0f && !TacticalProjectionFx.UnitHasMark(_unit)) FadeOut();
      }

      float master = 1f;
      if (_fadeT >= 0f)
      {
        _fadeT += dt;
        master = 1f - Mathf.Clamp01(_fadeT / FadeDur);
        if (_fadeT >= FadeDur) { Destroy(gameObject); return; }
      }

      Vector3 c = _unit.Position;
      float gy = 0.05f;

      // ── 入场：外圈从 2.2 倍半径收拢到准星圈 ──
      float ki = Mathf.Clamp01(_t / IntroDur);
      if (ki < 1f)
      {
        _intro.enabled = true;
        float e = 1f - (1f - ki) * (1f - ki);
        Circle(_intro, c, Mathf.Lerp(_r * 2.2f, _r, e), gy);
        SetA(_intro, TacticalProjectionFx.White, 0.9f * Mathf.Sin(ki * Mathf.PI * 0.5f + 0.3f) * master);
      }
      else _intro.enabled = false;

      float appear = Mathf.Clamp01((_t - IntroDur * 0.7f) / 0.2f) * master;

      // ── 实线准星圈 ──
      Circle(_ring, c, _r, gy);
      SetA(_ring, TacticalProjectionFx.Cyan, 0.75f * appear);

      // ── 旋转虚线环 ──
      float rot = _t * 0.7f;
      float span = Mathf.PI * 2f / Dashes * 0.55f;
      float rd = _r * 1.22f;
      for (int i = 0; i < Dashes; i++)
      {
        float a0 = rot + i * Mathf.PI * 2f / Dashes;
        for (int p = 0; p < DashPts; p++)
        {
          float a = a0 + span * p / (DashPts - 1f);
          _dash[i].SetPosition(p, c + new Vector3(Mathf.Cos(a) * rd, gy, Mathf.Sin(a) * rd));
        }
        SetA(_dash[i], TacticalProjectionFx.Cyan, 0.6f * appear);
      }

      // ── 四角定位框：入场滑入，之后轻微呼吸 ──
      float slide = Mathf.Clamp01((_t - IntroDur * 0.5f) / 0.25f);
      float s = _r * 1.45f * Mathf.Lerp(1.6f, 1f, 1f - (1f - slide) * (1f - slide))
                * (1f + 0.04f * Mathf.Sin(_t * 4f));
      float len = s * 0.32f;
      for (int i = 0; i < 4; i++)
      {
        float sx = (i == 0 || i == 3) ? 1f : -1f;
        float sz = (i < 2) ? 1f : -1f;
        var corner = c + new Vector3(sx * s, gy, sz * s);
        _corner[i].SetPosition(0, corner - new Vector3(sx * len, 0f, 0f));
        _corner[i].SetPosition(1, corner);
        _corner[i].SetPosition(2, corner - new Vector3(0f, 0f, sz * len));
        SetA(_corner[i], TacticalProjectionFx.Cyan, 0.9f * slide * master);
      }

      // ── 扫描环：脚底 → 头顶，循环 ──
      float f = Mathf.Repeat(_t, ScanPeriod) / ScanPeriod;
      Circle(_scan, c, _r * 0.8f, gy + f * _h * 0.95f);
      SetA(_scan, TacticalProjectionFx.White, 0.5f * (1f - f) * appear);

      // ── 头顶菱形（正对镜头，上下浮动）──
      var cam = Camera.main;
      Vector3 right = cam != null ? cam.transform.right : Vector3.right;
      right.y = 0f;
      right = right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right;
      var top = c + Vector3.up * (_h + 0.45f + 0.08f * Mathf.Sin(_t * 3f));
      const float d = 0.2f;
      _diamond.SetPosition(0, top + Vector3.up * d);
      _diamond.SetPosition(1, top + right * d * 0.75f);
      _diamond.SetPosition(2, top - Vector3.up * d * 1.3f);
      _diamond.SetPosition(3, top - right * d * 0.75f);
      SetA(_diamond, TacticalProjectionFx.Amber, 0.95f * appear);
    }

    // ───────────── 基础件 ─────────────
    private LineRenderer Flat(string name, Material mat, int n, bool loop, float width)
    {
      var lr = NewLine(name, mat, n, loop, width, LineAlignment.TransformZ);
      lr.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward); // Z 朝上 = 平躺
      return lr;
    }

    private LineRenderer NewLine(string name, Material mat, int n, bool loop, float width, LineAlignment align)
    {
      var go = new GameObject(name) { layer = gameObject.layer };
      go.transform.SetParent(transform, false);
      var lr = go.AddComponent<LineRenderer>();
      lr.sharedMaterial = mat;
      lr.useWorldSpace = true;
      lr.loop = loop;
      lr.positionCount = n;
      lr.alignment = align;
      lr.widthMultiplier = width;
      lr.numCornerVertices = 1;
      lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
      lr.receiveShadows = false;
      SetA(lr, Color.white, 0f);
      return lr;
    }

    private static void Circle(LineRenderer lr, Vector3 c, float r, float y)
    {
      for (int i = 0; i < RingSeg; i++)
      {
        float a = i * Mathf.PI * 2f / RingSeg;
        lr.SetPosition(i, c + new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
      }
    }

    private static void SetA(LineRenderer lr, Color col, float a)
    {
      var c = new Color(col.r, col.g, col.b, Mathf.Clamp01(a));
      lr.startColor = c; lr.endColor = c;
    }

    /// <summary>只量蒙皮网格（粒子包围盒会被吹到几百米，见 dev-notes §0）。</summary>
    private static float ModelHeight(UnitEntityData u)
    {
      try
      {
        var v = u.View;
        float baseY = v.transform.position.y, top = baseY;
        foreach (var r in v.GetComponentsInChildren<SkinnedMeshRenderer>())
          if (r != null && r.enabled) top = Mathf.Max(top, r.bounds.max.y);
        float h = top - baseY;
        if (h > 0.5f && h < 12f) return h;
      }
      catch { }
      return 2.0f;
    }
  }

  /// <summary>增益加上/移除的事件订阅（Main.Load 里订阅一次）。</summary>
  internal sealed class TacticalProjectionFxWatcher : IUnitBuffHandler
  {
    public void HandleBuffDidAdded(Buff buff) => TacticalProjectionFx.OnBuffAdded(buff);
    public void HandleBuffDidRemoved(Buff buff) => TacticalProjectionFx.OnBuffRemoved(buff);
  }
}
