// 重构（9 环）/ 文明的存续（10 环）的**专属特效**（纯视觉，不影响结算）。
//
// 主题与配色：
//   · 重构 = 凯尔希式的冷静重建：冷白 + 青，像细胞重组 / 手术台扫描。
//   · 文明的存续 = 特蕾西娅：绯红 + 暗紫，加一线白金。速度更慢、范围感更大。
//
// 材质一律用 `SwordStreakFx.StreakMaterialPlain`（剑雨 / 王者冲锋**实机验证能上屏**的那份材质），
// 形状全部自己画（LineRenderer / TrailRenderer）。**不用 Shader.Find，也不借巨型粒子素材**——
// 这两条路在本 mod 的历史里都翻过车（见 SwordStreakFx.cs 文件头、dev-notes §0）。
//
// 节奏（详见各处注释）：
//   重构：施法者脚下两道同心扫描环快速扩到半径 → 波扫到的友军各自触发
//         （收拢环 → 三道青白螺旋上升 → 重染成青白的复活之光）。
//   文明：一道绯红环 + 一道暗紫环缓缓扩开；施法者身边 8 道丝线升至头顶汇成白金"王冠"；
//         王冠向每个友军垂下一根带弧度的红线（"把所有人连在一起"）；
//         每个友军身上落下白金火种 + 红紫螺旋上升 + 一发绯红色的圣歌爆发。
using System;
using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities;
using UnityEngine;

namespace RhodesDoctor
{
  internal static class MassRestoreFx
  {
    // 重构：冷白 / 青
    internal static readonly Color ReconCore = new Color(0.78f, 1.00f, 0.96f, 1f);
    internal static readonly Color ReconEdge = new Color(0.20f, 0.85f, 0.80f, 1f);
    // 文明的存续：绯红 / 暗紫 / 白金
    internal static readonly Color CivCrimson = new Color(0.90f, 0.10f, 0.22f, 1f);
    internal static readonly Color CivViolet  = new Color(0.55f, 0.15f, 0.70f, 1f);
    internal static readonly Color CivGold    = new Color(1.00f, 0.85f, 0.62f, 1f);

    private const float ReconWaveSpeed = 14f; // m/s
    private const float CivWaveSpeed   = 8f;

    // ───────────── 重构 ─────────────
    internal static void PlayReconstruction(Vector3 center, float radius,
      IList<UnitEntityData> affected, int layer)
    {
      try
      {
        float waveDur = radius / ReconWaveSpeed;
        Ring(center, 0.3f, radius, waveDur, 0f, 0.35f, ReconCore, 0.95f, 0.15f, layer);
        Ring(center, 0.3f, radius * 1.04f, waveDur * 1.25f, 0.10f, 0.14f, ReconEdge, 0.65f, 0.1f, layer);

        foreach (var u in affected)
        {
          if (u == null) continue;
          var p = u.Position;
          float d = Flat(p - center);
          float t0 = d / ReconWaveSpeed;   // 扫描波扫到这个单位的时刻：波前半径 = 距离
          // 收拢环
          Ring(p, 1.8f, 0.25f, 0.45f, t0, 0.12f, ReconCore, 0.9f, 0.2f, layer);
          // 三道螺旋上升
          for (int i = 0; i < 3; i++)
            Riser(p, 0.65f, 0.25f, 2.3f, 1.0f, i * 2.094f, 0.55f, t0 + 0.25f,
              ReconCore, 0.07f, 0.35f, layer);
          // 重染青白的复活之光
          Delayed(t0 + 0.55f, layer, () => Burst(FxLibrary.ReconstructionBurst, p, ReconCore, 0.30f, 3f));
        }
        Dbg.Log($"[RhodesDoctor] 重构·专属特效：扫描波 + {affected.Count} 个单位。");
      }
      catch (Exception e) { Main.Mod.Logger.Error("[RhodesDoctor] 重构特效失败（效果照常）：\n" + e); }
    }

