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
  /// 巴别塔恶灵形态开启瞬间动画（SpecterActivateAnim，~0.8s）：
  ///   · 胸口源石神经突触（正对镜头）与脚下突触网络（贴地，漆黑源石底 + 形态色边缘辉光）猛烈放射激荡 + 地面冲击环；
  ///   · 头顶虚空裂隙（白金裂缘 + 漆黑源石内芯）撕开，前文明黑冠自裂隙中由 1.6 倍幻影收缩凝聚显现，
  ///     末段淡出交接常驻的 SpecterFormAnim。
  /// </summary>
  public class SpecterActivateAnim : MonoBehaviour
  {
    private const float Dur = 0.80f;
    private const int ChestN = 8, ChestPts = 6, GroundN = 8, GroundPts = 8;
    private const int Spikes = 7, CrownPts = Spikes * 2, BandPts = 48, RiftPts = 28;
    private static readonly Color Ob = SpecterFormFx.OriginiumObsidian, Au = SpecterFormFx.OriginiumGold,
      Vi = SpecterFormFx.NeuralViolet, Pt = SpecterFormFx.CrownPlatinum;

    private float _t, _h = 2f, _r = 0.8f;
    private UnitEntityData _caster;
    private bool _isAgile;
    private Color _acc;
    private LineRenderer _flash, _shock, _riftEdge, _riftCore, _crownBody, _crownEdge, _bandEdge;
    private readonly LineRenderer[] _chest = new LineRenderer[ChestN];
    private readonly float[] _chestAng = new float[ChestN];
    private readonly float[] _chestLen = new float[ChestN];
    private readonly float[] _chestJit = new float[ChestN * ChestPts];
    private readonly LineRenderer[] _gBody = new LineRenderer[GroundN];
    private readonly LineRenderer[] _gEdge = new LineRenderer[GroundN];
    private readonly float[] _gAng = new float[GroundN];
    private readonly float[] _gLen = new float[GroundN];
    private readonly float[] _gJit = new float[GroundN * GroundPts];

    internal bool Init(UnitEntityData caster, bool isAgile)
    {
      if (caster?.View == null) return false;
      var mOb = Mat(Ob); var mAu = Mat(Au); var mVi = Mat(Vi); var mPt = Mat(Pt);
      if (mOb == null || mAu == null || mVi == null || mPt == null)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 巴别塔恶灵开启特效放弃：StreakMaterialPlain 无可用材质。");
        return false;
      }
      _caster = caster;
      _isAgile = isAgile;
      _acc = isAgile ? Au : Vi;
      var mAcc = isAgile ? mAu : mVi;
      _h = ModelHeight(caster);
      _r = Mathf.Clamp(caster.View.Corpulence * 1.6f, 0.7f, 3.0f);
      transform.position = caster.Position;

      var taper = AnimationCurve.Linear(0f, 1f, 1f, 0.25f);
      for (int i = 0; i < ChestN; i++)
      {
        var lr = Line(transform, "RD_SP_ActChest", (i & 1) == 0 ? mAcc : mPt, ChestPts, false, 0.11f);
        lr.widthCurve = taper;
        _chest[i] = lr;
        _chestAng[i] = (i + UnityEngine.Random.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / ChestN;
        _chestLen[i] = UnityEngine.Random.Range(0.55f, 0.95f);
        for (int p = 0; p < ChestPts; p++)
          _chestJit[i * ChestPts + p] = p == 0 ? 0f : UnityEngine.Random.Range(-0.2f, 0.2f);
      }
      for (int i = 0; i < GroundN; i++)
      {
        _gBody[i] = Flat(transform, "RD_SP_ActSynBody", mOb, GroundPts, false, 0.22f);
        _gEdge[i] = Flat(transform, "RD_SP_ActSynEdge", mAcc, GroundPts, false, 0.09f);
        _gBody[i].widthCurve = taper;
        _gEdge[i].widthCurve = taper;
        _gAng[i] = (i + UnityEngine.Random.Range(-0.35f, 0.35f)) * Mathf.PI * 2f / GroundN;
        _gLen[i] = UnityEngine.Random.Range(1.5f, 2.3f);
        for (int p = 0; p < GroundPts; p++)
          _gJit[i * GroundPts + p] = p == 0 ? 0f : UnityEngine.Random.Range(-0.15f, 0.15f);
      }
      _flash     = Line(transform, "RD_SP_ActFlash", mPt, 32, true, 0.12f);
      _shock     = Flat(transform, "RD_SP_ActShock", mAcc, RingSeg, true, 0.18f);
      _riftEdge  = Line(transform, "RD_SP_ActRiftEdge", mPt, RiftPts, true, 0.08f);
      _riftCore  = Line(transform, "RD_SP_ActRiftCore", mOb, 2, false, 0.24f);
      _crownBody = Line(transform, "RD_SP_ActCrownBody", mOb, CrownPts, true, 0.16f);
      _crownEdge = Line(transform, "RD_SP_ActCrownEdge", mPt, CrownPts, true, 0.08f);
      _bandEdge  = Line(transform, "RD_SP_ActBandEdge", mAcc, BandPts, true, 0.07f);
      return true;
    }

    private static float Out3(float x) { x = 1f - Mathf.Clamp01(x); return 1f - x * x * x; }

    /// <summary>立体锯齿冠：偶数点为冠箍、奇数点为冠尖（水平自旋）。</summary>
    private static void Crown(LineRenderer lr, Vector3 c, float r, float h, float spin)
    {
      int n = lr.positionCount;
      for (int i = 0; i < n; i++)
      {
        float a = spin + i * Mathf.PI * 2f / n;
        bool tip = (i & 1) == 1;
        float rr = tip ? r * 1.1f : r;
        lr.SetPosition(i, c + new Vector3(Mathf.Cos(a) * rr, tip ? h : 0f, Mathf.Sin(a) * rr));
      }
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
        CamBasis(out var right, out var up);
        var feet = _caster.Position;
        var chest = feet + Vector3.up * (_h * 0.55f);
        transform.position = feet;
        float jitHz = _isAgile ? 70f : 38f;   // 敏捷激荡更急促

        // ① 胸口神经突触：0.18 s 猛烈放射、高频激荡，随后 0.55 s 消散
        float burst = Out3(_t / 0.18f);
        float burstA = Mathf.Clamp01(1f - (_t - 0.15f) / 0.55f);
        for (int i = 0; i < ChestN; i++)
        {
          var lr = _chest[i];
          float a = _chestAng[i];
          var d = right * Mathf.Cos(a) + up * Mathf.Sin(a);
          var n = up * Mathf.Cos(a) - right * Mathf.Sin(a);
          float len = _chestLen[i] * _r * 1.3f * burst * (1f + 0.1f * Mathf.Sin(_t * jitHz + i * 1.7f));
          for (int p = 0; p < ChestPts; p++)
          {
            float u = p / (float)(ChestPts - 1);
            lr.SetPosition(p, chest + d * (len * u) + n * (_chestJit[i * ChestPts + p] * len));
          }
          SetA(lr, (i & 1) == 0 ? _acc : Pt, burstA);
        }
        float fk = Mathf.Clamp01(_t / 0.25f);
        BillCircle(_flash, chest, Mathf.Lerp(0.08f, 0.6f, Out3(fk)), right, up);
        _flash.widthMultiplier = Mathf.Lerp(0.16f, 0.05f, fk);
        SetA(_flash, Pt, 1f - fk);

        // ② 脚下源石神经突触网：贴地放射（漆黑源石底 + 形态色辉光）+ 冲击环
        float gGrow = Out3((_t - 0.03f) / 0.28f);
        float gA = Mathf.Clamp01(1f - (_t - 0.35f) / 0.45f);
        for (int i = 0; i < GroundN; i++)
        {
          float a = _gAng[i];
          float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
          float len = _gLen[i] * _r * gGrow;
          for (int p = 0; p < GroundPts; p++)
          {
            float u = p / (float)(GroundPts - 1);
            float j = _gJit[i * GroundPts + p] * len;
            var pos = feet + new Vector3(ca * len * u - sa * j, 0.05f, sa * len * u + ca * j);
            _gBody[i].SetPosition(p, pos);
            _gEdge[i].SetPosition(p, pos);
          }
          SetA(_gBody[i], Ob, gA);
          SetA(_gEdge[i], _acc, gA * (0.7f + 0.3f * Mathf.Sin(_t * jitHz * 0.6f + i * 2.3f)));
        }
        float sk = Mathf.Clamp01(_t / 0.45f);
        Circle(_shock, feet, Mathf.Lerp(_r * 0.3f, _r * 2.4f, Out3(sk)), 0.05f);
        _shock.widthMultiplier = Mathf.Lerp(0.20f, 0.05f, sk);
        SetA(_shock, _acc, (1f - sk) * (1f - sk));

        // ③ 头顶虚空撕裂：锯齿裂隙张开（白金裂缘 + 漆黑源石内芯），0.45 s 后合拢
        var top = feet + Vector3.up * (_h + 0.5f);
        float open = Out3((_t - 0.05f) / 0.25f);
        float close = Mathf.Clamp01((_t - 0.45f) / 0.30f);
        float riftA = open * (1f - close);
        float rw = Mathf.Clamp(_r * 0.65f, 0.5f, 1.2f) * (0.35f + 0.65f * open);
        float rh = 0.34f * open * (1f - close);
        for (int i = 0; i < RiftPts; i++)
        {
          float a = i * Mathf.PI * 2f / RiftPts;
          float tear = 1f + 0.3f * Mathf.Sin(_t * 40f + i * 2.3f);
          _riftEdge.SetPosition(i, top + right * (Mathf.Cos(a) * rw) + up * (Mathf.Sin(a) * rh * tear));
        }
        _riftCore.SetPosition(0, top - right * (rw * 0.85f));
        _riftCore.SetPosition(1, top + right * (rw * 0.85f));
        _riftCore.widthMultiplier = 0.24f * riftA + 0.01f;
        SetA(_riftEdge, Pt, riftA);
        SetA(_riftCore, Ob, riftA);

        // ④ 黑冠显现：自裂隙中由 1.6 倍幻影收缩凝聚为常驻尺寸，末段淡出交接 SpecterFormAnim
        float m = Out3((_t - 0.20f) / 0.40f);
        float cr = Mathf.Clamp(_r * 0.42f, 0.32f, 0.8f) * Mathf.Lerp(1.6f, 1f, m);
        float spin = _t * 2.4f;
        Crown(_crownBody, top, cr, cr * 0.85f * m, spin);
        Crown(_crownEdge, top, cr, cr * 0.85f * m, spin);
        Circle(_bandEdge, top, cr * 1.05f, 0f);
        float crownA = m * Mathf.Clamp01(1f - (_t - 0.62f) / 0.18f);
        SetA(_crownBody, Ob, crownA);
        SetA(_crownEdge, Pt, crownA);
        SetA(_bandEdge, _acc, 0.9f * crownA);
      }
      catch (Exception ex)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 巴别塔恶灵开启特效更新失败（已移除）：\n" + ex);
        Destroy(gameObject);
      }
    }
  }

