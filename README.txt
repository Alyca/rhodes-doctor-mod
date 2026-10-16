罗德岛博士（圣武士变体）mod — 源码说明（v0.5.0 法术书方案）
==============================================================

配套规则文档：rhodes-doctor-archetype.txt（中文，规则以此为准，修订第 6 版）
配套英文文本：rhodes-doctor-en.txt

本版已从"理智值资源制"改为"法术书方案"：指令是一本独立的智力自发法术书
（每日法位与已知指令数同术士），共 64 个指令：0–9 环各 6 个，另有 4 个
10 环指令（**实验阶段**：测试面板按钮发放）。

v0.3.0：全套源码按 WW-Blueprint-Core 2.8.7 与游戏规则程序集的真实接口重写。
所有字符串移到 LocalizedStrings.json（游戏原生本地化包格式），由
Content.LoadLocalizationPack 在启动时加载。


一、目录结构
------------
Info.json                 Unity Mod Manager 清单
LocalizedStrings.json     中英文本地化包（约 190 条，键前缀 rhodesdoctor.）
RhodesDoctorMod.csproj    工程文件（.NET Framework 4.8）
src/Main.cs               UMM 入口 + 蓝图注入时机（含 ModPath）
src/Guids.cs              全部蓝图 GUID + Guids.Vanilla 原版蓝图常量区
src/L10n.cs               本地化辅助（按键取共享条目）
src/Content.cs            装配入口（加载本地化包 + 各模块独立 try/catch）
src/SpellbookSetup.cs     战术指令法术书 + 法术列表 + 神话合书登记（stub）
src/SpellsSetup.cs        64 个指令法术（规格表驱动；复杂机制为 VERIFY 骨架）
src/ArchetypeSetup.cs     变体本体、成长表、集火、源石技艺精通、灵光、克隆段
src/SpecterSetup.cs       巴别塔的恶灵（资源、三阶段形态、锁定、余波、20级）
src/Components.cs         自定义机制组件（规则钩子）
src/Actions.cs            自定义动作（每轮消耗资源、移除标记）
src/Clone.cs              蓝图 JSON 往返深拷贝工具（当前未调用）
src/TestSetup.cs          测试木桩蓝图（UMM 测试面板用）
src/TestPanel.cs          UMM 测试面板（Ctrl+F10）
docs/dev-notes.md         实测踩坑记录：日志取证 / 反编译 / 原版蓝图对照 / 常见运行时坑


二、编译
--------
1. 安装 .NET SDK（6.0 以上即可，目标框架是 net48）。
2. 安装游戏并在 Unity Mod Manager 里启用 mod 支持（游戏会生成
   Wrath_Data\Managed\publicized_assemblies）。
3. Guids.Vanilla 的原版 GUID 已全部填实，无需再填。
4. dotnet build -p:WrathPath="游戏安装目录"
   （或设环境变量 WOTR_PATH）
5. 把编出的 RhodesDoctor.dll、Info.json、LocalizedStrings.json，以及
   icons/png-64/ 整个文件夹放进 Mods\RhodesDoctor\，用 UMM 安装。
   csproj 已配置清单、本地化文件与 icons 下的 PNG 随编译输出；不要只复制 DLL。


v0.6.3 → v0.6.4（20 级恶灵修复 + 全套指令可学满 + 发布版收尾）：
- **20 级"人灵合一"现在真正生效**：此前 20 级开恶灵形态仍不能施放非【恶灵】指令——根因是
  压制锁定依赖 BlueprintCore 给 `Buff.OnAttach` 挂的补丁，与其他改同一处的 mod 共存时不可靠。
  现改为**确定性判定**：只要 20 级「罗德岛的博士」增益在场，锁定一律视为不生效
  （`BuffHelper.SpecterLockActive`），施法限制 / 穿透与 DC 倒扣 / 冷静头脑减半 / 结束后的余波
  一并自动解除，与载入顺序无关。
- **全套 64 个指令现在都能学满**：原照抄术士已知法术表，各环上限 3–5 个、且无 10 环列，永远学不全。
  现改为**自定义累计分配表**——按等级均匀铺开，20 级恰好学会 1–9 环各 6 + 10 环 4（58 个），
  加上自动全给的 6 个 0 环戏法 = 全套 64；并新增补发组件，**旧存档读档**自动补齐按等级应得、
  却没拿到的已知指令（幂等，不重复加）。
- 发布版收尾：收回诊断日志（`Dbg.Log` 相关调用全部编译掉，运行时零开销）。
- `Info.json` 0.6.3 → **0.6.4**。

v0.6.2 → v0.6.3（补齐两个状态栏空白图标）：
- **巴别塔的恶灵（锁定）**增益此前没有图标（状态栏显示空白框）：改借原版"狂暴"图标
  （`Guids.Vanilla.RageFeature`，与一/二/三段形态特性同款）。三个锁定（一段/二段/三段）共用同一方法，
  一次补齐。
- **罗德岛的博士**增益（20 级 `CapstoneBuff`）此前没有图标：改用与 20 级特性同一张
  `icons/png-64/specter/doctor-capstone.png`。
- 不新增素材、不动规则逻辑；`Info.json` 0.6.2 → **0.6.3**。

