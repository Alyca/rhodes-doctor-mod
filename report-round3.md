# 第三轮回报（report-round3.md）

- 执行者：本机 AI
- 依据：`rhodes-doctor-local-ai-handoff-round3.md`（冲突以本文件为准）；设计文档指 `docs/rhodes-doctor-archetype.txt`（本轮已按 G2/G3/G4/G5/T21-T28 改）与 `docs/rhodes-doctor-en.txt`（同步改）。
- 游戏本体：`D:\steam\steamapps\common\Pathfinder Second Adventure\Wrath_Data\Managed\Assembly-CSharp.dll`（ilspycmd 9.0）；BlueprintCore 2.8.7；原版蓝图 `blueprints.zip`。
- 编译：`dotnet build -p:WrathPath="D:\steam\steamapps\common\Pathfinder Second Adventure"` 通过，0 警告 0 错误。
- 行号：交接行号对应第二轮交回版，下述位置按内容/方法名定位。

## 主表

| 编号 | 结果 | 改动位置 | 证据 / 改后代码片段 | 备注 |
|---|---|---|---|---|
| N1 | 已改 | `Actions.cs`（`ContextActionCatastropheRound` 重写） | E3（`evidence/E3-AreaRound.txt`）：`AbilityAreaEffectRunAction.OnRound` 对 `InGameUnitsInside` 循环 `GetDataScope(item)+Round.Run()`，`Target` 即本次单位。改后只打 `Target.Unit` 且 `IsEnemy`，`dice=passed?6:12`，12d6火/轰/电 | 每个敌人每轮一次（C4 同闭环） |
| N2 | 已改 | `SpellsSetup.cs`（`DelayedBlast`：引爆能力先建，追踪增益 `AddFacts` 授予） | `cfg.AddFacts(... { Guids.DelayedBlastDetonateAbility })`；依据见 `evidence/E6-Mercy.txt` 末段（`AddFacts` 取 `BlueprintUnitFactReference`，`BlueprintAbility` 系其子类；原版 `ArmyDimentionDoorBuff` 同用 `AddFacts`；本 mod 内先例形态增益授结束恶灵能力） | 有炸弹时能力出现，炸弹清完移除 |
| N3 | 已改 | `Actions.cs`（`ContextActionDelayedBlastBurst` 删除回退块） | 删除“无独立记录回退读炮火”整段；现只读 `DelayedBlastTracker`，不碰 `ArtilleryBarrageTracker` | 炸弹是本轮新功能，无旧存档 |
| N4 | 已改 | `Components.cs`（`InklingFightsWithMasterBAB` 改读召唤者） | E4（`evidence/E4-Summoner.txt`）：`RuleSummonUnit.OnTrigger` 写 `UnitPartSummonedMonster.Init(Initiator)`；`SpawnMonster` 与镜中流形都走 `RuleSummonUnit`。改后 `Get<UnitPartSummonedMonster>()?.Summoner ?? Context?.MaybeCaster ?? Owner.Master` | X3 关闭，无需实测 |
| N5 | 已改 | `SpellsSetup.cs`（`BannerStands` Spec `AoERadius=20→30`） | `AoERadius=30`；JSON/设计已是 30 尺，代码内嵌原文同步 30 尺 | 与文字一致 |
| N6 | 已改 | `Actions.cs`（`ContextActionStealBuffsCustom` 友军筛选） | `if (u==null \|\| !u.IsPlayerFaction) continue;`（原 `IsEnemy` 会分给中立 NPC），与 R6 同标准 | 无 |
| N7 | 已改 | `Actions.cs`（`ArtilleryStrike`、`HarvestTick` 按 Context 找自己那份） | 炮火 `if (bf.Context != Context) continue;`（原取第一份）；岁稔同条件（原加进所有份）；与 `DelayedBlastRound` 同做法 | 两份施放各算各的 |
| N8a | 已改 | 文字（T24） | 见 T24 | 代码未动（G5 接受现状） |
| N8b | 已改 | `Actions.cs`（新增 `ContextActionEarthFuryHit`）+ 文字（T25） | 失败倒地（原版倒地增益 24cf3deb）；`Get<UnitPartSummonedMonster>()!=null → dice*=2`；设计/JSON/内嵌恢复召唤物双倍 | 困难地形按 G2 删 |
| N8c | 已改 | 回报本栏更正 + T21/T26 | 上轮“文字还写着困难地形”有误：本轮核对时游戏内文字已无困难地形；设计按 G2 删（T21），设计与游戏内一致（T26） | 无 |
| N8d | 做不到（强风驱散子项） | `SpellsSetup.cs`（`Smokescreen` 未动） | 查过 `BlueprintAbilityAreaEffect` 无风字段；`blueprints.zip` 未见云雾被风驱散现成组件。中文保持无该句、英文保留原句，不为一致删英文 | 列入文字不符栏 |
| N8e | 已改 | 设计文档第 298 行（T28） | 改成“成功敌人随机失去一个可驱散的法术增益，该增益直接消失”；代码与 JSON 本就如此 | 无 |
| N9 | 已改/已核对 | `SpellsSetup.cs` 头注释、`ChainDetonation` 注释、`BeaconProjection` 注释 | 头注释按实际重写；连锁改为“目标点 20 尺内全部敌人（不再是单体）”；信标改为“C32 只保留照明” | `Components.cs` R7d 两处已是新注释，核对无误 |
| N10 | 无需改（查证） | `Components.cs`（`SaveBonusVsSpells` 未动） | E5（`evidence/E5-IsSpell.txt`）：`IsSpell` 在 `Type==Spell` 或 `SpellLike` 时为真，已含类法术能力 | 设计范围已覆盖 |
| G2 | 已改 | 设计（冰封/大地/天灾删困难地形）+ JSON（冰封/大地删）+ 内嵌原文（冰封/大地/天灾删）+ Meta（`EarthFury` 持续改 `instant`） | 设计/JSON/内嵌 `grep` 均无困难地形；代码从未实现地形 | 其余效果不变 |
| G3 | 已改 | 设计/JSON/内嵌（炮火“三炮击点”改“同一点连续三轮”） | 代码保持现状（同一施法点三轮），文字同步（T22） | 无 |
| G4 | 已改 | JSON/内嵌/设计（迅捷突破按实际写法，T23） | “本轮内直到你下一次近战命中为止，你不引发借机攻击（包括移动）”，其余按加速 3 轮；代码未动 | 无 |
| G5 | 已改 | JSON/设计（重构按实际写法，T24） | “只复活友军（召唤物不复活，其余效果照常）；解除除诅咒、疾病、中毒以外的所有负面效果”；代码未动 | 无 |

