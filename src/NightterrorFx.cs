// 梦魇（4 环·夜魔/格罗莉亚）专属自绘视觉特效（纯视觉，不影响数值结算）。
//
// 对应干员：【夜魔】（Nightmare，格罗莉亚 Gloria）——罗德岛 5 星术师干员，罕见的双重人格感染者：
//   · 白天/主人格·格罗莉亚：温驯柔弱，白蓝光辉；
//   · 黑夜/里人格·夜魔：狂暴残忍，深紫幽暗梦魇。
// 技能对应：二技能【夜魇魔影】（Nightmare's Shadow）——在敌人脚下与周身植入如影随形的梦魇恶咒。
//
// 视觉体系（三位一体）：
//   1. 【施法植入（Cast / Mind Shock）】：
//      施法一瞬（NightterrorCastAnim），心智冲击波贯穿目标头部，双重人格微裂痕闪烁。
//   2. 【意志豁免失败 · 恐慌逃跑 2 轮（NightterrorPanicAnim，继承 MarkFxBase）】：
//      目标陷入极度恐慌仓皇逃离时（持续 2 轮），由 MarkFxTracker 订阅 RhodesDoctorBuffNightterrorPanic；
//      头顶盘旋浮现狂乱扭曲的【夜魇魔面/恐惧之眼】，脚底泛起深紫幽暗梦魇漩涡，
//      身周数道暗影怨灵之触在周围疯狂撕扯纠缠；到期或驱散后平滑淡出。
//   3. 【意志豁免成功 · 战栗 1 轮（NightterrorPassAnim）】：
//      意志豁免通过时（NightterrorPassAnim），仅在胸口与周身泛起微弱的幽蓝震颤波纹与轻度心智微光，迅速敛去。
//
// 渲染技术规范：
//   · 材质统一复用 MarkFxKit.Mat（底模 SwordStreakFx.StreakMaterialPlain），严禁 Shader.Find 与原版 PrefabLink。
//   · 零运行时 GC，数组/缓冲区在 Init/Build 中预分配，Tick/Update 不做 new 操作。
//   · 锚点防角色模型深度遮挡，线宽与尺度适配信仰 45° 俯视镜头。
//
// 注：基础脚手架已就绪，所有具体视觉几何、粒子物理与缓动算法全权交由 Macroscope 在 PR Check-Run 中自主完成。
using System;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Buffs;
using UnityEngine;
using static RhodesDoctor.MarkFxKit;

namespace RhodesDoctor
{
  internal static class NightterrorFx
  {
    // ── 夜魔双重人格·调色盘 ──
    // 加法材质下颜色越暗越透明，禁止使用纯黑以免完全透空。
    internal static readonly Color NightmarePurple = new Color(0.68f, 0.22f, 0.95f, 1f); // 夜魔里人格·幽魅深紫
    internal static readonly Color GloriaBlue      = new Color(0.42f, 0.75f, 1.00f, 1f); // 格罗莉亚主人格·冰蓝/温柔光辉
    internal static readonly Color MindWhite       = new Color(0.95f, 0.95f, 1.00f, 1f); // 心智裂光·高光内芯
    internal static readonly Color ShadowViolet    = new Color(0.40f, 0.12f, 0.65f, 1f); // 幽暗梦魇底色·残影外晕

    /// <summary>
    /// 恐慌状态（2 轮）常驻跟踪器：由 MarkFxWatcher 订阅 RhodesDoctorBuffNightterrorPanic。
    /// 目标仓皇逃跑时，夜魇魔影（头顶魔面/魔眼 + 脚下梦魇漩涡 + 怨灵触手）全程跟随。
    /// </summary>
    internal static readonly MarkFxTracker PanicTracker = new MarkFxTracker(
      "RhodesDoctorBuffNightterrorPanic", "梦魇·夜魇魔影（恐慌）", go => go.AddComponent<NightterrorPanicAnim>(),
      Guids.NightterrorPanicBuff);

