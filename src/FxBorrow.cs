// 特效借用：运行时从原版蓝图借 PrefabLink（不写死 AssetId）。
// 借不到返回 null（调用方保留原值），只打日志，功能不受影响。
// （写法对照 ExusiaiCompanion 的 SkillFx：Buff 常驻循环光 / 施法爆发，分开借。）
using System;
using BlueprintCore.Blueprints.References;
using BlueprintCore.Utils;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.ResourceLinks;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components.Base;
using Kingmaker.UnitLogic.Buffs.Blueprints;

namespace RhodesDoctor
{
  internal static class FxBorrow
  {
    /// <summary>借原版区域的常驻 FX（区域存续期间循环显示）。按顺序取第一个借到的。</summary>
    internal static PrefabLink AreaFx(params string[] areaGuids)
    {
      foreach (var a in areaGuids)
      {
        try
        {
          if (BlueprintTool.TryGet<BlueprintAbilityAreaEffect>(a.ToString(), out var bp)
            && bp != null && bp.Fx != null)
          {
            Dbg.Log($"[RhodesDoctor] 特效借用成功（区域）：{a}");
            return bp.Fx;
          }
        }
        catch (Exception e)
        {
          Main.Mod.Logger.Error($"[RhodesDoctor] 特效借用异常（区域 {a}）：\n{e}");
        }
      }
      Dbg.Log("[RhodesDoctor] 特效借用失败（区域）：全部候选均无 FX。");
      return null;
    }

    /// <summary>
    /// 借原版区域里“一次性爆发”的 FX：区域 UnitEnter/UnitExit/UnitMove/Round 动作里第一个
    /// ContextActionSpawnFx 的 PrefabLink。区域自己的 Fx 是常驻循环，不适用于爆炸这种一次性效果。
    /// 按顺序取第一个借到的。
    /// </summary>
    internal static PrefabLink AreaBurstFx(params string[] areaGuids)
    {
      foreach (var a in areaGuids)
      {
        try
        {
          if (!BlueprintTool.TryGet<BlueprintAbilityAreaEffect>(a.ToString(), out var bp) || bp == null)
            continue;
          Kingmaker.UnitLogic.Abilities.Components.AreaEffects.AbilityAreaEffectRunAction comp = null;
          foreach (var c in bp.ComponentsArray)
            if (c is Kingmaker.UnitLogic.Abilities.Components.AreaEffects.AbilityAreaEffectRunAction run)
            {
              comp = run;
              break;
            }
          if (comp == null) continue;
          foreach (var list in new[] { comp.UnitEnter, comp.UnitExit, comp.UnitMove, comp.Round })
          {
            var found = FindSpawnFx(list?.Actions);
            if (found != null)
            {
              Dbg.Log($"[RhodesDoctor] 特效借用成功（爆发）：{a}");
              return found;
            }
          }
        }
        catch (Exception e)
        {
          Main.Mod.Logger.Error($"[RhodesDoctor] 特效借用异常（爆发 {a}）：\n{e}");
        }
      }
      Dbg.Log("[RhodesDoctor] 特效借用失败（爆发）：全部候选均无爆发 FX。");
      return null;
    }

    /// <summary>在动作树里（含 Conditional 分支）找第一个 ContextActionSpawnFx 的 PrefabLink。</summary>
    private static PrefabLink FindSpawnFx(Kingmaker.ElementsSystem.GameAction[] actions)
    {
      if (actions == null) return null;
      foreach (var act in actions)
      {
        if (act is Kingmaker.UnitLogic.Mechanics.Actions.ContextActionSpawnFx fx
            && fx.PrefabLink != null && fx.PrefabLink.Exists())
          return fx.PrefabLink;
        if (act is Kingmaker.Designers.EventConditionActionSystem.Actions.Conditional cond)
        {
          var r = FindSpawnFx(cond.IfTrue?.Actions) ?? FindSpawnFx(cond.IfFalse?.Actions);
          if (r != null) return r;
        }
      }
      return null;
    }

    /// <summary>
    /// 借原版法术的“施法/命中爆发”FX：AbilitySpawnFx 组件里的 PrefabLink。
    /// 优先取锚在目标（ClickedTarget / SelectedTarget）的那一个，避免拿到只是施法者脚下的光效。
    /// 按顺序取第一个借到的；借不到返回 null（调用方不加特效）。
    /// </summary>
    internal static PrefabLink AbilityBurstFx(params string[] abilityGuids)
    {
      foreach (var a in abilityGuids)
      {
        try
        {
          if (!BlueprintTool.TryGet<BlueprintAbility>(a.ToString(), out var bp) || bp == null)
            continue;
          PrefabLink fallback = null;
          foreach (var c in bp.ComponentsArray)
          {
            if (!(c is AbilitySpawnFx fx) || fx.PrefabLink == null || !fx.PrefabLink.Exists())
              continue;
            if (fx.Anchor == AbilitySpawnFxAnchor.Caster || fx.PositionAnchor == AbilitySpawnFxAnchor.Caster)
            {
              if (fallback == null) fallback = fx.PrefabLink; // 只有施法者锚点时留作兜底
              continue;
            }
            Dbg.Log($"[RhodesDoctor] 特效借用成功（爆发·法术）：{a}");
            return fx.PrefabLink;
          }
          if (fallback != null)
          {
            Dbg.Log($"[RhodesDoctor] 特效借用成功（爆发·法术·兜底）：{a}");
            return fallback;
          }
        }
        catch (Exception e)
        {
          Main.Mod.Logger.Error($"[RhodesDoctor] 特效借用异常（爆发·法术 {a}）：\n{e}");
        }
      }
      Dbg.Log("[RhodesDoctor] 特效借用失败（爆发·法术）：全部候选均无 AbilitySpawnFx。");
      return null;
    }

