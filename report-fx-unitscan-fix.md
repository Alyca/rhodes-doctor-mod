# 法术特效 · **"扫不到队友"真因修复** —— 重构（没特效）/ 文明的存续（没效果）

> 日期：2026-10-11　范围：作者反馈"**重构没有特效了**（它应该是每一个受影响的单位各有一份特效）/
> **文明的存续有特效，但似乎没有效果**"。
> **这两条是同一个根因**：`ContextActionMassRestore`（重构 / 文明的存续共用的那个动作）
> **一个受影响的单位都没找到**。
> 编译：`dotnet build -p:WrathPath="…"` **0 警告 0 错误**；已部署（旧 DLL 备份
> `RhodesDoctor.dll.bak-20261011-173709`，最终版 17:39:13）。⚠ 部署后**必须重启游戏**。

---

## 一、先读 `Player.log`：这次不用猜

那一局（17:25 退出前）只有这三条运行期日志，全部对得上现象：

```
[RhodesDoctor] 群体恢复完成：中心=(345.6,40.0,341.9) 半径=9.1m 活着恢复=0 复活=0/0 后续增益挂上=0 PerUnitFx=有 GoldFlash=False
[RhodesDoctor][特效尺寸] 素材 394fedae396415a43a05748b4d53893fd 原始包围盒=356.22x51.64x352.56 米   ← 文明：施法者处圣光确实播了
[RhodesDoctor] 群体恢复完成：中心=(345.6,40.0,341.9) 半径=9.1m 活着恢复=0 复活=0/0 后续增益挂上=0 PerUnitFx=无 GoldFlash=False
```

| 现象 | 日志证据 | 真因 |
|---|---|---|
| **重构"没有特效"** | `PerUnitFx=有` 但 `活着恢复=0 复活=0/0` | 重构的视觉是**每个受影响单位各一份**（作者上一轮的要求，已按此接线）；**一个单位都没受影响 ⇒ 一发特效都不会生成** |
| **文明"有特效但没效果"** | 素材 `394fedae…`（= `AngelHolyHymn` 的 `AbilitySpawnFx`）在施法者处实例化了；但 `活着恢复=0 复活=0/0 后续增益挂上=0` | 施法者处的圣光是**动作生成**的，播得出来；而同一条动作序列里的"群体恢复"**扫不到友军** ⇒ 没人被治疗 / 复活 / 吃后续 +4 |

**关键旁证（两条，都指向"是人没扫到"，不是筛选太严）**：

1. **中心就是施法者自己的位置**（`CenterOnCaster=true`，半径 9.1 m = 30 尺），
   连施法者自己都没算进 `活着恢复` ⇒ 扫描列表里**根本没有友军**；
2. 同一台机器的 `Player-prev.log`（15:53 那局，`src/Diag.cs` 的临时补丁）里有
   ```
   [RhodesDoctor-DIAG] RhodesDoctorSpellTidalVigil anchor[unit=博士] selected=3 :: 博士,索希尔,拉米恩
   ```
   —— **原版选目标机制找得到 3 个队友**，说明队友确实在场上，只是**不在我们扫的那份列表里**；
3. 复活术的 FX（素材 `ee0c5b93…`）在上一版（施法者处一发）是能看见的 ⇒ **素材本身没问题**，问题在"没人可放"。

---

## 二、根因：队伍成员**不在** `CurrentScene.AllEntityData` 里

原实现（`Actions.cs` 的 `ContextActionMassRestore` 等）：

```csharp
var scene = Game.Instance?.CurrentScene ?? Game.Instance?.LoadedAreaState;
var units = scene?.AllEntityData?.OfType<UnitEntityData>();   // ← 只有区域场景状态
```