/// <summary>
  /// 巴别塔恶灵常驻跟随动画（SpecterFormAnim，继承 MarkFxBase）：
  ///   · 头顶倾斜进动 + 自旋的前文明黑冠（漆黑源石冠体 × 白金冠缘 × 形态色冠箍 × 白金光冕），向下垂落微光精神粒子；
  ///   · 脚下源石神经突触网（漆黑源石底 + 形态色辉光）脉动，能量脉冲沿突触奔流；
  ///     - 敏捷（IsAgile = true）：炽烈源石金螺旋光流束 + 迅捷白金脉冲；
  ///     - 坚韧（IsAgile = false）：精神紫源石晶壁 + 向内坍缩的重力场波纹 + 缓沉脉冲；
  ///   · 入场 EaseOut3，结束/驱散由 MarkFxBase.Master 0.3 s 平滑淡出。
  /// </summary>
  public class SpecterFormAnim : MarkFxBase
  {
    public bool IsAgile;

    private const int Spikes = 7, CrownPts = Spikes * 2, BandPts = 48, GemPts = 20, DripN = 10;
    private const int SynN = 6, SynPts = 9, StreamN = 3, StreamPts = 22, ShardN = 6, RippleN = 3;
    private const float Tilt = 0.14f;
    private static readonly Color Ob = SpecterFormFx.OriginiumObsidian, Au = SpecterFormFx.OriginiumGold,
      Vi = SpecterFormFx.NeuralViolet, Pt = SpecterFormFx.CrownPlatinum;

    private Color _acc;
    private LineRenderer _bandBody, _bandEdge, _crownBody, _crownEdge, _halo, _gem;
    private readonly LineRenderer[] _drip = new LineRenderer[DripN];
    private readonly float[] _dripAng = new float[DripN];
    private readonly float[] _dripPhase = new float[DripN];
    private readonly float[] _dripSpeed = new float[DripN];
    private readonly LineRenderer[] _synBody = new LineRenderer[SynN];
    private readonly LineRenderer[] _synEdge = new LineRenderer[SynN];
    private readonly LineRenderer[] _pulse = new LineRenderer[SynN];
    private readonly float[] _synAng = new float[SynN];
    private readonly float[] _synLen = new float[SynN];
    private readonly float[] _synJit = new float[SynN * SynPts];
    private readonly float[] _pulsePhase = new float[SynN];
    private readonly float[] _pulseSpeed = new float[SynN];
    private readonly LineRenderer[] _stream = new LineRenderer[StreamN];
    private readonly LineRenderer[] _shardBody = new LineRenderer[ShardN];
    private readonly LineRenderer[] _shardEdge = new LineRenderer[ShardN];
    private readonly LineRenderer[] _ripple = new LineRenderer[RippleN];

    protected override bool Build(Buff buff)
    {
      var mOb = Mat(Ob); var mAu = Mat(Au); var mVi = Mat(Vi); var mPt = Mat(Pt);
      if (mOb == null || mAu == null || mVi == null || mPt == null)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 巴别塔恶灵·形态常驻放弃：StreakMaterialPlain 无可用材质。");
        return false;
      }
      _acc = IsAgile ? Au : Vi;
      var mAcc = IsAgile ? mAu : mVi;

      // 头顶黑冠：漆黑源石冠体一律配白金/形态色边缘辉光（加法材质防透空）
      _bandBody  = Line(transform, "RD_SP_BandBody", mOb, BandPts, true, 0.16f);
      _bandEdge  = Line(transform, "RD_SP_BandEdge", mAcc, BandPts, true, 0.06f);
      _crownBody = Line(transform, "RD_SP_CrownBody", mOb, CrownPts, true, 0.15f);
      _crownEdge = Line(transform, "RD_SP_CrownEdge", mPt, CrownPts, true, 0.07f);
      _halo      = Line(transform, "RD_SP_Halo", mPt, BandPts, true, 0.05f);
      _gem       = Line(transform, "RD_SP_Gem", mAcc, GemPts, true, 0.07f);
      for (int i = 0; i < DripN; i++)
      {
        _drip[i] = Line(transform, "RD_SP_Drip", (i & 1) == 0 ? mPt : mAcc, 2, false, 0.05f);
        _dripAng[i] = i * Mathf.PI * 2f / DripN + UnityEngine.Random.Range(-0.25f, 0.25f);
        _dripPhase[i] = UnityEngine.Random.value;
        _dripSpeed[i] = UnityEngine.Random.Range(0.55f, 0.95f);
      }

      // 脚下神经突触网 + 奔流脉冲
      var taper = AnimationCurve.Linear(0f, 1f, 1f, 0.3f);
      float speed = IsAgile ? 1.7f : 0.55f;
      for (int i = 0; i < SynN; i++)
      {
        _synBody[i] = Flat(transform, "RD_SP_SynBody", mOb, SynPts, false, 0.20f);
        _synEdge[i] = Flat(transform, "RD_SP_SynEdge", mAcc, SynPts, false, 0.08f);
        _synBody[i].widthCurve = taper;
        _synEdge[i].widthCurve = taper;
        _synAng[i] = (i + UnityEngine.Random.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / SynN;
        _synLen[i] = UnityEngine.Random.Range(1.3f, 1.9f);
        for (int p = 0; p < SynPts; p++)
          _synJit[i * SynPts + p] = p == 0 ? 0f : UnityEngine.Random.Range(-0.16f, 0.16f);
        _pulse[i] = Flat(transform, "RD_SP_Pulse", IsAgile ? mPt : mAcc, 2, false, IsAgile ? 0.13f : 0.15f);
        _pulsePhase[i] = UnityEngine.Random.value;
        _pulseSpeed[i] = speed * UnityEngine.Random.Range(0.8f, 1.25f);
      }

      if (IsAgile)
      {
        var streamCurve = new AnimationCurve(new Keyframe(0f, 0.25f), new Keyframe(0.45f, 1f), new Keyframe(1f, 0.15f));
        for (int s = 0; s < StreamN; s++)
        {
          _stream[s] = Line(transform, "RD_SP_Stream", mAu, StreamPts, false, 0.12f);
          _stream[s].widthCurve = streamCurve;
        }
      }
      else
      {
        for (int i = 0; i < ShardN; i++)
        {
          _shardBody[i] = Line(transform, "RD_SP_ShardBody", mOb, 4, true, 0.18f);
          _shardEdge[i] = Line(transform, "RD_SP_ShardEdge", mVi, 4, true, 0.07f);
        }
        for (int i = 0; i < RippleN; i++)
          _ripple[i] = Flat(transform, "RD_SP_Gravity", mVi, RingSeg, true, 0.09f);
      }
      return true;
    }

    private static float Out3(float x) { x = 1f - Mathf.Clamp01(x); return 1f - x * x * x; }

    /// <summary>倾斜进动的水平环（冠箍 / 光冕）。</summary>
    private static void Ring3(LineRenderer lr, Vector3 c, float r, float spin, float tiltPh, float yOff)
    {
      int n = lr.positionCount;
      for (int i = 0; i < n; i++)
      {
        float a = spin + i * Mathf.PI * 2f / n;
        float y = yOff + Mathf.Sin(a - tiltPh) * r * Tilt;
        lr.SetPosition(i, c + new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
      }
    }

    /// <summary>立体锯齿冠（偶数点冠箍、奇数点冠尖），随冠箍同步倾斜进动。</summary>
    private static void Crown(LineRenderer lr, Vector3 c, float r, float h, float spin, float tiltPh)
    {
      int n = lr.positionCount;
      for (int i = 0; i < n; i++)
      {
        float a = spin + i * Mathf.PI * 2f / n;
        bool tip = (i & 1) == 1;
        float rr = tip ? r * 1.1f : r;
        float y = (tip ? h : 0f) + Mathf.Sin(a - tiltPh) * r * Tilt;
        lr.SetPosition(i, c + new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr));
      }
    }

    private Vector3 SynNode(Vector3 feet, float ca, float sa, float len, int i, int p)
    {
      float u = p / (float)(SynPts - 1);
      float j = _synJit[i * SynPts + p] * len;
      return feet + new Vector3(ca * len * u - sa * j, 0.05f, sa * len * u + ca * j);
    }

    private Vector3 SynAt(Vector3 feet, float ca, float sa, float len, int i, float u)
    {
      float f = Mathf.Clamp01(u) * (SynPts - 1);
      int p = Mathf.Min((int)f, SynPts - 2);
      return Vector3.Lerp(SynNode(feet, ca, sa, len, i, p), SynNode(feet, ca, sa, len, i, p + 1), f - p);
    }

    protected override void Tick(float dt)
    {
      var feet = Unit.Position;
      float ink = 1f - Mathf.Clamp01(T / 0.35f);
      float appear = (1f - ink * ink * ink) * Master;               // EaseOut3 入场 × 0.3 s 淡出
      float R = Radius;
      CamBasis(out var right, out var up);

      // ── 头顶前文明黑冠：倾斜进动 + 自旋 ──
      float cr = Mathf.Clamp(R * 0.42f, 0.32f, 0.8f) * Mathf.Lerp(0.6f, 1f, appear);
      float ch = cr * 0.85f;
      var cc = feet + Vector3.up * (Height + 0.5f + 0.06f * Mathf.Sin(T * 1.6f));
      float spin = T * 0.9f, tiltPh = T * 0.7f;
      float beat = 0.75f + 0.25f * Mathf.Sin(T * 2.4f);
      Ring3(_bandBody, cc, cr, spin, tiltPh, 0f);
      Ring3(_bandEdge, cc, cr * 1.05f, spin, tiltPh, 0f);
      Crown(_crownBody, cc, cr, ch, spin, tiltPh);
      Crown(_crownEdge, cc, cr, ch, spin, tiltPh);
      Ring3(_halo, cc, cr * 0.72f, -spin * 1.6f, tiltPh, ch * 1.15f);
      BillCircle(_gem, cc + Vector3.up * (ch * 0.45f), cr * 0.2f * (0.9f + 0.1f * Mathf.Sin(T * 5f)), right, up, T * 2f);
      SetA(_bandBody, Ob, appear);
      SetA(_bandEdge, _acc, 0.9f * appear);
      SetA(_crownBody, Ob, appear);
      SetA(_crownEdge, Pt, beat * appear);
      SetA(_halo, Pt, 0.55f * beat * appear);
      SetA(_gem, _acc, beat * appear);

      // ── 垂落微光精神粒子：自冠箍下缘加速下坠，拖尾随下坠拉长 ──
      float dropLen = Height * 0.65f;
      for (int i = 0; i < DripN; i++)
      {
        float k = Mathf.Repeat(T * _dripSpeed[i] + _dripPhase[i], 1f);
        float a = _dripAng[i] + spin;
        float fall = dropLen * (0.25f * k + 0.75f * k * k);
        var head = cc + new Vector3(Mathf.Cos(a) * cr * 0.95f, -fall, Mathf.Sin(a) * cr * 0.95f);
        var lr = _drip[i];
        lr.SetPosition(0, head);
        lr.SetPosition(1, head + Vector3.up * (0.10f + 0.14f * k));
        SetA(lr, (i & 1) == 0 ? Pt : _acc, 0.85f * Mathf.Sin(k * Mathf.PI) * appear);
      }

      // ── 脚下源石神经突触网：脉动 + 沿突触奔流的能量脉冲 ──
      float grow = Out3(T / 0.45f);
      float throbHz = IsAgile ? 7f : 2.5f;
      float pulseLen = IsAgile ? 0.22f : 0.14f;
      var lift = Vector3.up * 0.02f;
      for (int i = 0; i < SynN; i++)
      {
        float a = _synAng[i];
        float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
        float len = R * _synLen[i] * grow * (0.94f + 0.06f * Mathf.Sin(T * throbHz * 0.8f + i));
        for (int p = 0; p < SynPts; p++)
        {
          var pos = SynNode(feet, ca, sa, len, i, p);
          _synBody[i].SetPosition(p, pos);
          _synEdge[i].SetPosition(p, pos);
        }
        SetA(_synBody[i], Ob, appear);
        SetA(_synEdge[i], _acc, (0.6f + 0.4f * Mathf.Sin(T * throbHz - i * 1.1f)) * appear);

        float u0 = Mathf.Repeat(T * _pulseSpeed[i] + _pulsePhase[i], 1f);
        _pulse[i].SetPosition(0, SynAt(feet, ca, sa, len, i, u0) + lift);
        _pulse[i].SetPosition(1, SynAt(feet, ca, sa, len, i, u0 + pulseLen) + lift);
        SetA(_pulse[i], IsAgile ? Pt : _acc, Mathf.Sin(u0 * Mathf.PI) * appear);
      }

      if (IsAgile)
      {
        // ── 敏捷：环身螺旋上冲的炽烈源石金光流束 ──
        for (int s = 0; s < StreamN; s++)
        {
          var lr = _stream[s];
          float ph = s * Mathf.PI * 2f / StreamN + T * 4.2f;
          for (int p = 0; p < StreamPts; p++)
          {
            float u = p / (float)(StreamPts - 1);
            float a = ph + u * 3.8f;
            float r = R * (0.78f - 0.28f * u) + 0.05f * Mathf.Sin(T * 11f + u * 12f + s);
            lr.SetPosition(p, feet + new Vector3(Mathf.Cos(a) * r, u * Height * 1.02f, Mathf.Sin(a) * r));
          }
          SetA(lr, Au, (0.65f + 0.35f * Mathf.Sin(T * 9f + s * 2.1f)) * appear);
        }
      }
      else
      {
        // ── 坚韧：环身矗立的精神紫源石晶壁 + 向内坍缩的重力场波纹 ──
        float half = Mathf.Clamp(R * 0.22f, 0.14f, 0.4f);
        for (int i = 0; i < ShardN; i++)
        {
          float a = i * Mathf.PI * 2f / ShardN + T * 0.22f;
          float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
          float rise = Out3((T - i * 0.05f) / 0.5f);
          float h = Height * (0.5f + 0.08f * Mathf.Sin(T * 1.3f + i * 1.7f)) * rise;
          var b = feet + new Vector3(ca * R * 1.1f, 0.04f, sa * R * 1.1f);
          var tan = new Vector3(-sa * half, 0f, ca * half);
          var p1 = b + tan + Vector3.up * (h * 0.42f);
          var p2 = b + Vector3.up * h;
          var p3 = b - tan + Vector3.up * (h * 0.42f);
          var body = _shardBody[i];
          var edge = _shardEdge[i];
          body.SetPosition(0, b); body.SetPosition(1, p1); body.SetPosition(2, p2); body.SetPosition(3, p3);
          edge.SetPosition(0, b); edge.SetPosition(1, p1); edge.SetPosition(2, p2); edge.SetPosition(3, p3);
          SetA(body, Ob, appear);
          SetA(edge, Vi, (0.7f + 0.3f * Mathf.Sin(T * 2f + i * 1.3f)) * appear);
        }
        for (int i = 0; i < RippleN; i++)
        {
          float k = Mathf.Repeat(T * 0.45f + i / (float)RippleN, 1f);
          Circle(_ripple[i], feet, Mathf.Lerp(R * 1.7f, R * 0.25f, k * k), 0.04f);
          SetA(_ripple[i], Vi, 0.8f * Mathf.Sin(k * Mathf.PI) * appear);
        }
      }
    }
  }