| C11 | 已改 | `SpellsSetup.cs`（`Volcano` 转 Special + `VolcanoEmberBuff` 90）+ `Actions.cs`（`ContextActionVolcanoEmber`） | 首伤每 2 级 1d6（最多 10d6）反射减半，失败挂余焰 2 轮；余焰每轮单独豁免，每 4 级 1d6（最多 5d6，取首伤骰数一半） | 无 |
| C19 | 已改 | `SpellsSetup.cs`（`Twilight` 转 Special + `TwilightMeltBuff` 91）+ `Actions.cs`（`ContextActionTwilightMelt`） | 首伤每级 1d6（最多 20d6），失败挂熔火 2 轮；熔火每轮单独豁免 4d6（通过 2d6） | 无 |
| C13 | 已改 | `SpellsSetup.cs`（`Flashbang` 转 Special + `FlashbangBlindBuff` 93 + `FlashbangDazzleBuff` 92）+ `Actions.cs`（`ContextActionRemoveInvisibility`） | 失败目盲 1 轮（到期转目眩 1 轮/级）+ 移除隐形；成功目眩 1 轮；15 尺点爆发近似锥形不变（第 7 版已接受） | 无 |
| C21 | 已改 | 见 N8b | 同 N8b；建筑双倍按 V16 已删，保持删除 | 无 |
| C27 | 已改 | `SpellsSetup.cs`（`FoxfireDance`：新增 `FoxfireTouchBuff` 98，区域改挂触） | 触进场/每轮意志豁免（仿影之疆域），失败挂缓慢+30% 1 轮；友军回生不变；离开撤触 | 无 |
| C18后半 | 已改 | `Components.cs`（新增 `DrunkenDreamWake`）+ `SpellsSetup.cs`（`DrunkenDream` 转 Special + `DrunkenDreamSleepBuff` 8f） | 失败沉眠 1 轮/级（`Sleeping`+`Wake`）；受伤移除睡眠并恍惚 1 轮（原版 Daze）；失败分支同时挂恍惚 1 轮故免疫者仍恍惚 | 睡眠持续已是 1 轮/级 |
| C10 | 已改 | `SpellsSetup.cs`（`ScorchingBreath` 转 Special + `ScorchResistBuff` 94） | 首伤每级 1d6（最多 10d6）；失败者火焰抗力 -10（`AddDamageResistanceEnergy` 负值，Valmallos 同款，见 E6）1 轮/级 | 无 |
| C15 | 已改 | `SpellsSetup.cs`（`FrozenDomain` 转 Special + 区域 `FrozenDomainArea` 8e + `FrozenEntangledBuff` 96 + `FrozenHalfSpeedBuff` 97）+ `Actions.cs`（`ContextActionFrozenDomainHit`） | 区域 1 轮/级，进入/每轮 4d6（通过 2d6）；失败定身（`Entangled`）1 轮，成功半速（IronBody 同款 0/0.5，见 E6）1 轮；困难地形按 G2 删 | 无 |
| C26 | 已改 | `SpellsSetup.cs`（`Calcification` 敌方改半速） | `Slowed` 换 `BuffMovementSpeed(Value0/Cap0.5)`（IronBody 同款，见 E6）；豁免 -2 不变 | 文字本就是半速，现一致 |
| C17 | 部分已改（灼烧已做；忽略半抗/免疫半伤做不到） | `SpellsSetup.cs`（`BlackSnakeFlame` 转 Special + `BlackSnakeBurnBuff` 95）+ `Actions.cs`（`ContextActionBlackSnakeBurn`） | 首伤每级 1d6（最多 20d6）反射减半（成功失败都挂灼烧）；灼烧每轮 3d6，1 轮/级；Meta 持续改 `round1level` | 抗力子项见做不到栏；文字保留 |
| C22 | 已改 | `SpellsSetup.cs`（`Cloudpiercer` 转 Special）+ `Actions.cs`（`ContextActionCloudpiercerHit`） | 20d6 穿刺（通过 10d6），`RuleDealDamage{IgnoreDamageReduction=true}`；穿墙按 V16 已删 | 文字同步删掩蔽/穿墙 |
| C6 | 已改 | `SpellsSetup.cs`（`EmergencyTreatment` 转 Special + 17 慈悲链） | 治疗 1d6/2 级（`DiceRank Step2 Cap5`，LoH 同公式）；`CasterHasFact(慈悲)→RemoveBuffsByDescriptor`，GUID/描述符逐条抄 `LayOnHandsOthers`（见 `evidence/E6-Mercy.txt`） | 不能伤不死生物由 Friendly 目标保证 |
| C4 | 已改 | 见 N1 + `SpellsSetup.cs`（天灾整轮+80 尺区域 1 分钟） | `SetIsFullRoundAction(true)`；N1 单体化后每敌人每轮一次 12d6火/轰/电 | 六点自选简化整区域伤害不变（前轮已定） |
| C33 | 已改 | 见 N2/N3 | 追踪 1 轮/级 + 自由动作手动引爆；2d6火+2d6轰反射减半；下轮开始自然触发 | 落点 10 尺简化不变（前轮已定） |
| 6.3顶栏 | 已核对同步 | `SpellsSetup.cs`（Metas：`EarthFury`→instant，`BlackSnakeFlame`→round1level） | 来源：顶栏读蓝图 `LocalizedDuration/LocalizedSavingThrow`，本 mod 在 `CreateSpell` 经 `SetLocalizedDuration(meta.DurationKey)`/`SetLocalizedSavingThrow(meta.Save)` 写入；本轮改动持续/豁免头与 Meta 一致 | X5 留实测 |

