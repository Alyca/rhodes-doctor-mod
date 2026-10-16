# 非法术代码审计修复报告（第二轮，2026-10-15）

> 范围：`src/*.cs` 全部非法术文件（不含 `SpellsSetup.cs` 64 条指令表）
> 交叉：三路并行静态审查（核心机制 / 装配 / 特效与工具）+ 编译验证
> 编译：Debug 与 Release 均 **0 警告 0 错误**（`WrathPath=D:/steam/.../Pathfinder Second Adventure`，非公开化程序集）
> 产物：`bin/Debug/net48/RhodesDoctor.dll`（369664→370688 字节），新修复字符串（UTF-16LE）已逐条确认编入 DLL

---

## 一、本次交付

| # | 项目 | 优先级 | 文件 | 状态 |
|---|---|---|---|---|
| 1 | `FxTint.SetAlpha` / `CutAlphaByShader` 的 `CanWrite` 条件写反（钙质化压投影贴花 Alpha 核心功能失效） | **P1** | FxTint.cs | ✅ |
| 2 | 每日法位表行数无下限保护，原版术士表若只 21 行则 10 环法位永不开放 | **P1** | SpellbookSetup.cs | ✅ |
| 3 | `InklingDeathBurst.OnDeactivate` 无 try/catch，异常打断 buff 移除链 | P1 | Components.cs | ✅ |
| 4 | `HarvestYearsAccumulator.OnDeactivate` 无 try/catch + ResidualBuff 空静默丢失额度 | P1 | Components.cs | ✅ |
| 5 | `AftermathRestriction` Nauseated Add/Remove 不对称，Capstone 在场时会误删别的来源 Nauseated | P1 | Components.cs | ✅ |
| 6 | `DelayedBlastTracker.OnDeactivate` 无 try/catch | P1 | Components.cs | ✅ |
| 7 | `DrunkenDreamWake.OnEventDidTrigger` 移除自身+挂 buff 无 try/catch | P1 | Components.cs | ✅ |
| 8 | `SinPunishmentRetribution` 反向链无 try/catch + `TriggeredThisRound` 先置位后触发（异常时标记已扣但反伤无效） | P1 | Components.cs | ✅ |
| 9 | `ContextActionVolcanoEmberFx` 不检查 `VolcanoTracker.Fx` 是否为空 | P2 | Components.cs | ✅ |
| 10 | `BanishRecovery.Start` 在 `owner.View==null` 时静默放弃（无日志、不重试） | P2 | Components.cs | ✅ |
| 11 | `ContextActionHarvestTick.RunAction` 无 try/catch | P1 | Actions.cs | ✅ |
| 12 | `ContextActionRemoveMyBuffs` 未统一到 `UnitScan`（与同 mod 其它动作扫描方式不一致） | P3 | Actions.cs | ✅ |
| 13 | RenderTexture 异常路径不释放（`ReadPixels`/`Desaturated`/`TintedTexture` 三处） | P1 | FxTint.cs | ✅ |
| 14 | `Main` 6 个 `EventBus.Subscribe` 在一个 try 里，单个失败拖累后续全部 | P1 | Main.cs | ✅ |
| 15 | `TestPanel.Dummies` 跨读档残留（`u==null` 测不出已回收 UnitEntityData） | P3 | TestPanel.cs | ✅ |
| 16 | 注释引用不存在的方法 `CreateTenRingFeature`（方案已换双表） | P3 | SpellbookSetup.cs | ✅ |
| 17 | `Guids.cs` 分区注释与实际后缀分配漂移 | P3 | Guids.cs | ✅ |

---

## 二、关键 P1 详解

### 2.1 `FxTint` CanWrite 条件写反（最优先）

**位置**：`FxTint.cs:961`（SetAlpha）、`FxTint.cs:1064`（CutAlphaByShader）

**问题**：两处 continue 条件写成 `!p1.CanRead || p1.CanWrite || ...`，意为"可写的 material 属性被跳过"，只有**只读**的 material 属性才进入处理。但紧接着的 `p1.SetValue(comp, m, null)` 在只读属性上会抛异常被 catch 吞掉。

