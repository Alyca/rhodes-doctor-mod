// 巴别塔的恶灵（核心职业特技·死灵/变化·博士 Doctor）专属自绘视觉特效（纯视觉，不影响数值结算）。
//
// 对应角色：【博士】（Doctor，前文明源石主宰者“巴别塔的恶灵”）
// 机制体系：核心职业特技，形态切换：敏捷形态 Agile / 坚韧形态 Enduring（共三个阶段 V1/V2/V3 六个形态增益），
//           每日轮数持续，结束或不足时触发 2 轮恶灵余波（AftermathRestrictBuff / AftermathSpeedBuff）。
//
// 视觉体系（四位一体）：
//   1. 【恶灵形态开启瞬间（SpecterActivateAnim）】：
//      角色胸口与脚下源石神经突触猛烈向外放射激荡；
//      头顶上方虚空撕裂，前文明黑冠精神虚影显现（~0.8s）。
//   2. 【形态常驻跟随（SpecterFormAnim，继承 MarkFxBase，随形态增益全程跟随）】：
//      头顶悬浮立体自旋的前文明“黑冠”精神虚影（文明的存续），向下垂落细碎微光精神粒子；
//      脚底与周身蔓延脉动的源石神经突触网络：
//        - 敏捷形态（IsAgile = true）：偏炽烈源石金光流束与迅捷脉冲；
//        - 坚韧形态（IsAgile = false）：偏幽邃精神紫结晶晶壁与重力场波纹；
//      持续时间结束或主动关闭时 0.3s 平滑淡出。
//   3. 【恶灵形态结束/关闭瞬间（SpecterDeactivateAnim）】：
//      黑冠瓦解化为漫天源石星芒光尘散落，源石突触收缩敛入体内（~0.8s）。
//   4. 【恶灵余波期间（SpecterAftermathAnim，继承 MarkFxBase，随 AftermathRestrictBuff 跟随 2 轮）】：
//      疲惫虚弱的暗沉余光与微弱神经余颤，0.3s 平滑淡出。
//
// 渲染技术规范：
//   · 色彩体系：
//     - 漆黑源石晶体（OriginiumObsidian）：深邃源石晶体，在加法材质下配合黑金/暗紫边缘辉光防透空隐形；
//     - 炽烈源石金（OriginiumGold）：敏捷突触与能量脉冲流束；
//     - 幽邃精神紫（NeuralViolet）：坚韧晶壁结晶与重力波纹；
//     - 文明余辉白金（CrownPlatinum）：前文明黑冠精神虚影核心；
//     - 恶灵余波暗沉色（AftermathDim）：疲惫虚弱暗光与神经余颤。
//   · 材质统一复用 MarkFxKit.Mat（底模 SwordStreakFx.StreakMaterialPlain），严禁 Shader.Find 与原版 PrefabLink。
//   · 零运行时 GC，组件自管理生命周期与超时 Destroy。
//   · 锚点防角色模型深度遮挡，线宽与尺度须适配信仰 45° 俯视镜头，具备厚度与实体感。
//
// 注：基础脚手架已就绪，所有具体视觉几何、粒子物理与缓动算法全权交由 Macroscope 在 PR Check-Run 中自主完成。
using System;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Buffs;
using UnityEngine;
using static RhodesDoctor.MarkFxKit;

namespace RhodesDoctor
{
  internal static class SpecterFormFx
  {
    // ── 巴别塔恶灵·前文明源石调色盘 ──
    // 加法材质下颜色越暗越透明，漆黑源石晶体需结合暗金/暗紫边缘辉光，坚决防止加法材质透空隐形。
    internal static readonly Color OriginiumObsidian = new Color(0.12f, 0.08f, 0.18f, 1f); // 漆黑源石晶体底色
    internal static readonly Color OriginiumGold     = new Color(1.40f, 0.85f, 0.18f, 1f); // 炽烈源石金·敏捷突触与神经能量流束
    internal static readonly Color NeuralViolet      = new Color(0.68f, 0.28f, 1.25f, 1f); // 幽邃精神紫·坚韧结晶晶壁与重力场波纹
    internal static readonly Color CrownPlatinum     = new Color(0.95f, 0.95f, 1.20f, 1f); // 文明余辉白金·前文明黑冠精神虚影核心
    internal static readonly Color AftermathDim      = new Color(0.40f, 0.30f, 0.45f, 1f); // 恶灵余波·疲惫虚弱暗光与神经余颤

    // ── 蓝图 GUID 映射（归一化：去横线小写）──
    private static string Norm(string g) => (g ?? "").Replace("-", "").ToLowerInvariant();

    private static readonly string[] AgileGuidsNorm =
    {
      Norm(Guids.AgileBuffV1),
      Norm(Guids.AgileBuffV2),
      Norm(Guids.AgileBuffV3),
    };

    private static readonly string[] EnduringGuidsNorm =
    {
      Norm(Guids.EnduringBuffV1),
      Norm(Guids.EnduringBuffV2),
      Norm(Guids.EnduringBuffV3),
    };

    private static readonly string AftermathGuidNorm = Norm(Guids.AftermathRestrictBuff);

    // ── 常驻跟踪器（由 MarkFxWatcher 分发生命周期）──
    internal static readonly MarkFxTracker AgileTrackerV1 = new MarkFxTracker(
      "RhodesDoctorAgileFormV1", "巴别塔恶灵·敏捷形态V1", go => AttachForm(go, true), Guids.AgileBuffV1);
    internal static readonly MarkFxTracker AgileTrackerV2 = new MarkFxTracker(
      "RhodesDoctorAgileFormV2", "巴别塔恶灵·敏捷形态V2", go => AttachForm(go, true), Guids.AgileBuffV2);
    internal static readonly MarkFxTracker AgileTrackerV3 = new MarkFxTracker(
      "RhodesDoctorAgileFormV3", "巴别塔恶灵·敏捷形态V3", go => AttachForm(go, true), Guids.AgileBuffV3);

    internal static readonly MarkFxTracker EnduringTrackerV1 = new MarkFxTracker(
      "RhodesDoctorEnduringFormV1", "巴别塔恶灵·体质形态V1", go => AttachForm(go, false), Guids.EnduringBuffV1);
    internal static readonly MarkFxTracker EnduringTrackerV2 = new MarkFxTracker(
      "RhodesDoctorEnduringFormV2", "巴别塔恶灵·体质形态V2", go => AttachForm(go, false), Guids.EnduringBuffV2);
    internal static readonly MarkFxTracker EnduringTrackerV3 = new MarkFxTracker(
      "RhodesDoctorEnduringFormV3", "巴别塔恶灵·体质形态V3", go => AttachForm(go, false), Guids.EnduringBuffV3);

    internal static readonly MarkFxTracker AftermathTracker = new MarkFxTracker(
      "RhodesDoctorAftermathRestrict", "巴别塔恶灵·余波", go => go.AddComponent<SpecterAftermathAnim>(), Guids.AftermathRestrictBuff);

    private static SpecterFormAnim AttachForm(GameObject go, bool isAgile)
    {
      var anim = go.AddComponent<SpecterFormAnim>();
      anim.IsAgile = isAgile;
      return anim;
    }

    /// <summary>
    /// 开启瞬间爆发特效：胸口与脚下源石神经突触放射激荡，头顶虚空撕裂黑冠显现。
    /// </summary>
    internal static void PlayActivate(UnitEntityData caster, bool isAgile)
    {
      try
      {
        if (caster?.View == null) return;
        var go = new GameObject("RD_SpecterActivate") { layer = caster.View.gameObject.layer };
        var anim = go.AddComponent<SpecterActivateAnim>();
        if (!anim.Init(caster, isAgile)) UnityEngine.Object.Destroy(go);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 巴别塔恶灵开启特效失败（逻辑照常）：\n" + e);
      }
    }

    /// <summary>
    /// 结束瞬间瓦解特效：黑冠瓦解化为漫天源石星芒光尘，源石突触收缩敛入体内。
    /// </summary>
    internal static void PlayDeactivate(UnitEntityData caster)
    {
      try
      {
        if (caster?.View == null) return;
        var go = new GameObject("RD_SpecterDeactivate") { layer = caster.View.gameObject.layer };
        var anim = go.AddComponent<SpecterDeactivateAnim>();
        if (!anim.Init(caster)) UnityEngine.Object.Destroy(go);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 巴别塔恶灵解除特效失败（逻辑照常）：\n" + e);
      }
    }

