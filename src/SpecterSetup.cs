// 巴别塔的恶灵：资源、三阶段形态、锁定（限【恶灵】指令 + 穿透倒扣）、余波、20 级能力。
// 规则依据：rhodes-doctor-archetype.txt 修订第 6 版（法术书方案）。
//
// 结构（2.8.7 没有 Conditional/HasFact，放弃单能力开关）：
//   每个形态 = 一个"开启能力"（ApplyBuffPermanentFixed 上永久形态增益）
//   每个形态增益附带一个"结束恶灵"能力（自由动作，移除全部六个形态增益）
//   三个阶段各做一对，升级时用 RemoveFeatureOnApply 换新版，数值与动作类型全是静态的：
//     V1（8 级）：移动动作开启，转移量 = 智力调整值的一半
//     V2（12 级）：移动动作开启，转移量全额，敏捷形态加反射闪避，体质形态加伤害减免
//     V3（16 级）：迅捷动作开启，锁定换成"集火可用"的版本
using System.Collections.Generic;
using System.Linq;
using BlueprintCore.Actions.Builder;
using BlueprintCore.Blueprints.Components.Replacements;
using BlueprintCore.Blueprints.CustomConfigurators;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem.Stats;
using BlueprintCore.Actions.Builder.ContextEx;
using BlueprintCore.Blueprints.CustomConfigurators.Classes;
using BlueprintCore.Blueprints.CustomConfigurators.UnitLogic.Abilities;
using BlueprintCore.Blueprints.CustomConfigurators.UnitLogic.Buffs;
using BlueprintCore.Utils;
using BlueprintCore.Utils.Types;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Enums;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.Buffs.Components;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Components;
using RhodesDoctor.Components;

namespace RhodesDoctor
{
  internal static class SpecterSetup
  {
    /// <summary>
    /// 六个形态增益的 GUID（敏捷 / 体质 × 一 / 二 / 三段）。
    /// "同时只能有一个形态"（开启闸门）与"结束恶灵"（一次性删完）两处共用这一份名单，
    /// 别再各写一半 —— 旧版开启闸门只列本阶段那一对，导致升级换阶段时 V1 形态和 V2 形态也能共存。
    /// </summary>
    private static readonly string[] FormBuffGuids =
    {
      Guids.AgileBuffV1, Guids.EnduringBuffV1,
      Guids.AgileBuffV2, Guids.EnduringBuffV2,
      Guids.AgileBuffV3, Guids.EnduringBuffV3,
    };

    /// <summary>形态增益 GUID → "去横线小写"（自定义动作按字符串比对，见 ContextActionApplySpecterForm.Norm）。</summary>
    private static string[] FormBuffGuidKeys()
      => FormBuffGuids.Select(g => g.Replace("-", "").ToLowerInvariant()).ToArray();

    /// <summary>形态增益的事实引用（给 `AbilityCasterHasNoActiveFacts` / `AbilityCasterHasAnyActiveFact` 用）。</summary>
    internal static BlueprintUnitFactReference[] FormFactRefs()
      => FormBuffGuids.Select(g => BlueprintTool.GetRef<BlueprintUnitFactReference>(g)).ToArray();

    internal static void Create()
    {
      CreateResource();
      CreateLocks();
      CreateAftermath();
      CreateEndAbility();
      CreateStage(stage: 1, halved: true,  swift: false,
        Guids.AgileToggleV1, Guids.AgileBuffV1, Guids.EnduringToggleV1, Guids.EnduringBuffV1);
      CreateStage(stage: 2, halved: false, swift: false,
        Guids.AgileToggleV2, Guids.AgileBuffV2, Guids.EnduringToggleV2, Guids.EnduringBuffV2);
      CreateStage(stage: 3, halved: false, swift: true,
        Guids.AgileToggleV3, Guids.AgileBuffV3, Guids.EnduringToggleV3, Guids.EnduringBuffV3);
      CreateStageFeatures();
      CreateCapstone();
    }

