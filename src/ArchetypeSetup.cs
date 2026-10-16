// 变体本体：法术书挂载（SetReplaceSpellbook）、集火（职业特性）、源石技艺精通、
// 冷静头脑、指挥灵光/全军指令、圣疗与引导正能量的智力化克隆，以及成长表的增删。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BlueprintCore.Actions.Builder;
using BlueprintCore.Actions.Builder.ContextEx;
using BlueprintCore.Conditions.Builder;
using BlueprintCore.Conditions.Builder.ContextEx;
using BlueprintCore.Blueprints.Configurators.Classes;
using BlueprintCore.Blueprints.CustomConfigurators;
using BlueprintCore.Blueprints.CustomConfigurators.Classes;
using BlueprintCore.Blueprints.CustomConfigurators.UnitLogic.Abilities;
using BlueprintCore.Blueprints.CustomConfigurators.UnitLogic.Buffs;
using BlueprintCore.Utils;
using BlueprintCore.Utils.Types;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Abilities.Components.TargetCheckers;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Buffs.Components;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.Utility;
using RhodesDoctor.Components;

namespace RhodesDoctor
{
  internal static class ArchetypeSetup
  {
    internal static void Create()
    {
      CreateFocusFire();
      CreateMastery();
      CreateCoolHead();
      CreateAuras();
      ClonePaladinFeatures();
      CreateSpellcastingFeature();
      CreateArchetype();
    }

    // ── 战术指令法术书：1 级就授予（规则文档第二节）──
    // 圣武士本体到 4 级才进施法；博士 1 级起就该有书，所以额外挂一个"授予整本法术书"的特性。
    // AddSpellbook 组件的施法者等级取 ContextRank（ContextRankConfig = 圣武士等级）。
    private static void CreateSpellcastingFeature()
    {
      FeatureConfigurator.New("RhodesDoctorSpellcasting", Guids.SpellcastingFeature)
        .SetDisplayName(L10n.S("spellbook.name", "战术指令", "Tactical Orders"))
        .SetDescription(L10n.S("spellcasting.desc",
          "1 级起，博士获得战术指令法术书：以智力自发施法，每日法术位与已知指令数与术士相同；0 环指令为可无限施展的戏法。",
          "From 1st level the Doctor gains the Tactical Orders spellbook: spontaneous Intelligence casting using the sorcerer's spells-per-day and spells-known tables; 0-level orders are at-will cantrips."))
        .AddContextRankConfig(ContextRankConfigs.ClassLevel(
          new[] { Guids.Vanilla.PaladinClass }, false, AbilityRankType.Default))
        // V4：本法术书忽略护甲/盾牌的奥术失败率（反编译见 IgnoreArcaneSpellFailure 注释）。
        .AddComponent<IgnoreArcaneSpellFailure>()
        // 已知指令补发：让"已经练到高等级 / 旧存档"的角色也能拿到按等级应得的全套已知指令
        //（升级界面只对逐级升上来的角色发名额）。见 GrantKnownOrdersBackfill。
        .AddComponent<GrantKnownOrdersBackfill>()
        .AddSpellbook(
          casterLevel: ContextValues.Rank(),
          spellbook: BlueprintTool.GetRef<BlueprintSpellbookReference>(Guids.Spellbook))
        .Configure();
    }
    // 10 环指令**不进成长表**：20 级时由法术书的"已知法术表"像其他环一样在**升级界面**给出选择
    //（4 个名额选满 = 学会全部 4 个 10 环指令）。见 SpellbookSetup.CreateSpellsKnownTable 与 TenRingRow。
    // 成长表不补发旧存档，而"表 + 升级界面"这条路对升级中的角色天然生效，所以能不用就不用。

