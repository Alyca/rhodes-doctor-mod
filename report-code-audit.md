# 全量代码扫描与分析报告 — 罗德岛博士 RhodesDoctor

- **扫描对象**：本项目（`D:/SteamLibrary/Mods/rhodes-doctor-mod`）。
  项目即面向《开拓者：正义之怒》（Pathfinder: Wrath of the Righteous，即用户所称"正义之怒"）的
  UnityModManager 职业变体 mod「罗德岛博士 Rhodes Island Doctor (Paladin Archetype)」，
  `Info.json` 版本 0.6.1 / UMM 0.27.0。项目内无其它子 mod。
- **扫描范围**：`src/*.cs` 全部 30 个文件（13,214 行）、`RhodesDoctorMod.csproj`、`Info.json`、
  `LocalizedStrings.json`（273 键）、`docs/`、`refs/`。
- **交叉验证**：GUID 唯一性（`Guids.cs` 无重复/无段位碰撞）、本地化键覆盖率（代码引用键 0 缺失）、
  死代码引用检索（见 §7）。
- **严重度分级**：**P0** 阻断/存档损坏；**P1** 高（玩家可见故障或数据风险）；**P2** 中（性能/兼容/维护）；
  **P3** 低（清理/文档）。
- **总体结论**：工程化程度明显高于同类 mod —— 模块化注入（逐个 try/catch）、
  `[Conditional]` 零成本日志、特效全链路"克隆不碰原版"、大量"踩坑注释 + 反编译证据"。
  **未发现 P0 级问题**（无阻断加载、无确定性崩溃、无必然存档损坏路径）。
  存在 2 处 P1 风险点（存档数据往返、放逐单位找回兜底）、一批 P2（反射静默失败、克隆资源回收、
  性能细节）与若干 P3 清理项。

---

## §1 整体目录结构与核心文件职责

### 1.1 目录总览

| 目录 | 职责 |
|---|---|
| `src/` | 全部 C# 源码（30 文件，单一程序集 `RhodesDoctor.dll`） |
| `Info.json` | UMM 清单（Id/RhodesDoctor、EntryMethod=RhodesDoctor.Main.Load） |
| `LocalizedStrings.json` | 简体中文/英文本地化包（273 键，无重复，运行期加载） |
| `LocalizedStrings.json` 同层 `RhodesDoctorMod.csproj` | net48 类库工程；游戏路径参数化（WrathPath/WOTR_PATH） |
| `icons/` | 70 张 PNG 图标（`icons/png-64/` 按需读为 Sprite）+ 借图机制 |
| `docs/` | 规则文档、开发笔记（dev-notes）、特效方案 |
| `refs/` | 原版蓝图 JSON 摘录（65 个）+ 游戏 API 参考 |
| `evidence/` | 反编译/实测证据文本（16 份） |
| `report-*.md` | 历次工作报告（本报告为其中一员） |

### 1.2 `src/` 分层与核心文件职责

| 层 | 文件 | 职责 |
|---|---|---|
| 入口 | `Main.cs` | UMM 入口；6 个事件订阅（特效 Watcher）；`BlueprintsCache.Init` Postfix 注入（`_done` 幂等） |
| 装配 | `Content.cs` | 模块化注入：6 个模块各自 try/catch（一个失败不拖垮其余） |
| 装配 | `ArchetypeSetup.cs` | 变体本体：成长表增删、集火、源石技艺精通、冷静头脑、灵光、圣疗/引导正能量克隆 |
| 装配 | `SpellbookSetup.cs` | 战术指令法术书：自建"术士表+10 环列"双表、法术列表、神话合书登记 |
| 装配 | `SpellsSetup.cs` | **核心**：64 条指令 Spec 表驱动创建（目标规则/范围/豁免/描述符/特效接线） |
| 装配 | `SpecterSetup.cs` | 巴别塔的恶灵：三阶段形态、锁定、余波、20 级 Capstone |
| 装配 | `SummonsSetup.cs` | 墨灵单位蓝图、镜像行为锁、模板外观照抄 |
| 装配 | `TestSetup.cs` | 测试木桩（3 单位 / 8 增益，`IsFromSpell` 反射标记） |
| 机制 | `Components.cs` | 24 个规则钩子组件（豁免/伤害/死亡拦截/形态收敛等），R1"每效果 Data"存档结构 |
| 机制 | `Actions.cs` | 30+ 自定义 ContextAction + 4 个 MonoBehaviour（FallingFx/FollowFx 等） |
| 机制 | `UnitScan.cs` | 全局单位池快照（State.Units ∪ 跨场景队伍 ∪ Party，解决"队伍不在区域状态"问题） |
| 特效 | `FxTint.cs` | **重染引擎**：克隆重染（粒子/材质/贴图/网格顶点色/脚本字段）+ 贴图缓存 |
| 特效 | `FxLibrary.cs` / `FxBorrow.cs` | 素材借用（区域 Fx / 法术爆发 / 增益 FxOnStart / 武器模型）+ 负缓存 |
| 特效 | `AreaGroundFx.cs` | C 类地面区域注册表（克隆+重染+跟区域同生同灭+渐隐） |
| 特效 | `SmokeFx/BeaconFx/BeaconPillarFx/MedicalDroneFx/HasteTrailFx/TintedAuraFx/SwordStreakFx/SwordHitFx/BombFx` | 各专项特效 + 6 个 `I*Handler` Watcher（生命周期订阅） |
| 工具 | `Guids.cs` | 全部 GUID（前缀 d0c7 分区编号；发布后不可改） |
| 工具 | `L10n.cs` / `Dbg.cs` / `Clone.cs` / `Icons.cs` | 本地化键 / `[Conditional("RD_VERBOSE")]` 日志 / 蓝图深拷贝工具（无调用方）/ 图标缓存 |
| UI | `TestPanel.cs` | UMM 面板：召唤木桩、回血、清除、发放指令（实验） |

