// 不稳定血浆（2 环·华法琳）专属视觉特效（纯视觉，不影响结算）。
//
// 主视觉：**原版血雾光环**（BloodHazeBuff / FxLibrary.PlasmaAura）经 TintedAuraFx 验证的
// clone-and-retint 路径生成，重染成华法琳血魔的动脉猩红。这是持续的、跟随单位的身体型光环。
//
// 次要增强（非主体，可选）：
//   · 【中央脉动核心】：一枚小型 20 面体 Mesh 悬浮于胸口，以心搏节律收缩膨胀。
//   · 【地面共振符印】：脚底一枚浅色放射状 Mesh 圆盘，随心搏微幅呼吸。
//
// 实现要点：
//   · 主视觉借原版血雾粒子（FxTint.SpawnAttached），保持项目验证过的克隆重染路径。
//   · 次要增强为自绘 Mesh，Build 时一次性生成并缓存，Tick 中仅调整 Transform / Color，零运行时 GC。
//   · 材质通过 MarkFxKit.Mat（→ SwordStreakFx.StreakMaterialPlain）克隆，不使用 Shader.Find。
//   · 继承 MarkFxBase，由 Tracker 自动管理生成、跟随与 0.3 s 淡出。
//   · OnDestroy 正确 override 基类以调用 Tracker.Forget，避免 _live 字典残留。
using System;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Buffs;
using UnityEngine;
using static RhodesDoctor.MarkFxKit;

namespace RhodesDoctor
{
  internal static class UnstablePlasmaFx
  {
    // 华法琳血魔试剂调色盘
    internal static readonly Color ArterialBlood = new Color(0.92f, 0.08f, 0.16f, 1f); // 动脉猩红
    internal static readonly Color AdrenalineRed = new Color(1.00f, 0.25f, 0.30f, 1f); // 脉冲鲜红
    internal static readonly Color ShadowBlood   = new Color(0.50f, 0.05f, 0.10f, 1f); // 深色血雾

    /// <summary>
    /// 增益跟踪器：按蓝图名或 GUID 过滤。
    /// 蓝图名：RhodesDoctorBuffUnstablePlasma
    /// GUID：Guids.Buff(13)
    /// </summary>
    internal static readonly MarkFxTracker Tracker = new MarkFxTracker(
      "RhodesDoctorBuffUnstablePlasma", "不稳定血浆·血魔活性", go => go.AddComponent<UnstablePlasmaAnim>(),
      Guids.Buff(13));
  }

  /// <summary>
  /// 不稳定血浆常驻动画控制组件（跟随增益同生同灭，纯视觉）：
  /// 主视觉为原版血雾光环（PlasmaAura）经 clone-and-retint 生成；
  /// 次要增强为小型自绘 Mesh（中央核心 + 地面符印）。
  /// 继承 MarkFxBase，由 Tracker 自动管理生成、单位跟随与 0.3s 平滑淡出。
  /// </summary>
  public class UnstablePlasmaAnim : MarkFxBase
  {
    // ── 主视觉：原版血雾光环克隆体 ──
    private GameObject _plasmaAura;

    // ── 次要增强：自绘 Mesh 配置 ──
    private const int IcoSubdiv = 0;          // 二十面体细分级数（0 = 20 面）
    private const int SigilPetals = 16;       // 地面符印瓣数（简化）

    // ── 次要增强：Mesh 渲染组件 ──
    private MeshRenderer _coreRenderer;
    private Transform _coreTransform;
    private MeshRenderer _sigilRenderer;
    private Transform _sigilTransform;

    // ── 材质实例（每个渲染器需独立实例以支持独立 alpha 调节）──
    private Material _coreMat;
    private Material _sigilMat;

    // ── 静态 Mesh 缓存（所有实例共用，只生成一次）──
    private static Mesh _coreMesh;
    private static Mesh _sigilMesh;

    // ── 动画状态 ──
    private float _beat;                       // 心搏包络 0~1
    private const float CycleSec = 1.0f;       // 心动周期

    protected override bool Build(Buff buff)
    {
      var view = Unit?.View;
      if (view == null)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 不稳定血浆放弃：单位 View 为空。");
        return false;
      }