    // ── 集火：职业特性，每日 3 + 智力调整值 次（不占法术位）──
    private static void CreateFocusFire()
    {
      AbilityResourceConfigurator.New("RhodesDoctorFocusFireResource", Guids.FocusFireResource)
        .SetMaxAmount(ResourceAmountBuilder.New(3).IncreaseByStat(StatType.Intelligence))
        .Configure(); // VERIFY：最低 1 次未强制

      // 标记增益：挂在被标记的敌人身上；加值 = 1 + 博士等级÷4（逐级表）
      BuffConfigurator.New("RhodesDoctorFocusFireMark", Guids.FocusFireMarkBuff)
        .SetIcon(Icons.Status("focus-fire-mark", "spells/00-tactical-mark.png"))
        .SetDisplayName(L10n.S("focusfire.mark.name", "集火标记", "Focus Fire Mark"))
        .SetDescription(L10n.S("focusfire.mark.desc",
          "所有友军对该目标的攻击与伤害获得洞察加值。",
          "All allies gain an insight bonus on attack and damage rolls against this target."))
        .AddComponent<FocusFireMark>(c => c.Bonus = ContextValues.Rank())
        .AddContextRankConfig(ContextRankConfigs
          .ClassLevel(new[] { Guids.Vanilla.PaladinClass }, false, AbilityRankType.Default)
          .WithCustomProgression(Enumerable.Range(1, 20).Select(l => (l, 1 + l / 4)).ToArray()))
        .Configure();

      AbilityConfigurator.New("RhodesDoctorFocusFire", Guids.FocusFireAbility)
        .SetDisplayName(L10n.S("focusfire.name", "集火", "Focus Fire"))
        .SetDescription(L10n.S("focusfire.desc",
          "迅捷动作；60 尺；持续轮数等于智力调整值（至少 1 轮）。标记一名敌人：所有友军对它的攻击与伤害获得 +1 洞察加值，每 4 个圣武士等级再 +1（20 级 +6）。同一时间只能维持一个标记。每日 3 + 智力调整值 次。",
          "Swift action; 60 ft.; duration equals Intelligence modifier rounds (min 1). Mark one enemy: all allies gain +1 insight on attack and damage against it, +1 more per 4 paladin levels (+6 at 20th). One mark at a time. Usable 3 + Intelligence modifier times per day."))
        .SetActionType(UnitCommand.CommandType.Swift)
        .SetIcon(Icons.Status("focus-fire-mark", "spells/00-tactical-mark.png"))
        .SetRange(AbilityRange.Custom)
        .SetCustomRange(60)
        .SetCanTargetEnemies(true)
        // 集火只能标敌人：CanTargetEnemies 只影响光标，真正挡住自己/友军靠这条限制
        //（与 SpellsSetup 里的纯敌方单体法术同一处理）。
        .AddComponent<AbilityTargetIsAlly>(c => c.Not = true)
        // B12：恶灵生效期间（一段/二段锁定）禁用集火；16 级三段锁定是另一个增益，不受此限。
        // R2：二段改用独立锁定 LockBuffV2，一并拦截。
        .AddComponent<AbilityCasterHasNoActiveFacts>(c => c.Facts = new BlueprintUnitFactReference[]
        {
          BlueprintTool.GetRef<BlueprintUnitFactReference>(Guids.LockBuff),
          BlueprintTool.GetRef<BlueprintUnitFactReference>(Guids.LockBuffV2),
        })
        .AddAbilityResourceLogic(amount: 1, isSpendResource: true,
          requiredResource: BlueprintTool.GetRef<BlueprintAbilityResourceReference>(Guids.FocusFireResource))
        // 持续轮数 = 智力调整值（B12：智力调整值 ≤0 时至少 1 轮）。
        .AddContextRankConfig(ContextRankConfigs.StatBonus(StatType.Intelligence, min: 1))
        .AddAbilityEffectRunAction(ActionsBuilder.New()
          .Add<ContextActionRemoveMyBuffs>(a =>
            a.Buff = BlueprintTool.GetRef<BlueprintBuffReference>(Guids.FocusFireMarkBuff))
          .ApplyBuff(BlueprintTool.GetRef<BlueprintBuffReference>(Guids.FocusFireMarkBuff),
            ContextDuration.Variable(ContextValues.Rank(), DurationRate.Rounds)))
        .Configure();

      FeatureConfigurator.New("RhodesDoctorFocusFireFeature", Guids.FocusFireFeature)
        .SetIcon(Icons.Status("focus-fire-mark", "spells/00-tactical-mark.png"))
        .SetDisplayName(L10n.S("focusfire.name", "集火", "Focus Fire"))
        .SetDescription(L10n.S("focusfire.feature.desc",
          "博士每日可使用集火 3 + 智力调整值 次。此能力与战术指令法术书一同替换惩戒邪恶。",
          "The Doctor can use Focus Fire 3 + Intelligence modifier times per day. Together with the spellbook, this replaces smite evil."))
        .AddAbilityResources(resource: Guids.FocusFireResource, restoreAmount: true)
        .AddFacts(new List<Blueprint<BlueprintUnitFactReference>> { Guids.FocusFireAbility })
        .Configure();
    }