---

## §2 依赖与引擎版本兼容性

### 2.1 依赖清单

| 依赖 | 来源 | 说明 |
|---|---|---|
| .NET Framework 4.8 | 目标框架 | WotR Unity/Mono 运行时兼容 |
| **WW-Blueprint-Core 2.8.7** | NuGet | 蓝图构建主力；与"游戏数据版本 2.8.7"强耦合（注释多处可见） |
| Assembly-CSharp.dll | 游戏 `Wrath_Data/Managed`（优先 `publicized_assemblies`） | 有公共化→非公共化的回退链 |
| UnityModManager.dll / 0Harmony.dll / Newtonsoft.Json.dll | 游戏自带 | 不随包发布（`Private=false`），避免版本冲突——**写法正确** |
| UnityEngine 模块（Core/IMGUI/ImageConversion/ParticleSystem/Audio/InputLegacy） | 游戏自带 | 按功能精确引用 |
| SpellbookMerge mod（4 道途合书 GUID） | 第三方 mod | **软依赖**：逐条 try/catch，无该 mod 时跳过，不加硬依赖 |
| DLC5/DLC6 蓝图（素材候选） | 游戏 DLC | 仅作"借用候选"，缺失时优雅降级 |

### 2.2 兼容性风险

- **P2｜游戏版本锁定 2.8.7 时代**：注释中大量引用"2.8.7 反编译结论"；游戏升级（数据版本变更）后
  BlueprintCore 与私有成员都可能失效。建议升级前跑一遍回归清单（编译 + 进游戏读日志 6 模块注入）。
- **P2｜反射依赖私有成员（5 处，且失败静默）**：
  `BlueprintAbilityAreaEffect.m_TargetType`（私有嵌套枚举，`ArchetypeSetup.SetAreaTargetType`、`SpellsSetup.SetPrivateField`）、
  `AuraFeatureComponent.m_Buff`（`SetAuraBuff`）、`WeaponVisualParameters.m_WeaponModel`（`FxBorrow.WeaponModelFx`）、
  `BlueprintBuff.m_Flags`（`TestSetup.MarkAsSpell`）。
  除 `WeaponModelFx` 有失败日志外，其余三处用 `?.` 链**静默吞失败** —— 游戏改字段名时表现为
  "灵光不给友军 / 区域类型错 / 木桩不标可偷"，且**日志里什么都看不到**。详见 §6-P2。
- **P3｜公共化程序集回退**：csproj 在无 `publicized_assemblies` 时回退非公共化 Assembly-CSharp；
  若部分代码依赖 internal 成员，则该回退实际不可编译。建议实测一次"无公共化"编译路径或移除误导性回退。
- **P3｜DLC 素材候选**：`FxLibrary`/`AreagroundFx` 使用 DLC5/DLC6 素材仅作候选；`TryGet` 失败即跳过。行为安全。
- **P3｜合书登记**：`RegisterForMythicMerge` 对 6 条道途逐条 try/catch —— 已正确隔离第三方 mod 缺失。