    // ── 资源：每日 4 + 圣武士等级 + 智力调整值 轮 ──
    private static void CreateResource()
    {
      AbilityResourceConfigurator.New("RhodesDoctorSpecterRounds", Guids.SpecterResource)
        .SetLocalizedName(L10n.S("specter.resource.name", "恶灵轮数", "Specter Rounds"))
        .SetMaxAmount(ResourceAmountBuilder.New(4)
          .IncreaseByLevel(new[] { Guids.Vanilla.PaladinClass }, 1) // 每级 +1 轮
          .IncreaseByStat(StatType.Intelligence))
        .Configure();
    }

    // ── 锁定增益：开启恶灵时附加；限【恶灵】指令、压制指挥灵光 ──
    // Suppress 名单在 Lock / Capstone 里内联构造（静态字段初始化早于蓝图注入，不可靠）。
    // R2：一段（8–11）半额倒扣禁集火；二段（12–15）全额倒扣禁集火；三段（16+）全额倒扣允许集火。
    private static void CreateLocks()
    {
      // 一段：只能施放带【恶灵】标记的指令；源石技艺精通失效并按一半转移量倒扣穿透
      Lock(Guids.LockBuff, Guids.AuraCommandSelfBuff, Guids.TotalCommandSelfBuff);
      // 二段（R2 新增独立锁定）：全额倒扣，仍禁集火
      Lock(Guids.LockBuffV2, Guids.AuraCommandSelfBuff, Guids.TotalCommandSelfBuff);
      // 三段：16 级起集火可用（穿透倒扣按全额转移量，见 OriginiumMastery 组件）
      Lock(Guids.LockBuffV3, Guids.AuraCommandSelfBuff, Guids.TotalCommandSelfBuff);
    }

    private static void Lock(string guid, params string[] suppressedBuffs)
    {
      string suffix = guid == Guids.LockBuffV3 ? "V3" : guid == Guids.LockBuffV2 ? "V2" : "";
      // ⚠ 2026-10-11：用**持续压制**（`applyToNewBuffs: true`）而不是普通压制。
      // 普通压制（`ApplyToNewBuffs = false` → `AddNormalEntry`）**只压制"压制生效那一刻已经存在"的增益**，
      // 之后新挂上来的同名增益不受影响 —— 而指挥灵光/全军指令的载体增益是**特性授予的事实**，
      // 每次升级/特性重算都可能被重新挂一遍。要"恶灵生效期间灵光一直失效"就得用持续压制。
      var suppress = new SuppressBuffsFixed(applyToNewBuffs: true).ApplyToBuffs(
        suppressedBuffs.Select(g => (Blueprint<BlueprintBuffReference>)g).ToArray());
      BuffConfigurator.New($"RhodesDoctorSpecterLock{suffix}", guid)
        .SetDisplayName(L10n.S("specter.lock.name", "巴别塔的恶灵（锁定）", "Specter of Babel (Lock)"))
        // 图标：借原版"狂暴"（与本变体一/二/三段形态特性同款），此前该增益无图、状态栏显示空白。
        .SetIcon(Icons.Borrow(Guids.Vanilla.RageFeature))
        .SetDescription(L10n.S("specter.lock.desc",
          "恶灵生效期间，博士只能施放【恶灵】指令；源石技艺精通失效且法术穿透倒扣；指挥灵光与全军指令失效。",
          "While the Specter is active, only [Specter] orders can be cast; Originium Arts Mastery inverts to a penalty; the auras are suppressed."))
        // SuppressBuffsFixed：预先构建名单，用配置器的 AddSuppressBuffsFixed 挂载
        .AddSuppressBuffsFixed(suppress)
        // B19：施法限制改为挂在每个非【恶灵】指令上的 SpecterCasterRestriction（按钮变灰），
        // 这里不再用 RuleCastSpell.ForceFail。
        .Configure();
    }

