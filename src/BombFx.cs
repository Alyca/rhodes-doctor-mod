// 延时爆破视觉：地面炸弹标记（符文 + 半径红圈）+ 引爆爆发。
// 做法照 docs/fx-recolor.md：借原版 PrefabLink、只对运行时 Instantiate 出来的克隆体动手；
// 红圈是本 mod 现场生成的网格/ LineRenderer，完全不碰任何原版特效与材质。
// 之所以不用“把借来的 FX 写进自制区域蓝图的 area.Fx”：实测借来的区域 FX 不上屏。
using System;
using Kingmaker.ResourceLinks;
using UnityEngine;

namespace RhodesDoctor
{
  internal static class BombFx
  {
    /// <summary>地面炸弹本体 FX（常驻循环；SpellsSetup 借原版区域 Fx 写入）。</summary>
    internal static PrefabLink MarkerSourceLink;

    /// <summary>引爆爆发 FX（一次性；SpellsSetup 借原版区域爆发 FX 写入）。</summary>
    internal static PrefabLink BoomLink;

    /// <summary>爆发半径（尺）。与 SpellsSetup 里 DelayedBlast 的 AoERadius 保持一致。</summary>
    internal const float RadiusFeet = 10f;

    private const float FeetToMeters = 0.3048f;

    private static GameObject _markerTemplate;
    private static GameObject _boomTemplate;
    private static Shader _circleShader;
    private static bool _shaderLogged;
    private static bool _boomLogged;

    // ── P2-B：圆盘网格与两个圈材质**静态缓存复用** ──────────────────────────────────
    // 旧实现每次 PlaceMarker 都新建 1 个 Mesh（73 顶点）+ 2 个 Material，无缓存无销毁；
    // 反复布设（延时爆破）会累积未回收对象。现在只建一次、整天复用：
    //   · 网格按**单位半径**生成，实际半径靠 transform.localScale 缩放（半径是常量，缩放无损）；
    //   · 两个材质（填充盘 alpha .22 / 描边 alpha .9）颜色恒定，直接缓存。
    // 这些资源归静态字段所有（不挂在 GameObject 上），Destroy(root) 只销毁 GO，不影响共享资源。
    private static Mesh _discMesh;
    private static Material _discMat;
    private static Material _ringMat;
    private static Vector3[] _ringPositions;

    private const int DiscSegments = 72;

    /// <summary>在 point 放一个炸弹地面标记（符文克隆 + 半径为 RadiusFeet 的红圈）；slot 已有实例时跳过。</summary>
    internal static void PlaceMarker(ref GameObject slot, Vector3 point)
    {
      try
      {
        if (slot != null) return;
        var root = new GameObject("RhodesDoctorDelayedBlastMarker");
        root.transform.position = point;

        // ① 炸弹本体：借来的符文，克隆一份（原特效不受影响）。
        if (MarkerSourceLink != null && MarkerSourceLink.Exists())
        {
          if (_markerTemplate == null) _markerTemplate = MarkerSourceLink.Load(false, true);
          if (_markerTemplate != null)
          {
            var rune = UnityEngine.Object.Instantiate(_markerTemplate, point, Quaternion.identity);
            rune.transform.SetParent(root.transform, worldPositionStays: true);
          }
          else
          {
            Dbg.Log("[RhodesDoctor] 炸弹符文模板加载失败（只显示红圈）。");
          }
        }

        // ② 半径红圈：贴地，让玩家随时看到实际爆发范围。
        AddRadiusCircle(root.transform, point, RadiusFeet * FeetToMeters);
        slot = root;
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 炸弹标记生成失败：\n" + e);
      }
    }

    /// <summary>销毁炸弹地面标记（含符文与红圈）。</summary>
    internal static void DestroyMarker(ref GameObject slot)
    {
      try
      {
        if (slot != null) UnityEngine.Object.Destroy(slot);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 炸弹标记清理失败：\n" + e);
      }
      slot = null;
    }

    /// <summary>引爆爆发（一次性实例，5 秒后自删）。</summary>
    internal static void BoomAt(Vector3 point)
    {
      try
      {
        if (BoomLink == null || !BoomLink.Exists()) return;
        if (_boomTemplate == null)
        {
          _boomTemplate = BoomLink.Load(false, true);
          if (_boomTemplate == null) return;
        }
        var go = UnityEngine.Object.Instantiate(_boomTemplate, point, Quaternion.identity);
        UnityEngine.Object.Destroy(go, 5f);
      }
      catch (Exception e)
      {
        if (!_boomLogged)
        {
          _boomLogged = true;
          Main.Mod.Logger.Error("[RhodesDoctor] 爆炸闪光失败：\n" + e);
        }
      }
    }

