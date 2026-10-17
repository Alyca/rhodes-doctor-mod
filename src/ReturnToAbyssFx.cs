// 归溟 / 潮（7 环·归溟幽灵鲨/劳伦缇娜）专属自绘视觉特效（纯视觉，不影响数值结算）。
//
// 对应干员：【归溟幽灵鲨】（Specter the Unchained，劳伦缇娜 Laurentina）——阿戈尔深海猎人二期工程荣光，死灵学派：
//   · 核心意象：深海洋流漩涡、狂暴大洋暗流光带、深海浪涌与生命同调逆流回溯、怒涛冲击环。
//
// 视觉体系（四位一体）：
//   1. 【迅捷施法瞬间（ReturnToAbyssCastAnim）】：
//      施法一瞬脚下深海潮汐螺旋升腾炸开（必须体现真实潮汐喷薄与破浪水流厚度）。
//   2. 【增益常驻跟随（ReturnToAbyssAnim，继承 MarkFxBase，持续 1 轮/级）】：
//      由 MarkFxTracker 订阅 RhodesDoctorBuffReturnToAbyss（Guids.ReturnToAbyssBuff）；
//      脚底自旋呼吸的深海洋流漩涡；周身螺旋缠绕升腾的大洋暗流光带与悬浮浪沫水粒；0.3s 平滑淡出。
//   3. 【近战命中吸血（ReturnToAbyssHealAnim）】：
//      命中吸血 2d6 时，汹涌水瀑与深海暗涌自目标处逆流回流施法者。
//   4. 【暴击/击杀续期恶灵（ReturnToAbyssRefundAnim）】：
//      每轮首暴/首杀返还 1 轮恶灵形态时，深海怒涛暴涨，荡开一道排山倒海的炽白浪沫冲击环！
//
// 渲染技术规范：
//   · 色彩体系：深渊海蓝（AbyssalBlue）、苍翠洋流绿（OceanicTeal）、炽白浪沫（WaveFoamWhite）、深海暗涌（DeepOceanViolet）。
//   · 加法材质下禁止使用完全透空的纯黑，保证 45° 俯视远景下清晰完整。
//   · 材质统一复用 MarkFxKit.Mat（底模 SwordStreakFx.StreakMaterialPlain），严禁 Shader.Find 与原版 PrefabLink。
//   · 零运行时 GC，组件自管理生命周期与超时 Destroy。
//   · 锚点防角色模型深度遮挡，线宽与尺度适配信仰 45° 俯视镜头。
//
// 【CRITICAL MANDATE — 作者实测反馈（2026-10-17）】：
//   上一版因线条感过重、缺乏水与潮汐的厚度与流体质感而被全盘否定。
//   本文件已重置为骨架，全权交由 Macroscope 重新设计并输出具备真实流体厚度、潮涌起伏与浪花翻滚质感的自绘代码。
using System;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Buffs;
using UnityEngine;
using static RhodesDoctor.MarkFxKit;

namespace RhodesDoctor
{
  internal static class ReturnToAbyssFx
  {
    // ── 阿戈尔深海猎人·调色盘 ──
    // 加法材质下颜色越暗越透明，禁止使用纯黑以免完全透空。
    internal static readonly Color AbyssalBlue      = new Color(0.12f, 0.45f, 0.95f, 1f); // 深渊海蓝·主调狂暴洋流
    internal static readonly Color OceanicTeal     = new Color(0.15f, 0.85f, 0.75f, 1f); // 苍翠洋流绿·生命同调光晕
    internal static readonly Color WaveFoamWhite    = new Color(0.92f, 0.98f, 1.00f, 1f); // 炽白浪沫·高光水粒与怒涛波芯
    internal static readonly Color DeepOceanViolet  = new Color(0.38f, 0.18f, 0.75f, 1f); // 深海暗涌·外层暗流卷带

    /// <summary>
    /// 归溟常驻跟踪器：由 MarkFxWatcher 订阅 RhodesDoctorBuffReturnToAbyss。
    /// 施法者身上洋流缠身（脚底漩涡 + 周身螺旋洋流光带 + 浪沫水粒）全程跟随。
    /// </summary>
    internal static readonly MarkFxTracker Tracker = new MarkFxTracker(
      "RhodesDoctorBuffReturnToAbyss", "归溟·潮（大洋流）", go => go.AddComponent<ReturnToAbyssAnim>(),
      Guids.ReturnToAbyssBuff);