    // ── 恶灵余波：2 轮只能移动 + 速度 +50 尺 ──
    private static void CreateAftermath()
    {
      BuffConfigurator.New("RhodesDoctorAftermathRestrict", Guids.AftermathRestrictBuff)
        .SetIcon(Icons.Get("specter/aftermath.png"))
        .SetDisplayName(L10n.S("specter.aftermath.name", "恶灵余波", "Specter's Aftermath"))
        .SetDescription(L10n.S("specter.aftermath.restrict.desc",
          "博士只能执行移动动作：无法攻击（包括借机攻击）、施法，或主动使用职业特性与物品。",
          "The Doctor can only take move actions: no attacks (including attacks of opportunity), spellcasting, or active use of abilities or items."))
        // 游戏原生的"恶心"状态恰好等于"只能执行移动动作"。
        // ⚠ 2026-10-11：不用裸 `AddCondition(Nauseated)` —— 20 级要靠压制解除，但压制链路实测不可靠。
        //   改用自定义组件 `AftermathRestriction`：自己判断 20 级 Capstone 在不在，在场就不施加。
        // 已知偏差：对恶心免疫的角色会跳过本限制（需要时改自定义组件）。
        .AddComponent<AftermathRestriction>()
        .Configure();

      BuffConfigurator.New("RhodesDoctorAftermathSpeed", Guids.AftermathSpeedBuff)
        .SetIcon(Icons.Get("specter/aftermath.png"))
        .SetDisplayName(L10n.S("specter.aftermath.name", "恶灵余波", "Specter's Aftermath"))
        .SetDescription(L10n.S("specter.aftermath.speed.desc",
          "博士的速度获得 +50 尺加值（不具类型，可叠加）。",
          "The Doctor gains a +50-foot untyped bonus to speed."))
        .AddStatBonus(descriptor: ModifierDescriptor.UntypedStackable, stat: StatType.Speed, value: 50)
        .Configure();
    }

    // ── 结束恶灵：自由动作，移除全部六个形态增益（余波由增益的 deactivated 触发）──
    // ⚠ 2026-10-11：原来把六个 `RemoveBuff` 排成动作队列，作者实测"点一次只取消一个形态、另一个还在"
    //   —— 因为"结束恶灵"能力本身是形态增益用 AddFacts 授予的，删掉那个增益的瞬间能力就被销毁、
    //   后面的动作不再执行。改成**一个自定义动作**在同一帧内一次性删完（见 ContextActionEndSpecterForms）。
    private static void CreateEndAbility()
    {
      var act = ActionsBuilder.New()
        .Add<ContextActionEndSpecterForms>(a => a.FormBuffGuids = FormBuffGuidKeys());

      var endAb = AbilityConfigurator.New("RhodesDoctorEndSpecter", Guids.EndSpecterAbility)
        .SetDisplayName(L10n.S("specter.end.name", "结束恶灵", "End the Specter"))
        .SetDescription(L10n.S("specter.end.desc",
          "立刻结束巴别塔的恶灵，进入 2 轮恶灵余波。",
          "End the Specter of Babel immediately and suffer its Aftermath for 2 rounds."))
        .SetActionType(UnitCommand.CommandType.Free)
        .SetRange(AbilityRange.Personal)
        .SetCanTargetSelf(true)
        .AddAbilityEffectRunAction(act);
      Icons.ApplySpecterIcon(endAb, "aftermath");
      endAb.Configure();
    }

    // ── 一个阶段：一对开启能力 + 一对形态增益 ──
    // R2：一段用 LockBuff，一段后二段用独立 LockBuffV2，三段用 LockBuffV3。
    private static void CreateStage(int stage, bool halved, bool swift,
      string agileAbilityGuid, string agileBuffGuid,
      string enduringAbilityGuid, string enduringBuffGuid)
    {
      string lockGuid = stage <= 1 ? Guids.LockBuff : stage == 2 ? Guids.LockBuffV2 : Guids.LockBuffV3;
      CreateFormBuff("Agile", stage, halved, agileBuffGuid, lockGuid, isAgile: true);
      CreateFormBuff("Enduring", stage, halved, enduringBuffGuid, lockGuid, isAgile: false);
      CreateActivator("Agile", stage, swift, agileAbilityGuid, agileBuffGuid);
      CreateActivator("Enduring", stage, swift, enduringAbilityGuid, enduringBuffGuid);
    }

