// 召唤物与镜像：墨灵单位蓝图、墨灵自爆增益、镜像行为锁增益。
// 镜中流形的复制动作本体在 Actions.cs（ContextActionSpawnMirrorCopy），
// 法术侧接线在 SpellsSetup.ApplySpecial 的 InkedMountains / MirrorFlow 两个分支。
//
// 原版引用已从游戏 blueprints.zip 核实（对照小型气元素被召单位）。
// 按协作规则不留静默空串：InklingPrefab / NaturalWeapon 为空时跳过相关配置并记日志，
// 镜像行为锁增益不依赖原版蓝图，照常创建。
using System;
using BlueprintCore.Blueprints.Configurators;
using BlueprintCore.Blueprints.CustomConfigurators.UnitLogic.Buffs;
using BlueprintCore.Utils;
using Kingmaker.Blueprints;
using Kingmaker.Enums;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.Utility;
using RhodesDoctor.Components;

namespace RhodesDoctor
{
  internal static class SummonsSetup
  {
    /// <summary>原版引用（已从游戏 blueprints.zip 核实，对照小型气元素被召单位）。
    /// TestSetup 复用其中模型/类型/Brain（internal）。</summary>
    internal static class Refs
    {
      // 气元素生物类型（Units/Types/ElementalAir.jbp）。注意：元素单位的 m_Race 为空，
      // 种族包走 m_Type，所以下面用 SetType 而不是 SetRace。
      internal const string ElementalType   = "16f736b21b43e12449012844ea5f9ca1";
      // 召唤物阵营（Factions/Summoned.jbp；野生气元素用的是另一个阵营，不要混）。
      internal const string SummonFaction   = "1b08d9ed04518ec46a9b3e4e23cb5105";
      // 气元素 AI（AirElementalBrain.jbp；被召/野生共用同一个）。
      internal const string SummonBrain     = "69c3bc09d43b4c34a9e79e6e88c71c2a";
      // 小型气元素模型（原版小型气元素外观，墨灵换皮起点）。
      internal const string InklingPrefab   = "4dfff781b20ddea4d9dfe78fff0bfb4d";
      // 原版小型气元素被召单位（SummonedAirElementalSmall.jbp）。手搓单位照抄它的
      // Visual / Body（含血液类型、脚印、血渍/碎裂特效、空手武器等），否则这些字段为 null，
      // 被打中触发血迹特效或读 Bark 时会 NRE。
      internal const string SmallAirElemental = "04944455200bc224d955a8e9bbd64f3f";
      internal const string NaturalWeapon   = ""; // VERIFY：墨灵天生武器特性（空则改用制式武器走 Body 槽）
    }

    internal static void Create()
    {
      CreateMirrorLockBuff();
      CreateInklingDeathBurstBuff();
      CreateInklingUnit();
    }

    // ── 镜像行为锁：只能攻击、移动与使用普通物品 ──
    // 依据 AbilityData.CanBeCastByCaster 反编译：Spell/SpL + SpellcastingForbidden → false；
    // Ex/Su + ExAndSuAbilitiesForbidden → false；ForbidMagicItems 连魔杖卷轴一起禁。
    private static void CreateMirrorLockBuff()
    {
      BuffConfigurator.New("RhodesDoctorMirrorLock", Guids.MirrorLockBuff)
        .SetDisplayName(L10n.S("mirrorlock.name", "镜像限制", "Mirror Restriction"))
        .SetDescription(L10n.S("mirrorlock.desc",
          "镜像复制体只能攻击、移动与使用普通物品，不能施法或使用职业能力。",
          "The mirror duplicate can only attack, move, and use ordinary items — no spells or abilities."))
        .AddComponent<ForbidSpellCasting>(c => c.ForbidMagicItems = true)
        // ExAndSuAbilitiesForbidden 由反编译确认，与 ForbidSpellCasting 配合禁用主动能力。
        .AddComponent<AddCondition>(c => c.Condition = UnitCondition.ExAndSuAbilitiesForbidden)
        .Configure();
    }

