// 全部 64 个指令法术（规则文档第三至十三节）。
// 规格表驱动：每个法术一条 Spec，由 CreateSpell 按类型装配。
// 本版按真实签名补齐了范围组件（N9：按实际做法更新）：
//   · 点爆发：AddAbilityAoERadius（画圈）+ AddAbilityTargetsAround（选圈内目标）+ SetCanTargetPoint(true)；
//     自扫场景的区域/追踪法术（烟幕、钙质化、儿时舞乐、天灾、重构、文明、炮火、延时）不挂 TargetsAround，避免重复执行
//   · 直线：AddAbilityDeliverProjectile（真实投射物）+ SetCanTargetPoint(true)
//   · 复活：ContextActionMassRestore（群体完全恢复+限名复活，先活后死）
//   · 时空静止：CastSpell(原版 TimeStop)
//   · 王冠回响：Steal（至多2个分给友军）+ Purge（清除剩余）+ 成功者随机失去一个；筛选见 IsStealable
// 召唤与镜像已接线：墨染山河走原版召唤动作（单位蓝图见 SummonsSetup），
// 镜中流形走引擎自带的全量复制（自定义动作见 Actions.cs）。
// 画中世界按降级方案实现（v0.3.8/v0.3.9）：意志豁免失败 → 麻痹 + 隐形 2 轮，结束后原位返回（不再是 Kill 秒杀）。
// 伤害类法术（v0.3.8）：DealDamage 外层包 SavingThrow，halfIfSaved 才有豁免结果可读。
// 学派与描述符已进蓝图：SpellComponent.School / SpellDescriptorComponent.Descriptor 均为公开字段
//（Kingmaker.Blueprints.Classes.Spells），按 Desc 文本自动推断，法术专攻等按学派的效果因此生效。
// 本地化键全部写字面量（抓取器不展开拼接键）。
using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintCore.Actions.Builder;
using BlueprintCore.Actions.Builder.ContextEx;
using BlueprintCore.Blueprints.CustomConfigurators.UnitLogic.Abilities;
using BlueprintCore.Blueprints.CustomConfigurators.UnitLogic.Buffs;
using BlueprintCore.Blueprints.References; // BuffRefs / AbilityAreaEffectRefs（特效借用）
using BlueprintCore.Utils;
using BlueprintCore.Utils.Types;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.Enums.Damage;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Abilities.Components.Base;
using Kingmaker.UnitLogic.Abilities.Components.TargetCheckers;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.Utility;
using BlueprintCore.Conditions.Builder;
using BlueprintCore.Conditions.Builder.ContextEx; // IsEnemy()（钙质化分敌我，BlueprintCore 现成方法）
using RhodesDoctor.Components;

namespace RhodesDoctor
{
  internal static class SpellsSetup
  {
    // ── 类型 ──
    private enum Kind { Damage, Heal, Buff, Debuff, Special }

    private sealed class Bonus
    {
      public StatType Stat; public int Value; public ModifierDescriptor Descriptor;
      public Bonus(StatType s, int v, ModifierDescriptor d) { Stat = s; Value = v; Descriptor = d; }
    }

    private sealed class Spec
    {
      public string Key = "";
      public string Name = "";     // L10n.S 字面量调用（在 Specs 表里直接写）
      public string Desc = "";
      public int Ring;
      public UnitCommand.CommandType Cast = UnitCommand.CommandType.Standard;
      public AbilityRange Range = AbilityRange.Close;
      /// <summary>
      /// &gt;0 时改用"固定距离"（尺）：`AbilityRange` 只有近距/中距/极距这类**随等级变化**的档位，
      /// 而规则文档写的是绝对尺数（如战术标记"60 尺"）。填这里会自动 `Range = Custom` + `CustomRange`。
      /// </summary>
      public int RangeFeet;
      public bool SpecterOnly;
      public Kind Kind;
      public bool Friendly;             // Special 类法术里目标是友军的（镜像 / 重构 / 文明 / 永恒注视）
      // 范围（0 = 不挂范围组件）
      public int AoERadius;                                  // 点爆发半径（尺）
      public TargetType AoETarget = TargetType.Enemy;        // 点爆发影响谁
      public int LineLength;                                 // 直线长度（尺）
      public int LineWidth = 5;                              // 直线宽度（尺）
      // Damage / Heal 共用骰参数
      public DamageEnergyType Energy = DamageEnergyType.Fire;
      public PhysicalDamageForm Physical = PhysicalDamageForm.Slashing;
      public bool IsPhysical;
      public int DicePerStep = 1;   // 每 Step 级多少骰（0 = 固定值）
      public int Step = 1;
      public int DiceCap = 10;
      public DiceType Dice = DiceType.D6;
      public SavingThrowType Save = SavingThrowType.Reflex;
      // Buff 参数（只放固定加值；随等级成长的走下方 switch 的 rank 配置）
      public List<Bonus> Bonuses = new();
      public UnitCondition? Condition;
      public int Rounds = 3;
      public DurationRate Unit = DurationRate.Rounds; // 持续时间单位（默认轮；分钟用 Minutes）
      public bool SaveNone;   // Debuff 但无豁免（战术标记 / 火力侦察）
      public bool PerLevel;   // 持续 1 轮/级：duration 用 Rank，法术挂 BuffRank(l=>l)
    }

    // ── 学派 / 描述符 / 法术抗力 / 表头字段的显式元数据（键 = Spec.Key）──
    // SpellSchool / SpellDescriptor 是蓝图公开字段；DurationKey 用我们自带的本地化键
    // （官方 Duration 枚举没有"立即 / 固定 N 轮"，故单独一套）；Save 为空 = 不显示豁免字段。
    private sealed class Meta
    {
      public SpellSchool School = SpellSchool.None;
      public SpellDescriptor Descriptor = SpellDescriptor.None;
      public bool Sr = true;
      public string DurationKey;   // 形如 "rhodesdoctor.duration.instant"；null = 不设
      public SavingThrow? Save;    // null = 无豁免
    }

    private static readonly Dictionary<string, Meta> Metas = new()
    {
      // ─── 0 环 ───
      { "TacticalMark",         new Meta { School = SpellSchool.Divination,   Sr = false, DurationKey = "rhodesdoctor.duration.round1" } },
      { "FieldDressing",        new Meta { School = SpellSchool.Transmutation, Sr = false, DurationKey = "rhodesdoctor.duration.instant" } },
      { "BattlefieldSurvey",    new Meta { School = SpellSchool.Divination,   Sr = false, DurationKey = "rhodesdoctor.duration.minute1" } },
      { "BeaconProjection",     new Meta { School = SpellSchool.Illusion,     Sr = false, DurationKey = "rhodesdoctor.duration.minute10level" } },
      { "SuppressingShot",      new Meta { School = SpellSchool.Enchantment,  Descriptor = SpellDescriptor.MindAffecting, DurationKey = "rhodesdoctor.duration.round1level", Save = SavingThrow.WillNegates } },
      { "EmergencyExtraction",  new Meta { School = SpellSchool.Conjuration,  Sr = false, DurationKey = "rhodesdoctor.duration.instant" } },

      // ─── 1 环 ───
      { "HoldTheLine",          new Meta { School = SpellSchool.Abjuration,   DurationKey = "rhodesdoctor.duration.minute1level" } },
      { "CavalryCharge",        new Meta { School = SpellSchool.Transmutation, DurationKey = "rhodesdoctor.duration.minute1level" } },
      { "FrostPrayer",          new Meta { School = SpellSchool.Necromancy,   Descriptor = SpellDescriptor.Cold, DurationKey = "rhodesdoctor.duration.round1level", Save = SavingThrow.WillNegates } },
      { "EmergencyTreatment",   new Meta { School = SpellSchool.Conjuration,  DurationKey = "rhodesdoctor.duration.instant" } },
      { "FireRecon",            new Meta { School = SpellSchool.Divination,   Sr = false, DurationKey = "rhodesdoctor.duration.round1level" } },
      { "MedicalDrone",         new Meta { School = SpellSchool.Conjuration,  Sr = false, DurationKey = "rhodesdoctor.duration.round1level" } },

      // ─── 2 环 ───
      // ⚠ 乌拉去掉了 `MindAffecting` 描述符（作者 10-11：施放时会跳"对乌拉！免疫"）——
      //   这是个**有益**法术，挂"影响心灵"会被目标的（心灵/强迫类）免疫挡下。
      { "Ura",                  new Meta { School = SpellSchool.Enchantment,  DurationKey = "rhodesdoctor.duration.minute1level" } },
      { "UnstablePlasma",       new Meta { School = SpellSchool.Necromancy,   DurationKey = "rhodesdoctor.duration.round1level" } },
      { "AntiGravity",          new Meta { School = SpellSchool.Transmutation, DurationKey = "rhodesdoctor.duration.round1level", Save = SavingThrow.WillNegates } },
      { "Smokescreen",          new Meta { School = SpellSchool.Conjuration,  Sr = false, DurationKey = "rhodesdoctor.duration.round1level" } },
      { "DelayedBlast",         new Meta { School = SpellSchool.Evocation,    Descriptor = SpellDescriptor.Fire, DurationKey = "rhodesdoctor.duration.round1level", Save = SavingThrow.ReflexHalf } },
      { "Silencer",             new Meta { School = SpellSchool.Abjuration,   DurationKey = "rhodesdoctor.duration.round1level", Save = SavingThrow.WillNegates } },

      // ─── 3 环 ───
      { "Overloading",          new Meta { School = SpellSchool.Transmutation, DurationKey = "rhodesdoctor.duration.round1level" } },
      { "SwordRain",            new Meta { School = SpellSchool.Evocation,    DurationKey = "rhodesdoctor.duration.instant", Save = SavingThrow.ReflexHalf } },
      { "Calcification",        new Meta { School = SpellSchool.Transmutation, Sr = false, DurationKey = "rhodesdoctor.duration.round1level" } },
      { "LockOfTime",           new Meta { School = SpellSchool.Transmutation, DurationKey = "rhodesdoctor.duration.round1level", Save = SavingThrow.WillNegates } },
      { "ScorchingBreath",      new Meta { School = SpellSchool.Evocation,    Descriptor = SpellDescriptor.Fire, DurationKey = "rhodesdoctor.duration.instant", Save = SavingThrow.ReflexHalf } },
      { "Execution",            new Meta { School = SpellSchool.Transmutation, Sr = false, DurationKey = "rhodesdoctor.duration.round1level" } },

      // ─── 4 环 ───
      { "Sanctuary",            new Meta { School = SpellSchool.Abjuration,   DurationKey = "rhodesdoctor.duration.round1level" } },
      { "Volcano",              new Meta { School = SpellSchool.Evocation,    Descriptor = SpellDescriptor.Fire, DurationKey = "rhodesdoctor.duration.instant", Save = SavingThrow.ReflexHalf } },
      { "Dragnet",              new Meta { School = SpellSchool.Conjuration,  DurationKey = "rhodesdoctor.duration.instant", Save = SavingThrow.FortNegates } },
      { "Nightterror",          new Meta { School = SpellSchool.Enchantment,  Descriptor = SpellDescriptor.Fear | SpellDescriptor.MindAffecting, DurationKey = "rhodesdoctor.duration.round1level", Save = SavingThrow.WillPartial } },
      { "SwiftBreach",          new Meta { School = SpellSchool.Transmutation, Sr = false, DurationKey = "rhodesdoctor.duration.round1" } },
      { "Flashbang",            new Meta { School = SpellSchool.Evocation,    DurationKey = "rhodesdoctor.duration.round1level", Save = SavingThrow.ReflexPartial } },

      // ─── 5 环 ───
      { "Chimera",              new Meta { School = SpellSchool.Transmutation, DurationKey = "rhodesdoctor.duration.round1level" } },
      { "Mon3tr",               new Meta { School = SpellSchool.Conjuration,  DurationKey = "rhodesdoctor.duration.instant" } },
      { "Ironcast",             new Meta { School = SpellSchool.Abjuration,   DurationKey = "rhodesdoctor.duration.round1level" } },
      { "InkedMountains",       new Meta { School = SpellSchool.Illusion,     Sr = false, DurationKey = "rhodesdoctor.duration.round1level" } },
      { "FrozenDomain",         new Meta { School = SpellSchool.Evocation,    Descriptor = SpellDescriptor.Cold, DurationKey = "rhodesdoctor.duration.round1level", Save = SavingThrow.ReflexPartial } },
      { "RoyalCharge",          new Meta { School = SpellSchool.Transmutation, Sr = false, DurationKey = "rhodesdoctor.duration.instant", Save = SavingThrow.ReflexPartial } },

      // ─── 6 环 ───
      { "TidalElegy",           new Meta { School = SpellSchool.Transmutation, Sr = false, DurationKey = "rhodesdoctor.duration.round1level" } },
      { "FoxfireDance",         new Meta { School = SpellSchool.Enchantment,  Descriptor = SpellDescriptor.MindAffecting, DurationKey = "rhodesdoctor.duration.round1level", Save = SavingThrow.WillNegates } },
      { "DrunkenDream",         new Meta { School = SpellSchool.Enchantment,  Descriptor = SpellDescriptor.Sleep | SpellDescriptor.MindAffecting, DurationKey = "rhodesdoctor.duration.round1level", Save = SavingThrow.WillNegates } },
      { "BlackSnakeFlame",      new Meta { School = SpellSchool.Evocation,    Descriptor = SpellDescriptor.Fire, DurationKey = "rhodesdoctor.duration.round1level", Save = SavingThrow.ReflexHalf } },
      { "ArtilleryBarrage",     new Meta { School = SpellSchool.Evocation,    DurationKey = "rhodesdoctor.duration.round3", Save = SavingThrow.ReflexHalf } },
      { "SinPunishment",        new Meta { School = SpellSchool.Necromancy,   DurationKey = "rhodesdoctor.duration.round1level", Save = SavingThrow.WillNegates } },

      // ─── 7 环 ───
      { "Twilight",             new Meta { School = SpellSchool.Evocation,    Descriptor = SpellDescriptor.Fire, DurationKey = "rhodesdoctor.duration.instant", Save = SavingThrow.ReflexHalf } },
      { "MirrorFlow",           new Meta { School = SpellSchool.Illusion,     Sr = false, DurationKey = "rhodesdoctor.duration.round1level" } },
      { "FinalSnipe",           new Meta { School = SpellSchool.Divination,   DurationKey = "rhodesdoctor.duration.instant", Save = SavingThrow.ReflexHalf } },
      { "ChainDetonation",      new Meta { School = SpellSchool.Necromancy,   DurationKey = "rhodesdoctor.duration.round1level", Save = SavingThrow.ReflexHalf } },
      { "ThunderousJudgment",   new Meta { School = SpellSchool.Evocation,    Descriptor = SpellDescriptor.Electricity, DurationKey = "rhodesdoctor.duration.instant", Save = SavingThrow.ReflexPartial } },
      { "ReturnToAbyss",        new Meta { School = SpellSchool.Necromancy,   Sr = false, DurationKey = "rhodesdoctor.duration.round1level" } },

      // ─── 8 环 ───
      { "PaintedWorld",         new Meta { School = SpellSchool.Illusion,     DurationKey = "rhodesdoctor.duration.round1level", Save = SavingThrow.WillNegates } },
      { "BlazingSunBow",        new Meta { School = SpellSchool.Evocation,    DurationKey = "rhodesdoctor.duration.instant", Save = SavingThrow.ReflexPartial } },
      { "TidalVigil",           new Meta { School = SpellSchool.Abjuration,   DurationKey = "rhodesdoctor.duration.minute1level" } },
      { "ShadowRealm",          new Meta { School = SpellSchool.Illusion,     DurationKey = "rhodesdoctor.duration.round1level", Save = SavingThrow.WillPartial } },
      { "HarvestYears",         new Meta { School = SpellSchool.Conjuration,  DurationKey = "rhodesdoctor.duration.round3" } },
      { "BannerStands",         new Meta { School = SpellSchool.Enchantment,  DurationKey = "rhodesdoctor.duration.round1level" } },

      // ─── 9 环 ───
      { "ShadowBladeDraw",      new Meta { School = SpellSchool.Necromancy,   Descriptor = SpellDescriptor.Death, Sr = false, DurationKey = "rhodesdoctor.duration.instant", Save = SavingThrow.FortPartial } },
      { "Chronostasis",         new Meta { School = SpellSchool.Transmutation, Sr = false, DurationKey = "rhodesdoctor.duration.instant" } },
      { "Cloudpiercer",         new Meta { School = SpellSchool.Evocation,    Sr = false, DurationKey = "rhodesdoctor.duration.instant", Save = SavingThrow.ReflexHalf } },
      { "EarthFury",            new Meta { School = SpellSchool.Evocation,    Sr = false, DurationKey = "rhodesdoctor.duration.instant", Save = SavingThrow.ReflexPartial } },
      { "Reconstruction",       new Meta { School = SpellSchool.Conjuration,  DurationKey = "rhodesdoctor.duration.instant" } },
      { "TacticianOfOld",       new Meta { School = SpellSchool.Transmutation, Sr = false, DurationKey = "rhodesdoctor.duration.round1level" } },

      // ─── 10 环（合书）───
      { "Civilization",         new Meta { School = SpellSchool.Conjuration,  DurationKey = "rhodesdoctor.duration.instant" } },
      { "EternalGaze",          new Meta { School = SpellSchool.Abjuration,   DurationKey = "rhodesdoctor.duration.minute1level" } },
      { "CrownEcho",            new Meta { School = SpellSchool.Enchantment,  Descriptor = SpellDescriptor.MindAffecting, DurationKey = "rhodesdoctor.duration.round1level", Save = SavingThrow.WillPartial } },
      { "Catastrophe",          new Meta { School = SpellSchool.Evocation,    DurationKey = "rhodesdoctor.duration.minute1", Save = SavingThrow.ReflexHalf } },
    };

