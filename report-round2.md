# 罗德岛博士第二轮回报（report-round2.md）

- 执行者：本机 AI
- 依据：`rhodes-doctor-local-ai-handoff-round2.md`（下称“本轮交接”）；冲突处以本轮交接为准；设计文档指 `docs/rhodes-doctor-archetype.txt` 第 10 版（含本轮 T16 一处改动）。
- 游戏本体：`D:\steam\steamapps\common\Pathfinder Second Adventure\Wrath_Data\Managed\Assembly-CSharp.dll`（ilspycmd 9.0）；BlueprintCore（WW-Blueprint-Core）2.8.7。
- 编译：`dotnet build -p:WrathPath="D:\steam\steamapps\common\Pathfinder Second Adventure"` 通过，0 警告 0 错误。
- 行号：本轮交接行号对应审计 zip 旧版，下述位置按内容/方法名定位。

## 主表（每个编号一行；“已改”均附关键代码）

| 编号 | 结果 | 改动位置 | 证据 / 改后代码片段 | 备注 |
|---|---|---|---|---|
| R1 | 已改 | `Components.cs` 全组 + `Actions.cs` 调用方 | 见“R1 清单”。例：`public class ExecutionStrike : UnitBuffComponentDelegate<ExecutionStrikeData>`；`public class ExecutionStrikeData { [JsonProperty] public bool Armed; }`；handler 内 `if (Data.Armed && !evt.IsHit) Data.Armed = false;`。依据见 `evidence/R1-Data.txt`（Delegate 共用 vs Runtime 按效果隔离；原版 `TemporaryHitPointsPerLevel : UnitFactComponentDelegate<TemporaryHitPointsPerLevelData>`，`base.Data.Modifier`）。 | 8 处逐一见清单；另全模组复查结论见清单末。 |
| R2 | 已改 | `Guids.cs`（新增 `LockBuffV2`）、`SpecterSetup.cs`（`CreateLocks`/`CreateStage`/`CreateCapstone`）、`ArchetypeSetup.cs`（集火拦截、精通、冷静头脑）、`SpellsSetup.cs`（`SpecterCasterRestriction`） | `string lockGuid = stage <= 1 ? Guids.LockBuff : stage == 2 ? Guids.LockBuffV2 : Guids.LockBuffV3;`；集火 `Facts = [LockBuff, LockBuffV2]`；精通新增 `LockFull2`，`if (Full \|\| Full2) 全额 else if (Halved) 半额`；施法限制含三锁；20 级压制 `V2+V3`。设计文档第 56 行“集火在 16 级起允许使用”与转述一致，无出入。 | 二段（12–15）全额倒扣不变（B11），一段/二段禁集火、三段放开。 |
| R3 | 已改（乙：动作内扫描） | `Actions.cs`（新增 `ContextActionArtilleryInitialBurst`、`ContextActionCatastropheRound`）、`SpellsSetup.cs`（炮火首炸改扫描；天灾改 `Special`+80 尺区域 Round；延时改 `Special`+追踪） | 炮火：`effects.Add<ContextActionArtilleryInitialBurst>();`（15 尺扫描 8d6+8d6，反射按骰数减半）；天灾：`AddAbilityAreaEffectRunAction(round: …Add<ContextActionCatastropheRound>())` + `SpawnAreaEffect(CatastropheArea, Fixed(1, Minutes))`；延时见 C33。 | 与 C4/C33 最终做法一致；三者均不挂 `TargetsAround`（区域/追踪自扫，避免重复执行）。 |
| R4 | 已改 | `Actions.cs`（`IsStealable` + 新增 `ContextActionCrownEchoSuccessRemove`、`ContextActionCrownEchoPurgeRemaining`）、`SpellsSetup.cs`（`CrownEcho` 分支重写） | `IsStealable = !Harmful && !IsNotDispelable && !HiddenInInspector && IsFromSpell && src!=null && src.IsEnemy(caster)`，依据见 `evidence/R4-Dispel.txt`（`TryDispelBuff` 同款四条）。失败：`Steal(Max2) + PurgeRemaining + ApplyBuff(Daze, Fixed(1))`；成功：`SuccessRemove`（`UnityEngine.Random.Range` 随机一个，直接 `Remove()`）。 | 与设计文档一处出入见备注：设计第 298 行成功者写“持续时间最长的一个”，本轮交接 R4/T18 写“随机”，游戏内文字已按“随机”写（T18），代码按随机实现。 |
| R5 | 已改 | `Components.cs`（新增 `SuppressingShotDebuff`）、`SpellsSetup.cs`（`SuppressingShot` 去 `Bonuses`、增益挂组件）、`LocalizedStrings.json`（T15 改回原文） | `IInitiatorRulebookHandler<RuleCalculateAttackBonus>：int penalty = (evt.Target==caster) ? -1 : -2; evt.AddModifier(penalty, Fact, UntypedStackable);` + `DidTrigger(RuleAttackRoll) => Remove()`（仅下一次）。`RuleCalculateAttackBonus : RulebookTargetEvent`（Initiator=攻击者，Target=被击者），故可按攻击对象区分。 | 无“做不到”；中英文已改回设计第 81 行原文（T15）。 |
| R6 | 已改 | `Actions.cs`（`ContextActionMassRestore` 活体/死者/后续三段） | 活体：`if (!IsPlayerFaction) continue;`（不再排除召唤物/宠物）；死者：`if (Get<UnitPartSummonedMonster>() != null) continue;` 后复活；后续：`if (!IsPlayerFaction) continue;`（与复活用同一套友军标准）。依据见 `evidence/E1-Master.txt`（`Master` 只看 `UnitPartPet`；召唤物走 `UnitPartSummonedMonster`）。 | E1 若动物伙伴算有主人，则不能用 Master 排召唤物——已改用 `SummonedMonster` 判，宠物不再误伤。 |
| R7a | 已改 | `SpellsSetup.cs`（标记改 `newRound`）、`Actions.cs`（`ContextActionFinalSnipe` 结算后移除，隐形哑火亦移除） | `cfg.AddFactContextActions(newRound: …Add<ContextActionFinalSnipe>())`；动作内 `RemoveMark(target)`（`b.Context == Context` 者删）。被驱散则标记提前消失，`newRound` 不触发。 | 掩蔽判断本就无（代码只查两种隐形），D4 合规。 |
| R7b/T19 | 已改 | `LocalizedStrings.json` 第 143 行 | `enGB` 尾部 `with同类 (Nian)` → `with similar effects. (Nian)`。 | — |
| R7c/T20 | 乙：确认脚本以后不会覆盖 | 不改代码；依据见备注 | `L10n.S` 直接 `return "rhodesdoctor." + key`（`L10n.cs`），运行时 `zh/en` 参数根本不用；游戏内文字只读 `LocalizedStrings.json`（`Content.LoadLocalizationPack`）；仓库内无 `gen-loc.ps1`（`glob **/*.ps1` 空），本次也未新增生成脚本。 | 选乙。代码内嵌旧原文（如战场测绘“方向”、信标“隐形失效”、岁稔“60+等级”等）仅为人看的对照注释，不进游戏；以后若重跑外部抓取脚本，需先同步代码，见新发现。 |
| R7d | 已改 | `Components.cs`（`BreachStrike`/`EternalGazeWatch` 注释） | Breach 注释改为“D2 单独组件，不引发借机攻击见 D2”并注明 Data；Eternal 注释改为“回满+传送+ C34 加速”，删“2 米偏移/额外标准动作”旧字样。 | — |
| D1 | 接受现状，无需改 | — | 过载/迅捷加速/加速术：速度不具类型 vs 加速术增强，额外攻击非 haste 通道 vs 加速术 haste 通道（V14 已证可叠加，每轮最多多 3 次）。作者接受，不加限制。 | — |
| D2 | 已改 | `SpellsSetup.cs`（`SwiftBreach` 增益加条件） | `cfg.AddComponent<BreachStrike>(); cfg.AddComponent<AddCondition>(c => c.Condition == UnitCondition.ImmuneToAttackOfOpportunity);`（`UnitCondition` 反编译确认有该条件，见 `evidence/D2-R7a-C30.txt`）。与 Breach 同一增益、1 轮、命中后随增移除，只管本轮。游戏内文字保留“不引发借机攻击”（T17）。 | — |
| D3 | 已改（含 R4） | 同 R4 成功分支 | 成功者随机一个直接 `Remove()`，不分给友军。 | — |
| D4 | 核对一致，无需加强 | `Actions.cs`（终末狙击无掩蔽判断）、设计文档、JSON | 代码只查隐形（两种原版隐形），无掩蔽/超射程判断；设计文档已删“掩蔽、超射程则失效”只留隐形（T16）；JSON 本就是“隐形则失效”。删掉的是失效条件，只会更稳定，不找补。 | — |
| C4 | 已改（简化） | `SpellsSetup.cs`（天灾改整轮+区域）+ `Actions.cs`（`CatastropheRound`） | `SetIsFullRoundAction(true)`；80 尺区域 1 分钟，Round 扫描施法点 80 尺：`dice = passed?6:12`，`12d6火+12d6轰+12d6电`，每敌人每轮一次（Round 对每单位一次天然满足）。 | 偏差：规则“至多六个 10 尺点由你选”简化为整区域伤害；“困难地形”无接口未做（见文字不符栏）。持续 1 分钟/三种伤害/整轮/每轮一次已做。 |
| C6 | 未完成 | — | 紧急救治仍为近似圣疗骰（`DicePerStep1/Step2/Cap5`），未接 6 个慈悲链。卡点：需把圣武士慈悲特性树（`ContextConditionCasterHasFact` 条件链）接入本变体治疗动作，逐个慈悲映射解除条件，本轮未接线。查过：`LayOnHandsOthers` 慈悲条件树、原版慈悲特性。 | 列入文字不符栏。 |
| C8 | 已改 | `SpellsSetup.cs`（剑雨转 `Special`） | `SavingThrow Reflex + ConditionalSaved(failed: DealDamage( Rank d6 挥砍 halfIfSaved) + ApplyBuff(Daze d2e35b87…, Fixed(1)), succeed: DealDamage)`，`DiceRank` 按每 2 级 1d6 上限 10d6。 | — |
| C10 | 未完成 | — | 灼烧仍纯伤害，未加“火焰抗力 −10 / 1 轮/级”。卡点：`AddDamageResistanceEnergy` 负值是否真降敌方抗力未经证（引擎可能钳制），无把握不臆造。查过：`AddDamageResistanceEnergy`、游戏 `AddDamageResistance*` 族。 | 列入文字不符栏。 |
| C11 | 未完成 | — | 火山仍单次伤害，未加 2 轮余焰（每 4 级 1d6 上限 5d6、每次单独豁免）。通用做法（失败挂余焰增益 + newRound 单独豁免伤害）本轮排期不足，未接线。 | 列入文字不符栏。 |
| C12 | 已改 | `SpellsSetup.cs`（梦魇转 `Special`，新增两增益 `8c/8d`） | 失败：`ApplyBuff(Panic(Frightened), Fixed(2))`，其 `deactivated: ApplyBuff(Shaken, Variable(Rank))`（Eyebite 同款接力）；成功：`ApplyBuff(Shaken, Fixed(1))`。`Rank=BuffRank(l=>l)`（1 轮/级）。恐慌实现用 `Frightened` 条件（本体无 `Panicked` 枚举，Eyebite 恐慌亦用 `Frightened`，见回报说明）。 | — |
| C13 | 未完成 | — | 闪光弹仍单段目盲，未做目盲→目眩两段与隐形显形。卡点：需新增目眩接力 + 移除隐形动作（`RemoveBlindness` 同款判 `Invisible/Greater`），本轮未接线。 | 列入文字不符栏。 |
| C15 | 未完成 | — | 冰封领域仍 4d6 寒冷单次，未做困难地形/首入伤害/定身与减速。卡点：困难地形无接口（`BlueprintAbilityAreaEffect` 无地形字段）；定身/减速需区域进场+回合双触发，本轮未接。查过：`BlueprintAbilityAreaEffect` 字段、`AddAbilityAreaEffectRunAction`。 | 列入文字不符栏。 |
| C16 | 已改 | `Actions.cs`（新增 `ContextActionRemoveMon3trConditions`）、`SpellsSetup.cs`（Mon3tr 转 `Special`） | `HealTarget(Rank d6)` + `Add<RemoveMon3tr>`；移除判 `AddCondition/BuffStatusCondition` 含 `Stunned/Dazed/Paralyzed/Nauseated`（V11 同款双认）。 | — |
| C17 | 未完成 | — | 黑蛇之焰仍单次，未做每轮 3d6/半抗/免疫半伤。卡点：持续灼烧需增益 + 忽略半抗需自定义伤害（无“忽略一半抗力”字段），未接。 | 列入文字不符栏。 |
| C18后半 | 未完成 | 醉梦持续已 1 轮/级（旧），后半未做 | “受伤醒来恍惚、免疫睡眠仍恍惚”需受伤监听 + 睡眠免疫分支，未接。 | 列入文字不符栏。 |
| C19 | 未完成 | — | 黄昏仍单次，未加熔火 2×4d6（每次反射减半）。同 C11，需余焰增益，本轮未接。 | 列入文字不符栏。 |
| C20 | 已改 | `SpellsSetup.cs`（雷霆转 `Special`） | 失败：`DealDamage(12d6电 halfIfSaved) + ApplyBuffPermanentFixed(倒地 24cf3deb…) + ApplyBuff(Daze, Fixed(1))`；成功：半伤不倒地。`DiceRank` 恒 12d6。 | — |
| C21 | 未完成 | — | 大地之怒仍纯伤害，未加倒地/困难地形；召唤物双倍未做。倒地本身易做但与地形同案未合入（本轮优先 R/C 高优，已无余量验证 AoE60 改 Special 的回归）。 | 列入文字不符栏。 |
| C22 | 未完成 | — | 穿云仍普通直线伤害，未加忽略 DR。卡点：直线 `DealDamage` 无“忽略 DR”开关；`IgnoreDamageReduction` 是攻击方增益，需给施法者挂临时增益并保证只作用于本次伤害，未稳妥接线。查过：`RuleDealDamage.IgnoreDamageReduction`、`IgnoreDamageReduction` 组件。 | 列入文字不符栏。 |
| C26 | 未完成 | — | 钙质化仍 `Slowed` 近似，未换“速度减半”。卡点：精确半速无“乘 0.5”字段（`BuffMovementSpeed` 未找到乘子语义），负值速度加成非精确半速，不臆造。 | 游戏内文字已写半速，列入不符栏。 |
| C27 | 未完成 | — | 儿时舞乐敌方未加意志豁免（区域直接挂缓慢易伤）。卡点：`AbilityAreaEffectBuff` 进场无豁免位；改影之疆域式“增益内 save”需重做两增益，本轮未接。 | 列入文字不符栏。 |
| C30 | 已改 | `Components.cs`（新增 `CivilizationDeathGuard<Data>`）、`SpellsSetup.cs`（后续增益换组件） | `HandleDeadlyDamage：if (!Used) { Used=true; Damage = MaxHP-1; }`，`Data.Used` 按效果隔离（R1）。替换 `UnitHealthGuard`（可反复保命）。 | — |
| C33 | 已改（简化） | `Components.cs`（新增 `DelayedBlastTracker<Data>`）、`Actions.cs`（`DelayedBlastBurst` 改独立记录循环 + 新增 `DelayedBlastRound` 近炸检查）、`SpellsSetup.cs`（延时转 `Special` + 追踪 1 轮/级 + 手动引爆自由动作能力 `88`） | 追踪记点+DC（`OnActivate` 写 `Data`）；`Round` 仅 10 尺有敌才爆（无敌保留）；手动能力 `Free + Personal + Add<Burst>` 引爆全部自设炸弹；伤害 `2d6火+2d6轰` 反射按骰数减半。旧炮火记录回退兼容保留。 | 偏差：“放地上实体/相邻格”简化为施法点 10 尺；“敌人靠近触发”简化为下轮 Round 检查；多炸弹各有独立 Data（R1）。 |
| C37 | 已改（含 R4） | 同 R4 | 失败直接失去全部（`Steal2 + Purge`，无检定）+ 恍惚 1 轮（原版 Daze）。 | — |
| C38 | 已改（近似） | `Actions.cs`（`MassRestore` 解除分支未动，复核） | 现逻辑：有害且非 `Curse/Disease/Poison` 描述符者移除（含恍惚/震慑等战斗状态）；剧情诅咒多为 `Curse` 描述符，被排除。`Restoration` 可解表更窄，引擎无“复原术可解”名单接口，此近似保留。 | 近似，见备注。 |
| 6.3顶栏 | 已核对，来源写明，未逐一改键 | `SpellsSetup.cs`（`SetLocalizedDuration/SavingThrow`） | 来源：顶栏读蓝图 `LocalizedDuration/LocalizedSavingThrow`，本 mod 在 `CreateSpell` 用 `SetLocalizedDuration(meta.DurationKey)`（`rhodesdoctor.duration.*`）与 `SetLocalizedSavingThrow(meta.Save)` 写入。本轮改动的持续/豁免头与 Meta 一致：天灾整轮+1 分钟（`minute1`+`SetIsFullRoundAction`）、延时 1 轮/级（`round1level`）、剑雨/雷霆立即（`instant`）、梦魇 1 轮/级（`round1level`）、Mon3tr 立即（`instant`）。无需逐一替换。 | X5 相关，留实测。 |
| T15 | 已改 | `LocalizedStrings.json` 牵制射击 | 改回设计第 81 行原文（`−1 … 攻击除你以外 … −2`），撤销“下一次 −2”。 | — |
| T16 | 已改 | 设计文档第 229 行 + 核对 JSON/代码 | 设计删“掩蔽、超射程”，只留隐形；JSON 本就只写隐形；代码只查隐形。 | D4 不找补。 |
| T17 | 已核对 | 设计 169 行/JSON/代码 | 三处均有“不引发借机攻击”，代码已实现（D2）。 | — |
| T18 | 已改 | `LocalizedStrings.json` 王冠回响 | 成功“随机失去一个可驱散的法术增益，该增益直接消失”；失败“失去所有可驱散的法术增益并恍惚 1 轮（…至多两个分给友军）”。 | 设计第 298 行成功者写“最长”，按本轮 R4/T18 用随机，见 R4 备注。 |
| T19 | 已改 | 同 R7b | — | — |
| T20 | 乙 | 见 R7c | — | — |
| T3后半 | 不改 | — | 坚守/骑兵/乌拉/过载中心点现状见专栏 4（照抄上一轮，未动）。 | — |
| E1 | 已补 | `evidence/E1-Master.txt` | `UnitEntityData.Master` 只看 `UnitPartPet`（宠物）；召唤物走 `UnitPartSummonedMonster`（`RuleSummonUnit` 设）。故 R6 改用 `SummonedMonster` 判。 | — |
| E2 | 已补 | `evidence/E2-LoH.txt` | `IncreaseByLevelStartPlusDivStep(0,0,2,1)` = `0+1*(Lv-0)/2` 向下取整 + 智力 = 半级+智力。BlueprintCore XML 原文见证据。 | 圣疗维持 V6，不用改。 |

