// 敌方"标记"类专属特效的公共件（火力侦察 / 终末狙击共用；纯视觉，不影响结算）。
//
// · MarkFxTracker：按增益蓝图名过滤；增益加上 → 生成并跟随目标；移除 → 0.3 s 淡出；
//   同一目标还有另一份同名增益（两次施放）时不收。
// · MarkFxBase：跟随 / 淡出 / 每秒自检（增益已不在就淡出，防漏事件残留）。
// · MarkFxKit：LineRenderer 基础件。材质一律 SwordStreakFx.StreakMaterialPlain（实机验证能上屏），
//   不用 Shader.Find、不借原版粒子。
using System;
using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using Kingmaker.UnitLogic.Buffs;
using UnityEngine;

namespace RhodesDoctor
{
  internal sealed class MarkFxTracker
  {
    private readonly string _buffName;
    /// <summary>增益蓝图 GUID（归一化：去横线、小写）——**名字那一路的兜底**，可为 null。</summary>
    private readonly string _buffGuid;
    private readonly string _label;
    private readonly Func<GameObject, MarkFxBase> _attach;
    private readonly Dictionary<UnitEntityData, MarkFxBase> _live = new();
    private bool _guidOnlyLogged;

    internal MarkFxTracker(string buffName, string label, Func<GameObject, MarkFxBase> attach,
      string buffGuid = null)
    {
      _buffName = buffName; _label = label; _attach = attach;
      _buffGuid = string.IsNullOrEmpty(buffGuid)
        ? null : buffGuid.Replace("-", "").ToLowerInvariant();
    }

    /// <summary>
    /// 是不是本条特效认的那份增益：**蓝图名**（<c>CreateSpellBuff</c> / <c>CreateExtraBuff</c> 的
    /// `RhodesDoctorBuff{Key}` 规则）**或蓝图 GUID**（本项目其它常驻特效 TintedAuraFx / MedicalDroneFx
    /// 一律按 GUID 过滤——GUID 是存档里真实生效的键；名字那一路要依赖 BlueprintCore 把
    /// `New(name, guid)` 的 name 落到 `Blueprint.name` 上）。两条取"或"：哪条口径变了都不会让特效静默哑掉。
    /// </summary>
    internal bool Is(Buff b)
    {
      var bp = b?.Blueprint;
      if (bp == null) return false;
      if (bp.name == _buffName) return true;
      if (_buffGuid == null) return false;
      try
      {
        var guid = bp.AssetGuid.ToString();
        if (string.IsNullOrEmpty(guid)
          || guid.Replace("-", "").ToLowerInvariant() != _buffGuid) return false;
        // 只按 GUID 认上 → 说明"名字"那条口径跟代码里写的不一样。**必须在 release 也看得见**，
        // 否则这条线一旦变了就是纯静默失效（打一次就够，顺带把真实蓝图名报出来）。
        if (!_guidOnlyLogged)
        {
          _guidOnlyLogged = true;
          Main.Mod.Logger.Log($"[RhodesDoctor] {_label}：增益是按 GUID 认到的，"
            + $"其蓝图名实际为 '{bp.name}'（代码里写的是 '{_buffName}'）——两条口径只需对一条。");
        }
        return true;
      }
      catch { return false; }
    }

    internal MarkFxBase Get(UnitEntityData u)
      => u != null && _live.TryGetValue(u, out var a) && a != null ? a : null;

    internal void Added(Buff buff)
    {
      try
      {
        if (!Is(buff)) return;
        var unit = buff.Owner?.Unit;
        if (unit?.View == null) return;
        var old = Get(unit);
        if (old != null) { old.Replay(buff); return; }
        var go = new GameObject("RD_Mark_" + _buffName) { layer = unit.View.gameObject.layer };
        var anim = _attach(go);
        if (anim == null || !anim.Begin(this, unit, buff)) { UnityEngine.Object.Destroy(go); return; }
        _live[unit] = anim;
        Dbg.Log($"[RhodesDoctor] {_label}已生成。");
      }
      catch (Exception e) { Main.Mod.Logger.Error($"[RhodesDoctor] {_label}生成失败（效果照常）：\n" + e); }
    }

    internal void Removed(Buff buff)
    {
      try
      {
        if (!Is(buff)) return;
        var unit = buff.Owner?.Unit;
        if (unit == null || HasBuff(unit, buff)) return;
        var a = Get(unit);
        if (a != null) { _live.Remove(unit); a.FadeOut(); }
      }
      catch (Exception e) { Main.Mod.Logger.Error($"[RhodesDoctor] {_label}清理失败：\n" + e); }
    }

    internal bool HasBuff(UnitEntityData unit, Buff except = null)
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

    internal void Forget(UnitEntityData u, MarkFxBase a)
    {
      if (u != null && _live.TryGetValue(u, out var x) && x == a) _live.Remove(u);
    }
  }

  /// <summary>跟随单位的标记特效基类：子类只管 Build（建线）与 Tick（每帧摆位）。</summary>
  public abstract class MarkFxBase : MonoBehaviour
  {
    protected const float FadeDur = 0.30f;
    private MarkFxTracker _tracker;
    protected UnitEntityData Unit;
    protected float T, Master = 1f, Height = 2f, Radius = 0.8f;
    private float _fadeT = -1f, _checkT;