      // ── 主视觉：原版血雾光环（BloodHazeBuff）clone-and-retint ──
      // 使用 TintedAuraFx 验证过的路径：FxTint.SpawnAttached + FxLibrary.PlasmaAura
      var plasmaLink = FxLibrary.PlasmaAura;
      if (plasmaLink == null || !plasmaLink.Exists())
      {
        Main.Mod.Logger.Warning("[RhodesDoctor] 不稳定血浆：PlasmaAura 素材不可用，仅使用自绘增强。");
      }
      else
      {
        // 重染成华法琳的动脉猩红，保留灯光
        _plasmaAura = FxTint.SpawnAttached(
          plasmaLink,
          view.transform,
          UnstablePlasmaFx.ArterialBlood,
          lift: 0.15f,        // 低 lift 保留血雾的层次感
          keepLights: true,
          alphaScale: 1.0f);

        if (_plasmaAura != null)
        {
          Dbg.Log("[RhodesDoctor] 不稳定血浆·血雾光环已生成。");
        }
      }

      // ── 次要增强：自绘 Mesh（中央核心 + 地面符印）──
      var matPulse = Mat(UnstablePlasmaFx.AdrenalineRed);
      var matDark  = Mat(UnstablePlasmaFx.ShadowBlood);
      if (matPulse == null || matDark == null)
      {
        // 材质不可用时，只要主视觉成功就返回 true
        if (_plasmaAura != null) return true;
        Main.Mod.Logger.Error("[RhodesDoctor] 不稳定血浆放弃：材质不可用且主视觉也失败。");
        return false;
      }

      EnsureMeshes();
      int layer = transform.gameObject.layer;

      // 中央脉动核心（小型二十面体，作为点缀）
      _coreMat = new Material(matPulse) { name = "RD_UP_CoreMat" };
      var coreGo = CreateMeshObject("RD_UP_Core", _coreMesh, _coreMat, layer);
      coreGo.transform.SetParent(transform, false);
      _coreRenderer = coreGo.GetComponent<MeshRenderer>();
      _coreTransform = coreGo.transform;

      // 地面共振符印（简化版）
      _sigilMat = new Material(matDark) { name = "RD_UP_SigilMat" };
      var sigilGo = CreateMeshObject("RD_UP_Sigil", _sigilMesh, _sigilMat, layer);
      sigilGo.transform.SetParent(transform, false);
      _sigilRenderer = sigilGo.GetComponent<MeshRenderer>();
      _sigilTransform = sigilGo.transform;

      // 主视觉或次要增强至少有一个成功即可
      return _plasmaAura != null || _coreRenderer != null;
    }

    protected override void Tick(float dt)
    {
      if (Unit?.View == null) return;
      var root = Unit.Position;
      float alpha = Master;

      // 更新心搏包络
      UpdateCardiacBeat();

      // 主视觉：原版血雾的 alpha 跟随 Master 淡出
      UpdatePlasmaAura(alpha);

      // 次要增强：中央脉动核心
      if (_coreTransform != null) UpdateCore(root, alpha);

      // 次要增强：地面共振符印
      if (_sigilTransform != null) UpdateSigil(root, alpha);
    }

    /// <summary>心搏节律（lub-dub）：S1 收缩 → S2 重搏 → 舒张</summary>
    private void UpdateCardiacBeat()
    {
      float c = Mathf.Repeat(T, CycleSec);
      if (c < 0.18f)
      {
        // S1 收缩期：快速上升后回落
        float p = c / 0.18f;
        _beat = Mathf.Sin(Mathf.Min(1f, p * 3f) * Mathf.PI * 0.5f) * (1f - 0.5f * p);
      }
      else if (c < 0.22f)
      {
        // S1→S2 间隙
        _beat = 0.5f * (1f - (c - 0.18f) / 0.04f);
      }
      else if (c < 0.38f)
      {
        // S2 重搏期
        float p = (c - 0.22f) / 0.16f;
        _beat = 0.65f * Mathf.Sin(p * Mathf.PI);
      }
      else
      {
        // 舒张期
        float p = (c - 0.38f) / 0.62f;
        _beat = 0.15f * (1f - p * p);
      }
    }

    /// <summary>主视觉：原版血雾光环的 alpha 跟随 Master 平滑淡出</summary>
    private void UpdatePlasmaAura(float alpha)
    {
      if (_plasmaAura == null) return;
      // 使用 FxTint.SetAlpha 让整个克隆体的所有材质颜色属性跟随 Master 淡出
      FxTint.SetAlpha(_plasmaAura, alpha);
    }

