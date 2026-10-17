// 自定义机制组件：挂在特性/增益上，通过规则钩子实现游戏原生组件做不到的效果。
//
// 重要约定：所有规则钩子组件只保留 ITarget/IInitiatorRulebookHandler<T> 一个接口，
// 不再同时挂 IRulebookHandler<T> / ISubscriber / I*RulebookSubscriber——
// 接口重复会让同一事件触发两次。
using System;
using System.Linq;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells; // BlueprintSpellbook（已知指令补发）
using Kingmaker.Blueprints.Facts;
using Kingmaker.EntitySystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.Enums.Damage;
using Kingmaker.PubSubSystem;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities.Blueprints; // BlueprintAbility（已知指令补发）
using Kingmaker.UnitLogic.Abilities.Components.Base;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Buffs.Components;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.Utility;
using Kingmaker.View;
using Newtonsoft.Json;
using UnityEngine;

namespace RhodesDoctor.Components
{
  /// <summary>
  /// 冷静头脑：豁免检定 + 智力调整值（不具类型，可叠加）。
  /// 恶灵锁定生效期间减半；20 级时锁定被压制（HasActive 视为不存在），自动恢复全额。
  /// </summary>
  public class CoolHeadSaveBonus : UnitFactComponentDelegate,
      ITargetRulebookHandler<RuleSavingThrow>
  {
    public BlueprintBuffReference[] SpecterLocks = new BlueprintBuffReference[0];

    public void OnEventAboutToTrigger(RuleSavingThrow evt)
    {
      int intMod = Owner.Stats.Intelligence.Bonus;
      if (intMod <= 0) return;
      bool locked = SpecterLocks.Any(l => l != null && BuffHelper.SpecterLockActive(Owner, l));
      int value = locked ? intMod / 2 : intMod;
      if (value > 0)
        evt.AddModifier(value, Fact, ModifierDescriptor.UntypedStackable);
    }

    public void OnEventDidTrigger(RuleSavingThrow evt) { }
  }

  /// <summary>
  /// 敏捷形态：近战攻击与伤害用敏捷代替力量（取较高者）。
  /// 伤害侧：RuleCalculateWeaponDamage 不存在，用 RuleCalculateWeaponStats.OverrideDamageBonusStat；
  /// 双手 1.5 倍 / 副手 0.5 倍由规则按替换后的属性自行处理。
  /// </summary>
  public class DexToMeleeAttackDamage : UnitFactComponentDelegate,
      IInitiatorRulebookHandler<RuleCalculateAttackBonusWithoutTarget>,
      IInitiatorRulebookHandler<RuleCalculateWeaponStats>
  {
    public void OnEventAboutToTrigger(RuleCalculateAttackBonusWithoutTarget evt)
    {
      if (evt.Weapon == null || !evt.Weapon.Blueprint.IsMelee) return;
      if (Owner.Stats.Dexterity.Bonus > Owner.Stats.Strength.Bonus)
        evt.AttackBonusStat = StatType.Dexterity;
    }
    public void OnEventDidTrigger(RuleCalculateAttackBonusWithoutTarget evt) { }

    public void OnEventAboutToTrigger(RuleCalculateWeaponStats evt)
    {
      if (evt.Weapon == null || !evt.Weapon.Blueprint.IsMelee) return;
      if (Owner.Stats.Dexterity.Bonus > Owner.Stats.Strength.Bonus)
        evt.OverrideDamageBonusStat(StatType.Dexterity);
    }
    public void OnEventDidTrigger(RuleCalculateWeaponStats evt) { }
  }

  /// <summary>
  /// 过载模式：全回合攻击额外 +1 次。走 AddExtraAttacks（不带"加速"标记），
  /// 与加速术、速度武器天然叠加。
  /// </summary>
  public class ExtraAttackStackable : UnitFactComponentDelegate,
      IInitiatorRulebookHandler<RuleCalculateAttacksCount>
  {
    public void OnEventAboutToTrigger(RuleCalculateAttacksCount evt)
      => evt.AddExtraAttacks(1, false, false, null);

    public void OnEventDidTrigger(RuleCalculateAttacksCount evt) { }
  }

  /// <summary>
  /// 体质形态（12 级）：伤害减免 3/—，可与其他来源叠加。
  /// 做法：给每段物理伤害追加固定负值修饰（等效减免）；无视伤害减免的攻击（奇美拉）跳过。
  /// </summary>
  public class StackingDamageReduction : UnitFactComponentDelegate,
      ITargetRulebookHandler<RuleDealDamage>
  {
    public int Value = 3;

    public void OnEventAboutToTrigger(RuleDealDamage evt)
    {
      if (evt.IgnoreDamageReduction) return;
      foreach (var dmg in evt.DamageBundle)
      {
        if (dmg is PhysicalDamage)
          dmg.AddModifier(-Value, Fact);
      }
    }
    public void OnEventDidTrigger(RuleDealDamage evt) { }
  }

  /// <summary>圣域：五种元素（火/寒/电/酸/音波）抗力各 10 点，可与其他来源叠加（同上加固定负值修饰）。
  /// V12：只作用于五种元素能量；神圣/邪恶/负能量等不吃此抗力。</summary>
  public class StackingEnergyResistance : UnitFactComponentDelegate,
      ITargetRulebookHandler<RuleDealDamage>
  {
    public int Value = 10;

    private static bool IsFiveElements(DamageEnergyType t)
      => t == DamageEnergyType.Fire || t == DamageEnergyType.Cold
        || t == DamageEnergyType.Electricity || t == DamageEnergyType.Acid
        || t == DamageEnergyType.Sonic;

    public void OnEventAboutToTrigger(RuleDealDamage evt)
    {
      foreach (var dmg in evt.DamageBundle)
      {
        if (dmg is EnergyDamage ed && IsFiveElements(ed.EnergyType))
          dmg.AddModifier(-Value, Fact);
      }
    }
    public void OnEventDidTrigger(RuleDealDamage evt) { }
  }

  /// <summary>
  /// 能力限制：施法者身上**至少有一个**指定增益在生效时才能使用（与下面的 HasNoActiveFacts 相反）。
  /// 用例：**旧日的战术家** —— 作者 10-11：没开恶灵形态时别让它空放（白扣法术位），直接不给放。
  /// 这是 `IAbilityCasterRestriction`：按钮会置灰；而且 `UnitUseAbility.OnAction` 在**施法前**会复查
  /// `Ability.IsAvailable`（不通过就 `return ResultType.Fail`，**不扣法术位**），所以 UI 与硬拦都靠它。
  /// </summary>
  public class AbilityCasterHasAnyActiveFact : BlueprintComponent, IAbilityCasterRestriction
  {
    public BlueprintUnitFactReference[] Facts = new BlueprintUnitFactReference[0];
    public string NotText = "需要先开启恶灵形态";

    public bool IsCasterRestrictionPassed(UnitEntityData caster)
      => caster != null && Facts.Any(f => f != null && BuffHelper.HasActive(caster, f));

    public string GetAbilityCasterRestrictionUIText() => NotText;
  }

  /// <summary>
  /// 能力限制：施法者身上没有"生效中"的指定增益时才能使用。
  /// 被压制（Suppressed）的增益不算生效，因此 20 级压制锁定/余波限制即解禁。
  /// </summary>
  public class AbilityCasterHasNoActiveFacts : BlueprintComponent, IAbilityCasterRestriction
  {
    public BlueprintUnitFactReference[] Facts = new BlueprintUnitFactReference[0];
    public string NotText = "巴别塔的恶灵生效期间无法使用";

    public bool IsCasterRestrictionPassed(UnitEntityData caster)
      => Facts.All(f => f == null || !(BuffHelper.IsCapstoneSuppressedFact(f.Guid)
        ? BuffHelper.SpecterLockActive(caster, f)
        : BuffHelper.HasActive(caster, f)));

    public string GetAbilityCasterRestrictionUIText() => NotText;
  }

  /// <summary>
  /// 恶灵阶段收敛：把"不属于当前阶段"的形态开关承载（Toggles holder）从单位上清掉。
  ///
  /// 背景：8 / 12 / 16 级各换发一套形态开关，换发用 `RemoveFeatureOnApply`；但**旧承载是 `AddFacts` 授予的**，
  /// 而 `AddFacts` 默认 `DoNotRestoreMissingFacts = false` —— 每次**读档**时 post-load 修复都会走恢复分支
  /// （`AddFacts.OnApplyPostLoadFixes → UpdateFacts(postLoad: true)`），把已经被删掉的旧承载**重新补回来**，
  /// 于是快捷栏里会同时出现 V1 / V2 / V3 **三套恶灵形态开关**（用哪套都能开，但 V1/V2 还是旧的半额转移量 / 旧动作）。
  /// 这里在"激活"和"读档修复"两个时机各收敛一次，保证只留本阶段的承载（对旧存档同样有效）。
  /// （配套：阶段特性的 `AddFacts` 已设 `doNotRestoreMissingFacts: true`，从源头掐掉"读档补回"。）
  /// </summary>
  public class EnforceSpecterStage : UnitFactComponentDelegate
  {
    /// <summary>应当被移除的旧承载（更高阶段的特性上列出更低阶段的承载）。</summary>
    public BlueprintUnitFactReference[] StaleHolders = new BlueprintUnitFactReference[0];

    private void Enforce()
    {
      var owner = Owner;
      if (owner == null) return;
      foreach (var r in StaleHolders)
      {
        var bp = r?.Get();
        if (bp != null && owner.HasFact(bp)) owner.RemoveFact(bp);
      }
    }

    protected override void OnActivate() => Enforce();
    protected override void OnApplyPostLoadFixes() => Enforce();
  }

  /// <summary>
  /// 赤霄·绝影数据的每效果存档（R1：原 _attacked 写在蓝图组件字段上，所有单位共用）。
  /// </summary>
  public class ChiXiaoStrikeData
  {
    [JsonProperty] public bool Attacked;
  }

