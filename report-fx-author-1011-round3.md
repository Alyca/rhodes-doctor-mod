# 作者反馈批 · 第三轮 — 重构/文明补特效（真因）/ 闪光弹真缩小 / 王者冲锋改自定义 / 潮汐守望修偏

> 日期：2026-10-11　范围：作者对第二轮 7 项的复查反馈（重构·文明 / 闪光弹 / 王者冲锋 / 潮汐守望）。
> 这轮**先读了 `Player.log`** 定位真因（日志里有 `[RhodesDoctor]` 行），四处问题全部有据可查。
> 编译：`dotnet build -p:WrathPath="…"` **0 警告 0 错误**；已 `tools/deploy.sh` 部署（旧 DLL 备份 `RhodesDoctor.dll.bak-20261011-153956`）。
> ⚠ 部署后**必须重启游戏**。

---

## 一、日志查到的真因（这次不靠猜）

| 现象 | 日志证据 | 真因 |
|---|---|---|
| 重构 / 文明**没特效** | `特效借用失败（爆发·法术）：全部候选均无 AbilitySpawnFx。`（两处） | 我把扫描表里的 **Fx 素材 ID** 误当成**能力 GUID** 传给了 `AbilityBurstFx` |
| 闪光弹**还是那么大** | `粒子尺寸×0.40（7 个粒子系统，未动根缩放）` + 素材包围盒 `356×47×354 米` | ① 阳炎爆是**网格型**爆发，粒子缩放动不了；② Unity 粒子默认 **`Local` 缩放模式**，**根缩放对粒子也无效** |
| 潮汐守望**干脆没特效** | `潮汐守望·青蓝水膜已生成。`（生成过） | 我加的"按包围盒自动居中"用的是**被吹大的粒子包围盒**（海之披风 356 米）→ 把整份特效**挪飞了** |
| 潮汐守望原本"偏一侧" | 该素材粒子是 `Puddles / BottomWater / FrontCaustics` | **海之披风的 `FxOnStart` 其实是"地面水花/水洼"**，不是贴身水膜 |
| 王者冲锋**直接无效** | 无异常日志 | `AbilityCustomCharge` 挂上去让整个法术不可用（原版冲锋另有 `HasMeleeWeapon` 等一整套前提） |

---

## 二、逐条修法

### 1. 重构 / 文明的存续：修 GUID + 改用"动作"生成
- **修 GUID**：改用 BlueprintCore 的真能力引用 `AbilityRefs.Resurrection`（复活术）/ `AbilityRefs.AngelHolyHymn`（天使·圣歌）。
- **改挂法**：`AbilitySpawnFx` 对"个人距离 + 自扫场景"的法术**不生效**（这也是你两次都没看见的原因之一），
  改成在法术的**动作列表**里加 `ContextActionSpawnScaledFx`（施法时在施法者处生成一发）——
  这条路径和"群体恢复"动作同一份 ActionList，**一定会跑**。
- 视觉：重构 = 复活术之光；文明的存续 = 天使·圣歌的圣光。

### 2. 闪光弹：这次靠"粒子缩放模式"真缩小
- 在 `FxTint.SpawnPlaced` 里：**根缩放 != 1 时，先把所有粒子系统的 `scalingMode` 改成 `Hierarchy`**，
  再套根缩放 —— 这样根缩放会**同时缩网格和粒子**（`Local` 模式下粒子不跟缩放，这就是前两次"没变"的原因）。
- 闪光弹现在用 `RootScale = 0.35`（`ParticleSizeScale` 不再叠加，避免缩两次）。
- 想更小/更大只改 `SpellsSetup.ApplyAbilityFx` 里 Flashbang 分支的 **`RootScale`**。

### 3. 王者冲锋：改成"一个自定义动作"（满足你说的三点）
- 删掉 `AbilityCustomCharge`（它让法术无效），改成 `Actions.cs` 新增的 **`ContextActionRoyalCharge`**，一次做完：
  1. **人物移动**：施法者沿直线冲到点击的落点（`FreePlaceSelector` 保证落点是空地）；
  2. **路径敌人受伤**：从起点到落点拉一条线段，**到线段距离 ≤ 5 尺**的敌人各受 **4d6 轰击**；
  3. **受到"影响"**：反射失败者**倒地**（原版倒地增益）。
- 施法方式：**选一点**（`SetCanTargetPoint`），距离 **40 尺**（`SetCustomRange(40)`，你要求的）。
- 不再挂任何投射物 / 传送组件（避免投递冲突），也不再有那条像射线的 FX。

### 4. 潮汐守望：换素材 + 删掉"自动居中"
- **删掉** `Recenter`（包围盒居中把特效挪飞了），改为每条的**手动偏移** `LocalOffset`（默认 0）。
- **换素材**：海之披风（地面水花）→ 原版**水元素聚能**（`GatherPowerWaterBuff`，包在身上的水），仍染**青蓝**。

### 5. 大地之怒
- 你说"可以"，**未改动**。

---

## 三、代码改动

| 文件 | 改动 |
|---|---|
| `src/FxLibrary.cs` | `ReconstructionBurst` / `CivilizationBurst` 改用 `AbilityRefs.Resurrection` / `AbilityRefs.AngelHolyHymn`；`TideAura` 换 `GatherPowerWaterBuff` |
| `src/FxTint.cs` | `SpawnPlaced`：根缩放 != 1 时先把粒子 `scalingMode` 改成 `Hierarchy`（根缩放才缩得动粒子） |
| `src/TintedAuraFx.cs` | 删掉 `Recenter`（包围盒居中不可靠），改为手动 `LocalOffset`；潮汐守望换素材 |
| `src/Actions.cs` | 新增 `ContextActionRoyalCharge`（冲锋移动 + 路径 4d6 + 倒地） |
| `src/SpellsSetup.cs` | 闪光弹 `RootScale 0.35`；王者冲锋改自定义动作 + `SetCustomRange(40)` + 落点施法；重构/文明的爆发改在动作列表里生成 |
| `LocalizedStrings.json` | 同步王者冲锋描述（40 尺 / 选一点） |

---

## 四、待进游戏确认

1. **重构 / 文明的存续**：施法时应能看见**复活术之光 / 天使圣光**（这次是"动作"生成，一定会跑）。
2. **闪光弹**：爆发应**明显变小**（根缩放 + Hierarchy 缩放模式）。
3. **王者冲锋**：应能**选一点**施放 → 施法者**冲过去** → 路径上的敌人挨 4d6、失败者倒地。
4. **潮汐守望**：应是**包在身上的青蓝水膜**（不再是地面水花、也不该消失）。
5. ⚠ **换 DLL 后必须重启游戏**。

---

## 五、若还不行的下一步（可选项）

- **潮汐守望**若还偏：现在有 `LocalOffset` 手动偏移，告诉我"偏上/偏下/偏左偏右多少"，我直接填。
- **闪光弹**若还想更小：改 `RootScale`（0.35 → 0.2 等）。
- **王者冲锋**若"冲过头/撞不到人"：`HalfWidthFeet`（路径判定半宽，默认 5 尺）与 `MaxDistanceFeet` 都可调。
