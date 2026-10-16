# 火力侦察 / 终末狙击 · 专属自绘特效（2026-10-16）

> 作者要求：两个法术**都不再借「火之烙印」**（头顶一团火），改成自己画的线条特效 ——
> **火力侦察（克洛丝，琥珀色）：侦察锁定框**；**终末狙击（黑，红色）：红外激光 + 准心锁定**。
> 两个法术**共用一个新的公共文件**，共改 6 处（新建 3 个文件 + 修改 3 个文件）。
> 编译：`dotnet build -p:WrathPath="D:\steam\steamapps\common\Pathfinder Second Adventure"` **0 警告 0 错误**；
> 已部署（旧 DLL 备份 `RhodesDoctor.dll.bak-20261016-134542`，新 DLL md5 `B14C5E38F3A81A8AB9AE653C47D4C2B0`，
> 与 `bin\Debug\net48` 产物一致）。**待作者进游戏确认**。
> ⚠ 首次进游戏截图后按作者反馈微调过一次（**火力侦察的框整体抬高**，见 1.1 末尾）——
> 该次改动前的备份为 `RhodesDoctor.dll.bak-20261016-135202`。

## 一、演出

### 1.1 火力侦察（`FireReconFx`）—— 琥珀色侦察锁定框

| 内容 | 实现 | 参数 |
| --- | --- | --- |
| 四角**竖直锁定框**（始终正对镜头） | 4 条 3 点折线，`LineAlignment.View`；水平方向用**相机右向量压平**、竖直方向用世界 up | 入场 `IntroDur = 0.30 s` 从 **1.7 倍**（宽）/ **1.4 倍**（高）收紧到位；之后 ±3% 呼吸 |
| 收紧过程**快闪** | 入场期间 alpha 做 16 Hz 方波（1.0 / 0.35） | 入场结束即停 |
| 框内**自上而下**扫描线 | 2 点横线，y 从 `+halfH` 线性走到 `-halfH` | `ScanPeriod = 1.1 s`；两端用 `sin` 淡出 |
| 框顶**倒三角** | 3 点闭合折线：单顶点在下、两顶点在上 | 上下浮动 ±6 cm |
| 脚下**雷达回波环** ×2 | 两条贴地圆环，**错开半个周期**，半径 0.4R → 2.4R 缓出扩散 | `PingPeriod = 1.6 s`；越扩越淡 |

琥珀 `(1.00,0.60,0.12)`＝主色；淡黄 `(1.00,0.90,0.55)`＝内圈回波 / 扫描线。

> ⚠ **10-16 作者进游戏实测后的微调：侦察框整体抬高** —— 框中心从 `Height × 0.5`（正好居中）改为
> **`Height × 0.62`**（`FireReconAnim.BracketCenter`，≈ 2 m 模型抬高约 0.24 m）。
> 框体 / 框内扫描线 / 框顶倒三角是**同一套**，一起跟着这个系数走；用比例而非固定米数，大体型也等比抬。
> **想再上/下只改 `BracketCenter` 这一个数。**（改后重新编译 + 部署。）

### 1.2 终末狙击（`FinalSnipeFx`）—— 红色红外激光 + 准心锁定

| 内容 | 实现 | 参数 |
| --- | --- | --- |
| **红外激光**：施法者枪口 → 目标胸口 | 2 点线，每帧两端都读实时位置 → **两端都跟着人移动** | 入场 0.25 s 内"射出去"（终点插值），之后 `sin(41t) + sin(17t)` 轻微闪烁 |
| 目标胸口**红色准心** + **四根刻度线** | 正对镜头的圆（相机基向量 billboard，48 段）+ 4 条从 `0.55r` 到 `1.35r` 的径向短线 | 入场 0.45 s：半径 2.6R → 1R、整体**旋转 45° → 0°**（"旋转收缩到位"） |
| 锁定后**脉动越来越快** | alpha 脉动频率 `Lerp(1.5, 5) Hz`（按 1 轮 ≈ 6 s） | 像倒计时 |
| 脚下细红圈 | 贴地圆环，半径 0.9R | 淡（alpha 0.4） |
| **开火**：红白曳光 + 命中点白色闪光环 | 粗红曳光（宽 0.14）+ 白芯（0.05）从枪口到胸口；命中点白色圆环 0.1 → 1.3 m 炸开 | 0.45 s 内一起收尽；同时**准心立刻淡出** |
| **隐形哑火** | 哑火分支在 `Fire()` 之前就 `return`，不产生曳光；标记被移除 → 准心走正常的 0.3 s 淡出 | —— |

## 二、共用的新公共文件（`MarkFxKit.cs`）

- **`MarkFxTracker`**：按增益蓝图名/GUID 过滤；增益加上 → 建 GameObject + 挂子类组件；移除 → 0.3 s 淡出；
  同目标还有另一份同名增益（两次施放）时不收；`Get(unit)` 供"开火"这类外部触发按目标取当前实例。
- **`MarkFxBase`**：跟随 / 淡出 / **每秒自检**（增益已不在就淡出，防漏事件残留）；
  子类只实现 `Build`（建线）与 `Tick`（每帧摆位）；`Tick` 异常会打 Error 并自毁（不会每帧刷屏）。
- **`MarkFxKit`**：`Line` / `Flat` / `Circle`（贴地圆）/ `BillCircle`（正对镜头的圆）/ `SetA` / `CamBasis` /
  `FlatRight` / `ModelHeight`。材质一律 `SwordStreakFx.StreakMaterialPlain` —— **不用 `Shader.Find`、不借原版粒子**。