---

## §3 关键功能模块实现逻辑（摘要）

1. **变体成长表**（`ArchetypeSetup.CreateArchetype`）：`SetReplaceSpellbook` + 移除 12 项原版特性
   （1/2/2/4/4/7/8/10/11/13/16/19/20 级）+ 增补 11 行本 mod 特性（1/2/4/7/8/11/12/13/16/19/20 级）。
2. **战术指令法术书**（`SpellbookSetup`）：术士式自发书、智力施法、自建"每日法位表 + 已知法术表"各追加
   第 11 列（10 环）；**门槛行 `TenRingRow = 21`** —— 依赖实测 quirk"博士法术书等级 = 职业等级 + 1"
   （来源：1 级 `AddSpellbook` 特性与变体链 `SetReplaceSpellbook` **双来源**，见 §6-P2）。
   10 环走升级界面获取（与其它环同路径）。
3. **巴别塔的恶灵**（`SpecterSetup` + `Components`）：8/12/16 级三段；
   形态增益（敏捷/体质 × 3 段，转移量 = 智力调整值一半→全额）挂"锁定"增益
   （限【恶灵】指令 + 穿透倒扣 + `SuppressBuffsFixed(applyToNewBuffs)` 持续压制灵光）；
   每轮 `ContextActionSpendResourceOrEndBuff` 扣资源；结束→余波 2 轮；20 级 Capstone 持续压制
   全部锁定+余波实现"人灵合一"。开启硬互斥与"结束恶灵"单动作清理均为已修问题后的稳定形态。
4. **64 条指令**（`SpellsSetup`）：`Spec` 表（环/距离/施法动作/Kind/范围/骰/豁免/目标类型）+
   `Meta` 表（学派/描述符/SR/持续/豁免显示）；`CreateSpell` 按目标规则四级分派
   （个人/友军/敌人/落点），范围三级（点爆发 TargetsAround / 直线真实投射物 / 自扫场景）；
   `ApplySpecial` 30 余个特例分支（放逐/召唤/镜像/克隆/反噬/区域等）。
5. **召唤与镜像**（`SummonsSetup` + `Actions.ContextActionSpawnMirrorCopy`）：墨灵 = 手搓单位
   （照抄小型气元素 Visual/Body、7 项种族/专长包、主人 BAB + 2d6 力场、死亡自爆）；
   镜中流形 = `UnitHelper.Copy` + 摆位 + `AddEntity` + 法术位/资源 `Rest()` + `RuleSummonUnit`。
6. **特效双轨制**：①"不改色"走声明式（`area.Fx` / `AbilitySpawnFx` / `FxOnStart`）；
   ②"要改色/改尺寸"走运行时克隆重染（`FxTint` 引擎 + 8 个 Watcher / 注册表管理生命周期）。
   全部只改克隆体，原版素材不碰——这一约定执行得相当彻底。

---

## §4 性能与内存风险评估

> 结论先行：**未发现"每帧泄漏"或"随游玩时间线性膨胀"级别的严重泄漏**；
> 主要风险是"**每次施放产生的一次性克隆资源（材质/网格/贴图）缺乏显式回收，依赖引擎在场景切换时兜底**"，
> 以及若干可低成本消除的热点。

### P1 级

- **P1｜克隆资源回收无显式策略（主要是 `FxTint` 与 `BombFx`）**
  - `FxTint.Apply` 里 `renderer.materials`（复数）会把每个渲染器的材质**实例化一份**、
    `WhitenMeshes` 可能克隆网格、`TintedTexture/Desaturated/ScaledAlpha` 克隆贴图；
    这些克隆在 GameObject 销毁时**不会**被显式销毁（Unity 常规行为，靠 `UnloadUnusedAssets`/场景切换回收）。
  - `BombFx.PlaceMarker → AddRadiusCircle` 每次布设**新建 1 个 Mesh（73 顶点） + 2 个 Material**，
    无缓存、无销毁（对比 `SwordHitFx` 的材质在 `OnDestroy` 里有显式 `Destroy(Mat)` —— 那是正确示范）。
  - 影响：单次施放量小；但"长时间单场景 + 大量施放"（或反复布设延时爆破）会累积未回收对象，
    表现为内存缓慢上涨、场景切换后回落。
  - 建议：① `BombFx` 把 disc Mesh 与两个圈材质改为**静态缓存 + 复用**（随 marker 销毁时仅销毁 GO，
    不重建资源）；② `FxTint` 在每次克隆时记录"本实例创建的材质/网格清单"，
    由销毁挂钩（或调用方）统一 `Destroy`；③ 至少为 `SwordRain`（一次 18 份 × N 材质）按施放次数评估。

