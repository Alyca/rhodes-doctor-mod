# 罗德岛博士（圣武士变体）0.5.1 交接审计报告

- 执行者：本机 AI
- 依据文件：`rhodes-doctor-local-ai-handoff.md`（下称"交接"）
- 证据来源：游戏安装目录 `D:\steam\steamapps\common\Pathfinder Second Adventure` 的 `Wrath_Data\Managed\Assembly-CSharp.dll`（ilspycmd 9.0 反编译）、`blueprints.zip`（原版 `.jbp`）、`%USERPROFILE%\.nuget\packages\ww-blueprint-core\2.8.7`（BlueprintCore.dll + XML 文档）。
- 编译：`dotnet build -t:Rebuild -p:WrathPath=...` 通过，0 警告 0 错误（源码与本机游戏程序集/BlueprintCore 2.8.7 一起编译）。
- 行号：交接里的行号是旧版本，本报告按内容定位。

---

## 3. V 项（先核实）

| 编号 | 结果 | 改动位置 | 证据 | 备注 |
|---|---|---|---|---|
| V1 | 已核实属实，已改 | `SpellsSetup.CreateSpell`（范围段） | `AbilityAoERadius` 反编译只有 `IAbilityAoERadiusProvider/IAffectedCellsProvider/IReachInCellsProvider`，`OnTrigger` 不选目标；原版 `Spells/Level3/Fireball.jbp` 的范围组件是 `757439ef8cc900741bd9b57bf26eb500 = AbilityTargetsAround`（`m_Radius 20 / m_TargetType Any`），其 `Select()` 用 `GameHelper.GetTargetsAround` 选圈内单位并在 `context.MaybeCaster.IsEnemy/IsAlly` 上过滤。已对范围法术改挂 `AddAbilityTargetsAround`（它同时实现 `IAbilityAoERadiusProvider`，会画圈）。 | 烟幕/钙质化/儿时舞乐/天灾（`SpawnAreaEffect` 自扫）、重构/文明的存续（自扫全场动作）、炮火延伸（自写落点扫描）、延时爆破（自写炸弹）不挂 `TargetsAround`，否则同一动作会按目标数重复执行。 |
| V2 | 已核实属实，已改 | `SpellsSetup.CreateSpell`（直线段） | `AbilityDeliverProjectile.Deliver()`：`referenceArrayProxy = Projectiles; if (Length==0) ...; take(count).Select(...)`。原版 `Spells/Level3/LightningBolt.jbp` 的 `AbilityDeliverProjectile` 带 `m_Projectiles=["!bp_c7734162c01abdc478418bfb286ed7a5"]`，`Type=Line`。旧代码只传 `length/lineWidth/type`，`projectiles` 为空 → 投递不选取任何目标。已补 `projectiles:`（借用原版闪电束投射物）。 | 直线投射物借用了闪电束的贴图（伤害类型由效果动作决定，不影响结算），视觉上可能不符；锥形（闪光弹）仍以点爆发近似。 |
| V3 | 已核实属实，已改 | `ArchetypeSetup.CreateAuras` | `Kingmaker.Designers.Mechanics.Facts.AuraFeatureComponent.OnActivate`：`Data.AppliedBuff = Owner.AddBuff(Buff, ...)`——只加给持有者自己。原版 `AuraOfCourageFeature.jbp` 的 `AuraFeatureComponent.m_Buff = AuraOfCourageBuff`，`AuraOfCourageBuff.jbp` 带 `AddAreaEffect → AuraOfCourageArea`，`AuraOfCourageArea` 带 `AbilityAreaEffectBuff(TargetType Ally, ContextConditionIsAlly) → AuraOfCourageEffectBuff`。已按此三段结构重做指挥灵光/全军指令。 | 旧的 `AuraCommandSelfBuff` 名/描述保留，改为承载区域；新增区域蓝图 `AuraCommandArea/TotalCommandArea`。 |
| V4 | 已核实属实（会受影响），已改 | `ArchetypeSetup.CreateSpellcastingFeature` + `Components.IgnoreArcaneSpellFailure` | `AbilityData.IsAffectedByArcaneSpellFailure`：`SpellSource==Arcane && Spellbook!=null && !Spellbook.IsStandaloneMythic` → true；`RuleCastSpell.OnTrigger` 随后触发 `RuleCalculateArcaneSpellFailureChance`；该类有公开 `IgnoreArmor/IgnoreShield`，`OnTrigger` 里 `(Armor!=null && !IgnoreArmor)` 才计入。已在施法特性上挂一个把二者置真的组件。 | 组件按"施法者"生效，因此对博士任何奥秘施法都免失败率；单职业博士即本法术书。多职业时略宽（已在备注记录）。 |
| V5 | 已核实无问题（原写法不扣法术位），仍按 B19 改 | `Components.SpecterCasterRestriction`、`SpecterSetup.Lock` | `RuleCastSpell.ShouldSpendResource`：`if (!ForceFail) return !IsUMDFailed; return false;`；`OnTrigger` 里 `if (ForceFail) { Success=false; return; }` 在法术位结算前返回。即 ForceFail 不会扣法术位。 | 保留结论：原写法"不扣法术位"；已按 B19 改成施法者限制组件（按钮变灰）。 |
| V6 | 已核实属实，已改 | `ArchetypeSetup.ClonePaladinFeatures`（圣疗资源） | BlueprintCore `ResourceAmountBuilder.IncreaseByLevel(classes, bonusPerLevel)`：文档"by BonusPerLevel * ClassLevels"，字段 `Amount.LevelIncrease = bonusPerLevel`。旧代码 `New(0).IncreaseByLevel(paladin, 2)` = 2×等级 + 智力（错）。已改 `IncreaseByLevelStartPlusDivStep(levelsPerStep:2, bonusPerStep:1)`，即半级 + 智力。 | 半级向下取整。 |
| V7 | 已核实属实，已改 | `ArchetypeSetup.ClonePaladinFeatures`（引导） | `ChannelEnergyPaladinHeal.jbp`（`6670f0f2…`）的 `AbilityResourceLogic.m_RequiredResource = 9dedf41d995ff4446a181f143c3db98c`，`Amount=2`；`LayOnHandsSelf/Others.jbp` 用同一 `9dedf41d…`。而本 mod 的 `ChannelPositiveResource = 5e2bba3e07c37be42909a12945c27de7` 是 `Classes/Cleric/ChannelEnergyResource`（牧师池）。博士移除圣武士原版特性后没有 `9dedf41d…`。已把克隆的引导能力改为消耗 `Guids.LohResource`（每次 2）。 | 设计文档未明写引导消耗什么，按原版"每次 2 次圣疗"语义指向本变体圣疗；`ChannelFeature` 不再单独发牧师池。 |
| V8 | 已核实属实，已改 | `Components.ExecutionStrike` | `RuleAttackWithWeapon.OnTrigger`：先 `Rulebook.Trigger(AttackRoll)`（含 DidTrigger），命中后才 `CreateRuleDealDamage(...)` + `context.Trigger`。旧版在攻击检定 DidTrigger 移除增益 → 组件退订，伤害翻倍不生效。已改为命中后保留、伤害结算时翻骰、结算后再移除，且打空不消耗（B4）。 | — |
| V9 | 已核实会丢，已改 | `Actions.ContextActionFinalSnipe` | 旧写法在标记增益 `OnDeactivate` 里用"正在被移除的增益"（`b.Context==Context`，即标记本身）当 source 加体质减值；增益移除后该修饰不可靠。已改用 `RuleDealStatDamage(caster, target, StatType.Constitution, 2d6, 0)`。 | 属性伤害本身持久、走伤害管线。 |
| V10 | 已核实属实，已改 | `SpecterSetup.CreateActivator` | `BuffCollection.PrepareFactForAttach`：非 Stack 叠加时 `GetBuff(blueprint)` 找到旧增益，`StackingType.Replace` 分支 `break` 后 `base.Manager.Remove(buff)` 移除旧增益 → 触发旧增益的 Deactivated（余波）。已把"同一形态增益"也加入 `AbilityCasterHasNoActiveFacts` 的名单。 | — |
| V11 | 部分属实（漏了 BuffStatusCondition），已改 | `Actions.ContextActionRemoveBlindness` | 原版 `Spells/Level2/BlindnessBuff.jbp`、`Buffs/Blind.jbp` 用 `AddCondition{Blindness}`（旧代码已认）；`Spells/Level2/GlitterdustBlindnessBuff.jbp`、`Spells/Level7/PowerWordBlindBuff.jbp` 用 `BuffStatusCondition{Condition=Blindness}`（旧代码漏掉）。已两种都纳入。 | — |
| V12 | 部分属实（抗力范围过宽；豁免已含类法术），已改 | `Components.StackingEnergyResistance`；`SaveBonusVsSpells` 未改 | `DamageEnergyType` 含 Fire/Cold/Sonic/Electricity/Acid/NegativeEnergy/PositiveEnergy/Holy/Unholy/Divine/Magic；旧代码 `dmg is EnergyDamage` 会把神圣/邪恶/负能都减 10。已限定五元素。豁免侧：`BlueprintAbility.IsSpell => Type==0(Spell) || Type==SpellLike`，故 `SourceAbility?.IsSpell` 已包含类法术能力（无需改）。 | — |
| V13 | 两部分都属实，已改 | `SpellsSetup`（TidalElegy）、`Components.DodgeVsAoO` | (a) `TemporaryHitPointsPerLevel.OnActivate`：`num = Value.Calculate(Context) * HitPointsPerLevel * Owner.Progression.CharacterLevel` → 旧写法是 60×等级；已改 `AddTemporaryHitPointsFromAbilityValue(ContextValues.Constant(60))`（该类 `OnActivate` 是 `Value.Calculate` 固定值）。(b) `RuleCalculateAttackBonusWithoutTarget.OnTrigger`：`AddModifier(BAB - AttackBonusPenalty, ...)`，正数=减值；旧代码 `SetAttackBonusPenalty(-Value)` 反了，已改正的 `Value`。 | — |
| V14 | 已核实无问题（可叠加），无需改 | — | 原版 `Spells/Level3/Haste.jbp` 的速度来自 `HasteBuff` 链上的 `BuffMovementSpeed{Descriptor=Enhancement, Value=30}`（增强加值）；额外攻击来自 `BuffExtraAttack{Number=1, Haste=true}`。`RuleCalculateAttacksCount.AddExtraAttackForHand`：`if (haste) hand.HasteAttacks = Max(...)`（同类取最大，不叠加）；否则 `AdditionalAttacks += amount`。本 mod 的 `ExtraAttackStackable` 调 `AddExtraAttacks(1, false, false, null)` → 进 `AdditionalAttacks`，与加速术的 `HasteAttacks` 相加；速度用 `UntypedStackable`，与加速术的 `Enhancement` 不同类。**无"不能同时生效"的限制**。 | 迅捷突破/永恒的注视按此实现（C34）。 |
| V15 | 岁稔当初不能点地，已加；其余本已能 | `SpellsSetup`（HarvestYears 加 AoERadius/CanTargetPoint） | 圣域/潮汐守望/旗帜不倒/永恒的注视旧代码已有 `AoERadius>0` → `SetCanTargetPoint(true)`；岁稔是单体所以不能。已把岁稔改为近距点选 + 30 尺爆发。 | 5 个法术均按 C1 变为"近距一点为心、30 尺爆发友军"。 |
| V16 | 部分做到，其余按备注处理 | 见"做不到"一栏 | — | 穿墙/墙体减伤、建筑双倍：做不到，已删文档半句（T11）。召唤物双倍伤害：未做。其余补回效果见下。 |

