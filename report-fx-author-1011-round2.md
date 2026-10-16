# 作者反馈批 · 第二轮 — 重构/文明补特效 / 闪光弹真缩小 / 王者冲锋用原版冲锋 / 大地之怒换素材 / 天灾降临 40 尺

> 日期：2026-10-11　范围：作者对上一轮 10 项改动的**复查反馈**。
> 编译：`dotnet build -p:WrathPath="…"` **0 警告 0 错误**；已 `tools/deploy.sh` 部署（旧 DLL 备份 `RhodesDoctor.dll.bak-20261011-152422`）。
> ⚠ 部署后**必须重启游戏**。

---

## 一、总表

| # | 法术 | 作者反馈 | 结果 |
|---|---|---|---|
| 1 | 重构 / 文明的存续 | "本来应该是有的特效，我没看见"（不是让你移除） | ✅ **补施法爆发**（复活术之光 / 天使圣歌），锚施法者 |
| 2 | 闪光弹 | 特效范围**还是那么大** | ✅ 上次只给粒子缩放没用 → **加根缩放** `RootScale 0.4`（阳炎爆是网格型爆发） |
| 3 | 王者冲锋 | 特效不对，效果也不对，要看游戏里"冲锋"逻辑 | ✅ 改用原版 `AbilityCustomCharge`（基础冲锋同款） |
| 4 | 岁稔 | 改成**植物鲜绿** | ✅ `Tint` 改鲜绿 `(0.35,1.30,0.35)`、`Lift 0.70` |
| 5 | 潮汐守望 | 特效**偏一侧**、和人物差好远 | ✅ 新增 `Recenter`：按包围盒中心**水平自动居中** |
| 6 | 大地之怒 | 素材像缠绕术、太丑；**缩短成 30 尺** | ✅ 换**落石**素材、半径 **40 → 30** |
| 7 | 天灾降临 | 改成 **40 尺** | ✅ 半径 **80 → 40**（地面 FX 免缩放） |
| — | 永恒的注视 | 很不错 | ✅ 不动 |

---

## 二、逐条说明

### 1. 重构 / 文明的存续：补上"本来就该有"的特效
- 上一轮按你"没特效"的说法**把视觉去掉了**——理解反了，这轮补回来。
- 两者 `Range=Personal`（以你为中心 30 尺）不变；`ApplyAbilityFx` 里给它们各挂一发**施法爆发**（锚施法者）：
  - **重构** → 原版 7 环**复活术**（`Resurrection`）的 `AbilitySpawnFx`（一道复活之光）；
  - **文明的存续** → 原版**天使·圣歌**（`AngelHolyHymn`）的 `AbilitySpawnFx`（宏大圣光）。
- 重构的"每个被恢复者身上炸金光"仍是关的（那是逐个的金光，不是法术本体特效）；若你想要那种也一起给，说一声。

### 2. 闪光弹：这次真的缩小了
- 上一轮只给了 `ParticleSizeScale`（**只缩粒子**）——**对阳炎爆没用**，它是**网格型**爆发，粒子缩放动不了它。
- 本轮 `FxTint.SpawnPlaced` 新增 **根缩放** `rootScale` 参数，`ContextActionSpawnScaledFx` 新增 `RootScale` 字段；
  闪光弹同时给 `ParticleSizeScale = 0.4` + **`RootScale = 0.4`**（网格和粒子一起缩）。
- 想更小/更大就改这两个数（`SpellsSetup.ApplyAbilityFx` 的 Flashbang 分支）。

### 3. 王者冲锋：改用游戏自带的"冲锋"逻辑
- 查了原版：游戏的基础**冲锋**是 `Root/SystemMechanics/Abilities/ChargeAbility`，它挂的组件是
  **`Kingmaker.UnitLogic.Abilities.Components.AbilityCustomCharge`**（BlueprintCore 里对应 `AddAbilityCustomCharge()`）。
- 本轮改成 `ability.AddAbilityCustomCharge()`：
  - 施法者**沿直线冲到目标身边**并自动进行一次**冲锋攻击**（移动动画 / 借机攻击等交给引擎）；
  - **删掉**了"直线投射物 + 传送"两个打架的投递组件，也删掉了原来那条"看着像射线"的 FX（冲锋本身即视觉）；
  - 距离改 **两倍移动速度**（`AbilityRange.DoubleMove`），目标改为**一名敌人**；
  - `effects` 里删掉多余的 `MeleeAttack()`（冲锋自己会打一次），只保留"4d6 + 倒地"。
