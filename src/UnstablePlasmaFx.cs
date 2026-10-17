// 不稳定血浆（2 环·华法琳）专属自绘 3D 程序化视觉特效（纯视觉，不影响结算）。
//
// 视觉体系（Sanguine Resonance Matrix — 以 Mesh/Quad/Facet 为主体的血魔共振矩阵）：
//   1. 【轨道血晶碎片（Orbital Blood Shards）】：
//      六枚菱形 Mesh 面片以双层轨道环绕目标胸腔（120° 相位，内三外三反向），
//      各碎片自旋并随心搏闪烁微张，营造晶格悬浮的血魔仪式感。
//   2. 【中央脉动核心（Central Pulse Core）】：
//      一枚 20 面体低聚球 Mesh 悬浮于胸口，以真实心搏节律（lub-dub 二段式）收缩膨胀，
//      表面顶点随时间微扰动，呈现不稳定血浆"将溢未溢"的张力。
//   3. 【地面共振符印（Ground Resonance Sigil）】：
//      脚底一枚 24 瓣放射状 Mesh 圆盘，中心向外渐隐，随心搏微幅呼吸并缓慢自旋，
//      呈现试剂沉淀的仪式刻痕。
//   4. 【升腾血浆微尘（Ascending Plasma Wisps）】：
//      八枚正对相机的 Billboard Quad Mesh 自足底螺旋攀升，各自相位错开，
//      以正弦波沿高度淡入淡出，营造血能蒸腾的氛围。
//
// 实现要点：
//   · 主体可视元素均为 Mesh（MeshFilter + MeshRenderer），不以 LineRenderer / TrailRenderer 为主。
//   · 所有 Mesh 在 Build 时一次性生成并缓存，Tick 中仅调整 Transform / Color，零运行时 GC。
//   · 材质通过 MarkFxKit.Mat（→ SwordStreakFx.StreakMaterialPlain）克隆，不使用 Shader.Find。
//   · 继承 MarkFxBase，由 Tracker 自动管理生成、跟随与 0.3 s 淡出。
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
    internal static readonly Color ReagentAmber  = new Color(1.00f, 0.80f, 0.32f, 1f); // 试剂流金
    internal static readonly Color CrystalWhite  = new Color(1.00f, 0.94f, 0.96f, 1f); // 晶核高亮
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
  /// 继承 MarkFxBase，由 Tracker 自动管理生成、单位跟随与 0.3s 平滑淡出。
  /// </summary>
  public class UnstablePlasmaAnim : MarkFxBase
  {
    // ── 几何体常量配置 ──
    private const int ShardCount = 6;         // 轨道血晶数量（双层各 3）
    private const int WispCount = 8;          // 升腾微尘数量
    private const int IcoSubdiv = 1;          // 二十面体细分级数（0 = 20 面，1 = 80 面）
    private const int SigilPetals = 24;       // 地面符印瓣数

    // ── Mesh 渲染组件 ──
    private readonly MeshRenderer[] _shardRenderers = new MeshRenderer[ShardCount];
    private readonly Transform[] _shardTransforms = new Transform[ShardCount];
    private MeshRenderer _coreRenderer;
    private Transform _coreTransform;
    private MeshRenderer _sigilRenderer;
    private Transform _sigilTransform;
    private readonly MeshRenderer[] _wispRenderers = new MeshRenderer[WispCount];
    private readonly Transform[] _wispTransforms = new Transform[WispCount];

    // ── 材质实例（每个渲染器需独立实例以支持独立 alpha 调节）──
    private readonly Material[] _shardMats = new Material[ShardCount];
    private Material _coreMat;
    private Material _sigilMat;
    private readonly Material[] _wispMats = new Material[WispCount];

    // ── 静态 Mesh 缓存（所有实例共用，只生成一次）──
    private static Mesh _shardMesh;
    private static Mesh _coreMesh;
    private static Mesh _sigilMesh;
    private static Mesh _wispMesh;

    // ── 动画状态 ──
    private float _beat;                       // 心搏包络 0~1
    private const float CycleSec = 1.0f;       // 心动周期

    protected override bool Build(Buff buff)
    {
      // 获取材质模板
      var matBlood = Mat(UnstablePlasmaFx.ArterialBlood);
      var matPulse = Mat(UnstablePlasmaFx.AdrenalineRed);
      var matAmber = Mat(UnstablePlasmaFx.ReagentAmber);
      var matWhite = Mat(UnstablePlasmaFx.CrystalWhite);
      var matDark  = Mat(UnstablePlasmaFx.ShadowBlood);
      if (matBlood == null || matPulse == null || matAmber == null || matWhite == null || matDark == null)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 不稳定血浆放弃：StreakMaterialPlain 无可用材质。");
        return false;
      }

      EnsureMeshes();
      int layer = transform.gameObject.layer;

      // 1. 轨道血晶碎片（6 枚：内层 3 枚血红，外层 3 枚流金）
      for (int i = 0; i < ShardCount; i++)
      {
        var mat = i < 3 ? matBlood : matAmber;
        _shardMats[i] = new Material(mat) { name = "RD_UP_ShardMat_" + i };
        var go = CreateMeshObject("RD_UP_Shard_" + i, _shardMesh, _shardMats[i], layer);
        go.transform.SetParent(transform, false);
        _shardRenderers[i] = go.GetComponent<MeshRenderer>();
        _shardTransforms[i] = go.transform;
      }

      // 2. 中央脉动核心（二十面体）
      _coreMat = new Material(matPulse) { name = "RD_UP_CoreMat" };
      var coreGo = CreateMeshObject("RD_UP_Core", _coreMesh, _coreMat, layer);
      coreGo.transform.SetParent(transform, false);
      _coreRenderer = coreGo.GetComponent<MeshRenderer>();
      _coreTransform = coreGo.transform;

      // 3. 地面共振符印
      _sigilMat = new Material(matDark) { name = "RD_UP_SigilMat" };
      var sigilGo = CreateMeshObject("RD_UP_Sigil", _sigilMesh, _sigilMat, layer);
      sigilGo.transform.SetParent(transform, false);
      _sigilRenderer = sigilGo.GetComponent<MeshRenderer>();
      _sigilTransform = sigilGo.transform;

      // 4. 升腾血浆微尘（8 枚 billboard quad）
      for (int i = 0; i < WispCount; i++)
      {
        _wispMats[i] = new Material(matWhite) { name = "RD_UP_WispMat_" + i };
        var go = CreateMeshObject("RD_UP_Wisp_" + i, _wispMesh, _wispMats[i], layer);
        go.transform.SetParent(transform, false);
        _wispRenderers[i] = go.GetComponent<MeshRenderer>();
        _wispTransforms[i] = go.transform;
      }

      return true;
    }

    protected override void Tick(float dt)
    {
      if (Unit?.View == null) return;
      var root = Unit.Position;
      float alpha = Master;

      // 更新心搏包络
      UpdateCardiacBeat();

      // 模块 1：轨道血晶碎片
      UpdateShards(root, alpha);

      // 模块 2：中央脉动核心
      UpdateCore(root, alpha);

      // 模块 3：地面共振符印
      UpdateSigil(root, alpha);

      // 模块 4：升腾血浆微尘
      UpdateWisps(root, alpha);
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

    /// <summary>轨道血晶碎片：双层反向环绕 + 自旋 + 心搏闪烁</summary>
    private void UpdateShards(Vector3 root, float alpha)
    {
      float chestY = Height * 0.55f;
      float innerR = Radius * 0.65f;
      float outerR = Radius * 1.15f;
      const float innerSpeed = 1.2f;
      const float outerSpeed = -0.9f;
      float baseScale = Mathf.Clamp(Height * 0.055f, 0.08f, 0.18f);

      for (int i = 0; i < ShardCount; i++)
      {
        bool inner = i < 3;
        int idx = inner ? i : (i - 3);
        float phase = idx * (Mathf.PI * 2f / 3f);
        float orbitR = inner ? innerR : outerR;
        float speed = inner ? innerSpeed : outerSpeed;
        float orbitA = phase + T * speed;

        // 轨道位置
        float bob = Mathf.Sin(T * 2.2f + i * 1.1f) * (Height * 0.04f);
        var pos = root + new Vector3(
          Mathf.Cos(orbitA) * orbitR,
          chestY + bob + (inner ? 0f : Height * 0.08f),
          Mathf.Sin(orbitA) * orbitR);

        // 自旋：绕自身轴旋转 + 随公转切向倾斜
        float spin = T * 3.5f + i * 0.8f;
        float tilt = 0.25f + 0.15f * Mathf.Sin(T * 1.5f + i);
        var rot = Quaternion.Euler(tilt * 57.3f, spin * 57.3f, Mathf.Sin(T * 2f + i) * 15f);

        // 心搏微张
        float pulse = 1f + 0.25f * _beat;
        float scale = baseScale * pulse * (inner ? 1f : 1.15f);

        _shardTransforms[i].position = pos;
        _shardTransforms[i].rotation = rot;
        _shardTransforms[i].localScale = new Vector3(scale, scale * 1.6f, scale * 0.3f);

        // Alpha 与心搏联动闪烁
        float shimmer = 0.75f + 0.25f * _beat + 0.1f * Mathf.Sin(T * 8f + i * 2f);
        SetMaterialAlpha(_shardMats[i], alpha * shimmer);
      }
    }

    /// <summary>中央脉动核心：心搏收缩膨胀 + 表面微扰</summary>
    private void UpdateCore(Vector3 root, float alpha)
    {
      float chestY = Height * 0.55f;
      var pos = root + Vector3.up * chestY;

      // 基础大小随心搏收缩膨胀
      float baseR = Mathf.Clamp(Height * 0.08f, 0.10f, 0.22f);
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

      // Alpha：心搏高峰更亮
      float intensity = 0.70f + 0.30f * _beat;
      SetMaterialAlpha(_coreMat, alpha * intensity);
    }

    /// <summary>地面共振符印：心搏呼吸 + 缓慢自旋</summary>
    private void UpdateSigil(Vector3 root, float alpha)
    {
      var pos = root + Vector3.up * 0.03f;

      // 心搏呼吸微胀
      float baseR = Radius * 1.05f;
      float pulse = 1f + 0.08f * _beat;
      var scale = new Vector3(baseR * pulse, 1f, baseR * pulse);

      // 缓慢自旋
      var rot = Quaternion.Euler(0f, T * 8f, 0f);

      _sigilTransform.position = pos;
      _sigilTransform.rotation = rot;
      _sigilTransform.localScale = scale;

      // Alpha：基础较暗 + 心搏微亮
      float intensity = 0.40f + 0.25f * _beat;
      SetMaterialAlpha(_sigilMat, alpha * intensity);
    }

    /// <summary>升腾血浆微尘：螺旋攀升 + 正弦淡入淡出 + Billboard</summary>
    private void UpdateWisps(Vector3 root, float alpha)
    {
      float maxH = Height * 1.15f;
      float spiralR = Radius * 0.55f;
      const float riseSpeed = 0.35f;
      const float spiralTurns = 1.5f;
      float wispSize = Mathf.Clamp(Height * 0.025f, 0.03f, 0.08f);

      // Billboard 朝向相机
      var cam = Camera.main;
      Quaternion billboardRot = cam != null
        ? Quaternion.LookRotation(cam.transform.forward, cam.transform.up)
        : Quaternion.identity;

      for (int i = 0; i < WispCount; i++)
      {
        float phase = i * (1f / WispCount);
        // 循环上升：归一化高度 0~1
        float normH = Mathf.Repeat(T * riseSpeed + phase, 1f);
        float y = normH * maxH;

        // 螺旋轨迹
        float angle = phase * Mathf.PI * 2f + normH * spiralTurns * Mathf.PI * 2f;
        // 螺旋半径：底部宽 → 腰部收 → 顶部散
        float r = spiralR * (0.7f + 0.4f * Mathf.Sin(normH * Mathf.PI));
        var pos = root + new Vector3(Mathf.Cos(angle) * r, y, Mathf.Sin(angle) * r);

        // 淡入淡出：底部与顶部淡，中段亮
        float fadeIn = Mathf.SmoothStep(0f, 1f, normH * 5f);
        float fadeOut = Mathf.SmoothStep(0f, 1f, (1f - normH) * 4f);
        float wispAlpha = fadeIn * fadeOut * (0.5f + 0.5f * _beat);

        // 大小微扰
        float sizeVar = wispSize * (0.8f + 0.4f * Mathf.Sin(T * 6f + i * 3f));

        _wispTransforms[i].position = pos;
        _wispTransforms[i].rotation = billboardRot;
        _wispTransforms[i].localScale = Vector3.one * sizeVar;

        SetMaterialAlpha(_wispMats[i], alpha * wispAlpha);
      }
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
      if (_shardMesh == null) _shardMesh = BuildShardMesh();
      if (_coreMesh == null) _coreMesh = BuildIcosahedronMesh(IcoSubdiv);
      if (_sigilMesh == null) _sigilMesh = BuildSigilMesh(SigilPetals);
      if (_wispMesh == null) _wispMesh = BuildQuadMesh();
    }

    /// <summary>菱形碎片 Mesh（双面，XY 平面，中心在原点）</summary>
    private static Mesh BuildShardMesh()
    {
      var mesh = new Mesh { name = "RD_UP_ShardMesh" };
      // 菱形四顶点：上、右、下、左
      var verts = new Vector3[]
      {
        new Vector3(0f, 0.5f, 0f),   // 上
        new Vector3(0.5f, 0f, 0f),   // 右
        new Vector3(0f, -0.5f, 0f),  // 下
        new Vector3(-0.5f, 0f, 0f),  // 左
        // 背面（重复顶点以支持双面渲染）
        new Vector3(0f, 0.5f, 0f),
        new Vector3(0.5f, 0f, 0f),
        new Vector3(0f, -0.5f, 0f),
        new Vector3(-0.5f, 0f, 0f),
      };
      var tris = new int[]
      {
        // 正面
        0, 1, 2,
        0, 2, 3,
        // 背面（反绕）
        6, 5, 4,
        7, 6, 4,
      };
      var uvs = new Vector2[]
      {
        new Vector2(0.5f, 1f),
        new Vector2(1f, 0.5f),
        new Vector2(0.5f, 0f),
        new Vector2(0f, 0.5f),
        new Vector2(0.5f, 1f),
        new Vector2(1f, 0.5f),
        new Vector2(0.5f, 0f),
        new Vector2(0f, 0.5f),
      };
      mesh.vertices = verts;
      mesh.triangles = tris;
      mesh.uv = uvs;
      mesh.RecalculateNormals();
      mesh.RecalculateBounds();
      return mesh;
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

    /// <summary>Billboard Quad Mesh（XY 平面，中心在原点）</summary>
    private static Mesh BuildQuadMesh()
    {
      var mesh = new Mesh { name = "RD_UP_QuadMesh" };
      var verts = new Vector3[]
      {
        new Vector3(-0.5f, -0.5f, 0f),
        new Vector3( 0.5f, -0.5f, 0f),
        new Vector3( 0.5f,  0.5f, 0f),
        new Vector3(-0.5f,  0.5f, 0f),
      };
      var uvs = new Vector2[]
      {
        new Vector2(0f, 0f),
        new Vector2(1f, 0f),
        new Vector2(1f, 1f),
        new Vector2(0f, 1f),
      };
      var tris = new int[] { 0, 1, 2, 0, 2, 3 };
      mesh.vertices = verts;
      mesh.triangles = tris;
      mesh.uv = uvs;
      mesh.RecalculateNormals();
      mesh.RecalculateBounds();
      return mesh;
    }

    // ────────────────────────────────────────────────────────────────
    // 清理：销毁材质实例（Mesh 是静态缓存，不销毁）
    // ────────────────────────────────────────────────────────────────
    private void OnDestroy()
    {
      for (int i = 0; i < ShardCount; i++)
        if (_shardMats[i] != null) Destroy(_shardMats[i]);
      if (_coreMat != null) Destroy(_coreMat);
      if (_sigilMat != null) Destroy(_sigilMat);
      for (int i = 0; i < WispCount; i++)
        if (_wispMats[i] != null) Destroy(_wispMats[i]);
    }
  }
}