---

## 4. B 项（确定的代码错误）

| 编号 | 结果 | 改动位置 | 备注 |
|---|---|---|---|
| B1 | 已改 | `SpellsSetup.CreateSpell` 等级名单 | 补入 罪罚/连锁殉爆/永恒的注视/影之疆域/旗帜不倒。 |
| B2 | 已改 | `SpellsSetup`（Foxfire regen buff、HarvestYears buff 各挂等级 rank） | 儿时舞乐回血与岁稔每轮治疗现在读得到施法者等级。 |
| B3 | 已改 | `SpellsSetup` ShadowRealm 分支 | 载体增益改 `AddAreaEffect → ShadowRealmArea`（友军隐形、敌人影触），敌我不再颠倒。 |
| B4 | 已改 | `Components.ExecutionStrike` | 打空不消耗（配合 V8 的时机修正）。 |
| B5 | 已改 | `SpellsSetup` Smokescreen | 区域 `m_TargetType` 由 Enemy 改 Ally，隐蔽给友军。 |
| B6 | 已改 | `Actions.ContextActionStealBuffsCustom` | 只取"敌方施加的、非减益"的增益（旧版只看 `IsFromSpell`）。 |
| B7 | 已改 | `Actions.ContextActionFinalSnipe` | 去掉 100 尺硬编码限制。 |
| B8 | 已改 | `Actions.ContextActionChainExplosion` | 加 `u.IsEnemy(caster)`，只伤敌人。 |
| B9 | 已改 | `SpellsSetup` TacticianOfOld 分支 | 只有当敏捷/体质任一段生效时才补对端，否则什么都不给。 |
| B10 | 已改 | `SpecterSetup.CreateFormBuff` + `TransferRankRaw` | 护甲最大敏捷加值改用"转移量"原值档（不再复用 ×2 的属性加值档）。 |
| B11 | 已改 | `SpecterSetup.CreateStage` | 12 级（二段）起改用 `LockBuffV3` 通道 → `OriginiumMastery` 视为全额倒扣。 |
| B12 | 已改 | `ArchetypeSetup.CreateFocusFire`、`Components.FocusFireMark` | (1) 集火加 `AbilityCasterHasNoActiveFacts{LockBuff}`：一段/二段锁定期间禁用、16 级三段锁定不禁；(2) rank 加 `min:1`；(3) 伤害只对武器攻击、每次加一次。 |
| B13 | 已改 | `Components.HarvestYearsAccumulator` + 新增 `HarvestYearsResidual`、`Guids.HarvestYearsResidualBuff` | 结束时挂独立"余温"增益（1 分钟），由它自己负责加/移临时生命；上限 60。 |
| B14 | 已改 | `Components.EternalGazeWatch` | 用 `FreePlaceSelector.RelaxPoints` 找落点，不再写死 (2,0,2)。 |
| B15 | 已改 | `Components.InklingFightsWithMasterBAB` | 只在 `DamageBundle.Weapon != null`（武器攻击）时加 2d6，自爆不吃。 |
| B16 | 已改 | `SpellsSetup` Metas（ShadowBladeDraw） | 加 `SpellDescriptor.Death`。 |
| B17 | 已改 | `Actions.ContextActionMassRestore` | 只作用于友军（`IsPlayerFaction`）、排除召唤物（`Master!=null`）；解除时排除诅咒/疾病/毒素类。 |
| B18 | 已改 | `Components.InklingDeathBurst.OnDeactivate` | 只有 `Owner.Descriptor.State.IsDead` 才爆发，到期不自爆。 |
| B19 | 已改 | `Components.SpecterCasterRestriction` + `SpellsSetup.CreateSpell` + `SpecterSetup.Lock` | 非【恶灵】指令挂施法者限制组件；旧的 `SpecterSpellRestriction`(ForceFail) 从锁定移除。 |