v0.6.1 → v0.6.2（代码审计修复 + 源石技艺精通改版）：
- **源石技艺精通改版**：由"施法者等级检定 +2/+4/+6/+8"改为"**法术穿透 + 法术豁免难度各 +1/+2/+3/+4/+5**"，
  5 档落在 **1/5/9/13/17 级**（新增第 5 档特性 `Mastery5Feature`，GUID `…3f`）。
  `OriginiumMastery` 组件同时挂 `RuleSpellResistanceCheck`（穿透）与 `RuleCalculateAbilityParams`（DC）；
  恶灵锁定期间两项加值失效并**同样倒扣转移量**（一段半额、二/三段全额）。
- **P1-A｜岁稔·余温不再把运行时修饰器写进存档**：`HarvestYearsResidualData.Modifier` 改 `[JsonIgnore]`，
  OnActivate 按 `Amount` 重建、OnDeactivate 补 try/catch（原先读档可能静默丢临时生命 / 移除链被打断）。
- **P1-B｜画中世界"找不回"兜底**：找回失败改为 **Error 级日志**（release 下也可见）+ 挂 `BanishRecovery`
  在宽限时间点（0.5/2/6/15/30s）重试全局扫描；读档重激活时也会重试，找回即返回+麻痹。
- **P2 性能**：`FxTint` 反射（`GetFields/GetProperties/material` 属性）按 `Type` 缓存；
  `AreaGroundFx.LogAreaView` 补 `[Conditional("RD_VERBOSE")]`（原先 release 仍全树枚举）；
  `BombFx` 圆盘网格与两个圈材质改**静态缓存复用**（原每次布设新建 mesh+2 材质）；
  `Main.Load` 的 `PatchAll` 包 try/catch + 重入守卫（UMM 重载不再双重订阅）。

v0.6.0 → v0.6.1（收尾：诊断日志改为编译期开关）：
- **删除 `src/Diag.cs`**（"TEMP-DIAG" 的 Harmony 补丁，每次范围选择都要跑）与 9 条 `[RhodesDoctor-DIAG]` 埋点
  （含只为它们存在的死变量）。
- 新增 `src/Dbg.cs`：诊断日志走 `[Conditional("RD_VERBOSE")]` —— **未开符号时调用被编译器整体删除**
  （连参数字符串都不拼接）⇒ 发布版零开销。**100 处**逐事件日志已转入这条通道；
  另有 5 个**纯诊断方法**本体也标了 `[Conditional]`，调用点一并消失。
  `Logger.Log` **132 → 24**（只剩启动 / 关键状态 / 测试面板），`Logger.Error` 全部保留。
- 需要排查时一次性开回来：`dotnet build -p:DefineConstants=RD_VERBOSE -p:WrathPath="…"`。
- 见 `docs/dev-notes.md` §29。

v0.5.9 → v0.6.0（描述全部按官方规范重写）：
- **114 条法术/能力描述全部重写**（中文 97 条、英文 93 条）：删掉**手写的状态栏**（27 条，
  学派/距离/持续/豁免那些由 UI 自动渲染，正文重复写还会过期）、删掉**作者署名**（62 处）与
  **实现细节**（"（实现为…）"）；术语统一为官方写法（**减值**、**钝击**、**纠缠**、**抗性**）；
  并首次使用游戏的术语链接标记 `{g|Encyclopedia:…}`（中文 259 处 / 英文 196 处）。
- 规范文档见 `docs/description-style-guide.md`，落地记录见 `docs/dev-notes.md` §28，
  原始文本备份在 `_tools/tmp/LocalizedStrings.orig.json`。
- ⚠ 进游戏请先看**一条**（如"战术标记"）确认 `{g|}` 渲染成可点链接。

v0.5.8 → v0.5.9（固定距离 + 0 环施法时间显式化；官方描述规范）：
- **三个法术补固定距离**（新增 `Spec.RangeFeet`，走 `AbilityRange.Custom` + `CustomRange`）：
  战术标记 60 尺、信标投射 30 尺、牵制射击 30 尺；王者冲锋的 40 尺也并入这条通用路径。
- **0 环整组施法时间显式化**（补 `Cast=Standard`，此前靠默认值）。
- **更正**：天灾降临**本来就是整轮动作**（`SetIsFullRoundAction(true)`，上轮我漏读该字段）；
  旗帜不倒**实际就是 1 轮/级**（走 `ContextDuration.Variable(Rank)`，`Rounds=5` 不参与计算，已清理）。
- **新增 `docs/description-style-guide.md`**：从游戏官方文案反推的**描述规范**
  （头区自动生成 / 正文只写效果 / 术语用 `{g|Encyclopedia:…}` 链接 / 术语与句式规范），
  含 mod 现状诊断与改写示例。见 docs/dev-notes.md §26、§27。

v0.5.7 → v0.5.8（快捷栏"三套恶灵形态"修复；规则文档全表对齐）：
- **快捷栏里出现 3 套"敏捷/体质形态"开关**的修复。根因：8/12/16 级用 `RemoveFeatureOnApply`
  换发开关（**删除本身是生效的**），但旧承载是 `AddFacts` 授予的，而 `AddFacts` 的
  `DoNotRestoreMissingFacts` 默认为 `false` —— **每次读档**的 post-load 修复都会把已删掉的旧承载**重新补回来**。
  修法两处互补：① 阶段特性的 `AddFacts` 加 `doNotRestoreMissingFacts: true`（只影响读档）；
  ② 新增组件 `EnforceSpecterStage` 挂在二/三段特性上，激活与读档各收敛一次（**旧存档读档即修，不用洗点**）。
  做法与取证见 docs/dev-notes.md §24。