    /// <summary>
    /// 增益添加事件分发：由 MarkFxWatcher.HandleBuffDidAdded 调用。
    /// </summary>
    internal static void OnBuffAdded(Buff buff)
    {
      if (buff?.Blueprint == null) return;
      var unit = buff.Owner?.Unit;
      if (unit?.View == null) return;

      string guid = Norm(buff.Blueprint.AssetGuid.ToString());
      bool isAgile = Array.IndexOf(AgileGuidsNorm, guid) >= 0;
      bool isEnduring = Array.IndexOf(EnduringGuidsNorm, guid) >= 0;

      if (isAgile || isEnduring)
      {
        PlayActivate(unit, isAgile);
        if (isAgile)
        {
          AgileTrackerV1.Added(buff);
          AgileTrackerV2.Added(buff);
          AgileTrackerV3.Added(buff);
        }
        else
        {
          EnduringTrackerV1.Added(buff);
          EnduringTrackerV2.Added(buff);
          EnduringTrackerV3.Added(buff);
        }
        return;
      }

      if (guid == AftermathGuidNorm)
      {
        AftermathTracker.Added(buff);
      }
    }

    /// <summary>
    /// 增益移除事件分发：由 MarkFxWatcher.HandleBuffDidRemoved 调用。
    /// </summary>
    internal static void OnBuffRemoved(Buff buff)
    {
      if (buff?.Blueprint == null) return;
      var unit = buff.Owner?.Unit;
      string guid = Norm(buff.Blueprint.AssetGuid.ToString());
      bool isAgile = Array.IndexOf(AgileGuidsNorm, guid) >= 0;
      bool isEnduring = Array.IndexOf(EnduringGuidsNorm, guid) >= 0;

      if (isAgile || isEnduring)
      {
        if (unit?.View != null) PlayDeactivate(unit);
        if (isAgile)
        {
          AgileTrackerV1.Removed(buff);
          AgileTrackerV2.Removed(buff);
          AgileTrackerV3.Removed(buff);
        }
        else
        {
          EnduringTrackerV1.Removed(buff);
          EnduringTrackerV2.Removed(buff);
          EnduringTrackerV3.Removed(buff);
        }
        return;
      }

      if (guid == AftermathGuidNorm)
      {
        AftermathTracker.Removed(buff);
      }
    }
  }


  /// <summary>
  /// 巴别塔恶灵实体几何载体：一个动态 Mesh（MeshFilter + MeshRenderer，材质复用 MarkFxKit.Mat）。
  /// 平直着色、三角不共享顶点：每个棱面独立明暗，晶体棱角分明。
  /// 顶点 / UV / 顶点色 / 索引全部在构造时按容量预分配；每帧 Begin → Tri… → End 只改写数组并上传，零托管分配。
  /// 顶点坐标相对宿主 transform（宿主旋转恒为 identity，每帧只摆 position）。
  /// 注：与 SwordHitFx 命中面片同一份 StreakMaterialPlain（粒子类 shader，不剔背面）。
  /// </summary>
  internal sealed class SolidMesh
  {
    private readonly Mesh _mesh;
    private readonly Vector3[] _v;
    private readonly Vector2[] _uv;
    private readonly Color32[] _c;
    private int _n, _prev;

    internal SolidMesh(Transform parent, string name, Material mat, int maxTris)
    {
      int cap = Mathf.Clamp(maxTris, 1, 20000) * 3;
      _v = new Vector3[cap];
      _uv = new Vector2[cap];
      _c = new Color32[cap];
      var idx = new int[cap];
      for (int i = 0; i < cap; i++) { idx[i] = i; _uv[i] = new Vector2(0.5f, 0.5f); }
      _mesh = new Mesh { name = name };
      _mesh.MarkDynamic();
      _mesh.vertices = _v;
      _mesh.uv = _uv;
      _mesh.colors32 = _c;
      _mesh.triangles = idx;
      var go = new GameObject(name) { layer = parent.gameObject.layer };
      go.transform.SetParent(parent, false);
      go.AddComponent<MeshFilter>().sharedMesh = _mesh;
      var mr = go.AddComponent<MeshRenderer>();
      mr.sharedMaterial = mat;
      mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
      mr.receiveShadows = false;
    }

    internal void Begin() => _n = 0;

    /// <summary>写一个三角：v 为贴图横向坐标（0/1 = 软边透明，0.5 = 实心）。超容量静默丢弃。</summary>
    internal void Tri(Vector3 a, Vector3 b, Vector3 c, float va, float vb, float vc, Color32 ca, Color32 cb, Color32 cc)
    {
      if (_n + 3 > _v.Length) return;
      int i = _n;
      _v[i] = a; _v[i + 1] = b; _v[i + 2] = c;
      _uv[i].y = va; _uv[i + 1].y = vb; _uv[i + 2].y = vc;
      _c[i] = ca; _c[i + 1] = cb; _c[i + 2] = cc;
      _n += 3;
    }

    internal void Tri(Vector3 a, Vector3 b, Vector3 c, Color32 col) => Tri(a, b, c, 0.5f, 0.5f, 0.5f, col, col, col);

    internal void End()
    {
      for (int i = _n; i < _prev; i++) { _v[i] = Vector3.zero; _c[i] = default(Color32); }
      _prev = _n;
      _mesh.vertices = _v;
      _mesh.uv = _uv;
      _mesh.colors32 = _c;
      _mesh.RecalculateBounds();
    }

    internal void Dispose()
    {
      if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
    }
  }

  /// <summary>
  /// 巴别塔恶灵四件套共用实体几何：多棱晶体、实心黑冠、宽幅流体带、实心环 / 盘、翻滚碎晶。
  /// 只写值类型，零 GC。棱面按固定光向明暗着色，漆黑本体（body）与形态色晶光（glow）双层叠加防加法透空。
  /// </summary>
  internal static class SpecterGeo
  {
    private const float TwoPi = Mathf.PI * 2f;
    private static readonly Vector3 Light = new Vector3(0.35f, 0.85f, -0.40f).normalized;

    internal static float Out3(float x) { x = 1f - Mathf.Clamp01(x); return 1f - x * x * x; }

    internal static Vector3 ViewFwd(Vector3 right, Vector3 up) => Vector3.Cross(right, up).normalized;

    internal static Color32 C(float bright, float a)
    {
      byte g = (byte)(Mathf.Clamp01(bright) * 255f);
      return new Color32(g, g, g, (byte)(Mathf.Clamp01(a) * 255f));
    }

    /// <summary>实心棱面：明暗 = 法线·光向（取绝对值，双面一致），同时写入漆黑本体与形态色晶光。</summary>
    internal static void Facet(SolidMesh body, SolidMesh glow, Vector3 p0, Vector3 p1, Vector3 p2, float a, float glowA)
    {
      var n = Vector3.Cross(p1 - p0, p2 - p0);
      float m = n.magnitude;
      float lit = m > 1e-7f ? Mathf.Abs(Vector3.Dot(n, Light)) / m : 0f;
      if (body != null && a > 0.003f) body.Tri(p0, p1, p2, C(0.30f + 0.70f * lit, a));
      if (glow != null && glowA > 0.003f) glow.Tri(p0, p1, p2, C(0.12f + 0.88f * lit * lit, glowA));
    }

    /// <summary>晶脊高光：沿棱线、正对镜头的柔边亮带（只作实体棱面的描边点缀）。</summary>
    internal static void Ridge(SolidMesh m, Vector3 p0, Vector3 p1, float w, Vector3 viewFwd, float a)
    {
      if (m == null || a <= 0.003f || w <= 0f) return;
      var s = Vector3.Cross(p1 - p0, viewFwd);
      float sm = s.magnitude;
      if (sm < 1e-6f) return;
      s *= w * 0.5f / sm;
      var c = C(1f, a);
      m.Tri(p0 - s, p0 + s, p1 + s, 0f, 1f, 1f, c, c, c);
      m.Tri(p0 - s, p1 + s, p1 - s, 0f, 1f, 0f, c, c, c);
    }

    /// <summary>
    /// 多棱晶体（双锥柱）：底环 0.72·rad → 腰环 rad（0.58 h）→ 尖顶 h，底部短尖 0.16 h。
    /// sides 面数（3 = 三棱晶壁、4 = 菱晶），hint 为任意参考横向（内部正交化）。
    /// </summary>
    internal static void Crystal(SolidMesh body, SolidMesh glow, SolidMesh edge, Vector3 b, Vector3 ax, Vector3 hint,
      float rad, float h, int sides, float phase, float a, float glowA, float ridgeW, Vector3 viewFwd)
    {
      if (h < 1e-3f || rad < 1e-4f || (a <= 0.003f && glowA <= 0.003f)) return;
      var t1 = hint - ax * Vector3.Dot(hint, ax);
      if (t1.sqrMagnitude < 1e-6f) t1 = Vector3.Cross(ax, Mathf.Abs(ax.y) < 0.9f ? Vector3.up : Vector3.forward);
      t1.Normalize();
      var t2 = Vector3.Cross(ax, t1);
      var apex = b + ax * h;
      var tail = b - ax * (h * 0.16f);
      var mid = b + ax * (h * 0.58f);
      float step = TwoPi / sides;
      for (int i = 0; i < sides; i++)
      {
        float a0 = phase + i * step, a1 = a0 + step;
        var d0 = t1 * Mathf.Cos(a0) + t2 * Mathf.Sin(a0);
        var d1 = t1 * Mathf.Cos(a1) + t2 * Mathf.Sin(a1);
        var lo0 = b + d0 * (rad * 0.72f);
        var lo1 = b + d1 * (rad * 0.72f);
        var hi0 = mid + d0 * rad;
        var hi1 = mid + d1 * rad;
        Facet(body, glow, lo0, lo1, hi1, a, glowA);
        Facet(body, glow, lo0, hi1, hi0, a, glowA);
        Facet(body, glow, hi0, hi1, apex, a, glowA);
        Facet(body, glow, tail, lo1, lo0, a, glowA);
        if (edge != null && ridgeW > 0f)
        {
          Ridge(edge, hi0, apex, ridgeW, viewFwd, a);
          Ridge(edge, lo0, hi0, ridgeW * 0.6f, viewFwd, 0.7f * a);
        }
      }
    }