---

## 5. C 项（作者已决定的改动）

| 编号 | 结果 | 改动位置 | 备注 |
|---|---|---|---|
| C1 | 部分已改 | 圣域/潮汐守望/岁稔/旗帜不倒/永恒的注视 → 近距一点 + 30 尺爆发友军（`SpellsSetup` 各 Spec + TargetsAround） | 另 4 个现状见专栏。 |
| C2 | 已改 | `SpellsSetup` Dragnet 分支 + `Actions.ContextActionTeleportToPoint` | 去掉战技检定，只留强韧；失败者传送到法术点附近并倒地。 |
| C3 | 已改 | `SpellsSetup` ChainDetonation（加 AoERadius 20 Enemy）+ B8 | 以目标敌人为中心 20 尺内敌人全部被标记；爆炸只伤敌人。 |
| C4 | 未完成 | — | 见"做不到/未完成"。天灾降临仍是单次 12d6 火焰（未做整轮整分钟、每轮三种伤害）。 |
| C5 | 已改 | `SpellsSetup` SuppressingShot | 罚值改 −2。 |
| C6 | 未完成 | — | 紧急救治未附加慈悲（需把圣武士慈悲链接进治疗动作），见"做不到/未完成"。 |
| C7 | 已改 | `SpellsSetup` Ura Spec | 1 分钟/级。 |
| C8 | 未完成 | — | 剑雨"失败者恍惚 1 轮"未加。 |
| C9 | 已改 | `SpellsSetup` LockOfTime（`BuffStatusCondition.SaveEachRound`） | 每轮重新豁免，成功解除。 |
| C10 | 未完成 | — | 灼烧"火焰抗力 −10 / 1 轮/级"未加。 |
| C11 | 未完成 | — | 火山 2 轮余焰未加。 |
| C12 | 未完成 | — | 梦魇的恐慌→动摇两段未做。 |
| C13 | 未完成 | — | 闪光弹的目盲→目眩两段、隐形显形未做（仍是 15 尺点爆发 + 目盲）。 |
| C14 | 已改 | `SpellsSetup` Ironcast Spec | 1 轮/级。 |
| C15 | 未完成 | — | 冰封领域的困难地形/首入伤害/定身与速度减半未做（仍为 4d6 寒冷）。 |
| C16 | 未完成 | — | Mon3tr 解除震慑/恍惚/麻痹/恶心未加。 |
| C17 | 未完成 | — | 黑蛇之焰持续灼烧/半抗/免疫半伤未做。 |
| C18 | 部分已改 | `SpellsSetup` DrunkenDream Spec | 持续改 1 轮/级；"受伤醒来恍惚、免疫睡眠仍恍惚"未做。 |
| C19 | 未完成 | — | 黄昏熔火未加。 |
| C20 | 未完成 | — | 雷霆审判倒地+恍惚未加。 |
| C21 | 未完成 | — | 大地之怒倒地/困难地形未加（仍为纯伤害）。 |
| C22 | 未完成 | — | 穿云"忽略伤害减免"未做（掩蔽/屏障相关已按 V16 删文档）。 |
| C23 | 已改 | `SpellsSetup` TidalVigil Spec | 1 分钟/级（30 尺爆发友军）。 |
| C24 | 已改 | `Actions.ContextActionFinalSnipe` | 补反射减半。 |
| C25 | 已改 | `SpellsSetup` RoyalCharge | 成功也受 4d6，仅免倒地。 |
| C26 | 未完成 | — | 钙质化"速度减半、豁免 −2"仍是 `Slowed` 状态（近似，未换成精确半速）。 |
| C27 | 未完成 | — | 儿时舞乐敌人意志豁免未接（区域挂增益未过豁免）。 |
| C28 | 已改 | `Components.BreachStrike` | 去掉 +BAB 伤害。 |
| C29 | 已改 | `SpellsSetup` Ironcast buff | 改用引擎 `AddDamageResistancePhysical(value:10, isStackable:false)`，与同类不叠加。 |
| C30 | 未完成 | — | 文明的存续"免死只一次"仍是 `UnitHealthGuard`（增益期内可反复保命）。 |
| C31 | 已改 | `Components.HarvestYearsAccumulator` | 上限 60。 |
| C32 | 已改 | `SpellsSetup` BeaconProjection | 去掉 `FireReconMark`，只留照明。 |
| C33 | 未完成 | 已写好 `Actions.ContextActionDelayedBlastBurst`，未接线 | 见"做不到/未完成"。 |
| C34 | 已改 | `SpellsSetup.HasteAccel()` | 只给速度 + 额外攻击；不具类型速度、非 haste 通道额外攻击，可与加速术叠加。 |
| C35 | 已改 | `SpellsSetup` SwiftBreach | 加速 3 轮；3d6 只管本轮。 |
| C36 | 已改 | `SpellsSetup` EternalGaze + `Components.EternalGazeWatch` | 触发后由组件新挂加速（1 分钟），不拿正在移除的增益当来源。 |
| C37 | 部分已改 | `Actions.ContextActionStealBuffsCustom`（B6）+ `SpellsSetup` CrownEcho | 只取敌方增益已改；"失败直接失去全部可驱散增益、不过驱散检定 + 恍惚"未完全实现（仍走驱散动作）。 |
| C38 | 未完成 | — | 文明的存续"可被复原术解除的状态 + 战斗状态"未细化。 |