### P2 级

- **P2｜`FxTint` 反射调用无 MemberInfo 缓存**：`TintCustomScripts` / `NeutralizeMaterial`（经
  `GetProperty("material")` 分支）在每个克隆**每个组件**上现做 `GetFields/GetProperties/GetProperty`。
  高频施放（剑雨 18 份克隆）时是纯重复劳动。建议按 `Type` 缓存字段/属性列表（`Dictionary<Type, ...>`）。
- **P2｜`AreaGroundFx.LogAreaView` 缺少 `[Conditional("RD_VERBOSE")]`**：对比 `LogInventory`（有）——
  该方法的**调用没有被编译期剔除**，且其内部的三段 `sb.Append(...)` 语句在 release 下仍会执行
  （仅 `Dbg.Log(...)` 调用行被删）→ 每次**已注册区域**生成时白白全树枚举 + 构建 3 份 StringBuilder。
  修复：方法上加 `[System.Diagnostics.Conditional("RD_VERBOSE")]`（一行）。
- **P2｜静态缓存无上限/无淘汰**：`FxTint.TexCache`（键含贴图引用）、`AlphaCache`、`FxLibrary.Cache`、
  `Icons.Cache/Borrowed`、`SwordStreakFx.Mats`。均为"素材 × 配色"有界组合，正常游玩不构成泄漏；
  但贴图克隆驻留 VRAM。建议加容量上限（如 256 条 LRU）或随区域销毁释放对应贴图（进阶）。
- **P2｜事件订阅与字典生命周期**：
  - 6 个 Watcher 在 `Main.Load` 订阅后**无卸载路径**（`Info.json` 未定义 OnUnload）；
    UMM 重载场景会**双重订阅**（`Main.Load` 无重入防护），导致同一事件处理两遍（表现为特效重建抖动）。
  - `TintedAuraFx/HasteTrailFx/MedicalDroneFx/AreaGroundFx/SmokeFx/BeaconFx` 的 `Live` 字典只在
    **下一次事件**时 `Prune()`；单位销毁不即时清理条目（内存量小，可接受）。
  - 建议：`Main.Load` 加静态 `_loaded` 守卫；记录订阅列表并提供清理方法。
- **P2｜`DelayedRetint` 每区域再跑 3 次全量重染**（0.35/1.2/3.0s）：每次遍历全树 + 贴图（有缓存）。
  一次性开销可接受；若区域法术高频率使用可考虑降为 2 次。

### P3 级

- **P3｜施法路径分配热点**：GUID 归一化 `Replace("-","").ToLowerInvariant()`（每次施法/每次限制检查）、
  `UnitScan.Take()` 每次分配 List+HashSet、`ContextActionSpawnFxRing` 中每把剑多次
  `GetComponentsInChildren`。单次量小，密集战斗下有 GC 抖动可能。可选优化：GUID 直接用
  `BlueprintGuid` 结构比较；组件查找结果缓存。
- **P3｜`CloudExtentLog` 热键（`[` `]`）常驻**：云存在期间全局生效——调参期工具，可接受。
- **P3｜`TestPanel.GrantAllSpells` 绕过成长名额**：实验按钮保留在发布版；设计上已做幂等
  （先 `RemoveSpell` 再 `AddKnown`）+ `Rest()` 自愈，误触可恢复。

---

## §5 Bug 与不稳定因素（按严重度）

### P0 — 无
未发现阻断加载、确定性崩溃、必然存档损坏的问题。所有注入模块独立 try/catch；
运行时钩子绝大多数带防护（个别例外见 P1-1）。

### P1（高）