    // 本地化键全部字面量（抓取器不展开拼接键）。
    private static void CreateFormBuff(string key, int stage, bool halved,
      string guid, string lockGuid, bool isAgile)
    {

      // 形态增益附带：结束恶灵能力 + 锁定
      var facts = new List<Blueprint<BlueprintUnitFactReference>>
      {
        Guids.EndSpecterAbility,
        lockGuid,
      };
      if (isAgile && stage >= 2)
        facts.Add(Guids.Vanilla.EvasionFeature);

      var cfg = BuffConfigurator.New($"RhodesDoctor{key}FormV{stage}", guid)
        .SetIcon(Icons.Get(isAgile ? "specter/agile-form.png" : "specter/enduring-form.png"))
        .SetDisplayName(isAgile
          ? L10n.S("specter.form.agile.name", "巴别塔的恶灵：敏捷形态", "Specter of Babel: Agile Form")
          : L10n.S("specter.form.enduring.name", "巴别塔的恶灵：体质形态", "Specter of Babel: Enduring Form"))
        .SetDescription(isAgile
          ? L10n.S("specter.form.agile.desc", "敏捷值获得等于转移量两倍的加值；近战攻击与伤害用敏捷代替力量（取较高者）。", "Bonus to Dexterity equal to twice the Transfer Value; melee attack and damage rolls use Dexterity instead of Strength (whichever is higher).")
          : L10n.S("specter.form.enduring.desc", "体质值获得等于转移量两倍的加值，生命值与强韧豁免随之提高。", "Bonus to Constitution equal to twice the Transfer Value; hit points and Fortitude saves increase accordingly."))
        .AddContextStatBonus(
          isAgile ? StatType.Dexterity : StatType.Constitution,
          ContextValues.Rank(),
          ModifierDescriptor.UntypedStackable)
        .AddContextRankConfig(TransferRank(halved))
        // 激活后每轮消耗 1 轮恶灵；不足时自动结束
        .AddFactContextActions(newRound: ActionsBuilder.New()
          .Add<ContextActionSpendResourceOrEndBuff>(a =>
            a.Resource = BlueprintTool.GetRef<BlueprintAbilityResourceReference>(Guids.SpecterResource)))
        // 以任何方式结束 → 恶灵余波
        .AddFactContextActions(deactivated: ActionsBuilder.New()
          .ApplyBuff(BlueprintTool.GetRef<BlueprintBuffReference>(Guids.AftermathRestrictBuff), ContextDuration.Fixed(2))
          .ApplyBuff(BlueprintTool.GetRef<BlueprintBuffReference>(Guids.AftermathSpeedBuff), ContextDuration.Fixed(2)));
        // AddFacts 走配置器方法（组件的 m_Facts 字段名不可靠）
        cfg.AddFacts(facts);

      if (isAgile)
      {
        cfg.AddComponent<DexToMeleeAttackDamage>();
        // 护甲最大敏捷加值 + 转移量（B10：旧版复用"属性加值"档 = 转移量×2，多乘了 2）。
        // 这里单独用 StatBonus 档提供"转移量"原值。
        cfg.AddContextRankConfig(TransferRankRaw(halved, 0, AbilityRankType.StatBonus));
        cfg.AddComponent<MaxDexBonusIncrease>(c =>
        {
          c.UseContextInstead = true;
          c.ContextBonus = ContextValues.Rank(AbilityRankType.StatBonus);
        });
      }
      else if (stage >= 2)
      {
        cfg.AddComponent<StackingDamageReduction>(); // 伤害减免 3/—，可叠加
      }

      cfg.Configure();
    }