## 单独栏 1：做不到 / 未完成（卡在哪、查过什么）

- C6 慈悲：需 6 慈悲特性树接入治疗（`LayOnHandsOthers` 条件链），本轮未接。查过原版 LoH 慈悲链。
- C10：`AddDamageResistanceEnergy` 负值语义未经证，不臆造。查过抗力族组件。
- C11/C19：余焰增益 + 每次单独豁免需新 buff+newRound，本轮排期不足。
- C13：两段目盲目眩 + 隐形显形需新移除动作，本轮未接。
- C15/C21 地形：`BlueprintAbilityAreaEffect` 无困难地形字段；C15 首入/回合双触发、C21 召唤物双倍亦未接。
- C17：持续灼烧 + “忽略一半抗力/免疫半伤”无字段，未接。
- C18 后半：受伤醒 + 睡眠免疫分支未接。
- C22：直线伤害无忽略 DR 开关；临时挂 `IgnoreDamageReduction`  scope 未稳妥验证。
- C26：精确半速无乘子字段，仍 `Slowed` 近似。
- C27：区域进场加意志豁免需重做增益为 buff 内 save，本轮未接。
- C21 倒地虽易做但与地形同案未合入（回归余量不足），与 C11/C13/C15/C17/C19/C10/C22/C26/C27/C6 同列未完成，不替代。