---

## 6. T 项（文档与游戏内文字同步）

| 编号 | 结果 | 位置 |
|---|---|---|
| T1 | 已改 | 中文设计文档"战场测绘"删除"知道最近敌人方向"；`LocalizedStrings.json` 同步。 |
| T2 | 已改 | 中文设计文档"紧急撤离"按代码（落在施法者身后最近空地）改写；JSON 同步。 |
| T3 | 部分已改 | 圣域/潮汐守望/岁稔/旗帜不倒/永恒的注视：文档与 JSON 均改为"30 尺爆发内全部友军 / 近距一点为心"；另 4 个待回报现状后再定，未改。 |
| T4 | 已改 | 牵引 JSON 去掉"至多三个敌人"。 |
| T5 | 已改 | 连锁殉爆 JSON 改为"以目标为中心 20 尺内全部敌人"。 |
| T6 | 已改 | 天灾降临 JSON（文档本已为整轮/1 分钟）。 |
| T7 | 已改 | 信标投射 JSON 只写照明。 |
| T8 | 已改 | 迅捷突破 JSON 写"加速，持续 3 轮"。 |
| T9 | 已改 | 永恒的注视 JSON 写"触发后 1 分钟加速"，删额外标准动作。 |
| T10 | 已改 | 王冠的回响 JSON（成功随机失去一个、失败全部 + 恍惚）。 |
| T11 | 已改 | 穿云、大地之怒文档与 JSON 删除做不到的半句。 |
| T12 | 已改 | 文明的存续 JSON 写明解除范围与免死一次（代码未完全落实，见 C30/C38）。 |
| T13 | 已改 | 岁稔 JSON 上限 60。 |
| T14 | 部分已改 | 乌拉/铸铁/潮汐守望/醉梦持续、迅捷突破/永恒的注视加速等已同步；其余 C 项未完成者未同步。 |
| 6.3 提示框顶部栏 | 来源已定位，未改 | 见"新发现"：顶栏来自 `LocalizedStrings.json` 的 `rhodesdoctor.duration.*` 键，经 `AbilityData`/`AbilityTooltip` 读取；本次未逐一替换。 |