    private static void CreateActivator(string key, int stage, bool swift,
      string abilityGuid, string buffGuid)
    {
      bool isAgile = key == "Agile";
      var ab = AbilityConfigurator.New($"RhodesDoctor{key}ActivateV{stage}", abilityGuid)
        .SetDisplayName(isAgile
          ? L10n.S("specter.form.agile.name", "巴别塔的恶灵：敏捷形态", "Specter of Babel: Agile Form")
          : L10n.S("specter.form.enduring.name", "巴别塔的恶灵：体质形态", "Specter of Babel: Enduring Form"))
        .SetDescription(isAgile
          ? L10n.S("specter.toggle.agile.desc", "唤醒巴别塔的恶灵。结束后进入恶灵余波；可用\"结束恶灵\"提前结束。", "Awaken the Specter of Babel. The Specter's Aftermath follows when it ends; use End the Specter to end it early.")
          : L10n.S("specter.toggle.enduring.desc", "唤醒巴别塔的恶灵。结束后进入恶灵余波；可用\"结束恶灵\"提前结束。", "Awaken the Specter of Babel. The Specter's Aftermath follows when it ends; use End the Specter to end it early."))
        .SetActionType(swift ? UnitCommand.CommandType.Swift : UnitCommand.CommandType.Move)
        .SetRange(AbilityRange.Personal)
        .SetCanTargetSelf(true)
        // 至少剩 1 轮才能开启（开启本身不扣，之后每轮由增益扣）
        .AddAbilityResourceLogic(amount: 1, isSpendResource: false,
          requiredResource: BlueprintTool.GetRef<BlueprintAbilityResourceReference>(Guids.SpecterResource))
        // V10：**任一形态**生效中、或余波限制期间都不能开启。
        // （重复开同一形态会 Replace 旧增益、触发旧增益的结束动作=余波，人还在恶灵里被"恶心"。
        //   被压制的增益不算，20 级可立即重开。）
        // ⚠ 2026-10-11：名单从"本阶段那一对"改成**全部六个形态增益** —— 旧写法在"升到 12/16 级换阶段"
        //   时会漏（V1 形态还在、V2 的闸门只认 V2 增益，于是两个形态共存）。这只是**按钮置灰**用的 UI 闸门，
        //   真正保证"同时只有一个形态"的是动作里的硬互斥 `ContextActionApplySpecterForm`。
        .AddComponent<AbilityCasterHasNoActiveFacts>(c => c.Facts = FormFactRefs()
          .Concat(new[] { BlueprintTool.GetRef<BlueprintUnitFactReference>(Guids.AftermathRestrictBuff) })
          .ToArray())
        // 硬互斥：动作执行时再判一次"身上有没有形态增益"，有就什么都不做（不依赖上面的 UI 闸门）。
        .AddAbilityEffectRunAction(ActionsBuilder.New()
          .Add<ContextActionApplySpecterForm>(a =>
          {
            a.FormBuff = BlueprintTool.GetRef<BlueprintBuffReference>(buffGuid);
            a.FormBuffGuids = FormBuffGuidKeys();
            // 余波期间不能重开恶灵（原来只写在 UI 闸门的名单里，这里也硬拦一道）。
            a.BlockedBuffGuids = new[] { Guids.AftermathRestrictBuff.Replace("-", "").ToLowerInvariant() };
          }));
      Icons.ApplySpecterIcon(ab, $"{key.ToLowerInvariant()}-form");
      ab.Configure();
    }