## 单独栏 2：R1 清单（改前字段 → 改后 Data；读档）

| 组件 | 改前（共用蓝图字段，不进存档） | 改后（每效果 Data，进存档） | 读档 |
|---|---|---|---|
| 永恒注视 `EternalGazeWatch` | `_used` | `EternalGazeWatchData.Used` | bool 存活；同次施放多人各救一次 |
| 岁稔积攒 `HarvestYearsAccumulator` | `Stored/CasterLevel` | `HarvestYearsAccumulatorData.Stored/CasterLevel` | int 存活；各友军独立累加 |
| 岁稔余温 `HarvestYearsResidual` | `Amount/_modifier` | `HarvestYearsResidualData.Amount/Modifier` | int 存活；`Modifier` 为引用，丢失最坏仅丢本次临时生命（`OnActivate` 有补加分支），不跨污染 |
| 处决 `ExecutionStrike` | `_armed` | `ExecutionStrikeData.Armed` | bool 存活 |
| 迅捷突破 `BreachStrike` | `_hitConfirmed` | `BreachStrikeData.HitConfirmed` | bool 存活 |
| 归溟 `ReturnToAbyssHunger` | `_healsThisRound/_refundedThisRound` | `ReturnToAbyssHungerData.*`，`ResetRound(Buff)` 按 `GetComponentWithRuntime` 取 Data | int 存活 |
| 罪罚 `SinPunishmentRetribution` | `_triggeredThisRound` | `SinPunishmentRetributionData.TriggeredThisRound`，同上静态重置 | bool 存活 |
| 炮火 `ArtilleryBarrageTracker` | `Point/SaveDC` | `ArtilleryBarrageTrackerData.Point/SaveDC` | Vector3+int 存活；读档不再变原点/0 |
| 延时 `DelayedBlastTracker`（新增独立） | 原借用炮火字段 | `DelayedBlastTrackerData.Point/SaveDC` | 同上；双法术不再读错 |
| 赤霄 `ChiXiaoStrike` | `_attacked` | `ChiXiaoStrikeData.Attacked` | 孤儿组件（无引用），仅修结构 |
| 文明免死 `CivilizationDeathGuard`（新增） | 原 `UnitHealthGuard` 可反复 | `CivilizationDeathGuardData.Used` | bool 存活 |