  /// <summary>
  /// 赤霄·绝影：下一次攻击检定 +20 洞察，重击威胁自动确认，
  /// 命中附加等于博士圣武士等级的伤害（不吃重击翻倍），随后移除自身。
  /// RuleAttackRoll.AddModifier 是 protected：攻击加值改挂 RuleCalculateAttackBonus，
  /// 伤害走 AddModifierTargetRelated。未命中在 AttackRoll 结束时移除；命中留到伤害后。
  /// R1：_attacked 已挪到每个效果自己的 Data.Attacked 里。
  /// 注意：本组件目前没有被任何法术引用（孤儿组件），仅按 R1 修好存档结构。
  /// </summary>
  public class ChiXiaoStrike : UnitBuffComponentDelegate<ChiXiaoStrikeData>,
      IInitiatorRulebookHandler<RuleCalculateAttackBonus>,
      IInitiatorRulebookHandler<RuleAttackRoll>,
      IInitiatorRulebookHandler<RuleDealDamage>
  {
    public int AttackBonus = 20;
    public ContextValue ExtraDamage;

    public void OnEventAboutToTrigger(RuleCalculateAttackBonus evt)
    {
      if (Data.Attacked || evt.Weapon == null) return;
      evt.AddModifier(AttackBonus, Fact, ModifierDescriptor.Insight);
    }
    public void OnEventDidTrigger(RuleCalculateAttackBonus evt) { }

    public void OnEventAboutToTrigger(RuleAttackRoll evt)
    {
      if (Data.Attacked || evt.Weapon == null) return;
      Data.Attacked = true;
      evt.AutoCriticalConfirmation = true;
    }

    public void OnEventDidTrigger(RuleAttackRoll evt)
    {
      if (Data.Attacked && !evt.IsHit) (Fact as Buff)?.Remove();
    }

    public void OnEventAboutToTrigger(RuleDealDamage evt)
    {
      if (!Data.Attacked) return;
      foreach (var dmg in evt.DamageBundle)
        dmg.AddModifierTargetRelated(ExtraDamage.Calculate(Context), Fact, ModifierDescriptor.UntypedStackable);
    }

    public void OnEventDidTrigger(RuleDealDamage evt)
    {
      if (Data.Attacked) (Fact as Buff)?.Remove();
    }
  }

  /// <summary>
  /// 集火标记：挂在被标记的敌人身上。攻击它的单位获得攻击与伤害洞察加值（数值来自博士等级）。
  /// </summary>
  public class FocusFireMark : UnitBuffComponentDelegate,
      ITargetRulebookHandler<RuleCalculateAttackBonus>,
      ITargetRulebookHandler<RuleDealDamage>
  {
    public ContextValue Bonus;

    public void OnEventAboutToTrigger(RuleCalculateAttackBonus evt)
      => evt.AddModifier(Bonus.Calculate(Context), Fact, ModifierDescriptor.Insight);
    public void OnEventDidTrigger(RuleCalculateAttackBonus evt) { }

    public void OnEventAboutToTrigger(RuleDealDamage evt)
    {
      // B12：按设计只对"攻击"的伤害生效，且每次攻击只加一次。
      // 旧版对伤害包里每一段都加，法术与持续伤害（DOT）也吃加值。
      var weaponDamage = evt.DamageBundle.WeaponDamage; // Weapon==null 时返回 null
      if (weaponDamage == null) return;
      weaponDamage.AddModifierTargetRelated(Bonus.Calculate(Context), Fact, ModifierDescriptor.Insight);
    }
    public void OnEventDidTrigger(RuleDealDamage evt) { }
  }

  /// <summary>
  /// 骑兵突进：只在防御借机攻击时生效的 AC 加值（实现为给攻击者的减值）。
  /// V13：RuleCalculateAttackBonusWithoutTarget 里 AttackBonusPenalty 是"减值"（正数拉低攻击），
  /// 所以这里传正的 Value（旧版传 -Value 反而帮了敌人）。
  /// </summary>
  public class DodgeVsAoO : UnitBuffComponentDelegate,
      ITargetRulebookHandler<RuleAttackRoll>
  {
    public int Value = 4;

    public void OnEventAboutToTrigger(RuleAttackRoll evt)
    {
      if (evt.RuleAttackWithWeapon?.IsAttackOfOpportunity == true)
        evt.SetAttackBonusPenalty(Value);
    }
    public void OnEventDidTrigger(RuleAttackRoll evt) { }
  }

  /// <summary>圣域：只对法术与类法术能力生效的豁免加值。
  /// N10+E5：SourceAbility.IsSpell 在本体为 Type==Spell 或 Type==SpellLike 时为真
  ///（BlueprintAbility.IsSpell：Type!=0 时 return Type==SpellLike），已含类法术能力，无需改。</summary>
  public class SaveBonusVsSpells : UnitFactComponentDelegate,
      ITargetRulebookHandler<RuleSavingThrow>
  {
    public int Value = 4;
    public ModifierDescriptor Descriptor = ModifierDescriptor.Sacred;

    public void OnEventAboutToTrigger(RuleSavingThrow evt)
    {
      if (evt.Reason?.Context?.SourceAbility?.IsSpell == true)
        evt.AddModifier(Value, Fact, Descriptor);
    }
    public void OnEventDidTrigger(RuleSavingThrow evt) { }
  }

  /// <summary>奇美拉：目标的攻击无视所有伤害减免（规则的统一开关）。</summary>
  public class IgnoreDamageReduction : UnitBuffComponentDelegate,
      IInitiatorRulebookHandler<RuleDealDamage>
  {
    public void OnEventAboutToTrigger(RuleDealDamage evt) => evt.IgnoreDamageReduction = true;
    public void OnEventDidTrigger(RuleDealDamage evt) { }
  }

  /// <summary>
  /// 源石技艺精通（2026-10-11 改版）：同时给**法术穿透**与**法术豁免 DC** 加值，
  /// 分 5 档 +1/+2/+3/+4/+5（1/5/9/13/17 级，见 <see cref="ArchetypeSetup.CreateMastery"/>）。
  /// 恶灵锁定生效时，两项加值均失效并改为承受转移量的减值（一段锁定按一半转移量，二/三段按全额）。
  /// 20 级时锁定被压制，自动恢复。
  /// R2：二段改用独立锁定（LockBuffV2），同样按全额倒扣。
  /// 穿透走 <see cref="RuleSpellResistanceCheck"/>；DC 走 <see cref="RuleCalculateAbilityParams"/>。
  /// </summary>
  public class OriginiumMastery : UnitFactComponentDelegate,
      IInitiatorRulebookHandler<RuleSpellResistanceCheck>,
      IInitiatorRulebookHandler<RuleCalculateAbilityParams>
  {
    /// <summary>档位加值：法术穿透与法术豁免 DC **各自** +Value。</summary>
    public int Value = 1;
    public BlueprintBuffReference LockHalved = new();
    public BlueprintBuffReference LockFull = new();
    public BlueprintBuffReference LockFull2 = new();

    /// <summary>是否处于任意恶灵锁定（一/二/三段）之下。20 级 Capstone 在场时一律不算（见 SpecterLockActive）。</summary>
    public bool HasAnyLock()
      => (LockFull != null && BuffHelper.SpecterLockActive(Owner, LockFull))
        || (LockFull2 != null && BuffHelper.SpecterLockActive(Owner, LockFull2))
        || (LockHalved != null && BuffHelper.SpecterLockActive(Owner, LockHalved));

    /// <summary>算出本档位在当前锁定状态下的**实际加值**（锁定中为负，否则为 +Value）。</summary>
    private int EffectiveBonus()
    {
      int intMod = Owner.Stats.Intelligence.Bonus;
      if ((LockFull != null && BuffHelper.SpecterLockActive(Owner, LockFull))
        || (LockFull2 != null && BuffHelper.SpecterLockActive(Owner, LockFull2)))
        return -Math.Max(intMod, 0);         // 二/三段：全额倒扣
      if (LockHalved != null && BuffHelper.SpecterLockActive(Owner, LockHalved))
        return -Math.Max(intMod / 2, 0);     // 一段：半额倒扣
      return Value;                          // 无锁定：正常加值
    }

    public void OnEventAboutToTrigger(RuleSpellResistanceCheck evt)
    {
      int bonus = EffectiveBonus();
      if (bonus != 0) evt.AddSpellPenetration(bonus, ModifierDescriptor.UntypedStackable);
    }
    public void OnEventDidTrigger(RuleSpellResistanceCheck evt) { }

    // 法术豁免 DC：只对**经由法术书施放的法术**生效（evt.Spellbook != null 排除非施法类能力的参数计算）。
    // 反编译依据：RuleCalculateAbilityParams.AddBonusDC(Int32, ModifierDescriptor) —— 与穿透同款"不具类型可叠加"。
    public void OnEventAboutToTrigger(RuleCalculateAbilityParams evt)
    {
      if (evt.Spellbook == null) return; // 非法术书来源（职业能力/物品）不加成
      int bonus = EffectiveBonus();
      if (bonus != 0) evt.AddBonusDC(bonus, ModifierDescriptor.UntypedStackable);
    }
    public void OnEventDidTrigger(RuleCalculateAbilityParams evt) { }
  }

  /// <summary>
  /// 恶灵施法限制：本指令法术书中，只有带【恶灵】标记的法术可以在恶灵期间施放。
  /// 挂在恶灵锁定增益上；锁定被 20 级压制后组件不生效，自动解禁。
  /// 法术名单见 SpellsSetup.AllSpellGuids / SpecterAllowedGuids（均为去横线小写形式）。
  /// </summary>
  public class SpecterSpellRestriction : UnitBuffComponentDelegate,
      IInitiatorRulebookHandler<RuleCastSpell>
  {
    public void OnEventAboutToTrigger(RuleCastSpell evt)
    {
      var blueprint = evt.Spell?.Fact?.Blueprint;
      if (blueprint == null) return;
      // BlueprintGuid 是 struct 不能用 ?.；统一转成去横线小写再比对。
      string guid = blueprint.AssetGuid.ToString().Replace("-", "").ToLowerInvariant();
      if (!SpellsSetup.AllSpellGuids.Contains(guid)) return;      // 不是本法术书的指令：不管
      if (SpellsSetup.SpecterAllowedGuids.Contains(guid)) return; // 【恶灵】指令：允许
      evt.ForceFail = true;
    }
    public void OnEventDidTrigger(RuleCastSpell evt) { }
  }

  /// <summary>R1：处决候选标记的每效果存档（原 _armed 写在蓝图字段上，所有人共用）。</summary>
  public class ExecutionStrikeData
  {
    [JsonProperty] public bool Armed;
  }