- **P1-1｜岁稔·余温把"运行时修饰器对象"写进存档 Data（`Components.cs` L1145-1149 / L1167-1171）**
  - 现象：`HarvestYearsResidualData.Modifier` 标了 `[JsonProperty]`，会把一个
    `ModifiableValue.Modifier`（含运行时 source 引用）序列化进每个效果自己的存档 Data。
  - 风险：读档后若反序列化出**孤立 Modifier**（挂在任何 Stat 之外）：
    ① `OnActivate` 的守卫 `Data.Modifier == null` 不成立 → **临时生命不会重建**（效果静默丢失）；
    ② `OnDeactivate` 对孤立对象调 `Remove()` 可能抛异常，而此处**是全文件少数没有 try/catch 的
    OnDeactivate**（对比 `ChainDetonationOnDeath`、`PaintedWorldBanishTracker` 都有防护）→
    可能打断增益移除链。
  - 建议（低成本明确收益）：`Modifier` 改 `[JsonIgnore]`；`OnActivate` 只依赖 `Amount` 重建；
    `OnDeactivate` 包 try/catch。
  - 验证方法：存档 → 施放岁稔并等它结束 → 存/读档 → 检查临时生命与 Player.log。

- **P1-2｜画中世界：单位找回失败时无兜底，可能永久 `IsInGame=false`（`Components.cs` L534-583）**
  - 现象：`OnDeactivate` 先写 `Data.Banished = false` 再 `FindUnit`；找不到即
    "跳过返回"（仅 `Dbg.Log`，默认编译期删除 → release 下**无任何日志**）。
  - 风险：被放逐单位若从全局池消失（召唤物解散、场景切换、异常路径），将没有任何恢复手段，
    视觉上表现为该单位永久失踪（若实体仍存在）。
  - 建议：找不到时记 **Error 级**日志（`Main.Mod.Logger.Error`）；增加"区域切换/读档后"兜底扫描
    （宽限期重试 + 最后手段全局遍历），并在 `Main` 侧对 tracker 残留做一次启动自检。
  - 注：队友跨场景路径已由 `UnitScan` 修复（10-12），本条针对残余边界。

### P2（中）

- **P2-1｜反射写入私有成员失败静默（4 处）**：`SetAreaTargetType`/`SetAuraBuff`（`ArchetypeSetup`
  L265-274、`SpellsSetup` L2071-2074）、`SetPrivateField`（`SpellsSetup` L2281-2284）用 `?.` 链，
  字段缺失/改名时**不报错不写日志**——游戏更新后表现为"核心功能静默失效"，排查成本极高。
  建议：统一加"字段未找到 → `Main.Mod.Logger.Error`"。
- **P2-2｜法术书双来源（已知开放项）与 `TenRingRow = 21` 强耦合**：1 级 `AddSpellbook` 特性 +
  变体链 `SetReplaceSpellbook` 各计一次等级（实测 20 级时法术书等级 21）。10 环门槛写死 21 ——
  若日后修复双来源（或其它 mod 干预），**10 环解锁会静默不开放**（有"共 N 行"日志可诊断）。
  建议：实测 4 级以上法位是否翻倍（既有遗留项①）；把"法术书等级"改为运行时计算门槛而非常量。
- **P2-3｜`Main.Load` 中 `Harmony.PatchAll` 未捕获异常**（L48）：若补丁失败，异常上抛 →
  UMM 报"加载失败"，且此前已完成的事件订阅残留（半加载状态）。建议包 try/catch + Error 日志、
  失败时 return false。
- **P2-4｜连锁殉爆的同步重入深度**（`Components.cs` L663-702）：爆炸→击杀→其它标记
  `OnDeactivate`→再爆炸，为**同步链式**（设计内的连锁效果）；极端场景（一次性引爆大量标记者）
  理论上有深递归/长卡顿。建议：`Explode` 里加"正在结算"守卫或把后续爆炸入队延迟一帧；
  或在 20 目标以上时降级为队列结算。
- **P2-5｜`Specs` 顺序与 `Guids.Spell(i)/Buff(i)` 下标严格绑定**：任何"中间插入/删除/重排"都会
  静默改变所有后续 GUID 映射（注释已警示，但无编译期/运行期防护）。建议：启动时断言各环数量
  与快照一致（或显式编号 + 校验日志）。

### P3（低 / 清理）

- **P3-1｜死代码清单**（多为迁移后残留；"有意留档"的已在代码内注明）：
  `SpecterSpellRestriction`（旧 ForceFail 方案，B19 后未接线）、`SpellsSetup.SetAuraBuff`
  （重复副本，未调用）、`SpellsSetup.PrivateDispelBuffType`（未调用）、`Clone.Of`（注明保留）、
  `ContextActionSpawnFxBetweenSummons`（注明备用）、`ContextActionTeleportCasterToPoint`（注明被取代）、
  `FxLibrary.SwordField/SwordWall/SwordDrop/FearAura/MedicalDroneGlow`、`FxLibrary.AuraSpells`（空结构）、
  `AreaGroundFx.MakeLoopsContinuous/MakePersistent`（注明已撤）。
  建议：删除未注明用途的 2-3 处（`SpecterSpellRestriction`、`SetAuraBuff` 副本、`PrivateDispelBuffType`），
  其余维持现状即可（留档价值大于清理价值）。