- **规则文档 + 英文文本按代码做了全表 diff**（29 处）：把"近距内选一点为中心"改成"以施法者为中心"
  （坚守阵地/骑兵突进/乌拉/圣域/潮汐守望/岁稔/旗帜不倒/永恒的注视/重构/文明的存续）；
  大地之怒 60→30 尺且改以你为中心、天灾降临 80→40 尺；牵制射击(中) 1 轮 → 1 轮/级；
  王者冲锋"个人/两倍速度" → 40 尺（选一点）。见 docs/dev-notes.md §25。

v0.5.6 → v0.5.7（旧日的战术家：不给空放 + 补全收益；规则文档时长对齐）：
- **没开恶灵形态就不给放**：新增 `AbilityCasterHasAnyActiveFact`（至少有一个指定增益才可用），
  按钮置灰提示"需要先开启恶灵形态"；`UnitUseAbility.OnAction` 会**在扣法术位之前**复查，
  所以点下去也**不会白扣法位**。做法见 docs/dev-notes.md §23。
- **补全"敏捷形态的全部收益"**里漏掉的**护甲最大敏捷加值**（形态有、法术补的那一半没有）；
  口径统一为"转移量 +2"：补的那一侧 = 转移量+2，形态自己那一侧再 +2。
- **规则文档 + 英文文本的持续时间与代码对齐**（41 处）：坚守阵地/骑兵突进 → 1 分钟/级；
  寒霜祷言/不稳定血浆/过载模式/钙质化/时之锁/圣域/奇美拉/涌潮悲歌/镜中流形/旧日的战术家/反重力/儿时的舞乐
  → 1 轮/级；牵制射击(en) → 1 round/level；画中世界 → "放逐 2 轮，返回后麻痹 1 轮/级"；归溟 → 潮。
  文档加了【第 12 版】修订记录。

v0.5.5 → v0.5.6（旧日的战术家改回原设计）：
- 这个 9 环【恶灵】指令在 10-10"补特效"那轮被**改了机制**：为了让光环在"没有开形态"时也能看到，
  把原本"不在形态就空放"改成了"无形态时两套一起给"，于是它在形态外也变成一份独立大 buff。
- 现按作者要求**改回**：在**体质形态**补敏捷套件、在**敏捷形态**补体质套件、
  **不在任何形态时什么都不给**（= 规则"同时获得两敏捷与体质形态的全部收益"的正确口径：
  形态给一半、法术补另一半；**不要改成两套无条件一起给**，那样在形态内会叠成双份）。
- 工具提示同步说明"需先开启恶灵形态（不在形态时无效果）"。教训见 docs/dev-notes.md §22。
  ⚠ 因此**蓝色光环只会在恶灵形态中施放时出现**（法术没生效就不会有光环，这是正确表现）。

v0.5.4 → v0.5.5（20 级"罗德岛的博士"真正生效）：
- **真凶**：Capstone 用的是**普通压制**（`SuppressBuffsFixed` 默认 `ApplyToNewBuffs=false`），
  它**只压制"它生效那一刻已经存在"的增益**；20 级升级时身上没有锁定（恶灵没开）⇒ 记下的是空名单 ⇒
  之后每次开恶灵**新生成**的锁定增益根本不在压制范围里。
  表现：20 级开恶灵**不能施法**（本该可施展全部指令）、结束恶灵后**仍被余波限制**、穿透**仍倒扣**、灵光**不生效**。
- 修法：Capstone 与三段锁定都改用**持续压制**（`new SuppressBuffsFixed(applyToNewBuffs: true)`），
  新挂上来的锁定/余波也会被压制。**旧存档不用重训**（读档时 `OnPostLoad` 会重新登记持续条目）。见 docs/dev-notes.md §21.5。
- （上一版修的"两个形态能共存 / 结束恶灵只取消一个"见 v0.5.3 → v0.5.4 段与 §21.2–21.3。）

v0.5.3 → v0.5.4（恶灵形态互斥 / 结束恶灵一次删完）：
- **同时只能有一个恶灵形态**：原来只挂"施法者限制"（只管按钮能不能点），作者实测 20 级时
  两个形态仍能被连续点开并共存。现在改成**动作执行时的硬互斥**（`ContextActionApplySpecterForm`：
  身上已有任意形态增益 → 本次开启直接忽略并记日志），名单覆盖**全部六个**形态增益
  （敏捷/体质 × 一/二/三段，旧版只列本阶段那一对，换阶段时会漏）。UI 限制名单同步扩到六个。
- **结束恶灵改成一次删完**：原来六个 `RemoveBuff` 排成动作队列，而"结束恶灵"能力本身是形态增益
  用 `AddFacts` 授予的 —— 删掉增益的瞬间能力被销毁、队列后面不再执行（表现为"点一次只取消一个"）。
  现在用一个自定义动作 `ContextActionEndSpecterForms` 在同一帧内一次性删完。
  详见 docs/dev-notes.md §21。