  /// <summary>
  /// 处决（红）：下一次近战命中当前生命不高于一半的目标时，武器伤害骰翻倍；
  /// 额外精确伤害不翻倍。触发后增益移除（法术结束）。
  /// V8：RuleAttackWithWeapon 先触发攻击检定（含 DidTrigger）再触发伤害规则，
  /// 旧版在攻击检定的 DidTrigger 就移除增益，组件随即退订，伤害翻倍来不及生效。
  /// 现在改为：攻击检定确认命中后保留，伤害结算时翻骰，伤害结算后再移除。
  /// B4：未命中不消耗（打空保留增益）。
  /// R1：_armed 已挪到每个效果自己的 Data.Armed 里。
  /// </summary>
  public class ExecutionStrike : UnitBuffComponentDelegate<ExecutionStrikeData>,
      IInitiatorRulebookHandler<RuleAttackRoll>,
      IInitiatorRulebookHandler<RuleDealDamage>
  {
    public void OnEventAboutToTrigger(RuleAttackRoll evt)
    {
      if (Data.Armed) return;
      if (evt.Weapon == null || !evt.Weapon.Blueprint.IsMelee) return;
      var target = evt.Target;
      if (target == null) return;
      if (target.HPLeft * 2 > target.MaxHP) return; // 高于半血：不触发
      Data.Armed = true;                                 // 候选，待命中确认
    }

    public void OnEventDidTrigger(RuleAttackRoll evt)
    {
      if (Data.Armed && !evt.IsHit) Data.Armed = false; // 打空：不消耗
    }

    public void OnEventAboutToTrigger(RuleDealDamage evt)
    {
      if (!Data.Armed) return;
      if (evt.DamageBundle.Weapon == null) return; // 只翻武器攻击的伤害骰
      foreach (var dmg in evt.DamageBundle)
      {
        // 武器伤害骰翻倍，只动物理段；精确伤害等额外骰不动。
        // PhysicalDamage.Dice 是只读属性、ModifiableDiceFormula 没有 Rolls/Dice：
        // 读 BaseFormula 再 Modify 追加一份同样的骰数（Modify 语义是追加，加一份 = 翻倍）。
        if (dmg is PhysicalDamage pd)
        {
          var bf = pd.Dice.BaseFormula;
          pd.Dice.Modify(new DiceFormula(bf.Rolls, bf.Dice), ModifierDescriptor.UntypedStackable);
        }
      }
    }

    public void OnEventDidTrigger(RuleDealDamage evt)
    {
      if (Data.Armed && evt.DamageBundle.Weapon != null) (Fact as Buff)?.Remove();
      Data.Armed = false;
    }
  }

  /// <summary>
  /// 墨灵自爆：增益被移除（墨灵死亡或持续结束）时，对与它交战的敌人造成 2d6 力场伤害，
  /// 意志豁免失败再目眩 1 轮（原版目眩增益）。
  /// 走正式伤害管线：ForceDamage + DamageBundle + RuleDealDamage，经 context.TriggerRule 结算，
  /// 因此显示伤害数字、吃抗力/减免等规则。
  /// </summary>
  public class InklingDeathBurst : UnitBuffComponentDelegate
  {
    public int DiceCount = 2;
    public int DieSides = 6;

    // 原版目眩增益（Spells/Level0/DazzledBuff.jbp，BuffStatusCondition{Dazzled}）。
    internal const string VanillaDazzledBuff = "df6d1025da07524429afbae248845ecc";

    protected override void OnDeactivate()
    {
      // P1：补 try/catch —— 本方法内部 TriggerRule(伤害/豁免) + AddBuff 任意异常会冒泡回
      // Buff.OnDetach 组件迭代，打断本增益后面的兄弟组件与整个 buff 移除链（同文件
      // ChainDetonationOnDeath / PaintedWorldBanishTracker.OnDeactivate 均已防护）。
      try
      {
        var owner = Owner;
        if (owner == null || owner.IsInGame == false) return;
        // B18：只在"被摧毁（死亡）"时爆发；召唤时间到期解散不算被摧毁，不自爆。
        if (!owner.Descriptor.State.IsDead) return;
        var engaged = owner.CombatState?.EngagedBy;
        if (engaged == null) return;

        var context = Context ?? Buff?.Context;
        if (context == null) return;

        var diceType = DieSides switch
        {
          2 => DiceType.D2,
          3 => DiceType.D3,
          4 => DiceType.D4,
          8 => DiceType.D8,
          10 => DiceType.D10,
          12 => DiceType.D12,
          20 => DiceType.D20,
          100 => DiceType.D100,
          _ => DiceType.D6,
        };
        var dice = new DiceFormula(DiceCount, diceType);
        int dc = context.Params?.DC ?? 10;

        foreach (var enemy in engaged.ToList())
        {
          if (enemy == null) continue;
          var bundle = new DamageBundle(new ForceDamage(new ModifiableDiceFormula(dice), 0));
          context.TriggerRule(new RuleDealDamage(owner, enemy, bundle));
          var save = context.TriggerRule(new RuleSavingThrow(enemy, SavingThrowType.Will, dc));
          if (!save.IsPassed
            && BlueprintCore.Utils.BlueprintTool.TryGet<BlueprintBuff>(VanillaDazzledBuff, out var dazzled)
            && dazzled != null)
            enemy.Buffs.AddBuff(dazzled, context, System.TimeSpan.FromSeconds(6));
        }
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 墨灵自爆失败：\n" + e);
      }
    }
  }

  /// <summary>
  /// 连锁殉爆：标记增益被移除时，**只在持有者"死亡"**（不是到期 / 被驱散）于尸体处爆一次
  /// （20 尺内敌人 10d6 力场，反射减半按骰数减半）。爆炸本体见 <see cref="ChainDetonation.Explode"/>。
  ///
  /// ⚠ 为什么不用原版 <c>UnitDeathTrigger</c>（2026-10-10 实测踩坑）：那个组件是
  /// **"别的单位在我附近死掉时触发"**——它 <c>HandleEvent</c> 开头就是 <c>if (unit == Owner) return;</c>，
  /// **明确排除"自己死"**（还要求死亡单位落在 <c>RadiusInMeters</c> 内，而它默认是 0）。
  /// 所以挂在标记增益上时，标记者**自己一死反而什么都不发生**（引爆伤害与特效全无）。
  /// 改用本组件：<see cref="UnitBuffComponentDelegate.OnDeactivate"/> 在增益移除时跑，
  /// 再用 <c>IsDead</c> 把"到期 / 被驱散"挡掉——与墨灵自爆（<see cref="InklingDeathBurst"/>）同一套写法。
  /// </summary>
  /// <summary>
  /// 画中世界（作者 2026-10-10 定案：本质是**放逐**）：把目标收进画境 N 轮——这几轮它**不能动、我们也打不到它**；
  /// 到期后**原位返回**并施加**麻痹**（作者：回来之后施加麻痹，才配得上 8 环）。
  ///
  /// 为什么计时器挂在**施法者**身上而不是目标身上：目标被 `IsInGame = false` 后，它的增益会被
  /// `Descriptor.Buffs.SetActive(false)` 一起停用（反编译 `UnitEntityData.OnIsInGameChanged` 确认），
  /// 挂目标身上的增益走不完自己的时长、也就没法自己"回来"。所以载体挂施法者，由本组件在**失效时**放回目标。
  /// </summary>
  public class PaintedWorldBanishTrackerData
  {
    [JsonProperty] public string UnitId;          // 被放逐单位的 UniqueId（可序列化）
    [JsonProperty] public bool Banished;
    [JsonProperty] public int ParalysisRounds = 2; // 返回后麻痹几轮
    /// <summary>P1-B：返回失败、进入兜底重试的状态（读档后会重试找回）。</summary>
    [JsonProperty] public bool PendingRecovery;
  }

  public class PaintedWorldBanishTracker : UnitBuffComponentDelegate<PaintedWorldBanishTrackerData>
  {
    /// <summary>返回后要施加的麻痹增益（创建蓝图时写入）。</summary>
    public BlueprintBuffReference ParalysisBuff;

    protected override void OnActivate()
    {
      try
      {
        // P1-B：读档重激活时，若上一次返回失败（PendingRecovery），在这里**重试找回**——
        // 覆盖"施法时找不到、之后场景/读档恢复正常"的边界。
        // 无论重试是否成功都 return：这是"已经放逐过"的实例，绝不能再走一次放逐分支
        // （重试成功后 Data.Banished 会变 false，若继续往下走会把刚救回的单位**再次放逐**）。
        if (Data.Banished)
        {
          if (Data.PendingRecovery) TryReturnUnit(owner: Owner);
          return;
        }
        var unit = Context?.Root?.MainTarget?.Unit;
        if (unit == null || unit.Descriptor.State.IsDead) return;
        Data.UnitId = unit.UniqueId;
        Data.Banished = true;
        unit.IsInGame = false; // 移除出场：不能动、不可被选为目标
        try { unit.View?.SetVisible(false, true); } catch { }
        Dbg.Log($"[RhodesDoctor] 画中世界：已放逐 {unit.CharacterName}。");
      }
      catch (Exception e) { Main.Mod.Logger.Error("[RhodesDoctor] 画中世界放逐失败：\n" + e); }
    }

    protected override void OnDeactivate()
    {
      try
      {
        if (!Data.Banished) return;
        // P1-B：**不要**先无条件清 Banished —— 先尝试找回，成功才清，
        // 否则"找不到"时会留下"Data 说没放逐、单位其实还在画中"的不一致状态。
        TryReturnUnit(owner: Owner);
      }
      catch (Exception e) { Main.Mod.Logger.Error("[RhodesDoctor] 画中世界返回失败：\n" + e); }
    }

    /// <summary>
    /// 找回被放逐单位并原地返回 + 麻痹。找不到时：
    /// ① 写 **Error 级**日志（release 下也可见，不再被编译期剔除）；
    /// ② 保持 <c>Banished = true</c> 并置 <c>PendingRecovery</c>，挂一个 <see cref="BanishRecovery"/> 兜底重试。
    /// </summary>
    /// <returns>是否成功返回。</returns>
    private bool TryReturnUnit(UnitEntityData owner)
    {
      var unit = FindUnit(Data.UnitId);
      if (unit == null)
      {
        Data.PendingRecovery = true;
        Main.Mod.Logger.Error(
          $"[RhodesDoctor] 画中世界：找不到被放逐的单位（UnitId={Data.UnitId}），已进入兜底重试。");
        if (owner != null) BanishRecovery.Start(owner, this);
        return false;
      }
      unit.IsInGame = true; // 原位返回
      try { unit.View?.SetVisible(true, true); } catch { }
      var bp = ParalysisBuff?.Get();
      if (bp != null)
      {
        // 麻痹轮数 = **1 轮/级**（作者 10-11：画中世界该按"返回后的麻痹轮数"走等级，不是放逐轮数）。
        // ⚠ **不能用 `ContextValues.Rank()`**——这个 Context 是**增益自己的**上下文，取不到挂在能力上的
        //   rank 配置（作者实测"轮/级麻痹没生效"就是这个原因）。改为**直接读施法者的圣武士等级**。
        int paraRounds = Data.ParalysisRounds;
        try
        {
          var caster = Context?.MaybeCaster;
          var paladin = BlueprintCore.Utils.BlueprintTool
            .GetRef<BlueprintCharacterClassReference>(Guids.Vanilla.PaladinClass)?.Get();
          if (caster != null && paladin != null)
            paraRounds = Math.Max(1, caster.Descriptor.Progression.GetClassLevel(paladin));
        }
        catch { }
        UnitHelper.AddBuff(unit, bp, Context, TimeSpan.FromSeconds(Math.Max(1, paraRounds) * 6));
      }
      Data.Banished = false;
      Data.PendingRecovery = false;
      Dbg.Log($"[RhodesDoctor] 画中世界：{unit.CharacterName} 已返回并麻痹。");
      return true;
    }