| T21 | 已改 | 设计/JSON/内嵌（冰封/大地/天灾删困难地形） | 同 G2 | 无 |
| T22 | 已改 | 设计/JSON/内嵌（炮火同一点连续三轮） | 同 G3 | 无 |
| T23 | 已改 | 设计/JSON/内嵌（迅捷突破） | 同 G4 | 无 |
| T24 | 已改 | 设计/JSON/内嵌（重构） | 同 G5 | 无 |
| T25 | 已改 | 设计/JSON/内嵌（大地召唤物双倍） | 同 N8b | 无 |
| T26 | 已改 | 设计与游戏内一致（天灾无困难地形）+ 本栏 N8c 更正 | 同 N8c/G2 | 无 |
| T27 | 未改（做不到随 N8d） | 烟幕中英文保持原样 | 中文无强风句、英文保留强风句；不为一致删英文 | 列入不符栏 |
| T28 | 已改 | 设计第 298 行（王冠成功随机失去一个，直接消失） | 同 N8e | 无 |
| T29 | 已改 | JSON/内嵌按改后效果更新（含顶栏持续/范围） | 火山/黄昏/闪光/大地/舞乐/醉梦/灼烧/黑蛇/穿云/救治/冰封/钙质化均已同步；`HarvestYears` 上限 60、`BannerStands` 30 尺、`EternalGaze` 加速 1 分钟、`Cloudpiercer` 删掩蔽同步 | `ChainDetonation` 描述与代码-area 不一致未动，记新发现 |
| T3后半 | 不改 | 无 | 坚守/骑兵/乌拉/过载中心点与范围现状见“需作者决定”栏（照抄上轮，未动） | 待作者确认 |
| E3 | 已补 | `evidence/E3-AreaRound.txt` | `AbilityAreaEffectRunAction.OnRound` 对 `InGameUnitsInside` 逐单位 `GetDataScope+Round.Run()`；`Target` 即本次单位（另有 `InGameUnitsInside` 与 `ContextAction.Target` 原文） | 用于 N1，已改完 |
| E4 | 已补 | `evidence/E4-Summoner.txt` | `RuleSummonUnit.OnTrigger` 写 `Summoner`；`UnitPartSummonedMonster` 字段；`ContextActionSpawnMonster` 走 `RuleSummonUnit`；镜中流形同走该规则 | 用于 N4，已改完 |
| E5 | 已补 | `evidence/E5-IsSpell.txt` | `BlueprintAbility.IsSpell`（Spell/SpellLike 为真）+ `AbilityType` 枚举 + `SourceAbility` 链 | 用于 N10，无需改 |
| E6 | 已补 | `evidence/E6-HalfSpeed-Resist.txt` + `evidence/E6-Mercy.txt` | 半速 IronBody（2eabea6a… Value0/Cap0.5）+ 反编译 `OnTurnOn`；降抗 Valmallos（6bb29963… Fire -10）+ `UnitPartDamageReduction` 罚值逻辑；17 慈悲 GUID/描述符对抄 `LayOnHandsOthers`；N2 的 `AddFacts` 依据附于 E6-Mercy 末段 | 速度/降抗/慈悲已做；C17 抗力无先例做不到 |