全模组复查：其余组件字段均为配置（加值/引用/开关），无运行时可变状态；`ContextAction*` 均为无字段动作（`MaxStolen/Amount/Resource` 为配置），`HarvestTick/ArtilleryStrike/DelayedBlast*` 已改走 `GetComponentWithRuntime(...).GetData<...>()`。`SpecterSpellRestriction` 已无人引用（B19 遗留），未删。

## 单独栏 3：游戏内文字与实际效果不符（本轮结束仍对不上；未改文字迎合代码）

- C6：紧急救治文字“可附加慈悲”实际未附加。
- C10：灼烧文字“抗力 −10”实际无。
- C11：火山文字“余焰 2 轮…”实际单次。
- C13：闪光弹文字两段+显形实际单段目盲。
- C15：冰封文字地形/首入/定身减速实际仅 4d6。
- C17：黑蛇文字持续灼烧/半抗实际单次。
- C18：醉梦文字受伤醒/免疫仍恍惚实际无。
- C19：黄昏文字熔火实际单次。
- C21：大地文字倒地/地形实际纯伤害。
- C22：穿云文字“忽略 DR 与掩蔽”实际未忽略（掩蔽按 V16 早删？此处指 DR）。
- C26：钙质化文字半速实际 `Slowed` 近似。
- C27：舞乐文字“意志无效”实际敌方无豁免。
- C4 困难地形：天灾文字“困难地形”实际无。
- C33 相邻格/实体：延时文字相邻格/地面实体实际为施法点 10 尺。
- C4 六点自选：天灾文字隐含六点自选（旧文）现按整区域，JSON 已改整区域，无残留。
- 其余本轮已对上（剑雨/雷霆/梦魇/Mon3tr/文明一次/延时双爆/王冠/牵制/终末隐形）。