    /// <summary>P1-B 兜底重试用的回调（由 <see cref="BanishRecovery"/> 调用）。</summary>
    internal bool RetryReturn()
    {
      try
      {
        return Data != null && Data.Banished && TryReturnUnit(Owner);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 画中世界兜底重试失败：\n" + e);
        return true; // 状态异常：视为已处理，停止重试
      }
    }

    private static UnitEntityData FindUnit(string id)
    {
      if (string.IsNullOrEmpty(id)) return null;
      // ⚠ 10-12：按 UniqueId 找回单位**不能**只扫区域场景状态（`CurrentScene.AllEntityData`）——
      // 队伍成员住在 `Player.CrossSceneState`，不在区域状态里，于是**队友**一旦被放逐就永远找不回来
      //（日志"找不到被放逐的单位（可能已离场），跳过返回"）。改走全局单位池，见 `UnitScan.cs`。
      var all = UnitScan.Take().Units;
      if (all == null) return null;
      foreach (var u in all)
        if (u != null && u.UniqueId == id) return u;
      return null;
    }
  }

  /// <summary>
  /// P1-B：画中世界"返回失败"的兜底重试器。挂在施法者身上，按宽限时间点反复走全局单位池找回被放逐单位
  /// （覆盖"施法结束那一刻正处在场景切换 / 单位池瞬时为空"的边界）；找回即返回 + 麻痹并自删，
  /// 超过总宽限仍找不到则以 Error 日志收尾、停止重试（避免永久空转）。
  /// </summary>
  internal sealed class BanishRecovery : MonoBehaviour
  {
    /// <summary>重试时间点（秒）：覆盖"片刻后 / 场景切换后 / 读档后"。</summary>
    private static readonly float[] At = { 0.5f, 2f, 6f, 15f, 30f };

    private PaintedWorldBanishTracker _tracker;
    private int _next;
    private float _t;

    internal static void Start(UnitEntityData owner, PaintedWorldBanishTracker tracker)
    {
      try
      {
        if (owner == null || tracker == null) return;
        if (owner.View == null)
        {
          // P2：View 为 null（caster 处 cross-scene 状态 / 刚发生场景切换）时挂不上 MonoBehaviour。
          // 立刻同步重试一次（最坏只是再走一遍全局单位池），并记 Error 让 release 也可见。
          Main.Mod.Logger.Error("[RhodesDoctor] 画中世界兜底重试器挂载失败（owner.View 为空），本次同步重试一次。");
          tracker.RetryReturn();
          return;
        }
        var go = owner.View.gameObject;
        if (go.GetComponent<BanishRecovery>() != null) return; // 已在重试中，避免读档/重激活重复叠加
        var runner = go.AddComponent<BanishRecovery>();
        runner._tracker = tracker;
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 画中世界兜底重试器挂载失败：\n" + e);
      }
    }

    private void Update()
    {
      if (_tracker == null) { UnityEngine.Object.Destroy(this); return; }
      _t += Time.deltaTime;
      while (_next < At.Length && _t >= At[_next])
      {
        _next++;
        try
        {
          if (_tracker.RetryReturn()) { UnityEngine.Object.Destroy(this); return; }
        }
        catch (Exception e)
        {
          Main.Mod.Logger.Error("[RhodesDoctor] 画中世界兜底重试异常：\n" + e);
        }
      }
      if (_next >= At.Length) UnityEngine.Object.Destroy(this);
    }
  }

  /// <summary>
  /// 火山（作者 10-11）：余焰的**逐轮特效只放一个**——记住施法落点，每轮在那里放一发；
  /// 而不是"每个被余焰烧到的敌人各放一个"（那样有几个敌人就多出几根火柱）。
  /// 载体挂**施法者**（落点在施法上下文里最可靠，且不受敌人多少影响）。
  /// </summary>
  public class VolcanoTrackerData
  {
    [JsonProperty] public Vector3 Point;
    [JsonProperty] public bool Placed;
    [JsonProperty] public float SizeScale = 1.5f;
    [JsonProperty] public float Lifetime = 1.8f;
  }

  public class VolcanoTracker : UnitBuffComponentDelegate<VolcanoTrackerData>
  {
    /// <summary>余焰特效素材（创建蓝图时写入；全 mod 只有火山用，用静态字段最省事）。</summary>
    internal static Kingmaker.ResourceLinks.PrefabLink Fx;

    protected override void OnActivate()
    {
      if (Data.Placed) return;
      var mt = Context?.Root?.MainTarget;
      if (mt != null && mt.Unit != null) Data.Point = mt.Unit.Position;
      else if (mt != null) Data.Point = mt.Point;
      else Data.Point = Owner.Position;
      Data.Placed = true;
    }
  }

  /// <summary>火山：每轮在**记录下来的落点**放一发余焰特效（只一发）。</summary>
  public class ContextActionVolcanoEmberFx : Kingmaker.UnitLogic.Mechanics.Actions.ContextAction
  {
    public override string GetCaption() => "火山：余焰落点特效";

    public override void RunAction()
    {
      if (Context.DisableFx) return;
      if (VolcanoTracker.Fx == null || !VolcanoTracker.Fx.Exists()) return; // P2：素材未回填/缺失时静默跳过，对齐 ContextActionSpawnFxBetweenSummons
      var owner = Context.MaybeOwner ?? Context.MaybeCaster;
      if (owner == null) return;
      foreach (var b in owner.Buffs.RawFacts.ToList())
      {
        var bf = b as Buff;
        if (bf == null) continue;
        var cr = bf.GetComponentWithRuntime<VolcanoTracker>();
        if (cr.Runtime == null) continue;
        var data = cr.Runtime.GetData<VolcanoTrackerData>();
        if (data == null || !data.Placed) continue;
        FxTint.SpawnPlaced(VolcanoTracker.Fx, data.Point, Quaternion.identity,
          data.SizeScale, data.Lifetime);
        return;
      }
    }
  }

  public class ChainDetonationOnDeath : UnitBuffComponentDelegate
  {
    protected override void OnDeactivate()
    {
      try
      {
        var owner = Owner;
        if (owner == null || !owner.IsInGame) return;
        if (!owner.Descriptor.State.IsDead) return; // 只在"被杀死"时爆；到期 / 驱散不算
        var context = Context ?? Buff?.Context;
        ChainDetonation.Explode(context, owner);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 连锁殉爆触发失败：\n" + e);
      }
    }
  }

  /// <summary>
  /// 连锁殉爆的爆炸本体（由 <see cref="ChainDetonationOnDeath"/> 在持有者死亡时调用）。
  /// 以尸体位置为圆心扫场景，**只伤施法者的敌人**（不伤友军与自己）；连锁自动成立
  /// （被炸死的其他标记者各自走自己的 OnDeactivate）。走正式伤害管线，显示伤害数字、吃抗力与减免。
  /// </summary>
  internal static class ChainDetonation
  {
    internal static void Explode(MechanicsContext context, UnitEntityData owner)
    {
      try
      {
        if (context == null || owner == null) return;
        var caster = context.MaybeCaster ?? owner;
        var center = owner.Position;
        // 视觉：尸体处放一次爆炸（复用延时爆破那套已上屏验证的爆发 FX；借不到时静默跳过，伤害照常）。
        BombFx.BoomAt(center);

        float radius = new Feet(20).Meters;
        int dc = context.Params?.DC ?? 10;
        var scene = Game.Instance?.CurrentScene ?? Game.Instance?.LoadedAreaState;
        var units = scene?.AllEntityData?.OfType<UnitEntityData>();
        if (units == null) return;

        int hit = 0;
        foreach (var u in units.ToList())
        {
          if (u == null || u.Descriptor.State.IsDead) continue;
          if (!u.IsEnemy(caster)) continue; // 只伤敌人，不伤友军与博士自己
          if ((u.Position - center).magnitude > radius) continue;
          var save = context.TriggerRule(new RuleSavingThrow(u, SavingThrowType.Reflex, dc));
          int dice = save.IsPassed ? 5 : 10; // 反射减半 → 骰数减半（10d6 → 5d6）
          var bundle = new DamageBundle(new ForceDamage(
            new ModifiableDiceFormula(new DiceFormula(dice, DiceType.D6)), 0));
          context.TriggerRule(new RuleDealDamage(caster, u, bundle));
          hit++;
        }
        Dbg.Log($"[RhodesDoctor] 连锁殉爆：'{owner.CharacterName}' 死亡处爆炸"
          + $"，命中 {hit} 个敌人（半径 {radius:F1} m）。");
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 连锁殉爆爆炸失败：\n" + e);
      }
    }
  }

  /// <summary>
  /// 战术标记（自编，不复用集火）：挂在目标敌人身上。攻击它的单位下一次攻击 +1 洞察；
  /// 攻击结算后标记移除（"下一次攻击"只生效一次）。
  /// </summary>
  public class TacticalMarkDebuff : UnitBuffComponentDelegate,
      ITargetRulebookHandler<RuleCalculateAttackBonus>,
      ITargetRulebookHandler<RuleAttackRoll>
  {
    public int Value = 1;

    public void OnEventAboutToTrigger(RuleCalculateAttackBonus evt)
      => evt.AddModifier(Value, Fact, ModifierDescriptor.Insight);
    public void OnEventDidTrigger(RuleCalculateAttackBonus evt) { }

    public void OnEventAboutToTrigger(RuleAttackRoll evt) { }

    public void OnEventDidTrigger(RuleAttackRoll evt) => (Fact as Buff)?.Remove();
  }

