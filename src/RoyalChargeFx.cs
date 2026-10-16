// 王者冲锋的"冲锋轨迹"（作者 10-16：原来冲锋光秃秃的，没有骑兵冲锋的气势）。
//
// 10-16 第二版（作者反馈：特效是绿的、轨迹不好看）：
//   · 发绿的原因：光带材质克隆自原版素材并**保留了原贴图**，金色 × 偏青贴图 = 绿；
//     沿途冲击用的是 SpawnPlaced（**不重染**），土系元素爆击原色也偏绿。
//     → 光带改用 SwordStreakFx.StreakMaterialPlain（贴图换成自生成的白色软边），冲击改走 FxTint.Spawn 重染。
//   · 轨迹：去掉齐胸高、面向镜头的那道横光（像空中飘一条带子，不像冲锋），
//     只留**平铺在地面**的一道犁痕（TransformZ 对齐、物件朝上），收窄；沿途冲击更密更小，终点一次大的撞停。
//
// 组成（都是纯视觉，不影响结算）：
//   ① 地面犁痕：贴地金土色光带，0.25 s 从起点扫到落点，约 1.2 s 从尾部淡掉；
//   ② 沿途土石冲击：每约 2 m 一次（重染土金色、缩小）；
//   ③ 终点撞停：一次放大的冲击；
//   ④ 施法者身上挂 1.6 s 的加速术拖尾，重染成金色。
// 任一环节失败都静默跳过（只打一行日志），冲锋本身照常结算。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RhodesDoctor
{
  internal static class RoyalChargeFx
  {
    /// <summary>地面犁痕颜色：王者金偏土。</summary>
    internal static readonly Color StreakColor = new Color(1.00f, 0.72f, 0.30f, 1f);
    /// <summary>沿途冲击的重染色：土金。</summary>
    internal static readonly Color BurstColor = new Color(0.95f, 0.70f, 0.35f, 1f);
    /// <summary>施法者身上加速拖尾的底色（金色是暖色，加速术素材底部不会变回原色）。</summary>
    internal static readonly Color BodyTrailColor = new Color(1.30f, 0.85f, 0.30f, 1f);

    private const float TravelSeconds = 0.25f;
    private const float FadeSeconds = 1.20f;
    private const float BodyTrailSeconds = 1.6f;
    /// <summary>沿途冲击间距（米）。</summary>
    private const float BurstSpacingMeters = 2.0f;
    private const int MaxBursts = 10;
    /// <summary>沿途冲击的粒子缩放（小一点，不抢终点）。</summary>
    internal const float PathBurstScale = 0.6f;
    /// <summary>终点撞停的粒子缩放。</summary>
    internal const float EndBurstScale = 1.3f;

    private static bool _missLogged;

    internal static void Play(Vector3 start, Vector3 dest, Transform casterView)
    {
      try
      {
        Vector3 flat = dest - start; flat.y = 0f;
        float len = flat.magnitude;
        if (len < 0.3f) return;
        Vector3 dir = flat / len;

        // 沿途冲击点（不含终点，终点单独放大的）
        int n = Mathf.Clamp(Mathf.FloorToInt(len / BurstSpacingMeters), 0, MaxBursts);
        var bursts = new List<Vector3>(n + 1);
        for (int i = 1; i <= n; i++)
        {
          var p = Vector3.Lerp(start, dest, i / (float)(n + 1));
          bursts.Add(p);
        }
        bursts.Add(dest); // 最后一个 = 终点撞停

        // ① 地面犁痕（贴地 5 cm，平铺）
        SpawnGroundStreak(start + Vector3.up * 0.05f, dest + Vector3.up * 0.05f, dir, 0.9f, StreakColor, 0.9f, bursts);

        // ④ 施法者身上的金色加速拖尾
        if (casterView != null)
        {
          var go = FxTint.SpawnAttached(FxLibrary.HasteTrail, casterView, BodyTrailColor, 0.25f);
          if (go != null) UnityEngine.Object.Destroy(go, BodyTrailSeconds);
        }
        Dbg.Log($"[RhodesDoctor] 王者冲锋·轨迹已生成：长度 {len:F1} m，沿途冲击 {n} 次 + 终点 1 次。");
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 王者冲锋·轨迹生成失败（冲锋照常）：\n" + e);
      }
    }

    /// <summary>在 p 处放一次重染过的土石冲击；last=true 为终点撞停（放大）。</summary>
    internal static void Burst(Vector3 p, bool last)
    {
      try
      {
        var go = FxTint.Spawn(FxLibrary.Physical, p, BurstColor, 0.25f, last ? 4f : 3f);
        if (go == null) return;
        FxTint.ScaleParticleSizes(go, last ? EndBurstScale : PathBurstScale);
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
          try { if (!ps.isPlaying) ps.Play(true); } catch { }
      }
      catch { }
    }

    private static void SpawnGroundStreak(Vector3 from, Vector3 to, Vector3 dir, float width, Color color,
      float peakAlpha, List<Vector3> bursts)
    {
      var mat = SwordStreakFx.StreakMaterialPlain(color);
      if (mat == null)
      {
        if (!_missLogged)
        {
          _missLogged = true;
          Dbg.Log("[RhodesDoctor] 王者冲锋·光带材质不可用（跳过光带，沿途冲击照常）。");
        }
        for (int i = 0; i < bursts.Count; i++) Burst(bursts[i], i == bursts.Count - 1);
        return;
      }

      var go = new GameObject("RhodesDoctorRoyalChargeStreak");
      go.transform.position = from;
      // TransformZ：带子的法线 = 物件的 Z 轴 → Z 朝上，带子就平躺在地上
      go.transform.rotation = Quaternion.LookRotation(Vector3.up, dir);
      var tr = go.AddComponent<TrailRenderer>();
      tr.sharedMaterial = mat;
      tr.time = FadeSeconds;
      tr.startWidth = width;
      tr.endWidth = width * 0.5f;
      tr.minVertexDistance = 0.08f;
      tr.numCapVertices = 4;
      tr.numCornerVertices = 2;
      tr.alignment = LineAlignment.TransformZ;
      tr.autodestruct = false;
      tr.emitting = true;
      tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
      tr.receiveShadows = false;
      var g = new Gradient();
      g.SetKeys(
        new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
        new[]
        {
          new GradientAlphaKey(peakAlpha, 0f),
          new GradientAlphaKey(peakAlpha * 0.7f, 0.5f),
          new GradientAlphaKey(0f, 1f),
        });
      tr.colorGradient = g;

      var mover = go.AddComponent<ChargeStreakMover>();
      mover.From = from;
      mover.To = to;
      mover.Seconds = TravelSeconds;
      mover.Bursts = bursts;
      UnityEngine.Object.Destroy(go, TravelSeconds + FadeSeconds + 0.3f);
    }
  }

  /// <summary>
  /// 把光带头从 From 推到 To（缓出：起步最猛），途经冲击点时放一次土石冲击；最后一个点是终点撞停。
  /// </summary>
  public class ChargeStreakMover : MonoBehaviour
  {
    public Vector3 From;
    public Vector3 To;
    public float Seconds = 0.25f;
    public List<Vector3> Bursts;

    private float _elapsed;
    private int _next;

    private void Update()
    {
      _elapsed += Time.deltaTime;
      float k = Seconds <= 0.01f ? 1f : Mathf.Clamp01(_elapsed / Seconds);
      float e = 1f - (1f - k) * (1f - k); // 缓出
      transform.position = Vector3.Lerp(From, To, e);
      if (Bursts != null)
      {
        float total = (To - From).magnitude;
        while (_next < Bursts.Count)
        {
          float at = total < 0.01f ? 1f : (Bursts[_next] - From).magnitude / total;
          if (e + 0.001f < Mathf.Clamp01(at) && k < 1f) break;
          RoyalChargeFx.Burst(Bursts[_next], _next == Bursts.Count - 1);
          _next++;
        }
      }
      if (k >= 1f && (Bursts == null || _next >= Bursts.Count)) UnityEngine.Object.Destroy(this);
    }
  }
}