    // ───────────── 文明的存续 ─────────────
    internal static void PlayCivilization(Vector3 center, float radius, UnitEntityData caster,
      IList<UnitEntityData> affected, int layer)
    {
      try
      {
        float waveDur = radius / CivWaveSpeed;
        Ring(center, 0.5f, radius, waveDur, 0f, 0.45f, CivCrimson, 0.95f, 0.35f, layer);
        Ring(center, 0.5f, radius * 1.06f, waveDur * 1.3f, 0.15f, 0.25f, CivViolet, 0.8f, 0.3f, layer);

        // 施法者：8 道绯红丝线螺旋上升 → 头顶白金王冠
        const float crownH = 3.6f;
        for (int i = 0; i < 8; i++)
          Riser(center, 1.4f, 0.7f, crownH, 0.75f, i * 0.785f, 0.9f, 0f,
            i % 2 == 0 ? CivCrimson : CivViolet, 0.09f, 0.6f, layer);
        var crown = center + Vector3.up * crownH;
        Ring(crown, 0.4f, 0.9f, 2.6f, 0.85f, 0.10f, CivGold, 1f, 0.6f, layer);
        Delayed(0.4f, layer, () => Burst(FxLibrary.CivilizationBurst, center, CivCrimson, 0.15f, 4f));

        foreach (var u in affected)
        {
          if (u == null || u == caster) continue;
          var p = u.Position;
          // 王冠在 0.85~1.75 s 之间成型，连线略晚一点、留出"看见王冠"的时间。
          float t0 = 0.9f + Flat(p - center) / CivWaveSpeed * 0.5f;
          // 王冠 → 友军 的红线
          Link(crown, p + Vector3.up * 1.1f, t0, 0.35f, 1.0f, 0.8f, CivCrimson, 0.06f, 0.9f, layer);
          // 白金火种自天而降（height 传负 = 自上而下）
          Riser(p + Vector3.up * 6f, 0f, 0f, -5.0f, 0f, 0f, 0.35f, t0 + 0.2f, CivGold, 0.10f, 0.3f, layer);
          // 红紫螺旋上升
          for (int i = 0; i < 4; i++)
            Riser(p, 0.8f, 0.3f, 2.6f, 1.25f, i * 1.571f, 0.8f, t0 + 0.5f,
              i % 2 == 0 ? CivCrimson : CivViolet, 0.07f, 0.45f, layer);
          Delayed(t0 + 0.55f, layer, () => Burst(FxLibrary.CivilizationBurst, p, CivCrimson, 0.15f, 3f, 0.55f));
        }
        Dbg.Log($"[RhodesDoctor] 文明的存续·专属特效：王冠 + {affected.Count} 个单位。");
      }
      catch (Exception e) { Main.Mod.Logger.Error("[RhodesDoctor] 文明的存续特效失败（效果照常）：\n" + e); }
    }

    // ───────────── 基础件 ─────────────
    /// <summary>水平面距离（忽略高度差）：扫描波是贴地扩散的，判定也该只看平面。</summary>
    private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

    private static GameObject NewGo(string name, int layer)
    {
      var go = new GameObject(name) { layer = layer };
      return go;
    }

    /// <summary>
    /// 贴地水平圆环：半径 r0→r1 缓出扩张，<paramref name="hold"/> 比例后淡出。
    /// ⚠ 环一律**平躺**（水平面），高度固定贴地 6 cm —— 头顶"王冠"也是水平的，所以没有"竖直环"的需求，
    /// 早先版本里那个 flatY 参数从头到尾没被用过，已删（免得后面有人以为它能改朝向）。
    /// </summary>
    private static void Ring(Vector3 c, float r0, float r1, float dur, float delay, float width,
      Color col, float peak, float hold, int layer)
    {
      var mat = SwordStreakFx.StreakMaterialPlain(col);
      if (mat == null) return;
      var go = NewGo("RD_Ring", layer);
      var a = go.AddComponent<RdRingAnim>();
      a.Init(mat, c, r0, r1, dur, delay, width, col, peak, hold);
    }

    /// <summary>
    /// 沿螺旋线移动的光尾（TrailRenderer）。<paramref name="height"/> 为负 = 自上而下落。
    /// </summary>
    private static void Riser(Vector3 basePos, float r0, float r1, float height, float turns, float phase,
      float dur, float delay, Color col, float width, float trailTime, int layer)
    {
      var mat = SwordStreakFx.StreakMaterialPlain(col);
      if (mat == null) return;
      var go = NewGo("RD_Riser", layer);
      var a = go.AddComponent<RdRiserAnim>();
      a.Init(mat, col, width, trailTime, basePos, r0, r1, height, turns, phase, dur, delay);
    }