  /// <summary>
  /// 火力侦察（自编）：挂在目标敌人身上；攻击它的单位忽略其隐形、镜影或掩蔽带来的失手几率。
  /// </summary>
  public class FireReconMark : UnitBuffComponentDelegate,
      ITargetRulebookHandler<RuleAttackRoll>
  {
    public void OnEventAboutToTrigger(RuleAttackRoll evt)
    {
      evt.IgnoreConcealment = true;
    }
    public void OnEventDidTrigger(RuleAttackRoll evt) { }
  }

  /// <summary>R1：牵制射击下一次攻击消耗状态的每效果存档。</summary>
  public class SuppressingShotDebuffData
  {
    [JsonProperty] public bool Used;
  }

  /// <summary>
  /// R5：牵制射击（设计文档第 81 行）：目标下一次攻击 −1；若本轮攻击你（博士）以外的生物，改为 −2。
  /// 挂在目标敌人身上（攻击者）。按攻击对象区分罚值，只作用于下一次攻击（标记 Used，整轮保持压制锁定视觉与图标）。
  /// 数据存 SuppressingShotDebuffData，不违反 R1。
  /// </summary>
  public class SuppressingShotDebuff : UnitBuffComponentDelegate<SuppressingShotDebuffData>,
      IInitiatorRulebookHandler<RuleCalculateAttackBonus>,
      IInitiatorRulebookHandler<RuleAttackRoll>
  {
    public void OnEventAboutToTrigger(RuleCalculateAttackBonus evt)
    {
      if (Data.Used) return;
      var caster = Context?.MaybeCaster;
      if (caster == null) return;
      // 攻击博士本人：−1；攻击其他生物：−2。
      int penalty = (evt.Target != null && evt.Target == caster) ? -1 : -2;
      evt.AddModifier(penalty, Fact, ModifierDescriptor.UntypedStackable);
    }
    public void OnEventDidTrigger(RuleCalculateAttackBonus evt) { }

    public void OnEventAboutToTrigger(RuleAttackRoll evt) { }

    public void OnEventDidTrigger(RuleAttackRoll evt)
    {
      if (Data.Used) return;
      Data.Used = true;
    }
  }

  /// <summary>R1：炮火延伸落点/DC 的每效果存档（原 Point/SaveDC 写在蓝图字段上，共用且读档清零）。</summary>
  public class ArtilleryBarrageTrackerData
  {
    [JsonProperty] public Vector3 Point;
    [JsonProperty] public int SaveDC;
    [JsonProperty] public bool Placed;
  }

  /// <summary>
  /// 炮火延伸（定案：逐轮依次爆炸的增益）。挂在施法者身上，记住施法目标点与施法 DC；
  /// 爆炸由 newRound 动作（ContextActionArtilleryStrike）执行，本组件只存数据。
  /// 已知偏差：规则是三个不同落点，实现为同一施法点三轮；反射减半已接（DC 取施法时原法术 DC）。
  /// R1：Point/SaveDC 已挪到每个效果自己的 Data 里（原字段共用且不进存档，读档后落点变原点、DC 变 0）。
  /// </summary>
  public class ArtilleryBarrageTracker : UnitBuffComponentDelegate<ArtilleryBarrageTrackerData>
  {
    protected override void OnActivate()
    {
      // 同 DelayedBlastTracker：buff 自己的 MainTarget 会退化成主人（施法者），
      // 落点必须取本次施法的 AbilityExecutionContext（父链的 Root）。只在第一次激活写点，
      // 读档重激活沿用已存点，避免落点被重置到施法者脚下。
      if (!Data.Placed)
      {
        var mt = Context?.Root?.MainTarget;
        if (mt != null && mt.Unit != null) Data.Point = mt.Unit.Position;
        else if (mt != null) Data.Point = mt.Point;
        else Data.Point = Owner.Position;
        Data.SaveDC = Context?.Params?.DC ?? 10;
        Data.Placed = true;
      }
      else if (Data.SaveDC <= 0)
      {
        Data.SaveDC = Context?.Params?.DC ?? 10;
      }
    }
  }

  /// <summary>R1/C33：延时爆破独立记录的每效果存档（原借用炮火记录， double 法术同时生效会读错）。</summary>
  public class DelayedBlastTrackerData
  {
    [JsonProperty] public Vector3 Point;
    [JsonProperty] public int SaveDC;
    [JsonProperty] public bool Placed;
    // 地面标记实例：per-fact 的运行时引用，不进存档（读档后由 OnActivate 重新生成）。
    [JsonIgnore] public GameObject Marker;
  }

  /// <summary>
  /// 延时爆破追踪：挂在施法者身上，记住布设点与 DC（C33 独立记录，不借用炮火）。
  /// R1：存每个效果自己的 Data。
  /// 地面标记由本组件自己生成/销毁（跟炸弹同生同灭），不依赖区域 Fx。
  /// </summary>
  public class DelayedBlastTracker : UnitBuffComponentDelegate<DelayedBlastTrackerData>
  {
    protected override void OnActivate()
    {
      // 关键坑：Context 是 buff 自己的上下文——AddBuff 会 CloneFor 成一个新的 MechanicsContext，
      // 它的 m_MainTarget 为空，MainTarget 于是回退到“buff 主人”（反编译 MechanicsContext.MainTarget：
      // m_MainTarget ?? m_OwnerRef.Entity）。所以直接读 Context.MainTarget 拿到的是施法者本人，
      // 而不是玩家点击的目标——炸弹会被丢在博士脚下。
      // 要点必须沿父链找到本次施法的 AbilityExecutionContext（Root），它的 MainTarget 才是点击目标。
      // 只在第一次激活写点：读档重激活时父链已不在、Data.Placed 已存，沿用已存点，
      // 否则爆心会被重置到施法者脚下（炸弹跟人走）。
      if (!Data.Placed)
      {
        var mt = Context?.Root?.MainTarget;
        if (mt != null && mt.Unit != null) Data.Point = mt.Unit.Position;
        else if (mt != null) Data.Point = mt.Point;
        else Data.Point = Owner.Position;
        Data.SaveDC = Context?.Params?.DC ?? 10;
        Data.Placed = true;
      }
      else if (Data.SaveDC <= 0)
      {
        Data.SaveDC = Context?.Params?.DC ?? 10;
      }
      // 地面炸弹标记：自己实例化（借来的区域 Fx 实测不上屏），存在 per-fact 的 Data 上随 buff 销毁。
      if (Data.Marker == null) BombFx.PlaceMarker(ref Data.Marker, Data.Point);
    }

    protected override void OnDeactivate()
    {
      // P1：包 try/catch —— BombFx.DestroyMarker 对已销毁对象再 Dispose 可能抛异常，
      // 否则会冒泡打断 Buff 移除链（与同文件其它 OnDeactivate 对齐）。
      try
      {
        BombFx.DestroyMarker(ref Data.Marker);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 延时爆破标记销毁失败：\n" + e);
      }
    }
  }

  /// <summary>
  /// 墨灵攻击：以召唤者 BAB 攻击，命中附加 2d6 力场伤害。
  /// 补正 = 召唤者 BAB − 自身 BAB（只补正向差）；力场段直接追加到伤害包。
  /// N4+E4：原读 Owner.Master（只看 UnitPartPet，召唤物没有主人，从没生效）；现读
  /// Owner.Get&lt;UnitPartSummonedMonster&gt;().Summoner（RuleSummonUnit.OnTrigger 写入，SpawnMonster 与
  /// 镜中流形都走这条），取不到时回退 Context.MaybeCaster。
  /// </summary>
  public class InklingFightsWithMasterBAB : UnitFactComponentDelegate,
      IInitiatorRulebookHandler<RuleCalculateAttackBonusWithoutTarget>,
      IInitiatorRulebookHandler<RuleDealDamage>
  {
    public void OnEventAboutToTrigger(RuleCalculateAttackBonusWithoutTarget evt)
    {
      if (evt.Weapon == null) return;
      var summoner = Owner.Get<Kingmaker.UnitLogic.Parts.UnitPartSummonedMonster>()?.Summoner
        ?? Context?.MaybeCaster
        ?? Owner.Master;
      if (summoner == null) return;
      int diff = summoner.Stats.BaseAttackBonus.ModifiedValue
        - Owner.Stats.BaseAttackBonus.ModifiedValue;
      if (diff > 0)
        evt.AddModifier(diff, Fact, ModifierDescriptor.UntypedStackable);
    }
    public void OnEventDidTrigger(RuleCalculateAttackBonusWithoutTarget evt) { }

    public void OnEventAboutToTrigger(RuleDealDamage evt)
    {
      // B15：只在武器攻击的伤害包上追加 2d6 力场；墨灵自爆等非武器伤害不吃这 2d6。
      if (evt.DamageBundle.Weapon == null) return;
      evt.Add(new ForceDamage(
        new ModifiableDiceFormula(new DiceFormula(2, DiceType.D6)), 0));
    }
    public void OnEventDidTrigger(RuleDealDamage evt) { }
  }

  /// <summary>
  /// 儿时舞乐：受到的伤害 +百分比如（受伤易伤）。
  /// 原版只有固定值易伤（BuffIncomingDamageIncrease），百分比自写。
  /// </summary>
  public class TakeExtraDamagePercent : UnitBuffComponentDelegate,
      ITargetRulebookHandler<RuleDealDamage>
  {
    public int Percent = 30;

    public void OnEventAboutToTrigger(RuleDealDamage evt)
    {
      foreach (var dmg in evt.DamageBundle) dmg.BonusPercent += Percent;
    }
    public void OnEventDidTrigger(RuleDealDamage evt) { }
  }

  /// <summary>R1：归溟每轮计数的每效果存档（原字段共用，多人/多次施放互相覆盖）。</summary>
  public class ReturnToAbyssHungerData
  {
    [JsonProperty] public int HealsThisRound;
    [JsonProperty] public int RefundedThisRound;
  }