- **P3-2｜文档漂移**：`SpellbookSetup.cs` L82 注释引用不存在的 `CreateTenRingFeature`（方案已换为双表）；
  多处注释中的版本号/行为与现状需随文件维护。
- **P3-3｜`TestSetup.MarkAsSpell` 无空值防护**（L164-171）：`field.GetValue(buff)` 在字段缺失/
  蓝图缺失时 NRE → 木桩模块整体失败（被 `Content.Run` 捕获，仅表现为"测试木桩不可用"）。
  建议加空判断 + 明确日志。
- **P3-4｜手工耦合参数**：剑雨 `FallSeconds = 0.4`（SpellsSetup L1489）与
  `ContextActionSwordHitFx.Delay = 0.4`（Actions L1766 默认值）必须同步——注释已强调；
  建议抽成共享常量。
- **P3-5｜`ContextActionRemoveMyBuffs` 未统一到 `UnitScan`**（仍 `CurrentScene ∪ AllCrossSceneUnits`）：
  功能正确（合并后含队伍），仅为一致性建议（既有遗留项②）。
- **P3-6｜`TestPanel.Dummies` 静态列表**：跨读档残留引用（仅测试面板，量小）。

### 已在代码注释中自认的规则偏差（非新发现，供知悉）

恶灵余波用 `Nauseated`（对恶心免疫者不生效）；旗帜不倒用 `UnitHealthGuard`（昏迷免除待验）；
炮火延伸同点三轮（规则为三点）；天灾降临整区域伤害（规则为六点选放）；时空静止用原版 TimeStop
（1d4+1 表观轮，略慷慨）；紧急撤离固定"施法者身后"落点；处决/缄默等对描述符边界的近似处理；
画中世界放逐 2 轮为固定值。以上均有注释记录，行为可预期。

---

## §6 改进建议（按优先级）

| 优先级 | 行动 | 成本 | 收益 |
|---|---|---|---|
| 1 | **P1-1** `HarvestYearsResidualData.Modifier` → `[JsonIgnore]` + OnActivate 重建 + OnDeactivate try/catch | 极低 | 消除存档往返风险 |
| 2 | **P1-2** 画中世界找回失败 → Error 日志 + 兜底恢复扫描 | 低 | 消除"单位永久失踪"边界 |
| 3 | **P2-1** 4 处反射写入补失败日志 | 极低 | 未来兼容问题可诊断 |
| 4 | **P2-2** 实测法术书双来源法位翻倍问题；10 环门槛改运行时判定 | 中 | 规则正确性 + 解除强耦合 |
| 5 | **P2-3** `Main.Load` PatchAll 包 try/catch + Load 重入守卫 | 极低 | 加载健壮性 |
| 6 | **P2｜§4** `LogAreaView` 补 `[Conditional]` | 极低 | 消除 release 版白工 |
| 7 | **P1｜§4** `BombFx` 网格/材质模板化缓存；`FxTint` 克隆资源登记回收 | 低 | 消除施放级小量泄漏 |
| 8 | **P2｜§4** `FxTint` 反射 MemberInfo 缓存 | 低 | 高频施放 CPU 抖动 |
| 9 | **P2-4** 连锁殉爆重入守卫 | 低 | 极端场景稳定性 |
| 10 | **P3** 死代码清理 + 文档漂移修正 + 共享常量 | 低 | 可维护性 |

---

## §7 验证证据摘要

- `Guids.cs`：全部手工后缀无重复；与 `Spell(0xa0-0xdf)` / `Buff(0x40-0x7f)` 动态段无碰撞（脚本校验）。
- `LocalizedStrings.json`：273 条键、**0 重复**；6 个装配文件引用的 `rhodesdoctor.*` 键**全部存在**（0 缺失）。
- 死代码检索：`SpecterSpellRestriction` / `PrivateDispelBuffType` / `SpellsSetup.SetAuraBuff` /
  `Clone.Of` / `ContextActionSpawnFxBetweenSummons` 等确认无调用方（grep 全量命中仅为定义与注释）。
- 所有"运行时堆资源"路径（材质/网格/贴图/字典/订阅）已按文件逐一核对回收点，结论见 §4。