    // ── 源石技艺精通（2026-10-11 改版）：法术穿透 + 法术豁免 DC 各 +1/+2/+3/+4/+5；
    //    5 档落在 1/5/9/13/17 级；恶灵中两项加值失效并倒扣转移量 ──
    // 本地化键全部字面量（抓取器不展开拼接键）；zh/en 文本在 LocalizedStrings.json。
    // 描述模板：档位值 N 两处（穿透/DC）+ 恶灵倒扣说明统一。
    private static void CreateMastery()
    {
      Mastery(Guids.Mastery1Feature, 1, "RhodesDoctorMastery1",
        L10n.S("mastery1.name", "源石技艺精通", "Originium Arts Mastery"));
      Mastery(Guids.Mastery2Feature, 2, "RhodesDoctorMastery2",
        L10n.S("mastery2.name", "源石技艺精通", "Originium Arts Mastery"),
        upgrades: Guids.Mastery1Feature);
      Mastery(Guids.Mastery3Feature, 3, "RhodesDoctorMastery3",
        L10n.S("mastery3.name", "源石技艺精通", "Originium Arts Mastery"),
        upgrades: Guids.Mastery2Feature);
      Mastery(Guids.Mastery4Feature, 4, "RhodesDoctorMastery4",
        L10n.S("mastery4.name", "源石技艺精通", "Originium Arts Mastery"),
        upgrades: Guids.Mastery3Feature);
      Mastery(Guids.Mastery5Feature, 5, "RhodesDoctorMastery5",
        L10n.S("mastery5.name", "源石技艺精通", "Originium Arts Mastery"),
        upgrades: Guids.Mastery4Feature);
    }

    /// <summary>第 N 档（N=1..5）：法术穿透与法术豁免 DC **各** +N。描述由档位值生成。</summary>
    private static void Mastery(string guid, int value, string name, string displayName, string upgrades = null)
    {
      string desc = L10n.S("mastery" + value + ".desc",
        $"博士的法术穿透与法术豁免难度各获得 +{value} 加值（不具类型，可叠加）。巴别塔的恶灵生效期间，此二项加值失效，且博士的法术穿透与法术豁免难度改为承受等于转移量的减值。此能力替换神恩。",
        $"+{value} on spell penetration and spell save DC (untyped, stacking). While the Specter is active these bonuses are lost and the Doctor's spell penetration and spell save DC instead take a penalty equal to the Transfer Value. This ability replaces divine grace.");
      var cfg = FeatureConfigurator.New(name, guid)
        // 图标：借原版专长"法术穿透"（没做专属图，见 Icons.Borrow）。缺图时 SetIcon(null) 用默认图，不断链。
        .SetIcon(Icons.Borrow(Guids.Vanilla.SpellPenetrationFeature))
        .SetDisplayName(displayName)
        .SetDescription(desc)
        .AddComponent<OriginiumMastery>(c =>
        {
          c.Value = value;
          c.LockHalved = BlueprintTool.GetRef<BlueprintBuffReference>(Guids.LockBuff);
          c.LockFull = BlueprintTool.GetRef<BlueprintBuffReference>(Guids.LockBuffV3);
          c.LockFull2 = BlueprintTool.GetRef<BlueprintBuffReference>(Guids.LockBuffV2);
        });
      if (upgrades != null)
        cfg.AddRemoveFeatureOnApply(upgrades);
      cfg.Configure();
    }