    // ── 阶段特性（8/12/16 级）：换发对应版本的开启能力 ──
    private static void CreateStageFeatures()
    {
      Holder(Guids.TogglesV1Holder, 1, Guids.AgileToggleV1, Guids.EnduringToggleV1);
      Holder(Guids.TogglesV2Holder, 2, Guids.AgileToggleV2, Guids.EnduringToggleV2);
      Holder(Guids.TogglesV3Holder, 3, Guids.AgileToggleV3, Guids.EnduringToggleV3);

      // 三段特性原本没有图标（游戏里显示为默认空白框）：借原版"狂暴"的图——语义上都是
      // "把智慧化成战斗本能"的爆发形态；换图只改 Guids.Vanilla.RageFeature 一处。
      FeatureConfigurator.New("RhodesDoctorSpecterStage1", Guids.Stage1Feature)
        .SetIcon(Icons.Borrow(Guids.Vanilla.RageFeature))
        .SetDisplayName(L10n.S("specter.name", "巴别塔的恶灵", "Specter of Babel"))
        .SetDescription(L10n.S("specter.stage1.desc",
          "唤醒沉睡的另一个自己：开启时选择敏捷或体质形态，将一半的智力调整值转移为战斗本能。每日可维持 4 + 圣武士等级 + 智力调整值 轮。结束后进入 2 轮恶灵余波。此能力替换决意灵光。",
          "Awaken the other self within: choose the Agile or Enduring Form, transferring half your Intelligence modifier into pure fighting instinct. Usable for 4 + paladin level + Intelligence modifier rounds per day. The Specter's Aftermath follows. This ability replaces aura of resolve."))
        .AddAbilityResources(resource: Guids.SpecterResource, restoreAmount: true)
        // ⚠ 2026-10-11：`doNotRestoreMissingFacts: true` —— 否则**读档时** AddFacts 的 post-load 修复
        // 会把已被 `RemoveFeatureOnApply` 删掉的旧开关承载**重新补回来**（快捷栏里长成"三套形态"）。
        .AddFacts(new List<Blueprint<BlueprintUnitFactReference>> { Guids.TogglesV1Holder },
          doNotRestoreMissingFacts: true)
        .Configure();

      FeatureConfigurator.New("RhodesDoctorSpecterStage2", Guids.Stage2Feature)
        .SetIcon(Icons.Borrow(Guids.Vanilla.RageFeature))
        .SetDisplayName(L10n.S("specter.stage2.name", "巴别塔的恶灵（二段）", "Specter of Babel (Stage II)"))
        .SetDescription(L10n.S("specter.stage2.desc",
          "转移量提高为全部智力调整值。敏捷形态获得反射闪避，体质形态获得伤害减免 3/—（可与其他来源叠加）。",
          "The Transfer Value becomes the full Intelligence modifier. Agile Form gains evasion; Enduring Form gains DR 3/— (stacking)."))
        .AddRemoveFeatureOnApply(Guids.TogglesV1Holder)
        .AddFacts(new List<Blueprint<BlueprintUnitFactReference>> { Guids.TogglesV2Holder },
          doNotRestoreMissingFacts: true)
        // 读档再收敛一次：把一段的旧开关承载清掉（对"旧存档里已经存在"的也有效）。
        .AddComponent<EnforceSpecterStage>(c => c.StaleHolders = new[]
        {
          BlueprintTool.GetRef<BlueprintUnitFactReference>(Guids.TogglesV1Holder),
        })
        .Configure();

      FeatureConfigurator.New("RhodesDoctorSpecterStage3", Guids.Stage3Feature)
        .SetIcon(Icons.Borrow(Guids.Vanilla.RageFeature))
        .SetDisplayName(L10n.S("specter.stage3.name", "巴别塔的恶灵（三段）", "Specter of Babel (Stage III)"))
        .SetDescription(L10n.S("specter.stage3.desc",
          "开启恶灵改为迅捷动作；恶灵生效期间可以下达集火。",
          "Activating the Specter becomes a swift action; Focus Fire can be issued while it is active."))
        .AddRemoveFeatureOnApply(Guids.TogglesV2Holder)
        .AddFacts(new List<Blueprint<BlueprintUnitFactReference>> { Guids.TogglesV3Holder })
        // 读档收敛：三段在场时，一段 / 二段的旧开关承载都该被清掉。
        .AddComponent<EnforceSpecterStage>(c => c.StaleHolders = new[]
        {
          BlueprintTool.GetRef<BlueprintUnitFactReference>(Guids.TogglesV1Holder),
          BlueprintTool.GetRef<BlueprintUnitFactReference>(Guids.TogglesV2Holder),
        })
        .Configure();
    }

    private static void Holder(string guid, int stage, string agileToggle, string enduringToggle)
    {
      var (name, desc) = stage switch
      {
        1 => (L10n.S("specter.holder1.name", "巴别塔的恶灵（开关）", "Specter of Babel (Toggles)"),
              L10n.S("specter.holder1.desc", "恶灵形态开关。", "Specter form toggles.")),
        2 => (L10n.S("specter.holder2.name", "巴别塔的恶灵（开关）", "Specter of Babel (Toggles)"),
              L10n.S("specter.holder2.desc", "恶灵形态开关。", "Specter form toggles.")),
        _ => (L10n.S("specter.holder3.name", "巴别塔的恶灵（开关）", "Specter of Babel (Toggles)"),
              L10n.S("specter.holder3.desc", "恶灵形态开关。", "Specter form toggles.")),
      };
      FeatureConfigurator.New($"RhodesDoctorTogglesV{stage}", guid)
        .SetDisplayName(name)
        .SetDescription(desc)
        .SetHideInUI(true)
        .AddFacts(new List<Blueprint<BlueprintUnitFactReference>> { agileToggle, enduringToggle })
        .Configure();
    }