- **`MarkFxWatcher`**：`IUnitBuffHandler` 单一订阅入口，分发给两个 `Tracker`（`Main.Load` 订阅一次）。

## 三、改过的 6 处

| # | 文件 | 改动 |
| --- | --- | --- |
| ① | `src/MarkFxKit.cs` | **新增**（公共件，见上） |
| ② | `src/FireReconFx.cs` | **新增**：`FireReconAnim`（侦察锁定框） |
| ③ | `src/FinalSnipeFx.cs` | **新增**：`FinalSnipeAnim`（激光 + 准心）、`FinalSnipeShotAnim`（曳光 + 闪光环）、`Fire()` |
| ④ | `src/FxLibrary.cs` | `DebuffAuraFor` 撤掉 `case "FireRecon"` / `case "FinalSnipe"`（→ `default` = 不给光环）；上方那段类注释同步 |
| ⑤ | `src/Actions.cs` | `ContextActionFinalSnipe.RunAction`：隐形 `return` 之后、`C24` 之前加 `FinalSnipeFx.Fire(caster, target);` |
| ⑥ | `src/Main.cs` | 加 `_markFxWatcher` 字段 + `SubscribeOne(ref _markFxWatcher, "标记特效（火力侦察 / 终末狙击）")`；订阅失败的日志文案补齐 |

- **连锁殉爆不受影响**：`MarkAura()`（火之烙印）仍被 `case "ChainDetonation"` 使用，只是火力侦察 / 终末狙击不再走它。
- **不会残留旧光环**：`ApplyAuraFx` = `AuraFor(key) ?? DebuffAuraFor(key)`，而 D 类名单 `AuraSpells` 是**空集**，
  收窄 `DebuffAuraFor` 之后这两个法术两条路都返回 null → 火之烙印彻底不再出现。
- **只动视觉**：火力侦察的"忽略失手几率"（`FireReconMark` 组件）、终末狙击的 15d6/7d6 + 2d6 体质与隐形哑火判定一律未动。

### 3.1 三处主动加固（与作者原稿的差异，都能一行回退）

1. **过滤多了 GUID 兜底**。原稿只按蓝图名（`RhodesDoctorBuffFireRecon` / `RhodesDoctorBuffFinalSnipeMark`）过滤；
   本项目其它常驻特效（`TintedAuraFx` / `MedicalDroneFx`）**一律按 GUID** 过滤（GUID 是存档里真实生效的键），
   而名字那一路要依赖 BlueprintCore 把 `New(name, guid)` 的 name 落到 `Blueprint.name` 上。
   现在 `MarkFxTracker.Is` **两条取"或"**，第二条是：
   - 火力侦察 → `Guids.Buff(10)`（`CreateSpellBuff(s, i)` 用 **Specs 下标** i，已核实 **`Specs[10].Key == "FireRecon"`**，
     全部 64 条索引已离线枚举核对：`Specs[0] == TacticalMark`、`Specs[10] == FireRecon`）；
   - 终末狙击 → `Guids.FinalSnipeMarkBuff`（**具名常量**，终末狙击是 `Kind.Special`，标记由
     `CreateExtraBuff("FinalSnipeMark", Guids.FinalSnipeMarkBuff, …)` 建，名字规则确实是 `RhodesDoctorBuff{name}`）。
   - ⚠ 若日后重排 `SpellsSetup.Specs`，火力侦察那个下标要跟着改（代码里有注释）。**只按 GUID 认上时会打一行
     `Logger.Log`**（release 也可见）把真实蓝图名报出来 —— 万一命名口径变了，日志能直接指出真名，不至于纯静默失效。
2. **施法者延迟解析**：`FinalSnipeAnim` 记下 `Buff`，每帧只要 `_caster` 还没解析出来就再试一次
   （`buff.Context.MaybeCaster` 在"增益刚加上"那一刻可能为 null）→ 避免"**激光整场不出现**"这种静默失效。
3. **材质失败不再静默**：`Build` / `FinalSnipeShotAnim.Init` 里 `StreakMaterialPlain` 拿不到材质时打一行 `Error`
   （原稿是直接 `return false`，屏幕上一片空白、日志里什么都没有）。

## 四、进游戏要看的点

1. **火力侦察**：目标身上是否出现**竖直**四角框、是否**正对镜头**（转视角时框应跟着转向）；
   入场是否从更大一档收紧 + 快闪；扫描线是否**从上往下**；框顶倒三角是否**朝下**；
   脚下是否有两道**错开**的回波环在扩散。
2. **终末狙击**：激光是否**两端都跟着人**（施法者/目标走动时跟着走）；准心是否**旋转收缩**到位后**越脉动越快**；
   刻度线是否 4 根；**开火**时是否有红白曳光 + 命中点白环，且准心**同时**消失；
   对**隐形**目标施放（哑火）应**没有曳光**、准心直接淡出。
3. 两者被消耗 / 到期后是否**0.3 s 内淡出**、不留残影（每秒自检兜底）。
4. 若"框立起来了/不是正对镜头"，问题在 `MarkFxKit.Line` 的 `LineAlignment` 或 `FlatRight`（地面件才用 `Flat`）。

> 日志（需 `RD_VERBOSE`）：生成成功各打一行 `…已生成。`；
> release 可见：材质失败 `…放弃：StreakMaterialPlain 无可用材质。`、
> 或"只按 GUID 认上"时的那行（会报出**真实蓝图名**）。