    // ── 法术表（顺序 = Guids.Spell(i) 的下标；0–9 环各 6 个，10 环 4 个）──
    // 注意：Name / Desc 必须是 L10n.S("字面键", ...) 的调用结果，抓取器只认字面量。
    private static readonly Spec[] Specs =
    {
      // ─── 0 环（0–5）───
      // 0 环整组：**施法时间显式写 Standard**（此前靠 `Spec.Cast` 的默认值，数据表上看不出是"标准动作"）。
      new Spec { Key="TacticalMark", Ring=0, Cast=UnitCommand.CommandType.Standard, Range=AbilityRange.Custom, RangeFeet=60, Kind=Kind.Debuff, SaveNone=true, Rounds=1,
        Name=L10n.S("spell.TacticalMark.name", "战术标记", "Tactical Mark"),
        Desc=L10n.S("spell.TacticalMark.desc", "学派：预言；距离：60 尺；目标：一名生物；持续：1 轮；豁免：无。你与友军对其的下一次攻击检定 +1（洞察）。", "Divination; 60 ft.; one creature; 1 round; no save. You and your allies gain a +1 insight bonus on the next attack roll against it this round.") },
      new Spec { Key="FieldDressing", Ring=0, Cast=UnitCommand.CommandType.Standard, Range=AbilityRange.Touch, Kind=Kind.Heal, DicePerStep=0,
        Name=L10n.S("spell.FieldDressing.name", "医疗敷料", "Field Dressing"),
        Desc=L10n.S("spell.FieldDressing.desc", "学派：变化（治疗）；距离：接触；持续：立即。目标恢复 1 点生命；24 小时内受过此治疗者不再受益。", "Transmutation (healing); touch; instantaneous. The target regains 1 hit point; cannot affect a creature healed by it in the last 24 hours.") },
      new Spec { Key="BattlefieldSurvey", Ring=0, Cast=UnitCommand.CommandType.Standard, Range=AbilityRange.Personal, Kind=Kind.Buff, Rounds=10,
        Bonuses={ new Bonus(StatType.SkillPerception, 2, ModifierDescriptor.Insight) },
        Name=L10n.S("spell.BattlefieldSurvey.name", "战场测绘", "Battlefield Survey"),
        Desc=L10n.S("spell.BattlefieldSurvey.desc", "学派：预言；距离：个人；目标：你；持续时间：1 分钟。你获得察觉检定 +2 洞察加值。", "Divination; personal; 1 minute. +2 insight on Perception checks.") },
      new Spec { Key="BeaconProjection", Ring=0, Cast=UnitCommand.CommandType.Standard, Range=AbilityRange.Custom, RangeFeet=30, Kind=Kind.Special,
        Name=L10n.S("spell.BeaconProjection.name", "信标投射", "Beacon Projection"),
        Desc=L10n.S("spell.BeaconProjection.desc", "学派：幻术（光）；距离：30 尺；持续：10 分钟/级。悬浮信标照亮 20 尺半径。", "Illusion (light); 30 ft.; 10 min./level. A floating beacon lights a 20-ft. radius.") },
      new Spec { Key="SuppressingShot", Ring=0, Cast=UnitCommand.CommandType.Standard, Range=AbilityRange.Custom, RangeFeet=30, Kind=Kind.Debuff, PerLevel=true, Save=SavingThrowType.Will,
        Name=L10n.S("spell.SuppressingShot.name", "牵制射击", "Suppressing Shot"),
        Desc=L10n.S("spell.SuppressingShot.desc", "学派：惑控（影响心灵）；距离：30 尺；意志无效。目标承受 –1 罚值于其下一次攻击检定；若目标在此轮攻击除你以外的生物，该罚值变为 –2。——克洛丝", "Enchantment (mind-affecting); 30 ft.; Will negates. –1 on the target's next attack roll, or –2 if it attacks anyone but you this round. (Kroos)") },
      new Spec { Key="EmergencyExtraction", Ring=0, Cast=UnitCommand.CommandType.Standard, Range=AbilityRange.Touch, Kind=Kind.Special, Friendly=true,
        Name=L10n.S("spell.EmergencyExtraction.name", "紧急撤离", "Emergency Extraction"),
        Desc=L10n.S("spell.EmergencyExtraction.desc", "学派：咒法（传送）；距离：接触；持续时间：立即。目标移动至 10 尺内可见的空格（实现为永远落在施法者身后最近空地）。该移动不引发借机攻击，不能穿越实体障碍。", "Conjuration (teleportation); touch; instantaneous. The target moves up to 10 ft. to a visible square (implemented as the nearest open square behind the caster). Provokes no attacks of opportunity.") },

      // ─── 1 环（6–11）───
      // 坚守阵地（10-11）：改为"**持续的区域**"（Kind.Special，半径 30 的圈；见 ApplySpecial 的 HoldTheLine 分支）。
      new Spec { Key="HoldTheLine", Ring=1, Cast=UnitCommand.CommandType.Swift, Range=AbilityRange.Personal, Kind=Kind.Special, Friendly=true, PerLevel=true, Unit=DurationRate.Minutes, AoERadius=30, AoETarget=TargetType.Ally,
        Bonuses={ new Bonus(StatType.AC, 2, ModifierDescriptor.Insight), new Bonus(StatType.SaveFortitude, 1, ModifierDescriptor.Insight),
          new Bonus(StatType.SaveReflex, 1, ModifierDescriptor.Insight), new Bonus(StatType.SaveWill, 1, ModifierDescriptor.Insight) },
        Name=L10n.S("spell.HoldTheLine.name", "坚守阵地", "Hold the Line"),
        Desc=L10n.S("spell.HoldTheLine.desc", "学派：防护；迅捷动作；以你为中心，30 尺爆发内全部友军；1 分钟/级。AC +2、豁免 +1（均洞察）。——星熊", "Abjuration; swift; personal; all allies in a 30-ft. burst centered on you; 1 min./level. +2 insight to AC, +1 insight on saves. (Hoshiguma)") },
      new Spec { Key="CavalryCharge", Ring=1, Cast=UnitCommand.CommandType.Swift, Range=AbilityRange.Personal, Kind=Kind.Buff, PerLevel=true, Unit=DurationRate.Minutes, AoERadius=30, AoETarget=TargetType.Ally,
        Name=L10n.S("spell.CavalryCharge.name", "骑兵突进", "Cavalry Charge"),
        Desc=L10n.S("spell.CavalryCharge.desc", "学派：变化；迅捷动作；以你为中心，30 尺爆发内全部友军；1 分钟/级。速度 +10 尺（10 级起 +20；不具类型，可与加速术叠加），对借机攻击的 AC +4（闪避）。——格拉尼", "Transmutation; swift; personal; all allies in a 30-ft. burst centered on you; 1 min./level. +10 ft. speed (+20 at 10th; untyped, stacks with haste), +4 dodge to AC vs attacks of opportunity. (Grani)") },
      new Spec { Key="FrostPrayer", Ring=1, Cast=UnitCommand.CommandType.Swift, Kind=Kind.Debuff, PerLevel=true, Save=SavingThrowType.Will,
        Bonuses={ new Bonus(StatType.AC, -2, ModifierDescriptor.UntypedStackable),
          new Bonus(StatType.SaveFortitude, -2, ModifierDescriptor.UntypedStackable),
          new Bonus(StatType.SaveReflex, -2, ModifierDescriptor.UntypedStackable),
          new Bonus(StatType.SaveWill, -2, ModifierDescriptor.UntypedStackable) },
        Name=L10n.S("spell.FrostPrayer.name", "寒霜祷言", "Frost Prayer"),
        Desc=L10n.S("spell.FrostPrayer.desc", "学派：死灵（寒冷）；迅捷动作；一名敌人；1 轮/级；意志无效。AC 与所有豁免 −2。——初雪", "Necromancy (cold); swift; one enemy; 1 round/level; Will negates. –2 to AC and all saves. (Pramanix)") },
      new Spec { Key="EmergencyTreatment", Ring=1, Cast=UnitCommand.CommandType.Move, Kind=Kind.Special, Friendly=true, DicePerStep=1, Step=2, DiceCap=10,
        Name=L10n.S("spell.EmergencyTreatment.name", "紧急救治", "Emergency Treatment"),
        Desc=L10n.S("spell.EmergencyTreatment.desc", "学派：咒法（治疗）；移动动作；近距；一名友军。目标获得博士圣疗的治疗效果（可附加慈悲）；不能伤害不死生物。——闪灵", "Conjuration (healing); move; close; one ally. The target gains the Doctor's lay on hands healing (mercies apply); cannot harm undead. (Shining)") },
      new Spec { Key="FireRecon", Ring=1, Cast=UnitCommand.CommandType.Swift, Range=AbilityRange.Medium, Kind=Kind.Debuff, SaveNone=true, PerLevel=true,
        Name=L10n.S("spell.FireRecon.name", "火力侦察", "Fire Recon"),
        Desc=L10n.S("spell.FireRecon.desc", "学派：预言；迅捷动作；中距；一名敌人；1 轮/级。友军攻击目标时忽略其隐形、镜影或掩蔽带来的失手几率。——克洛丝", "Divination; swift; medium; one enemy; 1 round/level. Allies ignore the target's miss chance from invisibility, mirror images, or concealment. (Kroos)") },
      // 医疗无人机：按定案简化为"跟随友军的增益"（不做可攻击的实体单位）。
      new Spec { Key="MedicalDrone", Ring=1, Kind=Kind.Buff, PerLevel=true,
        Name=L10n.S("spell.MedicalDrone.name", "医疗无人机", "Medical Drone"),
        Desc=L10n.S("spell.MedicalDrone.desc", "学派：咒法（创造、治疗）；标准动作；近距；1 轮/级。医疗无人机跟随目标友军，每轮开始时为其恢复 1d6 + 施法者等级（至多 +5）生命。——赫默", "Conjuration (creation, healing); standard; close; 1 round/level. A medical drone follows the target ally, healing 1d6 + caster level (max +5) at the start of each round. (Silence)") },

      // ─── 2 环（12–17）───
      new Spec { Key="Ura", Ring=2, Cast=UnitCommand.CommandType.Swift, Range=AbilityRange.Personal, Kind=Kind.Buff, PerLevel=true, Unit=DurationRate.Minutes, AoERadius=30, AoETarget=TargetType.Ally,
        Name=L10n.S("spell.Ura.name", "乌拉！", "Ura!"),
        Desc=L10n.S("spell.Ura.desc", "学派：惑控（影响心灵）；迅捷动作；以你为中心，30 尺爆发内全部友军；1 分钟/级。结束动摇、恐惧与恐慌；攻击与武器伤害 +1（10 级起 +2，不具类型）。——凛冬", "Enchantment; swift; personal; all allies in a 30-ft. burst centered on you; 1 min./level. Ends fear; +1 untyped on attack and weapon damage (+2 at 10th). (Zima)") },
      new Spec { Key="UnstablePlasma", Ring=2, Cast=UnitCommand.CommandType.Swift, Kind=Kind.Buff, PerLevel=true,
        Bonuses={ new Bonus(StatType.AdditionalAttackBonus, 2, ModifierDescriptor.UntypedStackable) },
        Name=L10n.S("spell.UnstablePlasma.name", "不稳定血浆", "Unstable Plasma"),
        Desc=L10n.S("spell.UnstablePlasma.desc", "学派：死灵；迅捷动作；一名友军；1 轮/级。攻击 +2、武器伤害 +2（不具类型，伤害每 4 级再 +1）；代价：每轮开始失去 2 点生命（不低于 1）。——华法琳", "Necromancy; swift; one ally; 1 round/level. +2 untyped attack and damage (+1 per 4 levels); loses 2 hp each round (not below 1). (Warfarin)") },
      new Spec { Key="AntiGravity", Ring=2, Kind=Kind.Debuff, Condition=UnitCondition.Slowed, PerLevel=true, Save=SavingThrowType.Will,
        Name=L10n.S("spell.AntiGravity.name", "反重力", "Anti-Gravity"),
        Desc=L10n.S("spell.AntiGravity.desc", "学派：变化；标准动作；一名敌人；1 轮/级；意志无效。目标陷入缓慢（如同缓慢术）。——安洁莉娜", "Transmutation; standard; one enemy; 1 round/level; Will negates. The target is slowed, as the slow spell. (Angelina)") },
      new Spec { Key="Smokescreen", Ring=2, Range=AbilityRange.Medium, Kind=Kind.Special, Rounds=1, AoERadius=20, AoETarget=TargetType.Any,
        Name=L10n.S("spell.Smokescreen.name", "烟幕掩护", "Smokescreen"),
        Desc=L10n.S("spell.Smokescreen.desc", "学派：咒法（创造）；标准动作；中距；半径 20 尺；1 轮/级。区域提供隐蔽（20% 失手几率）；友军可正常视物。——杰西卡", "Conjuration (creation); standard; medium; 20-ft. radius; 1 round/level. Concealment (20% miss chance); allies see normally. (Jessica)") },
      new Spec { Key="DelayedBlast", Ring=2, Range=AbilityRange.Medium, Kind=Kind.Special, AoERadius=10, AoETarget=TargetType.Enemy, PerLevel=true,
        Name=L10n.S("spell.DelayedBlast.name", "延时爆破", "Delayed Blast"),
        Desc=L10n.S("spell.DelayedBlast.desc", "学派：塑能（火焰）；标准动作；中距；1 轮/级或直到触发；反射减半。在地面放置炸弹：每轮开始时，炸弹 10 尺内有敌人就爆炸（10 尺爆发 2d6 火焰 + 2d6 轰击）；你也可以用自由动作手动引爆。——W", "Evocation (fire); standard; medium; 1 round/level or until triggered; Reflex half. Place a bomb: at the start of each round it explodes if an enemy is within 10 ft. (10-ft. burst, 2d6 fire + 2d6 bludgeoning); you may also detonate it as a free action. (W)") },
      new Spec { Key="Silencer", Ring=2, Kind=Kind.Special, Rounds=1, Save=SavingThrowType.Will,
        Name=L10n.S("spell.Silencer.name", "缄默", "Silencer"),
        Desc=L10n.S("spell.Silencer.desc", "学派：防护；标准动作；一名敌人；1 轮/级；意志无效。目标不能施展法术或使用类法术、超自然能力；不妨碍被动能力。——拉普兰德", "Abjuration; standard; one enemy; 1 round/level; Will negates. The target cannot cast spells or use spell-like or supernatural abilities. (Lappland)") },

      // ─── 3 环（18–23）───
      new Spec { Key="Overloading", Ring=3, Cast=UnitCommand.CommandType.Swift, Kind=Kind.Buff, PerLevel=true, AoERadius=20, AoETarget=TargetType.Ally,
        Bonuses={ new Bonus(StatType.AdditionalAttackBonus, 1, ModifierDescriptor.UntypedStackable),
          new Bonus(StatType.AC, 1, ModifierDescriptor.Dodge),
          new Bonus(StatType.SaveReflex, 1, ModifierDescriptor.Dodge),
          new Bonus(StatType.Speed, 30, ModifierDescriptor.UntypedStackable) },
        Name=L10n.S("spell.Overloading.name", "过载模式", "Overloading Mode"),
        Desc=L10n.S("spell.Overloading.desc", "学派：变化；迅捷动作；近距内选一点为中心，20 尺爆发内全部友军；1 轮/级。全回合攻击额外攻击一次（全额 BAB，可与加速术、速度武器叠加）；攻击 +1（不具类型）；AC、反射 +1（闪避）；速度 +30 尺（不具类型）。——能天使", "Transmutation; swift; close; all allies in a 20-ft. burst centered on a point within close range; 1 round/level. One extra attack at full BAB (stacks with haste and speed weapons); +1 untyped attack; +1 dodge AC/Reflex; +30-ft. untyped speed. (Exusiai)") },
      new Spec { Key="SwordRain", Ring=3, Kind=Kind.Special, IsPhysical=true, Physical=PhysicalDamageForm.Slashing, DicePerStep=1, Step=2, DiceCap=10, AoERadius=10,
        Name=L10n.S("spell.SwordRain.name", "剑雨", "Sword Rain"),
        Desc=L10n.S("spell.SwordRain.desc", "学派：塑能；标准动作；近距；半径 10 尺爆发（只影响敌人）；反射减半。每 2 级 1d6 挥砍（最多 10d6）；失败者恍惚 1 轮。——德克萨斯", "Evocation; standard; close; 10-ft. burst (enemies only); Reflex half. 1d6 slashing per 2 levels (max 10d6); failures are dazed 1 round. (Texas)") },
      new Spec { Key="Calcification", Ring=3, Kind=Kind.Special, PerLevel=true, AoERadius=20, AoETarget=TargetType.Any,
        Name=L10n.S("spell.Calcification.name", "钙质化", "Calcification"),
        Desc=L10n.S("spell.Calcification.desc", "学派：变化；标准动作；半径 20 尺；1 轮/级；无豁免。区域内敌人速度减半、豁免 −2；友军每轮开始恢复 1d8 生命；敌方效果离开即结束。——塞雷娅", "Transmutation; standard; 20-ft. radius; 1 round/level; no save. Enemies: half speed, –2 saves; allies regain 1d8 hp each round. (Saria)") },
      new Spec { Key="LockOfTime", Ring=3, Range=AbilityRange.Medium, Kind=Kind.Debuff, PerLevel=true, Save=SavingThrowType.Will, AoERadius=20,
        Name=L10n.S("spell.LockOfTime.name", "时之锁", "Lock of Time"),
        Desc=L10n.S("spell.LockOfTime.desc", "学派：变化；标准动作；中距；半径 20 尺爆发；1 轮/级；意志无效。区域内敌人 AC、反射豁免与攻击检定各 −1，且每轮只能做一个动作（不能做整轮动作）；每轮开始可再次豁免，成功即解除。——莫斯提马", "Transmutation; standard; medium; 20-ft. burst; 1 round/level; Will negates. Enemies in the area take −1 on AC, Reflex saves and attack rolls, and can take only a single move or standard action each round (no full-round actions); a new save each round ends it. (Mostima)") },
      new Spec { Key="ScorchingBreath", Ring=3, Kind=Kind.Special, DicePerStep=1, Step=1, DiceCap=10, Energy=DamageEnergyType.Fire, LineLength=60,
        Name=L10n.S("spell.ScorchingBreath.name", "灼烧", "Scorching Breath"),
        Desc=L10n.S("spell.ScorchingBreath.desc", "学派：塑能（火焰）；标准动作；60 尺直线；反射减半。直线内敌人每级 1d6 火焰（最多 10d6）；失败者火焰抗力 −10，持续 1 轮/级。——伊芙利特", "Evocation (fire); standard; 60-ft. line; Reflex half. 1d6 fire per level (max 10d6); failures lose 10 fire resistance for 1 round/level. (Ifrit)") },
      new Spec { Key="Execution", Ring=3, Cast=UnitCommand.CommandType.Swift, Range=AbilityRange.Personal, Kind=Kind.Special, Rounds=1, SpecterOnly=true,
        Name=L10n.S("spell.Execution.name", "处决", "Execution"),
        Desc=L10n.S("spell.Execution.desc", "【恶灵】学派：变化；迅捷动作；个人；1 轮/级或直到触发。下一次近战命中生命不高于一半的目标时，武器伤害骰翻倍；触发后法术结束。——红", "[Specter] Transmutation; swift; personal; 1 round/level or until triggered. Your next melee hit against a target at half hp or less doubles its weapon damage dice. (Projekt Red)") },

      // ─── 4 环（24–29）───
      new Spec { Key="Sanctuary", Ring=4, Cast=UnitCommand.CommandType.Swift, Range=AbilityRange.Personal, Kind=Kind.Buff, PerLevel=true, AoERadius=30, AoETarget=TargetType.Ally,
        Name=L10n.S("spell.Sanctuary.name", "圣域", "Sanctuary"),
        Desc=L10n.S("spell.Sanctuary.desc", "学派：防护；迅捷动作；以你为中心，30 尺爆发内全部友军；1 轮/级。对抗法术与类法术能力的豁免 +4（神圣）；火、寒、电、酸、音波抗力各 10，可与其他来源叠加。——夜莺", "Abjuration; swift; personal; all allies in a 30-ft. burst centered on you; 1 round/level. +4 sacred on saves vs spells and spell-like abilities; resistance 10 to the five energies, stacking with other sources. (Nightingale)") },
      new Spec { Key="Volcano", Ring=4, Range=AbilityRange.Medium, Kind=Kind.Special, DicePerStep=1, Step=2, DiceCap=10, Energy=DamageEnergyType.Fire, AoERadius=20,
        Name=L10n.S("spell.Volcano.name", "火山", "Volcano"),
        Desc=L10n.S("spell.Volcano.desc", "学派：塑能（火焰）；标准动作；中距；半径 20 尺；立即 + 余焰 2 轮；反射减半。立即每 2 级 1d6（最多 10d6），之后 2 轮每轮每 4 级 1d6（最多 5d6）。——艾雅法拉", "Evocation (fire); standard; medium; 20-ft. burst; Reflex half. 1d6 per 2 levels now (max 10d6), then 1d6 per 4 levels (max 5d6) for 2 rounds. (Eyjafjalla)") },
      new Spec { Key="Dragnet", Ring=4, Range=AbilityRange.Medium, Kind=Kind.Special, Save=SavingThrowType.Fortitude, AoERadius=20, AoETarget=TargetType.Enemy,
        Name=L10n.S("spell.Dragnet.name", "牵引", "Dragnet"),
        Desc=L10n.S("spell.Dragnet.desc", "学派：咒法（传送）；标准动作；中距；半径 20 尺内的敌人；强韧无效。失败目标被传送至法术点附近并倒地；不能送入危险地形。——歌蕾蒂娅", "Conjuration (teleportation); standard; medium; enemies in a 20-ft. burst; Fortitude negates. Failing targets are teleported near the spell point and knocked prone. (Gladiia)") },
      new Spec { Key="Nightterror", Ring=4, Range=AbilityRange.Medium, Kind=Kind.Special, Save=SavingThrowType.Will,
        Name=L10n.S("spell.Nightterror.name", "梦魇", "Nightterror"),
        Desc=L10n.S("spell.Nightterror.desc", "学派：惑控（恐惧、影响心灵）；标准动作；中距；一名敌人；意志部分通过。失败：恐慌 2 轮后动摇；成功：动摇 1 轮。——夜魔", "Enchantment (fear, mind-affecting); standard; medium; one enemy; Will partial. Failure: panicked 2 rounds then shaken; success: shaken 1 round. (Nightmare)") },
      new Spec { Key="SwiftBreach", Ring=4, Cast=UnitCommand.CommandType.Swift, Range=AbilityRange.Personal, Kind=Kind.Special, Rounds=1, SpecterOnly=true,
        Name=L10n.S("spell.SwiftBreach.name", "迅捷突破", "Swift Breach"),
        Desc=L10n.S("spell.SwiftBreach.desc", "【恶灵】学派：变化；迅捷动作；个人；加速 3 轮。本轮内直到你下一次近战命中为止，你不引发借机攻击（包括移动）；下一次近战命中额外 3d6。加速：速度提升、全力攻击多一次攻击（不含加速术其他加值，可与加速术叠加）。——风笛", "[Specter] Transmutation; swift; personal. Haste-like acceleration for 3 rounds; until your next melee hit this round you provoke no attacks of opportunity (including movement); your next melee hit deals +3d6. (Bagpipe)") },
      new Spec { Key="Flashbang", Ring=4, Range=AbilityRange.Medium, Kind=Kind.Special, DicePerStep=1, Step=1, DiceCap=10, Save=SavingThrowType.Reflex,
        // G14：2.8.7 没有锥形投递组件，用 15 尺点爆发实现；文字已同步为 15 尺爆发（代码效果不变）。
        AoERadius=15,
        Name=L10n.S("spell.Flashbang.name", "闪光弹", "Flashbang"),
        Desc=L10n.S("spell.Flashbang.desc", "学派：塑能（光）；标准动作；中距内选一点为中心，15 尺爆发；反射部分通过。失败：目盲 1 轮后目眩；成功：目眩 1 轮；失败的隐形生物显形 1 轮。——灰烬", "Evocation (light); standard; a 15-ft. burst centered on a point within medium range; Reflex partial. Failure: blinded 1 round then dazzled; success: dazzled 1 round. Invisible creatures that fail become visible for 1 round. (Ash)") },

      // ─── 5 环（30–35）───
      new Spec { Key="Chimera", Ring=5, Cast=UnitCommand.CommandType.Swift, Kind=Kind.Buff, PerLevel=true,
        Bonuses={ new Bonus(StatType.AdditionalAttackBonus, 4, ModifierDescriptor.UntypedStackable) },
        Name=L10n.S("spell.Chimera.name", "奇美拉", "Chimera"),
        Desc=L10n.S("spell.Chimera.desc", "学派：变化；迅捷动作；一名友军；1 轮/级。攻击 +4（不具类型，可与集火叠加）；武器伤害 + 你等级的一半；攻击无视所有伤害减免。——阿米娅", "Transmutation; swift; one ally; 1 round/level. +4 untyped attack (stacks with Focus Fire); weapon damage + half your level; attacks ignore all DR. (Amiya)") },
      new Spec { Key="Mon3tr", Ring=5, Range=AbilityRange.Medium, Kind=Kind.Special, Friendly=true, DicePerStep=1, Step=1, DiceCap=20,
        Name=L10n.S("spell.Mon3tr.name", "Mon3tr", "Mon3tr"),
        Desc=L10n.S("spell.Mon3tr.desc", "学派：咒法（治疗）；标准动作；中距；一名友军。恢复每级 1d6 生命（最多 20d6），解除震慑、恍惚、麻痹与恶心。——凯尔希", "Conjuration (healing); standard; medium; one ally. Heals 1d6 per level (max 20d6); cures stunned, dazed, paralyzed, nauseated. (Kal'tsit)") },
      new Spec { Key="Ironcast", Ring=5, Kind=Kind.Buff, PerLevel=true,
        Name=L10n.S("spell.Ironcast.name", "铸铁", "Ironcast"),
        Desc=L10n.S("spell.Ironcast.desc", "学派：防护；标准动作；近距；一名友军；1 轮/级。获得伤害减免 10/—、免疫重击与偷袭；该减免为常规伤害减免，不与同类效果叠加。——年", "Abjuration; standard; close; one ally; 1 round/level. DR 10/—, immunity to critical hits and sneak attacks; standard DR that does not stack with similar effects. (Nian)") },
      new Spec { Key="InkedMountains", Ring=5, Kind=Kind.Special, Rounds=1,
        Name=L10n.S("spell.InkedMountains.name", "墨染山河", "Inked Mountains"),
        Desc=L10n.S("spell.InkedMountains.desc", "学派：幻术（创造）；标准动作；近距；1 轮/级。召唤两只墨灵：以你的 BAB 攻击（2d6 力场伤害），被摧毁时爆发（2d6 力场伤害并目眩，意志无效）。——夕", "Illusion (creation); standard; close; 1 round/level. Summons two ink spirits attacking with your BAB (2d6 force); they burst for 2d6 force on destruction. (Dusk)") },
      new Spec { Key="FrozenDomain", Ring=5, Range=AbilityRange.Medium, Kind=Kind.Special, DicePerStep=4, Step=1, DiceCap=4, Energy=DamageEnergyType.Cold, AoERadius=20,
        Name=L10n.S("spell.FrozenDomain.name", "冰封领域", "Frozen Domain"),
        Desc=L10n.S("spell.FrozenDomain.desc", "学派：塑能（寒冷）；标准动作；中距；半径 20 尺；1 轮/级；反射部分通过。敌人进入区域或在区域内开始回合时，受 4d6 寒冷伤害；失败者定身 1 轮，成功者速度减半 1 轮。——霜星", "Evocation (cold); standard; medium; 20-ft. radius; 1 round/level; Reflex partial. Enemies entering the area or starting their turn inside take 4d6 cold; entangled 1 round on failure, half speed 1 round on success. (FrostNova)") },
      new Spec { Key="RoyalCharge", Ring=5, Range=AbilityRange.Custom, RangeFeet=40, Kind=Kind.Special, SpecterOnly=true,
        Name=L10n.S("spell.RoyalCharge.name", "王者冲锋", "Royal Charge"),
        Desc=L10n.S("spell.RoyalCharge.desc", "【恶灵】学派：变化；标准动作；40 尺；选一点。沿直线冲锋至多 40 尺并撞击路径上的敌人；路径上每个敌人受 4d6 轰击（反射成功仅免倒地，不减伤）。——推进之王", "[Specter] Transmutation; standard; 40 ft.; one point. Charge up to 40 ft. in a line; every enemy in the path takes 4d6 bludgeoning (a successful Reflex only avoids being knocked prone, not the damage). (Siege)") },

      // ─── 6 环（36–41）───
      new Spec { Key="TidalElegy", Ring=6, Cast=UnitCommand.CommandType.Swift, Range=AbilityRange.Personal, Kind=Kind.Buff, PerLevel=true, SpecterOnly=true,
        Bonuses={ new Bonus(StatType.AdditionalAttackBonus, 6, ModifierDescriptor.UntypedStackable),
          new Bonus(StatType.Speed, 20, ModifierDescriptor.UntypedStackable) },
        Name=L10n.S("spell.TidalElegy.name", "涌潮悲歌", "Tidal Elegy"),
        Desc=L10n.S("spell.TidalElegy.desc", "【恶灵】学派：变化；迅捷动作；个人；1 轮/级。攻击 +6（不具类型），武器伤害 + 施法者等级，60 点临时生命，速度 +20 尺。——斯卡蒂", "[Specter] Transmutation; swift; personal; 1 round/level. +6 untyped attack, damage + caster level, 60 temp hp, +20-ft. speed. (Skadi)") },
      new Spec { Key="FoxfireDance", Ring=6, Range=AbilityRange.Medium, Kind=Kind.Special, PerLevel=true, Save=SavingThrowType.Will, AoERadius=20, AoETarget=TargetType.Any,
        Name=L10n.S("spell.FoxfireDance.name", "儿时的舞乐", "Dance of Childhood"),
        Desc=L10n.S("spell.FoxfireDance.desc", "学派：惑控（影响心灵）；标准动作；中距；半径 20 尺；1 轮/级；意志无效。失败敌人缓慢且受伤 +30%（离开即结束）；区域内友军每轮恢复 2d8 + 施法者等级一半。——铃兰", "Enchantment; standard; medium; 20-ft. radius; 4 rounds; Will negates. Enemies slowed and take +30% damage; allies regain 2d8 + half caster level each round. (Suzuran)") },
      new Spec { Key="DrunkenDream", Ring=6, Range=AbilityRange.Medium, Kind=Kind.Special, PerLevel=true, Save=SavingThrowType.Will, AoERadius=20,
        Name=L10n.S("spell.DrunkenDream.name", "醉梦", "Drunken Dream"),
        Desc=L10n.S("spell.DrunkenDream.desc", "学派：惑控（睡眠、影响心灵）；标准动作；中距；半径 20 尺爆发；1 轮/级；意志无效。失败敌人魔法沉眠（不受 HD 限制），被打醒后恍惚 1 轮；对睡眠免疫的目标：若其免疫挡下整个法术（如精灵）则完全不受影响，若只免疫睡眠效果则不沉眠但仍恍惚 1 轮。——令", "Enchantment (sleep, mind-affecting); standard; medium; 20-ft. burst; 1 round/level; Will negates. Failures fall into magical slumber (no HD limit) and are dazed 1 round when woken; sleep-immune targets: if the immunity stops the whole spell (e.g. elves) they are unaffected, if it stops only the sleep effect they do not sleep but are still dazed 1 round. (Ling)") },
      new Spec { Key="BlackSnakeFlame", Ring=6, Range=AbilityRange.Medium, Kind=Kind.Special, DicePerStep=1, Step=1, DiceCap=20, Energy=DamageEnergyType.Fire,
        Name=L10n.S("spell.BlackSnakeFlame.name", "黑蛇之焰", "Black Snake's Flame"),
        Desc=L10n.S("spell.BlackSnakeFlame.desc", "学派：塑能（火焰）；标准动作；中距；一名敌人；反射减半。每级 1d6 火焰（最多 20d6，其中一半为不受火焰抗力影响的神圣伤害，火焰免疫仅将总伤害减半）；无论豁免成败，之后目标每轮受 3d6 火焰伤害。——塔露拉", "Evocation (fire); standard; medium; one enemy; Reflex half. 1d6 fire per level (max 20d6, half of it divine damage unaffected by fire resistance; fire immunity only halves the total); the target then takes 3d6 fire each round regardless of the save. (Talulah)") },
      // 炮火延伸：定案为"一个法术挂逐轮依次爆炸的增益"——第一轮立即爆炸，
      // 之后两轮由挂在施法者身上的追踪增益（ArtilleryBarrageTracker）依次爆炸。
      new Spec { Key="ArtilleryBarrage", Ring=6, Range=AbilityRange.Long, Kind=Kind.Special, AoERadius=15,
        Name=L10n.S("spell.ArtilleryBarrage.name", "炮火延伸", "Artillery Barrage"),
        Desc=L10n.S("spell.ArtilleryBarrage.desc", "学派：塑能；标准动作；极距；同一点连续三轮；反射减半。每轮开始时落点发生半径 15 尺爆炸，造成 8d6 轰击与 8d6 火焰伤害；敌人可预见落点并离开。——号角", "Evocation; standard; long; same point for three rounds; Reflex half. Each round the point bursts (15-ft.): 8d6 bludgeoning + 8d6 fire. (Horn)") },
      new Spec { Key="SinPunishment", Ring=6, Range=AbilityRange.Medium, Kind=Kind.Special, Rounds=5, Save=SavingThrowType.Will,
        Name=L10n.S("spell.SinPunishment.name", "罪罚", "Sin and Punishment"),
        Desc=L10n.S("spell.SinPunishment.desc", "学派：死灵；标准动作；中距；一名敌人；1 轮/级；意志无效。每轮第一次目标对其他生物造成伤害时，它自己受一半等量同类型伤害。——斥罪", "Necromancy; standard; medium; one enemy; 1 round/level; Will negates. The first time each round the target deals damage, it takes half that much itself. (Penance)") },

      // ─── 7 环（42–47）───
      new Spec { Key="Twilight", Ring=7, Range=AbilityRange.Medium, Kind=Kind.Special, DicePerStep=1, Step=1, DiceCap=20, Energy=DamageEnergyType.Fire, AoERadius=20,
        Name=L10n.S("spell.Twilight.name", "黄昏", "Twilight"),
        Desc=L10n.S("spell.Twilight.desc", "学派：塑能（火焰）；标准动作；中距；半径 20 尺；反射减半。每级 1d6 火焰（最多 20d6）；失败者附着熔火，之后 2 轮各 4d6（可再豁免）。——史尔特尔", "Evocation (fire); standard; medium; 20-ft. burst; Reflex half. 1d6 fire per level (max 20d6); failures burn for 4d6 on the next 2 rounds. (Surtr)") },
      new Spec { Key="MirrorFlow", Ring=7, Kind=Kind.Special, PerLevel=true, Friendly=true,
        Name=L10n.S("spell.MirrorFlow.name", "镜中流形", "Mirror Flow"),
        Desc=L10n.S("spell.MirrorFlow.desc", "学派：幻术（阴影）；标准动作；近距；一名友军；1 轮/级。制造目标的镜像复制体：属性、AC、攻击加值与武器与目标相同，生命值为目标当前生命值，法术位与资源均为满值；它在目标回合后行动，可正常行动与施法；持续 1 轮/级或生命归零时消散。——缪尔赛思", "Illusion (shadow); standard; close; one ally; 1 round/level. A duplicate with the target's stats, AC, attack bonus and weapon, at the target's current hp, with full spell slots and resources; it acts after the target and can act and cast normally; it lasts 1 round/level or until destroyed. (Muelsyse)") },
      new Spec { Key="FinalSnipe", Ring=7, Range=AbilityRange.Long, Kind=Kind.Special, Save=SavingThrowType.Reflex,
        Name=L10n.S("spell.FinalSnipe.name", "终末狙击", "Final Snipe"),
        Desc=L10n.S("spell.FinalSnipe.desc", "学派：预言；标准动作；极距；一名敌人；直到下轮开始；反射减半。下轮开始目标被必中狙击命中：15d6 穿刺 + 2d6 体质伤害；若届时隐形则失效。——黑", "Divination; standard; long; one enemy; until your next turn; Reflex half. An unmissable shot hits for 15d6 piercing + 2d6 Constitution; fizzles if the target is invisible. (Schwarz)") },
      new Spec { Key="ChainDetonation", Ring=7, Range=AbilityRange.Medium, Kind=Kind.Special, Rounds=5, Save=SavingThrowType.Reflex, AoERadius=20, AoETarget=TargetType.Enemy,
        Name=L10n.S("spell.ChainDetonation.name", "连锁殉爆", "Chain Detonation"),
        Desc=L10n.S("spell.ChainDetonation.desc", "学派：死灵；标准动作；中距；以目标为中心 20 尺内的全部敌人；1 轮/级；反射减半。标记者死亡时在 20 尺内爆炸（10d6 力场，只伤敌人）。——维什戴尔", "Necromancy; standard; medium; all enemies within 20 ft. of the target; 1 round/level; Reflex half. Marked creatures explode on death (10d6 force, 20 ft., enemies only). (Wis'adel)") },
      new Spec { Key="ThunderousJudgment", Ring=7, Range=AbilityRange.Medium, Kind=Kind.Special, DicePerStep=12, Step=1, DiceCap=12, Energy=DamageEnergyType.Electricity, AoERadius=30,
        Name=L10n.S("spell.ThunderousJudgment.name", "雷霆审判", "Thunderous Judgment"),
        Desc=L10n.S("spell.ThunderousJudgment.desc", "学派：塑能（电击）；标准动作；中距；半径 30 尺；反射部分通过。敌人受 12d6 电击；失败者倒地并恍惚 1 轮，成功半伤不倒地。——艾丽妮", "Evocation (electricity); standard; medium; 30-ft. burst; Reflex partial. 12d6 electricity; failures fall prone and are dazed 1 round. (Irene)") },
      new Spec { Key="ReturnToAbyss", Ring=7, Cast=UnitCommand.CommandType.Swift, Range=AbilityRange.Personal, Kind=Kind.Special, PerLevel=true, SpecterOnly=true,
        Name=L10n.S("spell.ReturnToAbyss.name", "潮", "Tide"),
        Desc=L10n.S("spell.ReturnToAbyss.desc", "【恶灵】学派：死灵；迅捷动作；个人；1 轮/级。每次近战命中恢复 2d6 生命（每轮至多三次）；每轮首次重击或击杀返还 1 轮恶灵时间（至多三轮）。——幽灵鲨", "[Specter] Necromancy; swift; personal; 1 round/level. Melee hits heal 2d6 (max 3/round); the first crit or kill each round returns 1 round of Specter duration. (Specter)") },

      // ─── 8 环（48–53）───
      // 画中世界（10-10 深夜重构）：本质是**放逐**——失败者被收进画境 N 轮（不能动、也打不到），
      // 返回原位后**麻痹**（作者：这样才配得上 8 环）。见 ApplySpecial 的 PaintedWorld 分支。
      new Spec { Key="PaintedWorld", Ring=8, Range=AbilityRange.Medium, Kind=Kind.Special, PerLevel=true, Save=SavingThrowType.Will,
        Name=L10n.S("spell.PaintedWorld.name", "画中世界", "Painted World"),
        Desc=L10n.S("spell.PaintedWorld.desc", "学派：幻术（阴影）；标准动作；中距；一名敌人；意志无效。失败目标被收进画境 2 轮：这几轮它无法行动、也无法被攻击或选为目标；返回原位后麻痹 1 轮/级。——夕", "Illusion (shadow); standard; medium; one enemy; Will negates. A failing target is drawn into a painting for 2 rounds: it cannot act and cannot be attacked or targeted; when it returns it is paralyzed for 1 round/level. (Dusk)") },
      // 耀阳颔首走 Special（直线内敌我分算：敌人神圣伤害+目盲，友军治疗+解盲；见 ApplySpecial）。
      // 伤害固定 15d6 / 治疗固定 10d6，不走骰数 rank；目盲 1 轮/级走 BuffRank(l=>l)。
      new Spec { Key="BlazingSunBow", Ring=8, Kind=Kind.Special, Save=SavingThrowType.Reflex, LineLength=120,
        Name=L10n.S("spell.BlazingSunBow.name", "耀阳颔首", "Blazing Sun's Bow"),
        Desc=L10n.S("spell.BlazingSunBow.desc", "学派：塑能（光）；标准动作；120 尺直线；反射部分通过。直线内敌人 15d6 神圣伤害，失败者目盲 1 轮/级，成功者半伤且目眩 1 轮；直线内友军恢复 10d6 并解除目盲。——耀骑士临光", "Evocation (light); standard; 120-ft. line; Reflex partial. 15d6 holy to enemies in the line (blinded 1 round/level on failure, dazzled 1 round on success); allies in the line heal 10d6 and are cured of blindness. (Nearl the Radiant Knight)") },
      new Spec { Key="TidalVigil", Ring=8, Range=AbilityRange.Personal, Kind=Kind.Buff, PerLevel=true, Unit=DurationRate.Minutes, AoERadius=30, AoETarget=TargetType.Ally,
        Name=L10n.S("spell.TidalVigil.name", "潮汐守望", "Tidal Vigil"),
        Desc=L10n.S("spell.TidalVigil.desc", "学派：防护；标准动作；以你为中心，30 尺爆发内全部友军；1 分钟/级。每轮开始获得 15 临时生命（刷新不叠加）；免疫魅惑与强迫。——浊心斯卡蒂", "Abjuration; standard; personal; all allies in a 30-ft. burst centered on you; 1 min./level. 15 temp hp each round (refreshing, not stacking); immunity to charm and compulsion. (Skadi the Corrupting Heart)") },
      new Spec { Key="ShadowRealm", Ring=8, Range=AbilityRange.Medium, Kind=Kind.Special, Rounds=5, Save=SavingThrowType.Will, Friendly=true,
        Name=L10n.S("spell.ShadowRealm.name", "影之疆域", "Shadow Realm"),
        Desc=L10n.S("spell.ShadowRealm.desc", "学派：幻术（阴影）；标准动作；中距；一名友军；1 轮/级；意志部分通过。以目标为中心 15 尺领域随其移动：范围内友军隐形；敌人进场与每轮开始意志失败者目盲 1 轮，成功者目眩 1 轮；离开即结束。——伊内丝", "Illusion (shadow); standard; medium; one ally; 1 round/level; Will partial. A 15-ft. realm follows the target: allies inside are invisible; enemies failing on entry or each round are blinded 1 round, dazzled 1 round on success. (Ines)") },
      new Spec { Key="HarvestYears", Ring=8, Range=AbilityRange.Personal, Kind=Kind.Special, Friendly=true, AoERadius=30, AoETarget=TargetType.Ally,
        Name=L10n.S("spell.HarvestYears.name", "岁稔", "Harvest of Years"),
        Desc=L10n.S("spell.HarvestYears.desc", "学派：咒法（治疗）；标准动作；以你为中心，30 尺爆发内全部友军；3 轮。每轮恢复 4d8 + 施法者等级；结束时未用掉的治疗转为临时生命（最多 60，持续 1 分钟）。——种地的大姐姐", "Conjuration (healing); standard; personal; all allies in a 30-ft. burst centered on you; 3 rounds. 4d8 + caster level each round; unused healing becomes temp hp (max 60, 1 minute) when it ends. (the farming big sister)") },
      // 持续 1 轮/级（用法术内的 `ContextDuration.Variable(Rank)`；`Rounds` 在这里不参与，
      // 之前误填 Rounds=5 —— 作者 10-11 澄清：就是轮/级）。
      new Spec { Key="BannerStands", Ring=8, Range=AbilityRange.Personal, Cast=UnitCommand.CommandType.Swift, Kind=Kind.Special, Friendly=true, PerLevel=true, AoERadius=30, AoETarget=TargetType.Ally,
        Name=L10n.S("spell.BannerStands.name", "旗帜不倒", "The Banner Stands"),
        Desc=L10n.S("spell.BannerStands.desc", "学派：惑控；迅捷动作；以你为中心，30 尺爆发内全部友军；1 轮/级。目标生命值降至 0 或以下时不会死亡或昏迷，并能正常行动；法术结束时若仍低于 0 点生命则按通常规则结算。该效果不阻止即死效果。——琴柳", "Enchantment; swift; personal; all allies in a 30-ft. burst centered on you; 1 round/level. Targets at 0 or fewer hp stay conscious and act; normal rules apply when it ends. Does not stop death effects. (Saileach)") },

      // ─── 9 环（54–59）───
      new Spec { Key="ShadowBladeDraw", Ring=9, Range=AbilityRange.Medium, Kind=Kind.Special, Save=SavingThrowType.Fortitude,
        Name=L10n.S("spell.ShadowBladeDraw.name", "影霄·拔刀", "Shadow Blade: Draw"),
        Desc=L10n.S("spell.ShadowBladeDraw.desc", "学派：死灵；标准动作；中距；一名敌人；强韧部分通过；法术抗力：否。失败立即死亡；成功仍受 10d6 + 施法者等级伤害；免疫即死者视为豁免成功。——阿米娅", "Necromancy; standard; medium; one enemy; Fortitude partial; SR no. Failure: death; success: 10d6 + caster level damage. Targets immune to death effects count as having succeeded. (Amiya)") },
      new Spec { Key="Chronostasis", Ring=9, Range=AbilityRange.Personal, Kind=Kind.Special,
        Name=L10n.S("spell.Chronostasis.name", "时空静止", "Chronostasis"),
        Desc=L10n.S("spell.Chronostasis.desc", "学派：变化；标准动作；个人。如同时间停止（原版：表观 1d4+1 轮）；不能直接影响其他生物，可移动、布置区域效果并对自己施法。——莫斯提马", "Transmutation; standard; personal. As time stop (vanilla: 1d4+1 apparent rounds); you cannot directly affect others. (Mostima)") },
      new Spec { Key="Cloudpiercer", Ring=9, Range=AbilityRange.Long, Kind=Kind.Special, DicePerStep=20, Step=1, DiceCap=20, IsPhysical=true, Physical=PhysicalDamageForm.Piercing, LineLength=200,
        Name=L10n.S("spell.Cloudpiercer.name", "穿云", "Cloudpiercer"),
        Desc=L10n.S("spell.Cloudpiercer.desc", "学派：塑能；标准动作；极距；5 尺宽直线；反射减半。直线内敌人 20d6 穿刺，忽略伤害减免。——提丰", "Evocation; standard; long; 5-ft.-wide line; Reflex half. 20d6 piercing to enemies in the line, ignoring damage reduction. (Typhon)") },
      new Spec { Key="EarthFury", Ring=9, Range=AbilityRange.Personal, Kind=Kind.Special, DicePerStep=10, Step=1, DiceCap=10, IsPhysical=true, Physical=PhysicalDamageForm.Bludgeoning, AoERadius=30,
        Name=L10n.S("spell.EarthFury.name", "大地之怒", "Earth's Fury"),
        Desc=L10n.S("spell.EarthFury.desc", "学派：塑能；标准动作；以你为中心，半径 30 尺；反射部分通过。敌人 10d6 轰击并倒地（成功半伤不倒地）；召唤物受双倍伤害。——卡涅利安", "Evocation; standard; personal; 30-ft. radius centered on you; Reflex partial. 10d6 bludgeoning and prone (half, no prone on success); summoned creatures take double damage. (Carnelian)") },
      new Spec { Key="Reconstruction", Ring=9, Range=AbilityRange.Personal, Kind=Kind.Special, Friendly=true, AoERadius=30, AoETarget=TargetType.Ally,
        Name=L10n.S("spell.Reconstruction.name", "重构", "Reconstruction"),
        Desc=L10n.S("spell.Reconstruction.desc", "学派：咒法（治疗）；标准动作；以你为中心，半径 30 尺。只复活友军（召唤物不复活，其余效果照常）；解除除诅咒、疾病、中毒以外的所有负面效果；阵亡者复活并恢复一半生命（至多三名）。——凯尔希", "Conjuration (healing); standard; personal; 30-ft. burst centered on you. Only allies revive (summons do not revive, other effects apply normally); cleanses all negative effects except curses, diseases and poisons; the dead return at half hp (max three). (Kal'tsit)") },
      new Spec { Key="TacticianOfOld", Ring=9, Cast=UnitCommand.CommandType.Swift, Range=AbilityRange.Personal, Kind=Kind.Special, PerLevel=true, SpecterOnly=true,
        Name=L10n.S("spell.TacticianOfOld.name", "旧日的战术家", "Tactician of Old"),
        Desc=L10n.S("spell.TacticianOfOld.desc", "【恶灵】学派：变化；迅捷动作；个人；1 轮/级。补足你当前恶灵形态缺的另一半：同时获得敏捷与体质两个形态的全部收益，转移量额外 +2。需先开启恶灵形态（不在形态时无法施放）；不解除恶灵对普通指令的限制。", "[Specter] Transmutation; swift; personal; 1 round/level. Completes the other half of your current Specter form: gain both forms' full benefits, Transfer Value +2. Requires an active Specter form (cannot be cast otherwise); the restriction on normal orders remains.") },

      // ─── 10 环（60–63，仅合书）───
      new Spec { Key="Civilization", Ring=10, Range=AbilityRange.Personal, Kind=Kind.Special, Friendly=true, AoERadius=30, AoETarget=TargetType.Ally,
        Name=L10n.S("spell.Civilization.name", "文明的存续", "The Survival of Civilization"),
        Desc=L10n.S("spell.Civilization.desc", "【10 环】学派：咒法（治疗）；标准动作；以你为中心 30 尺。30 尺内的友军完全恢复、解除除诅咒、疾病、中毒以外的所有负面效果、复活（至多五名）；其后 1 分钟攻击、伤害、AC、豁免 +4，且仅一次将降至 0 生命时保留 1 点。不清除剧情诅咒。", "[10th level] Conjuration (healing); standard; 30-ft. burst on you. Allies within 30 ft. are fully healed, cleansed of all negative effects except curses, diseases and poisons, revive up to five; then +4 to attacks, damage, AC, saves for 1 minute, surviving death at 1 hp once. Does not remove story curses.") },
      new Spec { Key="EternalGaze", Ring=10, Range=AbilityRange.Personal, Kind=Kind.Special, Rounds=10, Friendly=true, AoERadius=30, AoETarget=TargetType.Ally,
        Name=L10n.S("spell.EternalGaze.name", "永恒的注视", "Eternal Gaze"),
        Desc=L10n.S("spell.EternalGaze.desc", "【10 环】学派：防护；标准动作；以你为中心，30 尺爆发内全部友军；1 分钟/级。每个目标第一次将要死亡时，改为回满生命、传送至你身旁并立刻获得 1 分钟加速（速度提升、全力攻击多一次攻击，可与加速术叠加）。每个目标仅触发一次。——普瑞赛斯", "[10th level] Abjuration; standard; personal; all allies in a 30-ft. burst centered on you; 1 min./level. The first time each target would die: full hp, teleport beside you, and 1 minute of haste-like acceleration. Once per target. (Priestess)") },
      // 王冠的回响：**以施法者为中心**（作者 2026-10-10：释放体验应与祝福术一致——个人距离、圈挂在自己身上，
      // 而不是选点）。注意效果与祝福术不同：它是**敌方减益/夺增益**，所以 AoETarget 仍是 Enemy，只是中心改成自己。
      new Spec { Key="CrownEcho", Ring=10, Range=AbilityRange.Personal, Kind=Kind.Special, Rounds=5, Save=SavingThrowType.Will, AoERadius=60,
        Name=L10n.S("spell.CrownEcho.name", "王冠的回响", "Echo of the Crown"),
        Desc=L10n.S("spell.CrownEcho.desc", "【10 环】学派：惑控（影响心灵）；标准动作；个人；以你为中心半径 60 尺爆发；意志部分通过。豁免失败者失去所有可驱散的法术增益并恍惚 1 轮（不过驱散检定；每人至多两个增益分给友军）；豁免成功者随机失去一个可驱散的法术增益，该增益直接消失。只取敌方的增益。——特蕾西娅", "[10th level] Enchantment; standard; personal; 60-ft. burst centred on you; Will partial. Failures lose all dispellable spell buffs and are dazed 1 round (no dispel check; up to two per enemy redistributed to allies); success loses one random dispellable spell buff, which vanishes. Only the enemy's own buffs are taken. (Theresa)") },
      new Spec { Key="Catastrophe", Ring=10, Range=AbilityRange.Long, Kind=Kind.Special, AoERadius=40,
        Name=L10n.S("spell.Catastrophe.name", "天灾降临", "Catastrophe Descends"),
        Desc=L10n.S("spell.Catastrophe.desc", "【10 环】学派：塑能；整轮动作；极距；半径 40 尺；1 分钟；反射减半。每轮开始时区域内每个敌人受一次 12d6 火焰、12d6 轰击与 12d6 电击；同一生物每轮只受一次。", "[10th level] Evocation; full-round; long; 40-ft. radius; 1 minute; Reflex half. At the start of each round every enemy in the area takes 12d6 fire, 12d6 bludgeoning and 12d6 electricity once per round.") },
    };