## 单独栏 1：做不到（卡在哪、查过什么）

- N8d 强风驱散烟幕：`BlueprintAbilityAreaEffect` 无风相关字段（E3 同源反编译字段）；`blueprints.zip` 未见云雾被风驱散现成组件。未改代码与文字（英文保留强风句）。
- C17 忽略一半火焰抗力、火焰免疫仅减半：`AddDamageResistanceEnergy` 无“忽略一半”字段；`AddEnergyDamageImmunity` 无半伤字段；`RuleDealDamage.IgnoreDamageReduction` 只管物理 DR；原版未找到对应组件（见 E6 第 4 节）。持续 3d6 已做。
- C17 成功也挂灼烧：设计“之后目标每轮 3d6”未分成功失败，本实现成功失败都挂灼烧（与黑蛇首伤减半共存），如作者要求成功不挂需另定。

## 单独栏 2：需作者决定（含 T3 后半 4 法术现状，照抄上轮未动）

| 法术 | 中心点 | 范围 | 目标类型 | 说明 |
|---|---|---|---|---|
| 坚守阵地 | 玩家点选地面点 | 半径 30 尺 | Ally | 近距，迅捷，3 轮 |
| 骑兵突进 | 玩家点选地面点 | 半径 30 尺 | Ally | 同上；速度 +10/+20 不具类型、借机 AC+4 |
| 乌拉 | 玩家点选地面点 | 半径 30 尺 | Ally | 1 分钟/级；攻击/伤害 +1/+2 不具类型 |
| 过载模式 | 玩家点选地面点 | 半径 20 尺 | Ally | 3 轮；额外攻击（非 haste，可叠）+攻击/AC/反射/速度 |

