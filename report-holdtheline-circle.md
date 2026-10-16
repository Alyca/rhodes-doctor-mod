# 坚守阵地改"圈" / 乌拉改橙红

> 日期：2026-10-11　范围：作者反馈——坚守阵地特效浮夸且又跟单位数量有关、应是"和法术范围相当大小的圈"；乌拉特效改橙红。
> 编译：`dotnet build -p:WrathPath="…"` **0 警告 0 错误**；已 `tools/deploy.sh` 部署（旧 DLL 备份 `RhodesDoctor.dll.bak-20261011-140320`）。
> ⚠ 部署后**必须重启游戏**。

---

## 一、坚守阵地：改成"持续的区域 + 一个圈"

**问题**：上一版把大特效挂在**每个受益友军**身上 → **有几个友军就有几个**（"又变成和单位数量有关"），
而且借天使罩子放大 4 倍太浮夸。

**根因**：`Kind.Buff` 的 `AbilityEffectRunAction` 是**逐目标**跑的（`AddAbilityTargetsAround(Ally)`），
所以写在里面的 `SpawnAreaEffect` 会按目标数重复执行。

**改法**（照钙质化那套"区域给友军挂增益"）：
- `Kind` 由 `Buff` 改为 **`Special`**，并把 `HoldTheLine` 加入 `areaSelfScan`（**不挂 `TargetsAround`**）→ 效果**只跑一次**；
- 增益改由**区域**施加：`Guids.HoldTheLineArea`（Cylinder/**30**，与法术半径相等）
  `AddAbilityAreaEffectBuff(holdBuff, IsAlly)`；
- `effects.SpawnAreaEffect(Guids.HoldTheLineArea, 1 分钟/级)`——**只生成一个**区域；
- **视觉**：`FxLibrary.HoldTheLineGround` 借原版**吟游诗人·英勇激励**（`InspireHeroicsArea`）的区域 `Fx`
  ——它本身就是"**半径 30 的友军增益光环圈**"，与法术半径**完全相等、免缩放**，比罩子克制得多；
  经 `AreaGroundFx.Register(..., scale: 1f)` 染色（淡金）后跟区域同生同灭。

> 语义顺带变正确：现在是**持续的区域**（圈内友军获得增益），与作者"坚守阵地作用范围是持续的，一个区域内的单位"一致。

## 二、乌拉：光环改橙红

`TintedAuraFx` 里乌拉那条（`Guids.Buff(12)`）的 `Tint` 由暖金改为**橙红** `(1.30, 0.45, 0.20)`。

---

## 三、代码改动

| 文件 | 改动 |
|---|---|
| `src/SpellsSetup.cs` | 坚守阵地：Spec 改 `Kind.Special` + `Friendly`；加入 `areaSelfScan`；新增 `ApplySpecial` 的 `HoldTheLine` 分支（建增益 + 区域 + `SpawnAreaEffect` + `AreaGroundFx.Register`）；删掉上一版的"逐目标挂区域"写法 |
| `src/FxLibrary.cs` | 新增 `HoldTheLineGround`（借 `InspireHeroicsArea` 的区域 Fx，半径 30 免缩放） |
| `src/TintedAuraFx.cs` | 乌拉 `Tint` → 橙红 |

---

## 四、待进游戏确认

1. **坚守阵地**：应是**一个**跟法术范围同样大的**圈**（不再随友军数量变化、不再是罩子）。
   圈的大小/颜色不合意就调 `SpellsSetup` 里 `AreaGroundFx.Register(Guids.HoldTheLineArea, …, scale: 1f)` 与 `FxLibrary.HoldTheLineGround` 的候选。
2. **乌拉**：光环应为**橙红**、跟单位走。
3. ⚠ **换 DLL 后必须重启游戏**。