    // ── 冷静头脑（恶灵期间减半，20 级全额）──
    private static void CreateCoolHead()
    {
      FeatureConfigurator.New("RhodesDoctorCoolHead", Guids.CoolHeadFeature)
        // 图标：借原版专长"钢铁意志"（豁免主题）。
        .SetIcon(Icons.Borrow(Guids.Vanilla.IronWillFeat))
        .SetDisplayName(L10n.S("coolhead.name", "冷静头脑", "Cool Head"))
        .SetDescription(L10n.S("coolhead.desc",
          "博士将智力调整值（若为正）加到所有豁免检定上（不具类型，可叠加）。巴别塔的恶灵生效期间减半；20 级后不再减半。",
          "The Doctor adds their Intelligence modifier (if positive) as an untyped bonus on all saving throws. Halved while the Specter is active; full again at 20th level."))
        .AddComponent<CoolHeadSaveBonus>(c => c.SpecterLocks = new[]
        {
          BlueprintTool.GetRef<BlueprintBuffReference>(Guids.LockBuff),
          BlueprintTool.GetRef<BlueprintBuffReference>(Guids.LockBuffV2),
          BlueprintTool.GetRef<BlueprintBuffReference>(Guids.LockBuffV3),
        })
        .Configure();
    }

    // ── 指挥灵光（4 级）与全军指令（11 级）──
    // V3：原版灵光是"持有者增益 → AddAreaEffect → 区域 → 友军效果增益"三段结构。
    // 旧版直接给持有者挂 AuraFeatureComponent，而该组件只把增益加到持有者自己身上（反编译确认），
    // 友军根本吃不到。现在改为原版结构：Feature 用 AuraFeatureComponent 挂"载体增益"，
    // 载体增益用 AddAreaEffect 生成半径 10/30 尺的区域，区域把效果增益挂给范围内友军。
    private static void CreateAuras()
    {
      // 指挥灵光（10 尺）
      BuffConfigurator.New("RhodesDoctorAuraCommandAlly", Guids.AuraCommandAllyBuff)
        .SetIcon(Icons.Status("command-aura", "spells/01-hold-the-line.png"))
        .SetDisplayName(L10n.S("aura.name", "指挥灵光", "Aura of Command"))
        .SetDescription(L10n.S("aura.ally.desc", "攻击检定与先攻检定 +1（胜任）。", "+1 competence on attack rolls and initiative checks."))
        .AddStatBonus(descriptor: ModifierDescriptor.Competence, stat: StatType.AdditionalAttackBonus, value: 1)
        .AddStatBonus(descriptor: ModifierDescriptor.Competence, stat: StatType.Initiative, value: 1)
        .Configure();
      var auraArea = AbilityAreaEffectConfigurator.New("RhodesDoctorAuraCommandArea", Guids.AuraCommandArea)
        .SetShape(AreaEffectShape.Cylinder)
        .SetSize(new Feet(10))
        .AddAbilityAreaEffectBuff(buff: Guids.AuraCommandAllyBuff, condition: ConditionsBuilder.New().IsAlly())
        .Configure();
      SetAreaTargetType(auraArea, "Ally");
      BuffConfigurator.New("RhodesDoctorAuraCommandCarrier", Guids.AuraCommandSelfBuff)
        .SetIcon(Icons.Status("command-aura", "spells/01-hold-the-line.png"))
        .SetDisplayName(L10n.S("aura.name", "指挥灵光", "Aura of Command"))
        .SetDescription(L10n.S("aura.self.desc", "10 尺内的友军获得指挥灵光。", "Allies within 10 feet gain Aura of Command."))
        .AddAreaEffect(Guids.AuraCommandArea)
        .Configure();
      FeatureConfigurator.New("RhodesDoctorAuraCommand", Guids.AuraCommandFeature)
        .SetIcon(Icons.Status("command-aura", "spells/01-hold-the-line.png"))
        .SetDisplayName(L10n.S("aura.name", "指挥灵光", "Aura of Command"))
        .SetDescription(L10n.S("aura.desc",
          "半径 10 尺灵光：博士与灵光内友军攻击与先攻 +1（胜任）。博士失去意识或恶灵生效期间失效。此能力替换原版圣武士施法。",
          "10-foot aura: +1 competence on attack rolls and initiative checks for the Doctor and allies within. Suppressed while the Specter is active. This ability replaces the paladin's spellcasting."))
        .AddComponent<AuraFeatureComponent>(c =>
          SetAuraBuff(c, BlueprintTool.GetRef<BlueprintBuffReference>(Guids.AuraCommandSelfBuff)))
        .Configure();

      // 全军指令（30 尺，+2）
      BuffConfigurator.New("RhodesDoctorTotalCommandAlly", Guids.TotalCommandAllyBuff)
        .SetIcon(Icons.Status("command-aura", "spells/01-hold-the-line.png"))
        .SetDisplayName(L10n.S("total.name", "全军指令", "Total Command"))
        .SetDescription(L10n.S("total.ally.desc", "攻击检定与先攻检定 +2（胜任）。", "+2 competence on attack rolls and initiative checks."))
        .AddStatBonus(descriptor: ModifierDescriptor.Competence, stat: StatType.AdditionalAttackBonus, value: 2)
        .AddStatBonus(descriptor: ModifierDescriptor.Competence, stat: StatType.Initiative, value: 2)
        .Configure();
      var totalArea = AbilityAreaEffectConfigurator.New("RhodesDoctorTotalCommandArea", Guids.TotalCommandArea)
        .SetShape(AreaEffectShape.Cylinder)
        .SetSize(new Feet(30))
        .AddAbilityAreaEffectBuff(buff: Guids.TotalCommandAllyBuff, condition: ConditionsBuilder.New().IsAlly())
        .Configure();
      SetAreaTargetType(totalArea, "Ally");
      BuffConfigurator.New("RhodesDoctorTotalCommandCarrier", Guids.TotalCommandSelfBuff)
        .SetIcon(Icons.Status("command-aura", "spells/01-hold-the-line.png"))
        .SetDisplayName(L10n.S("total.name", "全军指令", "Total Command"))
        .SetDescription(L10n.S("total.self.desc", "30 尺内的友军获得全军指令。", "Allies within 30 feet gain Total Command."))
        .AddAreaEffect(Guids.TotalCommandArea)
        .Configure();
      FeatureConfigurator.New("RhodesDoctorTotalCommand", Guids.TotalCommandFeature)
        .SetIcon(Icons.Status("command-aura", "spells/01-hold-the-line.png"))
        .SetDisplayName(L10n.S("total.name", "全军指令", "Total Command"))
        .SetDescription(L10n.S("total.desc",
          "指挥灵光扩大到 30 尺、加值提高到 +2。此能力替换正义灵光。",
          "Aura of Command grows to 30 feet and +2. This ability replaces aura of justice."))
        .AddRemoveFeatureOnApply(Guids.AuraCommandFeature)
        .AddComponent<AuraFeatureComponent>(c =>
          SetAuraBuff(c, BlueprintTool.GetRef<BlueprintBuffReference>(Guids.TotalCommandSelfBuff)))
        .Configure();
    }

