# 第五轮回报（report-round5.md）

- 执行者：本机 AI
- 依据：`rhodes-doctor-local-ai-handoff-round5.md`（冲突以本文件为准）；设计文档指 `docs/rhodes-doctor-archetype.txt` 与 `docs/rhodes-doctor-en.txt`（本轮已按 G14/T41–T45 改）。
- 游戏本体：`D:\steam\steamapps\common\Pathfinder Second Adventure\Wrath_Data\Managed\Assembly-CSharp.dll`（ilspycmd 9.0）；原版蓝图 `blueprints.zip`。
- 编译：`dotnet build -p:WrathPath="D:\steam\steamapps\common\Pathfinder Second Adventure"` 通过，0 警告 0 错误。
- 行号：交接行号对应第四轮交回版，下述位置按内容/方法名定位。
- 第 7 节 X 项未动；本文件没提到的东西未改（除 en.txt 同义同步，见各行备注）。

## 主表

| 编号 | 结果 | 改动位置 | 证据 / 改后代码片段或原文 | 备注 |
|---|---|---|---|---|
| N17 | 已改 | `SpellsSetup.cs`（`RoyalCharge` 分支） | 改后：`ability.AddAbilityCustomTeleportation();` 保留在外；`effects.Conditional(IsEnemy(), ActionsBuilder.New().MeleeAttack().SavingThrow(Reflex, …DealDamage 4d6 轰击 + 倒地/半伤…))`。即传送保留，近战攻击 + 伤害倒地整段包进 `IsEnemy` | 冲锋对队友不产生任何效果。攻击一次会不会变每敌各打一次仍属 X7，未动 |
| E9 | 做不到（建议转实测） | 无代码改动；证据 `evidence/E9-SpellDescriptorImmunity.txt` | `SpellImmunityToSpellDescriptor.OnTurnOn → UnitPartSpellResistance.AddImmunity(SpellDescriptor)`；`UnitPartSpellResistance.IsImmune/CanApply(SpellDescriptor)` 按上下文描述符求交；`RuleSpellResistanceCheck.TargetIsImmune/IsSpellResisted` 为真；`AbilityExecutionProcess.DoApplyEffect` 中 `flag=RuleSpellResistanceCheck.IsSpellResisted; if(!flag){applyEffect.Apply(...)}`——拦的是整个法术，该目标的豁免+沉眠+恍惚一行都不执行。`ignoreParentContext` 在下游 `BuffCollection→RuleCanApplyBuff` 才生效，到不了。阴性：`RuleCastSpell` 无免疫分支，`RuleSavingThrow` 无 Immunity 引用 | 醉梦上下文必含 Sleep（`SpellsSetup.cs:163 Sleep\|MindAffecting`）。同一法术内任何单独效果共享同一上下文，同样被投递门拦掉；拆到上下文之外等于新建独立能力，属替代做法，按规则停下。建议实测找精灵目标一次，日志应显示被抵抗且无豁免无恍惚 |
| E10 | 已核对（不用改） | 无代码改动；证据 `evidence/E10-MindAffecting.txt` | 原版 Daze 增益 `d2e35b870e4ac574d9873b36402487e5` 自带 `SpellDescriptorComponent 16842832 = MA\|Compulsion\|Daze\|Stun`；`ContextActionApplyBuff.RunAction: parentContext=(IgnoreParentContext?null:ctx)` + `UnitHelper.AddBuff(parent==null→以目标为caster重建)` + `MechanicsContext: SpellDescriptor=Parent\|Blueprint.Descriptor`——新上下文仍含 MA，只丢 Sleep。`BuffDescriptorImmunity.OnEventAboutToTrigger(RuleCanApplyBuff)` 按上下文求交；亡灵 `UndeadImmunities`、MA 免疫 `ImmunityToMindAffecting(32848=MA\|Compulsion\|Charm)`、构装体经 `ConstructImmunities→ImmunityToMindAffecting` 双门命中 | 设计 206–207 醉梦属影响心灵为准：睡眠免疫者沉眠被挡恍惚照常；MA 免疫者投递门（Sleep\|MA）与挂增益门（MA\|…）双双挡住恍惚。符合设计，不改 |
| G14 | 已改（文字；代码效果不变） | 中文设计文档 2 处；`LocalizedStrings.json` 中英文；`SpellsSetup.cs` Spec 内嵌中英文 + 注释；`docs/rhodes-doctor-en.txt` 同义同步；顶栏核对见 T47 | 中文改为“中距内选一点为中心，15 尺爆发”。设计第 171 行改后：`【闪光弹（灰烬）】 学派：塑能（光）；施法时间：标准动作；距离：中距；区域：中距内选一点为中心，15 尺爆发；持续时间：1 轮/级；…`；第 7 版备注改后：`…；已改为 15 尺爆发，文字已同步。`；JSON 改后 zh：`学派：塑能（光）；标准动作；中距内选一点为中心，15 尺爆发；反射部分通过。反射豁免失败的敌人目盲 1 轮，之后目眩至持续时间结束；…`；en：`Evocation (light); standard; a 15-ft. burst centered on a point within medium range; Reflex partial. …`；Spec 同上；代码 `Range=Medium, AoERadius=15` 未动 | 施法距离代码确为中距（`SpellsSetup.cs:312 Range=Medium`），故用“中距”。en.txt 同步不在交接清单内，为保持一致附带同步，特此说明 |
| T41 | 已改 | `LocalizedStrings.json`（`spell.DelayedBlast.desc` zh） | 改后 zh：`在地面放置一枚炸弹：每轮开始时，炸弹 10 尺内有敌人就爆炸，在 10 尺爆发内造成 2d6 点火焰与 2d6 点轰击伤害；你也可以用自由动作手动引爆。——W`（删掉`若自然触发，爆炸延迟至下轮开始。`） | 设计 124–125 与代码内嵌（Spec :267 与引爆能力 :1194）本来就没有这句，已核对一致，未动；英文无此句 |
| T42 | 已改 | 中文设计文档；`LocalizedStrings.json` 中英文；`SpellsSetup.cs` 内嵌中英文 | 设计 292 行 `区域内友军完全恢复生命` → `30 尺内的友军完全恢复生命`；JSON/内嵌 zh `全队完全恢复` → `30 尺内的友军完全恢复`；EN `Full heal` → `Allies within 30 ft. are fully healed`（`[Merge only] … 30-ft. burst on you. Allies within 30 ft. are fully healed, cleansed …`） | 代码范围确为 30 尺（`Civilization Spec AoERadius=30, CenterOnCaster=true`，`ContextActionMassRestore radius 30`），故用 30 尺 |
| T43 | 已改 | `LocalizedStrings.json` en；`SpellsSetup.cs` 内嵌 en | EN 加：`Targets immune to death effects count as having succeeded.` 改后 EN：`Necromancy; standard; medium; one enemy; Fortitude partial; SR no. Failure: death; success: 10d6 + caster level damage. Targets immune to death effects count as having succeeded. (Amiya)` | 中文与设计 267 行本来就有该句，已核对；en.txt :488 本来就有该句 |
| T44 | 已改 | 中文设计文档 5 处；`LocalizedStrings.json` 中英文 5 处；`SpellsSetup.cs` 内嵌 2 处；`docs/rhodes-doctor-en.txt` 5 处同义同步 | 统一为 zh `近距内选一点为中心，30 尺爆发内全部友军`；en `all allies in a 30-ft. burst centered on a point within close range`。设计 156/250/256/259/294、JSON Sanctuary/TidalVigil/HarvestYears/BannerStands/EternalGaze、代码 BannerStands/EternalGaze（原`半径 30 尺内友军`/`allies in a 30-ft. burst`）均已改；代码 TidalVigil/HarvestYears/Sanctuary 本来就对，未动 | 代码范围均为近距（Spec 默认 `Close`，AoERadius 30），与文字一致。en.txt 同步为保持一致附带改，特此说明 |
| T45 | 已改 | `SpellsSetup.cs` 火山注释；`Actions.cs` 黑蛇注释；`SpellsSetup.cs` + `Actions.cs` 重构/文明注释 | 火山改后：`余焰按等级算：每 4 级 1d6，最多 5d6（N11 已配等级；T45 更新，旧“首伤一半”写法误导）。`；黑蛇改后：`G6 已做：照原版焰击术，一半火焰 + 一半神圣（各带 half；火免仅减半），成败都挂本灼烧（T45 更新）。`；重构/文明两处改后：`不限阵亡时间（作者已确认，T45 更新）。` | 仅注释，行为未动 |
| T46 | 已改 | 见单独栏 2 | 重写 8 行 + 本轮改动的王者冲锋/圣域行 | 本轮行为改动仅 N17；其余为文字同步 |
| T47 | 已核对（顶栏无需改代码） | `SpellsSetup.cs`（`SetRange` + `SetLocalizedDuration/SavingThrow`） | 顶栏范围来自 `SetRange(s.Range)`（游戏按 AbilityRange 本地化），持续/豁免来自 `SetLocalizedDuration(meta.DurationKey)/SetLocalizedSavingThrow`。本轮改动的范围/持续与代码一致：闪光弹 Medium + round1level；潮汐 minute1level、岁稔 round3、旗帜 round1level、永恒 minute1level、圣域 round3、延时 round1level、文明 instant、拔刀 instant、冲锋 instant——Meta 与 Spec 均未变，无需改键 | X5 留实测（实际显示什么以游戏为准） |