## 单独栏 3：游戏内文字与实际效果不符（本轮结束时）

- N8d 烟幕：中文无强风句、英文有强风句，实际无驱散（做不到）。
- C17 黑蛇：文字“忽略一半火焰抗力，火焰免疫仅减半”实际未实现（做不到）；“之后每轮 3d6”已实现。
- 其余本轮 C 项文字已对上。

## 单独栏 4：自查结果（第 9 节 6 项）

1. 四处一致（代码/中文设计/JSON 中文/JSON 英文 + 内嵌原文）：除上栏两项做不到外，其余一致；连锁殉爆描述（每 3 级一名）与代码-area 不一致未动，记新发现。
2. R1：新增 `DrunkenDreamWake` 无字段；新增动作字段均为蓝图引用配置（`EntangledBuff/HalfSpeedBuff/Marker` 类）；半速/降抗走原版组件配置；无运行时数据写组件字段。
3. TODO/占位：`grep` 本轮新增无 `TODO/占位/待实现/按定案不做`；7 处旧 `VERIFY`（集火最低 1 次、TransferRank、测试占位模型、墨灵天生武器、骨架注释）为前轮保留，未动。
4. 困难地形：中文设计、英文设计、`LocalizedStrings.json`、代码内嵌 `L10n.S` 均 `grep` 为空；代码注释残留“按 G2 删除”字样为历史注记，非面向玩家文字。
5. 新能力授予：本轮唯一新建可施放能力即延时爆破手动引爆，已由追踪增益 `AddFacts` 授予（N2）；其余新建均为增益，随法术挂载。
6. 多份施放：炮火/岁稔按 Context 找自己那份（N7）；延时 Round 本就按 Context；火山/黄昏余焰为逐目标独立增益，无跨目标串数据。

## 单独栏 5：新发现（只记录，不改）

1. `ChainDetonation` 游戏内描述“至多一名敌人/每 3 级”与代码实际（目标点 20 尺内全部敌人，`TargetsAround` 选圈）不一致；注释本轮已按实际改，描述未动，待作者定以哪边为准。
2. `BlazingSunBow` 成功分支只有半伤无目眩：代码成功 `DealDamage(halfIfSaved)` 无目眩，设计 248 行“成功者半伤且目眩 1 轮”、JSON“成功半伤不倒地”均无目眩——三处一致地缺了成功目眩，未动。
3. `Reconstruction`/`Civilization`“复活时间窗（1 分钟/级）”代码仍不限（前轮已载明），未动。
4. `DelayedBlast` 多炸弹手动一次全爆（前轮已载明），追踪增益各自 `AddFacts` 同一引爆能力，能力施放清全部，本轮保持。
5. `FrozenDomainArea` 用新 GUID 8e；`CalcificationArea` 仍为钙质化专用，无冲突。