    /// <summary>区域效果的 m_TargetType 是私有嵌套枚举，反射写入。P2-1：字段/枚举找不到时记 Error（不再静默）。</summary>
    private static void SetAreaTargetType(Kingmaker.Blueprints.BlueprintScriptableObject area, string name)
    {
      const string tag = "[RhodesDoctor] 区域目标类型反射写入失败（游戏更新可能改了私有成员名）";
      try
      {
        var t = area.GetType();
        var field = t.GetField("m_TargetType", BindingFlags.Instance | BindingFlags.NonPublic);
        var nested = t.GetNestedType("TargetType", BindingFlags.NonPublic);
        if (field == null || nested == null)
        {
          Main.Mod.Logger.Error($"{tag}：字段或枚举未找到（type={t.FullName}, 目标={name}）。");
          return;
        }
        field.SetValue(area, System.Enum.Parse(nested, name));
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error($"{tag}：{e}");
      }
    }

    private static void SetAuraBuff(AuraFeatureComponent component, BlueprintBuffReference buff)
    {
      const string tag = "[RhodesDoctor] 灵光 m_Buff 反射写入失败（游戏更新可能改了私有成员名）";
      try
      {
        var field = typeof(AuraFeatureComponent)
          .GetField("m_Buff", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
        {
          Main.Mod.Logger.Error($"{tag}：字段未找到。");
          return;
        }
        field.SetValue(component, buff);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error($"{tag}：{e}");
      }
    }

    // ── 圣疗 / 引导正能量：克隆原版后手术改按智力 ──
    private static void ClonePaladinFeatures()
    {
      try
      {
        // 圣疗资源：每日 等级÷2 + 智力调整值 次
        var lohRes = BlueprintTool.GetRef<BlueprintAbilityResourceReference>(Guids.LohResource);
        AbilityResourceConfigurator.New("RhodesDoctorLoHResource", Guids.LohResource)
          .SetLocalizedName(L10n.S("loh.resource.name", "圣疗次数", "Lay on Hands Charges"))
          // V6：圣疗次数 = 半级 + 智力调整值（旧版 IncreaseByLevel(class,2) 是 2×等级）。
          // 半级用"每 2 级 +1"表达（IncreaseByLevelStartPlusDivStep）。
          .SetMaxAmount(ResourceAmountBuilder.New(0)
            .IncreaseByLevelStartPlusDivStep(
              classes: new[] { Guids.Vanilla.PaladinClass },
              startingLevel: 0, startingBonus: 0, levelsPerStep: 2, bonusPerStep: 1)
            .IncreaseByStat(StatType.Intelligence))
          .Configure();

        // 只克隆 self / others（原版没有独立的 harm 能力）；换资源，harm 的 DC 改智力。
        // 注意：RemoveComponents 在"新建蓝图"上会在调用时对 null 的 Components 调 Where 而抛错，
        // 所以改用 CopyFrom(源, 谓词) 只挑要保留的组件，不调 RemoveComponents。
        AbilityConfigurator.New("RhodesDoctorLoHSelf", Guids.LohSelfAbility)
          .CopyFrom(Guids.Vanilla.LayOnHandsSelfAbility, c => !(c is AbilityResourceLogic))
          .AddAbilityResourceLogic(amount: 1, isSpendResource: true, requiredResource: lohRes)
          .Configure();
        AbilityConfigurator.New("RhodesDoctorLoHOthers", Guids.LohOthersAbility)
          .CopyFrom(Guids.Vanilla.LayOnHandsOthersAbility,
            c => !(c is AbilityResourceLogic) && !(c is ContextCalculateAbilityParams))
          .AddAbilityResourceLogic(amount: 1, isSpendResource: true, requiredResource: lohRes)
          .AddComponent<ContextCalculateAbilityParams>(c => c.StatType = StatType.Intelligence)
          .Configure();

        FeatureConfigurator.New("RhodesDoctorLoHFeature", Guids.LohFeature)
          // 图标：借原版"圣疗"特性自己的图（能力是原版克隆，图也用原版的，最连贯）。
          .SetIcon(Icons.Borrow(Guids.Vanilla.LayOnHandsFeature))
          .SetDisplayName(L10n.S("loh.name", "圣疗", "Lay on Hands"))
          .SetDescription(L10n.S("loh.desc",
            "每日次数与伤害不死生物的豁免 DC 改用智力调整值，其余同原版圣疗。",
            "Uses per day and the DC against undead use the Intelligence modifier; otherwise as the paladin ability."))
          .AddAbilityResources(resource: Guids.LohResource, restoreAmount: true)
          .AddFacts(new List<Blueprint<BlueprintUnitFactReference>> { Guids.LohSelfAbility, Guids.LohOthersAbility })
          .Configure();

        // 引导正能量：换 DC（智力），并把消耗改为博士自己的圣疗资源（V7：原版消耗 2 次原版圣疗，
        // 而博士没有原版圣疗，导致能力无法使用/资源池对不上）。设计文档未明写消耗方式，
        // 这里沿用原版"每次 2 次圣疗"的语义，指向本变体的 LohResource。
        AbilityConfigurator.New("RhodesDoctorChannelHeal", Guids.ChannelHealAbility)
          .CopyFrom(Guids.Vanilla.ChannelPositiveHealAbility,
            c => !(c is ContextCalculateAbilityParams) && !(c is AbilityResourceLogic))
          .AddComponent<ContextCalculateAbilityParams>(c => c.StatType = StatType.Intelligence)
          .AddAbilityResourceLogic(amount: 2, isSpendResource: true, requiredResource: lohRes)
          .Configure();
        AbilityConfigurator.New("RhodesDoctorChannelHarm", Guids.ChannelHarmAbility)
          .CopyFrom(Guids.Vanilla.ChannelPositiveHarmAbility,
            c => !(c is ContextCalculateAbilityParams) && !(c is AbilityResourceLogic))
          .AddComponent<ContextCalculateAbilityParams>(c => c.StatType = StatType.Intelligence)
          .AddAbilityResourceLogic(amount: 2, isSpendResource: true, requiredResource: lohRes)
          .Configure();

        FeatureConfigurator.New("RhodesDoctorChannelFeature", Guids.ChannelFeature)
          // 图标：借原版"引导正能量"特性自己的图。
          .SetIcon(Icons.Borrow(Guids.Vanilla.ChannelEnergyPaladinFeature))
          .SetDisplayName(L10n.S("channel.name", "引导正能量", "Channel Positive Energy"))
          .SetDescription(L10n.S("channel.desc",
            "豁免 DC 改用智力调整值；每次消耗 2 次圣疗次数，其余同原版。",
            "The DC uses the Intelligence modifier; each use consumes 2 lay-on-hands charges; otherwise unchanged."))
          .AddFacts(new List<Blueprint<BlueprintUnitFactReference>> { Guids.ChannelHealAbility, Guids.ChannelHarmAbility })
          .Configure();
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error($"[RhodesDoctor] 圣疗/引导正能量克隆失败（变体其余部分不受影响）：\n{e}");
      }
    }

    // ── 变体本体与成长表增删 ──
    private static void CreateArchetype()
    {
      ArchetypeConfigurator.New("RhodesDoctorArchetype", Guids.Archetype)
        .SetLocalizedName(L10n.S("archetype.name", "罗德岛博士", "Rhodes Island Doctor"))
        .SetLocalizedDescription(L10n.S("archetype.desc",
          "罗德岛的博士从石棺中醒来时，几乎失去了全部记忆，却保留着洞悉战局的头脑。博士很少亲自挥剑，而是以简短的指令调度同伴；战局需要时，博士会放任\"巴别塔的恶灵\"短暂苏醒，把全部智慧化为最锋利的战斗本能。",
          "The Doctor of Rhodes Island awoke from the sarcophagus with nearly all memories lost, yet with a mind that reads the battlefield like an open book. Rarely raising a blade in person, the Doctor directs allies with brief orders — and when the battle demands it, lets the Specter of Babel wake, turning wisdom into the sharpest fighting instinct."))
        .SetReplaceClassSkills(true)
        .SetClassSkills(new[]
        {
          StatType.SkillKnowledgeArcana,
          StatType.SkillKnowledgeWorld,
          StatType.SkillLoreReligion,
          StatType.SkillPerception,
          StatType.SkillPersuasion,
        })
        // 技能点 +2（圣武士基础 2 + 2 = 4），挂在变体链上而不是全局改职业
        .SetAddSkillPoints(2)
        // 法术书：用博士的法术书替换圣武士本体法术书。
        // 注意：SetRemoveSpellbook() 无参即 true，语义是"移除施法"，会把替换一起废掉；
        // 这里只做替换，绝不开移除。
        .SetReplaceSpellbook(BlueprintTool.GetRef<BlueprintSpellbookReference>(Guids.Spellbook))
        // 原版特性的 GUID 填在 Guids.Vanilla（BubblePrints 按名字查）
        .SetRemoveFeatures(Entries(
          (1, new[] { Guids.Vanilla.SmiteEvilFeature }),
          (2, new[] { Guids.Vanilla.DivineGraceFeature }),
          (2, new[] { Guids.Vanilla.LayOnHandsFeature }),
          (4, new[] { Guids.Vanilla.ChannelEnergyPaladinFeature }),
          (4, new[] { Guids.Vanilla.SmiteEvilAdditionalFeature }),
          (7, new[] { Guids.Vanilla.SmiteEvilAdditionalFeature }),
          (8, new[] { Guids.Vanilla.AuraOfResolveFeature }),
          (10, new[] { Guids.Vanilla.SmiteEvilAdditionalFeature }),
          (11, new[] { Guids.Vanilla.AuraOfJusticeFeature }),
          (13, new[] { Guids.Vanilla.SmiteEvilAdditionalFeature }),
          (16, new[] { Guids.Vanilla.SmiteEvilAdditionalFeature }),
          (19, new[] { Guids.Vanilla.SmiteEvilAdditionalFeature }),
          (20, new[] { Guids.Vanilla.HolyChampionFeature })))
        .SetAddFeatures(Entries(
          (1, new[] { Guids.FocusFireFeature, Guids.Mastery1Feature, Guids.SpellcastingFeature }),
          (2, new[] { Guids.CoolHeadFeature, Guids.LohFeature }),
          (4, new[] { Guids.AuraCommandFeature, Guids.ChannelFeature }),
          (5, new[] { Guids.Mastery2Feature }),
          (8, new[] { Guids.Stage1Feature }),
          (9, new[] { Guids.Mastery3Feature }),
          (11, new[] { Guids.TotalCommandFeature }),
          (12, new[] { Guids.Stage2Feature }),
          (13, new[] { Guids.Mastery4Feature }),
          (16, new[] { Guids.Stage3Feature }),
          (17, new[] { Guids.Mastery5Feature }),
          (20, new[] { Guids.CapstoneFeature })))
        .Configure();

      CharacterClassConfigurator.For(Guids.Vanilla.PaladinClass)
        .AddToArchetypes(Guids.Archetype)
        .Configure();
    }

    /// <summary>
    /// 成长表行：LevelEntryBuilder.New().AddEntry(level, ...).GetEntries()。
    /// AddEntry 的元素是 Blueprint&lt;BlueprintFeatureBaseReference&gt;（GUID 字符串隐式转换）。
    /// </summary>
    private static LevelEntry[] Entries(params (int level, string[] guids)[] rows)
      => rows.SelectMany(r => LevelEntryBuilder.New()
          .AddEntry(r.level, r.guids.Select(g => (Blueprint<BlueprintFeatureBaseReference>)g).ToArray())
          .GetEntries()).ToArray();
  }
}