    // ── 红圈 ──

    private static void AddRadiusCircle(Transform parent, Vector3 point, float radius)
    {
      var discMat = CircleMaterial(0.22f, ref _discMat);
      var ringMat = CircleMaterial(0.9f, ref _ringMat);
      if (discMat == null || ringMat == null) return;

      // 半透明红色填充盘（三角扇，贴地）。网格与材质只建一次（单位半径），半径靠缩放。
      var disc = new GameObject("BlastRadiusDisc");
      disc.transform.SetParent(parent, worldPositionStays: true);
      disc.transform.position = point + new Vector3(0f, 0.04f, 0f);
      disc.transform.localScale = new Vector3(radius, 1f, radius);
      disc.AddComponent<MeshFilter>().sharedMesh = DiscMesh();
      var mr = disc.AddComponent<MeshRenderer>();
      mr.sharedMaterial = discMat;
      mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
      mr.receiveShadows = false;

      // 亮红描边（LineRenderer 圆环）。半径是常量 → 顶点位置数组也可缓存复用。
      var ring = new GameObject("BlastRadiusRing");
      ring.transform.SetParent(parent, worldPositionStays: true);
      ring.transform.position = point;
      var lr = ring.AddComponent<LineRenderer>();
      lr.useWorldSpace = true;
      lr.loop = true;
      lr.positionCount = DiscSegments;
      lr.startWidth = lr.endWidth = 0.10f;
      lr.numCornerVertices = 2;
      lr.numCapVertices = 2;
      lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
      lr.receiveShadows = false;
      lr.sharedMaterial = ringMat;
      var red = new Color(1f, 0.16f, 0.16f, 1f);
      lr.startColor = red;
      lr.endColor = red;
      float y = point.y + 0.06f;
      var pos = RingPositions(radius);
      for (int i = 0; i < DiscSegments; i++)
        lr.SetPosition(i, new Vector3(point.x + pos[i].x, y, point.z + pos[i].z));
    }

    /// <summary>单位半径的贴地盘网格（三角扇）；只生成一次。</summary>
    private static Mesh DiscMesh()
    {
      if (_discMesh != null) return _discMesh;
      const int seg = DiscSegments;
      var mesh = new Mesh { name = "RhodesDoctorBlastDisc" };
      var verts = new Vector3[seg + 1];
      var tris = new int[seg * 3];
      verts[0] = Vector3.zero;
      for (int i = 0; i < seg; i++)
      {
        float a = (float)i / seg * Mathf.PI * 2f;
        verts[i + 1] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)); // 单位半径
      }
      for (int i = 0; i < seg; i++)
      {
        tris[i * 3] = 0;
        tris[i * 3 + 1] = i + 1;
        tris[i * 3 + 2] = (i + 1) % seg + 1;
      }
      mesh.vertices = verts;
      mesh.triangles = tris;
      mesh.RecalculateBounds();
      _discMesh = mesh;
      return mesh;
    }

    /// <summary>半径处的圆环顶点（x,z 分量）；半径是常量（RadiusFeet），缓存一次即可。</summary>
    private static Vector3[] RingPositions(float radius)
    {
      if (_ringPositions != null) return _ringPositions;
      const int seg = DiscSegments;
      var pos = new Vector3[seg];
      for (int i = 0; i < seg; i++)
      {
        float a = (float)i / seg * Mathf.PI * 2f;
        pos[i] = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
      }
      _ringPositions = pos;
      return pos;
    }

    /// <summary>红色半透明圆材质；只在本 mod 生成的红圈上用。借不到 shader 返回 null。按槽位缓存复用。</summary>
    private static Material CircleMaterial(float alpha, ref Material cache)
    {
      if (cache != null) return cache;
      if (_circleShader == null)
      {
        foreach (var name in new[]
        {
          "Sprites/Default", "Unlit/Color",
          "Legacy Shaders/Particles/Alpha Blended", "Particles/Standard Unlit",
        })
        {
          var sh = Shader.Find(name);
          if (sh != null)
          {
            _circleShader = sh;
            if (!_shaderLogged)
            {
              _shaderLogged = true;
              Dbg.Log("[RhodesDoctor] 炸弹红圈使用 shader：" + name);
            }
            break;
          }
        }
        if (_circleShader == null)
        {
          Dbg.Log("[RhodesDoctor] 炸弹红圈：找不到可用 shader（红圈不显示）。");
          return null;
        }
      }
      cache = new Material(_circleShader) { color = new Color(1f, 0.16f, 0.16f, alpha) };
      return cache;
    }
  }
}