---

## 单独栏 1：做不到 / 未完成（编号、卡在哪、查过什么）

- **V16 穿云"穿墙"与"墙体减伤"**：卡在无接口。`AbilityDeliverProjectile` 的线投递用 `ProjectileController.Launch`，命中集合由投射物路径决定，没有"每穿 5 尺实体墙扣 2d6"或"忽略物理屏障"的字段；原版没有任何直线法术这么做。已按备注删掉文档半句（T11）。查过：`AbilityDeliverProjectile`、`ProjectileController`、原版 LightningBolt/BurningHands。
- **V16 大地之怒"建筑受双倍伤害"**：卡在无"建筑"单位类型可判。`RuleDealDamage` 只能看 `Target`（`UnitEntityData`），没有建筑分类字段；`MapObject` 不是单位，伤害管线不经过。已按备注删掉文档半句（T11）。"召唤物受双倍伤害"未做（可用 `Target.Master!=null` 判，但需自写伤害钩子，本轮未接）。
- **V16 灼烧降低火焰抗力 / 钙质化速度减半 / 铸铁不叠加 / 黑蛇忽略半抗 / 穿云忽略减免 / 紧急救治慈悲 / 延时爆破手动引爆**：见下面各 C 项，均未完成或部分完成。
- **C4 天灾降临**：卡在"每轮、区域内每个敌人、三种伤害、整分钟"需要一个随区域存活的每轮伤害载体。可行做法是 `BlueprintAbilityAreaEffect + AbilityAreaEffectRunAction.Round` + `SpawnAreaEffect(1 分钟)`，本轮时间不足以稳妥接线与验证，未改。
- **C6 紧急救治附加慈悲**：需要把原版圣武士慈悲（Mercy）链（`LayOnHandsOthers` 的 `ContextConditionCasterHasFact(慈悲特性)` 条件树）接进本变体的治疗动作，涉及 6 个慈悲特性；本轮未接。
- **C8/C10/C11/C12/C13/C15/C16/C17/C19/C20/C21/C22/C26/C27/C30/C38**：这些多为"豁免失败后追加状态/持续伤害/两段状态"或"引擎状态精确化"。通用做法是把伤害分支改成 `ConditionalSaved{failed: 全额+附加, succeed: 半伤}` 并新增对应增益/组件；本轮已改完的 B/V 与高优先级 C 占用了主要时间，这些未能有把握地接线，故按交接规则不臆造替代，列为未完成/需商量。
- **C33 延时爆破**：已实现 `ContextActionDelayedBlastBurst`（读 `ArtilleryBarrageTracker` 的落点 + DC，10 尺内 2d6 火焰 + 2d6 钝击、反射减半），但"敌人靠近触发 + 下轮爆炸 + 单独手动引爆能力"的接线与实体（炸弹落到地面）未完成，故未接入法术，也未建手动引爆能力与对应 GUID 使用点。
- **C37 王冠回响的"直接失去、不过驱散检定"**：`ContextActionDispelMagic` 走驱散检定；要做到"不过检定直接全失"需自写移除动作（类似 `ContextActionStealBuffsCustom` 但不分配），本轮未改。