    // ── 供法术列表与恶灵限制查询 ──
    internal static IEnumerable<int> SpellsOfLevel(int level)
      => Specs.Select((s, i) => (s, i)).Where(t => t.s.Ring == level).Select(t => t.i);

    /// <summary>指令总数（= Specs 长度）。测试面板"发放全部指令"用。</summary>
    internal static int SpellCount => Specs.Length;

    /// <summary>第 i 个指令的环数。测试面板"发放全部指令"用。</summary>
    internal static int RingOf(int i) => Specs[i].Ring;

    /// <summary>GUID 统一成去横线小写，与 SpecterSpellRestriction 的运行时取法对齐。</summary>
    private static string Norm(string guid) => guid.Replace("-", "").ToLowerInvariant();

    /// <summary>全部指令法术的 GUID（恶灵限制用）。</summary>
    internal static readonly HashSet<string> AllSpellGuids =
      new HashSet<string>(Enumerable.Range(0, Specs.Length).Select(i => Norm(Guids.Spell(i))));

    /// <summary>恶灵期间仍可施放的法术 GUID（【恶灵】描述符）。</summary>
    internal static readonly HashSet<string> SpecterAllowedGuids =
      new HashSet<string>(Specs.Select((s, i) => (s, i)).Where(t => t.s.SpecterOnly).Select(t => Norm(Guids.Spell(t.i))));

    internal static void Create()
    {
      for (int i = 0; i < Specs.Length; i++) CreateSpell(Specs[i], i);
    }