    /// <summary>两点间带下垂弧度的丝线：从 A 长到 B → 停留 → 淡出。</summary>
    private static void Link(Vector3 from, Vector3 to, float delay, float grow, float hold, float fade,
      Color col, float width, float peak, int layer)
    {
      var mat = SwordStreakFx.StreakMaterialPlain(col);
      if (mat == null) return;
      var go = NewGo("RD_Link", layer);
      var a = go.AddComponent<RdLinkAnim>();
      a.Init(mat, from, to, delay, grow, hold, fade, col, width, peak);
    }

    /// <summary>延时执行一次（用于和扫描波同步的爆发）。</summary>
    private static void Delayed(float delay, int layer, Action act)
    {
      var go = NewGo("RD_Delayed", layer);
      var d = go.AddComponent<RdDelayed>();
      d.Delay = delay; d.Do = act;
    }

    /// <summary>借来的原版爆发（复活术 / 圣歌），重染成目标色；可选缩放粒子本身。</summary>
    private static void Burst(Kingmaker.ResourceLinks.PrefabLink link, Vector3 p, Color col,
      float lift, float life, float sizeScale = 1f)
    {
      var go = FxTint.Spawn(link, p + Vector3.up * 0.3f, col, lift, life);
      if (go == null) return;
      if (!Mathf.Approximately(sizeScale, 1f)) FxTint.ScaleParticleSizes(go, sizeScale);
      foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        try { if (!ps.isPlaying) ps.Play(true); } catch { }
    }
  }

  /// <summary>贴地（水平）圆环：半径 r0→r1 缓出扩张，hold 比例后淡出。</summary>
  public class RdRingAnim : MonoBehaviour
  {
    private const int Seg = 72;
    private LineRenderer _lr;
    private Vector3 _c; private float _r0, _r1, _dur, _delay, _peak, _hold, _t;
    private Color _col;

    internal void Init(Material mat, Vector3 c, float r0, float r1, float dur, float delay,
      float width, Color col, float peak, float hold)
    {
      _c = c; _r0 = r0; _r1 = r1; _dur = Mathf.Max(0.05f, dur); _delay = delay;
      _col = col; _peak = peak; _hold = Mathf.Clamp(hold, 0f, 0.95f);
      transform.position = c;
      // LineRenderer 用世界坐标自绘圆，朝向本身不影响顶点 —— 这里照旧把 Z 轴朝上，保持"平躺"语义。
      transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
      _lr = gameObject.AddComponent<LineRenderer>();
      _lr.sharedMaterial = mat;
      _lr.useWorldSpace = true;
      _lr.loop = true;
      _lr.positionCount = Seg;
      _lr.alignment = LineAlignment.TransformZ;
      _lr.widthMultiplier = width;
      _lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
      _lr.receiveShadows = false;
      _lr.enabled = false;   // 延时期间不画（否则会先闪一个半径 0 的点）
    }

    private void Update()
    {
      _t += Time.deltaTime;
      if (_t < _delay) return;
      if (!_lr.enabled) _lr.enabled = true;
      float k = Mathf.Clamp01((_t - _delay) / _dur);
      float e = 1f - (1f - k) * (1f - k);          // 缓出：一波冲出去再慢下来
      float r = Mathf.Lerp(_r0, _r1, e);
      for (int i = 0; i < Seg; i++)
      {
        float a = i * Mathf.PI * 2f / Seg;
        _lr.SetPosition(i, _c + new Vector3(Mathf.Cos(a) * r, 0.06f, Mathf.Sin(a) * r));
      }
      float alpha = _peak * (k < _hold ? 1f : 1f - (k - _hold) / (1f - _hold));
      var c = new Color(_col.r, _col.g, _col.b, alpha);
      _lr.startColor = c; _lr.endColor = c;
      if (k >= 1f) Destroy(gameObject);
    }
  }

  /// <summary>沿螺旋线移动的光尾（height 为负 = 自上而下落）。</summary>
  public class RdRiserAnim : MonoBehaviour
  {
    private TrailRenderer _tr;
    private Vector3 _base; private float _r0, _r1, _h, _turns, _phase, _dur, _delay, _t;
    private bool _started, _done;