  /// <summary>
  /// 归溟（幽灵鲨）：近战命中回 2d6（每轮至多三次）；每轮首次重击或击杀返还 1 轮恶灵时间。
  /// 计数按轮清零（newRound 动作调 ResetRound，炮火追踪同款手法）。
  /// R1：计数已挪到每个效果自己的 Data 里；ResetRound 改为静态，按 Buff 取 Data 清零。
  /// </summary>
  public class ReturnToAbyssHunger : UnitBuffComponentDelegate<ReturnToAbyssHungerData>,
      IInitiatorRulebookHandler<RuleAttackRoll>,
      IInitiatorRulebookHandler<RuleDealDamage>
  {
    public BlueprintAbilityResourceReference SpecterResource;

    internal static void ResetRound(Buff buff)
    {
      if (buff == null) return;
      var cr = buff.GetComponentWithRuntime<ReturnToAbyssHunger>();
      if (cr.Runtime == null) return;
      var data = cr.Runtime.GetData<ReturnToAbyssHungerData>();
      if (data == null) return;
      data.HealsThisRound = 0;
      data.RefundedThisRound = 0;
    }

    public void OnEventAboutToTrigger(RuleAttackRoll evt) { }
    public void OnEventDidTrigger(RuleAttackRoll evt)
    {
      if (!evt.IsHit || evt.Weapon == null || !evt.Weapon.Blueprint.IsMelee) return;
      if (Data.HealsThisRound >= 3) return;
      var context = Context ?? (Fact as Buff)?.Context;
      if (context == null) return;
      int roll = context.TriggerRule(new RuleRollDice(Owner,
        new DiceFormula(2, DiceType.D6))).Result;
      Owner.Descriptor.Damage = System.Math.Max(0, Owner.Descriptor.Damage - roll);
      Data.HealsThisRound++;
      ReturnToAbyssFx.PlayHeal(evt.Target, Owner);
      if (Data.RefundedThisRound == 0 && evt.IsCriticalConfirmed)
      {
        RefundSpecterRound();
        Data.RefundedThisRound++;
        ReturnToAbyssFx.PlayRefund(Owner);
      }
    }

    public void OnEventAboutToTrigger(RuleDealDamage evt) { }
    public void OnEventDidTrigger(RuleDealDamage evt)
    {
      if (Data.RefundedThisRound > 0) return;
      if (evt.Target == null || !evt.Target.Descriptor.State.IsDead) return;
      RefundSpecterRound();
      Data.RefundedThisRound++;
      ReturnToAbyssFx.PlayRefund(Owner);
    }

    private void RefundSpecterRound()
    {
      var resBp = SpecterResource?.GetBlueprint() as Kingmaker.Blueprints.BlueprintScriptableObject;
      if (resBp == null) return;
      Owner.Descriptor.Resources.Restore(resBp, 1);
    }
  }

  /// <summary>R1：罪罚每轮标记的每效果存档（原字段共用）。</summary>
  public class SinPunishmentRetributionData
  {
    [JsonProperty] public bool TriggeredThisRound;
  }

  /// <summary>
  /// 罪罚（斥罪）：目标每轮第一次对其他生物造成伤害，自己受一半——单段同类型，多段力场兜底。
  /// 计数按轮清零（newRound 动作调 ResetRound）；触发后本轮不再触发，天然防递归。
  /// R1：标记已挪到每个效果自己的 Data 里；ResetRound 改为静态，按 Buff 取 Data 清零。
  /// </summary>
  public class SinPunishmentRetribution : UnitBuffComponentDelegate<SinPunishmentRetributionData>,
      IInitiatorRulebookHandler<RuleDealDamage>
  {
    internal static void ResetRound(Buff buff)
    {
      if (buff == null) return;
      var cr = buff.GetComponentWithRuntime<SinPunishmentRetribution>();
      if (cr.Runtime == null) return;
      var data = cr.Runtime.GetData<SinPunishmentRetributionData>();
      if (data == null) return;
      data.TriggeredThisRound = false;
    }

    public void OnEventAboutToTrigger(RuleDealDamage evt) { }
    public void OnEventDidTrigger(RuleDealDamage evt)
    {
      // P1：补 try/catch —— DidTrigger 内反向触发 RuleDealDamage 抛异常会冒泡到 rulebook 事件迭代，
      // 影响同次其它 DidTrigger 订阅者；移除自身增益也易抛。
      try
      {
        if (Data.TriggeredThisRound) return;
        if (evt.Target == null || evt.Target == Owner) return; // 只对"其他生物"
        if (evt.Reason?.Fact == Fact) return; // 自己反的伤不再触发
        int half = evt.Result / 2;
        if (half <= 0) return;
        var segments = evt.DamageBundle.ToList();
        BaseDamage back;
        if (segments.Count == 1)
          back = HalvedSameType(segments[0], half);
        else
          back = new ForceDamage(new ModifiableDiceFormula(new DiceFormula(0, DiceType.Zero)), half);
        var context = Context ?? (Fact as Buff)?.Context;
        if (context == null) return;
        // P1：先触发反伤，成功后才置位 TriggeredThisRound（原置位于 Trigger 之前，
        // TriggerRule 抛异常会"标记已扣但反伤无效"）。
        context.TriggerRule(new RuleDealDamage(Owner, Owner, new DamageBundle(back)));
        Data.TriggeredThisRound = true;
        // 律法审判专属惩击特效（从天而降圣光裁决 + 地面震荡金环）
        SinPunishmentFx.PlayRetributionSmite(Owner);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 罪罚·报应反伤失败：\n" + e);
      }
    }

    /// <summary>单段按原类型返固定值；骰数清零（总数对，波动没了）。</summary>
    private static BaseDamage HalvedSameType(BaseDamage segment, int half)
    {
      var zero = new ModifiableDiceFormula(new DiceFormula(0, DiceType.Zero));
      if (segment is PhysicalDamage pd)
        return new PhysicalDamage(zero, half, pd.Form);
      if (segment is EnergyDamage ed)
        return new EnergyDamage(zero, half, ed.EnergyType);
      if (segment is ForceDamage)
        return new ForceDamage(zero, half);
      if (segment is DirectDamage)
        return new DirectDamage(new DiceFormula(0, DiceType.Zero), half);
      return new ForceDamage(zero, half);
    }
  }

  /// <summary>R1：迅捷突破命中标记的每效果存档（原 _hitConfirmed 共用，多人会互相消耗）。</summary>
  public class BreachStrikeData
  {
    [JsonProperty] public bool HitConfirmed;
  }

  /// <summary>
  /// 迅捷突破：下一次近战命中附加 3d6，结算后移除。不引发借机攻击见 D2（单独组件）。
  /// 顺序：命中先记名（不删增益），伤害结算时加段再删——删早了伤害段挂不上。
  /// R1：_hitConfirmed 已挪到每个效果自己的 Data 里。
  /// </summary>
  public class BreachStrike : UnitBuffComponentDelegate<BreachStrikeData>,
      IInitiatorRulebookHandler<RuleAttackRoll>,
      IInitiatorRulebookHandler<RuleDealDamage>
  {
    public void OnEventAboutToTrigger(RuleAttackRoll evt) { }
    public void OnEventDidTrigger(RuleAttackRoll evt)
    {
      if (!evt.IsHit || evt.Weapon == null || !evt.Weapon.Blueprint.IsMelee) return;
      Data.HitConfirmed = true;
    }

    public void OnEventAboutToTrigger(RuleDealDamage evt)
    {
      if (!Data.HitConfirmed) return;
      Data.HitConfirmed = false;
      // C28：去掉多出来的"+基本攻击加值"伤害，只保留 3d6。
      evt.Add(new UntypedDamage(new DiceFormula(3, DiceType.D6), 0));
      (Fact as Buff)?.Remove();
    }
    public void OnEventDidTrigger(RuleDealDamage evt) { }
  }

  /// <summary>R1：永恒注视触发标记的每效果存档（原 _used 共用：一个人触发后整局所有人都不再触发）。</summary>
  public class EternalGazeWatchData
  {
    [JsonProperty] public bool Used;
  }

  /// <summary>
  /// 永恒的注视：持有者将死时拦截一次——回满、传到施法者身边、加速（C34 通用加速）。
  /// IUnitDeathPreventionHandler 是 PubSub 死亡拦截（KeepAlliesAlive 同款），非规则钩子，不违反单接口约定。
  /// R1：_used 已挪到每个效果自己的 Data.Used 里（事件经 ISubscriptionProxy 带 Runtime 上下文，Data 按 Buff 隔离）。
  /// </summary>
  public class EternalGazeWatch : UnitBuffComponentDelegate<EternalGazeWatchData>,
      Kingmaker.PubSubSystem.IUnitDeathPreventionHandler
  {
    /// <summary>C36：免死触发后给的加速增益（C34）。来源是新挂的增益，不是正在移除的守护增益。</summary>
    public BlueprintBuffReference AccelBuff;

    public void HandleDeadlyDamage(UnitEntityData unit)
    {
      if (Data.Used || unit == null || unit != Owner) return;
      Data.Used = true;
      unit.Descriptor.Damage = 0;
      unit.Descriptor.Stats.DamageNonLethal.RemoveAllModifiers();
      var caster = Context?.MaybeCaster;
      if (caster != null)
      {
        // B14：不写死偏移量（开发笔记第 3 条）。取施法者朝向的反方向约 5 尺作候选，
        // 再用 FreePlaceSelector 推离单位、贴回导航网格（只改 x/z）。
        var dir = (unit.Position - caster.Position);
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : (-caster.OrientationDirection);
        var points = new[] { caster.Position + dir * 1.5f };
        var radii = new[] { unit.View != null ? unit.View.Corpulence : 0.5f };
        FreePlaceSelector.RelaxPoints(points, radii, 1, caster.Position);
        var dest = points[0];
        dest.y = unit.Position.y;
        unit.Translocate(dest, unit.Orientation);
      }
      // C36：从触发那一刻起获得 1 分钟的加速效果。
      var accel = AccelBuff?.Get() as BlueprintBuff;
      if (accel != null)
        Owner.Buffs.AddBuff(accel, Context, System.TimeSpan.FromMinutes(1));
    }

    public void HandleAttackOnWalkingDead(UnitEntityData unit) { }
  }

  /// <summary>R1：岁稔积攒数的每效果存档（原 Stored/CasterLevel 共用：全员累加到同一数且不清零）。</summary>
  public class HarvestYearsAccumulatorData
  {
    [JsonProperty] public int Stored;
    [JsonProperty] public int CasterLevel;
  }

  /// <summary>
  /// 岁稔积攒：记下每轮溢出的治疗，结束时按上限转临时生命（上限 60，持续 1 分钟）。
  /// B13：旧版在 OnDeactivate 里用"正在被移除的增益"当 source 直接加临时生命修饰，
  /// 结果要么随增益一起被清掉、要么永久保留（不可靠）。
  /// 现在改为：结束时挂一个新的"岁稔·余温"增益（1 分钟），把额度写进它的 Data 里，
  /// 由该增益自己的生命周期负责加上与移除临时生命。
  /// R1：Stored/CasterLevel 已挪到每个效果自己的 Data 里。
  /// </summary>
  public class HarvestYearsAccumulator : UnitBuffComponentDelegate<HarvestYearsAccumulatorData>
  {
    /// <summary>结束时挂的余温增益（由 SpellsSetup 创建后回填）。</summary>
    public BlueprintBuffReference ResidualBuff;