    // ── 单个法术 ──
    private static void CreateSpell(Spec s, int i)
    {
      // 显式标为法术（AbilityType.Spell）；默认值虽也是 Spell=0，写死避免依赖默认。
      // 0 环指令按规则做成戏法（IsCantrip），可无限施展。
      // ── 目标规则（全量审计后）──
      // 个人距离 → 仅自己；增益 / 治疗 / 友军法术 → 自己 + 友军；其余 → 仅敌人。
      // 点/线范围且落点无意义的（召唤、信标），按下面的特例处理。
      // （此前 SetCanTargetSelf(true) 无条件全开，导致战术标记能标自己、处决能对敌人放等一整批问题。）
      // 注意：CanTargetSelf/Friends/Enemies 只决定光标与描述；游戏 AbilityData.CanTarget 对"友军"
      // 并不读 CanTargetFriends（原版单目标有害法术同样允许点友军），所以纯敌方单体法术还要再挂
      // AbilityTargetIsAlly(Not=true) 才能真正挡住自己与友军（见下方）。
      bool personal = s.Range == AbilityRange.Personal;
      bool beneficial = s.Kind is Kind.Heal or Kind.Buff || s.Friendly;
      bool canSelf = personal || beneficial || s.Key is "BeaconProjection";
      bool canFriends = beneficial;
      bool canEnemies = !personal && !beneficial;
      if (s.Key is "BlazingSunBow") canFriends = true; // 直线内友军回血
      if (s.Key is "InkedMountains") { canSelf = false; canEnemies = false; } // 纯落点召唤
      var ability = AbilityConfigurator.New($"RhodesDoctorSpell{s.Key}", Guids.Spell(i))
        .SetType(AbilityType.Spell)
        .SetDisplayName(s.Name)
        .SetDescription(s.Desc)
        .SetActionType(s.Cast)
        .SetRange(s.Range)
        .SetCanTargetSelf(canSelf)
        .SetCanTargetFriends(canFriends)
        .SetCanTargetEnemies(canEnemies);
      if (s.Key is "BeaconProjection" or "InkedMountains" or "RoyalCharge")
        ability.SetCanTargetPoint(true); // 落点施法（信标是定点区域，墨灵是落点召唤，王者冲锋是选一条冲锋方向）
      // 固定距离（尺）：文档写的是绝对尺数，而 AbilityRange 的档位随等级变化
      // ⇒ 这些法术用 Custom + CustomRange 落成固定距离（含王者冲锋的 40 尺落点冲锋）。
      if (s.RangeFeet > 0)
        ability.SetRange(AbilityRange.Custom).SetCustomRange(new Feet(s.RangeFeet));
      if (s.Ring == 0) ability.SetIsCantrip(true);
      // C4：天灾降临为整轮动作（设计文档第 300 行）。
      if (s.Key is "Catastrophe") ability.SetIsFullRoundAction(true);
      // 王者冲锋（作者 10-16）：不要抬手施法动作，点下去直接冲。
      if (s.Key is "RoyalCharge")
        ability.SetAnimation(Kingmaker.Visual.Animation.Kingmaker.Actions.UnitAnimationActionCastSpell.CastAnimationStyle.Immediate)
          .SetHasFastAnimation(true);
      Icons.ApplySpellIcon(ability, s.Key);
      ApplyAbilityFx(ability, s);

      // 纯敌方法术：补一个"目标不能是友军"限制（含自己）。
      // 单体法术直接拦截；敌方范围/直线法术只在"直接点到某个单位"时拦截
      //（点地面时 target.Unit 为 null，仍可把爆发点放在任意地面，不妨碍正常 AoE 选点）。
      // AoETarget=Any/Ally 的范围法术（烟幕/钙质化/儿时舞乐等，本来就要照顾友军）不加。
      bool singleTarget = s.AoERadius == 0 && s.LineLength == 0;
      bool enemyArea = s.LineLength > 0 || (s.AoERadius > 0 && s.AoETarget == TargetType.Enemy);
      if (canEnemies && !canFriends && (singleTarget || enemyArea)
        && s.Key is not ("BeaconProjection" or "InkedMountains"))
        ability.AddComponent<AbilityTargetIsAlly>(c => c.Not = true);

      // 纯友军单体法术（增益 / 治疗，如影之疆域、医疗敷料）：补"目标必须是友军"限制，**挡住敌人**。
      // 与上面那支同理：`SetCanTargetEnemies(false)` 只影响光标与描述，真正拦截靠组件。
      // `Not=false`（默认）= "目标必须是友军"——自己算友军，所以仍可对自己施放。
      if (canFriends && !canEnemies && singleTarget
        && s.Key is not ("BeaconProjection" or "InkedMountains"))
        ability.AddComponent<AbilityTargetIsAlly>();

      // B19：非【恶灵】指令在恶灵期间用"施法者限制"拦（按钮变灰），
      // 取代旧的"施法时 ForceFail"（反编译 RuleCastSpell.ShouldSpendResource 显示 ForceFail 本就不扣法术位，
      // 但按钮仍可点、体验差）。锁定被 20 级压制后限制自动解除。
      // R2：一段/二段/三段三个锁定都要拦（非恶灵指令在恶灵全程禁用）。
      if (!s.SpecterOnly)
        ability.AddComponent<SpecterCasterRestriction>(c => c.LockFacts = new BlueprintUnitFactReference[]
        {
          BlueprintTool.GetRef<BlueprintUnitFactReference>(Guids.LockBuff),
          BlueprintTool.GetRef<BlueprintUnitFactReference>(Guids.LockBuffV2),
          BlueprintTool.GetRef<BlueprintUnitFactReference>(Guids.LockBuffV3),
        });

      // 旧日的战术家：**没开恶灵形态就不给放**（作者 10-11："空放要白扣法术位太亏"）。
      // 它的效果是"补当前形态缺的另一半"，没有形态时本来就没意义；这里用 `IAbilityCasterRestriction` 拦住 ——
      // 按钮会置灰显示"需要先开启恶灵形态"，而且 `UnitUseAbility.OnAction` 在**施法前**会复查
      // `Ability.IsAvailable`（不通过直接 `return ResultType.Fail`，**不扣法术位**），所以不会白亏法位。
      if (s.Key is "TacticianOfOld")
        ability.AddComponent<AbilityCasterHasAnyActiveFact>(
          c => c.Facts = SpecterSetup.FormFactRefs());

      // 学派 / 描述符 / 法术抗力 / 表头（持续与豁免）：一律取显式元数据表。
      // 注意：s.Desc 是本地化"键"（L10n.S 只返回键），不能再从它解析文本。
      var meta = Metas.TryGetValue(s.Key, out var m) ? m : null;
      ability.AddComponent<SpellComponent>(c => c.School = meta?.School ?? SpellSchool.None);
      if (meta != null && meta.Descriptor != SpellDescriptor.None)
        ability.AddComponent<SpellDescriptorComponent>(c => c.Descriptor = meta.Descriptor);
      ability.SetSpellResistance(meta?.Sr ?? true);
      if (meta?.DurationKey != null) ability.SetLocalizedDuration(meta.DurationKey);
      if (meta?.Save != null) ability.SetLocalizedSavingThrow(meta.Save.Value);

      // 范围组件（签名已探明）：
      //   点爆发：AddAbilityTargetsAround(radius, targetType) + SetCanTargetPoint(true)
      //           —— V1：原版火球等范围法术靠 AbilityTargetsAround 选取圈内目标；
      //              AbilityAoERadius 只画范围圈、不选目标，只挂它会导致只影响单体/点空无效。
      //              这里改用 AbilityTargetsAround（它本身也实现 IAbilityAoERadiusProvider，会画圈）。
      //   直线：AddAbilityDeliverProjectile(length:, lineWidth:, type:, projectiles:)
      //           —— V2：直线/锥形必须挂真实投射物，否则投递不选取任何目标。
      //   少数"区域效果/自扫场景"的法术不挂 TargetsAround（否则同一动作会按目标数重复执行）：
      //     烟幕、钙质化、儿时舞乐、天灾降临、冰封领域（SpawnAreaEffect）；重构、文明的存续（自扫全场的动作）；
      //     炮火延伸（自写落点扫描动作）；延时爆破（自写炸弹）。
      //     火山、黄昏、大地之怒、闪光弹、醉梦虽有 AoE 但按 TargetsAround 逐目标结算（含豁免），保留 TargetsAround。
      bool areaSelfScan = s.Key is "Smokescreen" or "Calcification" or "FoxfireDance"
        or "Catastrophe" or "FrozenDomain" or "Reconstruction" or "Civilization" or "ArtilleryBarrage" or "DelayedBlast"
        or "HoldTheLine" or "EarthFury"; // 坚守阵地：由**区域**给友军挂增益；大地之怒：自扫场景（否则视觉区域按目标数重复生成）
      if (s.AoERadius > 0)
      {
        // 以施法者为中心的范围法术（Range=Personal，如祝福术 / 王冠的回响）：**不选点**，圈挂在施法者身上。
        if (!personal) ability.SetCanTargetPoint(true);
        ability.AddAbilityAoERadius(radius: new Feet(s.AoERadius), targetType: s.AoETarget);
        if (!areaSelfScan)
          ability.AddAbilityTargetsAround(radius: new Feet(s.AoERadius), targetType: s.AoETarget);
      }
      if (s.LineLength > 0)
      {
        // 直线投射物按元素分开（此前统一借闪电束，火焰直线看着是电的）。
        var lineProjectile = s.Key switch
        {
          "ScorchingBreath" => Guids.Vanilla.FireLineProjectile,       // 灼烧：火焰
          "BlazingSunBow" => Guids.Vanilla.HolyLineProjectile,         // 耀阳颔首：光/神圣
          "Cloudpiercer" => Guids.Vanilla.SwordTraceLineProjectile,    // 穿云：穿刺剑痕
          "RoyalCharge" => Guids.Vanilla.EarthLineProjectile,          // 王者冲锋：土石轰击
          _ => Guids.Vanilla.LightningBoltProjectile,
        };
        ability.SetCanTargetPoint(true)
          .AddAbilityDeliverProjectile(
            length: new Feet(s.LineLength),
            lineWidth: new Feet(s.LineWidth),
            type: AbilityProjectileType.Line,
            projectiles: new List<Blueprint<BlueprintProjectileReference>>
            {
              BlueprintTool.GetRef<BlueprintProjectileReference>(lineProjectile),
            });
      }

      // 按 1 轮/级（或 10 分钟/级）持续的法术需要 rank = 圣武士等级（否则 Rank 为 0，挂上即消失）。
      // 信标（10 分钟/级）与烟幕（1 轮/级）此前漏配，区域持续为 0，表现为"放出去没东西"。
      // B1：罪罚、连锁殉爆、永恒的注视、影之疆域、旗帜不倒也按等级计算持续，同样要补进名单。
      if (s.PerLevel || s.Key is "InkedMountains" or "Silencer" or "Execution"
        or "BeaconProjection" or "Smokescreen" or "BlazingSunBow" or "ShadowBladeDraw"
        or "SinPunishment" or "ChainDetonation" or "EternalGaze" or "ShadowRealm" or "BannerStands")
        ability.AddContextRankConfig(BuffRank(l => l));

      // 医疗敷料 24 小时限制：治疗时挂标记增益（24 小时），带标记的目标不可选。
      BlueprintBuffReference treatedMarker = null;
      if (s.Key == "FieldDressing")
      {
        treatedMarker = CreateExtraBuff("FieldDressingTreated", Guids.FieldDressingTreatedBuff,
          L10n.S("spell.FieldDressing.treated.name", "医疗敷料（包扎中）", "Field Dressing (bandaged)"),
          L10n.S("spell.FieldDressing.treated.desc",
            "24 小时内无法再次受益于医疗敷料。",
            "Cannot benefit from field dressing again within 24 hours."),
          cfg => { });
        ability.AddAbilityTargetHasNoFactUnless(
          new List<Blueprint<BlueprintUnitFactReference>> { Guids.FieldDressingTreatedBuff });
      }

      var effects = ActionsBuilder.New();
      switch (s.Kind)
      {
        case Kind.Damage:
          ability.AddContextRankConfig(DiceRank(s));
          // 先掷豁免，再结算伤害：halfIfSaved 读取的是外层 SavingThrow 的结果，
          // 没有这一层就永远视为豁免失败、每次吃满伤害（写法与 Debuff 分支一致）。
          effects.SavingThrow(s.Save, onResult: ActionsBuilder.New()
            .DealDamage(
              s.IsPhysical
                ? DamageTypes.Physical(form: s.Physical)
                : DamageTypes.Energy(s.Energy),
              ContextDice.Value(s.Dice, ContextValues.Rank(), 0),
              halfIfSaved: true));
          break;
        case Kind.Heal:
          if (s.DicePerStep > 0)
            ability.AddContextRankConfig(DiceRank(s));
          effects.HealTarget(ContextDice.Value(s.Dice,
            s.DicePerStep == 0 ? (ContextValue)0 : ContextValues.Rank(),
            s.DicePerStep == 0 ? 1 : 0));
          if (treatedMarker != null)
            effects.ApplyBuff(treatedMarker, ContextDuration.Fixed(24, DurationRate.Hours));
          break;
        case Kind.Buff:
        {
          var buff = CreateSpellBuff(s, i);
          effects.ApplyBuff(buff, s.PerLevel
            ? ContextDuration.Variable(ContextValues.Rank(), s.Unit)
            : ContextDuration.Fixed(s.Rounds, s.Unit));
          break;
        }
        case Kind.Debuff:
        {
          var buff = CreateSpellBuff(s, i);
          var duration = s.PerLevel
            ? ContextDuration.Variable(ContextValues.Rank(), s.Unit)
            : ContextDuration.Fixed(s.Rounds, s.Unit);
          // 牵制射击（0 环·克洛丝）：播放专属自绘弩矢飞射与命中爆闪特效
          if (s.Key == "SuppressingShot")
            effects.Add<ContextActionSuppressingShotFx>();
          if (s.SaveNone)
            effects.ApplyBuff(buff, duration);
          else
            effects.SavingThrow(s.Save, onResult: ActionsBuilder.New()
              .ConditionalSaved(failed: ActionsBuilder.New().ApplyBuff(buff, duration)));
          break;
        }
        case Kind.Special:
          ApplySpecial(s, ability, effects);
          break;
      }
      // 战场测绘（0 环·自身预言）：专属"PRTS 战场扫描"（BattlefieldSurveyFx.cs）。
      // ⚠ **不在这里触发**：它改成**跟着自己的增益活一整分钟**（作者 10-16 定案），
      //   由 `MarkFxKit` 的 `MarkFxTracker` 订阅 `RhodesDoctorBuffBattlefieldSurvey`，
      //   增益在扫描就在、增益没了就淡出 —— 所以能力动作这边**不加任何东西**（加了会生成第二份特效）。

      ability.AddAbilityEffectRunAction(effects);
      ability.Configure();
    }