    // ── 墨灵自爆增益：死亡或消散时对相邻敌人爆发 ──
    private static void CreateInklingDeathBurstBuff()
    {
      BuffConfigurator.New("RhodesDoctorInklingDeathBurst", Guids.InklingDeathBurstBuff)
        .SetDisplayName(L10n.S("inkling.deathburst.name", "墨灵爆发", "Ink Burst"))
        .SetDescription(L10n.S("inkling.deathburst.desc",
          "墨灵被摧毁时在相邻敌人处爆发：2d6 力场伤害并目眩 1 轮（意志无效）。",
          "When destroyed, the ink spirit bursts: 2d6 force damage to an adjacent enemy and dazzled 1 round (Will negates)."))
        .AddComponent<InklingDeathBurst>()
        .Configure();
    }

    // ── 墨灵单位蓝图（手搓，结构对照原版小型气元素被召单位）──
    private static void CreateInklingUnit()
    {
      if (string.IsNullOrEmpty(Refs.ElementalType)
          || string.IsNullOrEmpty(Refs.SummonFaction)
          || string.IsNullOrEmpty(Refs.SummonBrain)
          || string.IsNullOrEmpty(Refs.InklingPrefab))
      {
        Main.Mod.Logger.Log(
          "[RhodesDoctor] 墨灵单位跳过：Refs 里的原版引用未填实（生物类型/阵营/AI），"
          + "墨染山河的召唤动作会按 GUID 找不到单位。填实后本提示消失。");
        // 注意：职业等级（HD）按平均生命写死在 MaxHP，攻击用博士 BAB 与 2d6 力场化的机制另做。
        return;
      }

      // UnitConfigurator 已确认的方法：SetType/SetSize/SetAlignment/SetStrength…/SetFaction/SetBrain/SetPrefab/SetAddFacts/SetMaxHP。
      // HD 自写：UnitConfigurator 没有 AddClassLevels，2 HD（体质 18 → +4/HD）按平均生命 19 写死。
      UnitConfigurator.New("RhodesDoctorInkling", Guids.InklingUnit)
        .SetDisplayName(L10n.S("inkling.name", "墨灵", "Ink Spirit"))
        .SetType(Refs.ElementalType)
        .SetPrefab(Refs.InklingPrefab)
        .SetSpeed(new Feet(40))
        // 属性照抄同环召唤物（SummonMonsterVSingle 的小型红帽：力18/敏19/体18）；
        // 心智保留构装体 flavor（智2/感11/魅10），不抄红帽的高心智。
        // 生命 2d10+8（体质 18 → +4/HD）取平均 19。
        .SetMaxHP(19)
        .SetSize(Size.Small)
        .SetAlignment(Alignment.TrueNeutral)
        .SetStrength(18).SetDexterity(19).SetConstitution(18)
        .SetIntelligence(2).SetWisdom(11).SetCharisma(10)
        .SetFaction(Refs.SummonFaction)
        .SetBrain(Refs.SummonBrain)
        // Skills 是类，不赋值即 null；UnitDescriptor.SetSkills 会解引用它，缺了会在生成时抛 NRE。
        // FactionOverrides 同理（BlueprintUnit.AttackFactions 会解引用它）。
        .SetSkills(new Kingmaker.Blueprints.BlueprintUnit.UnitSkills())
        .SetFactionOverrides(new Kingmaker.Blueprints.FactionOverrides())
        .AddComponent<InklingFightsWithMasterBAB>() // 以主人 BAB 攻击 + 2d6 力场（自写组件）
        // 种族特性 + 战斗专长：原来这里是**空的**（作者 10-11："没有任何特性和专长"太弱），
        // 现补上 `InklingFacts`（元素子类 / 绊摔免疫 / 武器娴熟 / 精通先攻 / 精通徒手 / 天生护甲 +3）。
        .SetAddFacts(BuildInklingFacts())
        .Configure();

      CopyTemplateVisuals(Guids.InklingUnit, "墨灵");
    }