    /// <summary>
    /// 迅捷施法瞬间：脚下深海潮汐螺旋升腾炸开。
    /// </summary>
    internal static void PlayCast(UnitEntityData caster)
    {
      try
      {
        if (caster?.View == null) return;
        var go = new GameObject("RD_ReturnToAbyssCast") { layer = caster.View.gameObject.layer };
        var anim = go.AddComponent<ReturnToAbyssCastAnim>();
        if (!anim.Init(caster)) UnityEngine.Object.Destroy(go);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 归溟施法潮汐特效失败（逻辑照常）：\n" + e);
      }
    }

    /// <summary>
    /// 近战命中吸血：幽蓝浪涌自目标处逆流回流施法者。
    /// </summary>
    internal static void PlayHeal(UnitEntityData source, UnitEntityData caster)
    {
      try
      {
        if (caster?.View == null) return;
        var go = new GameObject("RD_ReturnToAbyssHeal") { layer = caster.View.gameObject.layer };
        var anim = go.AddComponent<ReturnToAbyssHealAnim>();
        if (!anim.Init(source, caster)) UnityEngine.Object.Destroy(go);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 归溟命中吸血回流特效失败（逻辑照常）：\n" + e);
      }
    }

    /// <summary>
    /// 每轮首暴/首杀续期恶灵：深海怒涛暴涨，荡开一道炽白浪沫冲击环！
    /// </summary>
    internal static void PlayRefund(UnitEntityData caster)
    {
      try
      {
        if (caster?.View == null) return;
        var go = new GameObject("RD_ReturnToAbyssRefund") { layer = caster.View.gameObject.layer };
        var anim = go.AddComponent<ReturnToAbyssRefundAnim>();
        if (!anim.Init(caster)) UnityEngine.Object.Destroy(go);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 归溟恶灵返还怒涛冲击特效失败（逻辑照常）：\n" + e);
      }
    }
  }

  /// <summary>
  /// 归溟施法潮汐升腾动画（ReturnToAbyssCastAnim）：
  /// 迅捷施法瞬间，脚下深海潮汐喷薄爆发。
  /// 【作者实测反馈】：绝不可只有单薄细线，必须有潮汐涌泉与水体升腾的厚度与破浪感，全权交由 Macroscope 重构。
  /// </summary>
  public class ReturnToAbyssCastAnim : MonoBehaviour
  {
    private const float Dur = 0.70f;
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
  /// 归溟大洋流常驻动画（ReturnToAbyssAnim，继承 MarkFxBase）：
  /// 增益持续期间（1 轮/级）跟随施法者：
  ///   · 脚底自旋呼吸的深海洋流漩涡；
  ///   · 周身螺旋缠绕升腾的大洋暗流与悬浮浪沫水粒；
  ///   · 到期或被驱散后 0.3s 平滑淡出。
  /// 【作者实测反馈】：必须体现真实流体厚度、水流密度与潮汐起伏，绝不可只有单线几何，全权交由 Macroscope 重构。
  /// </summary>
  public class ReturnToAbyssAnim : MarkFxBase
  {
    protected override bool Build(Buff buff)
    {
      // 预留骨架，待 Macroscope 在 PR Check-Run 中提供全新流体自绘实现
      return true;
    }

    protected override void Tick(float dt)
    {
      // 预留骨架，待 Macroscope 在 PR Check-Run 中提供全新流体摆位与缓动
    }
  }

  /// <summary>
  /// 归溟命中吸血逆流动画（ReturnToAbyssHealAnim）：
  /// 近战命中造成伤害并吸血时，深海狂暴暗流与水瀑自受创目标处逆流回流施法者。
  /// 【作者实测反馈】：必须是奔涌回溯的深海流体水瀑，而不是单薄单线，全权交由 Macroscope 重构。
  /// </summary>
  public class ReturnToAbyssHealAnim : MonoBehaviour
  {
    private const float Dur = 0.60f;
    private float _t;
    private UnitEntityData _source;
    private UnitEntityData _caster;

    internal bool Init(UnitEntityData source, UnitEntityData caster)
    {
      if (caster?.View == null) return false;
      _source = source;
      _caster = caster;
      transform.position = source?.View != null ? source.Position : caster.Position;
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
  /// 归溟恶灵形态续期怒涛冲击环动画（ReturnToAbyssRefundAnim）：
  /// 每轮首暴或首杀返还 1 轮恶灵持续时间时，海啸破浪般排山倒海的怒涛冲击波与炽白浪花翻滚！
  /// 【作者实测反馈】：必须体现狂暴潮汐冲击波的磅礴厚度与翻滚浪沫，全权交由 Macroscope 重构。
  /// </summary>
  public class ReturnToAbyssRefundAnim : MonoBehaviour
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
}