对照同文件 `Apply` 第 472 行的正确写法 `p1.CanRead && p1.CanWrite && p1.PropertyType == typeof(Material)`，正确要求 CanWrite 为 true。

**后果**：
- `FadeOutThenDestroy` 的 `SetAlpha` 无法处理非 Renderer 的材质持有者（Projector 贴花），区域特效渐隐时贴花不淡化
- `CutAlphaByShader`（钙质化"压投影贴花 Alpha"核心功能）对 Projector **完全无效**——钙质化地面残留层变淡只靠压 Alpha，这条路径失效意味着钙质化的"投影贴花变淡"在贴花材质上根本不生效

**修法**：两处 `p1.CanWrite` → `!p1.CanWrite`（一个字符补 `!`）。

### 2.2 每日法位表行数无下限保护

**位置**：`SpellbookSetup.cs:107`（CreateSpellsPerDayTable）

**问题**：每日法位表行数直接取 `src.Levels.Length`（原版术士每日法位表）。已知法术表（同文件 L148）用 `Math.Max(TenRingRow + 1, 41)` 确保至少 22 行可索引；本表没有这个保护。

`count[10] = i >= TenRingRow ? 4 : -1` —— `TenRingRow = 21`。若原版术士每日法位表只有 21 行（CL 0..20），循环 `i` 永远到不了 21，`count[10]` 恒为 -1。`Spellbook.GetMaxSpellLevel()` 从 10 环往下找第一个 `GetCount >= 0` 的环，读到 -1 跳过，最高环停在 9 → **10 环法位标签页永不出现、20 级无法施放 10 环指令**。

**修法**：行数改 `Math.Max(TenRingRow + 1, src.Levels.Length)`；超原表的行用原表最后一行做基底（0–9 环法位不空），再设 10 环列。与已知法术表的保护对齐。

### 2.3 OnDeactivate / DidTrigger 异常防护缺口

本 mod 约定"OnDeactivate/DidTrigger 触发外部规则的组件必须有 try/catch"（避免异常冒泡回 `Buff.OnDetach` 组件迭代打断移除链）。本轮补齐 5 处缺口：

| 组件 | 文件:行 | 内部触发 |
|---|---|---|
| `InklingDeathBurst.OnDeactivate` | Components.cs:475 | TriggerRule(伤害+豁免) + AddBuff |
| `HarvestYearsAccumulator.OnDeactivate` | Components.cs:1241 | AddBuff + GetData + AddModifier |
| `DelayedBlastTracker.OnDeactivate` | Components.cs:961 | BombFx.DestroyMarker |
| `DrunkenDreamWake.OnEventDidTrigger` | Components.cs:1547 | Remove + AddBuff |
| `SinPunishmentRetribution.OnEventDidTrigger` | Components.cs:1107 | 反向 TriggerRule(RuleDealDamage) |

写法全部对齐已修的 `ChainDetonationOnDeath.OnDeactivate` / `PaintedWorldBanishTracker.OnDeactivate` / `HarvestYearsResidual.OnDeactivate`。

**SinPunishmentRetribution 额外修正**：原 `Data.TriggeredThisRound = true` 在 `TriggerRule` 调用**之前**置位——若 TriggerRule 抛异常，标记已扣但反伤无效（当轮不再尝试）。改为先触发反伤、成功后才置位。

### 2.4 `AftermathRestriction` Nauseated 不对称

**位置**：`Components.cs:1335-1356`

**问题**：
- OnActivate（Capstone 在场）：**没 AddCondition**，却额外 `RemoveCondition(Nauseated)`
- OnActivate（Capstone 不在场）：`AddCondition`
- OnDeactivate：**无条件** `RemoveCondition`

Capstone 在场时对应"0 次 add / 2 次 remove"——会误删别的来源（毒素/法术）的 Nauseated。

**修法**：Capstone 在场时只 `return`（不施加、不 Remove）；仅非 Capstone 分支 AddCondition；OnDeactivate 仍 RemoveCondition（自己加的自己移除一次，对称）。OnDeactivate 也补 try/catch。