- （20 级恶灵期间可以施展全部指令是**规则本身**，由 20 级"罗德岛的博士"压制锁定增益实现，不是 bug。）

v0.5.2 → v0.5.3（10 环走升级界面 / 恶灵黑烟 / 补图标）：
- 10 环指令不再靠测试面板发：**补两张表的第 11 列**（每日法位 + 已知法术），20 级升级时
  升级界面会像其他环一样弹 **10 环选择阶段**（4 个名额选满 = 学会全部 4 个 10 环指令）。
  反编译链路、以及"门槛行为什么是 21 而不是 20"（博士法术书等级比职业等级多 1，存档实测）
  见 docs/dev-notes.md §10.1 与 report-10ring-specter-fx-icons.md。
  ⚠ 旧存档已满 20 级不补发（重训/洗点，或继续用测试面板按钮）。
- 巴别塔的恶灵：六个形态增益（敏捷/体质 × 一/二/三段）挂**黑烟常驻光环**
  （`TintedAuraFx`，素材 = 原版血雾重染黑烟色），结束恶灵即灭。
- 原来没图标的特性补上：源石技艺精通 ×4 / 冷静头脑 / 圣疗 / 引导正能量 / 恶灵一·二·三段 ——
  新增 `Icons.Borrow(原版GUID)` **直接借游戏内现成 Sprite**，不新增 PNG。

v0.5.1 → v0.5.2（测试面板与目标/落点修复）：
- 敌人木桩点不出来（"刷怪返回 null"）的真因：手搓 Unit 蓝图缺两个**类字段**（默认 null，
  而 UnitDescriptor.Initialize 会解引用）：Skills（→ SetSkills NRE，报在 UnitDescriptor.SetSkills）
  与 FactionOverrides（→ BlueprintUnit.AttackFactions NRE）。两个手搓单位（木桩 / 墨灵）都补上
  SetSkills(new UnitSkills()) + SetFactionOverrides(new FactionOverrides())。
  （StartingInventory / AdditionalTemplates / AddFacts 是 null 安全的 ReferenceArrayProxy，不用管。）
  另外把原版小型气元素的 Visual / Body 照抄过去（类字段，默认 null；游戏会在被打中出血迹特效、
  读 Bark、或 PreloadResources 时解引用，见 SummonsSetup.CopyTemplateVisuals）。
  测试面板的刷怪点同时改为先经 FreePlaceSelector 推离单位/贴回导航网格、并在
  UnitSpawningData 请求里刷，避免边界外占位模型。
- 纯敌方法术（战术标记 / 火力侦察 / 牵制射击 / 寒霜祷言 / 缄默 / 反重力 / 影霄·拔刀 等）与集火：
  补 AbilityTargetIsAlly(Not=true)。游戏 AbilityData.CanTarget 对"友军"并不读 CanTargetFriends
  （原版单目标有害法术同样允许点友军，所以原版也挂了这个限制组件），只设 CanTargetFriends=false
  挡不住自己/友军。现覆盖：单体 + 敌方范围(AoETarget=Enemy) + 直线；点地面仍可放，
  只是不能直接点到自己/友军身上。AoETarget=Any/Ally 的范围法术（烟幕/钙质化/儿时舞乐等）不加。
- 紧急撤离：改为永远把目标丢到"施法者身后"（朝向反方向约 5 尺），再用 FreePlaceSelector
  落到最近空地。此前写死 caster.Position+(2,0,2)，表现为永远出现在施法者左下角。


v0.4.1 → v0.5.0（四项设计定案落地）：
- 炮火延伸：定案为"一个法术挂逐轮依次爆炸的增益"。施法立即爆第一轮
  （8d6 轰击 + 8d6 火焰，反射减半），之后挂在施法者身上的追踪增益
  （新组件 ArtilleryBarrageTracker）记住目标点，施法者的每个新回合开始再爆一次，共三轮。
  已知偏差：规则是三个不同落点依次爆炸，实现为同一施法点三轮（多点选点待实测）；
  逐轮爆炸的反射减半待接（VERIFY）。新增增益 GUID ArtilleryBarrageBuff（0xec）。
- 墨灵自爆伤害类型统一为力场（规则文档原写"精神"，与代码 ForceDamage 不一致，已统一）。
  游戏内描述文本改了，请重跑本地化生成脚本。
- 医疗无人机：按定案简化为"跟随友军的增益"——每轮开始为持有者恢复 1d6 + 圣武士等级
  （至多 +5），不再是可被攻击的实体单位。描述文本改了，请重跑脚本。
- 战术标记 / 火力侦察：不复用集火组件，按定案自编——
  TacticalMarkDebuff（攻击被标记者 +1 洞察，攻击结算后标记移除）；
  FireReconMark（攻击被标记者忽略失手几率；VERIFY：RuleAttackRoll 上忽略失手几率的开关名）。
  两者为无豁免的敌人减益（Spec 新增 SaveNone / PerLevel 字段）。
- 代码交叉检查顺手补：Spec 的 Buff/Debuff 分支支持 PerLevel（1 轮/级持续）；
  Components.cs 补 using Kingmaker / Kingmaker.Utility。