    /// <summary>次要增强：中央脉动核心（心搏收缩膨胀 + 表面微扰）</summary>
    private void UpdateCore(Vector3 root, float alpha)
    {
      float chestY = Height * 0.55f;
      var pos = root + Vector3.up * chestY;

      // 基础大小随心搏收缩膨胀（比原版小，作为点缀）
      float baseR = Mathf.Clamp(Height * 0.05f, 0.06f, 0.14f);
      float pulse = 1f + 0.35f * _beat;
      // 表面微扰（通过非均匀缩放模拟）
      float jitterX = 1f + 0.08f * Mathf.Sin(T * 7.3f);
      float jitterY = 1f + 0.08f * Mathf.Sin(T * 6.1f + 1f);
      float jitterZ = 1f + 0.08f * Mathf.Sin(T * 8.7f + 2f);
      var scale = new Vector3(baseR * pulse * jitterX, baseR * pulse * jitterY, baseR * pulse * jitterZ);

      // 缓慢自旋
      var rot = Quaternion.Euler(T * 12f, T * 23f, T * 8f);

      _coreTransform.position = pos;
      _coreTransform.rotation = rot;
      _coreTransform.localScale = scale;

      // Alpha：心搏高峰更亮（比主视觉稍暗，作为点缀）
      float intensity = 0.50f + 0.30f * _beat;
      SetMaterialAlpha(_coreMat, alpha * intensity);
    }

    /// <summary>次要增强：地面共振符印（心搏呼吸 + 缓慢自旋）</summary>
    private void UpdateSigil(Vector3 root, float alpha)
    {
      var pos = root + Vector3.up * 0.03f;

      // 心搏呼吸微胀
      float baseR = Radius * 0.8f;  // 比原版小，作为点缀
      float pulse = 1f + 0.08f * _beat;
      var scale = new Vector3(baseR * pulse, 1f, baseR * pulse);

      // 缓慢自旋
      var rot = Quaternion.Euler(0f, T * 8f, 0f);

      _sigilTransform.position = pos;
      _sigilTransform.rotation = rot;
      _sigilTransform.localScale = scale;

      // Alpha：基础较暗 + 心搏微亮
      float intensity = 0.30f + 0.20f * _beat;
      SetMaterialAlpha(_sigilMat, alpha * intensity);
    }

    // ────────────────────────────────────────────────────────────────
    // 辅助方法
    // ────────────────────────────────────────────────────────────────

    private static void SetMaterialAlpha(Material mat, float a)
    {
      if (mat == null) return;
      a = Mathf.Clamp01(a);
      if (mat.HasProperty("_Color"))
      {
        var c = mat.color;
        c.a = a;
        mat.color = c;
      }
      if (mat.HasProperty("_TintColor"))
      {
        var c = mat.GetColor("_TintColor");
        c.a = a;
        mat.SetColor("_TintColor", c);
      }
    }

    private static GameObject CreateMeshObject(string name, Mesh mesh, Material mat, int layer)
    {
      var go = new GameObject(name) { layer = layer };
      var mf = go.AddComponent<MeshFilter>();
      mf.sharedMesh = mesh;
      var mr = go.AddComponent<MeshRenderer>();
      mr.sharedMaterial = mat;
      mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
      mr.receiveShadows = false;
      return go;
    }

    /// <summary>确保静态 Mesh 缓存已生成（只执行一次）</summary>
    private static void EnsureMeshes()
    {
      if (_coreMesh == null) _coreMesh = BuildIcosahedronMesh(IcoSubdiv);
      if (_sigilMesh == null) _sigilMesh = BuildSigilMesh(SigilPetals);
    }

    /// <summary>二十面体 Mesh（可选细分）</summary>
    private static Mesh BuildIcosahedronMesh(int subdivisions)
    {
      var mesh = new Mesh { name = "RD_UP_IcoMesh" };
      float t = (1f + Mathf.Sqrt(5f)) / 2f;
      var v = new Vector3[]
      {
        new Vector3(-1,  t,  0).normalized,
        new Vector3( 1,  t,  0).normalized,
        new Vector3(-1, -t,  0).normalized,
        new Vector3( 1, -t,  0).normalized,
        new Vector3( 0, -1,  t).normalized,
        new Vector3( 0,  1,  t).normalized,
        new Vector3( 0, -1, -t).normalized,
        new Vector3( 0,  1, -t).normalized,
        new Vector3( t,  0, -1).normalized,
        new Vector3( t,  0,  1).normalized,
        new Vector3(-t,  0, -1).normalized,
        new Vector3(-t,  0,  1).normalized,
      };
      var tri = new int[]
      {
        0,11, 5,  0, 5, 1,  0, 1, 7,  0, 7,10,  0,10,11,
        1, 5, 9,  5,11, 4, 11,10, 2, 10, 7, 6,  7, 1, 8,
        3, 9, 4,  3, 4, 2,  3, 2, 6,  3, 6, 8,  3, 8, 9,
        4, 9, 5,  2, 4,11,  6, 2,10,  8, 6, 7,  9, 8, 1,
      };

      // 简单细分（可选）
      var verts = new System.Collections.Generic.List<Vector3>(v);
      var tris = new System.Collections.Generic.List<int>(tri);
      for (int s = 0; s < subdivisions; s++)
      {
        var newTris = new System.Collections.Generic.List<int>();
        var midCache = new System.Collections.Generic.Dictionary<long, int>();
        for (int i = 0; i < tris.Count; i += 3)
        {
          int i0 = tris[i], i1 = tris[i + 1], i2 = tris[i + 2];
          int m0 = MidPoint(i0, i1, verts, midCache);
          int m1 = MidPoint(i1, i2, verts, midCache);
          int m2 = MidPoint(i2, i0, verts, midCache);
          newTris.AddRange(new[] { i0, m0, m2, i1, m1, m0, i2, m2, m1, m0, m1, m2 });
        }
        tris = newTris;
      }

      // 生成 UV（简单球面映射）
      var uvs = new Vector2[verts.Count];
      for (int i = 0; i < verts.Count; i++)
      {
        var n = verts[i].normalized;
        uvs[i] = new Vector2(
          0.5f + Mathf.Atan2(n.z, n.x) / (2f * Mathf.PI),
          0.5f - Mathf.Asin(n.y) / Mathf.PI);
      }

      mesh.SetVertices(verts);
      mesh.SetTriangles(tris, 0);
      mesh.SetUVs(0, new System.Collections.Generic.List<Vector2>(uvs));
      mesh.RecalculateNormals();
      mesh.RecalculateBounds();
      return mesh;
    }