---

## 单独栏 2：C1 中坚守阵地、骑兵突进、乌拉、过载模式的中心点与范围现状

（代码现状，本轮**未改**这些，按 C1 要求"中心点先保持代码现状"。）

| 法术 | 中心点 | 范围 | 目标类型 | 说明 |
|---|---|---|---|---|
| 坚守阵地 | 玩家点选的地面点（`SetCanTargetPoint(true)`） | 半径 30 尺 | Ally | 近距，迅捷动作，3 轮。V1 后挂 `AbilityTargetsAround`，效果作用于以点为中心 30 尺内友军。 |
| 骑兵突进 | 玩家点选的地面点 | 半径 30 尺 | Ally | 同上；速度 +10/+20（不具类型）、借机 AC +4（已修正符号）。 |
| 乌拉 | 玩家点选的地面点 | 半径 30 尺 | Ally | 1 分钟/级；攻击/伤害 +1/+2（不具类型）。 |
| 过载模式 | 玩家点选的地面点 | 半径 20 尺 | Ally | **不是单体**，已是范围；3 轮；额外攻击（非 haste 通道，可与加速术叠加）+ 攻击/AC/反射/速度。 |

注：设计文本对这 4 个写的是"一名友军/级，彼此间距不超过 30 尺"，与"点选中心 + 爆发"不完全一致；按 C1 保持现状，等作者进游戏实测后再定是否改。