/// <summary>
  /// 巴别塔恶灵形态结束/关闭瞬间动画（SpecterDeactivateAnim，~0.8s）：
  ///   · 黑冠外胀崩散、冠尖塌陷、加速自旋后消失，化为初速外抛 + 阻尼 + 微重力缓落的源石星芒光尘；
  ///   · 脚下源石突触 EaseInQuad 回旋收缩至脚心，胸口精神紫内收环坍缩，白金核心一闪敛入体内。
  /// </summary>
  public class SpecterDeactivateAnim : MonoBehaviour
  {
    private const float Dur = 0.80f;
    private const int Spikes = 7, CrownPts = Spikes * 2, MoteN = 20, SynN = 6, SynPts = 7;
    private static readonly Color Ob = SpecterFormFx.OriginiumObsidian, Au = SpecterFormFx.OriginiumGold,
      Vi = SpecterFormFx.NeuralViolet, Pt = SpecterFormFx.CrownPlatinum;

    private float _t, _h = 2f, _r = 0.8f, _cr = 0.5f;
    private UnitEntityData _caster;
    private LineRenderer _crownBody, _crownEdge, _implode, _core;
    private readonly LineRenderer[] _mote = new LineRenderer[MoteN];
    private readonly Vector3[] _motePos = new Vector3[MoteN];
    private readonly Vector3[] _moteVel = new Vector3[MoteN];
    private readonly Color[] _moteCol = new Color[MoteN];
    private readonly LineRenderer[] _synBody = new LineRenderer[SynN];
    private readonly LineRenderer[] _synEdge = new LineRenderer[SynN];
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
      var feet = caster.Position;
      transform.position = feet;
      var crownC = feet + Vector3.up * (_h + 0.5f);

      _crownBody = Line(transform, "RD_SP_DeCrownBody", mOb, CrownPts, true, 0.16f);
      _crownEdge = Line(transform, "RD_SP_DeCrownEdge", mPt, CrownPts, true, 0.08f);
      _implode   = Line(transform, "RD_SP_DeImplode", mVi, 40, true, 0.10f);
      _core      = Line(transform, "RD_SP_DeCore", mPt, 20, true, 0.10f);
      for (int i = 0; i < MoteN; i++)
      {
        int c = i % 3;
        _moteCol[i] = c == 0 ? Pt : c == 1 ? Au : Vi;
        _mote[i] = Line(transform, "RD_SP_DeMote", c == 0 ? mPt : c == 1 ? mAu : mVi, 2, false, 0.09f);
        float a = i * Mathf.PI * 2f / MoteN + UnityEngine.Random.Range(-0.2f, 0.2f);
        float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
        _motePos[i] = crownC + new Vector3(ca * _cr, UnityEngine.Random.Range(0f, _cr * 0.85f), sa * _cr);
        _moteVel[i] = new Vector3(ca, 0f, sa) * UnityEngine.Random.Range(0.9f, 2.1f)
          + Vector3.up * UnityEngine.Random.Range(0.3f, 1.4f);
      }
      var taper = AnimationCurve.Linear(0f, 1f, 1f, 0.25f);
      for (int i = 0; i < SynN; i++)
      {
        _synBody[i] = Flat(transform, "RD_SP_DeSynBody", mOb, SynPts, false, 0.20f);
        _synEdge[i] = Flat(transform, "RD_SP_DeSynEdge", (i & 1) == 0 ? mAu : mVi, SynPts, false, 0.08f);
        _synBody[i].widthCurve = taper;
        _synEdge[i].widthCurve = taper;
        _synAng[i] = (i + UnityEngine.Random.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / SynN;
        _synLen[i] = UnityEngine.Random.Range(1.3f, 1.9f);
        for (int p = 0; p < SynPts; p++)
          _synJit[i * SynPts + p] = p == 0 ? 0f : UnityEngine.Random.Range(-0.16f, 0.16f);
      }
      return true;
    }

    private static float Out3(float x) { x = 1f - Mathf.Clamp01(x); return 1f - x * x * x; }

    private static void Crown(LineRenderer lr, Vector3 c, float r, float h, float spin)
    {
      int n = lr.positionCount;
      for (int i = 0; i < n; i++)
      {
        float a = spin + i * Mathf.PI * 2f / n;
        bool tip = (i & 1) == 1;
        float rr = tip ? r * 1.1f : r;
        lr.SetPosition(i, c + new Vector3(Mathf.Cos(a) * rr, tip ? h : 0f, Mathf.Sin(a) * rr));
      }
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
        float k = _t / Dur;
        CamBasis(out var right, out var up);
        var feet = _caster.Position;
        var chest = feet + Vector3.up * (_h * 0.55f);
        var crownC = feet + Vector3.up * (_h + 0.5f);
        transform.position = feet;

        // ① 黑冠瓦解：0.3 s 内外胀崩散、冠尖塌陷、加速自旋
        float sk = Mathf.Clamp01(_t / 0.30f);
        float cr = _cr * (1f + 0.35f * Out3(sk));
        float spin = 0.9f * _t + 6f * sk * sk;
        float spikeH = _cr * 0.85f * (1f - sk);
        Crown(_crownBody, crownC, cr, spikeH, spin);
        Crown(_crownEdge, crownC, cr, spikeH, spin);
        SetA(_crownBody, Ob, 1f - sk);
        SetA(_crownEdge, Pt, (1f - sk) * (0.75f + 0.25f * Mathf.Sin(_t * 60f)));

        // ② 源石星芒光尘：初速外抛 + 阻尼 + 微重力缓落，拖尾随速度伸缩
        float drag = Mathf.Max(0f, 1f - 2.4f * dt);
        float moteA = Mathf.Pow(1f - k, 1.5f);
        float moteW = Mathf.Lerp(0.10f, 0.03f, k);
        for (int i = 0; i < MoteN; i++)
        {
          var v = _moteVel[i] * drag;
          v.y -= 0.9f * dt;
          _moteVel[i] = v;
          _motePos[i] += v * dt;
          var lr = _mote[i];
          lr.SetPosition(0, _motePos[i]);
          lr.SetPosition(1, _motePos[i] - v * 0.07f + Vector3.up * 0.03f);
          lr.widthMultiplier = moteW;
          SetA(lr, _moteCol[i], moteA * (0.6f + 0.4f * Mathf.Sin(_t * 30f + i * 1.9f)));
        }

        // ③ 源石突触收缩：EaseInQuad 回旋敛入脚心
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
            var pos = feet + new Vector3(ca * len * u - sa * j, 0.05f, sa * len * u + ca * j);
            _synBody[i].SetPosition(p, pos);
            _synEdge[i].SetPosition(p, pos);
          }
          SetA(_synBody[i], Ob, retract);
          SetA(_synEdge[i], (i & 1) == 0 ? Au : Vi, retract * (0.7f + 0.3f * Mathf.Sin(_t * 40f + i)));
        }

        // ④ 胸口内收环坍缩 → 白金核心一闪敛入体内
        BillCircle(_implode, chest, Mathf.Lerp(_r * 1.2f, 0.06f, rk * rk), right, up, -_t * 3f);
        SetA(_implode, Vi, 0.85f * (1f - rk * rk * rk));
        float ck = Mathf.Clamp01((_t - 0.45f) / 0.35f);
        float coreA = Mathf.Sin(ck * Mathf.PI);
        BillCircle(_core, chest, 0.08f + 0.14f * coreA, right, up);
        SetA(_core, Pt, coreA);
      }
      catch (Exception ex)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 巴别塔恶灵解除特效更新失败（已移除）：\n" + ex);
        Destroy(gameObject);
      }
    }
  }