    /// <summary>
    /// 复杂机制：按已探明的原版对照逐组实现；其余保持描述骨架（VERIFY）。
    /// 召唤（墨灵 / 镜中流形）与放逐（画中世界）需要新建单位蓝图 / 抄 Banishment 动作树，属单独一组。
    /// </summary>
    private static void ApplySpecial(Spec s, AbilityConfigurator ability, ActionsBuilder effects)
    {
      switch (s.Key)
      {
        // 时空静止：直接施放原版 TimeStop（表观 1d4+1 轮；比规则文档的"两个额外回合"略慷慨，已确认接受）
        case "Chronostasis":
          effects.CastSpell(BlueprintTool.GetRef<BlueprintAbilityReference>(Guids.Vanilla.TimeStopAbility));
          break;

        // 重构 / 文明的存续：群体完全恢复 + 驱散全部减益 + 限名复活（自定义动作）。
        // 顺序按规则先活后死；不限阵亡时间（作者已确认，T45 更新）。
        // 作者 10-11：改为**以施法者为中心**；重构**不再放金光**（"看没特效"）。
        case "Reconstruction":
          // 10-16：专属特效（扫描波 + 逐单位收拢环 / 螺旋 / 青白复活之光），见 MassRestoreFx。
          // 不再逐单位借用复活术爆发（那条路已由 Style 接管、并统一重染成青白）。
          effects.Add<ContextActionMassRestore>(a =>
          {
            a.CenterOnCaster = true; a.ResurrectMax = 3; a.ResurrectHealth = 0.5f;
            a.Style = "Reconstruction";
          });
          break;
        case "Civilization":
          var after = CreateExtraBuff("CivilizationAfter", Guids.CivilizationAfterBuff,
            L10n.S("spell.Civilization.after.name", "文明的存续（众志）", "Survival of Civilization (as one)"),
            L10n.S("spell.Civilization.after.desc", "攻击、伤害、AC、豁免 +4；第一次将降至 0 生命时保留 1 点。", "+4 on attacks, damage, AC, saves; survive death once at 1 hp."),
            cfg =>
            {
              cfg.AddStatBonus(descriptor: ModifierDescriptor.UntypedStackable, stat: StatType.AdditionalAttackBonus, value: 4);
              cfg.AddStatBonus(descriptor: ModifierDescriptor.UntypedStackable, stat: StatType.AdditionalDamage, value: 4);
              cfg.AddStatBonus(descriptor: ModifierDescriptor.UntypedStackable, stat: StatType.AC, value: 4);
              cfg.AddStatBonus(descriptor: ModifierDescriptor.UntypedStackable, stat: StatType.SaveFortitude, value: 4);
              cfg.AddStatBonus(descriptor: ModifierDescriptor.UntypedStackable, stat: StatType.SaveReflex, value: 4);
              cfg.AddStatBonus(descriptor: ModifierDescriptor.UntypedStackable, stat: StatType.SaveWill, value: 4);
              // C30：免死只生效一次（自定义 CivilizationDeathGuard，Data.Used 按效果隔离）；
              // 原 UnitHealthGuard 在增益期内可反复保命。
              cfg.AddComponent<CivilizationDeathGuard>();
            });
          // 视觉（10-16）：白金圣歌改由 MassRestoreFx 重染成绯红后再播（王冠成型那一下），
          // 所以这里不再单独放 ContextActionSpawnScaledFx（否则会出现一发原色圣光）。
          effects.Add<ContextActionMassRestore>(a =>
          {
            a.CenterOnCaster = true; a.ResurrectMax = 5; a.ResurrectHealth = 1.0f;
            a.AfterBuff = after;
            a.Style = "Civilization";
          });
          break;

        // 画中世界：已从 Special 移到 Debuff（Will 豁免 → 失败麻痹 2 轮），不再走这里。

        // 坚守阵地（作者 10-11）：改成"**持续的区域**"——半径 30 的**圈**，圈内友军获得增益；
        // 视觉只放**一个圈**（纯视觉区域 + 圈特效），不再每个友军各一个（原来按目标数重复生成、且罩子太浮夸）。
        case "HoldTheLine":
        {
          var holdBuff = CreateExtraBuff("HoldTheLine", Guids.Buff(6),
            L10n.S("spell.HoldTheLine.name", "坚守阵地", "Hold the Line"), s.Desc,
            cfg =>
            {
              cfg.AddStatBonus(descriptor: ModifierDescriptor.Insight, stat: StatType.AC, value: 2);
              cfg.AddStatBonus(descriptor: ModifierDescriptor.Insight, stat: StatType.SaveFortitude, value: 1);
              cfg.AddStatBonus(descriptor: ModifierDescriptor.Insight, stat: StatType.SaveReflex, value: 1);
              cfg.AddStatBonus(descriptor: ModifierDescriptor.Insight, stat: StatType.SaveWill, value: 1);
            });
          AbilityAreaEffectConfigurator.New("RhodesDoctorHoldTheLineArea", Guids.HoldTheLineArea)
            .SetShape(AreaEffectShape.Cylinder)
            .SetSize(new Feet(30))
            .AddAbilityAreaEffectBuff(buff: holdBuff, condition: ConditionsBuilder.New().IsAlly())
            .Configure();
          // 圈素材 = 原版"吟游诗人·英勇激励"区域（半径 30，与法术半径**相等、免缩放**）；只染色 + 缩放。
          // 颜色 = **绿**（作者 10-11）。屏幕色 ≈ tint 的 2~3 次幂，所以往"绿"给时 R/B 要压低、G 给满。
          AreaGroundFx.Register(Guids.HoldTheLineArea, FxLibrary.HoldTheLineGround,
            new UnityEngine.Color(0.45f, 1.00f, 0.50f, 1f), textureLift: 0.55f, scale: 1f);
          effects.SpawnAreaEffect(Guids.HoldTheLineArea,
            ContextDuration.Variable(ContextValues.Rank(), DurationRate.Minutes));
          break;
        }

        // 罪罚：意志无效。失败挂减益 1 轮/级：目标每轮第一次对其他生物造成伤害，
        // 自己受一半（单段同类型，多段力场兜底）。
        case "SinPunishment":
        {
          var buff = CreateExtraBuff("SinPunishment", Guids.SinPunishmentBuff,
            L10n.S("spell.SinPunishment.buff.name", "罪罚", "Sin and Punishment"), s.Desc,
            cfg =>
            {
              cfg.AddComponent<SinPunishmentRetribution>();
              cfg.AddFactContextActions(newRound: ActionsBuilder.New()
                .Add<ContextActionResetSinCounter>());
            });
          effects.SavingThrow(SavingThrowType.Will, onResult: ActionsBuilder.New()
            .ConditionalSaved(failed: ActionsBuilder.New()
              .ApplyBuff(buff, ContextDuration.Variable(ContextValues.Rank(), DurationRate.Rounds))));
          break;
        }

        // 连锁殉爆：以目标点 20 尺内全部敌人为标记（TargetsAround 选圈，N9：不再是单体）。
        // 持有者**死亡**时在尸体处爆炸（ChainDetonationOnDeath，20 尺内只伤敌人，10d6 力场反射减半）。
        // 连锁自动成立；"每生物每轮一次"靠死亡一次性满足，复活当轮再死边角不计。
        case "ChainDetonation":
        {
          var mark = CreateExtraBuff("ChainDetonationMark", Guids.ChainDetonationMarkBuff,
            L10n.S("spell.ChainDetonation.mark.name", "连锁殉爆（标记）", "Chain Detonation (marked)"), s.Desc,
            // ⚠ 不能用原版 UnitDeathTrigger：它是"**别人**在我附近死"的触发器，明确排除"自己死"
            //（HandleEvent 开头 `if (unit == Owner) return;`），挂上去标记者自己一死反而没反应。
            // 改用自定义组件：增益被移除时判 IsDead，只在"被杀死"时于尸体处爆炸（与墨灵自爆同一套）。
            cfg => cfg.AddComponent<ChainDetonationOnDeath>());
          effects.ApplyBuff(mark, ContextDuration.Variable(ContextValues.Rank(), DurationRate.Rounds));
          break;
        }

        // 画中世界（10-10 深夜重构）：意志无效。失败 → 收进画境 2 轮（不能动、也打不到），
        // 返回原位后**麻痹 2 轮**。放逐 / 计时 / 放回由挂在**施法者**身上的载体增益
        // （PaintedWorldBanishTracker）负责——目标被 IsInGame=false 后自身增益会被停用，计时器不能挂目标。
        case "PaintedWorld":
        {
          var paralysis = CreateExtraBuff("PaintedWorldParalysis", Guids.PaintedWorldParalysisBuff,
            L10n.S("spell.PaintedWorld.paralysis.name", "画中世界（麻痹）", "Painted World (paralyzed)"), s.Desc,
            cfg => cfg.AddBuffStatusCondition(condition: UnitCondition.Paralyzed, saveEachRound: false, saveType: null));
          var banish = CreateExtraBuff("PaintedWorldBanish", Guids.PaintedWorldBanishBuff,
            L10n.S("spell.PaintedWorld.banish.name", "画中世界（画境）", "Painted World (imprisoned)"), s.Desc,
            cfg => cfg.AddComponent<PaintedWorldBanishTracker>(c => c.ParalysisBuff = paralysis));
          effects.SavingThrow(SavingThrowType.Will, onResult: ActionsBuilder.New()
            .ConditionalSaved(failed: ActionsBuilder.New()
              .ApplyBuff(banish, ContextDuration.Fixed(2), toCaster: true)));
          break;
        }

        // 旧日的战术家（作者 2026-10-11：**改回原设计**——按当前形态"补对端"）。
        //
        // 规则：迅捷动作；个人；你；1 轮/级（原文写 3 轮，作者 10-11 与其他 15 个法术一起统一成轮/级）；
        //   "你同时获得敏捷形态与体质形态的全部收益；转移量额外提高 2。该法术不解除恶灵对普通指令的限制。"
        //
        // 口径（与规则等价，且**不重复吃同一份加成**）：恶灵期间同时只能有一个形态（见 dev-notes §21），
        // 所以"同时获得两个形态的收益" = **当前形态已经在提供的那一半 + 法术补上的另一半**：
        //   · 在**体质形态** → 补**敏捷**套件；在**敏捷形态** → 补**体质**套件；
        //   · **不在任何形态** → 本来就没意义，所以**能力上直接不给放**（作者 10-11："空放要白扣法术位太亏"）：
        //     由 `CreateSpell` 里挂的 `AbilityCasterHasAnyActiveFact` 拦住（按钮置灰 + 施法前复查、不扣法位），
        //     下面 Conditional 的最后那个空分支只是兜底。
        // ⚠ 2026-10-10 深夜为了"任何情况下都能看到光环"，曾把最后那个分支改成"无形态时两套一起给"
        //   （见 report-fx-6-9ring-revise2.md §7），那等于让它在形态外变成一个独立大 buff，**已按作者要求改回**。
        //   ⚠ 同理别改成"两套无条件一起给"：那样在形态内会与形态自身的加值**叠加成双份**。
        // "转移量额外 +2"落在两处：补的那一半用 `TransferRank(false, 4)`（= (转移量+2)×2）给属性，
        //   同时给**形态自己那个属性** +4（转移量 +2 → ×2），于是两个属性都吃到 +2。
        //   形态二段以上才有的"反射闪避 / 伤害减免 3/—"照给（9 环需 CL17，必然已是三段）。
        case "TacticianOfOld":
        {
          var enduringSuite = CreateExtraBuff("TacticianEnduring", Guids.TacticianEnduringBuff,
            L10n.S("spell.TacticianOfOld.enduring.name", "旧日的战术家（坚韧）", "Tactician of Old (enduring)"), s.Desc,
            cfg =>
            {
              cfg.AddContextStatBonus(StatType.Constitution, ContextValues.Rank(), ModifierDescriptor.UntypedStackable);
              cfg.AddContextRankConfig(SpecterSetup.TransferRank(false, 4));
              cfg.AddStatBonus(descriptor: ModifierDescriptor.UntypedStackable, stat: StatType.Dexterity, value: 4);
              cfg.AddComponent<StackingDamageReduction>();
              // 体质形态本身没有"最大敏捷加值"这一项；这一条是"转移量 +2"落在**敏捷形态那一侧**的部分：
              // 敏捷形态的 `MaxDexBonusIncrease` 是 +转移量，所以这里补 +2 → 合计 转移量+2。
              cfg.AddComponent<MaxDexBonusIncrease>(c => c.Bonus = 2);
            });
          var agileSuite = CreateExtraBuff("TacticianAgile", Guids.TacticianAgileBuff,
            L10n.S("spell.TacticianOfOld.agile.name", "旧日的战术家（敏捷）", "Tactician of Old (agile)"), s.Desc,
            cfg =>
            {
              cfg.AddContextStatBonus(StatType.Dexterity, ContextValues.Rank(), ModifierDescriptor.UntypedStackable);
              cfg.AddContextRankConfig(SpecterSetup.TransferRank(false, 4));
              cfg.AddStatBonus(descriptor: ModifierDescriptor.UntypedStackable, stat: StatType.Constitution, value: 4);
              cfg.AddComponent<DexToMeleeAttackDamage>();
              cfg.AddFacts(new List<Blueprint<BlueprintUnitFactReference>> { Guids.Vanilla.EvasionFeature });
              // 补全"敏捷形态的全部收益"里原先漏掉的一项：**护甲最大敏捷加值 + 转移量**
              // （形态自己在 `SpecterSetup.CreateFormBuff` 里给的是 `TransferRankRaw(halved, 0, StatBonus)`，
              //   这里算上"转移量 +2" → `TransferRankRaw(false, 2, StatBonus)`）。
              cfg.AddContextRankConfig(SpecterSetup.TransferRankRaw(false, 2, AbilityRankType.StatBonus));
              cfg.AddComponent<MaxDexBonusIncrease>(c =>
              {
                c.UseContextInstead = true;
                c.ContextBonus = ContextValues.Rank(AbilityRankType.StatBonus);
              });
            });
          var dur = ContextDuration.Variable(ContextValues.Rank(), DurationRate.Rounds);
          effects.Conditional(ConditionsBuilder.New().CasterHasFact(Guids.EnduringBuffV1),
            ActionsBuilder.New().ApplyBuff(agileSuite, dur),
            ActionsBuilder.New().Conditional(ConditionsBuilder.New().CasterHasFact(Guids.EnduringBuffV2),
              ActionsBuilder.New().ApplyBuff(agileSuite, dur),
              ActionsBuilder.New().Conditional(ConditionsBuilder.New().CasterHasFact(Guids.EnduringBuffV3),
                ActionsBuilder.New().ApplyBuff(agileSuite, dur),
                ActionsBuilder.New().Conditional(ConditionsBuilder.New().CasterHasFact(Guids.AgileBuffV1),
                  ActionsBuilder.New().ApplyBuff(enduringSuite, dur),
                  ActionsBuilder.New().Conditional(ConditionsBuilder.New().CasterHasFact(Guids.AgileBuffV2),
                    ActionsBuilder.New().ApplyBuff(enduringSuite, dur),
                    ActionsBuilder.New().Conditional(ConditionsBuilder.New().CasterHasFact(Guids.AgileBuffV3),
                      ActionsBuilder.New().ApplyBuff(enduringSuite, dur),
                      // 不在任何形态：**兜底空放**（正常情况下走不到 —— 能力上挂了
                      // `AbilityCasterHasAnyActiveFact`，没形态时按钮置灰、也点不出来，见 CreateSpell）。
                      ActionsBuilder.New()))))));
          break;
        }

        // 王冠的回响 R4+C37+D3：失败者先夺至多 2 个分给友军，再直接清除剩余（不过驱散检定），并恍惚 1 轮；
        // 成功者随机失去 1 个，直接消失（不分给友军）。
        // 筛选见 ContextActionStealBuffsCustom.IsStealable（可驱散法术增益、敌方施法者）。
        // 顺序必须先偷后清——反过来增益已被清除，夺取动作什么也拿不到。
        case "CrownEcho":
          // 专属自绘特效：头顶黑冠显现 + 60 尺精神织网扩散 + 敌方增益剥离索取（CrownEchoFx.cs）
          // 个人距离法术点击目标为施法者自身，挂在 ClickedTarget 上确保每次施法仅运行一次（无敌人也展开、多敌人不重复叠层）
          ability.AdditionalAbilityEffectRunActionOnClickedTarget(ActionsBuilder.New()
            .Add<ContextActionCrownEchoFx>());
          effects.SavingThrow(SavingThrowType.Will, onResult: ActionsBuilder.New()
            .ConditionalSaved(
              failed: ActionsBuilder.New()
                .Add<RhodesDoctor.Components.ContextActionStealBuffsCustom>(a => a.MaxStolen = 2)
                .Add<RhodesDoctor.Components.ContextActionCrownEchoPurgeRemaining>()
                .ApplyBuff(BlueprintTool.GetRef<BlueprintBuffReference>("d2e35b870e4ac574d9873b36402487e5"), ContextDuration.Fixed(1)),
              succeed: ActionsBuilder.New()
                .Add<RhodesDoctor.Components.ContextActionCrownEchoSuccessRemove>()));
          break;

        // 墨染山河：BPCore 的 SpawnMonster Builder（签名 2.8.7 实测）。
        // 数量 2：ContextDice.Value(DiceType.Zero, 骰数 0, 加值 2)——骰数写 2、加值写 0 会得到 0 只。
        case "InkedMountains":
          effects.SpawnMonster(
            countValue: ContextDice.Value(DiceType.Zero, 0, 2),
            durationValue: ContextDuration.Variable(ContextValues.Rank(), DurationRate.Rounds),
            monster: Guids.InklingUnit,
            afterSpawn: ActionsBuilder.New()
              .ApplyBuffPermanentFixed(Guids.InklingDeathBurstBuff, asChild: true),
            isDirectlyControllable: false); // 墨灵 AI 操控（定案），不是玩家操控
          // 召唤视觉：作者 2026-10-10 定案**去掉**（"能召唤出来就行"，那朵花不要了）。
          // 之前接的自定义动作 ContextActionSpawnFxBetweenSummons 保留在 Actions.cs 备用，未接线。
          break;

        // 镜中流形：引擎自带全量复制 + 行为锁 + 3 轮召唤挂靠（动作本体见 Actions.cs）。
        case "MirrorFlow":
          effects.Add<ContextActionSpawnMirrorCopy>(a =>
            a.LockBuff = BlueprintTool.GetRef<BlueprintBuffReference>(Guids.MirrorLockBuff));
          break;

        // 终末狙击 R7a+D4：标记 1 轮，正常到期才结算（newRound）；被驱散则标记提前消失，不开枪。
        // 隐形哑火在动作里判（D4：掩蔽判断去掉，隐形保留；超射程随 B7 已去掉）。
        case "FinalSnipe":
        {
          var mark = CreateExtraBuff("FinalSnipeMark", Guids.FinalSnipeMarkBuff,
            L10n.S("spell.FinalSnipe.mark.name", "终末狙击（标记）", "Final Snipe (marked)"), s.Desc,
            cfg => cfg.AddFactContextActions(newRound: ActionsBuilder.New()
              .Add<ContextActionFinalSnipe>()));
          effects.ApplyBuff(mark, ContextDuration.Fixed(1));
          break;
        }

        // 永恒的注视：友军增益 1 分钟/级。将死拦截一次：回满、传到施法者身边（EternalGazeWatch）；
        // C36：触发后获得 C34 的加速 1 分钟（由组件在触发时挂上，不拿正在移除的增益当来源）。
        case "EternalGaze":
        {
          var buff = CreateExtraBuff("EternalGaze", Guids.EternalGazeBuff,
            L10n.S("spell.EternalGaze.buff.name", "永恒的注视", "Eternal Gaze"), s.Desc,
            cfg => cfg.AddComponent<EternalGazeWatch>(c => c.AccelBuff = HasteAccel()));
          effects.ApplyBuff(buff, ContextDuration.Variable(ContextValues.Rank(), DurationRate.Minutes));
          break;
        }

        // 归溟：挂自己 3 轮。近战命中回 2d6（轮三次封顶），每轮首次重击/击杀返 1 轮恶灵时间。
        case "ReturnToAbyss":
        {
          var buff = CreateExtraBuff("ReturnToAbyss", Guids.ReturnToAbyssBuff,
            L10n.S("spell.ReturnToAbyss.buff.name", "潮", "Tide"), s.Desc,
            cfg =>
            {
              cfg.AddComponent<ReturnToAbyssHunger>(c =>
                c.SpecterResource = BlueprintTool.GetRef<BlueprintAbilityResourceReference>(Guids.SpecterResource));
              cfg.AddFactContextActions(newRound: ActionsBuilder.New()
                .Add<ContextActionResetAbyssCounters>());
            });
          // 持续 1 轮/级（作者 10-11）。
          effects.ApplyBuff(buff, ContextDuration.Variable(ContextValues.Rank(), DurationRate.Rounds));
          break;
        }

        // 迅捷突破：C34/C35。加速效果（速度 +30 不具类型 + 全力攻击多一次攻击，可与加速术叠加）
        // 持续 3 轮；"下一次近战命中 +3d6"仍只管本轮（单独增益，C28 已去掉 BAB）。
        // 迅捷突破 D2+C34/C35：加速 3 轮（HasteAccel） + 下一次近战 +3d6（本轮） + 本轮内不引发借机攻击。
        // D2：不引发借机攻击用 ImmuneToAttackOfOpportunity 条件实现（反编译 UnitCondition 确认有该条件），
        // 与 BreachStrike 同一增益（1 轮，命中后移除），作用范围只管本轮。
        case "SwiftBreach":
        {
          var buff = CreateExtraBuff("SwiftBreach", Guids.SwiftBreachBuff,
            L10n.S("spell.SwiftBreach.buff.name", "迅捷突破", "Swift Breach"), s.Desc,
            cfg =>
            {
              cfg.AddComponent<BreachStrike>();
              cfg.AddComponent<AddCondition>(c => c.Condition = UnitCondition.ImmuneToAttackOfOpportunity);
            });
          effects.ApplyBuff(buff, ContextDuration.Fixed(1));
          effects.ApplyBuff(HasteAccel(), ContextDuration.Fixed(3));
          break;
        }

        // 王者冲锋（作者 10-11 的"替代办法"）：原版 `AbilityCustomCharge` 挂上去会让**整个法术无效**，
        // "直线投射物 + 传送"两个投递组件又会打架 → 全部改成**一个自定义动作**
        // （`ContextActionRoyalCharge`：施法者冲到落点 + 路径上敌人 4d6 / 失败倒地）。
        // 距离 40 尺（作者定），走"选一点"的落点施法（`SetCanTargetPoint`，同信标投射）。
        case "RoyalCharge":
          effects.Add<ContextActionRoyalCharge>(a =>
          {
            a.MaxDistanceFeet = 40f;
            a.HalfWidthFeet = 5f;
          });
          break;

        // 炮火延伸（定案：一个法术挂逐轮依次爆炸的增益）：
        // 第一轮立即在目标点爆炸；之后两轮由挂在施法者身上的追踪增益
        // 记住目标点、每轮开始各爆一次（组件见 Components.cs）。
        // 已知偏差：规则是三个不同落点依次爆炸，实现为同一施法点三轮（G3 定案：同一点连续三轮）。
        // R3乙：首炸改为动作内扫描 15 尺（原普通单体伤害点地无效）；R1：落点/DC 存 Data。
        case "ArtilleryBarrage":
        {
          var tracker = CreateExtraBuff("ArtilleryBarrage", Guids.ArtilleryBarrageBuff,
            L10n.S("spell.ArtilleryBarrage.buff.name", "炮火延伸（炮击中）", "Artillery Barrage (incoming)"), s.Desc,
            cfg =>
            {
              cfg.AddComponent<ArtilleryBarrageTracker>();
              cfg.AddFactContextActions(newRound: ActionsBuilder.New()
                .Add<ContextActionArtilleryStrike>());
            });
          effects.Add<ContextActionArtilleryInitialBurst>();
          effects.ApplyBuff(tracker, ContextDuration.Fixed(2), toCaster: true);
          break;
        }

        // ─── v0.4.0 好做批 ───

        // 影霄·拔刀：强韧部分通过。失败即死（ContextActionKill，放逐术同款）；
        // 成功仍受 10d6 + 圣武士等级伤害。免疫即死者走 Kill 规则天然不受影响，视为豁免成功。
        case "ShadowBladeDraw":
        {
          // 视觉（作者 2026-10-10：原来借"指骨术"的死亡爆发不行，要"红剑砍人"）：
          // 复用剑雨那套**武器模型**（巨剑网格）——但只落**一把**、落在目标身上、重染**血红**，
          // 配一条自绘红色光尾（`GoldenStreak` 用的是同一份 Tint）。挂在"点击目标"动作上（同剑雨）。
          var blade = FxLibrary.SwordRainBlade;
          if (blade != null)
            ability.AdditionalAbilityEffectRunActionOnClickedTarget(ActionsBuilder.New()
              .Add<ContextActionSpawnFxRing>(a =>
              {
                a.PrefabLink = blade;
                a.Count = 1;                  // 只一把
                a.FitLongestMeters = 6.0f;    // 米：剑的长度（作者 10-10：放大几倍）
                a.Pitch = 180f;               // 剑尖朝下，自上方劈落
                a.OffsetUp = 1.2f;            // 米：抬高落点，让剑插在地上
                a.FallFromMeters = 7f;        // 米：从多高处落下
                a.FallSeconds = 0.35f;        // 秒：伤害是施法瞬间结算的，落剑紧跟其后
                a.Tint = ShadowBladeRed;      // 血红
                a.Lift = 0.55f;
                a.Lifetime = 1.5f;            // 秒：落地后留存
                a.GoldenStreak = true;
                a.StreakSeconds = 0.25f;
                a.StreakWidth = 0.12f;
              }));
          effects.SavingThrow(SavingThrowType.Fortitude, onResult: ActionsBuilder.New()
            .ConditionalSaved(
              failed: ActionsBuilder.New().Kill(),
              succeed: ActionsBuilder.New().DealDamage(
                DamageTypes.Energy(DamageEnergyType.Magic), // 未定型伤害走 Magic 能量（程序集确认有该类型）
                ContextDice.Value(DiceType.D6, 10, ContextValues.Rank()),
                halfIfSaved: false)));
          break;
        }

        // 缄默：意志无效。失败挂减益：禁法术与类法术（ForbidSpellCasting）+
        // 禁非凡与超自然主动能力（ExAndSuAbilitiesForbidden）——镜像行为锁同款两件套，去掉禁魔法物品。
        case "Silencer":
        {
          var debuff = CreateExtraBuff("SilencerDebuff", Guids.SilencerDebuffBuff,
            L10n.S("spell.Silencer.debuff.name", "缄默", "Silenced"), s.Desc,
            cfg =>
            {
              cfg.AddComponent<ForbidSpellCasting>(c => c.ForbidMagicItems = false);
              cfg.AddComponent<AddCondition>(c => c.Condition = UnitCondition.ExAndSuAbilitiesForbidden);
            });
          effects.SavingThrow(SavingThrowType.Will, onResult: ActionsBuilder.New()
            .ConditionalSaved(failed: ActionsBuilder.New()
              .ApplyBuff(debuff, ContextDuration.Variable(ContextValues.Rank(), DurationRate.Rounds))));
          break;
        }

        // 烟幕掩护：20 尺点，1 轮/级。区域内敌人隐蔽（Fog/Partial，即 20% 失手）；
        // 友军不挂增益故正常视物；强风驱散按 G7 删除（游戏里没有风）。
        // 视觉借恶臭云区域的云雾（StinkingCloudArea，备选 Cloudkill/Sirocco/Grease）。
        case "Smokescreen":
        {
          var conceal = CreateExtraBuff("SmokescreenConceal", Guids.SmokescreenConcealBuff,
            L10n.S("spell.Smokescreen.conceal.name", "烟幕掩护（隐蔽）", "Smokescreen (concealed)"), s.Desc,
            cfg => cfg.AddConcealment(concealment: Concealment.Partial, descriptor: ConcealmentDescriptor.Fog));
          var area = AbilityAreaEffectConfigurator.New("RhodesDoctorSmokescreenArea", Guids.SmokescreenArea)
            .SetShape(AreaEffectShape.Cylinder)
            .SetSize(new Feet(20))
            .AddAbilityAreaEffectBuff(buff: conceal)
            .Configure();
          SetPrivateField(area, "m_TargetType", System.Enum.Parse(
            typeof(BlueprintAbilityAreaEffect).GetNestedType("TargetType",
              System.Reflection.BindingFlags.NonPublic),
            "Ally")); // B5：隐蔽给友军（旧版给了敌人）
          var smokeFx = FxBorrow.AreaFx(
            AbilityAreaEffectRefs.StinkingCloudArea.ToString(),
            AbilityAreaEffectRefs.CloudkillArea.ToString(),
            AbilityAreaEffectRefs.SiroccoArea.ToString(),
            AbilityAreaEffectRefs.GreaseArea.ToString());
          // 灰白烟由 SmokeFx 运行时克隆重染（跟区域同生同灭），这里不挂 area.Fx，
          // 否则绿云原样上屏。借不到则模板缺失，功能不受影响。
          SmokeFx.SourceLink = smokeFx;
          effects.SpawnAreaEffect(Guids.SmokescreenArea,
            ContextDuration.Variable(ContextValues.Rank(), DurationRate.Rounds));
          break;
        }

        // 信标投射：20 尺定点区域，10 分钟/级。C32+N9：只保留照明（淡金标记光），不再移除隐形、镜影、掩蔽。
        // 视觉两件：
        //   ① 物体 = 竖直光柱（借原版"生命之柱"PillarOfLife 的 Fx，FxLibrary.BeaconPillar），
        //      由 BeaconFx 克隆重染成蓝白、放到区域中心、跟区域同生同灭；
        //   ② 周围光 = 照明术（MageLight 增益的 FxOnStart），挂在区域上（area.Fx）。
        //   （旧版借"天使之貌"当光、借"发光石"当物体，实测画面上要么没有、要么是块实心蓝石头；
        //     自绘光柱用户也不满意，要求复用原版素材，故改借生命之柱。敌方淡金标记光仍保留。）
        // 已知偏差：真光照与移动信标无接口，区域固定。
        case "BeaconProjection":
        {
          var beaconLight = FxLibrary.BeaconGlow;       // 照明术的周围光
          var beaconMarker = FxBorrow.BuffAura(         // 敌方淡金标记光（原设计保留）
            BuffRefs.AngelicAspectBuff.ToString(),
            BuffRefs.HasteBuff.ToString());
          var reveal = CreateExtraBuff("BeaconReveal", Guids.BeaconRevealBuff,
            L10n.S("spell.BeaconProjection.reveal.name", "信标投射（显形）", "Beacon Projection (revealed)"), s.Desc,
            cfg =>
            {
              // C32：只保留照明；去掉移除隐形、镜影、掩蔽的效果。
              if (beaconMarker != null) cfg.SetFxOnStart(beaconMarker);
            });
          var beaconArea = AbilityAreaEffectConfigurator.New("RhodesDoctorBeaconArea", Guids.BeaconArea)
            .SetShape(AreaEffectShape.Cylinder)
            .SetSize(new Feet(20))
            .AddAbilityAreaEffectBuff(buff: reveal)
            .Configure();
          SetPrivateField(beaconArea, "m_TargetType", System.Enum.Parse(
            typeof(BlueprintAbilityAreaEffect).GetNestedType("TargetType",
              System.Reflection.BindingFlags.NonPublic),
            "Enemy"));
          if (beaconLight != null) beaconArea.Fx = beaconLight;
          effects.SpawnAreaEffect(Guids.BeaconArea,
            ContextDuration.Variable(ContextValues.Rank(), DurationRate.Minutes));
          break;
        }

        // 紧急撤离：接触友军，传送到施法者身边。不引发借机攻击，不掷战技检定。
        // （此前用 CombatManeuver Pull，会刷"拖拽"日志并过战技检定，已换掉。）
        case "EmergencyExtraction":
          effects.Add<ContextActionTeleportAllyToCaster>();
          break;

        // 儿时舞乐 C27：40 尺区域 4 轮。敌人意志豁免（进场与每轮开始，仿影之疆域影触），失败缓慢且受伤 +30%；
        // 友军每轮恢复 2d8 + 半级。离开区域后敌方效果结束（出圈撤触，缓慢 1 轮自然到期）。
        case "FoxfireDance":
        {
          var slow = CreateExtraBuff("FoxfireDanceSlow", Guids.FoxfireSlowBuff,
            L10n.S("spell.FoxfireDance.slow.name", "儿时的舞乐（迟缓）", "Dance of Childhood (slowed)"), s.Desc,
            cfg =>
            {
              cfg.AddComponent<AddCondition>(c => c.Condition = UnitCondition.Slowed);
              cfg.AddComponent<TakeExtraDamagePercent>(c => c.Percent = 30);
            });
          System.Func<ActionsBuilder> foxSave = () => ActionsBuilder.New()
            .SavingThrow(SavingThrowType.Will, onResult: ActionsBuilder.New()
              .ConditionalSaved(failed: ActionsBuilder.New()
                .ApplyBuff(slow, ContextDuration.Fixed(1))));
          var touch = CreateExtraBuff("FoxfireTouch", Guids.FoxfireTouchBuff,
            L10n.S("spell.FoxfireDance.slow.name", "儿时的舞乐（迟缓）", "Dance of Childhood (slowed)"), s.Desc,
            cfg =>
            {
              cfg.AddFactContextActions(activated: foxSave());
              cfg.AddFactContextActions(newRound: foxSave());
            });
          var regen = CreateExtraBuff("FoxfireDanceRegen", Guids.FoxfireRegenBuff,
            L10n.S("spell.FoxfireDance.regen.name", "儿时的舞乐（回生）", "Dance of Childhood (mending)"), s.Desc,
            cfg =>
            {
              // B2：回血要读施法者等级（旧版区域内没有等级配置，读成 0 被兜底成 1）。
              cfg.AddContextRankConfig(BuffRank(l => l));
              cfg.AddFactContextActions(newRound: ActionsBuilder.New()
                .Add<ContextActionAllyRegen>());
            });
          AbilityAreaEffectConfigurator.New("RhodesDoctorFoxfireArea", Guids.FoxfireArea)
            .SetShape(AreaEffectShape.Cylinder)
            .SetSize(new Feet(20))
            .AddAbilityAreaEffectBuff(buff: touch, condition: ConditionsBuilder.New().IsEnemy())
            .AddAbilityAreaEffectBuff(buff: regen, condition: ConditionsBuilder.New().IsAlly())
            .Configure();
          // 视觉（C 类·持续地面区域）：借原版"心灵迷雾"区域的常驻 Fx（素材 Cylinder/20），
          // 染成"童年的柔暖淡紫"。法术半径已从 40 改为 20，素材半径 20 = 法术半径，**免缩放**（scale=1）。
          // 做法与钙质化同源（AreaGroundFx 注册表），但**不传 calcProfile**——
          // 那套部件手术只对黑曜石碎壳成立，别的素材跑它会误删自己的网格/贴花（见 AreaGroundFx.Spec）。
          // 颜色与烟幕（冷灰白）、钙质化（冷白）拉开：这里取**偏暖的淡紫**，一眼能分开（dev-notes §7.11.4）。
          AreaGroundFx.Register(Guids.FoxfireArea, FxLibrary.FoxfireGround,
            new UnityEngine.Color(0.96f, 0.86f, 0.98f, 1f), textureLift: 0.9f, scale: 1f);
          effects.SpawnAreaEffect(Guids.FoxfireArea,
            ContextDuration.Variable(ContextValues.Rank(), DurationRate.Rounds));
          break;
        }

        // 处决：挂上蓄势增益（组件见 Components.cs），1 轮/级或触发后结束。
        case "Execution":
        {
          var buff = CreateExtraBuff("Execution", Guids.ExecutionBuff,
            L10n.S("spell.Execution.buff.name", "处决（蓄势）", "Execution (readied)"), s.Desc,
            cfg => cfg.AddComponent<ExecutionStrike>());
          effects.ApplyBuff(buff, ContextDuration.Variable(ContextValues.Rank(), DurationRate.Rounds));
          break;
        }

        // 钙质化 C26+E6：20 尺区域 3 轮。敌方速度减半（IronBody 同款 BuffMovementSpeed Value0/Cap0.5，
        // 不再用 Slowed 近似）+ 豁免 −2；友军每轮 1d8。出圈自动撤，符合"离开即结束"。
        case "Calcification":
        {
          var enemyDebuff = CreateExtraBuff("CalcificationEnemy", Guids.CalcificationEnemyBuff,
            L10n.S("spell.Calcification.enemy.name", "钙质化（凝滞）", "Calcification (hindered)"), s.Desc,
            cfg =>
            {
              cfg.AddComponent<Kingmaker.Designers.Mechanics.Buffs.BuffMovementSpeed>(c =>
              {
                c.Value = 0;
                c.CappedOnMultiplier = true;
                c.MultiplierCap = 0.5f;
              });
              cfg.AddStatBonus(descriptor: ModifierDescriptor.UntypedStackable, stat: StatType.SaveFortitude, value: -2);
              cfg.AddStatBonus(descriptor: ModifierDescriptor.UntypedStackable, stat: StatType.SaveReflex, value: -2);
              cfg.AddStatBonus(descriptor: ModifierDescriptor.UntypedStackable, stat: StatType.SaveWill, value: -2);
            });
          var allyBuff = CreateExtraBuff("CalcificationAlly", Guids.CalcificationAllyBuff,
            L10n.S("spell.Calcification.ally.name", "钙质化（治疗）", "Calcification (healing)"), s.Desc,
            cfg => cfg.AddFactContextActions(newRound: ActionsBuilder.New()
              .HealTarget(ContextDice.Value(DiceType.D8, 1, 0))));
          AbilityAreaEffectConfigurator.New("RhodesDoctorCalcificationArea", Guids.CalcificationArea)
            .SetShape(AreaEffectShape.Cylinder)
            .SetSize(new Feet(20))
            .AddAbilityAreaEffectBuff(buff: enemyDebuff, condition: ConditionsBuilder.New().IsEnemy())
            .AddAbilityAreaEffectBuff(buff: allyBuff, condition: ConditionsBuilder.New().IsAlly())
            .Configure();
          // 视觉（C 类·持续地面区域）：**克隆重染**一片苍白石壳，跟区域同生同灭。
          // 为什么不用 area.Fx：那是引擎直接播的 PrefabLink，**改不了色**；而原版没有天然是
          // "钙化的苍白石壳"那个颜色的地面素材，所以必须自己染（做法见 AreaGroundFx）。
          // 素材 = 原版黑曜流的**碎壳**（形状是"地面结壳"，不是云也不是泥——"致命大地"那套是泥浆飞溅，已被否掉）。
          // 色 = 石灰岩的**冷白**（这就是"钙"）。给色的三条经验（都踩过，别退回）：
          //   ① **要往白里给**：tint 会被"材质色 × 贴图色"乘上两三次，屏幕色 ≈ tint 的 2~3 次幂，
          //      所以 (0.86,0.84,0.76) 那版上屏是**土褐**、不是石白；
          //   ② **通道差会被幂次放大**：`(0.95,0.94,0.90)` 看着只是"微暖"，平方后 R−G 与 G−B 各翻一倍，
          //      上屏就成了明显的**米黄/奶油**（用户："颜色更对一点"）；
          //   ③ 所以取 **中性偏冷** `(0.96,0.96,0.97)`——R=G、B 略高，幂次放大后仍是"冷白"（饱和仅 0.02）。
          // 贴图提亮 0.88：素材（黑曜石）本来就近黑，提亮低了主体染不动（更早那版就是"黑壳 + 白裂缝"）。
          // scale=1：素材 Cylinder/20 与法术半径 20 相等，免缩放。
          // 借不到则区域没有地面特效，功能完全不受影响，只打一行日志。
          // 贴图提亮给到 **1.0**：这一档下 `贴图 = tint × lerp(原亮度, 1, 1) = tint × 1 = tint`，
          // 即"贴图被抹成纯目标色"，**不再依赖原素材的亮度**——原版这套 FX 里有一层
          // （`Owlcat/Decals/Projector` 的投影贴花 + `_ColorAlphaRamp=Gradient_Black00` 的泥潭）
          // 底色偏黑，靠"提亮"是顶不动的（提亮只改"贴图 × 颜色"，改不了烘在 LUT 里的黑），
          // 只有抹平才**保证**那一层也变成目标色。代价是失去贴图本身的明暗纹理。
          // 等确认不再发黑后，可以往回收（0.5~0.7）换回一点石头纹理。
          // scale 1.1 = 用户要的"扩大一点"。⚠️ 这个旋钮能生效是因为 AreaGroundFx 把粒子的
          // `scalingMode` 改成了 `Hierarchy`（默认 Local 模式下粒子不跟根节点缩放走，
          // 所以以前 scale 对粒子无效）。想更大/更小就只改这一个数。
          // calcProfile: true = 跑"钙质化专用"的那套部件手术（关 hole/fountain/puddle/cycle、
          // 销毁外扩白环 groundfire、把 wave 云铺满范围、压投影贴花 Alpha）。那一段只对黑曜流碎壳成立，
          // 所以默认关闭、这里显式打开；别的 C 类素材不传（走"只染色 + 缩放"）。
          AreaGroundFx.Register(Guids.CalcificationArea, FxLibrary.CalcificationGround,
            new UnityEngine.Color(0.96f, 0.96f, 0.97f, 1f), textureLift: 1.0f, calcProfile: true);
          effects.SpawnAreaEffect(Guids.CalcificationArea,
            ContextDuration.Variable(ContextValues.Rank(), DurationRate.Rounds));
          break;
        }

        // 旗帜不倒：近距内选一点为中心，30 尺爆发内全部友军各挂增益，0 血以下不死不昏、正常行动；
        // 到期按通常规则结算（增益消失自动恢复）。保底用 UnitHealthGuard（文明后续同款；昏迷是否同样免除见 X15）。
        case "BannerStands":
        {
          var buff = CreateExtraBuff("BannerStands", Guids.BannerStandsBuff,
            L10n.S("spell.BannerStands.buff.name", "旗帜不倒", "The Banner Stands"), s.Desc,
            cfg => cfg.AddComponent<UnitHealthGuard>(c => c.HealthPercent = 1));
          effects.ApplyBuff(buff, ContextDuration.Variable(ContextValues.Rank(), DurationRate.Rounds));
          break;
        }

        // 岁稔：C1 改为近距内一点为中心 30 尺爆发内的全部友军；每轮 4d8 + 等级，溢出积攒、
        // 结束按上限 60 转临时生命（1 分钟）。B2：等级配置补在增益上。
        case "HarvestYears":
        {
          // 余温增益先建出来（承载结束时的临时生命，1 分钟）。
          CreateExtraBuff("HarvestYearsResidual", Guids.HarvestYearsResidualBuff,
            L10n.S("spell.HarvestYears.residual.name", "岁稔（余温）", "Harvest of Years (lingering)"),
            L10n.S("spell.HarvestYears.residual.desc", "把未用完的治疗转为临时生命，持续 1 分钟。",
              "Unused healing becomes temporary hit points for 1 minute."),
            cfg => cfg.AddComponent<HarvestYearsResidual>());
          var buff = CreateExtraBuff("HarvestYears", Guids.HarvestYearsBuff,
            L10n.S("spell.HarvestYears.buff.name", "岁稔（积攒）", "Harvest of Years (accumulating)"), s.Desc,
            cfg =>
            {
              cfg.AddContextRankConfig(BuffRank(l => l)); // B2：每轮治疗读得到施法者等级
              cfg.AddComponent<HarvestYearsAccumulator>(c =>
                c.ResidualBuff = BlueprintTool.GetRef<BlueprintBuffReference>(Guids.HarvestYearsResidualBuff));
              cfg.AddFactContextActions(newRound: ActionsBuilder.New()
                .Add<ContextActionHarvestTick>());
            });
          effects.ApplyBuff(buff, ContextDuration.Fixed(3));
          break;
        }

        // 牵引：C2 保留"范围内的敌人"，去掉多出来的战技检定，只留强韧豁免。
        // 失败者被传送到法术点附近并倒地（落点用 FreePlaceSelector 找空地）。
        case "Dragnet":
          effects.SavingThrow(SavingThrowType.Fortitude, onResult: ActionsBuilder.New()
            .ConditionalSaved(failed: ActionsBuilder.New()
              .Add<ContextActionTeleportToPoint>()
              .ApplyBuffPermanentFixed("24cf3deb078d3df4d92ba24b176bda97")));
          break;

        // 影之疆域（移动领域）：载体挂目标友军（含自己），30 尺领域随人走。
        // B3/V3：原版灵光机制是"持有者增益 → AddAreaEffect → 区域 → 分挂增益"，
        // 旧版用单个 AuraFeatureComponent 把增益挂在持有者自己身上（且敌我弄反）。
        // 现在改为：区域对友军挂隐形、对敌人挂影触（进场与每轮意志豁免，失败目盲 1 轮，成功目眩 1 轮）。出圈自动撤。
        case "ShadowRealm":
        {
          var blind = CreateExtraBuff("ShadowRealmBlind", Guids.ShadowRealmBlindBuff,
            L10n.S("spell.ShadowRealm.blind.name", "影之疆域（致盲）", "Shadow Realm (blinded)"), s.Desc,
            cfg => cfg.AddComponent<AddCondition>(c => c.Condition = UnitCondition.Blindness));
          var hide = CreateExtraBuff("ShadowRealmHide", Guids.ShadowRealmHideBuff,
            L10n.S("spell.ShadowRealm.hide.name", "影之疆域（隐形）", "Shadow Realm (invisible)"), s.Desc,
            cfg => cfg.AddComponent<AddCondition>(c => c.Condition = UnitCondition.Invisible));
          // G12：失败目盲 1 轮，成功目眩 1 轮（原“视线受阻”）。
          var dazzle = CreateExtraBuff("ShadowRealmDazzle", Guids.ShadowRealmDazzleBuff,
            L10n.S("spell.ShadowRealm.dazzle.name", "影之疆域（目眩）", "Shadow Realm (dazzled)"), s.Desc,
            cfg => cfg.AddComponent<AddCondition>(c => c.Condition = UnitCondition.Dazzled));
          System.Func<ActionsBuilder> saveBlind = () => ActionsBuilder.New()
            .SavingThrow(SavingThrowType.Will, onResult: ActionsBuilder.New()
              .ConditionalSaved(
                failed: ActionsBuilder.New()
                  .ApplyBuff(blind, ContextDuration.Fixed(1)),
                succeed: ActionsBuilder.New()
                  .ApplyBuff(dazzle, ContextDuration.Fixed(1))));
          var touch = CreateExtraBuff("ShadowRealmTouch", Guids.ShadowRealmTouchBuff,
            L10n.S("spell.ShadowRealm.touch.name", "影之疆域（影触）", "Shadow Realm (touch)"), s.Desc,
            cfg =>
            {
              cfg.AddFactContextActions(activated: saveBlind());
              cfg.AddFactContextActions(newRound: saveBlind());
            });
          AbilityAreaEffectConfigurator.New("RhodesDoctorShadowRealmArea", Guids.ShadowRealmArea)
            .SetShape(AreaEffectShape.Cylinder)
            .SetSize(new Feet(15))   // 半径 15 尺（作者 2026-10-10 由 30 改小）
            .AddAbilityAreaEffectBuff(buff: hide, condition: ConditionsBuilder.New().IsAlly())
            .AddAbilityAreaEffectBuff(buff: touch, condition: ConditionsBuilder.New().IsEnemy())
            .Configure();
          // 视觉（C 类·**移动**地面区域）：借原版"极夜"区域的常驻 Fx（素材 Cylinder/30 = 法术半径，免缩放），
          // 染成**暗紫**。区域挂在目标友军身上（`AddAreaEffect`）会跟着走，
          // 所以克隆体要每帧跟住区域位置（`AreaGroundFx.FollowArea`）。
          AreaGroundFx.Register(Guids.ShadowRealmArea, FxLibrary.ShadowRealmGround,
            new UnityEngine.Color(0.52f, 0.34f, 0.82f, 1f), textureLift: 0.80f, scale: 0.5f);  // 素材 30 尺 → 法术 15 尺
          var holder = CreateExtraBuff("ShadowRealmHolder", Guids.ShadowRealmHolderBuff,
            L10n.S("spell.ShadowRealm.holder.name", "影之疆域（领域）", "Shadow Realm (field)"), s.Desc,
            cfg => cfg.AddAreaEffect(Guids.ShadowRealmArea));
          effects.ApplyBuff(holder, ContextDuration.Variable(ContextValues.Rank(), DurationRate.Rounds));
          break;
        }

        // 耀阳颔首：120 尺直线。敌人 15d6 神圣伤害（反射减半，失败目盲 1 轮/级，成功目眩 1 轮）；
        // 友军恢复 10d6 并解除目盲。此前走通用 Damage 分支：会误伤友军且没有治疗。
        // 伤害固定 15d6（不随等级），目盲持续 1 轮/级走 BuffRank(l=>l)。
        // N15：成功者目眩 1 轮（设计文档第 248 行）。
        case "BlazingSunBow":
        {
          var blind = CreateExtraBuff("BlazingSunBlind", Guids.BlazingSunBlindBuff,
            L10n.S("spell.BlazingSunBow.blind.name", "耀阳颔首（目盲）", "Blazing Sun Bow (blinded)"), s.Desc,
            cfg => cfg.AddComponent<AddCondition>(c => c.Condition = UnitCondition.Blindness));
          var dazzle = CreateExtraBuff("BlazingSunDazzle", Guids.BlazingSunDazzleBuff,
            L10n.S("spell.BlazingSunBow.dazzle.name", "耀阳颔首（目眩）", "Blazing Sun Bow (dazzled)"), s.Desc,
            cfg => cfg.AddComponent<AddCondition>(c => c.Condition = UnitCondition.Dazzled));
          var holyDice = ContextDice.Value(DiceType.D6, 15, 0);
          var healDice = ContextDice.Value(DiceType.D6, 10, 0);
          effects.Conditional(ConditionsBuilder.New().IsEnemy(),
            ActionsBuilder.New().SavingThrow(SavingThrowType.Reflex, onResult: ActionsBuilder.New()
              .ConditionalSaved(
                failed: ActionsBuilder.New()
                  .DealDamage(DamageTypes.Energy(DamageEnergyType.Holy), holyDice, halfIfSaved: true)
                  .ApplyBuff(blind, ContextDuration.Variable(ContextValues.Rank(), DurationRate.Rounds)),
                succeed: ActionsBuilder.New()
                  .DealDamage(DamageTypes.Energy(DamageEnergyType.Holy), holyDice, halfIfSaved: true)
                  .ApplyBuff(dazzle, ContextDuration.Fixed(1)))),
            ActionsBuilder.New()
              .HealTarget(healDice)
              .Add<ContextActionRemoveBlindness>());
          break;
        }

        // 延时爆破 C33/R3乙+N2：在地面布设炸弹（追踪增益记点，1 轮/级）；敌人进入 10 尺下轮开始爆炸；
        // 博士可用自由动作手动引爆（单独能力）；伤害 2d6 火焰 + 2d6 钝击，反射减半。
        // R1：用独立 DelayedBlastTracker Data，不借用炮火记录。
        // N2：追踪增益通过 AddFacts 授予手动引爆能力（恶灵结束能力同款做法：形态增益 AddFacts 结束能力；
        // 原版 AddFacts 可挂任意 BlueprintUnitFact，含能力），有炸弹时能力出现，炸弹清完后移除。
        case "DelayedBlast":
        {
          // 手动引爆能力（自由动作，施放即引爆全部自设炸弹）先建，供追踪增益引用。
          var detonate = AbilityConfigurator.New("RhodesDoctorDelayedBlastDetonate", Guids.DelayedBlastDetonateAbility)
            .SetDisplayName(L10n.S("spell.DelayedBlast.name", "延时爆破", "Delayed Blast"))
            .SetDescription(L10n.S("spell.DelayedBlast.desc", "学派：塑能（火焰）；标准动作；中距；1 轮/级或直到触发；反射减半。在地面放置炸弹：每轮开始时，炸弹 10 尺内有敌人就爆炸（10 尺爆发 2d6 火焰 + 2d6 轰击）；你也可以用自由动作手动引爆。——W", "Evocation (fire); standard; medium; 1 round/level or until triggered; Reflex half. Place a bomb: at the start of each round it explodes if an enemy is within 10 ft. (10-ft. burst, 2d6 fire + 2d6 bludgeoning); you may also detonate it as a free action. (W)"))
            .SetActionType(UnitCommand.CommandType.Free)
            .SetRange(AbilityRange.Personal)
            .SetCanTargetSelf(true)
            .AddAbilityEffectRunAction(ActionsBuilder.New().Add<ContextActionDelayedBlastBurst>())
            .Configure();
          _ = detonate;
          // 地面炸弹标记：借“符文领域”的地面符文（常驻循环、5 尺圆，像一枚放在地上的延时炸弹）。
          // 不写到区域蓝图的 area.Fx 上——实测借来的区域 FX 不上屏；改成运行时自己实例化，
          // 由 DelayedBlastTracker 组件随炸弹同生同灭（BombFx.PlaceMarker / DestroyMarker）。
          BombFx.MarkerSourceLink = FxBorrow.AreaFx(
            AbilityAreaEffectRefs.RuneDomainBaseAbilityFireArea.ToString(),
            AbilityAreaEffectRefs.RuneOfJandelayArea.ToString(),
            AbilityAreaEffectRefs.CorrosionBombArea.ToString(),
            AbilityAreaEffectRefs.GreaseArea.ToString());
          // 引爆：借游侠爆破陷阱里的一次性爆发 FX（区域 UnitEnter 的 ContextActionSpawnFx）。
          BombFx.BoomLink = FxBorrow.AreaBurstFx(
            AbilityAreaEffectRefs.RangerExplosivesTrapArea.ToString(),
            AbilityAreaEffectRefs.RangerHolyBombArea.ToString())
            ?? BombFx.MarkerSourceLink;
          var tracker = CreateExtraBuff("DelayedBlastBomb", Guids.DelayedBlastBombBuff,
            L10n.S("spell.DelayedBlast.bomb.name", "延时爆破（炸弹）", "Delayed Blast (bomb)"), s.Desc,
            cfg =>
            {
              cfg.AddComponent<DelayedBlastTracker>();
              cfg.AddFactContextActions(newRound: ActionsBuilder.New()
                .Add<ContextActionDelayedBlastRound>());
              cfg.AddFacts(new List<Blueprint<BlueprintUnitFactReference>> { Guids.DelayedBlastDetonateAbility });
            });
          effects.ApplyBuff(tracker, ContextDuration.Variable(ContextValues.Rank(), DurationRate.Rounds), toCaster: true);
          // 地面标记由 DelayedBlastTracker 组件自己生成/销毁，这里不再挂区域。
          break;
        }

        // 剑雨 C8：每 2 级 1d6 挥砍（最多 10d6），反射减半；失败者恍惚 1 轮（原版 Daze 增益）。
        case "SwordRain":
        {
          ability.AddContextRankConfig(DiceRank(s));
          // 视觉：AoE 是"半径 10 尺"（AoERadius=10 → 直径 20 尺），特效必须**落在红圈里**。
          //
          // 走过的弯路（别再回头）：
          //   ① 刃障 `BladeBarrierArea` 是 `Shape=Wall, Size=60`——**一条直线墙**，不是圆。
          //      摆 12 份想拼圆，直段既漏又叠，而且尺寸猜错（0.05），堆成一整片糊。
          //   ② 改成 1 份 + 缩小后又发现：它本质就是一条墙，**再小也还是个"条"**。
          //   ③ 锡斯胡德 `DLC5_SwordOfSithhudAreaEffect` 同样是 Wall（Size=180），同样不可用。
          //      也就是说：原版**没有**"圆形剑阵"的地面 FX，靠借是借不到的。
          //   ④ 借投射物外观摆环：圆是对了，但借到的是**飞行拖尾**（金色光弧），不像剑。
          //   ⑤ 借武器模型（剑身网格）：形状对了，但剑是**竖着**的，长度在 Y 轴上，
          //      而 FitToMeters 只量水平面 → 只量到剑身厚度 → 整把剑被放大好几倍，位置看着也不对。
          //      竖直物件必须用 FitLongestMeters。
          //   ⑥ 拖尾曾写在第二个 AdditionalAbilityEffectRunActionOnClickedTarget 里 = **从来没上过屏**：
          //      该方法走 AddUniqueComponent（mergeBehavior = ComponentMerge.Fail），**同类型组件只允许一个**，
          //      第二次调用不追加也不报错，只在 GameLogFull.txt 留一行
          //      "Multiple AdditionalAbilityEffectRunActionOnClickedTarget present but only one is allowed."
          //      → 要挂多个落点效果，就把它们放进**同一个 ActionsBuilder** 里 Add 多次。
          //   ⑦ 于是改成"剑身 + 借粒子素材当拖尾"：借到的投射物"阳炎之刃"外观是**数百米量级的巨型光柱**
          //      （`[特效尺寸]` 实测 345.6x62.3x343.8 m、约 40 个粒子系统），FitToMeters 缩到 1.4 m 后
          //      画面上仍是一整根橙色大柱子，18 条糊成一根天量光柱（用户截图实证）。**粒子素材的可见
          //      尺寸不跟根节点缩放走，包围盒自适应对粒子素材不可靠**（dev-notes §0 早有此条，这是第二次踩）。
          // 现在：武器模型（剑）+ 同时从天而降 + **整齐的双环列阵** + **自绘的细金光带**
          //      （不再借任何粒子素材，见 ① 与 ②；2026-10-09 按用户要求把"随机乱插"改成"整齐列阵"）。
          //
          // 几何：外环半径 2.4 m、内环 1.2 m，每把剑约 1.5 m 长（笔直竖立，向外最多再占半把）
          // → 基本压在 AoE 半径 10 尺 = 3.05 m 的圈里；剑数 18（内 9 外 9）。
          var swordBlade = FxLibrary.SwordRainBlade;
          if (swordBlade != null)
          {
            // ① 剑身：从天而降、**整齐列阵**（2026-10-09 按用户要求改，之前是"圆盘内随机、横七竖八"）。
            //    用户原话："金光尾拖其实是整齐的，不如把剑也弄整齐，别让它们歪歪扭扭的了"。
            //    所以随机量全部归零，只留"同时落下"这一条：
            //    · Rings=2 + RadiusJitter=0 → 同心双环、等角分布（不是圆盘内乱撒）
            //    · TiltJitter / YawJitter = 0 → 全部笔直竖立、朝向随环一致，没有歪斜
            //    · ScaleJitter / OffsetUpJitter = 0 → 大小一致、插地深度一致
            //    · FallSecondsJitter = 0 → **同时落下**：全员同帧出生、同高、同耗时，一起插到地上
            //    · FallSeconds = 0.4（原 1.1）：**伤害是施法瞬间结算的**，慢慢飘下来会出现
            //      "伤害数字先出来、剑还没落地"的怪感（用户实证）。现在 9 m 落 0.4 s，
            //      几乎是数字一跳、剑同时砸到地上。别再为了"看得清"把它拉长。
            //      ★ 命中金光的 Delay 也必须跟着这个值走（见 ContextActionSwordHitFx）。
            //    Pitch=180 把模型倒过来（剑尖朝下）；OffsetUp≈剑长×0.8 让剑尖插进地面一点。
            ability.AdditionalAbilityEffectRunActionOnClickedTarget(ActionsBuilder.New()
              .Add<ContextActionSpawnFxRing>(a =>
              {
                a.PrefabLink = swordBlade;
                a.Count = 18;
                a.Radius = 2.4f;              // 米：散布半径（AoE 半径 10 尺 = 3.05 m，留点余量不外溢）
                a.Rings = 2;                  // ★ 同心双环（内 9 外 9），整齐列阵
                a.RadiusJitter = 0f;          // ★ 0 = 正好贴在环上（1 = 圆盘内乱撒，旧观感）
                a.FitLongestMeters = 1.5f;    // 米：剑的长度（竖直物件必须用"最长边"）
                a.Pitch = 180f;               // 剑尖朝下
                a.TiltJitter = 0f;            // ★ 0 = 笔直竖立（旧值 35 = 横七竖八）
                a.YawJitter = 0f;             // ★ 0 = 朝向随环一致（旧值 180 = 完全随机）
                a.ScaleJitter = 0f;           // ★ 0 = 大小一致（旧值 0.25）
                a.OffsetUp = 1.2f;            // 米：抬高落点，让剑插在地上
                a.OffsetUpJitter = 0f;        // ★ 0 = 插地深度一致（旧值 0.35）
                a.FallFromMeters = 9f;        // 米：从多高处落下（原来 16 m 配 1.1 s，见下面的说明）
                a.FallSeconds = 0.4f;         // 秒：**伤害是施法瞬间就结算的**，落剑必须紧跟其后
                a.FallSecondsJitter = 0f;     // ★ 同时落下：不再错开（旧值 0.4 → 先后的"雨点"感）
                a.Tint = SwordTint;           // 德克萨斯的橙金刃色
                a.Lift = 0.9f;
                a.Lifetime = 2f;              // 秒：落地后留存时长（6 → 4 → 2，用户两次要求"短两秒"）
                // ② 金光尾拖（2026-10-08 第二版）：**不再借原版粒子素材**，改成自绘的细光带。
                //    第一版借的是投射物"阳炎之刃"的飞行光弧，实测那素材是**数百米量级的巨型光柱**
                //    （`[特效尺寸]`：345.6x62.3x343.8 米，约 40 个粒子系统），缩到 1.4 m 后画面上
                //    仍是一整根橙色大柱子，18 把剑各一条 → 糊成用户截图里那根天量光柱。
                //    现在：一条 13 cm 宽、约 6 m 长的金色光带（软边、加法混合），跟着剑下落，落地后 0.15 s 内淡掉。
                //    光带挂在**世界缩放归一**的子物件上（见 SwordStreakFx）——直接挂剑身上的话，宽度会被
                //    武器模型根节点的 lossyScale 再乘一次，上一版窄到看不见就是这个原因。
                //    观感要再淡/再亮都只调这三个数（宽度、时长、以及上面 Tint 的金色）。
                a.GoldenStreak = true;
                a.StreakSeconds = 0.25f;      // 秒：光带长度 ≈ 落速 × 该值（0.4 s 落 9 m → 约 10 m 一缕）
                a.StreakWidth = 0.15f;        // 米：光带宽度（"一缕"就是这个数）
              }));
          }
          else
          {
            // 兜底：借不到飞剑时，用一个圆形地面符文（半径 5 尺的圆形区域 FX，scale=2 → 半径 10 尺）。
            // 形状是圆的、尺寸按区域半径换算，仍然"只显示真正的法术范围"。
            ability.AdditionalAbilityEffectRunActionOnClickedTarget(ActionsBuilder.New()
              .Add<ContextActionSpawnFxRing>(a =>
              {
                a.PrefabLink = FxLibrary.SwordRainRune;
                a.Count = 1;
                a.Scale = 2f;             // 5 尺半径 → 10 尺半径
                a.Tint = SwordTint;
                a.Lift = 0.9f;
                a.Lifetime = 5f;
              }));
          }

          // ④ 命中特效：被打中的敌人身上炸一小下金光（SwordHitFx）。挂在**和伤害同一个动作分支**里
          //    （失败 / 成功两支都挂），所以只要伤害结算了，特效必出。
          //    原版那套受击反馈会"有时什么都没有"，原因见 Actions.cs 的 ContextActionSwordHitFx。
          effects.SavingThrow(SavingThrowType.Reflex, onResult: ActionsBuilder.New()
            .ConditionalSaved(
              failed: ActionsBuilder.New()
                .DealDamage(DamageTypes.Physical(form: PhysicalDamageForm.Slashing),
                  ContextDice.Value(DiceType.D6, ContextValues.Rank(), 0), halfIfSaved: true)
                .ApplyBuff(BlueprintTool.GetRef<BlueprintBuffReference>("d2e35b870e4ac574d9873b36402487e5"), ContextDuration.Fixed(1))
                .Add<ContextActionSwordHitFx>(a => { a.Tint = SwordTint; }),
              succeed: ActionsBuilder.New()
                .DealDamage(DamageTypes.Physical(form: PhysicalDamageForm.Slashing),
                  ContextDice.Value(DiceType.D6, ContextValues.Rank(), 0), halfIfSaved: true)
                .Add<ContextActionSwordHitFx>(a => { a.Tint = SwordTint; })));
          break;
        }

        // 雷霆审判 C20：12d6 电击；失败者倒地并恍惚 1 轮，成功半伤不倒地。
        // 倒地用原版倒地增益（王者冲锋/牵引同款 24cf3deb…，永久直到起身）；恍惚用原版 Daze。
        case "ThunderousJudgment":
        {
          ability.AddContextRankConfig(DiceRank(s));
          effects.SavingThrow(SavingThrowType.Reflex, onResult: ActionsBuilder.New()
            .ConditionalSaved(
              failed: ActionsBuilder.New()
                .DealDamage(DamageTypes.Energy(DamageEnergyType.Electricity),
                  ContextDice.Value(DiceType.D6, ContextValues.Rank(), 0), halfIfSaved: true)
                .ApplyBuffPermanentFixed("24cf3deb078d3df4d92ba24b176bda97")
                .ApplyBuff(BlueprintTool.GetRef<BlueprintBuffReference>("d2e35b870e4ac574d9873b36402487e5"), ContextDuration.Fixed(1)),
              succeed: ActionsBuilder.New()
                .DealDamage(DamageTypes.Energy(DamageEnergyType.Electricity),
                  ContextDice.Value(DiceType.D6, ContextValues.Rank(), 0), halfIfSaved: true)));
          break;
        }

        // 梦魇 C12：失败者恐慌 2 轮后动摇至持续时间结束；成功者动摇 1 轮。
        // 恐慌用 Frightened 条件 2 轮 + 到期转摇（Eyebite 同款 Deactivated 接力）；动摇 1 轮/级。
        case "Nightterror":
        {
          // 恐惧 buff 照原版 `Buffs/Frightened` / `Buffs/Shaken`：
          //   ① 补 **SpellDescriptorComponent(Fear)**（原版两个 buff 都带，"恐惧免疫 / 移除恐惧"靠它判定）；
          //   ② **显示名/说明用"状态"自己的**（惊惧 / 动摇），不再复用"梦魇"法术名——
          //      否则玩家只看到两个都叫"梦魇"的 buff（一个几秒、一个一分钟），认不出是恐惧效果（作者 10-10 反馈）。
          var shakenLong = CreateExtraBuff("NightterrorShaken", Guids.NightterrorShakenBuff,
            L10n.S("buff.nightterror.shaken.name", "动摇", "Shaken"),
            L10n.S("buff.nightterror.shaken.desc",
              "恐惧：攻击检定、豁免、技能与属性检定 −2。",
              "Fear: −2 penalty on attack rolls, saving throws, skill and ability checks."),
            cfg =>
            {
              cfg.AddContextRankConfig(BuffRank(l => l)); // N11：动摇持续 1 轮/级，须配等级
              cfg.AddComponent<AddCondition>(c => c.Condition = UnitCondition.Shaken);
              cfg.AddSpellDescriptorComponent(SpellDescriptor.Fear);
            });
          var panic = CreateExtraBuff("NightterrorPanic", Guids.NightterrorPanicBuff,
            L10n.S("buff.nightterror.panic.name", "惊惧", "Frightened"),
            L10n.S("buff.nightterror.panic.desc",
              "恐惧：必须逃离恐惧源；攻击检定、豁免、技能与属性检定 −2。",
              "Fear: must flee from the source; −2 penalty on attack rolls, saving throws, skill and ability checks."),
            cfg =>
            {
              cfg.AddContextRankConfig(BuffRank(l => l)); // N11：到期转动的动摇按等级，须配等级
              cfg.AddComponent<AddCondition>(c => c.Condition = UnitCondition.Frightened);
              cfg.AddSpellDescriptorComponent(SpellDescriptor.Fear);
              cfg.AddFactContextActions(deactivated: ActionsBuilder.New()
                .ApplyBuff(shakenLong, ContextDuration.Variable(ContextValues.Rank(), DurationRate.Rounds)));
            });
          ability.AddContextRankConfig(BuffRank(l => l));
          // 持续时间显式写 Rounds（原版恐惧术的 JSON 也是 `Rate: Rounds`；不写就吃默认值，容易看错）。
          effects.SavingThrow(SavingThrowType.Will, onResult: ActionsBuilder.New()
            .ConditionalSaved(
              failed: ActionsBuilder.New()
                .ApplyBuff(panic, ContextDuration.Fixed(2, DurationRate.Rounds)),
              succeed: ActionsBuilder.New()
                .ApplyBuff(shakenLong, ContextDuration.Fixed(1, DurationRate.Rounds))));
          break;
        }

        // Mon3tr C16：每级 1d6（最多 20d6）+ 解除震慑/恍惚/麻痹/恶心。
        case "Mon3tr":
        {
          ability.AddContextRankConfig(DiceRank(s));
          effects.HealTarget(ContextDice.Value(DiceType.D6, ContextValues.Rank(), 0));
          effects.Add<ContextActionRemoveMon3trConditions>();
          break;
        }

        // 火山 C11：立即每 2 级 1d6（最多 10d6），之后 2 轮每轮每 4 级 1d6（最多 5d6），每次单独豁免。
        // 实现：首伤按 TargetsAround 逐目标结算（反射减半）；失败者挂余焰 2 轮，余焰 newRound 单独豁免。
        // 余焰按等级算：每 4 级 1d6，最多 5d6（N11 已配等级；T45 更新，旧“首伤一半”写法误导）。
        case "Volcano":
        {
          var ember = CreateExtraBuff("VolcanoEmber", Guids.VolcanoEmberBuff,
            L10n.S("spell.Volcano.ember.name", "火山（余焰）", "Volcano (ember)"), s.Desc,
            cfg =>
            {
              cfg.AddContextRankConfig(BuffRank(l => l)); // N11：余焰伤害按等级（每 4 级 1d6），须配等级
              cfg.AddFactContextActions(newRound: ActionsBuilder.New()
                .Add<ContextActionVolcanoEmber>());
            });
          // 余焰的**逐轮特效只放一个**（挂施法者、记住落点；作者 10-11：
          // "受到伤害的敌人有几个就多出几个火柱，其实只需要固定一个火柱"）。
          VolcanoTracker.Fx = FxLibrary.Fire;
          var volcanoFx = CreateExtraBuff("VolcanoTracker", Guids.VolcanoTrackerBuff,
            L10n.S("spell.Volcano.ember.name", "火山（余焰）", "Volcano (ember)"), s.Desc,
            cfg =>
            {
              cfg.AddComponent<VolcanoTracker>();
              cfg.AddFactContextActions(newRound: ActionsBuilder.New()
                .Add<ContextActionVolcanoEmberFx>());
            });
          ability.AddContextRankConfig(DiceRank(s));
          // 首轮落点爆发（作者 10-11："那个柱子的特效太小"）——放大到 2 倍；只放大**粒子本身**、不动根节点。
          ability.AdditionalAbilityEffectRunActionOnClickedTarget(ActionsBuilder.New()
            .Add<ContextActionSpawnScaledFx>(a =>
            {
              a.PrefabLink = FxLibrary.Fire;
              a.ParticleSizeScale = 2.0f;
              a.Lifetime = 2.5f;
            }));
          effects.SavingThrow(SavingThrowType.Reflex, onResult: ActionsBuilder.New()
            .ConditionalSaved(
              failed: ActionsBuilder.New()
                .DealDamage(DamageTypes.Energy(DamageEnergyType.Fire),
                  ContextDice.Value(DiceType.D6, ContextValues.Rank(), 0), halfIfSaved: true)
                .ApplyBuff(ember, ContextDuration.Fixed(2)),
              succeed: ActionsBuilder.New()
                .DealDamage(DamageTypes.Energy(DamageEnergyType.Fire),
                  ContextDice.Value(DiceType.D6, ContextValues.Rank(), 0), halfIfSaved: true)));
          // 余焰落点追踪（2 轮，与余焰同寿；挂**施法者**身上，只放一个火柱）
          effects.ApplyBuff(volcanoFx, ContextDuration.Fixed(2), toCaster: true);
          break;
        }

        // 黄昏 C19：立即每级 1d6（最多 20d6）；失败者附着熔火，之后 2 轮各 4d6，每次可再反射减半。
        case "Twilight":
        {
          var melt = CreateExtraBuff("TwilightMelt", Guids.TwilightMeltBuff,
            L10n.S("spell.Twilight.melt.name", "黄昏（熔火）", "Twilight (molten)"), s.Desc,
            cfg => cfg.AddFactContextActions(newRound: ActionsBuilder.New()
              .Add<ContextActionTwilightMelt>()));
          ability.AddContextRankConfig(DiceRank(s));
          effects.SavingThrow(SavingThrowType.Reflex, onResult: ActionsBuilder.New()
            .ConditionalSaved(
              failed: ActionsBuilder.New()
                .DealDamage(DamageTypes.Energy(DamageEnergyType.Fire),
                  ContextDice.Value(DiceType.D6, ContextValues.Rank(), 0), halfIfSaved: true)
                .ApplyBuff(melt, ContextDuration.Fixed(2)),
              succeed: ActionsBuilder.New()
                .DealDamage(DamageTypes.Energy(DamageEnergyType.Fire),
                  ContextDice.Value(DiceType.D6, ContextValues.Rank(), 0), halfIfSaved: true)));
          break;
        }

        // 闪光弹 C13：失败目盲 1 轮后目眩至持续结束；成功目眩 1 轮；隐形生物失败显形 1 轮。
        // 目盲 1 轮到期转目眩（梦魇同款 deactivated 接力）；目眩持续 1 轮/级走 Rank；显形先移除隐形增益。
        case "Flashbang":
        {
          var dazzleLong = CreateExtraBuff("FlashbangDazzle", Guids.FlashbangDazzleBuff,
            L10n.S("spell.Flashbang.dazzle.name", "闪光弹（目眩）", "Flashbang (dazzled)"), s.Desc,
            cfg =>
            {
              cfg.AddContextRankConfig(BuffRank(l => l)); // N11：目眩持续 1 轮/级，须配等级
              cfg.AddComponent<AddCondition>(c => c.Condition = UnitCondition.Dazzled);
            });
          var blind = CreateExtraBuff("FlashbangBlind", Guids.FlashbangBlindBuff,
            L10n.S("spell.Flashbang.blind.name", "闪光弹（目盲）", "Flashbang (blinded)"), s.Desc,
            cfg =>
            {
              cfg.AddContextRankConfig(BuffRank(l => l)); // N11：到期转动的目眩按等级，须配等级
              cfg.AddComponent<AddCondition>(c => c.Condition = UnitCondition.Blindness);
              cfg.AddFactContextActions(deactivated: ActionsBuilder.New()
                .ApplyBuff(dazzleLong, ContextDuration.Variable(ContextValues.Rank(), DurationRate.Rounds)));
            });
          ability.AddContextRankConfig(BuffRank(l => l));
          effects.SavingThrow(SavingThrowType.Reflex, onResult: ActionsBuilder.New()
            .ConditionalSaved(
              failed: ActionsBuilder.New()
                .ApplyBuff(blind, ContextDuration.Fixed(1))
                .Add<ContextActionRemoveInvisibility>(),
              succeed: ActionsBuilder.New()
                .ApplyBuff(dazzleLong, ContextDuration.Fixed(1))));
          break;
        }

        // 大地之怒 C21+N8b+T25：失败倒地；召唤物双倍伤害；困难地形按 G2 删除；建筑双倍按 V16 已删。
        // 作者 10-11：改为**以施法者为中心 30 尺**；特效换掉（原来的"尖石区域"看着像缠绕术、太丑）。
        // 因为不再挂 `AbilityTargetsAround`（见 areaSelfScan），伤害改成**自扫场景**（`ContextActionEarthFuryHit`）。
        case "EarthFury":
        {
          ability.AddContextRankConfig(DiceRank(s));
          effects.Add<ContextActionEarthFuryHit>(a => a.RadiusFeet = s.AoERadius);
          // 视觉：纯视觉区域（半径 = 法术半径）+ 地面**落石** FX
          // （借原版"落石术"区域 `StoneCallArea`，素材 Cylinder/40 → scale 0.75 → 30 尺）。
          AbilityAreaEffectConfigurator.New("RhodesDoctorEarthFuryArea", Guids.EarthFuryArea)
            .SetShape(AreaEffectShape.Cylinder)
            .SetSize(new Feet(s.AoERadius))
            .Configure();
          AreaGroundFx.Register(Guids.EarthFuryArea, FxLibrary.EarthWrathGround,
            new UnityEngine.Color(0.78f, 0.66f, 0.48f, 1f), textureLift: 0.35f, scale: 0.75f);
          effects.SpawnAreaEffect(Guids.EarthFuryArea, ContextDuration.Fixed(1));
          break;
        }

        // 灼烧 C10+E6+N13：直线每级 1d6（最多 10d6），反射减半；失败者火焰抗力 −10，1 轮/级无上限。
        // 降抗用 AddDamageResistanceEnergy 负值（Valmallos 同款 Value −10 Fire，已核蓝图）。
        case "ScorchingBreath":
        {
          var resist = CreateExtraBuff("ScorchResist", Guids.ScorchResistBuff,
            L10n.S("spell.ScorchingBreath.resist.name", "灼烧（抗性降低）", "Scorch (resistance lowered)"), s.Desc,
            cfg => cfg.AddComponent<AddDamageResistanceEnergy>(c =>
            {
              c.Type = DamageEnergyType.Fire;
              c.Value = ContextValues.Constant(-10);
            }));
          ability.AddContextRankConfig(DiceRank(s));
          // N13：降抗持续 1 轮/级无上限，与伤害骰（上限 10）分开算，走 StatBonus 档
          ability.AddContextRankConfig(BuffRank(l => l, AbilityRankType.StatBonus));
          // N14：直线只作用于敌人（耀阳颔首同款 IsEnemy 判断）
          effects.Conditional(ConditionsBuilder.New().IsEnemy(),
            ActionsBuilder.New().SavingThrow(SavingThrowType.Reflex, onResult: ActionsBuilder.New()
            .ConditionalSaved(
              failed: ActionsBuilder.New()
                .DealDamage(DamageTypes.Energy(DamageEnergyType.Fire),
                  ContextDice.Value(DiceType.D6, ContextValues.Rank(), 0), halfIfSaved: true)
                .ApplyBuff(resist, ContextDuration.Variable(ContextValues.Rank(AbilityRankType.StatBonus), DurationRate.Rounds)),
              succeed: ActionsBuilder.New()
                .DealDamage(DamageTypes.Energy(DamageEnergyType.Fire),
                  ContextDice.Value(DiceType.D6, ContextValues.Rank(), 0), halfIfSaved: true))));
          break;
        }

        // 黑蛇之焰 C17+G6：立即每级 1d6（最多 20d6），反射减半；之后每轮 3d6 灼烧（豁免成败都挂）。
        // G6 照原版焰击术思路：一半火焰 + 一半神圣（Divine，不受火焰抗力/免疫影响），各带 half；
        // 火焰免疫的目标自然只吃一半（神圣那半）。原版蓝图见 evidence/E7-FlameStrike.txt。
        // 灼烧用增益 newRound 实现。
        case "BlackSnakeFlame":
        {
          // 视觉（作者 2026-10-10：把颜色换成黑色）：`AbilitySpawnFx` 是引擎直接播的 PrefabLink、改不了色，
          // 所以走"重染克隆"动作——借原版**焰击术**的火焰爆发（FxLibrary.Fire），重染成**暗紫黑**。
          // 与缄默同一套（`AdditionalAbilityEffectRunActionOnClickedTarget` + `ContextActionSpawnTintedFx`）。
          var darkFlame = new ContextActionSpawnTintedFx
          {
            PrefabLink = FxLibrary.Fire,
            Tint = BlackSnakeBlack,
            Lift = 0.10f,
            Lifetime = 2.0f,
          };
          ability.AddComponent<Kingmaker.UnitLogic.Abilities.Components.AdditionalAbilityEffectRunActionOnClickedTarget>(
            c => c.Action = new Kingmaker.ElementsSystem.ActionList
            {
              Actions = new Kingmaker.ElementsSystem.GameAction[] { darkFlame },
            });
          var burn = CreateExtraBuff("BlackSnakeBurn", Guids.BlackSnakeBurnBuff,
            L10n.S("spell.BlackSnakeFlame.burn.name", "黑蛇之焰（灼烧）", "Black Snake Flame (burning)"), s.Desc,
            cfg =>
            {
              cfg.AddContextRankConfig(BuffRank(l => l));
              cfg.AddFactContextActions(newRound: ActionsBuilder.New()
                .Add<ContextActionBlackSnakeBurn>(a =>
                {
                  // 作者 10-11：**每造成一次伤害触发一次特效**——持续灼烧的每一轮也放一发黑焰
                  //（与施法瞬间那一发同一素材 / 同一颜色）。
                  a.Fx = FxLibrary.Fire;
                  a.Tint = BlackSnakeBlack;
                  a.Lift = 0.10f;
                }));
            });
          ability.AddContextRankConfig(DiceRank(s));
          ability.AddContextRankConfig(BuffRank(l => l));
          var snakeFire = ContextDice.Value(DiceType.D6, ContextValues.Rank(), 0);
          var snakeDivine = Kingmaker.UnitLogic.Abilities.AbilitySharedValue.Damage;
          effects.SavingThrow(SavingThrowType.Reflex, onResult: ActionsBuilder.New()
            .ConditionalSaved(
              failed: ActionsBuilder.New()
                .DealDamage(DamageTypes.Energy(DamageEnergyType.Fire), snakeFire,
                  half: true, halfIfSaved: true,
                  resultSharedValue: snakeDivine, writeRawResultToSharedValue: true)
                .DealDamagePreRolled(DamageTypes.Energy(DamageEnergyType.Divine), snakeDivine,
                  half: true, halfIfSaved: true)
                .ApplyBuff(burn, ContextDuration.Variable(ContextValues.Rank(), DurationRate.Rounds)),
              succeed: ActionsBuilder.New()
                .DealDamage(DamageTypes.Energy(DamageEnergyType.Fire), snakeFire,
                  half: true, halfIfSaved: true,
                  resultSharedValue: snakeDivine, writeRawResultToSharedValue: true)
                .DealDamagePreRolled(DamageTypes.Energy(DamageEnergyType.Divine), snakeDivine,
                  half: true, halfIfSaved: true)
                .ApplyBuff(burn, ContextDuration.Variable(ContextValues.Rank(), DurationRate.Rounds))));
          break;
        }

        // 穿云 C22：直线 20d6 穿刺，反射减半，忽略伤害减免（V16 穿墙已删）。
        case "Cloudpiercer":
        {
          ability.AddContextRankConfig(DiceRank(s));
          effects.Add<ContextActionCloudpiercerHit>();
          break;
        }

        // 紧急救治 C6+N12：治疗量与博士圣疗相同（1d6/2级，上限 10d6，LoH 同公式），并可附加慈悲。
        // 慈悲链抄原版 LayOnHandsOthers：施法者每拥有一个慈悲特性，就解除对应描述符的负面状态。
        // 慈悲 GUID 与描述符对见 evidence/E6-Mercy.txt（原版蓝图逐条抄入）；不能伤害不死生物（目标限制已由 Friendly 保证）。
        case "EmergencyTreatment":
        {
          ability.AddContextRankConfig(DiceRank(s));
          effects.HealTarget(ContextDice.Value(DiceType.D6, ContextValues.Rank(), 0));
          var mercies = new (string fact, SpellDescriptor desc)[]
          {
            ("e5aa306af9b91974a9b2f2cbe702f562", (SpellDescriptor)524288),
            ("7df9329013baaec4fac20ae08bc1f849", (SpellDescriptor)65536),
            ("7ee2ef06226a4884f80b7647a2aa2dee", (SpellDescriptor)131072),
            ("c4732ca5b0af7214a98f9b4dafe87da9", (SpellDescriptor)262144),
            ("19250e5a12db1fc448970b81e0f38922", (SpellDescriptor)1048576),
            ("a0cacf71d872d2a42ae3deb6bf977962", (SpellDescriptor)2097152),
            ("7687aa3764cb651448a50016e1758069", (SpellDescriptor)4194304),
            ("25641bda25467224e930e8c70eaf9a83", (SpellDescriptor)8388608),
            ("7775c915661f8fb449c38dd873524389", (SpellDescriptor)16777216),
            ("62582b4636218d74db34606f872c9c05", (SpellDescriptor)33554432),
            ("d306e052d2f85b24cb5082951459045e", (SpellDescriptor)67108864),
            ("23c3fb906c6346440b4e7d92fc25319b", (SpellDescriptor)134217728),
            ("3990a92ce97efa3439e55c160412ce14", (SpellDescriptor)16384),
            ("19f936eb38c3d224988e2a02cbe12878", (SpellDescriptor)256),
            ("445c696bead5140459909ffbed14750e", (SpellDescriptor)268435456),
            ("b636127171ae4c20b8e3f4d531c89905", (SpellDescriptor)655360),
            ("11c6495354ad4ac09a57882646917c64", (SpellDescriptor)262144),
          };
          foreach (var (fact, desc) in mercies)
          {
            string f = fact;
            var d = desc;
            effects.Conditional(ConditionsBuilder.New().CasterHasFact(f),
              ActionsBuilder.New().RemoveBuffsByDescriptor(d));
          }
          break;
        }

        // 冰封领域 C15+G2：30 尺区域 1 轮/级；敌人首次进入或回合开始受 4d6（反射部分通过），
        // 失败定身 1 轮，成功速度减半 1 轮；困难地形按 G2 删除。
        // 实现：区域挂进入+每轮 RunAction（ContextActionFrozenDomainHit），增益由动作内挂。
        case "FrozenDomain":
        {
          var entangled = CreateExtraBuff("FrozenEntangled", Guids.FrozenEntangledBuff,
            L10n.S("spell.FrozenDomain.entangled.name", "冰封领域（定身）", "Frozen Domain (entangled)"), s.Desc,
            cfg => cfg.AddComponent<AddCondition>(c => c.Condition = UnitCondition.Entangled));
          var half = CreateExtraBuff("FrozenHalfSpeed", Guids.FrozenHalfSpeedBuff,
            L10n.S("spell.FrozenDomain.halfspeed.name", "冰封领域（减速）", "Frozen Domain (slowed)"), s.Desc,
            cfg => cfg.AddComponent<Kingmaker.Designers.Mechanics.Buffs.BuffMovementSpeed>(c =>
            {
              c.Value = 0;
              c.CappedOnMultiplier = true;
              c.MultiplierCap = 0.5f;
            }));
          ability.AddContextRankConfig(BuffRank(l => l));
          AbilityAreaEffectConfigurator.New("RhodesDoctorFrozenArea", Guids.FrozenDomainArea)
            .SetShape(AreaEffectShape.Cylinder)
            .SetSize(new Feet(20))
            .AddAbilityAreaEffectRunAction(
              unitEnter: ActionsBuilder.New().Add<ContextActionFrozenDomainHit>(a =>
              {
                a.EntangledBuff = BlueprintTool.GetRef<BlueprintBuffReference>(Guids.FrozenEntangledBuff);
                a.HalfSpeedBuff = BlueprintTool.GetRef<BlueprintBuffReference>(Guids.FrozenHalfSpeedBuff);
              }),
              round: ActionsBuilder.New().Add<ContextActionFrozenDomainHit>(a =>
              {
                a.EntangledBuff = BlueprintTool.GetRef<BlueprintBuffReference>(Guids.FrozenEntangledBuff);
                a.HalfSpeedBuff = BlueprintTool.GetRef<BlueprintBuffReference>(Guids.FrozenHalfSpeedBuff);
              }))
            .Configure();
          // 视觉（C 类·持续地面区域）：借原版"寒冷灵光/霜障"区域的常驻 Fx（素材 Cylinder/30），
          // 染成**冰蓝白**。法术半径已从 30 改为 20，所以素材要缩到 20/30 ≈ 0.667 才贴合作用圈
          //（`AreaGroundFx` 的通用档会把粒子设成 Hierarchy 缩放模式，让 scale 生效）。
          // 不走钙质化的部件手术（calcProfile 默认 false）。
          AreaGroundFx.Register(Guids.FrozenDomainArea, FxLibrary.FrozenDomainGround,
            new UnityEngine.Color(0.82f, 0.92f, 1.00f, 1f), textureLift: 0.85f, scale: 0.667f);
          effects.SpawnAreaEffect(Guids.FrozenDomainArea,
            ContextDuration.Variable(ContextValues.Rank(), DurationRate.Rounds));
          _ = entangled;
          _ = half;
          break;
        }

        // 醉梦 C18后半+E7：意志无效；失败魔法沉眠 1 轮/级（不受 HD 限制），受伤醒来并恍惚 1 轮；
        // 免疫睡眠者仍恍惚 1 轮：恍惚用不带睡眠上下文的单独效果施加（ignoreParentContext，
        // 不继承法术的睡眠描述符上下文，故睡眠描述符免疫挡得住沉眠、挡不住恍惚；见 evidence/E7-Sleep.txt）。
        // 条件免疫（AddConditionImmunity 睡眠）本来就只挡沉眠；醒来时 Wake 组件再挂一次恍惚。
        // 睡眠增益带 DrunkenDreamWake（受伤监听，移除睡眠并挂恍惚）。
        case "DrunkenDream":
        {
          var daze = BlueprintTool.GetRef<BlueprintBuffReference>("d2e35b870e4ac574d9873b36402487e5");
          var sleep = CreateExtraBuff("DrunkenDreamSleep", Guids.DrunkenDreamSleepBuff,
            L10n.S("spell.DrunkenDream.sleep.name", "醉梦（沉眠）", "Drunken Dream (asleep)"), s.Desc,
            cfg =>
            {
              cfg.AddComponent<AddCondition>(c => c.Condition = UnitCondition.Sleeping);
              cfg.AddComponent<DrunkenDreamWake>();
            });
          ability.AddContextRankConfig(BuffRank(l => l));
          effects.SavingThrow(SavingThrowType.Will, onResult: ActionsBuilder.New()
            .ConditionalSaved(failed: ActionsBuilder.New()
              .ApplyBuff(sleep, ContextDuration.Variable(ContextValues.Rank(), DurationRate.Rounds))
              .ApplyBuff(daze, ContextDuration.Fixed(1), ignoreParentContext: true)));
          break;
        }

        // 天灾降临 C4/R3乙：整轮动作，持续 1 分钟；每轮开始时 80 尺区域内每个敌人
        // 受 12d6 火焰 + 12d6 轰击 + 12d6 电击（反射减半，每轮一次）。
        // 实现：40 尺固定区域 1 分钟，Round 动作扫描区域内单位（ContextActionCatastropheRound）。
        // 不挂 TargetsAround（区域自扫，避免同一动作按目标数重复执行）。
        // 已知偏差：规则"至多六个 10 尺爆发点由你选择"简化为整区域伤害（前轮已定）。
        // 作者 10-11：半径 80 → **40**。
        case "Catastrophe":
        {
          AbilityAreaEffectConfigurator.New("RhodesDoctorCatastropheArea", Guids.CatastropheArea)
            .SetShape(AreaEffectShape.Cylinder)
            .SetSize(new Feet(40))
            .AddAbilityAreaEffectRunAction(round: ActionsBuilder.New().Add<ContextActionCatastropheRound>())
            .Configure();
          // 视觉（C 类·持续地面区域）：借原版"火山风暴"区域的常驻 Fx（Cylinder/40，与半径相等、**免缩放**），
          // 染成**炽热橙红**（火 + 轰击的复合灾害）。不走钙质化的部件手术（calcProfile 默认 false）。
          AreaGroundFx.Register(Guids.CatastropheArea, FxLibrary.CatastropheGround,
            new UnityEngine.Color(1.00f, 0.62f, 0.34f, 1f), textureLift: 0.7f, scale: 1f);
          effects.SpawnAreaEffect(Guids.CatastropheArea,
            ContextDuration.Fixed(1, DurationRate.Minutes));
          break;
        }

        // 其余 Special：骨架已清零（64 指令全有分支），剩下的都是进游戏实测项。
        default:
          break;
      }
    }