## 单独栏 1：做不到

| 编号 | 卡在哪里 | 查过哪些源码 | 备注 |
|---|---|---|---|
| E9 | `SpellImmunityToSpellDescriptor(Sleep)` 在 `AbilityExecutionProcess.DoApplyEffect` 经 `RuleSpellResistanceCheck` 拦下整个法术；同一法术内任何 `ignoreParentContext` 的恍惚同样到不了执行点。拆到另一独立能力才行，属替代做法，按规则停下 | `SpellImmunityToSpellDescriptor.OnTurnOn/Off`、`UnitPartSpellResistance.IsImmune/CanApply`、`RuleSpellResistanceCheck.OnTrigger/IsSpellResisted`、`AbilityExecutionProcess.DoApplyEffect`、`ContextActionApplyBuff.RunAction`、`RuleCastSpell.OnTrigger`（无免疫）、`RuleSavingThrow`（无 Immunity）、`MechanicsContext` 构造；`SpellsSetup.cs:163` 醉梦描述符；`ElvenImmunities.jbp` | 建议转实测：精灵目标一次，确认被抵抗且无恍惚 |
| E10 | 无（已查清，不用改） | 见主表 E10 | — |

需作者决定：无。本轮未发现设计文档自相矛盾。

## 单独栏 2：对照表更新（只重写本轮有改动的 + T46 列出的上一轮误标一致行）