- ⚠ **已知偏差**：`AbilityCustomCharge` 只投递到**目标**、拿不到"路径"上的其它敌人 →
  规则里的"路径上每个敌人受 4d6"目前**只作用于冲锋目标**。要做真正的"路径伤害"需要再单独实现（见下）。

### 4. 岁稔：植物鲜绿
- `TintedAuraFx` 里岁稔那条的 `Tint` 由禾绿 `(0.35,1.10,0.45)` 改成**鲜绿** `(0.35,1.30,0.35)`、`Lift 0.60 → 0.70`。

### 5. 潮汐守望：水平自动居中
- 素材（海之披风）自带的 FX 在 prefab 里就**偏在一侧**，挂到单位上就"和人差好远"。
- `TintedAuraFx.Entry` 新增 **`Recenter`**：生成时按 FX 的**世界包围盒中心**把克隆体水平挪回单位原点（只动 x/z）。
  潮汐守望已开启。若还偏（或变成偏上下），告诉我方向，我再加竖直偏移。

### 6. 大地之怒：换素材 + 30 尺
- 素材：上一轮借的**尖石术**区域（`SpikeStonesArea`）"看起来像缠绕术、太丑" → 换成原版**落石术**区域的常驻 Fx
  （`StoneCallArea`，一片落石/碎石）。
- 半径 **40 → 30**（你这次的"缩短成 30 尺"）；地面 FX 用 `AreaGroundFx` 的 `scale=0.75`（素材 40 → 30）。
- 顺带把实现改成"以施法者为中心"的**自扫场景**（不再挂 `AbilityTargetsAround`，否则视觉区域会按敌人数重复生成）。

### 7. 天灾降临：40 尺
- 区域半径 **80 → 40**；地面 FX 素材（火山风暴，Cylinder/40）现在与半径相等、**免缩放**（`scale 2 → 1`）。

---

## 三、代码改动

| 文件 | 改动 |
|---|---|
| `src/SpellsSetup.cs` | 重构/文明补施法爆发（`fx` 表加两条）；闪光弹加 `RootScale`；王者冲锋改 `AddAbilityCustomCharge()` + Spec 改 `DoubleMove`/去直线；大地之怒 Spec 半径 30 + 纯视觉区域 + 自扫；天灾降临 Spec/区域 40 尺；`areaSelfScan` 加 EarthFury |
| `src/Actions.cs` | `ContextActionSpawnScaledFx` 加 `RootScale`；`ContextActionEarthFuryHit` 改为**自扫场景**（`RadiusFeet`） |
| `src/FxTint.cs` | `SpawnPlaced` 加 `rootScale` 参数；`MeasureBounds` 改 internal |
| `src/TintedAuraFx.cs` | `Entry` 加 `Recenter`；生成时按包围盒水平居中；岁稔改鲜绿；潮汐守望开 `Recenter` |
| `src/FxLibrary.cs` | `EarthWrathGround` 换落石素材；新增 `ReconstructionBurst` / `CivilizationBurst` |
| `src/Guids.cs` | 重新启用 `EarthFuryArea`(0x3e) |
| `LocalizedStrings.json` | 同步 3 条描述（EarthFury 30 尺 / RoyalCharge 两倍移动速度 / Catastrophe 40 尺） |

---

## 四、待进游戏确认

1. **重构 / 文明的存续**：施法时**看得见**特效（复活之光 / 圣光）。
2. **闪光弹**：爆发应比原来**明显小**。
3. **王者冲锋**：施法者**沿直线冲到目标身边并攻击一次**（不再有射线 FX）；目标挨 4d6（失败倒地）。
4. **岁稔**：应是**植物鲜绿**。
5. **潮汐守望**：水膜应**贴着人物**（不再偏一侧）。
6. **大地之怒**：应是**落石**地面、**半径 30 尺**。
7. **天灾降临**：半径 **40 尺**。
8. ⚠ **换 DLL 后必须重启游戏**。

---

## 五、需要你定的点

- **王者冲锋的"路径伤害"**：原版冲锋只打到目标，路径上的其它敌人目前不受 4d6。要真做"路径伤害"，
  得记下冲锋的起点位置、再沿起点→目标扫一遍敌人（要加一个记录起点的组件）。要做的话说一声。
- **重构**：要不要恢复"每个被恢复者身上炸金光"？
- **潮汐守望**：水平居中若仍偏，请告诉我偏的方向（偏上 / 偏下 / 还偏一侧）。