    /// <summary>
    /// 施法一瞬：心智冲击波贯穿目标头部，双重人格微裂痕闪烁。
    /// </summary>
    internal static void PlayCast(UnitEntityData caster, UnitEntityData target)
    {
      try
      {
        if (target?.View == null) return;
        var go = new GameObject("RD_NightterrorCast") { layer = target.View.gameObject.layer };
        var anim = go.AddComponent<NightterrorCastAnim>();
        if (!anim.Init(caster, target)) UnityEngine.Object.Destroy(go);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 梦魇施法冲击特效失败（逻辑照常）：\n" + e);
      }
    }

    /// <summary>
    /// 意志豁免失败：即刻恐惧冲击（可选瞬发爆发，配合 PanicTracker 常驻跟随）。
    /// </summary>
    internal static void PlayFail(UnitEntityData target)
    {
      try
      {
        if (target?.View == null) return;
        // 预留给 Macroscope 根据需要扩展瞬时爆发
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 梦魇豁免失败爆发特效失败（逻辑照常）：\n" + e);
      }
    }

    /// <summary>
    /// 意志豁免成功：心智震颤顿挫与轻度幽蓝余波（持续约 0.8s 迅速敛去）。
    /// </summary>
    internal static void PlayPass(UnitEntityData target)
    {
      try
      {
        if (target?.View == null) return;
        var go = new GameObject("RD_NightterrorPass") { layer = target.View.gameObject.layer };
        var anim = go.AddComponent<NightterrorPassAnim>();
        if (!anim.Init(target)) UnityEngine.Object.Destroy(go);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 梦魇豁免成功震颤特效失败（逻辑照常）：\n" + e);
      }
    }
  }

  /// <summary>
  /// 梦魇施法冲击动画（NightterrorCastAnim）：
  /// 施法命中一瞬，自施法者向目标/在目标头部爆发心智惊骇冲击与双色微裂隙。
  /// 具体视觉几何与动效算法委托 Macroscope 实现。
  /// </summary>
  public class NightterrorCastAnim : MonoBehaviour
  {
    private const float Dur = 0.60f;
    private float _t;
    private UnitEntityData _target;

    internal bool Init(UnitEntityData caster, UnitEntityData target)
    {
      if (target?.View == null) return false;
      _target = target;
      transform.position = target.Position + Vector3.up * (ModelHeight(target) * 0.75f);
      return true;
    }

    private void Update()
    {
      _t += Time.deltaTime;
      if (_t >= Dur || _target == null || _target.View == null)
      {
        Destroy(gameObject);
        return;
      }
    }
  }

  /// <summary>
  /// 梦魇恐慌常驻动画（NightterrorPanicAnim，继承 MarkFxBase）：
  /// 意志豁免失败后，目标陷入恐慌仓皇逃跑（持续 2 轮）期间的夜魇恶咒：
  ///   · 头顶盘旋狂乱扭曲的【夜魇魔面/恐惧之眼】（正对相机，幽魅紫与格罗莉亚冰蓝交织）；
  ///   · 脚下深紫幽暗【梦魇漩涡】；
  ///   · 身周数道狂乱撕扯纠缠的【怨灵暗影之触】。
  /// 具体视觉几何、旋转与波动算法委托 Macroscope 实现。
  /// </summary>
  public class NightterrorPanicAnim : MarkFxBase
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

  /// <summary>
  /// 梦魇豁免成功心智震颤动画（NightterrorPassAnim）：
  /// 意志豁免通过时（战栗 1 轮），仅受创一瞬心智震颤顿挫与轻度幽蓝余波（约 0.8s 迅速敛去）。
  /// 具体视觉几何与动效算法委托 Macroscope 实现。
  /// </summary>
  public class NightterrorPassAnim : MonoBehaviour
  {
    private const float Dur = 0.80f;
    private float _t;
    private UnitEntityData _target;

    internal bool Init(UnitEntityData target)
    {
      if (target?.View == null) return false;
      _target = target;
      transform.position = target.Position + Vector3.up * (ModelHeight(target) * 0.5f);
      return true;
    }

    private void Update()
    {
      _t += Time.deltaTime;
      if (_t >= Dur || _target == null || _target.View == null)
      {
        Destroy(gameObject);
        return;
      }
    }
  }
}