    /// <summary>
    /// 群体法术的可视化（A 类点爆发 / B 类直线）：按元素给法术挂一个原版"命中爆发"FX。
    /// 锚在点击落点（ClickedTarget），时间 OnApplyEffect；借不到就不挂（功能不受影响）。
    /// 元素映射见 FxLibrary；这是"元素 → 法术"的一览表，后续批次继续往下加。
    /// </summary>
    private static void ApplyAbilityFx(AbilityConfigurator ability, Spec s)
    {
      // ── 需要"缩小"的法术 ──
      // 闪光弹（作者 10-11）：法术半径只有 **15 尺**，但借的"阳炎爆"爆发太大，要把特效**缩小**。
      // `AbilitySpawnFx` 是引擎直接播的 PrefabLink、**改不了尺寸**，所以走"点击目标时执行"的
      // `ContextActionSpawnScaledFx`。⚠ 前两次都没变小：① 只给粒子缩放没用（阳炎爆是网格型爆发）；
      // ② 光给根缩放也没用——**Unity 粒子默认 Local 缩放模式，根缩放对粒子无效**。
      // 现在 `FxTint.SpawnPlaced` 在根缩放 != 1 时会先把粒子改成 `Hierarchy` 缩放模式，
      // 于是根缩放**同时缩网格和粒子**。想更小/更大只改 `RootScale`。
      if (s.Key == "Flashbang")
      {
        ability.AdditionalAbilityEffectRunActionOnClickedTarget(ActionsBuilder.New()
          .Add<ContextActionSpawnScaledFx>(a =>
          {
            a.PrefabLink = FxLibrary.Holy;
            a.RootScale = 0.35f;
            a.Lifetime = 2.0f;
          }));
        return;
      }
      // ── 需要"改色"的法术 ──
      // `AbilitySpawnFx` 是引擎直接播的 PrefabLink、**改不了色**，所以要走"重染克隆"动作：
      // `AdditionalAbilityEffectRunOnClickedTarget` + `ContextActionSpawnTintedFx`
      // （每次施放在点击目标处生成一份重染副本，见 Actions.cs）。
      // Mon3tr（作者 10-16）：不再借"治疗轻伤"的牧师绿光——要凯尔希的**黑色神经结晶**。
      // 借原版先知启示"碎片爆发"的晶片爆发（FxLibrary.Mon3trCrystal），在目标处克隆重染成暗墨绿黑。
      if (s.Key == "Mon3tr")
      {
        ability.AdditionalAbilityEffectRunActionOnClickedTarget(ActionsBuilder.New()
          .Add<ContextActionSpawnTintedFx>(a =>
          {
            a.PrefabLink = FxLibrary.Mon3trCrystal;
            a.Tint = Mon3trCrystalBlack;
            a.Lift = 0.15f;
            a.Lifetime = 3f;
          }));
        // 作者 10-16：只有结晶爆开像伤害法术 → 在目标身上再叠一层**治疗中伤**的原色治疗光。
        var mon3trHeal = FxLibrary.Mon3trHeal;
        if (mon3trHeal != null)
          ability.AddAbilitySpawnFx(
            anchor: AbilitySpawnFxAnchor.ClickedTarget,
            prefabLink: mon3trHeal,
            time: AbilitySpawnFxTime.OnApplyEffect);
        return;
      }
      if (s.Key == "Silencer")
      {
        // 缄默：借原版"解除魔法"的防护系爆发，重染成**灰白**（作者 2026-10-10 要求）。
        // （梦魇原本也走这里重染黑，作者 10-10 改为"持续"——见 TintedAuraFx 的梦魇两条表项。）
        // `AbilitySpawnFx` 是引擎直接播的 PrefabLink、改不了色；所以挂"点击目标时执行"的组件
        // （`AdditionalAbilityEffectRunOnClickedTarget`），里面跑一个重染克隆动作。
        var burst = new ContextActionSpawnTintedFx
        {
          PrefabLink = FxLibrary.Dispel,
          Tint = SilencerGrayWhite,
          Lift = 0.55f,
          Lifetime = 2.5f,
        };
        ability.AddComponent<Kingmaker.UnitLogic.Abilities.Components.AdditionalAbilityEffectRunActionOnClickedTarget>(
          c => c.Action = new Kingmaker.ElementsSystem.ActionList
          {
            Actions = new Kingmaker.ElementsSystem.GameAction[] { burst },
          });
        return;
      }

      var fx = s.Key switch
      {
        // 火焰：黄昏、炮火延伸（火山改走"放大的落点爆发"，见 ApplySpecial 的 Volcano 分支）
        "Twilight" or "ArtilleryBarrage" => FxLibrary.Fire,
        // 电击：雷霆审判
        "ThunderousJudgment" => FxLibrary.Electricity,
        // 神圣 / 光：耀阳颔首（闪光弹走上方"缩小"分支，不再走这里）
        "BlazingSunBow" => FxLibrary.Holy,
        // 物理：穿云（剑雨单独走"重染成白色"，见 ApplySpecial）
        // 王者冲锋（作者 10-11）：改走自定义冲锋动作（见 ApplySpecial 的 RoyalCharge 分支），这里不挂爆发。
        "Cloudpiercer" => FxLibrary.Physical,
        // 大地之怒改走"纯视觉区域 + 地面落石 FX"（见 ApplySpecial 的 EarthFury 分支），这里不挂爆发。
        // 重构 / 文明的存续：视觉**不在这里**。理由：①`AbilitySpawnFx` 对"个人距离 + 自扫"的法术不生效；
        // ②10-16 起两发法术改专属自绘特效（`ApplySpecial` 里设 `Style`，实际演出在 `MassRestoreFx`），
        // 这里不挂任何爆发。
        // 精神：醉梦
        "DrunkenDream" => FxLibrary.Psychic,
        // 力场：牵引（把敌人拽向落点）——借原版"力场拳"的冲击 FX，见 FxLibrary.Force
        "Dragnet" => FxLibrary.Force,
        // 王冠的回响（10 环·特雷西娅）：不再借"幻影杀手"黑紫鬼影，
        // 改为专属自绘（CrownEchoFx.cs）：头顶黑冠 + 60 尺精神织网 + 敌方丝线索取，挂在 effects 上，不走这里。
        // ── 单体批次（0 环起）──
        // 治疗：医疗敷料（接触包扎）——作者 10-16：不再用治疗轻伤绿光，改借次级复原术的复原光（FxLibrary.FieldDressingFx）
        "FieldDressing" => FxLibrary.FieldDressingFx,
        // 治疗：紧急救治（闪灵；移动动作）——改借圣疗的光（生命气息与复活术同款，已弃；FxLibrary.EmergencyTreatmentFx）
        "EmergencyTreatment" => FxLibrary.EmergencyTreatmentFx,
        // 紧急撤离：不再借"仪式·任意门"闪光，改为专属的 PRTS 战术撤离（EmergencyExtractionFx.cs，
        // 由传送动作 ContextActionTeleportAllyToCaster 在落地后触发），不走这里。
        // 战术标记：不再借"天界印记"（教会十字金光，不像 PRTS）。
        // 改为专属自绘的战术投影，跟增益同生同灭，见 TacticalProjectionFx.cs。
        // 战场测绘：已改专属 PRTS 战场扫描（BattlefieldSurveyFx.cs），
        // 挂在**能力自己的效果动作**上（见 `effects.Add<ContextActionBattlefieldSurveyFx>()`），不走这里。
        // 牵制射击（0 环·克洛丝）：不再借原版精神系紫圈爆发（与醉梦重样），
        // 改为专属自绘（SuppressingShotFx.cs）：射出高亮弩矢 + 命中针刺 + 持续压制准星，不走这里。
        // ── 单体批次（2 环）──
        // 增益：不稳定血浆（死灵；攻击 / 伤害 +2，代价每轮失血）——借原版"英雄气概"的增益施法闪光
        "UnstablePlasma" => FxLibrary.Heroism,
        // 缓慢：反重力 —— 已改为**常驻缓慢光环**（TintedAuraFx，跟减益同生同灭），不再走这里的施法爆发。
        // （作者 10-10：原来只有一发、不持续。）
        // 禁魔：缄默 —— 见上方"改色"分支（重染灰白），不走这里。
        // ── 单体批次（3–5 环）──
        // 处决（3 环；Personal）：借原版"刺客·公开处决"的施法爆发（锚施法者），见 FxLibrary.Execution
        "Execution" => FxLibrary.Execution,
        // 梦魇（4 环；恐惧）—— 见上方"改色"分支（重染成恐惧的黑），不走这里。
        // 迅捷突破（4 环；加速）—— 已改为**常驻橙色加速拖尾**（TintedAuraFx，跟加速增益同生同灭），不走这里。
        // Mon3tr（5 环）—— 作者 10-16：改走上方"重染"分支（黑色神经结晶），不走这里。
        // 奇美拉（5 环；战力增益）—— 已改为**常驻暗红光环**（TintedAuraFx），不走这里。
        // 铸铁（5 环；防护增益）—— 已改为**常驻淡蓝光环**（TintedAuraFx），不走这里。
        // 墨染山河（5 环；召唤）—— 见 InkedMountains 分支的**自定义动作**（在两只墨灵中间生成、放大），不走这里。
        // ── 单体批次（6–9 环）──
        // 黑蛇之焰（6 环；单体火焰）—— 要**黑焰**，改走"重染克隆"（见 ApplySpecial 的 BlackSnakeFlame 分支）。
        // 镜中流形（7 环；幻术·复制友军）—— 作者 10-10：**不需要特效**（能招出复制体就行），已撤。
        // 影霄·拔刀（9 环；死灵·即死）—— 作者 10-10：要"**红剑砍人**"，改走武器模型（见 ApplySpecial 的 ShadowBladeDraw 分支）。
        // 涌潮悲歌（6 环）/ 归溟（7 环）/ 旧日的战术家（9 环）—— 自增益，见 TintedAuraFx 的常驻光环。
        // 罪罚（6 环）/ 终末狙击（7 环）/ 画中世界（8 环）—— 目标身上的持续减益，见 FxLibrary.DebuffAuraFor / TintedAuraFx。
        _ => null,
      };
      if (fx == null) return;
      // 以施法者为中心的法术（Range=Personal，如王冠的回响）锚在**施法者**身上，其余锚落点（与祝福术一致）。
      ability.AddAbilitySpawnFx(
        anchor: s.Range == AbilityRange.Personal ? AbilitySpawnFxAnchor.Caster : AbilitySpawnFxAnchor.ClickedTarget,
        prefabLink: fx,
        time: AbilitySpawnFxTime.OnApplyEffect);
    }