v0.4.0 → v0.4.1（本地编译核对后的两处修正）：
- 处决（Components.ExecutionStrike）：PhysicalDamage.Dice 是只读属性、
  ModifiableDiceFormula 没有 Rolls/Dice——改读 Dice.BaseFormula 后
  Dice.Modify(同骰数) 追加一份实现翻倍；只翻武器自带骰，其他加骰不受影响。
- 钙质化（SpellsSetup）：敌我判断从直接 new ContextConditionIsEnemy
  （命名空间猜错）改为 BlueprintCore 现成的 ConditionsBuilder.IsEnemy()。
- 本地构建核对结果：缄默的 ForbidMagicItems 字段、ExAndSuAbilitiesForbidden
  枚举、影霄·拔刀的 DamageEnergyType.Magic 均在程序集中确认存在，相应
  VERIFY 视为已解除。本地化脚本空跑：198 键与源码一一对应，无孤儿。

v0.3.9 → v0.4.0（第二步"好做批"：四个空指令填上效果）：
- 影霄·拔刀：强韧部分通过——失败即死（ContextActionKill，放逐术同款动作），
  成功仍受 10d6 + 圣武士等级伤害。免疫即死者天然不受 Kill 影响，视为豁免成功。
  伤害类型暂用 Magic 能量（VERIFY：规则未定型伤害类型，若编译没有 Magic 改 Direct）。
- 缄默：意志无效。失败挂减益——ForbidSpellCasting（禁法术与类法术）
  + AddCondition(ExAndSuAbilitiesForbidden)（禁超自然与非凡主动能力），
  镜像行为锁同款两件套，去掉禁魔法物品。两个 VERIFY：ForbidSpellCasting 的字段名、枚举成员名。
- 处决：挂"处决（蓄势）"增益，新组件 ExecutionStrike（Components.cs）：
  近战命中当前生命不高于一半的目标时武器伤害骰翻倍（精确伤害不动），触发后移除增益。
  VERIFY：物理伤害骰的字段名与可写性。
- 钙质化：无豁免区域，按敌我拆两个增益——敌人缓慢 + 豁免 −2，友军每轮恢复 1d8。
  用 Conditional + ContextConditionIsEnemy 分敌我（VERIFY：类名与命名空间）；
  "缓慢"近似"速度减半"（缓慢自带 −1 攻击/AC/反射，比规则略狠，记偏差）。
- 这四个法术（加影霄·拔刀）的"1 轮/级"持续都要 rank = 圣武士等级，
  已在 CreateSpell 里统一挂上（此前只有墨染山河有）。
- 本地化新增 4 条增益名称键；增益描述复用对应法术的 desc 键。
  请重新运行本地化生成脚本（新增了 4 个字面键）。
- 空指令清单剩余：信标投射、紧急撤离、烟幕掩护、牵引、迅捷突破、王者冲锋、
  儿时的舞乐、罪罚、终末狙击、连锁殉爆、归溟、影之疆域、旗帜不倒、旧日的战术家、
  永恒的注视（15 个，属中等批与大活批）。【v0.5.0 起：战术标记、火力侦察、医疗无人机
  已落地；炮火延伸已按定案实现。】

v0.3.8 → v0.3.9：
- 画中世界补上隐形：CreateSpellBuff 的 PaintedWorld 分支加 AddCondition(UnitCondition.Invisible)，
  与麻痹同一写法（枚举值已在程序集确认；未引原版 InvisibilityBuff 525f980c…）。
  游戏内描述文本未改，不需要重新生成本地化。
- LocalizedStrings.json（站点副本）同步用户本地生成结果的 3 处变化：更新 spell.PaintedWorld.desc、
  spell.Catastrophe.desc，删除无人引用的 spellbook.desc。注意：站点副本仍是旧的外层包裹格式，
  真正随 mod 发布的应是脚本产出的顶层数组版本，以本地产物为准。

v0.3.7 → v0.3.8（修复进游戏会直接出问题的 1–3）：
- SpellsSetup 伤害分支：DealDamage 外层包 SavingThrow(s.Save, onResult: ...)。此前没有豁免层，
  halfIfSaved 读不到豁免结果，剑雨/火山/黄昏/雷霆审判等每次吃满伤害。
- 寒霜祷言、牵制射击：Kind.Buff → Kind.Debuff（Save=Will），改为只能点敌人、失败才挂减益。
- 画中世界：Special(Kill) → Debuff(Condition=Paralyzed, 2 轮, Will)，目标改一名敌人；描述文本同步改。
  降级方案里的“隐形”已在 v0.3.9 补上（UnitCondition.Invisible）。
- 本轮改了 PaintedWorld 的 L10n.S 字面文本，请重新运行 gen-loc.ps1 生成 LocalizedStrings.json。

v0.3.6 → v0.3.7：
- 新增 Clone.Of<T>：使用游戏自带 OwlcatJsonConvert 序列化/反序列化蓝图，避免不存在的
  ObjectDeepCopier，也不对普通 C# 蓝图对象调用 UnityEngine.Object.Instantiate。
  Clone.Of 当前没有调用方；返回副本后，调用方应在缓存注册前设置唯一 AssetGuid。
- RhodesDoctorMod.csproj 增加游戏目录中的 Newtonsoft.Json.dll 引用（Private=false），供 OwlcatJsonConvert 使用。