### 2.5 RenderTexture 异常路径释放

**位置**：`FxTint.cs`（ReadPixels / Desaturated / TintedTexture 三处）

**问题**：模式都是
```
var rt = RenderTexture.GetTemporary(...);
var prev = RenderTexture.active;
Graphics.Blit(src, rt);
RenderTexture.active = rt;
... ReadPixels ...
RenderTexture.active = prev;
RenderTexture.ReleaseTemporary(rt);
```
`Blit`/`ReadPixels` 抛异常时 `prev` 已保存但 active 没复位、rt 没释放。外层 catch 不做清理。

**修法**：`ReadPixels` 改 try/finally（复位 active + 释放 rt + 销毁 tmp Texture2D）；`Desaturated`/`TintedTexture` 的"非可读"分支改为调用 `ReadPixels`（走同一健壮路径）。

### 2.6 Main 事件订阅拆分

**位置**：`Main.cs:38-56`

**问题**：6 个 `EventBus.Subscribe` 在一个 try 里。第 3 个失败时前 2 个已订阅但后 4 个永远不订阅——`_loaded` 已置 true，下次 Load 直接跳过整块，导致某些特效永久失效且无自动恢复。

**修法**：抽出泛型 `SubscribeOne<T>(ref T field, string name)`，每个 Watcher 独立 try/catch；单个失败只影响自身，其余照常订阅。

---

## 三、未做（超出本轮范围 / 需进游戏实测）

| 项 | 原因 |
|---|---|
| `ExecutionStrike` 翻倍整个 DamageBundle 的 PhysicalDamage（应只武器段） | 需确认 `evt.DamageBundle.Weapon` 是否就是武器段、`PrecisionDamage` 是否派生自 PhysicalDamage——属规则正确性，建议进游戏核对伤害数字后定 |
| 集火资源未强制最小值 1（INT 极低时 0 次） | `ResourceAmountBuilder` 的 `WithMin` API 未在程序集核实，属 VERIFY 项 |
| `SpellsTable.Levels` 在 Configure() 后直接赋值 | WoTR modding 常见模式，实测可行；改用配置器 API 风险大、收益低 |
| `FxTint` 克隆材质/网格/贴图显式回收 | 结构较大，依赖场景切换兜底；建议后续单独立项 |
| `DelayedRetint` 3 次全量重染 | 留档性能优化，频次可接受 |
| 死代码清理（`SpecterSpellRestriction`/`SwordField`/`MakeLoopsContinuous` 等） | 多数注明"留档备用"，删除收益小于保留价值 |
| `FadeOutThenDestroy.Begin` 无条件设 alpha=1 可能闪白 | 边界表现，需新增"读起点 alpha"辅助函数，收益低 |
| 法术书双来源 `TenRingRow=21` 强耦合 | 已知实测项，需进游戏核对法位是否翻倍 |

---

## 四、验证清单

- [x] `dotnet build` Debug：**0 警告 0 错误**
- [x] `dotnet build` Release：**0 警告 0 错误**
- [x] DLL 字节 369664 → 370688（+1024，新代码已编入）
- [x] 新修复字符串（UTF-16LE）逐条确认编入 DLL：
  `墨灵自爆失败` / `岁稔积攒` / `ResidualBuff 蓝图为空` / `余波限制移除` / `醉梦醒来` / `报应反伤` / `View 为空` / `Watcher 订阅失败` 全部命中
- [x] 备份：`bin/Debug/net48/RhodesDoctor.dll.bak-20261015-070800`
- [ ] **待进游戏**：
  ① 20 级 10 环法位标签页是否出现（法位表行数保护）；
  ② 钙质化地面投影贴花是否变淡（CanWrite 修复）；
  ③ 墨灵被打死自爆时 buff 移除链是否完整（OnDeactivate try/catch）；
  ④ 20 级开恶灵时余波 Nauseated 是否仍被正确移除（AftermathRestriction 对称）；
  ⑤ 反复施放频繁法术后 RenderTexture 是否泄漏（异常路径 finally）。