    /// <summary>借原版增益的 FxOnStart（常驻循环光）。按顺序取第一个借到的。</summary>
    internal static PrefabLink BuffAura(params string[] buffGuids)
    {
      foreach (var b in buffGuids)
      {
        try
        {
          if (BlueprintTool.TryGet<BlueprintBuff>(b.ToString(), out var bp)
            && bp != null && bp.FxOnStart != null)
          {
            Dbg.Log($"[RhodesDoctor] 特效借用成功（增益）：{b}");
            return bp.FxOnStart;
          }
        }
        catch (Exception e)
        {
          Main.Mod.Logger.Error($"[RhodesDoctor] 特效借用异常（增益 {b}）：\n{e}");
        }
      }
      Dbg.Log("[RhodesDoctor] 特效借用失败（增益）：全部候选均无 FX。");
      return null;
    }

    /// <summary>
    /// 借原版**投射物**的外观 prefab（单个物件，如一把飞剑），用于"按个数摆放"的场景
    /// （例：剑雨把 N 把剑摆成一圈）。
    ///
    /// 为什么不能直接返回 <c>bp.View</c>：<c>ProjectileLink</c>（WeakResourceLink&lt;ProjectileView&gt;）
    /// 与 <c>PrefabLink</c>（WeakResourceLink&lt;GameObject&gt;）是**兄弟类型**，不是父子，不能直接转换。
    /// 所以这里把 <c>View.AssetId</c> 抄进一个新的 PrefabLink——AssetId 仍是**运行时从原版蓝图读的**，
    /// 没有写死。
    /// 按顺序取第一个借到的；借不到返回 null（调用方不加特效）。
    /// </summary>
    internal static PrefabLink ProjectileViewFx(params string[] projectileGuids)
    {
      foreach (var g in projectileGuids)
      {
        try
        {
          if (BlueprintTool.TryGet<BlueprintProjectile>(g, out var bp) && bp != null
            && bp.View != null && !string.IsNullOrEmpty(bp.View.AssetId))
          {
            Dbg.Log($"[RhodesDoctor] 特效借用成功（投射物外观）：{g}");
            return new PrefabLink { AssetId = bp.View.AssetId };
          }
        }
        catch (Exception e)
        {
          Main.Mod.Logger.Error($"[RhodesDoctor] 特效借用异常（投射物外观 {g}）：\n{e}");
        }
      }
      Dbg.Log("[RhodesDoctor] 特效借用失败（投射物外观）：全部候选均无 View。");
      return null;
    }
    /// <summary>
    /// 借原版**武器模型**（真正的剑身网格），用于"要看起来像剑"的场合（剑雨）。
    ///
    /// 为什么不用投射物外观：投射物借到的是**飞行拖尾**（金色光弧），不是剑的形状——
    /// 剑雨第一版就是这么翻车的（用户反馈"看着不像是剑"）。
    ///
    /// 取法：<c>BlueprintWeaponType.VisualParameters</c>（公开属性）→ 其中的 <c>m_WeaponModel</c>
    /// 是私有字段（`WeaponVisualParameters.Model` 那个公开属性返回的是已加载的 GameObject，
    /// 不是 PrefabLink），所以这里用反射读私有 `m_WeaponModel`，拿到的仍是 PrefabLink。
    /// AssetId 运行时从原版蓝图读，不写死。
    /// </summary>
    internal static PrefabLink WeaponModelFx(params string[] weaponTypeGuids)
    {
      var field = typeof(WeaponVisualParameters).GetField(
        "m_WeaponModel",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
          | System.Reflection.BindingFlags.Public);
      foreach (var g in weaponTypeGuids)
      {
        try
        {
          if (!BlueprintTool.TryGet<BlueprintWeaponType>(g, out var bp) || bp == null) continue;
          var vp = bp.VisualParameters;
          if (vp == null || field == null) continue;
          if (field.GetValue(vp) is PrefabLink link && link.Exists())
          {
            Dbg.Log($"[RhodesDoctor] 特效借用成功（武器模型）：{g}");
            return link;
          }
        }
        catch (Exception e)
        {
          Main.Mod.Logger.Error($"[RhodesDoctor] 特效借用异常（武器模型 {g}）：\n{e}");
        }
      }
      Dbg.Log("[RhodesDoctor] 特效借用失败（武器模型）：全部候选均无 m_WeaponModel。");
      return null;
    }
  }
}
