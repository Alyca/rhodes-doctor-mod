# 正义之怒「神话能力 / 神话专长」系统参考资料

> 作者 2026-10-11 要求：查清游戏里**通用的神话能力**（不是玩家专属道途的），比如「健壮（神话）」，
> 搞清楚整套结构，供后续 mod 制作参考。
>
> **取证来源**（全部为本地实机文件，非记忆/推测）：
> - `blueprints.zip`（游戏自带蓝图全量导出，252,498 个 .jbp）
> - `Wrath_Data/Managed/Assembly-CSharp.dll`（反编译，ilspycmd）
> - `Wrath_Data/StreamingAssets/Localization/zhCN.json`（官方中文，GUID→文本，21MB）
>
> **一句话结论**：玩家口中的「神话能力」在数据层是**两个并行的选择表** ——
> **神话能力（MythicAbility）67 项** + **神话专长（MythicFeat）48 项**，
> 由 `MythicStartingProgression` 按神话等级发放；两者都是普通 `BlueprintFeature`，
> 唯一区别是 `Groups` 里标了 `MythicAbility` / `MythicFeat`，并挂 `PrerequisiteFeature` 做前置。

---

## 一、总体结构：神话等级如何发东西

### 1.1 神话推进蓝图 `MythicStartingProgression`

- GUID `b2cd67193d1199f41bc6ecec3a2f2c87`，类型 `BlueprintProgression`。
- 绑定**神话职业** `m_Class = 247aa787806d5da4f89cfc3dff0b217f`。
- `LevelEntries`（**这是发牌节奏，最终版**）：

| 神话等级 | 发放的 feature（GUID） | 含义 |
|---|---|---|
| **1** | `1421e003…`（FirstAscensionSelection）<br>`ba0e5a900…`（**MythicAbilitySelection**）<br>`c60933cc…`（神话职业）<br>`afd1b8d4…`（道途选择）<br>`c0e6f7e7…` ×2 | 第 1 环：**1 个神话能力 + 1 个神话专长 + 道途** |
| 2 | `9ee0f674…`（**MythicFeatSelection**）、`bef12729…`、`c0e6f7e7…` ×2 | **1 个神话专长** |
| 3 | `ba0e5a900…`（MythicAbilitySelection） | **1 个神话能力** |
| 4 | `9ee0f674…`（MythicFeatSelection） | **1 个神话专长** |
| 5 | `ba0e5a900…` | **1 个神话能力** |
| 6 | `9ee0f674…` | **1 个神话专长** |
| 7 | `ba0e5a900…` | **1 个神话能力** |
| 8/9/10 | （未在基础列表，由道途自身推进补） | —— |

**规律**：神话等级 1 级给「能力 + 专长」各一；**之后奇数级给神话能力、偶数级给神话专长**交替。
（注：8–10 级的内容由各道途的独立推进补，本研究只覆盖**通用**部分。）

### 1.2 两张「选择表」

| 选择表 | GUID | Type | `Group` | 候选数 | 中文名 |
|---|---|---|---|---|---|
| **神话能力** | `ba0e5a900b775be4a99702f1ed08914d` | `BlueprintFeatureSelection` | `MythicAbility` | **67** | 神话能力 |
| **神话专长** | `9ee0f6745f555484299b0a1563b99d81` | `BlueprintFeatureSelection` | `MythicFeat` | **48** | 神话专长 |

- 两张表都是空壳：`Components = []`，只有 `m_AllFeatures`（候选列表）与 `Group` 字段。
- **关键点**：`Group` 字段（不是 `Groups`）是选择表的分类键，取值正是 `MythicAbility` / `MythicFeat`；
  单个具体能力/专长的 `Groups` 数组里也写同一个词 —— **这是游戏把候选归到哪张表的机制**。
- `m_Icon` 两张表**共用**同一个 GUID `fd12994eae19de5489d6ae37e29fd6b0`（神话图标）。

---

## 二、神话专长（MythicFeat）48 项

### 2.1 定义位置与共性

- 全部在 `Mythic/Feats/` 目录（含 `Maneuvers/`、`SkillFocus/`、`ActionFeats/` 子目录）。
- 类型：绝大多数 `BlueprintFeature`；少数**需要参数选择**的是 `BlueprintParametrizedFeature`
  （武器专攻/武器专精/精通重击/法术专攻/学派掌控/扩充战法）。
- **每一个都挂 `PrerequisiteFeature`**，前置 = 对应的**普通专长**（这就是「健壮（神话）」必须先有「健壮」的原因）。

