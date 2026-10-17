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
  /// 巴别塔恶灵形态开启瞬间动画（SpecterActivateAnim）：
  /// 恶灵形态开启瞬间（~0.8s）：
  ///   · 角色胸口与脚下源石神经突触猛烈向外放射激荡；
  ///   · 头顶上方虚空撕裂，前文明黑冠精神虚影显现。
  /// 具体视觉几何与动效算法委托 Macroscope 实现。
  /// </summary>
  public class SpecterActivateAnim : MonoBehaviour
  {
    private const float Dur = 0.80f;
    private float _t;
    private UnitEntityData _caster;
    private bool _isAgile;

    internal bool Init(UnitEntityData caster, bool isAgile)
    {
      if (caster?.View == null) return false;
      _caster = caster;
      _isAgile = isAgile;
      transform.position = caster.Position;
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
    }
  }

  /// <summary>
  /// 巴别塔恶灵常驻跟随动画（SpecterFormAnim，继承 MarkFxBase）：
  /// 形态常驻期间（随敏捷/坚韧形态 Buff 全程跟随）：
  ///   · 头顶悬浮立体自旋的前文明“黑冠”精神虚影（文明的存续），向下垂落细碎微光精神粒子；
  ///   · 脚底与周身蔓延脉动的源石神经突触网络：
  ///     - 敏捷形态（IsAgile = true）：偏炽烈源石金光流束与迅捷脉冲；
  ///     - 坚韧形态（IsAgile = false）：偏幽邃精神紫结晶晶壁与重力场波纹；
  ///   · 持续时间结束或被驱散后 0.3s 平滑淡出。
  /// 具体视觉几何、自旋粒子与突触网络算法委托 Macroscope 实现。
  /// </summary>
  public class SpecterFormAnim : MarkFxBase
  {
    public bool IsAgile;

    protected override bool Build(Buff buff)
    {
      // 预留骨架，待 Macroscope 在 PR Check-Run 中提供完整自绘实现
      return true;
    }

    protected override void Tick(float dt)
    {
      // 预留骨架，待 Macroscope 在 PR Check-Run 中提供完整每帧摆位与缓动
    }
  }

  /// <summary>
  /// 巴别塔恶灵形态结束/关闭瞬间动画（SpecterDeactivateAnim）：
  /// 恶灵形态结束瞬间（~0.8s）：
  ///   · 黑冠瓦解化为漫天源石星芒光尘散落；
  ///   · 源石突触收缩敛入体内。
  /// 具体视觉几何与光尘粒子算法委托 Macroscope 实现。
  /// </summary>
  public class SpecterDeactivateAnim : MonoBehaviour
  {
    private const float Dur = 0.80f;
    private float _t;
    private UnitEntityData _caster;

    internal bool Init(UnitEntityData caster)
    {
      if (caster?.View == null) return false;
      _caster = caster;
      transform.position = caster.Position;
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
    }
  }

  /// <summary>
  /// 恶灵余波常驻跟随动画（SpecterAftermathAnim，继承 MarkFxBase）：
  /// 恶灵余波期间（持续 2 轮，跟随 AftermathRestrictBuff）：
  ///   · 疲惫虚弱的暗沉余光与微弱神经余颤；
  ///   · 持续时间结束或被驱散后 0.3s 平滑淡出。
  /// 具体视觉几何与暗沉余光算法委托 Macroscope 实现。
  /// </summary>
  public class SpecterAftermathAnim : MarkFxBase
  {
    protected override bool Build(Buff buff)
    {
      // 预留骨架，待 Macroscope 在 PR Check-Run 中提供完整自绘实现
      return true;
    }

    protected override void Tick(float dt)
    {
      // 预留骨架，待 Macroscope 在 PR Check-Run 中提供完整每帧摆位与缓动
    }
  }
}
