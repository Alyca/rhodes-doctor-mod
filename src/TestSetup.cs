// UMM 测试面板用的木桩：敌方单位，不攻击，可自带若干永久增益。
// 用途：进游戏实测 64 个指令（伤害 / 豁免 / 驱散 / 偷取增益等），不用到处找怪。
//
// 阵营自建（EnemyForEveryone），不依赖任何原版阵营 GUID；
// 模型 / 生物类型 / Brain 沿用墨灵已验证的原版引用（SummonsSetup.Refs）。
// 不攻击靠刷出时 IsAIEnabled = false（TestPanel 里关，不是蓝图行为）。
// 所有测试增益都标 IsFromSpell：驱散（Dispel-All）与王冠回响的偷取都能覆盖到。
using System;
using System.Linq;
using BlueprintCore.Blueprints.Configurators;
using BlueprintCore.Blueprints.CustomConfigurators.UnitLogic.Buffs;
using BlueprintCore.Utils;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.Utility;
using RhodesDoctor.Components;

namespace RhodesDoctor
{
  internal static class TestSetup
  {
    internal static void Create()
    {
      FactionConfigurator.New("RhodesDoctorTestFaction", Guids.TestFaction)
        .SetEnemyForEveryone(true)
        .Configure();

      BuffConfigurator.New("RhodesDoctorTestBuffArmor", Guids.TestBuffArmor)
        .SetDisplayName(L10n.S("testdummy.buff.armor.name", "测试增益·护甲", "Test Buff: Armor"))
        .SetDescription(L10n.S("testdummy.buff.armor.desc",
          "测试木桩自带：AC 获得 +4 天生护甲加值。可被驱散与偷取。",
          "Test dummy buff: +4 natural armor to AC. Can be dispelled and stolen."))
        .AddStatBonus(descriptor: ModifierDescriptor.NaturalArmor, stat: StatType.AC, value: 4)
        .Configure();

      BuffConfigurator.New("RhodesDoctorTestBuffResist", Guids.TestBuffResist)
        .SetDisplayName(L10n.S("testdummy.buff.resist.name", "测试增益·抗力", "Test Buff: Resistance"))
        .SetDescription(L10n.S("testdummy.buff.resist.desc",
          "测试木桩自带：火、寒、电、酸、音波抗力各 10 点（可与其他来源叠加）。可被驱散与偷取。",
          "Test dummy buff: resistance 10 to fire, cold, electricity, acid and sonic (stacking). Can be dispelled and stolen."))
        .AddComponent<StackingEnergyResistance>(c => c.Value = 10)
        .Configure();

      // ── 6 个"常见增益"克隆（供王冠的回响偷取测试；都会标 IsFromSpell）──
      CreateTestBuff(Guids.TestBuffHaste, "haste", "测试增益·加速", "Test Buff: Haste",
        "测试木桩自带：AC 与反射 +1（闪避），攻击 +1（不具类型）——加速术的近似。可被驱散与偷取。",
        "Test dummy buff: +1 dodge AC and Reflex, +1 untyped attack (haste-like). Can be dispelled and stolen.",
        cfg =>
        {
          cfg.AddStatBonus(descriptor: ModifierDescriptor.Dodge, stat: StatType.AC, value: 1);
          cfg.AddStatBonus(descriptor: ModifierDescriptor.Dodge, stat: StatType.SaveReflex, value: 1);
          cfg.AddStatBonus(descriptor: ModifierDescriptor.UntypedStackable, stat: StatType.AdditionalAttackBonus, value: 1);
        });

      CreateTestBuff(Guids.TestBuffBless, "bless", "测试增益·祝福", "Test Buff: Bless",
        "测试木桩自带：攻击 +1（士气）——祝福术的近似。可被驱散与偷取。",
        "Test dummy buff: +1 morale attack (bless-like). Can be dispelled and stolen.",
        cfg => cfg.AddStatBonus(descriptor: ModifierDescriptor.Morale, stat: StatType.AdditionalAttackBonus, value: 1));

      CreateTestBuff(Guids.TestBuffMageArmor, "magearmor", "测试增益·法师护甲", "Test Buff: Mage Armor",
        "测试木桩自带：AC +4（护甲）——法师护甲的近似。可被驱散与偷取。",
        "Test dummy buff: +4 armor AC (mage armor-like). Can be dispelled and stolen.",
        cfg => cfg.AddStatBonus(descriptor: ModifierDescriptor.Armor, stat: StatType.AC, value: 4));

      CreateTestBuff(Guids.TestBuffShield, "shield", "测试增益·护盾", "Test Buff: Shield",
        "测试木桩自带：AC +4（盾牌）——护盾术的近似。可被驱散与偷取。",
        "Test dummy buff: +4 shield AC (shield-like). Can be dispelled and stolen.",
        cfg => cfg.AddStatBonus(descriptor: ModifierDescriptor.Shield, stat: StatType.AC, value: 4));

      CreateTestBuff(Guids.TestBuffHeroism, "heroism", "测试增益·英雄气概", "Test Buff: Heroism",
        "测试木桩自带：攻击与三项豁免 +2（士气）——英雄气概的近似。可被驱散与偷取。",
        "Test dummy buff: +2 morale on attacks and all saves (heroism-like). Can be dispelled and stolen.",
        cfg =>
        {
          cfg.AddStatBonus(descriptor: ModifierDescriptor.Morale, stat: StatType.AdditionalAttackBonus, value: 2);
          cfg.AddStatBonus(descriptor: ModifierDescriptor.Morale, stat: StatType.SaveFortitude, value: 2);
          cfg.AddStatBonus(descriptor: ModifierDescriptor.Morale, stat: StatType.SaveReflex, value: 2);
          cfg.AddStatBonus(descriptor: ModifierDescriptor.Morale, stat: StatType.SaveWill, value: 2);
        });

      CreateTestBuff(Guids.TestBuffBearEndurance, "bearendurance", "测试增益·熊之坚韧", "Test Buff: Bear's Endurance",
        "测试木桩自带：体质 +4（增强）——熊之坚韧的近似。可被驱散与偷取。",
        "Test dummy buff: +4 enhancement Constitution (bear's endurance-like). Can be dispelled and stolen.",
        cfg => cfg.AddStatBonus(descriptor: ModifierDescriptor.Enhancement, stat: StatType.Constitution, value: 4));

      // ── 三种木桩：普通（5000 血）/ 脆皮（10 血）/ 增益（5000 血 + 6 个常见增益）──
      // 除血量与自带增益外完全一致：速度一律 30 尺（正常）。
      CreateDummy("RhodesDoctorTestDummy", Guids.TestDummyUnit,
        L10n.S("testdummy.name", "测试木桩", "Test Dummy"), 5000,
        new[] { Guids.TestBuffArmor, Guids.TestBuffResist });
      CreateDummy("RhodesDoctorTestDummyLowHp", Guids.TestDummyLowHpUnit,
        L10n.S("testdummy.lowhp.name", "脆皮木桩（10 血）", "Frail Dummy (10 HP)"), 10,
        new[] { Guids.TestBuffArmor, Guids.TestBuffResist });
      CreateDummy("RhodesDoctorTestDummyBuffed", Guids.TestDummyBuffed,
        L10n.S("testdummy.buffed.name", "增益木桩（6 个常见增益）", "Buffed Dummy (6 buffs)"), 5000,
        new[]
        {
          Guids.TestBuffHaste, Guids.TestBuffBless, Guids.TestBuffMageArmor,
          Guids.TestBuffShield, Guids.TestBuffHeroism, Guids.TestBuffBearEndurance,
        });

      Main.Mod.Logger.Log("[RhodesDoctor] 测试木桩蓝图就绪（阵营 / 3 单位 / 8 增益）。");

      // 全部增益标 IsFromSpell（=1）：Flags 枚举是 protected，只能反射写 m_Flags。
      // 否则王冠回响的偷取（只认 IsFromSpell）与驱散都覆盖不到木桩。
      foreach (var g in new[]
      {
        Guids.TestBuffArmor, Guids.TestBuffResist,
        Guids.TestBuffHaste, Guids.TestBuffBless, Guids.TestBuffMageArmor,
        Guids.TestBuffShield, Guids.TestBuffHeroism, Guids.TestBuffBearEndurance,
      })
        MarkAsSpell(g);

      // 照抄原版小型气元素的 Visual / Body（否则被打中触发血迹特效或读 Bark 时 NRE）。
      SummonsSetup.CopyTemplateVisuals(Guids.TestDummyUnit, "测试木桩");
      SummonsSetup.CopyTemplateVisuals(Guids.TestDummyLowHpUnit, "脆皮木桩");
      SummonsSetup.CopyTemplateVisuals(Guids.TestDummyBuffed, "增益木桩");
    }