### 2.2 全部 48 项（中文名 ← 蓝图名 → GUID）

| 中文名 | 蓝图名 | GUID |
|---|---|---|
| 盔甲专攻（神话） | ArmorFocusMythicSelection | （选择表） |
| 神话盔甲专攻：轻甲 | ArmorFocusLightMythicFeature | —— |
| 神话盔甲专攻：中甲 | ArmorFocusMediumMythicFeatureVar1/Var2/Offence | —— |
| 神话盔甲专攻：重甲 | ArmorFocusHeavyMythicFeatureVar1/Var2/Offence | —— |
| 致命瞄准（神话） | DeadlyAimMythicFeat | —— |
| 防御式战斗训练（神话） | DefensiveCombatTrainingMythicFeat | —— |
| 生后命运 | DestinyBeyondBirthMythicFeat | —— |
| 破坏震波 | DestructiveShockwaveMythicFeat | —— |
| 闪避（神话） | DodgeMythicFeat | —— |
| 紧急饮药 | EmergencyPotionMythicFeat | —— |
| 扩充战法（魔法学派） | ExpandedArsenalSchool | （参数化） |
| 额外专长 | ExtraFeatMythicFeat | （选择表） |
| **额外神话能力** | ExtraMythicAbilityMythicFeat | （选择表） |
| 无瑕攻势 | FlawlessAttacks | —— |
| 顽石强韧（神话） | GreatFortitudeMythicFeat | —— |
| 精通重击（神话） | ImprovedCriticalMythicFeat | （参数化） |
| **精通先攻（神话）** | ImprovedInitiativeMythicFeat | `25f4cb387ba4ab94d944fdef3da9ab97` |
| 精通徒手击打（神话） | ImprovedUnarmedStrikeMythicFeat | —— |
| 钢铁意志（神话） | IronWillMythicFeat | —— |
| 闪电反射（神话） | LightningReflexesMythicFeat | —— |
| 神话调制药水 | MythicBrewPotionsFeat | —— |
| 神话导能 | MythicChannelMythicFeat | —— |
| 神话决心 | MythicResolveMythicFeat | —— |
| 食人鱼打击（神话） | PiranhaStrikeMythicFeat | —— |
| 近程射击（神话） | PointBlankShotMythicFeat | —— |
| 猛力攻击（神话） | PowerAttackMythicFeat | —— |
| 快速射击（神话） | RapidShotMythicFeat | —— |
| 学派掌控 | SchoolMasteryMythicFeat | （参数化） |
| 盾牌专攻（神话） | ShieldFocusMythicFeat | —— |
| 神话偷袭 | SneakAttackerMythicFeat | —— |
| （Sorcerous Reflex） | SorcerousReflexMythicFeat | —— |
| 法术专攻（神话） | SpellFocusMythicFeat | （参数化） |
| （SpellPenetration） | SpellPenetrationMythicFeat | —— |
| 震慑拳（神话） | StunningFistMythicFeat | —— |
| **健壮（神话）** | **ToughnessMythicFeat** | **`d8df8f8ad7149824e8e60dd3453557d2`** |
| 双武器战斗（神话） | TwoWeaponFightingMythicFeat | —— |
| 要害打击（神话） | VitalStrikeMythicFeat | —— |
| 武器娴熟（神话） | WeaponFinesseMythicFeat | —— |
| 武器专攻（神话） | WeaponFocusMythicFeat | （参数化） |
| 武器专精（神话） | WeaponSpecializationMythicFeat | （参数化） |
| 战技（神话）×5 | BullRush / DirtyTrick / Disarm / Sunder / Trip MythicFeat | `Mythic/Feats/Maneuvers/` |
| 技能专攻（神话）×N | SkillFocus{技能名}MythicFeat | `Mythic/Feats/SkillFocus/` |

---

## 三、神话能力（MythicAbility）67 项

### 3.1 定义位置

- 全部在 `Mythic/MythicAbilities/`。有独立子目录的 = 带主动技能/资源/Buff 的复杂能力
  （LastStand、ElementalBarrage、BreakControl、EmergencyHelp、EnforcedVigor…）。
- 类型：`BlueprintFeature` 为主；「选择器」类（巅峰元素/血承/秘示域/偏好超魔等）是 `BlueprintFeatureSelection`。

### 3.2 全部 67 项（中文名 ← 蓝图名）

**资源/法术类**：充裕施法 / 精通充裕施法 / 高等充裕施法、无穷奥法池、无穷破敌、无穷真气、
无穷辟邪斩、无穷辟乱斩、源泉满溢、无界治疗、超能念袭、念力超量、无限狂暴、永恒审判。