/// <summary>
  /// 恶灵余波常驻跟随动画（SpecterAftermathAnim，继承 MarkFxBase，随 AftermathRestrictBuff 跟随 2 轮）：
  ///   · 疲惫的缓慢喘息：脚下漆黑源石环 × AftermathDim 边缘余光、胸口暗沉光晕；
  ///   · 微弱神经余颤：断续闪现（Perlin 门控）、高频细颤的贴地突触残片；
  ///   · 缓缓沉落的暗沉余烬；0.3 s Master 平滑淡出。
  /// </summary>
  public class SpecterAftermathAnim : MarkFxBase
  {
    private const int TremorN = 4, TremorPts = 6, EmberN = 6;
    private static readonly Color D = SpecterFormFx.AftermathDim, Ob = SpecterFormFx.OriginiumObsidian;

    private LineRenderer _groundBody, _groundEdge, _aura;
    private readonly LineRenderer[] _tremor = new LineRenderer[TremorN];
    private readonly float[] _tremAng = new float[TremorN];
    private readonly float[] _tremSeed = new float[TremorN];
    private readonly float[] _tremJit = new float[TremorN * TremorPts];
    private readonly LineRenderer[] _ember = new LineRenderer[EmberN];
    private readonly float[] _emberAng = new float[EmberN];
    private readonly float[] _emberPhase = new float[EmberN];

    protected override bool Build(Buff buff)
    {
      var mD = Mat(D); var mOb = Mat(Ob);
      if (mD == null || mOb == null)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 巴别塔恶灵·余波放弃：StreakMaterialPlain 无可用材质。");
        return false;
      }
      _groundBody = Flat(transform, "RD_SP_AfGroundBody", mOb, RingSeg, true, 0.22f);
      _groundEdge = Flat(transform, "RD_SP_AfGroundEdge", mD, RingSeg, true, 0.08f);
      _aura       = Line(transform, "RD_SP_AfAura", mD, 40, true, 0.10f);
      for (int i = 0; i < TremorN; i++)
      {
        _tremor[i] = Flat(transform, "RD_SP_AfTremor", mD, TremorPts, false, 0.07f);
        _tremAng[i] = (i + UnityEngine.Random.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / TremorN;
        _tremSeed[i] = UnityEngine.Random.Range(0f, 100f);
        for (int p = 0; p < TremorPts; p++)
          _tremJit[i * TremorPts + p] = p == 0 ? 0f : UnityEngine.Random.Range(-0.08f, 0.08f);
      }
      for (int i = 0; i < EmberN; i++)
      {
        _ember[i] = Line(transform, "RD_SP_AfEmber", mD, 2, false, 0.06f);
        _emberAng[i] = i * Mathf.PI * 2f / EmberN + UnityEngine.Random.Range(-0.3f, 0.3f);
        _emberPhase[i] = UnityEngine.Random.value;
      }
      return true;
    }

    protected override void Tick(float dt)
    {
      var feet = Unit.Position;
      float ink = 1f - Mathf.Clamp01(T / 0.4f);
      float appear = (1f - ink * ink * ink) * Master;
      float breath = 0.5f + 0.5f * Mathf.Sin(T * 1.4f);   // 疲惫的缓慢喘息
      float R = Radius;

      // 脚下暗沉余光环（漆黑源石底 + 暗沉边缘辉光）
      Circle(_groundBody, feet, R * (0.9f + 0.05f * breath), 0.04f);
      Circle(_groundEdge, feet, R * (0.95f + 0.05f * breath), 0.05f);
      SetA(_groundBody, Ob, appear);
      SetA(_groundEdge, D, (0.4f + 0.3f * breath) * appear);

      // 胸口暗沉光晕
      CamBasis(out var right, out var up);
      var chest = feet + Vector3.up * (Height * 0.5f);
      BillCircle(_aura, chest, R * 0.6f * (0.94f + 0.06f * breath), right, up, T * 0.3f);
      SetA(_aura, D, (0.25f + 0.2f * breath) * appear);

      // 微弱神经余颤：Perlin 门控断续闪现 + 高频细颤
      for (int i = 0; i < TremorN; i++)
      {
        float on = Mathf.Clamp01((Mathf.PerlinNoise(T * 2.5f, _tremSeed[i]) - 0.45f) * 4f);
        float a = _tremAng[i];
        float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
        float shake = 0.035f * Mathf.Sin(T * 47f + i * 3.1f);
        var lr = _tremor[i];
        for (int p = 0; p < TremorPts; p++)
        {
          float u = p / (float)(TremorPts - 1);
          float rr = R * (0.3f + 0.65f * u);
          float j = _tremJit[i * TremorPts + p] + shake * u;
          lr.SetPosition(p, feet + new Vector3(ca * rr - sa * j, 0.06f, sa * rr + ca * j));
        }
        SetA(lr, D, 0.75f * on * appear);
      }

      // 缓缓沉落的暗沉余烬
      for (int i = 0; i < EmberN; i++)
      {
        float k = Mathf.Repeat(T * 0.35f + _emberPhase[i], 1f);
        float a = _emberAng[i] + T * 0.2f;
        var pos = feet + new Vector3(Mathf.Cos(a) * R * 0.55f, Height * (0.9f - 0.8f * k), Mathf.Sin(a) * R * 0.55f);
        var lr = _ember[i];
        lr.SetPosition(0, pos);
        lr.SetPosition(1, pos + Vector3.up * 0.1f);
        SetA(lr, D, 0.55f * Mathf.Sin(k * Mathf.PI) * appear);
      }
    }
  }
}