---

## 单独栏 3：新发现（只记录，不要改）

1. **顶栏来源**：提示框顶部的目标/范围/持续时间栏读取的是蓝图上的 `LocalizedDuration` / `LocalizedSavingThrow`（本 mod 在 `SpellsSetup.CreateSpell` 用 `ability.SetLocalizedDuration(...)`、`SetLocalizedSavingThrow(...)` 写入，键为 `rhodesdoctor.duration.*`）。这部分是"设计值"文本，与代码里写死的轮数可能对不上（X5）。本轮未逐一替换。
2. **`SpecterSpellRestriction` 组件已不再被引用**（B19 换成 `SpecterCasterRestriction`），保留在源码中备用，未删除。
3. **`Clone.cs` 仍无调用方**；`Guids.SpellcastingFeature` 注释"保留位"但仍被 `ArchetypeSetup.CreateSpellcastingFeature` 用作 `AddSpellbook` 载体（X1 相关）。
4. **`ContextActionRemoveMyBuffs`（集火换目标）** 依赖 `buff.Context?.MaybeCaster == caster`；若集火标记由脚本/AI 施加而不带 caster，旧标记可能清不掉（未改）。
5. **`ArtilleryBarrageTracker` 的每轮爆炸**用"骰数减半"近似反射减半；`ContextActionArtilleryStrike` 在追踪增益缺失时会 `return`，因此"施法点爆炸"若在无追踪增益时不会发生——本次未改（C 节未把炮火延伸列入必改）。
6. **`ApplySpecial` 里 `CrownEcho` 的"先夺取后驱散"顺序**在 B6 后仍成立，但夺取动作现在会跳过减益；若敌人只有减益，夺取为空、只驱散，符合预期。
7. **区域效果与 `TargetsAround` 的选择**：本轮为避免"同一动作按目标数重复执行"，把 `SpawnAreaEffect` 类法术从 `TargetsAround` 名单排除；如后续有人给这些法术加 `TargetsAround`，会重复生成区域。

---

## 编译与打包

- 源码编译通过（0 警告 0 错误），见页首。
- 本报告与改动源码、设计文档、`LocalizedStrings.json` 一并打包上传（见压缩包）。