**施法强化类**：大法师护甲、巅峰元素（选择器）、巅峰血承（选择器）、腐秽侵世、防御式研究、
持久法术 / 高等持久法术、偏好超魔（选择器）、学派兼容、揭示弱点、强力阴影、不惧邪恶（以暴制暴）。

**战斗类**：神话冲锋、分裂射击、试射瞄准、雷霆一击、不绝连击、一往无前、无瑕攻势（实为专长）、
擅射巨物、残虐化身、以暴制暴。

**召唤/同伴**：AscendantSummons（巅峰召唤）、MythicalBeastMaster（神话驯兽大师）、
第二血承 / 第二血脉（选择器）、第二秘示域（选择器，含启迪贤者/神圣草药师/独行者变体）、第二精魂（选择器）。

**区域/支援类**：战斗冥想（BattleMeditation）、打破控制（BreakControl）、次元报复（DimensionalRetribution）、
元素弹幕（ElementalBarrage）、紧急救援（EmergencyHelp）、强制活力（EnforcedVigor）、
振奋领袖（InspirationalLeader）、**背水一战（LastStand）**、领军突袭（LeadingStrike）、
魔法无效化（MagicalNullification）、女巫游荡巫术（WitchWanderingHex）。

**其他**：不失水准、惊天笑料、领域痴迷、投掷灵药、爆破专家、神话激励、骑术无双、常备不怠、
转祸为福、额外领域、第二精魂、神话调制药水（专长）。

> 完整清单见本报告末尾「附录：原始数据表」，或直接查 `Mythic/MythicAbilities/` 目录。

---

## 四、★ 对 mod 制作最有用的技术要点

### 4.1 「健壮（神话）」的完整实现（可作为模板复刻）

`Mythic/Feats/ToughnessMythicFeat.jbp`（GUID `d8df8f8ad7149824e8e60dd3453557d2`）拆开看：

```jsonc
{
  "$type": "...， BlueprintFeature",
  "Groups": [ "MythicFeat" ],          // ← 归入神话专长选择表
  "Ranks": 1,
  "ReapplyOnLevelUp": false,
  "Components": [
    { "$type": "...， ToughnessLogic", "CheckMythicLevel": false },   // ① 血量翻倍
    { "$type": "...， DamageReductionBelowZero", "Reduction": 10, "EpicBypass": true }, // ② 血<0 时 DR10/传奇
    { "$type": "...， PrerequisiteFeature", "m_Feature": "!bp_d09b20029e9abfe4480b356c92095623" } // ③ 前置=普通健壮
  ]
}
```

三个组件各自反编译如下（**可直接用 BlueprintCore 复刻**）：

**① `ToughnessLogic`**（`Kingmaker.Designers.Mechanics.Facts.ToughnessLogic`，TypeId `d32a32fa01d208c4bbf31462ec510fb3`）
```csharp
// 监听升级；CheckMythicLevel=false → 用 CharacterLevel（否则用 MythicLevel）
// 数值 = max(3, 等级)，作为 UntypedStackable 加到 HitPoints
int num = CheckMythicLevel ? Owner.Progression.MythicLevel : Owner.Progression.CharacterLevel;
int value = CheckMythicLevel ? num : Math.Max(3, num);
Owner.Stats.HitPoints.AddModifier(value, Runtime, ModifierDescriptor.UntypedStackable);
```
> 注意：`ToughnessLogic` **本身**就是「等级加到血量」的通用组件（普通「健壮」专长用的也是它），
> 神话版的价值在于**叠加**（普通健壮再来一份等级血量）。

**② `DamageReductionBelowZero`**（同命名空间，TypeId `2e176e89348547498d60f69899e9612b`）
```csharp
// 仅当「目标 HP ≤ 0」且是物理伤害时，给一个减伤（绕过传奇则无效）
public int Reduction;   // = 10
public bool EpicBypass; // = true
OnEventAboutToTrigger(RuleCalculateDamage evt):
  if (evt.Target.HPLeft <= 0 && evt.DamageBundle.WeaponDamage is PhysicalDamage p
      && (!EpicBypass || !p.IsEpic))
    p.SetReductionBecauseResistance(Reduction, Fact);
```
> `AllowedOn(BlueprintUnitFact)`，`AllowMultipleComponents`。**想给 mod 加"濒死不倒/负血减伤"就用它。**

**③ `PrerequisiteFeature`**：前置 = 普通「健壮」（`d09b20029e9abfe4480b356c92095623`）。
> **神话专长几乎全靠这一条做门槛**：先点普通版，才能点神话版。