    protected override void OnDeactivate()
    {
      // P1：补 try/catch —— 内部 AddBuff / GetData / AddModifier 任意异常会打断增益移除链
      //（与 HarvestYearsResidual.OnDeactivate 对齐）。
      try
      {
        if (Data.Stored <= 0) return;
        var owner = Owner;
        var context = Context ?? Buff?.Context;
        if (owner == null || context == null) return;
        int grant = System.Math.Min(Data.Stored, 60); // C31：上限 60，不是 60+等级
        if (grant <= 0) return;
        var bp = ResidualBuff?.Get() as BlueprintBuff;
        if (bp == null)
        {
          // P1：余温蓝图未回填时 Stored 额度会静默丢失；记 Error 让玩家可见、便于排查。
          Main.Mod.Logger.Error("[RhodesDoctor] 岁稔·余温 ResidualBuff 蓝图为空，Stored=" + Data.Stored + " 额度丢失。");
          return;
        }
        var residual = owner.Buffs.AddBuff(bp, context, System.TimeSpan.FromMinutes(1));
        if (residual == null) return;
        // R1：额度写进余温增益自己的 Data，并以余温自己的 Runtime 为来源加修饰。
        var cr = residual.GetComponentWithRuntime<HarvestYearsResidual>();
        if (cr.Runtime == null) return;
        var rdata = cr.Runtime.GetData<HarvestYearsResidualData>();
        if (rdata == null) return;
        rdata.Amount = grant;
        if (rdata.Modifier == null && grant > 0)
          rdata.Modifier = owner.Stats.TemporaryHitPoints.AddModifier(
            grant, cr.Runtime, ModifierDescriptor.UntypedStackable);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 岁稔积攒→余温注入失败：\n" + e);
      }
    }
  }

  /// <summary>
  /// R1：岁稔余温额度/修饰的每效果存档（原 Amount/_modifier 共用：第二个人起直接跳过）。
  /// P1-A（2026-10-11 审计修复）：<see cref="Modifier"/> **不进存档**（<c>[JsonIgnore]</c>）——
  /// 它是运行时 <c>ModifiableValue.Modifier</c>（含 Runtime/Stat 等运行时引用），序列化进存档后，
  /// 读档时反序列化出的会是一个"孤立对象"：① 使 <see cref="HarvestYearsResidual"/> 的
  /// <c>Data.Modifier == null</c> 守卫不成立 → 临时生命**不会重建**（效果静默丢失）；
  /// ② 对孤立对象调 <c>Remove()</c> 可能抛异常。改为只存 <see cref="Amount"/>，
  /// 由 OnActivate 按额度**重建**修饰器。
  /// </summary>
  public class HarvestYearsResidualData
  {
    [JsonProperty] public int Amount;
    [JsonIgnore] public ModifiableValue.Modifier Modifier;
  }

  /// <summary>
  /// 岁稔·余温：承载结束时的临时生命。额度由 <see cref="HarvestYearsAccumulator"/> 注入，
  /// 由本增益的激活/结束负责加上/移除（source = 本组件自己的 Runtime，随本增益可靠清除）。
  /// R1：Amount/Modifier 已挪到每个效果自己的 Data 里。
  /// </summary>
  public class HarvestYearsResidual : UnitBuffComponentDelegate<HarvestYearsResidualData>
  {
    protected override void OnActivate()
    {
      // 额度由积攒者在 AddBuff 后写入 Data；若先天已有额度（读档），在这里补加修饰。
      // 正常流程 AddBuff 时 Amount 尚未写入，OnActivate 不加，由积攒者直接加。
      // P1-A：Modifier 已 [JsonIgnore] —— 读档后必为 null，这里按 Amount **重建**，避免临时生命丢失。
      try
      {
        if (Data.Amount > 0 && Data.Modifier == null)
          Data.Modifier = Owner.Stats.TemporaryHitPoints.AddModifier(
            Data.Amount, Runtime, ModifierDescriptor.UntypedStackable);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 岁稔·余温临时生命重建失败：\n" + e);
      }
    }

    protected override void OnDeactivate()
    {
      // P1-A：补 try/catch（本文件其它 OnDeactivate 均有防护）——即使 Modifier 异常也不打断增益移除链。
      try
      {
        Data.Modifier?.Remove();
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 岁稔·余温临时生命移除失败：\n" + e);
      }
      finally
      {
        Data.Modifier = null;
      }
    }
  }

  /// <summary>
  /// 恶灵余波的"只能移动动作"限制（=原版 Nauseated 语义），**但 20 级 Capstone 在场时不生效**。
  ///
  /// ⚠ 为什么不用纯 `AddCondition(Nauseated)`：20 级本意靠 `SuppressBuffsFixed` 压制余波限制增益，
  /// 但实机取证表明该压制链路不可靠（见 <see cref="BuffHelper.SpecterLockActive"/> 的说明）。
  /// 这里改成"组件自己判断 Capstone 在不在"：在场则不施加 `Nauseated`，与压制机制解耦。
  /// Capstone 是 20 级**永久**特性，余波触发时通常已在身上；激活时判一次即可。
  /// </summary>
  public class AftermathRestriction : UnitFactComponentDelegate
  {
    private static bool CapstonePresent(UnitEntityData owner)
      => BuffHelper.HasActive(owner, BlueprintCore.Utils.BlueprintTool.GetRef<BlueprintUnitFactReference>(Guids.CapstoneBuff));

    protected override void OnActivate()
    {
      base.OnActivate();
      var owner = Owner;
      if (owner?.Descriptor == null) return;
      if (CapstonePresent(owner))
      {
        // P1：Capstone 在场 ⇒ 人灵合一，不施加 Nauseated。
        // ⚠ 不要在这里 RemoveCondition —— 否则当身上同时有别的来源 Nauseated 时会把那一份一起误删
        // （Add 0 次 / Remove 1 次 不对称）。OnDeactivate 只移除本组件加的那一次；这里直接 return。
        Dbg.Log("[RhodesDoctor] 余波限制：检测到 20 级 Capstone，不施加 Nauseated。");
        return;
      }
      owner.Descriptor.State.AddCondition(UnitCondition.Nauseated);
    }

    protected override void OnDeactivate()
    {
      try { Owner?.Descriptor?.State?.RemoveCondition(UnitCondition.Nauseated); }
      catch (Exception e) { Main.Mod.Logger.Error("[RhodesDoctor] 余波限制移除 Nauseated 失败：\n" + e); }
      base.OnDeactivate();
    }
  }

  /// <summary>
  /// 恶灵施法限制（B19）：本指令法术书中，只有带【恶灵】标记的法术可以在恶灵期间施放。
  /// 与旧的"施法时 ForceFail"不同，这是施法者限制组件：未达标时按钮直接变灰（不消耗法术位）。
  /// 挂在每个非【恶灵】指令法术上，LockFacts 指向一/二段与三段锁定增益；
  /// 锁定被 20 级压制后 HasActive 视为不存在，限制自动解除。
  /// </summary>
  public class SpecterCasterRestriction : BlueprintComponent, IAbilityCasterRestriction
  {
    public BlueprintUnitFactReference[] LockFacts = new BlueprintUnitFactReference[0];
    public string NotText = "巴别塔的恶灵生效期间只能施放【恶灵】指令";

    public bool IsCasterRestrictionPassed(UnitEntityData caster)
    {
      bool passed = LockFacts.All(f => !BuffHelper.SpecterLockActive(caster, f));
      if (!passed) DescribeLocks(caster);
      return passed;
    }

    /// <summary>
    /// 诊断：列出每个锁定在身上的"存在 / 压制"状态，用于判断 20 级压制是否真的生效。
    /// 标 [Conditional("RD_VERBOSE")]：发布版连方法体一起被编译掉（round 10 规范）。
    /// </summary>
    [System.Diagnostics.Conditional("RD_VERBOSE")]
    private void DescribeLocks(UnitEntityData caster)
    {
      if (caster == null) return;
      var sb = new System.Text.StringBuilder();
      foreach (var f in LockFacts)
      {
        string g = f?.Guid.ToString();
        string state = "无";
        foreach (var b in caster.Buffs.RawFacts)
        {
          if (b?.Blueprint == null) continue;
          if (b.Blueprint.AssetGuid == g)
            state = b.IsSuppressed ? $"存在(已压制,Active={b.Active})" : "存在(未压制)";
        }
        sb.Append($" {g}=>{state};");
      }
      // 压制部件是否存在（BlueprintCore 的 Fixed 部件）
      var part = caster.Get<BlueprintCore.UnitParts.Replacements.UnitPartBuffSuppressFixed>();
      sb.Append($" 压制部件={(part != null ? "有" : "无")}");
      // 20 级 Capstone 增益是否在身上
      bool cap = false;
      foreach (var b in caster.Buffs.RawFacts)
      {
        if (b?.Blueprint != null && b.Blueprint.AssetGuid == Guids.CapstoneBuff) cap = true;
      }
      sb.Append($" 20级Capstone={(cap ? "在身上" : "不在身上")}");
      // 可能阻断施法的单位状态（反编译 AbilityData.CanBeCastByCaster 的相关分支）
      var st = caster.Descriptor?.State;
      if (st != null)
      {
        sb.Append($" [CanCast={st.CanCast}"
          + $" UseAbilityForbidden={st.HasCondition(UnitCondition.UseAbilityForbidden)}"
          + $" SpellcastingForbidden={st.HasCondition(UnitCondition.SpellcastingForbidden)}"
          + $" CantUseStandardActions={st.HasCondition(UnitCondition.CantUseStandardActions)}"
          + $" Staggered={st.HasCondition(UnitCondition.Staggered)}"
          + $" Nauseated={st.HasCondition(UnitCondition.Nauseated)}]");
      }
      Dbg.Log($"[RhodesDoctor] 施法限制未通过（非【恶灵】指令）：{sb}");
    }

    public string GetAbilityCasterRestrictionUIText() => NotText;
  }