**`CurrentScene` = `State.LoadedAreaState`（`AreaPersistentState`），它的 `AllEntityData` 只含
"当前区域的场景状态"里的实体**：敌人、中立 NPC、区域内召唤物。
而**队伍成员住在 `Player.CrossSceneState`**（`SceneName = "<cross-scene>"` 的独立 `SceneEntitiesState`，
`Player.AllCrossSceneUnits => CrossSceneState.AllEntityData.OfType<UnitEntityData>()`）——
他们必须跨区域跟随玩家，所以**从来不在区域状态里**。

> 本 mod 自己其实早就在 `ContextActionRemoveMyBuffs` 的注释里写过这条：
> "`AllCrossSceneUnits` 单独用时只含**队伍成员**，敌人身上的旧标记扫不到"——
> 于是那个功能两家都扫；而"只扫区域"的这几个功能就全废了。

原版 AoE 选目标用的是**全局单位池**，不是区域状态（反编译 `Kingmaker.Designers.GameHelper`）：

```csharp
public static IEnumerable<UnitEntityData> GetTargetsAround(Vector3 point, float radius, bool checkLOS = true, bool includeDead = false)
{
  foreach (UnitEntityData unit in Game.Instance.State.Units)   // ← 原版的来源
    if ((!unit.Descriptor.State.IsDead || includeDead) && !unit.Descriptor.State.Features.IsUntargetable
        && unit.IsUnitInRange(point, radius, checkLOS)) yield return unit;
}
```

**同一个根因一共坑掉 4 个法术**（其余三个是顺手查出来的）：

| 法术 | 原来的表现 |
|---|---|
| **重构** | 0 恢复、0 复活 → "每个单位一份的特效"一发都不生成（本轮报告） |
| **文明的存续** | 圣光看得见，但 0 治疗 / 0 复活 / 0 后续增益（本轮报告） |
| **王冠回响** | "把偷来的增益分给 60 尺内友军"找不到友军 → 全部回落到施法者自己 |
| **画中世界** | 按 `UniqueId` 找回被放逐者 → 放逐**队友**后永远回不来（日志"找不到被放逐的单位（可能已离场），跳过返回"） |

---

## 三、修法

### 1. 新增 `src/UnitScan.cs`：唯一正确的"取单位"入口

```csharp
var snap  = UnitScan.Take();     // State.Units ∪ Player.AllCrossSceneUnits ∪ Player.Party（去重）
var units = snap.Units;          // List 快照：可边遍历边加增益 / 复活
```

- 三处来源**去重**；`u.Descriptor == null` 的**半成品单位**一律挡掉（调用方第一句话就是
  `u.IsPlayerFaction` = `Descriptor.IsPlayerFaction`，null 会直接抛异常、打断整个动作序列）；
- ⚠ 这是**全局池**，别的区域残留的单位也可能在里面 ⇒ 调用方**必须**自己按距离（或 `UniqueId`）筛选
  （原版同样靠 `IsUnitInRange` 过滤），这一点写进了 `UnitScan.cs` 顶部与本轮所有调用点的注释；
- 顶部注释把根因、证据、四个受害法术一次写全，避免下次又踩。

### 2. `ContextActionMassRestore`（重构 / 文明的存续）

- 单位来源 → `UnitScan.Take()`；遍历改成用快照（不再 `.ToList()`）。
- 友军判定：仍是 R6 的 `IsPlayerFaction`（**不含中立 NPC**），再补 `caster.IsAlly(u)`
  （与原版 `AbilityTargetsAround` 的友军判定同源；玩家阵营的召唤物 / 同组单位也算友军）。
- **重构的视觉不用改**：`PerUnitFx = FxLibrary.ReconstructionBurst`（复活术之光）本来就是
  "每个被恢复 / 复活的友军身上各放一发"；炸掉的原因是没人可放，现在会正常逐单位生成。
- **新增一行诊断日志**（下次若还是 0，一眼分清"池子空"还是"阵营筛错"）：
  ```
  群体恢复·扫描：State.Units=… 跨场景队伍=… Party=… 去重后=…；友军=… 其中 30 尺内=…（阵亡 …）；最近单位：博士0.0m(友) 索希尔2.4m(友) …
  ```