### 4.2 神话能力的通用「组件配方」（按效果归类）

从 67 项里提炼出可复用的组件模式：

| 想要的效果 | 用哪个组件 | 关键字段 | 例子 |
|---|---|---|---|
| **按神话等级加数值** | `ContextRankConfig` + `AddContextStatBonus` | `m_BaseValueType = **"MythicLevel"**`，`Value.ValueType=Rank` | 精通先攻（先攻 +神话等级） |
| 加固定属性/加值 | `AddStatBonus` | `Stat`、`Value` | 神话决心 |
| 增加资源池 | `IncreaseResourceAmountBySharedValue` | 引用共享值（`ContextCalculateSharedValue`） | 无穷奥法池/真气/辟邪斩 |
| 加每日法术位 | `AddSpellsPerDay` | 法术书引用 | 充裕施法 |
| 加无限法术 | `AddUnlimitedSpell` + `AutoMetamagic` | 法术引用 | 无界治疗 |
| 加机制开关 | `AddMechanicsFeature` | `m_Feature` = `MechanicsFeatureType` 枚举 | 见 4.4 |
| 攻/伤触发 | `AddInitiatorAttackWithWeaponTrigger` | 触发动作 | 雷霆一击、不绝连击 |
| 接触 AC | `ACTouchBonus` | | 盾牌专攻（神话） |
| 忽略法术免疫 | `IgnoreSpellImmunity` | | 腐秽侵世 |
| 加法术 DC | `IncreaseSpellDC` | | 震慑拳（神话） |
| 减 d20（重投/取优） | `ModifyD20` | `Rule`、`RollConditionType` | 顽石强韧/钢铁意志/闪电反射 |
| 按体型加命中 | `AttackBonusAgainstSize` | | 擅射巨物 |
| 对手回合外攻击加值 | `AttackOfOpportunityAttackBonus` | | 常备不怠 |
| PET 相关 | `AddFeatureToPet` / `PrerequisitePet` | | 神话驯兽大师 |
| 目标附近伤害扩散 | 自定义 `RangedCleave` 组件 | `Range`（尺） | 分裂射击 |

### 4.3 神话等级在代码里怎么取

- `ContextRankBaseValueType` 枚举里**有 `MythicLevel` 与 `MasterMythicLevel`**（还有 `MythicLevelPlusBuffRank`）。
  挂 `ContextRankConfig` 时把 `m_BaseValueType` 设成 `MythicLevel`，后续 `ValueType=Rank` 就能吃神话等级。
- C# 里直接读：`unit.Progression.MythicLevel`（反编译 `ToughnessLogic`/`MythicUnarmedStrike`/`DefensiveCombatTraining` 都在用）。
- `MythicUnarmedStrike`（神话徒手）：`evt.AddDamageModifier(Initiator.Progression.MythicLevel / 2, Fact)` —— **神话等级 ÷ 2** 的典型写法。
- `DefensiveCombatTraining`（`Mythic=true` 时）：`evt.AddModifier(Math.Max(Owner.Progression.MythicLevel / 2, 1), ...)` —— **至少 1**。

### 4.4 `AddMechanicsFeature.MechanicsFeatureType` —— 神话机制的开关枚举

这是**极其重要**的一个枚举（114 项，含道途专属）。**与通用神话相关的**：

| 枚举值 | 序号 | 用途 |
|---|---|---|
| `MythicHardToKill` | 69 | 神话「难杀」 |
| `MythicAbilitiesAsMoveAction` | 71 | 神话能力改为移动动作 |
| `MythicFullAttackAfterMoveAction` | 72 | 移动后全回合攻击 |
| `MythicFullLevelToCR` | 73 | 神话等级全额转 CR |
| `MythicReduceResistances` | 74 | 降低抗性 |
| `EnableSpellLevel10` | 102 | **开 10 环**（本 mod 的 10 环指令就靠它兜底） |
| `Immortality` | 44 | 不死 |
| `AlwaysChance` | 61 | 「不失水准」 |
| `FlawlessAttacks` | 88 | 「无瑕攻势」 |
| `MasterShapeshifter` | 84 | 「变形大师」 |
| `CombatHealer` | 85 | 战斗医疗 |
| `SharePersonalSpellsWithPets` | 100 | 个人法术分享给宠物 |
| `CannotBeFlanked` | 10 | 不被夹击 |
| `Pounce` | 11 | 扑击 |