  /// <summary>
  /// V4：战术指令法术书不受护甲奥术失败率影响。
  /// 反编译依据：AbilityData.IsAffectedByArcaneSpellFailure 对奥秘自发法术书 + 非 Mythic 独立书为 true，
  /// 然后 RuleCastSpell 触发 RuleCalculateArcaneSpellFailureChance；该规则有公开的 IgnoreArmor/IgnoreShield。
  /// 本组件把二者置真，等价于"本法术书忽略护甲与盾牌的奥术失败率"。
  /// </summary>
  public class IgnoreArcaneSpellFailure : UnitFactComponentDelegate,
      IInitiatorRulebookHandler<RuleCalculateArcaneSpellFailureChance>
  {
    public void OnEventAboutToTrigger(RuleCalculateArcaneSpellFailureChance evt)
    {
      evt.IgnoreArmor = true;
      evt.IgnoreShield = true;
    }
    public void OnEventDidTrigger(RuleCalculateArcaneSpellFailureChance evt) { }
  }

  /// <summary>R1+C30：文明存续一次性免死的每效果存档（原 UnitHealthGuard 在增益期内可反复保命）。</summary>
  public class CivilizationDeathGuardData
  {
    [JsonProperty] public bool Used;
  }

  /// <summary>
  /// C30：文明的存续"降到 0 改为保留 1 生命"只生效一次（设计文档第 292 行）。
  /// IUnitDeathPreventionHandler 拦截致死伤害：首次改为保留 1 生命并标记已用；之后不再拦截。
  /// R1：Used 存每个效果自己的 Data。
  /// </summary>
  public class CivilizationDeathGuard : UnitBuffComponentDelegate<CivilizationDeathGuardData>,
      Kingmaker.PubSubSystem.IUnitDeathPreventionHandler
  {
    public void HandleDeadlyDamage(UnitEntityData unit)
    {
      if (Data.Used || unit == null || unit != Owner) return;
      Data.Used = true;
      // 保留 1 生命：已受伤害 = 最大生命 - 1。
      int maxHP = unit.Descriptor.MaxHP;
      unit.Descriptor.Damage = System.Math.Max(0, maxHP - 1);
    }

    public void HandleAttackOnWalkingDead(UnitEntityData unit) { }
  }

  /// <summary>
  /// 战术指令「已知法术」补发（作者 2026-10-11）：让**已有存档的角色**也能拿到按等级应得的全套已知指令。
  ///
  /// 背景：升级界面按「已知法术表」的**累计差值**发放名额，只对"从低到高**逐级升上来**"的角色生效；
  /// 对**已经练到高等级**（或旧存档）的角色，历史等级那一步不会再跑，于是永远差着一批已知法术。
  /// 本组件在**两个时机**补齐：
  ///   · `OnActivate`（含读档后的 post-load 修复）—— 覆盖旧存档；
  ///   · `HandleUnitAfterLevelUp` —— 覆盖新升级（升级界面已给的名额不重复：本组件按"应有总数"补差）。
  /// 补发口径：每一环，把**主法术列表该环的前 N 个**（N = 该环在法术书当前等级应拿的累计名额）
  /// 设为已知；10 环同理（N 上限 = 4，即那 4 个 10 环指令）。**幂等**：已会的不重复加。
  /// 挂在 1 级那个"授予战术指令法术书"的特性上（`Guids.SpellcastingFeature`）。
  /// </summary>
  public class GrantKnownOrdersBackfill : UnitFactComponentDelegate,
      Kingmaker.PubSubSystem.IUnitLevelUpHandler
  {
    public void HandleUnitBeforeLevelUp(UnitEntityData unit) { }
    public void HandleUnitAfterLevelUp(UnitEntityData unit, Kingmaker.UnitLogic.Class.LevelUp.LevelUpController controller)
      => Backfill(unit);

    protected override void OnActivate()
    {
      base.OnActivate();
      // ⚠ 守卫：角色创建 / 升级流程期间**不补发**——此时 Game.Instance.LevelUpController 非空，
      // 升级界面会按「已知法术表」的累计差值正常给玩家"选择名额"（玩家自己选）。
      // 本组件的 OnActivate 补发**只用于读档后**（LevelUpController == null）覆盖旧存档。
      //
      // 为什么必须挡住 CharGen 期间：1 级创建角色时，SpellcastingFeature 一激活本组件就会
      // 直接 AddKnown 一个 1 环法术（列表第 1 个），既绕过了玩家的选择，又会被 CharGen 完成、
      // 法术书重建时清理掉——表现为"创建人物时法术书里录进了一个法术、创建完却消失了"。
      // 挡住后，1 级创建走正常升级界面：已知法术表算 GetCount(法术书等级=2, 1) - GetCount(0, 1) = 1，
      // 给 1 个 1 环选择名额，玩家自己选，创建完不再丢。
      if (Game.Instance?.LevelUpController != null) return;
      Backfill(Owner);
    }

    private void Backfill(UnitEntityData unit)
    {
      try
      {
        if (unit?.Descriptor == null) return;
        var book = BlueprintCore.Utils.BlueprintTool.Get<BlueprintSpellbook>(Guids.Spellbook);
        if (book == null) return;
        var sb = unit.Descriptor.GetSpellbook(book);
        if (sb == null) return; // 不是博士

        int cl = sb.BaseLevel; // 累计名额按法术书等级取
        int granted = 0;
        for (int ring = 0; ring <= 10; ring++)
        {
          int budget = SpellbookSetup.KnownBudget(cl, ring);
          if (budget <= 0) continue;
          var ringSpells = new System.Collections.Generic.List<BlueprintAbility>();
          var ringSpellSet = new System.Collections.Generic.HashSet<BlueprintAbility>();
          foreach (var idx in SpellsSetup.SpellsOfLevel(ring))
          {
            var ab = BlueprintCore.Utils.BlueprintTool.Get<BlueprintAbility>(Guids.Spell(idx));
            if (ab != null && ringSpellSet.Add(ab)) ringSpells.Add(ab);
          }
          int have = 0;
          foreach (var a in sb.GetAllKnownSpells())
            if (a != null && a.SpellLevel == ring && ringSpellSet.Contains(a.Blueprint)) have++;
          if (have >= budget) continue;

          int need = budget - have;
          foreach (var ab in ringSpells)
          {
            if (need <= 0) break;
            // 幂等：已会就跳过（不 Remove，以免清掉玩家自己在升级界面选的同环法术）。
            bool known = false;
            foreach (var a in sb.GetAllKnownSpells())
              if (a != null && a.Blueprint == ab) { known = true; break; }
            if (known) continue;
            sb.AddKnown(ring, ab);
            need--; granted++;
          }
        }
        Dbg.Log($"[RhodesDoctor] 已知指令补发：法术书等级 {cl}，本次新增 {granted} 个。");
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error($"[RhodesDoctor] 已知指令补发失败：{e.Message}");
      }
    }
  }

  /// <summary>
  /// C18后半：醉梦醒来（持有者受到生命值伤害时醒来并恍惚 1 轮）。
  /// 挂在睡眠增益上，监听 RuleDealDamage（目标是持有者且伤害&gt;0）：移除睡眠（本增益），挂恍惚 1 轮。
  /// 恍惚用原版 Daze 增益（d2e35b87…，剑雨/雷霆同款）。
  /// </summary>
  public class DrunkenDreamWake : UnitBuffComponentDelegate,
      ITargetRulebookHandler<RuleDealDamage>
  {
    public void OnEventAboutToTrigger(RuleDealDamage evt) { }
    public void OnEventDidTrigger(RuleDealDamage evt)
    {
      try
      {
        if (evt.Target == null || evt.Target != Owner) return;
        if (evt.Result <= 0) return;
        var context = Context ?? (Fact as Buff)?.Context;
        if (context == null) return;
        (Fact as Buff)?.Remove();
        if (BlueprintCore.Utils.BlueprintTool.TryGet<BlueprintBuff>(
          "d2e35b870e4ac574d9873b36402487e5", out var daze) && daze != null)
          Owner.Buffs.AddBuff(daze, context, System.TimeSpan.FromSeconds(6));
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 醉梦醒来失败：\n" + e);
      }
    }
  }

  /// <summary>只把"生效中"的增益算作存在（被压制的不算）。</summary>
  internal static class BuffHelper
  {
    internal static bool HasActive(UnitEntityData unit, BlueprintReferenceBase factRef)
    {
      if (unit == null || factRef == null) return false;
      foreach (var buff in unit.Buffs.RawFacts)
      {
        if (buff.Blueprint != null && buff.Blueprint.AssetGuid == factRef.Guid && !buff.IsSuppressed)
          return true;
      }
      return false;
    }

    /// <summary>是否为 20 级 Capstone「罗德岛的博士」应当压制的锁定/余波限制增益。</summary>
    internal static bool IsCapstoneSuppressedFact(BlueprintGuid guid)
    {
      var s = guid.ToString().Replace("-", "").ToLowerInvariant();
      return s == Guids.LockBuff.Replace("-", "").ToLowerInvariant()
        || s == Guids.LockBuffV2.Replace("-", "").ToLowerInvariant()
        || s == Guids.LockBuffV3.Replace("-", "").ToLowerInvariant()
        || s == Guids.AftermathRestrictBuff.Replace("-", "").ToLowerInvariant();
    }

    /// <summary>
    /// 恶灵锁定是否**实际生效**。与 <see cref="HasActive"/> 的区别：只要 20 级「罗德岛的博士」
    /// 增益（Capstone）在身上，就一律视为**未生效**。
    ///
    /// ⚠ 为什么不能只依赖「压制」：20 级本意是用 `SuppressBuffsFixed(applyToNewBuffs: true)` 压制一/二/三段锁定，
    /// 但实机取证（2026-10-11）表明**压制链路不可靠**——20 级时 Capstone 在身上、压制部件也在，
    /// 可三段锁定（`…d028`）仍是 `IsSuppressed=false`（作者实测"20 级开恶灵仍不能放法术"）。
    /// 该压制依赖 BlueprintCore 给 `Buff.OnAttach` 挂的 Harmony postfix（`IsSuppressedContinuously`），
    /// 与其它也改 `Buff.OnAttach` 的 mod / 载入顺序 / 增益重新挂载时机都有耦合。
    /// 这里改成**直接判 Capstone 在场**，绕开压制机制，结果确定、与载入顺序无关。
    /// </summary>
    internal static bool SpecterLockActive(UnitEntityData unit, BlueprintReferenceBase lockRef)
    {
      if (!HasActive(unit, lockRef)) return false;
      // Capstone（罗德岛的博士）在场 ⇒ 人灵合一 ⇒ 锁定一律不算。
      return !HasActive(unit, BlueprintCore.Utils.BlueprintTool.GetRef<BlueprintUnitFactReference>(Guids.CapstoneBuff));
    }
  }
}