### 3. 同类根因一次修完

| 位置 | 改动 |
|---|---|
| `Actions.cs` · `ContextActionStealBuffsCustom`（王冠回响） | 60 尺内友军来源 → `UnitScan` |
| `Components.cs` · `PaintedWorldBanishTracker.FindUnit`（画中世界） | 按 `UniqueId` 找回 → `UnitScan` |
| `Actions.cs` · `ContextActionMassRestore`（重构 / 文明） | 同上 + 诊断日志 |

**故意没动的**：只找**敌人**的自扫（炮火延伸 / 延时爆破 / 王者冲锋 / 连锁殉爆）——
敌人在区域状态里，本来就扫得到；改动越少越好（这条也写进了 `dev-notes.md` §20.3）。

---

## 四、代码改动

| 文件 | 改动 |
|---|---|
| `src/UnitScan.cs` | **新增**：`Take()`（三来源去重 + 挡半成品单位）、`Describe()`、`DescribeNearest()`；顶部写清根因与证据 |
| `src/Actions.cs` | `ContextActionMassRestore`：单位来源 → `UnitScan`、友军判定补 `IsAlly`、加"扫描"诊断行、`SpawnPerUnitFx` 去掉没用到的 `layer` 变量；`ContextActionStealBuffsCustom`：友军来源 → `UnitScan` |
| `src/Components.cs` | `PaintedWorldBanishTracker.FindUnit`：找回被放逐者 → `UnitScan` |
| `docs/dev-notes.md` | **新增 §20**：`CurrentScene.AllEntityData` 为什么扫不到队友（含日志 / 反编译证据、原版正确来源、`UnitScan` 定案做法、四个受害法术） |
| `docs/fx-status.md` | 重构 / 文明的存续两行按"逐单位特效 + 单位来源已修"改写；批次备忘加"自扫一律用 `UnitScan`"的 ⚠⚠ 条目 |

---

## 五、待进游戏确认（重启游戏后）

1. **重构**：站在队友（或队友尸体）旁边施放 → 应看到**每个被恢复 / 复活的友军身上各一发复活术之光**；
   日志应出现 `活着恢复=N`（N ≥ 1，施法者自己也算）与新的 `群体恢复·扫描：… 友军=N …`。
2. **文明的存续**：除施法者处的圣光外，**队友应真的被完全恢复、被复活（至多 5 名），并吃到 1 分钟的 +4 与免死**；
   日志 `后续增益挂上=N` 应 ≠ 0。
3. **王冠回响**（顺手修的）：偷来的增益应分给附近队友，而不是全给施法者自己。
4. **画中世界**（顺手修的）：放逐**队友**后应能原位返回 + 麻痹。
5. ⚠ **换 DLL 后必须重启游戏**（DLL 只在启动时加载）。

## 六、若还有问题，下一步怎么查

- 先看新那行 `群体恢复·扫描：…`：
  - `去重后=0` + `（池子里一个单位都没有）` → 场景/加载时机问题（把日志发我）；
  - `友军=0` 但 `最近单位` 里队友都标着"(非友)" → 是阵营判定（把日志发我）；
  - `友军≥1` 但 `30 尺内=0` → 距离问题（站位 / `CenterOnCaster`）。
- **重构的单特效太大 / 太小**：`SpellsSetup` 的 `Reconstruction` 分支加
  `a.PerUnitFxRootScale = 0.5f` 这类（`FxTint.SpawnPlaced` 会把粒子改成 `Hierarchy` 缩放再套根缩放），
  或改 `a.PerUnitFxLifetime`（默认 3 秒）。
- **重构想换素材**：改 `FxLibrary.ReconstructionBurst` 一行即可（备选见 `fx-plan.md` 的素材库）。