    /// <summary>实心八面晶（核心晶 / 精神晶屑），stretch 为竖向拉长。</summary>
    internal static void Octa(SolidMesh body, SolidMesh glow, Vector3 c, float r, float stretch, Quaternion q, float a, float glowA)
    {
      if (r < 1e-4f) return;
      var px = q * new Vector3(r, 0f, 0f);
      var py = q * new Vector3(0f, r * stretch, 0f);
      var pz = q * new Vector3(0f, 0f, r);
      Vector3 x0 = c + px, x1 = c - px, y0 = c + py, y1 = c - py, z0 = c + pz, z1 = c - pz;
      Facet(body, glow, y0, x0, z0, a, glowA);
      Facet(body, glow, y0, z0, x1, a, glowA);
      Facet(body, glow, y0, x1, z1, a, glowA);
      Facet(body, glow, y0, z1, x0, a, glowA);
      Facet(body, glow, y1, z0, x0, a, glowA);
      Facet(body, glow, y1, x1, z0, a, glowA);
      Facet(body, glow, y1, z1, x1, a, glowA);
      Facet(body, glow, y1, x0, z1, a, glowA);
    }

    /// <summary>实体碎晶：拉长四面体（瓦解 / 裂隙迸射）。</summary>
    internal static void Shard(SolidMesh body, SolidMesh glow, Vector3 c, float r, Quaternion q, float a, float glowA)
    {
      if (r < 1e-4f) return;
      var p0 = c + q * new Vector3(1.8f * r, 0f, 0f);
      var p1 = c + q * new Vector3(-0.9f * r, 0.9f * r, 0f);
      var p2 = c + q * new Vector3(-0.9f * r, -0.5f * r, 0.8f * r);
      var p3 = c + q * new Vector3(-0.9f * r, -0.5f * r, -0.8f * r);
      Facet(body, glow, p0, p1, p2, a, glowA);
      Facet(body, glow, p0, p2, p3, a, glowA);
      Facet(body, glow, p0, p3, p1, a, glowA);
      Facet(body, glow, p1, p3, p2, a, glowA);
    }

    /// <summary>实心环带（e1/e2 张成平面；squash 压扁 e2 向；jag 奇偶锯齿；soft = 内外缘柔边）。</summary>
    internal static void Annulus(SolidMesh m, Vector3 c, Vector3 e1, Vector3 e2, float r0, float r1, int segs,
      float phase, float jag, float squash, float a, bool soft)
    {
      if (m == null || a <= 0.003f || r1 <= r0) return;
      var col = C(1f, a);
      float vi = soft ? 0f : 0.5f, vo = soft ? 1f : 0.5f;
      float step = TwoPi / segs;
      for (int i = 0; i < segs; i++)
      {
        int j = i + 1;
        float a0 = phase + i * step, a1 = phase + j * step;
        float k0 = 1f + jag * ((i & 1) == 0 ? 1f : -0.6f);
        float k1 = 1f + jag * ((j & 1) == 0 ? 1f : -0.6f);
        var d0 = e1 * Mathf.Cos(a0) + e2 * (Mathf.Sin(a0) * squash);
        var d1 = e1 * Mathf.Cos(a1) + e2 * (Mathf.Sin(a1) * squash);
        var in0 = c + d0 * (r0 * k0); var in1 = c + d1 * (r0 * k1);
        var out0 = c + d0 * (r1 * k0); var out1 = c + d1 * (r1 * k1);
        m.Tri(in0, out0, out1, vi, vo, vo, col, col, col);
        m.Tri(in0, out1, in1, vi, vo, vi, col, col, col);
      }
    }

    /// <summary>实心盘（三角扇）：rimV = 0 → 外缘柔化成光晕，0.5 → 硬边实体面。</summary>
    internal static void Fan(SolidMesh m, Vector3 c, Vector3 e1, Vector3 e2, float r, int segs, float phase,
      float jag, float squash, float a, float rimV)
    {
      if (m == null || a <= 0.003f || r <= 0f) return;
      var col = C(1f, a);
      float step = TwoPi / segs;
      for (int i = 0; i < segs; i++)
      {
        int j = i + 1;
        float a0 = phase + i * step, a1 = phase + j * step;
        float k0 = 1f + jag * ((i & 1) == 0 ? 1f : -0.6f);
        float k1 = 1f + jag * ((j & 1) == 0 ? 1f : -0.6f);
        var p0 = c + (e1 * Mathf.Cos(a0) + e2 * (Mathf.Sin(a0) * squash)) * (r * k0);
        var p1 = c + (e1 * Mathf.Cos(a1) + e2 * (Mathf.Sin(a1) * squash)) * (r * k1);
        m.Tri(c, p0, p1, 0.5f, rimV, rimV, col, col, col);
      }
    }

    /// <summary>正对镜头的菱形星芒晶片。</summary>
    internal static void Diamond(SolidMesh m, Vector3 c, Vector3 e1, Vector3 e2, float w, float h, float a)
    {
      if (m == null || a <= 0.003f) return;
      var col = C(1f, a);
      Vector3 pl = c - e1 * w, pr = c + e1 * w, pt = c + e2 * h, pb = c - e2 * h;
      m.Tri(pl, pt, pr, 0f, 0.5f, 1f, col, col, col);
      m.Tri(pl, pr, pb, 0f, 1f, 0.5f, col, col, col);
    }

    /// <summary>
    /// 宽幅流体带：pts[0..n) 为中心线，nrm 为带面法线（贴地 Vector3.up / 立体 viewFwd）。
    /// taper = 根粗梢细（突触导管），否则两端尖、中段鼓胀（widthCurve 式）；swell/swellU = 沿带奔流的能量鼓包。
    /// </summary>
    internal static void Ribbon(SolidMesh m, Vector3[] pts, int n, Vector3 nrm, float w, bool taper, float a,
      float swell = 0f, float swellU = -1f)
    {
      if (m == null || n < 2 || a <= 0.003f) return;
      Vector3 pl = Vector3.zero, pr = Vector3.zero;
      Color32 pc = default(Color32);
      for (int i = 0; i < n; i++)
      {
        float u = i / (float)(n - 1);
        var t = pts[Mathf.Min(i + 1, n - 1)] - pts[Mathf.Max(i - 1, 0)];
        var s = Vector3.Cross(t, nrm);
        float sm = s.magnitude;
        float prof = taper ? 1f - 0.78f * u : Mathf.Sin(Mathf.PI * Mathf.Lerp(0.06f, 0.94f, u));
        if (swellU >= 0f) { float d = (u - swellU) / 0.09f; prof *= 1f + swell * Mathf.Exp(-d * d); }
        s = sm > 1e-6f ? s * (w * 0.5f * prof / sm) : Vector3.zero;
        var l = pts[i] - s;
        var r = pts[i] + s;
        var col = C(1f, a * (taper ? 1f - 0.55f * u * u : Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(prof))));
        if (i > 0)
        {
          m.Tri(pl, pr, r, 0f, 1f, 1f, pc, pc, col);
          m.Tri(pl, r, l, 0f, 1f, 0f, pc, col, col);
        }
        pl = l; pr = r; pc = col;
      }
    }