    /// <summary>建一个"测试增益"：命名/描述走本地化键，效果由 build 回调挂。</summary>
    private static void CreateTestBuff(string guid, string key, string zhName, string enName,
      string zhDesc, string enDesc, Action<BuffConfigurator> build)
    {
      var cfg = BuffConfigurator.New($"RhodesDoctorTestBuff_{key}", guid)
        .SetDisplayName(L10n.S($"testdummy.buff.{key}.name", zhName, enName))
        .SetDescription(L10n.S($"testdummy.buff.{key}.desc", zhDesc, enDesc));
      build(cfg);
      cfg.Configure();
    }

    /// <summary>
    /// 建一个测试木桩单位。三个木桩除血量与自带增益外完全一致——速度一律 30 尺（正常）。
    /// "不攻击"靠刷出时 <c>IsAIEnabled = false</c>（见 TestPanel），不是蓝图行为。
    /// </summary>
    private static void CreateDummy(string configName, string guid, string displayName, int maxHp,
      string[] facts)
    {
      UnitConfigurator.New(configName, guid)
        .SetDisplayName(displayName)
        .SetType(SummonsSetup.Refs.ElementalType)
        .SetPrefab(SummonsSetup.Refs.InklingPrefab)
        .SetSpeed(new Feet(30))
        .SetMaxHP(maxHp)
        .SetSize(Size.Medium)
        .SetAlignment(Alignment.ChaoticEvil)
        .SetStrength(10).SetDexterity(10).SetConstitution(14)
        .SetIntelligence(2).SetWisdom(10).SetCharisma(10)
        .SetFaction(Guids.TestFaction)
        .SetBrain(SummonsSetup.Refs.SummonBrain)
        // Skills 是类（不是结构体），不赋值就是 null；UnitDescriptor.SetSkills 会直接解引用它，
        // 导致 CreateEntityData 抛 NRE、SpawnUnit 返回 null。必须给一个非空实例（原版单位都有）。
        .SetSkills(new BlueprintUnit.UnitSkills())
        // FactionOverrides 同样是类；BlueprintUnit.AttackFactions 会 FactionOverrides.AttackFactionsToAdd，
        // 为 null 就 NRE（原版单位的 FactionOverrides 永远是空对象，不是 null）。所有手搓单位都要给。
        .SetFactionOverrides(new FactionOverrides())
        .SetAddFacts(facts.Select(g => (Blueprint<BlueprintUnitFactReference>)g).ToArray())
        .Configure();
    }

    private static void MarkAsSpell(string guid)
    {
      var buff = BlueprintTool.Get<BlueprintBuff>(guid);
      var field = typeof(BlueprintBuff).GetField("m_Flags",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
      if (field == null)
      {
        // P2-1：字段缺失时给日志，别静默（否则"木桩不能被偷/驱散"很难排查）。
        Main.Mod.Logger.Error("[RhodesDoctor] 测试木桩 IsFromSpell 标记失败：BlueprintBuff.m_Flags 字段未找到。");
        return;
      }
      int cur = System.Convert.ToInt32(field.GetValue(buff));
      field.SetValue(buff, System.Enum.ToObject(field.FieldType, cur | 1));
    }
  }
}