    // ── 20 级：罗德岛的博士 ──
    private static void CreateCapstone()
    {
      // 压制一/二/三段锁定与余波限制 → 恶灵期间可用全部指令、灵光生效、冷静头脑全额、
      // 结束后无行动限制且可立即重开；余波的 +50 尺速度保留。
      //
      // ⚠⚠ 2026-10-11 修：这里原来用的是**普通压制**（`new SuppressBuffsFixed()` → `AddNormalEntry`），
      // 而普通压制**只压制"它生效的那一刻已经存在"的增益**、并把那份名单记下来；
      // 20 级升级时身上并没有锁定（恶灵没开），所以记下来的是**空名单** ⇒
      // 之后每次开恶灵新生成的锁定增益**根本不在压制范围里** ⇒
      // 表现就是作者实测的"**20 级开了恶灵反而不能施法**"（锁定生效 → 非【恶灵】指令被拦）、
      // 以及"结束后仍被余波恶心""穿透倒扣""灵光失效"。
      // 改成**持续压制**（`applyToNewBuffs: true`，文档原话"effects added after suppression is applied are affected"）
      // 之后，锁定/余波一挂上就被压制，20 级的"人灵合一"才真正成立。
      // 顺带把一段锁定 `LockBuff` 也列进来（旧存档里可能残着 8–11 级开的形态）。
      BuffConfigurator.New("RhodesDoctorCapstoneBuff", Guids.CapstoneBuff)
        .SetDisplayName(L10n.S("capstone.buff.name", "罗德岛的博士", "Doctor of Rhodes Island"))
        // 图标：与 20 级特性同一张（`specter/doctor-capstone.png`）——此前该增益无图、状态栏显示空白。
        .SetIcon(Icons.Get("specter/doctor-capstone.png"))
        .SetDescription(L10n.S("capstone.buff.desc", "博士与恶灵合二为一。", "The Doctor and the Specter are one."))
        .AddSuppressBuffsFixed(new SuppressBuffsFixed(applyToNewBuffs: true)
          .ApplyToBuffs(Guids.LockBuff, Guids.LockBuffV2, Guids.LockBuffV3, Guids.AftermathRestrictBuff))
        .Configure();

      var capstone = FeatureConfigurator.New("RhodesDoctorCapstone", Guids.CapstoneFeature)
        .SetDisplayName(L10n.S("capstone.name", "罗德岛的博士", "Doctor of Rhodes Island"))
        .SetDescription(L10n.S("capstone.desc",
          "博士不再需要在指挥官与恶灵之间做出选择：恶灵生效期间可下达所有指令，冷静头脑与灵光完整生效；恶灵结束后不受余波限制并可立即再次开启，但仍获得 +50 尺速度。此能力替换神圣斗士。",
          "The Doctor no longer has to choose: all orders remain available during the Specter, Cool Head and auras function at full strength; the Aftermath imposes no restriction and the Specter can be reactivated immediately, though the +50-foot speed remains. This ability replaces holy champion."))
        .AddFacts(new List<Blueprint<BlueprintUnitFactReference>> { Guids.CapstoneBuff });
      Icons.ApplyFeatureIcon(capstone, "doctor-capstone");
      capstone.Configure();
    }

    /// <summary>
    /// 转移量 → 属性加值（= 转移量 × 2 + shift；旧日用 shift=4 表转移量 +2）。
    /// VERIFY：ContextRankBaseValueType.StatBonus 给出的是"调整值"还是"属性值"——
    /// 若给的是属性值（如 24 而非 +7），需先把属性值换算成调整值再进本表。
    /// </summary>
    internal static ContextRankConfig TransferRank(bool halved, int shift = 0, AbilityRankType type = AbilityRankType.Default)
      => ContextRankConfigs.StatBonus(StatType.Intelligence, type: type)
        .WithCustomProgression(Enumerable.Range(0, 41)
          .Select(r => (r, (halved ? 2 * (r / 2) : 2 * r) + shift)).ToArray());

    /// <summary>转移量原值（B10：护甲最大敏捷加值用它，而不是属性加值档的 ×2）。</summary>
    internal static ContextRankConfig TransferRankRaw(bool halved, int shift = 0, AbilityRankType type = AbilityRankType.Default)
      => ContextRankConfigs.StatBonus(StatType.Intelligence, type: type)
        .WithCustomProgression(Enumerable.Range(0, 41)
          .Select(r => (r, (halved ? r / 2 : r) + shift)).ToArray());
  }
}