    /// <summary>
    /// 前文明黑冠（文明的存续）：14 段实心多棱黑曜冠箍（外壁 / 顶面 / 内壁 / 底面）+ 7 大 7 小四棱黑曜晶尖
    /// + 白金冠缘与晶脊 + 悬浮白金八面核心晶。q = 冠体姿态（自旋 + 进动倾斜）；burst = 瓦解外抛量（世界单位）。
    /// </summary>
    internal static void Crown(SolidMesh body, SolidMesh glow, SolidMesh edge, Vector3 c, float r, float spike,
      Quaternion q, float a, float glowA, float burst, Vector3 viewFwd, float gemSpin)
    {
      const int Seg = 14;
      float rin = r * 0.84f, yb = -r * 0.11f, yt = r * 0.13f;
      float step = TwoPi / Seg;
      var up = q * Vector3.up;
      float lean = 0.30f + 1.5f * burst / Mathf.Max(r, 0.01f);
      for (int i = 0; i < Seg; i++)
      {
        float a0 = i * step, a1 = a0 + step, am = a0 + step * 0.5f;
        var o0 = q * new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
        var o1 = q * new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
        var om = q * new Vector3(Mathf.Cos(am), 0f, Mathf.Sin(am));
        var tg = q * new Vector3(-Mathf.Sin(am), 0f, Mathf.Cos(am));
        var k = c + om * burst + up * (burst * 0.3f);
        var ot0 = k + o0 * r + up * yt; var ot1 = k + o1 * r + up * yt;
        var ob0 = k + o0 * r + up * yb; var ob1 = k + o1 * r + up * yb;
        var it0 = k + o0 * rin + up * yt; var it1 = k + o1 * rin + up * yt;
        var ib0 = k + o0 * rin + up * yb; var ib1 = k + o1 * rin + up * yb;
        Facet(body, glow, ob0, ob1, ot1, a, glowA);          // 外壁
        Facet(body, glow, ob0, ot1, ot0, a, glowA);
        Facet(body, glow, ot0, ot1, it1, a, glowA * 0.6f);   // 顶面
        Facet(body, glow, ot0, it1, it0, a, glowA * 0.6f);
        Facet(body, null, it0, it1, ib1, a, 0f);             // 内壁
        Facet(body, null, it0, ib1, ib0, a, 0f);
        Facet(body, null, ib0, ib1, ob1, a, 0f);             // 底面
        Facet(body, null, ib0, ob1, ob0, a, 0f);
        Ridge(edge, ot0, ot1, r * 0.07f, viewFwd, a);        // 白金冠缘
        Ridge(edge, ob0, ob1, r * 0.04f, viewFwd, 0.6f * a);

        bool big = (i & 1) == 0;
        float sh = spike * (big ? 1f : 0.55f);
        var ax = (up + om * lean).normalized;
        var bp = k + om * (r * 0.92f + burst * 0.8f) + up * (yt * 0.5f);
        Crystal(body, glow, edge, bp, ax, tg, r * (big ? 0.17f : 0.11f), sh, 4, 0.785f, a, glowA, r * 0.05f, viewFwd);
      }
      Octa(edge, glow, c + up * (Mathf.Max(spike, r * 0.3f) * 0.55f), r * 0.2f, 1.6f,
        q * Quaternion.Euler(0f, gemSpin, 0f), a, glowA);
    }
  }

  /// <summary>
  /// 巴别塔恶灵形态开启瞬间动画（SpecterActivateAnim，~0.8s）——实体几何版：
  ///   · 胸口：正对镜头的实心冲击波板 + 白金闪核 + 8 根四棱黑曜晶刺猛烈放射激荡；
  ///   · 脚下：宽幅贴地突触导管（漆黑底 + 形态色奔流鼓胀）+ 漆黑厚壁冲击环外扩；
  ///   · 头顶：锯齿虚空裂隙实心撕开（漆黑裂体 + 形态色裂内辉光 + 白金裂缘），迸出翻滚黑曜碎晶；
  ///     前文明黑冠（实心冠箍 + 晶尖 + 核心晶）自裂隙中由 1.6 倍幻影收缩凝聚，末段淡出交接 SpecterFormAnim。
  /// </summary>
  public class SpecterActivateAnim : MonoBehaviour
  {
    private const float Dur = 0.80f;
    private const int ChestN = 8, GroundN = 8, GroundPts = 8, DebrisN = 10;
    private static readonly Color Ob = SpecterFormFx.OriginiumObsidian, Au = SpecterFormFx.OriginiumGold,
      Vi = SpecterFormFx.NeuralViolet, Pt = SpecterFormFx.CrownPlatinum;

    private float _t, _h = 2f, _r = 0.8f;
    private UnitEntityData _caster;
    private bool _isAgile;
    private SolidMesh _body, _glow, _edge;
    private readonly Vector3[] _pts = new Vector3[GroundPts];
    private readonly float[] _chestAng = new float[ChestN];
    private readonly float[] _chestLen = new float[ChestN];
    private readonly float[] _gAng = new float[GroundN];
    private readonly float[] _gLen = new float[GroundN];
    private readonly float[] _gJit = new float[GroundN * GroundPts];
    private readonly Vector3[] _debDir = new Vector3[DebrisN];
    private readonly Vector3[] _debAxis = new Vector3[DebrisN];
    private readonly float[] _debSpin = new float[DebrisN];

    internal bool Init(UnitEntityData caster, bool isAgile)
    {
      if (caster?.View == null) return false;
      var mOb = Mat(Ob); var mAcc = Mat(isAgile ? Au : Vi); var mPt = Mat(Pt);
      if (mOb == null || mAcc == null || mPt == null)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 巴别塔恶灵开启特效放弃：StreakMaterialPlain 无可用材质。");
        return false;
      }
      _caster = caster;
      _isAgile = isAgile;
      _h = ModelHeight(caster);
      _r = Mathf.Clamp(caster.View.Corpulence * 1.6f, 0.7f, 3.0f);
      transform.SetPositionAndRotation(caster.Position, Quaternion.identity);
      _body = new SolidMesh(transform, "RD_SP_ActBody", mOb, 900);
      _glow = new SolidMesh(transform, "RD_SP_ActGlow", mAcc, 1100);
      _edge = new SolidMesh(transform, "RD_SP_ActEdge", mPt, 800);

      for (int i = 0; i < ChestN; i++)
      {
        _chestAng[i] = (i + UnityEngine.Random.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / ChestN;
        _chestLen[i] = UnityEngine.Random.Range(0.55f, 0.95f);
      }
      for (int i = 0; i < GroundN; i++)
      {
        _gAng[i] = (i + UnityEngine.Random.Range(-0.35f, 0.35f)) * Mathf.PI * 2f / GroundN;
        _gLen[i] = UnityEngine.Random.Range(1.5f, 2.3f);
        for (int p = 0; p < GroundPts; p++)
          _gJit[i * GroundPts + p] = p == 0 ? 0f : UnityEngine.Random.Range(-0.15f, 0.15f);
      }
      for (int i = 0; i < DebrisN; i++)
      {
        var d = UnityEngine.Random.onUnitSphere;
        d.y = Mathf.Abs(d.y) * 0.6f + 0.15f;
        _debDir[i] = d.normalized * UnityEngine.Random.Range(0.7f, 1.5f);
        _debAxis[i] = UnityEngine.Random.onUnitSphere;
        _debSpin[i] = UnityEngine.Random.Range(240f, 720f);
      }
      return true;
    }

    private void Update()
    {
      _t += Time.deltaTime;
      if (_t >= Dur || _caster == null || _caster.View == null)
      {
        Destroy(gameObject);
        return;
      }
      try
      {
        transform.SetPositionAndRotation(_caster.Position, Quaternion.identity);
        CamBasis(out var right, out var up);
        var fwd = SpecterGeo.ViewFwd(right, up);
        var chest = Vector3.up * (_h * 0.55f);
        float jitHz = _isAgile ? 70f : 38f;   // 敏捷激荡更急促
        _body.Begin(); _glow.Begin(); _edge.Begin();

        // ① 胸口：实心冲击波板 + 白金闪核 + 四棱黑曜晶刺放射
        float burst = SpecterGeo.Out3(_t / 0.18f);
        float burstA = Mathf.Clamp01(1f - (_t - 0.15f) / 0.55f);
        float fk = Mathf.Clamp01(_t / 0.30f);
        float pr = Mathf.Lerp(0.10f, _r * 1.1f, SpecterGeo.Out3(fk));
        float pw = Mathf.Lerp(0.30f, 0.06f, fk);
        SpecterGeo.Annulus(_glow, chest, right, up, Mathf.Max(0f, pr - pw), pr + pw * 0.5f, 40, 0f, 0.06f, 1f, 0.9f * (1f - fk), true);
        SpecterGeo.Fan(_edge, chest, right, up, Mathf.Lerp(0.45f, 0.08f, fk), 24, 0f, 0f, 1f, 1f - fk, 0f);
        for (int i = 0; i < ChestN; i++)
        {
          float a = _chestAng[i];
          var d = right * Mathf.Cos(a) + up * Mathf.Sin(a);
          float len = _chestLen[i] * _r * 1.3f * burst * (1f + 0.1f * Mathf.Sin(_t * jitHz + i * 1.7f));
          SpecterGeo.Crystal(_body, _glow, _edge, chest + d * 0.12f, d, fwd, 0.09f + 0.05f * _chestLen[i], len,
            4, 0.785f, burstA, 0.55f * burstA, 0.035f, fwd);
        }

        // ② 脚下：宽幅贴地突触导管 + 漆黑厚壁冲击环
        float gGrow = SpecterGeo.Out3((_t - 0.03f) / 0.28f);
        float gA = Mathf.Clamp01(1f - (_t - 0.35f) / 0.45f);
        for (int i = 0; i < GroundN; i++)
        {
          float ca = Mathf.Cos(_gAng[i]), sa = Mathf.Sin(_gAng[i]);
          float len = _gLen[i] * _r * gGrow;
          for (int p = 0; p < GroundPts; p++)
          {
            float u = p / (float)(GroundPts - 1);
            float j = _gJit[i * GroundPts + p] * len;
            _pts[p] = new Vector3(ca * len * u - sa * j, 0.05f, sa * len * u + ca * j);
          }
          SpecterGeo.Ribbon(_body, _pts, GroundPts, Vector3.up, 0.50f, true, 0.9f * gA);
          SpecterGeo.Ribbon(_glow, _pts, GroundPts, Vector3.up, 0.28f, true,
            gA * (0.7f + 0.3f * Mathf.Sin(_t * jitHz * 0.6f + i * 2.3f)), 1.2f, gGrow * 0.9f);
        }
        float sk = Mathf.Clamp01(_t / 0.45f);
        float sr = Mathf.Lerp(_r * 0.3f, _r * 2.4f, SpecterGeo.Out3(sk));
        float sw = Mathf.Lerp(0.55f, 0.12f, sk);
        var g = Vector3.up * 0.04f;
        SpecterGeo.Annulus(_body, g, Vector3.right, Vector3.forward, Mathf.Max(0f, sr - sw * 1.6f), sr, 48, 0f, 0.05f, 1f, 0.9f * (1f - sk), false);
        SpecterGeo.Annulus(_glow, g + Vector3.up * 0.005f, Vector3.right, Vector3.forward, Mathf.Max(0f, sr - sw), sr + sw * 0.4f,
          48, 0f, 0.05f, 1f, (1f - sk) * (1f - sk), true);

        // ③ 头顶虚空裂隙：锯齿实心裂体撕开（0.45 s 后合拢）+ 迸射翻滚碎晶
        var top = Vector3.up * (_h + 0.5f);
        float open = SpecterGeo.Out3((_t - 0.05f) / 0.25f);
        float close = Mathf.Clamp01((_t - 0.45f) / 0.30f);
        float riftA = open * (1f - close);
        float rw = Mathf.Clamp(_r * 0.75f, 0.55f, 1.3f) * (0.35f + 0.65f * open);
        float sq = 0.38f * open * (1f - close) + 0.02f;
        float jag = 0.22f + 0.05f * Mathf.Sin(_t * 40f);
        SpecterGeo.Fan(_body, top, right, up, rw, 28, 0f, jag, sq, riftA, 0.5f);
        SpecterGeo.Fan(_glow, top, right, up, rw * 0.8f, 28, 0f, jag, sq * 0.7f, 0.8f * riftA, 0f);
        SpecterGeo.Annulus(_edge, top, right, up, rw, rw * 1.12f + 0.05f, 28, 0f, jag, sq, riftA, true);
        if (_t > 0.08f)
        {
          float dk = Mathf.Clamp01((_t - 0.08f) / 0.6f);
          for (int i = 0; i < DebrisN; i++)
          {
            var pos = top + _debDir[i] * (1.1f * SpecterGeo.Out3(dk)) + Vector3.down * (0.6f * dk * dk);
            var rot = Quaternion.AngleAxis(_t * _debSpin[i], _debAxis[i]);
            SpecterGeo.Shard(_body, _glow, pos, 0.07f + 0.03f * (i % 3), rot, 1f - dk, 0.6f * (1f - dk));
          }
        }

        // ④ 黑冠显现：实心冠体自裂隙中由 1.6 倍幻影收缩凝聚，末段淡出交接 SpecterFormAnim
        float m = SpecterGeo.Out3((_t - 0.20f) / 0.40f);
        float cr = Mathf.Clamp(_r * 0.42f, 0.32f, 0.8f) * Mathf.Lerp(1.6f, 1f, m);
        float crownA = m * Mathf.Clamp01(1f - (_t - 0.62f) / 0.18f);
        var q = Quaternion.Euler(0f, _t * 140f, 0f);
        SpecterGeo.Crown(_body, _glow, _edge, top, cr, cr * 1.05f * m, q, crownA, 0.5f * crownA, 0f, fwd, _t * 200f);

        _body.End(); _glow.End(); _edge.End();
      }
      catch (Exception ex)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 巴别塔恶灵开启特效更新失败（已移除）：\n" + ex);
        Destroy(gameObject);
      }
    }

    private void OnDestroy()
    {
      _body?.Dispose(); _glow?.Dispose(); _edge?.Dispose();
    }
  }

  /// <summary>
  /// 巴别塔恶灵常驻跟随动画（SpecterFormAnim，继承 MarkFxBase）——实体几何版：
  ///   · 头顶：前文明黑冠（文明的存续）= 实心多棱黑曜冠箍 + 7 大 7 小四棱黑曜晶尖 + 白金冠缘与晶脊
  ///     + 悬浮白金八面核心晶；倾斜进动 + 自旋 + 悬浮起伏；冠缘垂落实体精神晶屑与宽幅流光；
  ///   · 脚下：漆黑源石地壳盘 + 形态色柔边光环 + 7 条宽幅贴地突触导管（漆黑底 0.46 + 形态色奔流 0.26，
  ///     能量鼓包沿导管奔流 + 白金脉冲亮核）；
  ///     - 敏捷（IsAgile = true）：3 束宽幅（0.40）炽烈源石金螺旋能量洪流 + 白金流芯，能量向外迅捷奔流；
  ///     - 坚韧（IsAgile = false）：6 簇实体三棱精神紫晶壁（主晶 + 两枚外倾伴生晶）+ 向内坍缩的实心重力波环；
  ///   · 入场 EaseOut3，结束/驱散由 MarkFxBase.Master 0.3 s 平滑淡出。
  /// </summary>
  public class SpecterFormAnim : MarkFxBase
  {
    public bool IsAgile;

    private const int DripN = 12, DripPts = 5, SynN = 7, SynPts = 10, StreamN = 3, StreamPts = 24, SpireN = 6, RippleN = 3;
    private static readonly Color Ob = SpecterFormFx.OriginiumObsidian, Au = SpecterFormFx.OriginiumGold,
      Vi = SpecterFormFx.NeuralViolet, Pt = SpecterFormFx.CrownPlatinum;

    private SolidMesh _body, _glow, _edge;
    private readonly Vector3[] _pts = new Vector3[StreamPts];   // 共用中心线缓冲（≥ 各带最大点数）
    private readonly Vector3[] _pulsePts = new Vector3[3];
    private readonly float[] _dripAng = new float[DripN];
    private readonly float[] _dripPhase = new float[DripN];
    private readonly float[] _dripSpeed = new float[DripN];
    private readonly float[] _synAng = new float[SynN];
    private readonly float[] _synLen = new float[SynN];
    private readonly float[] _synJit = new float[SynN * SynPts];
    private readonly float[] _pulsePhase = new float[SynN];
    private readonly float[] _pulseSpeed = new float[SynN];
    private readonly float[] _spireAng = new float[SpireN];
    private readonly float[] _spireScale = new float[SpireN];

    protected override bool Build(Buff buff)
    {
      var mOb = Mat(Ob); var mAcc = Mat(IsAgile ? Au : Vi); var mPt = Mat(Pt);
      if (mOb == null || mAcc == null || mPt == null)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 巴别塔恶灵·形态常驻放弃：StreakMaterialPlain 无可用材质。");
        return false;
      }
      _body = new SolidMesh(transform, "RD_SP_FormBody", mOb, 900);
      _glow = new SolidMesh(transform, "RD_SP_FormGlow", mAcc, 1500);
      _edge = new SolidMesh(transform, "RD_SP_FormEdge", mPt, 900);
      for (int i = 0; i < DripN; i++)
      {
        _dripAng[i] = i * Mathf.PI * 2f / DripN + UnityEngine.Random.Range(-0.25f, 0.25f);
        _dripPhase[i] = UnityEngine.Random.value;
        _dripSpeed[i] = UnityEngine.Random.Range(0.55f, 0.95f);
      }
      float speed = IsAgile ? 1.7f : 0.55f;
      for (int i = 0; i < SynN; i++)
      {
        _synAng[i] = (i + UnityEngine.Random.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / SynN;
        _synLen[i] = UnityEngine.Random.Range(1.3f, 1.9f);
        for (int p = 0; p < SynPts; p++)
          _synJit[i * SynPts + p] = p == 0 ? 0f : UnityEngine.Random.Range(-0.16f, 0.16f);
        _pulsePhase[i] = UnityEngine.Random.value;
        _pulseSpeed[i] = speed * UnityEngine.Random.Range(0.8f, 1.25f);
      }
      for (int i = 0; i < SpireN; i++)
      {
        _spireAng[i] = (i + UnityEngine.Random.Range(-0.15f, 0.15f)) * Mathf.PI * 2f / SpireN;
        _spireScale[i] = UnityEngine.Random.Range(0.85f, 1.15f);
      }
      return true;
    }

    /// <summary>当前 _pts 突触中心线上 u∈[0,1] 处的点（脉冲亮核定位）。</summary>
    private Vector3 SynAt(float u)
    {
      float f = Mathf.Clamp01(u) * (SynPts - 1);
      int p = Mathf.Min((int)f, SynPts - 2);
      return Vector3.Lerp(_pts[p], _pts[p + 1], f - p) + Vector3.up * 0.01f;
    }

    protected override void Tick(float dt)
    {
      transform.SetPositionAndRotation(Unit.Position, Quaternion.identity);
      CamBasis(out var right, out var up);
      var fwd = SpecterGeo.ViewFwd(right, up);
      float ink = 1f - Mathf.Clamp01(T / 0.35f);
      float appear = (1f - ink * ink * ink) * Master;               // EaseOut3 入场 × 0.3 s 淡出
      float R = Radius, H = Height;
      float beat = 0.75f + 0.25f * Mathf.Sin(T * 2.4f);
      _body.Begin(); _glow.Begin(); _edge.Begin();

      // ── 头顶前文明黑冠：实心冠体，倾斜进动 + 自旋 + 悬浮起伏 ──
      float cr = Mathf.Clamp(R * 0.42f, 0.32f, 0.8f) * Mathf.Lerp(0.6f, 1f, appear);
      var cc = Vector3.up * (H + 0.5f + 0.06f * Mathf.Sin(T * 1.6f));
      var q = Quaternion.Euler(8f * Mathf.Sin(T * 0.7f), T * 52f, 8f * Mathf.Cos(T * 0.7f));
      SpecterGeo.Crown(_body, _glow, _edge, cc, cr, cr * 1.05f, q, appear, 0.42f * beat * appear, 0f, fwd, T * 90f);

      // ── 垂落精神晶屑：实体八面晶片加速下坠，拖曳宽幅流光 ──
      float dropLen = H * 0.7f;
      for (int i = 0; i < DripN; i++)
      {
        float k = Mathf.Repeat(T * _dripSpeed[i] + _dripPhase[i], 1f);
        float ang = _dripAng[i] + T * 0.9f;
        float fall = dropLen * (0.25f * k + 0.75f * k * k);
        var head = cc + new Vector3(Mathf.Cos(ang) * cr * 0.95f, -(fall + cr * 0.12f), Mathf.Sin(ang) * cr * 0.95f);
        float fa = Mathf.Sin(k * Mathf.PI) * appear;
        float tail = 0.18f + 0.30f * k;
        for (int p = 0; p < DripPts; p++)
          _pts[p] = head + Vector3.up * (tail * (1f - p / (float)(DripPts - 1)));
        SpecterGeo.Ribbon(_glow, _pts, DripPts, fwd, 0.13f, false, 0.8f * fa);
        var fq = Quaternion.Euler(0f, T * 180f + i * 30f, 0f);
        if ((i & 1) == 0) SpecterGeo.Octa(_edge, _glow, head, 0.045f, 1.8f, fq, fa, 0.5f * fa);
        else SpecterGeo.Octa(_glow, null, head, 0.045f, 1.8f, fq, fa, 0f);
      }

      // ── 脚下：漆黑源石地壳盘 + 形态色柔边光环 ──
      float grow = SpecterGeo.Out3(T / 0.45f);
      float gr = R * grow;
      var g = Vector3.up * 0.03f;
      SpecterGeo.Annulus(_body, g, Vector3.right, Vector3.forward, gr * 0.30f, gr * 1.02f, 48, T * 0.15f, 0.04f, 1f, 0.85f * appear, false);
      SpecterGeo.Annulus(_glow, g + Vector3.up * 0.005f, Vector3.right, Vector3.forward, gr * 0.92f, gr * 1.12f, 48, T * 0.15f, 0.04f, 1f,
        (0.5f + 0.3f * beat) * appear, true);

      // ── 宽幅源石神经突触导管：漆黑底 + 形态色奔流（能量鼓包沿导管奔流）+ 白金脉冲亮核 ──
      float throbHz = IsAgile ? 7f : 2.5f;
      for (int i = 0; i < SynN; i++)
      {
        float ca = Mathf.Cos(_synAng[i]), sa = Mathf.Sin(_synAng[i]);
        float len = R * _synLen[i] * grow * (0.94f + 0.06f * Mathf.Sin(T * throbHz * 0.8f + i));
        for (int p = 0; p < SynPts; p++)
        {
          float u = p / (float)(SynPts - 1);
          float j = _synJit[i * SynPts + p] * len;
          _pts[p] = new Vector3(ca * len * u - sa * j, 0.045f, sa * len * u + ca * j);
        }
        float pu = Mathf.Repeat(T * _pulseSpeed[i] + _pulsePhase[i], 1f);
        if (!IsAgile) pu = 1f - pu;   // 坚韧：能量向内缓沉回流
        float flick = 0.6f + 0.4f * Mathf.Sin(T * throbHz - i * 1.1f);
        SpecterGeo.Ribbon(_body, _pts, SynPts, Vector3.up, 0.46f, true, 0.9f * appear);
        SpecterGeo.Ribbon(_glow, _pts, SynPts, Vector3.up, 0.26f, true, flick * appear, 1.4f, pu);
        _pulsePts[0] = SynAt(pu - 0.06f);
        _pulsePts[1] = SynAt(pu);
        _pulsePts[2] = SynAt(pu + 0.06f);
        SpecterGeo.Ribbon(_edge, _pulsePts, 3, Vector3.up, 0.30f, false, 0.9f * Mathf.Sin(pu * Mathf.PI) * appear);
      }

      if (IsAgile)
      {
        // ── 敏捷：环身螺旋上冲的宽幅炽烈源石金能量洪流 + 白金流芯 ──
        for (int s = 0; s < StreamN; s++)
        {
          float ph = s * Mathf.PI * 2f / StreamN + T * 4.2f;
          for (int p = 0; p < StreamPts; p++)
          {
            float u = p / (float)(StreamPts - 1);
            float ang = ph + u * 3.8f;
            float rr = R * (0.80f - 0.30f * u) + 0.05f * Mathf.Sin(T * 11f + u * 12f + s);
            _pts[p] = new Vector3(Mathf.Cos(ang) * rr, u * H * 1.05f, Mathf.Sin(ang) * rr);
          }
          float sa = (0.65f + 0.35f * Mathf.Sin(T * 9f + s * 2.1f)) * appear;
          float su = Mathf.Repeat(T * 1.6f + s / (float)StreamN, 1f);
          SpecterGeo.Ribbon(_glow, _pts, StreamPts, fwd, 0.40f, false, 0.85f * sa, 0.8f, su);
          SpecterGeo.Ribbon(_edge, _pts, StreamPts, fwd, 0.12f, false, 0.7f * sa, 1.2f, su);
        }
      }
      else
      {
        // ── 坚韧：环身矗立的实体三棱精神紫晶壁簇 + 向内坍缩的实心重力波环 ──
        float rad = Mathf.Clamp(R * 0.17f, 0.12f, 0.32f);
        for (int i = 0; i < SpireN; i++)
        {
          float ang = _spireAng[i] + T * 0.22f;
          var od = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
          var tg = new Vector3(-od.z, 0f, od.x);
          float rise = SpecterGeo.Out3((T - i * 0.05f) / 0.5f);
          float h = H * 0.55f * _spireScale[i] * (1f + 0.06f * Mathf.Sin(T * 1.3f + i * 1.7f)) * rise;
          var b = od * (R * 1.1f) + Vector3.up * 0.02f;
          float shine = (0.45f + 0.25f * Mathf.Sin(T * 2f + i * 1.3f)) * appear;
          SpecterGeo.Crystal(_body, _glow, _edge, b, (Vector3.up + od * 0.12f).normalized, tg, rad, h, 3, 0.5f,
            appear, shine, 0.05f, fwd);
          SpecterGeo.Crystal(_body, _glow, _edge, b + tg * (rad * 1.3f), (Vector3.up + od * 0.45f + tg * 0.25f).normalized, od,
            rad * 0.6f, h * 0.5f, 3, 0f, appear, shine, 0.035f, fwd);
          SpecterGeo.Crystal(_body, _glow, _edge, b - tg * (rad * 1.3f), (Vector3.up + od * 0.45f - tg * 0.25f).normalized, od,
            rad * 0.6f, h * 0.42f, 3, 0.4f, appear, shine, 0.035f, fwd);
        }
        for (int i = 0; i < RippleN; i++)
        {
          float k = Mathf.Repeat(T * 0.45f + i / (float)RippleN, 1f);
          float rr = Mathf.Lerp(R * 1.8f, R * 0.25f, k * k);
          float bw = Mathf.Lerp(0.42f, 0.12f, k);
          SpecterGeo.Annulus(_glow, Vector3.up * (0.035f + 0.004f * i), Vector3.right, Vector3.forward,
            rr - bw * 0.5f, rr + bw * 0.5f, 48, 0f, 0f, 1f, 0.7f * Mathf.Sin(k * Mathf.PI) * appear, true);
        }
      }

      _body.End(); _glow.End(); _edge.End();
    }

    protected override void OnDestroy()
    {
      _body?.Dispose(); _glow?.Dispose(); _edge?.Dispose();
      base.OnDestroy();
    }
  }

  /// <summary>
  /// 巴别塔恶灵形态结束/关闭瞬间动画（SpecterDeactivateAnim，~0.8s）——实体几何版：
  ///   · 实心黑冠震颤、冠箍碎段外抛、晶尖外倾塌陷、加速自旋后消散；
  ///   · 迸射翻滚的黑曜四面体实体碎晶（源石金 / 精神紫晶光，初速 + 阻尼 + 重力）与白金菱形星芒光屑；
  ///   · 宽幅贴地突触导管 EaseInQuad 回旋收缩、能量鼓包回流脚心；胸口实心精神紫内收环坍缩 → 白金八面核心一闪敛入体内。
  /// </summary>
  public class SpecterDeactivateAnim : MonoBehaviour
  {
    private const float Dur = 0.80f;
    private const int ShardN = 24, DustN = 28, SynN = 6, SynPts = 7;
    private static readonly Color Ob = SpecterFormFx.OriginiumObsidian, Au = SpecterFormFx.OriginiumGold,
      Vi = SpecterFormFx.NeuralViolet, Pt = SpecterFormFx.CrownPlatinum;

    private float _t, _h = 2f, _r = 0.8f, _cr = 0.5f;
    private UnitEntityData _caster;
    private SolidMesh _body, _violet, _gold, _edge;
    private readonly Vector3[] _pts = new Vector3[SynPts];
    private readonly Vector3[] _shardPos = new Vector3[ShardN];
    private readonly Vector3[] _shardVel = new Vector3[ShardN];
    private readonly Vector3[] _shardAxis = new Vector3[ShardN];
    private readonly float[] _shardSpin = new float[ShardN];
    private readonly float[] _shardSize = new float[ShardN];
    private readonly Vector3[] _dustPos = new Vector3[DustN];
    private readonly Vector3[] _dustVel = new Vector3[DustN];
    private readonly float[] _synAng = new float[SynN];
    private readonly float[] _synLen = new float[SynN];
    private readonly float[] _synJit = new float[SynN * SynPts];

    internal bool Init(UnitEntityData caster)
    {
      if (caster?.View == null) return false;
      var mOb = Mat(Ob); var mAu = Mat(Au); var mVi = Mat(Vi); var mPt = Mat(Pt);
      if (mOb == null || mAu == null || mVi == null || mPt == null)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 巴别塔恶灵解除特效放弃：StreakMaterialPlain 无可用材质。");
        return false;
      }
      _caster = caster;
      _h = ModelHeight(caster);
      _r = Mathf.Clamp(caster.View.Corpulence * 1.6f, 0.7f, 3.0f);
      _cr = Mathf.Clamp(_r * 0.42f, 0.32f, 0.8f);
      transform.SetPositionAndRotation(caster.Position, Quaternion.identity);
      _body = new SolidMesh(transform, "RD_SP_DeBody", mOb, 700);
      _violet = new SolidMesh(transform, "RD_SP_DeViolet", mVi, 700);
      _gold = new SolidMesh(transform, "RD_SP_DeGold", mAu, 300);
      _edge = new SolidMesh(transform, "RD_SP_DeEdge", mPt, 600);

      var crownC = Vector3.up * (_h + 0.5f);
      for (int i = 0; i < ShardN; i++)
      {
        float a = i * Mathf.PI * 2f / ShardN + UnityEngine.Random.Range(-0.15f, 0.15f);
        var od = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        _shardPos[i] = crownC + od * _cr + Vector3.up * UnityEngine.Random.Range(0f, _cr * 0.9f);
        _shardVel[i] = od * UnityEngine.Random.Range(1.0f, 2.4f) + Vector3.up * UnityEngine.Random.Range(0.4f, 1.6f);
        _shardAxis[i] = UnityEngine.Random.onUnitSphere;
        _shardSpin[i] = UnityEngine.Random.Range(300f, 900f);
        _shardSize[i] = _cr * UnityEngine.Random.Range(0.10f, 0.20f);
      }
      for (int i = 0; i < DustN; i++)
      {
        var d = UnityEngine.Random.onUnitSphere;
        _dustPos[i] = crownC + d * (_cr * 0.6f);
        _dustVel[i] = d * UnityEngine.Random.Range(0.6f, 2.2f) + Vector3.up * 0.4f;
      }
      for (int i = 0; i < SynN; i++)
      {
        _synAng[i] = (i + UnityEngine.Random.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / SynN;
        _synLen[i] = UnityEngine.Random.Range(1.3f, 1.9f);
        for (int p = 0; p < SynPts; p++)
          _synJit[i * SynPts + p] = p == 0 ? 0f : UnityEngine.Random.Range(-0.16f, 0.16f);
      }
      return true;
    }

    private void Update()
    {
      float dt = Time.deltaTime;
      _t += dt;
      if (_t >= Dur || _caster == null || _caster.View == null)
      {
        Destroy(gameObject);
        return;
      }
      try
      {
        transform.SetPositionAndRotation(_caster.Position, Quaternion.identity);
        CamBasis(out var right, out var up);
        var fwd = SpecterGeo.ViewFwd(right, up);
        float k = _t / Dur;
        var chest = Vector3.up * (_h * 0.55f);
        var crownC = Vector3.up * (_h + 0.5f);
        _body.Begin(); _violet.Begin(); _gold.Begin(); _edge.Begin();

        // ① 黑冠瓦解：0.3 s 内震颤、冠箍碎段外抛、晶尖外倾塌陷、加速自旋
        float sk = Mathf.Clamp01(_t / 0.30f);
        float crownA = 1f - sk;
        var q = Quaternion.Euler(6f * Mathf.Sin(_t * 50f) * crownA, (0.9f * _t + 6f * sk * sk) * Mathf.Rad2Deg, 0f);
        var shake = Vector3.right * (0.03f * Mathf.Sin(_t * 70f) * crownA);
        SpecterGeo.Crown(_body, _violet, _edge, crownC + shake, _cr * (1f + 0.25f * SpecterGeo.Out3(sk)),
          _cr * 1.05f * (1f - 0.7f * sk), q, crownA, 0.5f * crownA, 0.45f * _cr * sk, fwd, _t * 600f);

        // ② 实体碎晶：黑曜四面体外抛 + 阻尼 + 重力 + 翻滚（金 / 紫晶光交错）
        float drag = Mathf.Max(0f, 1f - 2.2f * dt);
        float shardA = Mathf.Clamp01(_t / 0.08f) * Mathf.Pow(1f - k, 1.4f);
        for (int i = 0; i < ShardN; i++)
        {
          var v = _shardVel[i] * drag;
          v.y -= 2.2f * dt;
          _shardVel[i] = v;
          _shardPos[i] += v * dt;
          var rot = Quaternion.AngleAxis(_t * _shardSpin[i], _shardAxis[i]);
          SpecterGeo.Shard(_body, (i & 1) == 0 ? _gold : _violet, _shardPos[i], _shardSize[i] * (1f - 0.4f * k),
            rot, shardA, 0.65f * shardA);
        }

        // ③ 星芒光屑：白金菱形晶片飘散闪烁
        for (int i = 0; i < DustN; i++)
        {
          var v = _dustVel[i] * drag;
          v.y -= 0.6f * dt;
          _dustVel[i] = v;
          _dustPos[i] += v * dt;
          float tw = 0.55f + 0.45f * Mathf.Sin(_t * 30f + i * 1.9f);
          float s = 0.05f * (1f - 0.5f * k);
          SpecterGeo.Diamond(_edge, _dustPos[i], right, up, s, s * 1.8f, shardA * tw);
        }

        // ④ 宽幅突触导管：EaseInQuad 回旋收缩，能量鼓包回流脚心
        float rk = Mathf.Clamp01(_t / 0.6f);
        float retract = 1f - rk * rk;
        for (int i = 0; i < SynN; i++)
        {
          float a = _synAng[i] - rk * 0.6f;
          float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
          float len = _synLen[i] * _r * retract;
          for (int p = 0; p < SynPts; p++)
          {
            float u = p / (float)(SynPts - 1);
            float j = _synJit[i * SynPts + p] * len;
            _pts[p] = new Vector3(ca * len * u - sa * j, 0.05f, sa * len * u + ca * j);
          }
          SpecterGeo.Ribbon(_body, _pts, SynPts, Vector3.up, 0.44f, true, 0.9f * retract);
          SpecterGeo.Ribbon((i & 1) == 0 ? _gold : _violet, _pts, SynPts, Vector3.up, 0.24f, true,
            retract * (0.7f + 0.3f * Mathf.Sin(_t * 40f + i)), 1.3f, 1f - rk);
        }

        // ⑤ 胸口实心内收环坍缩 → 白金八面核心一闪敛入体内
        float ir = Mathf.Lerp(_r * 1.2f, 0.06f, rk * rk);
        float iw = Mathf.Lerp(0.36f, 0.05f, rk);
        SpecterGeo.Annulus(_violet, chest, right, up, Mathf.Max(0f, ir - iw), ir + iw * 0.3f, 40, -_t * 3f, 0.08f, 1f,
          0.85f * (1f - rk * rk * rk), true);
        float ck = Mathf.Clamp01((_t - 0.45f) / 0.35f);
        float coreA = Mathf.Sin(ck * Mathf.PI);
        SpecterGeo.Fan(_edge, chest, right, up, 0.10f + 0.28f * coreA, 24, 0f, 0f, 1f, coreA, 0f);
        SpecterGeo.Octa(_edge, _gold, chest, 0.06f + 0.08f * coreA, 1.5f, Quaternion.Euler(0f, _t * 400f, 0f), coreA, 0.6f * coreA);

        _body.End(); _violet.End(); _gold.End(); _edge.End();
      }
      catch (Exception ex)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 巴别塔恶灵解除特效更新失败（已移除）：\n" + ex);
        Destroy(gameObject);
      }
    }

    private void OnDestroy()
    {
      _body?.Dispose(); _violet?.Dispose(); _gold?.Dispose(); _edge?.Dispose();
    }
  }

  /// <summary>
  /// 恶灵余波常驻跟随动画（SpecterAftermathAnim，继承 MarkFxBase，随 AftermathRestrictBuff 跟随 2 轮）——实体几何版：
  ///   · 疲惫喘息：脚下实心锯齿漆黑余烬地壳 × AftermathDim 柔边余光环 × 极淡精神紫勾边（防加法透空）；
  ///   · 熄灭的源石晶残桩：低矮外倾、半埋于地的黑曜三棱残晶，暗沉晶光；胸口柔边暗沉光盘；
  ///   · 微弱神经余颤：宽幅贴地残缺突触，Perlin 门控断续抽搐；缓缓沉落的暗沉菱形余烬晶片；0.3 s Master 淡出。
  /// </summary>
  public class SpecterAftermathAnim : MarkFxBase
  {
    private const int TremorN = 5, TremorPts = 6, FragN = 6, EmberN = 8;
    private static readonly Color D = SpecterFormFx.AftermathDim, Ob = SpecterFormFx.OriginiumObsidian,
      Vi = SpecterFormFx.NeuralViolet;

    private SolidMesh _body, _dim, _rim;
    private readonly Vector3[] _pts = new Vector3[TremorPts];
    private readonly float[] _trAng = new float[TremorN];
    private readonly float[] _trSeed = new float[TremorN];
    private readonly float[] _trJit = new float[TremorN * TremorPts];
    private readonly float[] _frAng = new float[FragN];
    private readonly float[] _frDist = new float[FragN];
    private readonly float[] _frTilt = new float[FragN];
    private readonly float[] _frSize = new float[FragN];
    private readonly float[] _emAng = new float[EmberN];
    private readonly float[] _emPhase = new float[EmberN];

    protected override bool Build(Buff buff)
    {
      var mD = Mat(D); var mOb = Mat(Ob); var mVi = Mat(Vi);
      if (mD == null || mOb == null || mVi == null)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 巴别塔恶灵·余波放弃：StreakMaterialPlain 无可用材质。");
        return false;
      }
      _body = new SolidMesh(transform, "RD_SP_AfBody", mOb, 300);
      _dim = new SolidMesh(transform, "RD_SP_AfDim", mD, 400);
      _rim = new SolidMesh(transform, "RD_SP_AfRim", mVi, 150);
      for (int i = 0; i < TremorN; i++)
      {
        _trAng[i] = (i + UnityEngine.Random.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / TremorN;
        _trSeed[i] = UnityEngine.Random.Range(0f, 100f);
        for (int p = 0; p < TremorPts; p++)
          _trJit[i * TremorPts + p] = p == 0 ? 0f : UnityEngine.Random.Range(-0.08f, 0.08f);
      }
      for (int i = 0; i < FragN; i++)
      {
        _frAng[i] = (i + UnityEngine.Random.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / FragN;
        _frDist[i] = UnityEngine.Random.Range(0.55f, 0.95f);
        _frTilt[i] = UnityEngine.Random.Range(0.5f, 1.4f);
        _frSize[i] = UnityEngine.Random.Range(0.7f, 1.2f);
      }
      for (int i = 0; i < EmberN; i++)
      {
        _emAng[i] = i * Mathf.PI * 2f / EmberN + UnityEngine.Random.Range(-0.3f, 0.3f);
        _emPhase[i] = UnityEngine.Random.value;
      }
      return true;
    }

    protected override void Tick(float dt)
    {
      transform.SetPositionAndRotation(Unit.Position, Quaternion.identity);
      CamBasis(out var right, out var up);
      var fwd = SpecterGeo.ViewFwd(right, up);
      float ink = 1f - Mathf.Clamp01(T / 0.4f);
      float appear = (1f - ink * ink * ink) * Master;
      float breath = 0.5f + 0.5f * Mathf.Sin(T * 1.4f);   // 疲惫的缓慢喘息
      float R = Radius, H = Height;
      _body.Begin(); _dim.Begin(); _rim.Begin();

      // 脚下：实心锯齿漆黑余烬地壳 + 暗沉柔边余光环 + 极淡精神紫勾边
      var g = Vector3.up * 0.03f;
      float gr = R * (0.95f + 0.05f * breath);
      SpecterGeo.Annulus(_body, g, Vector3.right, Vector3.forward, gr * 0.35f, gr, 48, 0f, 0.05f, 1f, 0.85f * appear, false);
      SpecterGeo.Annulus(_dim, g + Vector3.up * 0.005f, Vector3.right, Vector3.forward, gr * 0.82f, gr * 1.06f, 48, 0f, 0.05f, 1f,
        (0.45f + 0.3f * breath) * appear, true);
      SpecterGeo.Annulus(_rim, g + Vector3.up * 0.008f, Vector3.right, Vector3.forward, gr * 1.04f, gr * 1.10f, 48, 0f, 0.05f, 1f,
        (0.12f + 0.08f * breath) * appear, true);

      // 熄灭的源石晶残桩：低矮外倾、半埋于地的黑曜三棱残晶
      float fragR = Mathf.Clamp(R * 0.09f, 0.07f, 0.2f);
      for (int i = 0; i < FragN; i++)
      {
        var od = new Vector3(Mathf.Cos(_frAng[i]), 0f, Mathf.Sin(_frAng[i]));
        var b = od * (R * _frDist[i]);
        var ax = (Vector3.up + od * _frTilt[i]).normalized;
        float h = H * 0.22f * _frSize[i] * (0.97f + 0.03f * breath);
        SpecterGeo.Crystal(_body, _dim, null, b, ax, od, fragR * _frSize[i], h, 3, 0.3f * i,
          0.9f * appear, (0.25f + 0.2f * breath) * appear, 0f, fwd);
      }

      // 胸口：柔边暗沉光盘
      SpecterGeo.Fan(_dim, Vector3.up * (H * 0.5f), right, up, R * 0.55f * (0.94f + 0.06f * breath), 24, T * 0.3f, 0f, 1f,
        (0.22f + 0.15f * breath) * appear, 0f);

      // 微弱神经余颤：宽幅贴地残缺突触，Perlin 门控断续抽搐 + 高频细颤
      for (int i = 0; i < TremorN; i++)
      {
        float on = Mathf.Clamp01((Mathf.PerlinNoise(T * 2.5f, _trSeed[i]) - 0.45f) * 4f);
        if (on <= 0.01f) continue;
        float ca = Mathf.Cos(_trAng[i]), sa = Mathf.Sin(_trAng[i]);
        float shake = 0.035f * Mathf.Sin(T * 47f + i * 3.1f);
        for (int p = 0; p < TremorPts; p++)
        {
          float u = p / (float)(TremorPts - 1);
          float rr = R * (0.3f + 0.65f * u);
          float j = _trJit[i * TremorPts + p] + shake * u;
          _pts[p] = new Vector3(ca * rr - sa * j, 0.05f, sa * rr + ca * j);
        }
        SpecterGeo.Ribbon(_dim, _pts, TremorPts, Vector3.up, 0.26f, true, 0.7f * on * appear);
      }

      // 缓缓沉落的暗沉菱形余烬晶片
      for (int i = 0; i < EmberN; i++)
      {
        float k = Mathf.Repeat(T * 0.35f + _emPhase[i], 1f);
        float a = _emAng[i] + T * 0.2f;
        var pos = new Vector3(Mathf.Cos(a) * R * 0.55f, H * (0.9f - 0.8f * k), Mathf.Sin(a) * R * 0.55f);
        SpecterGeo.Diamond(_dim, pos, right, up, 0.04f, 0.08f, 0.6f * Mathf.Sin(k * Mathf.PI) * appear);
      }

      _body.End(); _dim.End(); _rim.End();
    }

    protected override void OnDestroy()
    {
      _body?.Dispose(); _dim?.Dispose(); _rim?.Dispose();
      base.OnDestroy();
    }
  }
}