三、VERIFY 热点（编译报错时优先核对这些）
----------------------------------------
1. Guids.Vanilla 已全部填实（游戏数据核实值），不再是空串占位。新增原版引用一律填真实
   GUID（BlueprintCore.Blueprints.References 里短名如 SmiteEvilFeature 在 2.8.7 不存在，
   要用 AuraOfResolveFeature 式全名对应的 GUID），不要留空串——空串 GetRef 不报错，
   但运行时蓝图找不到。
2. 本地化：LocalizationManager.LoadPack 的 API 与 LocalizedStrings.json 的包格式；
   LocalizedString 按键解析的字段名（m_Key / m_ShouldProcess）。
3. 法术书：SpellbookConfigurator 的方法名（SetName / SetSpellList /
   SetCastingAttribute / SetSpellsPerDay / SetSpellsKnown / SetSpontaneous /
   SetCantripsType）；SpellLevelList 的 SpellsRefs 字段；第 10 环条目的支持方式。
4. 变体：SetReplaceSpellbook / SetRemoveSpellbook / SetAddSkillPoints 的方法名；
   LevelEntryBuilder.AddEntry 的签名；CopyFrom(源串, c=>true) 与 RemoveComponents<T>。
5. 光环：AuraFeatureComponent 的 m_Buff 反射写入已就位；灵光半径不可配置，待实测。
6. 规则钩子组件：RuleCalculateWeaponStats.OverrideDamageBonusStat、
   RuleCalculateAttacksCount.AddExtraAttacks、RuleSpellResistanceCheck.AddSpellPenetration、
   RuleCastSpell.ForceFail、RuleCalculateAttackBonus.SetAttackBonusPenalty（负号待实测）。
7. 恶灵：ContextRankConfigs.StatBonus 给"调整值"还是"属性值"；
   MaxDexBonusIncrease 的命名空间；AddFacts 组件的 m_Facts 字段名；
   ApplyBuffPermanentFixed / RemoveBuff 的方法名。
8. 血浆自伤：DealDamage 的 toTarget=false 指向施法者；血浆要扣增益持有者的血，
   若不能指向持有者，改为自定义 ContextAction。
9. 复杂机制法术（召唤、复制体、放逐、复活、夺取增益、额外回合）均为 VERIFY 骨架：
   优先抄原版 Time Stop / Dominate / Resurrection / Banishment。
10. 神话合书：对照天使道途 + 术士法术书的原版蓝图确认登记点，不要硬编码道途名称。

规则文档第十四节的实现备注与本清单一一对应，先看文档再改代码。

【实测踩坑】接口 / 运行时的经验（日志取证 Player.log / GameLogFull.txt、ilspycmd 反编译、
blueprints.zip 原版蓝图对照、SpawnUnit 返回 null 的典型原因、目标限制要挂 AbilityTargetIsAlly、
用 FreePlaceSelector 找落点等）统一记在 docs/dev-notes.md，改蓝图/接口前先看那份。