| 法术 | 代码实际效果 | 设计 | 中文 | 英文 | 内嵌 |
|---|---|---|---|---|---|
| 王者冲锋 | 标准恶灵，个人，直线冲锋至多两倍速+近战一次（仅敌人），路径敌人 4d6 轰（成功免倒地不减伤） | 一致 | 一致 | 一致 | 一致（本轮近战进 IsEnemy） |
| 闪光弹 | 标准，中距内选一点为中心 15 尺爆发，反射部分，失败目盲 1 轮后目眩至结束，成功目眩 1 轮，失败隐形显形 1 轮 | 一致（本轮 G14 改） | 一致（本轮改） | 一致（本轮改） | 一致（本轮改） |
| 延时爆破 | 标准，中距，炸弹 1 轮/级，每轮开始 10 尺内有敌爆炸（2d6 火+2d6 轰），自由手动引爆 | 一致 | 一致（本轮删重复句） | 一致 | 一致 |
| 文明的存续 | 标准，以己为中心 30 尺，30 尺内友军完全恢复+解咒病外一切负面+复活 5 名全血，后 1 分钟 +4 与一次性免死 | 一致（本轮改 30 尺内友军） | 一致（本轮改） | 一致（本轮改） | 一致（本轮改） |
| 影霄·拔刀 | 标准，中距单敌，强韧部分，失败即死，成功 10d6+等级，即死免疫视成功 | 一致 | 一致 | 一致（本轮补免疫句） | 一致（本轮补免疫句） |
| 潮汐守望 | 标准，近距内选一点为中心 30 尺爆发内全部友军，1 分钟/级，每轮 15 临时，免魅惑强迫 | 一致（本轮改） | 一致（本轮改） | 一致（本轮改） | 一致（本就对） |
| 岁稔 | 标准，近距内选一点为中心 30 尺爆发内全部友军，3 轮，每轮 4d8+等级，余转临时至多 60（1 分钟） | 一致（本轮改） | 一致（本轮改） | 一致（本轮改） | 一致（本就对） |
| 旗帜不倒 | 迅捷，近距内选一点为中心 30 尺爆发内全部友军，1 轮/级，0 血不死不昏正常行动，不挡即死 | 一致（本轮改） | 一致（本轮改） | 一致（本轮改） | 一致（本轮改） |
| 永恒的注视 | 标准，近距内选一点为中心 30 尺爆发内全部友军，1 分钟/级，将死回满+传身边+1 分钟加速，各一次 | 一致（本轮改） | 一致（本轮改） | 一致（本轮改） | 一致（本轮改） |
| 圣域 | 迅捷，近距内选一点为中心 30 尺爆发内全部友军，3 轮，抗法术豁免 +4 神圣，五抗 10 可叠 | 一致（本轮改） | 一致（本轮改） | 一致（本轮改） | 一致（本就对） |

其余法术沿用第四轮对照表，未变。

## 单独栏 3：新发现（只记录，不改）

1. `LocalizedStrings.json` 闪光弹英文缺隐形显形句（中文/设计/en.txt 均有该句，JSON EN 无）。属本文件没提到的不一致，只记录，未改。
2. `en.txt` 与 JSON/内嵌的同义同步（G14 闪光弹、T44 五法术）本轮附带改了 en.txt；若审阅方要求 en.txt 冻结，可回退 en.txt，本轮 JSON/代码/中文不受影响。
3. `IgnoreParentContext` 的恍惚仍带 MA（E10），驱散沉眠不会连带驱散恍惚（沿用第四轮新发现 3），行为符合“醒来恍惚”语义。