    /// <summary>灵光组件的 m_Buff 私有字段，反射写入（指挥灵光同款）。P2-1：失败记 Error。</summary>
    private static void SetAuraBuff(AuraFeatureComponent component, BlueprintBuffReference buff)
    {
      try
      {
        var field = typeof(AuraFeatureComponent)
          .GetField("m_Buff", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field == null)
        {
          Main.Mod.Logger.Error("[RhodesDoctor] 灵光 m_Buff 反射写入失败：字段未找到（游戏更新可能改了私有成员名）。");
          return;
        }
        field.SetValue(component, buff);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 灵光 m_Buff 反射写入失败：\n" + e);
      }
    }

    /// <summary>
    /// C34：通用"加速效果"（迅捷突破 / 永恒注视共用）。只给两样：速度提升、全力攻击多一次攻击；
    /// 不含加速术的 +1 攻击 / +1 防御 / +1 反射。速度用不具类型（加速术是 Enhancement，天然叠加）；
    /// 额外攻击走非 haste 通道（AddExtraAttacks(haste=false) → AdditionalAttacks），与加速术的 HasteAttacks 相加，可叠加。
    /// </summary>
    /// <summary>德克萨斯刃色（橙金）：剑雨特效的重染目标色。想更亮可把三个分量都往 1 调。</summary>
    private static readonly UnityEngine.Color SwordTint = new UnityEngine.Color(1f, 0.78f, 0.27f, 1f);