    internal bool Begin(MarkFxTracker tracker, UnitEntityData unit, Buff buff)
    {
      _tracker = tracker; Unit = unit;
      Height = MarkFxKit.ModelHeight(unit);
      float corp = unit.View != null ? unit.View.Corpulence : 0.5f;
      Radius = Mathf.Clamp(corp * 1.6f, 0.7f, 3.0f);
      return Build(buff);
    }

    protected abstract bool Build(Buff buff);
    protected abstract void Tick(float dt);

    /// <summary>同一目标被再次标记：重播入场。</summary>
    internal virtual void Replay(Buff buff) { T = 0f; _fadeT = -1f; }
    internal void FadeOut() { if (_fadeT < 0f) _fadeT = 0f; }

    private void Update()
    {
      float dt = Time.deltaTime;
      T += dt;
      if (Unit == null || Unit.View == null) { Destroy(gameObject); return; }

      _checkT += dt;
      if (_checkT >= 1f)
      {
        _checkT = 0f;
        if (_fadeT < 0f && !_tracker.HasBuff(Unit)) FadeOut();
      }

      Master = 1f;
      if (_fadeT >= 0f)
      {
        _fadeT += dt;
        Master = 1f - Mathf.Clamp01(_fadeT / FadeDur);
        if (_fadeT >= FadeDur) { Destroy(gameObject); return; }
      }

      try { Tick(dt); }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 标记特效更新失败（已移除）：\n" + e);
        Destroy(gameObject);
      }
    }

    private void OnDestroy() => _tracker?.Forget(Unit, this);
  }

  internal static class MarkFxKit
  {
    internal const int RingSeg = 64;

    internal static Material Mat(Color c) => SwordStreakFx.StreakMaterialPlain(c);

    internal static LineRenderer Line(Transform parent, string name, Material mat, int n, bool loop,
      float width, LineAlignment align = LineAlignment.View)
    {
      var go = new GameObject(name) { layer = parent.gameObject.layer };
      go.transform.SetParent(parent, false);
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

    /// <summary>平躺在地面上的线（Z 朝上）。</summary>
    internal static LineRenderer Flat(Transform parent, string name, Material mat, int n, bool loop, float width)
    {
      var lr = Line(parent, name, mat, n, loop, width, LineAlignment.TransformZ);
      lr.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
      return lr;
    }

    /// <summary>水平圆（贴地）。</summary>
    internal static void Circle(LineRenderer lr, Vector3 c, float r, float y)
    {
      int n = lr.positionCount;
      for (int i = 0; i < n; i++)
      {
        float a = i * Mathf.PI * 2f / n;
        lr.SetPosition(i, c + new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
      }
    }

    /// <summary>正对镜头的圆（right/up 取相机基向量）。</summary>
    internal static void BillCircle(LineRenderer lr, Vector3 c, float r, Vector3 right, Vector3 up, float phase = 0f)
    {
      int n = lr.positionCount;
      for (int i = 0; i < n; i++)
      {
        float a = phase + i * Mathf.PI * 2f / n;
        lr.SetPosition(i, c + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * r);
      }
    }

    internal static void SetA(LineRenderer lr, Color col, float a)
    {
      var c = new Color(col.r, col.g, col.b, Mathf.Clamp01(a));
      lr.startColor = c; lr.endColor = c;
    }

    internal static void CamBasis(out Vector3 right, out Vector3 up)
    {
      var cam = Camera.main;
      right = cam != null ? cam.transform.right : Vector3.right;
      up = cam != null ? cam.transform.up : Vector3.up;
    }

    /// <summary>相机右向量压到水平面（用于"竖直立着、但正对镜头"的框）。</summary>
    internal static Vector3 FlatRight(Vector3 right)
    {
      right.y = 0f;
      return right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right;
    }

    /// <summary>只量蒙皮网格（粒子包围盒会被吹到几百米，见 dev-notes §0）。</summary>
    internal static float ModelHeight(UnitEntityData u)
    {
      try
      {
        var v = u?.View;
        if (v == null) return 2.0f;
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

  /// <summary>标记类特效的增益事件订阅（Main.Load 里订阅一次）。</summary>
  internal sealed class MarkFxWatcher : IUnitBuffHandler
  {
    public void HandleBuffDidAdded(Buff buff)
    {
      FireReconFx.Tracker.Added(buff);
      FinalSnipeFx.Tracker.Added(buff);
      BattlefieldSurveyFx.Tracker.Added(buff);
      SuppressingShotFx.Tracker.Added(buff);
      SinPunishmentFx.Tracker.Added(buff);
      UnstablePlasmaFx.Tracker.Added(buff);
    }

    public void HandleBuffDidRemoved(Buff buff)
    {
      FireReconFx.Tracker.Removed(buff);
      FinalSnipeFx.Tracker.Removed(buff);
      BattlefieldSurveyFx.Tracker.Removed(buff);
      SuppressingShotFx.Tracker.Removed(buff);
      SinPunishmentFx.Tracker.Removed(buff);
      UnstablePlasmaFx.Tracker.Removed(buff);
    }
  }
}