    private static int MidPoint(int i0, int i1,
      System.Collections.Generic.List<Vector3> verts,
      System.Collections.Generic.Dictionary<long, int> cache)
    {
      long key = i0 < i1 ? ((long)i0 << 32) | (uint)i1 : ((long)i1 << 32) | (uint)i0;
      if (cache.TryGetValue(key, out int mid)) return mid;
      var p = ((verts[i0] + verts[i1]) * 0.5f).normalized;
      mid = verts.Count;
      verts.Add(p);
      cache[key] = mid;
      return mid;
    }

    /// <summary>地面符印 Mesh（放射状渐隐圆盘）</summary>
    private static Mesh BuildSigilMesh(int petals)
    {
      var mesh = new Mesh { name = "RD_UP_SigilMesh" };
      int segments = petals * 2; // 每瓣两点（内外交替）
      int vertCount = segments + 1;
      var verts = new Vector3[vertCount];
      var uvs = new Vector2[vertCount];
      var colors = new Color32[vertCount];

      // 中心顶点
      verts[0] = Vector3.zero;
      uvs[0] = new Vector2(0.5f, 0.5f);
      colors[0] = new Color32(255, 255, 255, 255);

      // 边缘顶点（长短交替形成锯齿）
      for (int i = 0; i < segments; i++)
      {
        float angle = i * Mathf.PI * 2f / segments;
        float r = (i % 2 == 0) ? 1f : 0.75f; // 长短交替
        verts[i + 1] = new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
        uvs[i + 1] = new Vector2(0.5f + Mathf.Cos(angle) * 0.5f * r, 0.5f + Mathf.Sin(angle) * 0.5f * r);
        colors[i + 1] = new Color32(255, 255, 255, (byte)(i % 2 == 0 ? 60 : 120)); // 边缘较淡
      }

      // 三角形（扇形）
      var tris = new int[segments * 3];
      for (int i = 0; i < segments; i++)
      {
        tris[i * 3] = 0;
        tris[i * 3 + 1] = i + 1;
        tris[i * 3 + 2] = (i + 1) % segments + 1;
      }

      mesh.vertices = verts;
      mesh.triangles = tris;
      mesh.uv = uvs;
      mesh.colors32 = colors;
      mesh.RecalculateNormals();
      mesh.RecalculateBounds();
      return mesh;
    }

    // ────────────────────────────────────────────────────────────────
    // 清理：正确 override 基类 OnDestroy，确保 Tracker.Forget 被调用
    // ────────────────────────────────────────────────────────────────
    protected override void OnDestroy()
    {
      // 销毁主视觉（原版血雾克隆体）
      if (_plasmaAura != null)
      {
        Destroy(_plasmaAura);
        _plasmaAura = null;
      }

      // 销毁次要增强的材质实例（Mesh 是静态缓存，不销毁）
      if (_coreMat != null)
      {
        Destroy(_coreMat);
        _coreMat = null;
      }
      if (_sigilMat != null)
      {
        Destroy(_sigilMat);
        _sigilMat = null;
      }

      // 调用基类清理，确保从 Tracker._live 字典中移除自身
      base.OnDestroy();
    }
  }
}