> **道途专属**（部分）：`LegendaryHero`=95、`AzataFavorableMagic`=91、
> `MythicDemonNaturalAttacksNoSecondary`=90、`Trickster*` 系列 63–70/97–98。
> **写 mod 时若要"开某个神话效果"，先查这个枚举有没有现成开关**，比自造组件省事。

### 4.5 把自定义内容接进神话选择表

若想让 mod 的某个能力出现在「神话能力」选择界面里，机制是：

1. 建一个 `BlueprintFeature`，**`Groups = ["MythicAbility"]`**（或 `"MythicFeat"`）；
2. 挂 `PrerequisiteFeature`（前置）与效果组件；
3. 把它的 GUID **追加进 `MythicAbilitySelection.m_AllFeatures`**（用 BlueprintCore 的 `FeatureSelectionConfigurator.For(...).AddToAllFeatures(...)`，
   或改 `m_AllFeatures` 数组）。
4. 如需按神话等级成长，挂 `ContextRankConfig`（`MythicLevel`）。

> **注意**：`Group`（单数，选择表自己的分类）与 `Groups`（复数，具体项的归属）是**两套字段**，
> 选择表用 `Group`、叶子用 `Groups`，两边都写 `MythicAbility` 才配得上。

---

## 五、本 mod 目前的接壤点（供后续决策）

- `SpellbookSetup.cs` 已有 `CreateMythicSpellList()`（`Guids.MythicSpellList = P+"e4"`）与
  `RegisterForMythicMerge()`（把博士法术书登记进各道途的 `BlueprintFeatureSelectMythicSpellbook.AllowedSpellbooks`）。
  —— 这是**合书**用途，与「神话能力选择表」是**两回事**，别混淆。
- 若后续想给博士/变体加**神话能力**（如「充裕施法」风格的自发法术位补充），
  建议做法：自建 `BlueprintFeature` + `Groups=["MythicAbility"]` + `AddSpellsPerDay`，并追加进 `MythicAbilitySelection`。
- 若想加**负血不倒**类效果，直接挂 `DamageReductionBelowZero`（`Reduction`/`EpicBypass`）。

---

## 附录：原始数据表（脚本可复现）

**复现脚本**（蓝图解包 + 本地化解析）：
- 解包：`unzip -o blueprints.zip "Mythic/Feats/*" "Mythic/MythicAbilities/*" "Mythic/Mythic*Selection.jbp"`
- 解析本地化：`zhCN.json` → `["strings"][m_Key]`（`m_Key` 即 GUID 字符串）。
- 组件反编译：`ilspycmd -t <完整类名> Assembly-CSharp.dll`
  （神话相关类在 `Kingmaker.Designers.Mechanics.Facts` 与 `Kingmaker.UnitLogic.FactLogic`）。

**关键 GUID 速查**：

| 对象 | GUID |
|---|---|
| 神话起始推进 | `b2cd67193d1199f41bc6ecec3a2f2c87` |
| 神话职业 | `247aa787806d5da4f89cfc3dff0b217f` |
| **神话能力选择表** | **`ba0e5a900b775be4a99702f1ed08914d`** |
| **神话专长选择表** | **`9ee0f6745f555484299b0a1563b99d81`** |
| 首次晋阶选择 | `1421e0034a3afac458de08648d06faf0` |
| 健壮（神话） | `d8df8f8ad7149824e8e60dd3453557d2` |
| 精通先攻（神话） | `25f4cb387ba4ab94d944fdef3da9ab97` |
| 普通健壮（前置） | `d09b20029e9abfe4480b356c92095623` |

**神话相关组件 TypeId 速查**：

| 组件 | TypeId | 命名空间 |
|---|---|---|
| `ToughnessLogic` | `d32a32fa01d208c4bbf31462ec510fb3` | `Kingmaker.Designers.Mechanics.Facts` |
| `DamageReductionBelowZero` | `2e176e89348547498d60f69899e9612b` | 同上 |
| `MythicUnarmedStrike` | `24d2e2483d5a466cb9cae6b1a1d7da3e` | 同上 |
| `DefensiveCombatTraining` | `da48d6c227bab7d42892fc31b480d14d` | 同上 |
| `EnduringSpells` | `637e85dfdde641b8bc208dea1ca563a7` | 同上 |
| `RangedCleave` | `536fc0ec41634558b5e415e03836d704` | `Kingmaker.UnitLogic.FactLogic` |
| `ModifyD20` | `149337b6a93e55a4a81aa2010c8e1c58` | `Kingmaker.Designers.Mechanics.Facts` |
| `AddMechanicsFeature` | `05fab0d4998390a4cbf88da0ab1e4123` | `Kingmaker.UnitLogic.FactLogic` |