## 单独栏 4：坚守阵地、骑兵突进、乌拉、过载模式中心点与范围现状（照抄上一轮，未动）

| 法术 | 中心点 | 范围 | 目标类型 | 说明 |
|---|---|---|---|---|
| 坚守阵地 | 玩家点选地面点 | 半径 30 尺 | Ally | 近距，迅捷，3 轮；`TargetsAround` 作用点心 30 尺友军 |
| 骑兵突进 | 玩家点选地面点 | 半径 30 尺 | Ally | 同上；速度 +10/+20 不具类型、借机 AC+4 |
| 乌拉 | 玩家点选地面点 | 半径 30 尺 | Ally | 1 分钟/级；攻击/伤害 +1/+2 不具类型 |
| 过载模式 | 玩家点选地面点 | 半径 20 尺 | Ally | 非单体；3 轮；额外攻击（非 haste，可叠）+攻击/AC/反射/速度 |

设计文本“一名友军/级…”与点爆发不完全一致，按 C1 保持现状待实测。

## 单独栏 5：新发现（只记录，不改）

1. T20：`L10n.S` 运行时只返回键，代码旧原文不进游戏；仓库无生成脚本，JSON 不会被代码覆盖（见 R7c）。
2. 王冠成功“随机 vs 最长”：设计 298 行写最长，本轮按随机实现（R4 备注），待作者统文档。
3. 天灾区域 Round 用施法点扫描：若区域被驱散/位移，伤害中心仍是施法点（区域固定故一致）；读档后区域重建的 `MainTarget` 恢复未经实测。
4. 延时追踪在施法者身上：施法者死亡/离场后炸弹是否保留未经实测；多炸弹手动一次全爆。
5. 终末 `newRound` 在非战斗实时模式的触发节拍未经实测（驱散作废逻辑依赖它）。
6. `ChiXiaoStrike` 无引用；`SpecterSpellRestriction` 无引用；均保留未删。
7. 梦魇恐慌用 `Frightened` 实现：本体无 `Panicked` 枚举（Eyebite 恐慌亦 `Frightened`+到期转摇），与文字“恐慌”对应关系留实测。

## 打包

- 源码 + 中/英文设计文档 + `LocalizedStrings.json` + 本回报 + `evidence/*.txt` 打 zip（≤500 文件/50MB），站内上传。