    /// <summary>
    /// 墨灵的"种族特性 + 战斗专长"包（作者 10-11：原来墨灵**没有任何特性与专长**、太弱，
    /// "加一些让它更符合强度"）。参照原版**小型气元素**被召单位的 `m_AddFacts` 逐条挑，
    /// 去掉气元素专属（旋风 / 空中 / 气之精通 / 气系子类），保留通用且契合"墨灵（墨兽）"的：
    ///   · 元素子类（`SubtypeElemental`）——免疫流血/麻痹/毒素/睡眠/震慑，不受重击与夹击；
    ///   · 异界子类（`SubtypeExtraplanar`）；
    ///   · 绊摔免疫（`TripImmune`）；
    ///   · 武器娴熟（`WeaponFinesse`，敏 19 &gt; 力 18，攻击用敏）；
    ///   · 精通先攻（`Improved Initiative`）；
    ///   · 精通徒手打击（`ImprovedUnarmedStrike`，徒手也致命、不引发借机）；
    ///   · 天生护甲 +3（`NaturalArmor3`）。
    /// 想加强 / 减弱就改这一张表（GUID 均已在 `blueprints.zip` 核实）。
    /// </summary>
    private static readonly string[] InklingFacts =
    {
      "198fd8924dabcb5478d0f78bd453c586", // CreatureAbilities/Subtypes/SubtypeElemental
      "136fa0343d5b4b348bdaa05d83408db3", // CreatureAbilities/Subtypes/SubtypeExtraplanar
      "c1b26f97b974aec469613f968439e7bb", // Feats/Maneuvers/Trip/TripImmune
      "90e54424d682d104ab36436bd527af09", // Feats/WeaponFinesse
      "797f25d709f559546b29e7bcb181cc74", // Feats/Improved Initiative
      "7812ad3672a4b9a4fb894ea402095167", // Feats/ImprovedUnarmedStrike
      "f6e106931f95fec4eb995f0d0629fb84", // CreatureAbilities/NaturalArmor/NaturalArmor3
    };

    private static Blueprint<BlueprintUnitFactReference>[] BuildInklingFacts()
    {
      var list = new System.Collections.Generic.List<Blueprint<BlueprintUnitFactReference>>();
      // 天生武器（`Refs.NaturalWeapon`，当前为空、走 Body 槽）；填实后一并挂上。
      if (!string.IsNullOrEmpty(Refs.NaturalWeapon)) list.Add(Refs.NaturalWeapon);
      foreach (var g in InklingFacts) list.Add(g);
      return list.ToArray();
    }

    /// <summary>
    /// 把原版小型气元素的 Visual / Body 照抄到我们的手搓单位上。
    /// 这两个字段是类、默认 null，而游戏会在被打中（血迹/碎裂特效）、读 Bark、或预加载资源时解引用它们；
    /// 不抄会在这些时机 NRE。原版模板取不到就记日志跳过（不致命，但被打中可能报错）。
    /// </summary>
    internal static void CopyTemplateVisuals(string targetGuid, string tag)
    {
      var template = BlueprintTool.Get<BlueprintUnit>(Refs.SmallAirElemental);
      if (template == null)
      {
        Dbg.Log($"[RhodesDoctor] {tag}：找不到原版小型气元素模板，Visual/Body 未照抄。");
        return;
      }
      var unit = BlueprintTool.Get<BlueprintUnit>(targetGuid);
      if (unit == null) return;
      // 直接照抄模板（Body 默认是非空的 new UnitBody()，也要覆盖成气元素的那份，
      // 否则空手/手部设置与原版不一致）。
      unit.Visual = template.Visual;
      unit.Body = template.Body;
    }
  }
}