    internal void Init(Material mat, Color col, float width, float trailTime, Vector3 basePos,
      float r0, float r1, float height, float turns, float phase, float dur, float delay)
    {
      _base = basePos; _r0 = r0; _r1 = r1; _h = height; _turns = turns; _phase = phase;
      _dur = Mathf.Max(0.05f, dur); _delay = delay;
      transform.position = Pos(0f);
      _tr = gameObject.AddComponent<TrailRenderer>();
      _tr.sharedMaterial = mat;
      _tr.time = trailTime;
      _tr.startWidth = width; _tr.endWidth = 0f;
      _tr.minVertexDistance = 0.04f;
      _tr.numCapVertices = 2;
      _tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
      _tr.receiveShadows = false;
      _tr.emitting = false;    // 延时期间不发射（否则起点会先拖一条）
      var g = new Gradient();
      g.SetKeys(
        new[] { new GradientColorKey(col, 0f), new GradientColorKey(col, 1f) },
        new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
      _tr.colorGradient = g;
    }

    private Vector3 Pos(float k)
    {
      float ang = _phase + _turns * Mathf.PI * 2f * k;
      float r = Mathf.Lerp(_r0, _r1, k);
      return _base + new Vector3(Mathf.Cos(ang) * r, _h * k, Mathf.Sin(ang) * r);
    }

    private void Update()
    {
      if (_done) return;
      _t += Time.deltaTime;
      if (_t < _delay) return;
      if (!_started) { _started = true; _tr.Clear(); _tr.emitting = true; }
      float k = Mathf.Clamp01((_t - _delay) / _dur);
      // 终点缓出 —— 顶点位置是"跟着时间走"的，所以每个顶点在世界里**留痕**，
      // 于是暂停整整一帧也不会导致尾迹破成两截。若将来改成每帧手工 AddPosition，这里要一起改。
      transform.position = Pos(1f - (1f - k) * (1f - k));
      if (k >= 1f)
      {
        _done = true;
        _tr.emitting = false;
        Destroy(gameObject, _tr.time + 0.1f);   // 等尾迹自己淡完再删
      }
    }
  }

  /// <summary>两点间带下垂弧度的丝线：从 A 长到 B → 停留 → 淡出。</summary>
  public class RdLinkAnim : MonoBehaviour
  {
    private const int N = 16;
    private LineRenderer _lr;
    private Vector3 _a, _b, _ctrl; private float _delay, _grow, _hold, _fade, _peak, _t;
    private Color _col;

    internal void Init(Material mat, Vector3 a, Vector3 b, float delay, float grow, float hold, float fade,
      Color col, float width, float peak)
    {
      _a = a; _b = b; _delay = delay; _grow = Mathf.Max(0.05f, grow); _hold = hold; _fade = Mathf.Max(0.05f, fade);
      _col = col; _peak = peak;
      _ctrl = (a + b) * 0.5f + Vector3.up * 0.8f;   // 上抬的控制点 = 弧线
      _lr = gameObject.AddComponent<LineRenderer>();
      _lr.sharedMaterial = mat;
      _lr.useWorldSpace = true;
      _lr.positionCount = N;
      _lr.alignment = LineAlignment.View;           // 始终面向相机，细线在任何视角都看得见
      _lr.startWidth = width * 1.4f; _lr.endWidth = width;
      _lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
      _lr.receiveShadows = false;
      _lr.enabled = false;
    }

    /// <summary>二次贝塞尔：A → 控制点 → B。</summary>
    private Vector3 Bez(float s)
    {
      float u = 1f - s;
      return u * u * _a + 2f * u * s * _ctrl + s * s * _b;
    }

    private void Update()
    {
      _t += Time.deltaTime;
      float t = _t - _delay;
      if (t < 0f) return;
      if (!_lr.enabled) _lr.enabled = true;
      float reach = Mathf.Clamp01(t / _grow);
      for (int i = 0; i < N; i++) _lr.SetPosition(i, Bez(reach * i / (N - 1f)));
      float alpha = _peak;
      float ft = t - _grow - _hold;
      if (ft > 0f) alpha *= 1f - Mathf.Clamp01(ft / _fade);
      var c = new Color(_col.r, _col.g, _col.b, alpha);
      _lr.startColor = c; _lr.endColor = c;
      if (ft >= _fade) Destroy(gameObject);
    }
  }

  /// <summary>延时执行一次（用于和扫描波同步的爆发）。</summary>
  public class RdDelayed : MonoBehaviour
  {
    public float Delay;
    public Action Do;
    private float _t;
    private void Update()
    {
      _t += Time.deltaTime;
      if (_t < Delay) return;
      try { Do?.Invoke(); } catch (Exception e) { Main.Mod.Logger.Error("[RhodesDoctor] 延时特效失败：\n" + e); }
      Destroy(gameObject);
    }
  }
}