三之二、技能图标（icons/）
------------------------
从网站下载的压缩包里带 icons/ 文件夹：
- icons/png-64/spells/<环位>-<指令>.png、icons/png-64/specter/*.png：64×64 PNG（游戏技能图标尺寸）
- icons/original/...：原始大图（webp），需要其他尺寸时从这里缩放
- icons/README.txt：文件名与指令/能力的对照表
当前代码还没有引用这些图标（游戏内仍显示默认图标）。接入需要游戏或 BlueprintCore 的
贴图加载接口（从 mod 目录读 PNG 生成 Sprite、再给能力/特性设图标）。按协作约定不猜接口：
请先确认接口名与签名，再把 SetIcon 写进 SpellsSetup / SpecterSetup。


四、与旧版的差异
----------------
v0.3.5 → v0.3.6（按程序集核对结果修复检查记录 5、6、8）：
- 集火换目标（ContextActionRemoveMyBuffs）：扫描范围改为当前场景 AllEntityData
  （Game.CurrentScene，备选 LoadedAreaState）里的全部 UnitEntityData，与 Player.AllCrossSceneUnits
  合并去重，敌人身上的旧标记现在能被移除。
- 王冠的回响夺来的增益：Remove 前先取 Buff.TimeLeft，改用 caster.Buffs.AddBuff(bp, Context, remaining)，
  保留原剩余持续时间。
- 墨灵自爆：改走伤害管线（ForceDamage + DamageBundle + RuleDealDamage，context.TriggerRule），
  现为力场伤害、显示数字、吃抗力与减免。目眩 + 意志豁免仍为 TODO。
- 未改：检查记录第 7 条（墨灵模型与职业等级），留作召唤组下一步。

v0.3.4 → v0.3.5（按本地编译结果同步 + 交叉检查）：
- SpecterSetup：补分号；Suppress 名单改在 Lock/Capstone 内联构造（SuppressBuffsFixed().ApplyToBuffs）；
  AddFacts / AddAbilityResources / AddRemoveFeatureOnApply 全部改传 GUID 串；SetHideInUI(true)。
- Actions.cs 重写：RunAction 改 public；去掉 Context.MaybeBuff；资源走 GetBlueprint() as
  BlueprintScriptableObject；夺取动作改名 ContextActionStealBuffsCustom；镜像用 UnitHelper.AddFact +
  RuleSummonUnit(caster, mirror, new Rounds(3))；血浆掉血改 Damage += loss。
- Components：InklingDeathBurst.OnDeactivate 改 protected、手掷 2d6；AddSpellPenetration 第二参数
  改 ModifierDescriptor.UntypedStackable；BuffHelper.HasActive 改按 BlueprintReferenceBase 的 GUID 比对。
- ArchetypeSetup：补 Kingmaker.Designers.Mechanics.Facts；同上改传 GUID；Entries 改
  LevelEntryBuilder.New().AddEntry(level, ...)。
- Content 改 LocalizationTool.LoadLocalizationPack；SpellbookSetup 合书传 Blueprint<...>[]；
  SummonsSetup 删 SetFlags / SetPrefab / AddClassLevels；SpellsSetup 删 SpellDescriptor.Light、
  CrownEcho 用 succeed:、墨染山河改 SpawnMonster Builder。

交叉检查发现并已顺手修的（编译能过、运行时会出错）：
1. 王冠的回响：原顺序是"先驱散、后夺取"，驱散会先把增益删光，夺取永远拿不到东西。已改为先夺取后驱散。
2. 墨染山河：数量写成 ContextDice.Value(Zero, 2, 0) = 2 个零面骰 = 0 只墨灵。已改为 (Zero, 0, 2)。
3. 墨染山河：持续时间用 Rank，但法术上没挂 rank 配置（Rank = 0，墨灵一出来就消失）。已补 rank = 圣武士等级。
4. 本地化包加载：BlueprintCore 文档里 LoadLocalizationPack 的参数是 JSON 文件路径，不是目录。
   已改为 Path.Combine(ModPath, "LocalizedStrings.json")；若实测它接受目录，改回即可。

交叉检查发现、未改、需要你定的：
5. 【v0.3.6 已修】集火换目标（ContextActionRemoveMyBuffs）：Player.AllCrossSceneUnits 只含队伍成员，
   标记挂在敌人身上，旧标记扫不到 → 实际会出现多个标记同时存在。需要一个包含敌人的单位集合
   （候选 Game.Instance.State.AllUnits，请在程序集里确认名字），或改为在标记增益上记录施法者、
   新标记施加时由施法者侧移除。
6. 【v0.3.6 已修】王冠的回响夺来的增益走 UnitHelper.AddFact，按蓝图默认持续时间，不保留原剩余时间
   （规则写"按原剩余持续时间"）。要严格按规则，需要找能传持续时间的挂增益接口。
7. 墨灵：去掉 SetPrefab 与 AddClassLevels 后即使 Refs 填实，墨灵也没有模型、生命骰为 0。
   模型要 AssetLink（PrefabLink），等级要 AddClassLevels 组件——这两项留作召唤组下一步。
8. 【v0.3.6 已修】墨灵自爆直接加 Damage，不走伤害规则：不受抗力/减免、不显示伤害数字、也不是力场类型。

v0.3.3 → v0.3.4（召唤与镜像落地）：
- 新增 src/SummonsSetup.cs：墨灵单位蓝图（手搓，UnitConfigurator）、墨灵自爆增益、
  镜像行为锁增益（ForbidSpellCasting{ForbidMagicItems:true} + ExAndSuAbilitiesForbidden）。
- 墨灵单位的种族/阵营/AI/模型四个原版引用待填实（SummonsSetup.Refs，任一为空则跳过并记日志）。
- 镜中流形已实现：ContextActionSpawnMirrorCopy 用引擎自带 UnitHelper.Copy 全量复制目标
  （属性/AC/攻击/武器/专长/装备全继承），再配行为锁增益与 3 轮召唤挂靠。
  复制体生命已确认为目标当前生命值（此前 ÷2 的偏差已修正）。
- 墨染山河已接线原版召唤动作（ContextActionSpawnMonster 公开字段直配）。
- 新增组件 InklingDeathBurst（墨灵死亡时相邻爆发，豁免接线待补）。
- Special 类法术新增 Friendly 标记（镜像/重构/文明/永恒注视可点选友军）。

v0.3.2 → v0.3.3（接口全表核对后落地）：
- Guids.Vanilla 全部填实（21 个原版蓝图 + 2 个合书登记点本体 + 4 个 SpellbookMerge mod 登记点）。
- 学派进蓝图：64 个法术加 SpellComponent.School（从描述文本推断），法术专攻等按学派的效果生效；
  火焰/寒冷/电击/光/影响心灵/恐惧/睡眠同时写入 SpellDescriptorComponent.Descriptor。
- 画中世界按 Banishment 的动作树接线（Will 豁免 → 失败 Kill）；跨位面条件省去，
  “放逐 2 轮后回来”原版无现成机制，记偏差（降级方案：麻痹 2 轮 + 隐形）。
- 直线投递补 type: AbilityProjectileType.Line 具名参数（灼烧 / 耀阳颔首 / 穿云）。
- 法术书 SetIsArcane(true)（合书前提）；10 环指令从主表拆出，独立小表挂 MythicSpellList
  （Guids.MythicSpellList，主表只剩 0–9 环）。
- 合书登记完成：天使、巫妖（本体）+ AEON、灵使、恶魔、诡计大师（SpellbookMerge mod，
  逐条 try/catch，无该 mod 自动跳过，不加硬依赖）。

v0.3.1 → v0.3.2（按真实签名实现复杂机制的第一批）：
- 点爆发范围：剑雨/火山/黄昏/雷霆审判/大地之怒/冰封领域/影之疆域/醉梦/儿时的舞乐/
  王冠回响/时之锁/钙质化/延时爆破/烟幕掩护 已加 SetCanTargetPoint + AddAbilityAoERadius。
- 直线：灼烧（60 尺）、耀阳颔首（120 尺）、穿云（200 尺长直线）已加
  AddAbilityDeliverProjectile(length:, lineWidth:)。
- 已知偏差：闪光弹暂以 15 尺点爆发近似 30 尺锥形（2.8.7 无锥形投递），描述文本不改。
- 复活组：重构 Resurrect(resultHealth: 0.5f)、文明的存续 Resurrect(resultHealth: 1.0f)；
  "至多三名/五名"与先完全恢复的顺序仍需自定义动作（VERIFY）。
- 时空静止：直接 CastSpell 原版 TimeStop（表观 1d4+1 轮，已确认接受）。
- 王冠回响：ContextActionDispelMagic（失败驱散全部）+ 自定义 ContextActionStealBuffs
  （夺走至多 2 个可驱散增益转给施法者；"分给任意友军"的分配界面是后续工作）。
- 不稳定血浆：每轮自伤改自定义 ContextActionLoseHitPointsClamped（保 1 线）。
- 镜中流形规则加强：复制体数值与本体相同、不再一碰就散（规则文档第 7 版）。
  召唤单位蓝图（墨灵/镜像）与放逐（画中世界，需抄 Banishment 动作树）仍是骨架，
  属单独一组大活。
- 神话合书登记已实现：FeatureSelectMythicSpellbookConfigurator.For(...).AddToAllowedSpellbooks，
  天使/巫妖两个登记点的完整 GUID 待从 BubblePrints 填实，其余道途从同蓝图的 All 列表补。
- 本地化键全部改为字面量（L10n.S("字面键", ...)），抓取器不再需要展开拼接键。

v0.3.0 → v0.3.1（对着真实编译结果修正）：
- 补 using：Components（PubSubSystem）、SpellsSetup（EntitySystem.Stats / FactLogic）、
  Specter / Archetype（CustomConfigurators / EntitySystem.Stats）、Spellbook（EntitySystem.Stats）。
- DodgeVsAoO 搬回 RuleAttackRoll（RuleCalculateAttackBonus 上没有
  RuleAttackWithWeapon / SetAttackBonusPenalty）。
- AddSpellPenetration 改为两参数；AddModifierTargetRelated 改挂到每段伤害实例上。
- Actions.cs 重写（此前的壳层改写损坏）。
- L10n.S 改回返回 string 键（LocalizedString 没有可按键解析的字段）；
  Content 改用 BlueprintCore 自带的 LocalizationPack.Load（游戏原生 LoadPack 要 locale 且格式不同）。
- SuppressBuffsFixed 改预构建名单 + 配置器 AddSuppressBuffsFixed；
  AddFacts 组件改配置器 AddFacts(List)；RemoveComponents 泛型重载改谓词式。
- 变体增删表改 LevelEntryBuilder.New().AddEntry(...).GetEntries()，AddEntry 元素显式
  cast 为 BlueprintFeatureBaseReference；SetRemoveSpellbook() 无参；
  SetAddSkillPoints(2) 搬回变体链。
- Guids.Vanilla 的空串占位已在本地构建副本填实（站点副本保留占位，以本地副本为准）。
- LocalizedStrings.json 不再手写：由生成脚本产出（修复了 Bonuses={...} 截断导致
  8 个法术漏键的 bug，共 181 条），站点副本以脚本产物为准。

v0.2 → v0.3：
- 包引用 BlueprintCore → WW-Blueprint-Core 2.8.7；Harmony 改引游戏自带 0Harmony.dll。
- 原蓝图克隆流程由 CopyFrom + RemoveComponents + 手术完成；圣疗只克隆 self/others，
  引导只换 DC 不换资源。v0.3.7 另新增未调用的 Clone.Of<T> JSON 深拷贝工具。
- 全部字符串移到 LocalizedStrings.json；L10n.S 只返回按键解析的共享条目。
- 规则钩子组件去掉重复的 IRulebookHandler<T>/ISubscriber 接口（否则双触发）。
- 恶灵开关去 Conditional/HasFact，改"开启能力 + 形态增益附带结束能力"双键结构，
  新增"结束恶灵"自由动作能力。
- 恶灵锁定/20 级压制改用 SuppressBuffsFixed；被压制视为未生效（IsSuppressed）。

v0.1 → v0.2（资源制 → 法术书方案）：
- 删除理智值资源与"按阶选指令"：指令全部改为法术，升级界面随法术书学习。
- 集火改为职业特性（每日 3 + 智力调整值 次），不再是法术。
- 新增 0 环戏法 6 个、1–9 环各 6 个、10 环 4 个（实验阶段：测试面板按钮发放）。
- 新增源石技艺精通（穿透 +2/+4/+6/+8，恶灵中倒扣，20 级取消）。
- 恶灵期间从"禁用全部指令"改为"仅能施放【恶灵】指令"。