    /// <summary>缄默的重染色：**灰白**（作者 2026-10-10）。想更白就把三个分量都往 1 抬、并把 Lift 调大。</summary>
    private static readonly UnityEngine.Color SilencerGrayWhite = new UnityEngine.Color(0.90f, 0.90f, 0.93f, 1f);

    /// <summary>
    /// Mon3tr 的结晶重染色：**暗墨绿黑**（作者 10-16：要凯尔希的黑色神经结晶，不要牧师绿光）。
    /// 纯黑会把粒子整片压没（dev-notes §7.11.5），留一点青绿让结晶轮廓/裂光仍可见。想更黑就往下调、并把 Lift 调小。
    /// </summary>
    private static readonly UnityEngine.Color Mon3trCrystalBlack = new UnityEngine.Color(0.16f, 0.26f, 0.22f, 1f);

    /// <summary>
    /// 黑蛇之焰的重染色：**黑焰**（作者 2026-10-10：把颜色换成黑色）。
    /// 素材是焰击术的橙红火焰，重染成**暗紫黑**——纯黑（0,0,0）会把火焰整体压没（见 dev-notes §7.11.5
    /// "压狠了整片消失"），所以留一点点紫红让它仍看得见"火"的形状。想更黑就往下调、并把 Lift 调小。
    /// </summary>
    private static readonly UnityEngine.Color BlackSnakeBlack = new UnityEngine.Color(0.30f, 0.10f, 0.38f, 1f);

    /// <summary>
    /// 影霄·拔刀的红剑重染色：**血红**（作者 2026-10-10：要"红剑砍人"）。
    /// 与剑雨共用同一把巨剑模型，只换颜色。
    /// </summary>
    private static readonly UnityEngine.Color ShadowBladeRed = new UnityEngine.Color(1.00f, 0.16f, 0.16f, 1f);

    private static BlueprintBuffReference _hasteAccel;
    private static BlueprintBuffReference HasteAccel()
    {
      if (_hasteAccel != null) return _hasteAccel;
      _hasteAccel = CreateExtraBuff("HasteAccel", Guids.HasteAccelBuff,
        L10n.S("buff.hasteaccel.name", "加速", "Hastened"),
        L10n.S("buff.hasteaccel.desc",
          "速度提升，全力攻击时多一次攻击；不含加速术的其他加值，可与加速术叠加。",
          "Increased speed and one extra attack on a full attack; no other haste bonuses, and it stacks with haste."),
        cfg =>
        {
          cfg.AddStatBonus(descriptor: ModifierDescriptor.UntypedStackable, stat: StatType.Speed, value: 30);
          cfg.AddComponent<ExtraAttackStackable>();
        });
      return _hasteAccel;
    }

    /// <summary>Special 类法术附带的独立增益（不占用 Buff(i) 序号，GUID 用 0xe8 起的专用段）。</summary>
    private static BlueprintBuffReference CreateExtraBuff(
        string name, string guid, string displayName, string description, Action<BuffConfigurator> setup)
    {
      var cfg = BuffConfigurator.New($"RhodesDoctorBuff{name}", guid)
        .SetDisplayName(displayName)
        .SetDescription(description);
      // If an extra status is named after a known spell, reuse that spell icon.
      // Shared or unrelated statuses are left untouched rather than guessed.
      var ownerSpell = Specs.FirstOrDefault(s => name.StartsWith(s.Key, StringComparison.Ordinal));
      if (ownerSpell != null) cfg.SetIcon(Icons.Spell(ownerSpell.Key));
      setup(cfg);
      // D 类：友军增益的常驻金光（按法术 key 命中才挂；见 FxLibrary.AuraFor）。
      ApplyAuraFx(cfg, ownerSpell?.Key);
      return cfg.Configure().ToReference<BlueprintBuffReference>();
    }

    /// <summary>
    /// 给增益蓝图挂常驻循环光环：先试 **D 类**（友军增益 → 金色光环，<see cref="FxLibrary.AuraFor"/>），
    /// 再试 **E 类**（敌方减益 → 烙印/诅咒类光环，<see cref="FxLibrary.DebuffAuraFor"/>）。
    /// 借不到素材 / 不属于这两批法术时什么都不做（功能不受影响，只打一行日志）。
    /// 接线只有两处：<see cref="CreateSpellBuff"/>（Kind.Buff/Debuff 走这里）与
    /// <see cref="CreateExtraBuff"/>（各法术在 ApplySpecial 里自建的增益），所以新加一个
    /// "要光环的增益/减益"只需往对应名单里加 key。
    /// </summary>
    private static void ApplyAuraFx(BuffConfigurator cfg, string spellKey)
    {
      var aura = FxLibrary.AuraFor(spellKey) ?? FxLibrary.DebuffAuraFor(spellKey);
      if (aura != null) cfg.SetFxOnStart(aura);
    }

    private static BlueprintBuffReference CreateSpellBuff(Spec s, int i)
    {
      var cfg = BuffConfigurator.New($"RhodesDoctorBuff{s.Key}", Guids.Buff(i))
        .SetDisplayName(s.Name)
        .SetDescription(s.Desc)
        .SetIcon(Icons.Spell(s.Key));

      foreach (var b in s.Bonuses)
        cfg.AddStatBonus(descriptor: b.Descriptor, stat: b.Stat, value: b.Value);

      if (s.Condition.HasValue)
        cfg.AddComponent<AddCondition>(c => c.Condition = s.Condition.Value);

      // 随等级成长的加值与专属机制组件
      switch (s.Key)
      {
        case "LockOfTime":
          // C9：每轮开始可再次豁免，成功即解除（BuffStatusCondition.SaveEachRound）。
          // ⚠ 2026-10-10 修：原实现**只**用 BuffStatusCondition 打"缓慢"条件标记——但反编译确认，
          //    原版缓慢的**数值（AC / 反射 / 攻击 −1）与"蹒跚(Staggered)"、特效全在 `Spells/Level3/SlowBuff` 上**
          //    （SlowBuff = 3 个 AddStatBonus + AddCondition(Staggered/Slowed) + ChangeImpatience + FxOnStart），
          //    所以原来数值没生效、也没有特效。这里照抄 SlowBuff 的**可感知组件**，让"缓慢"真的成立。
          //    （光环/特效见 FxLibrary.DebuffAuraFor → SlowAura：借 SlowBuff 的 FxOnStart。）
          cfg.AddStatBonus(descriptor: ModifierDescriptor.Dodge, stat: StatType.AC, value: -1);
          cfg.AddStatBonus(descriptor: ModifierDescriptor.Dodge, stat: StatType.SaveReflex, value: -1);
          cfg.AddStatBonus(descriptor: ModifierDescriptor.UntypedStackable, stat: StatType.AdditionalAttackBonus, value: -1);
          cfg.AddComponent<AddCondition>(c => c.Condition = UnitCondition.Staggered);
          cfg.AddComponent<Kingmaker.Designers.Mechanics.Buffs.BuffStatusCondition>(c =>
          {
            c.SaveEachRound = true;
            c.Condition = UnitCondition.Slowed;
            c.SaveType = SavingThrowType.Will;
          });
          break;
        case "Ura":
          // 加值用 DamageBonus 档，把 Default 档留给持续时间（分钟/级）。
          cfg.AddContextStatBonus(StatType.AdditionalAttackBonus, ContextValues.Rank(AbilityRankType.DamageBonus), ModifierDescriptor.UntypedStackable);
          cfg.AddContextStatBonus(StatType.AdditionalDamage, ContextValues.Rank(AbilityRankType.DamageBonus), ModifierDescriptor.UntypedStackable);
          cfg.AddContextRankConfig(BuffRank(l => l >= 10 ? 2 : 1, AbilityRankType.DamageBonus));
          cfg.AddFactContextActions(activated: ActionsBuilder.New()
            .Add<ContextActionRemoveBuffsByDescriptor>(a =>
              a.SpellDescriptor = SpellDescriptor.Shaken | SpellDescriptor.Frightened));
          break;
        case "UnstablePlasma":
          cfg.AddContextStatBonus(StatType.AdditionalDamage, ContextValues.Rank(), ModifierDescriptor.UntypedStackable);
          cfg.AddContextRankConfig(BuffRank(l => 2 + l / 4));
          // 代价：每轮开始失去 2 点生命，不能降到 1 以下 → 自定义动作钳制（保 1 线）
          cfg.AddFactContextActions(newRound: ActionsBuilder.New()
            .Add<ContextActionLoseHitPointsClamped>(a => a.Amount = 2));
          break;
        case "CavalryCharge":
          cfg.AddContextStatBonus(StatType.Speed, ContextValues.Rank(), ModifierDescriptor.UntypedStackable);
          cfg.AddContextRankConfig(BuffRank(l => l >= 10 ? 20 : 10));
          cfg.AddComponent<DodgeVsAoO>();
          break;
        case "Chimera":
          cfg.AddContextStatBonus(StatType.AdditionalDamage, ContextValues.Rank(), ModifierDescriptor.UntypedStackable);
          cfg.AddContextRankConfig(BuffRank(l => Math.Max(1, l / 2)));
          cfg.AddComponent<IgnoreDamageReduction>();
          break;
        case "TidalElegy":
          // 武器伤害 + 施法者等级（随等级成长）；V13：临时生命固定 60（不是每级 60×等级）。
          cfg.AddContextStatBonus(StatType.AdditionalDamage, ContextValues.Rank(), ModifierDescriptor.UntypedStackable);
          cfg.AddContextRankConfig(BuffRank(l => l));
          cfg.AddTemporaryHitPointsFromAbilityValue(value: ContextValues.Constant(60));
          break;
        case "Overloading":
          cfg.AddComponent<ExtraAttackStackable>();
          break;
        case "Sanctuary":
          cfg.AddComponent<SaveBonusVsSpells>();
          cfg.AddComponent<StackingEnergyResistance>();
          break;
        case "TidalVigil":
          // 免疫魅惑与强迫：描述符免疫（Charm | Compulsion 均在程序集确认）。
          cfg.AddBuffDescriptorImmunity(
            descriptor: SpellDescriptor.Charm | SpellDescriptor.Compulsion);
          // 每轮开始补满 15 临时生命（刷新不叠加）：初始挂一次，之后每轮补差额。
          cfg.AddFactContextActions(activated: ActionsBuilder.New()
            .Add<ContextActionRefreshTemporaryHitPoints>(a => a.Amount = 15));
          cfg.AddFactContextActions(newRound: ActionsBuilder.New()
            .Add<ContextActionRefreshTemporaryHitPoints>(a => a.Amount = 15));
          break;
        // 画中世界已改为 Kind.Special（放逐 + 返回后麻痹），不再走这里的 Debuff 增益。
        case "Ironcast":
          // C29：用引擎自带的物理伤害减免（同类型不叠加），而不是自写"可叠加"的减免。
          cfg.AddDamageResistancePhysical(value: ContextValues.Constant(10), isStackable: false);
          cfg.AddComponent<AddImmunityToCriticalHits>();
          cfg.AddComponent<AddImmunityToPrecisionDamage>();
          break;
        case "TacticalMark":
          cfg.AddComponent<TacticalMarkDebuff>();
          break;
        case "SuppressingShot":
          // R5：按设计第 81 行，区分攻击对象、只作用于下一次攻击（组件内结算后移除）。
          cfg.AddComponent<SuppressingShotDebuff>();
          break;
        case "FireRecon":
          cfg.AddComponent<FireReconMark>();
          break;
        case "MedicalDrone":
          // 增益版无人机：每轮开始为持有者恢复 1d6 + 施法者等级（至多 +5）；
          // 同时在持有者身上闪一下**治疗光**（作者 10-10 定案）——借原版"治疗轻伤"的施法光（FxLibrary.Heal）。
          cfg.AddContextRankConfig(BuffRank(l => Math.Min(l, 5)));
          cfg.AddFactContextActions(newRound: ActionsBuilder.New()
            .Add<Kingmaker.UnitLogic.Mechanics.Actions.ContextActionSpawnFx>(a => a.PrefabLink = FxLibrary.Heal)
            .HealTarget(ContextDice.Value(DiceType.D6, 1, ContextValues.Rank())));
          break;
      }

      // D 类：友军增益的常驻金光（按法术 key 命中才挂；见 FxLibrary.AuraFor）。
      ApplyAuraFx(cfg, s.Key);
      return cfg.Configure().ToReference<BlueprintBuffReference>();
    }

    /// <summary>伤害 / 治疗骰数：每 Step 级 DicePerStep 骰，上限 DiceCap 骰。</summary>
    private static ContextRankConfig DiceRank(Spec s)
      => ContextRankConfigs
          .ClassLevel(new[] { Guids.Vanilla.PaladinClass }, false, AbilityRankType.Default)
          .WithCustomProgression(Enumerable.Range(1, 20)
            .Select(l => (l, Math.Min(s.DiceCap, Math.Max(1, l / s.Step) * s.DicePerStep)))
            .ToArray());

    /// <summary>增益的随等级成长表（按圣武士等级）。</summary>
    private static ContextRankConfig BuffRank(Func<int, int> valueOf,
        AbilityRankType type = AbilityRankType.Default)
      => ContextRankConfigs
          .ClassLevel(new[] { Guids.Vanilla.PaladinClass }, false, type)
          .WithCustomProgression(Enumerable.Range(1, 20)
            .Select(l => (l, valueOf(l)))
            .ToArray());

    /// <summary>写游戏私有字段（原版有、公开 API 没暴露的才用）。P2-1：字段缺失记 Error，不再静默。</summary>
    private static void SetPrivateField(object obj, string field, object value)
    {
      try
      {
        var f = obj.GetType().GetField(field,
          System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (f == null)
        {
          Main.Mod.Logger.Error(
            $"[RhodesDoctor] 反射写私有字段失败：{obj.GetType().Name}.{field} 未找到（游戏更新可能改了成员名）。");
          return;
        }
        f.SetValue(obj, value);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error($"[RhodesDoctor] 反射写私有字段失败（{field}）：\n{e}");
      }
    }

    /// <summary>ContextActionDispelMagic 的私有嵌套枚举 BuffType，按名取值。</summary>
    private static object PrivateDispelBuffType(string name)
      => System.Enum.Parse(
        typeof(Kingmaker.UnitLogic.Mechanics.Actions.ContextActionDispelMagic)
          .GetNestedType("BuffType", System.Reflection.BindingFlags.NonPublic),
        name);
  }
}
