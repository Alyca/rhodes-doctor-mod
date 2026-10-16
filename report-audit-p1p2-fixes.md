# 审计修复报告：P1 全修 + 源石技艺精通改版 + P2 性能

> 日期：2026-10-11（同日第十一轮）
> 版本：0.6.1 → **0.6.2**；编译 **0 警告 0 错误**；已部署（备份 `RhodesDoctor.dll.bak-20261011-203706`），DLL/JSON md5 与构建产物一致。

---

## 一、本次交付

| # | 项目 | 类型 | 状态 |
|---|---|---|---|
| 1 | 岁稔·余温不再把运行时修饰器写进存档 | P1-A | ✅ |
| 2 | 画中世界"找不回"兜底 + Error 日志 | P1-B | ✅ |
| 3 | 源石技艺精通改版（穿透+DC，5 档 +1~+5） | 设计变更 | ✅ |
| 4 | `FxTint` 反射按 Type 缓存 | P2 性能 | ✅ |
| 5 | `AreaGroundFx.LogAreaView` 补 `[Conditional]` | P2 性能 | ✅ |
| 6 | `BombFx` 网格/材质静态缓存复用 | P2 性能 | ✅ |
| 7 | `Main.Load` PatchAll 防护 + 重入守卫 | P2 健壮性 | ✅ |
| 8 | 4 处反射写入失败改为记 Error（不再静默） | P2 可诊断性 | ✅ |

---

## 二、P1-A｜岁稔·余温存档风险（`Components.cs`）

**问题**：`HarvestYearsResidualData.Modifier`（`ModifiableValue.Modifier`，含运行时引用）被 `[JsonProperty]` 序列化进存档。读档后若反序列化为"孤立对象"：① `OnActivate` 的 `Data.Modifier == null` 守卫不成立 → **临时生命不重建**（静默丢失）；② `OnDeactivate` 对孤立对象 `Remove()` 可能抛异常——而这是全文件少数**没有 try/catch 的 OnDeactivate**。

**修法**：
- `Modifier` 改 `[JsonIgnore]`（只存 `Amount`）；
- `OnActivate` 按 `Amount` **重建**修饰器（读档后必为 null，必然重建）；
- `OnDeactivate` 补 try/catch + `finally { Data.Modifier = null; }`。

**未改**：`HarvestYearsAccumulator.OnDeactivate` 里写 `rdata.Modifier` 的运行时赋值不受影响（`[JsonIgnore]` 只影响序列化）。
**验证**：仍需进游戏做一次"存 → 施岁稔 → 结束 → 读档 → 核对临时生命"。

---

## 三、P1-B｜画中世界找回兜底（`Components.cs`）

**问题**：`OnDeactivate` 先无条件清 `Data.Banished`，再 `FindUnit`；找不到即"跳过返回"（仅 `Dbg.Log`，**release 下连这行都被编译剔除**）。极端场景（召唤物解散 / 场景切换 / 单位池瞬时为空）→ 单位可能永留 `IsInGame=false`，且**日志里什么都看不到**。

**修法**：
- `Data` 增加 `PendingRecovery` 标记：**先找回、成功才清 `Banished`**；找不到则保持 `Banished=true` + 置 `PendingRecovery`（避免"Data 说没放逐、单位还在画中"的不一致）；
- 找不到改为 **Error 级日志**（`Main.Mod.Logger.Error`，release 可见）；
- 新增 `BanishRecovery`（MonoBehaviour，挂施法者）：在 **0.5 / 2 / 6 / 15 / 30 秒** 五个宽限点重试全局单位池扫描（覆盖"场景切换后 / 读档后"），找回即返回+麻痹并自删；超时以 Error 收尾停止重试；`AddComponent` 前去重，避免读档重复叠加；
- `OnActivate`（读档重激活）若处于 `PendingRecovery` 也会重试一次，**重试后一律 return**（不重复走放逐分支，杜绝"刚救回又放逐"）。

---

## 四、源石技艺精通改版

**旧**：`+2/+4/+6/+8`（1/7/13/19 级），文案写"克服法术抗力的施法者等级检定"（实现即法术穿透）。
**新**：**法术穿透 + 法术豁免难度各 +1/+2/+3/+4/+5**，5 档落在 **1 / 5 / 9 / 13 / 17 级**。

### 代码改动
| 文件 | 改动 |
|---|---|
| `Guids.cs` | 新增 `Mastery5Feature = P + "3f"`（核心段唯一空闲后缀）；更新 1–4 档注释 |
| `Components.cs` | `OriginiumMastery` 追加实现 `IInitiatorRulebookHandler<RuleCalculateAbilityParams>`；抽出 `EffectiveBonus()` 统一"锁定判定"，穿透走 `AddSpellPenetration`、DC 走 `AddBonusDC`；DC 仅对 `evt.Spellbook != null`（法术书来源）生效 |
| `ArchetypeSetup.cs` | `CreateMastery()` 扩到 5 档；`Mastery()` 改由档位值生成描述；成长表新增行 **(5, Mastery2)(9, Mastery3)(17, Mastery5)**，删除旧 **(7/19)** |
| `LocalizedStrings.json` | `mastery1..4.desc` 重写；新增 `mastery5.name/.desc`（273 → 275 键，0 重复） |
| `docs/rhodes-doctor-archetype.txt` | 第 37 行、修订记录同步 |
| `README.txt` | 新增 v0.6.1 → v0.6.2 变更段 |

### 规则口径（保留作者既定）
- **恶灵锁定**：两项加值失效，且**穿透与 DC 一并**改为承受转移量减值（一段锁定 = 半额转移量，二/三段 = 全额）——与旧版穿透倒扣逻辑同源，只是把 DC 也纳入。
- 20 级 Capstone 压制锁定后自动恢复。

### 反编译依据
- `RuleSpellResistanceCheck.AddSpellPenetration(Int32, ModifierDescriptor)`；
- `RuleCalculateAbilityParams.AddBonusDC(Int32, ModifierDescriptor)` + 字段 `Spellbook`；
- 官方同款组件 `Kingmaker.Designers.Mechanics.Facts.IncreaseSpellDC`（同样实现 `RuleCalculateAbilityParams`）。

### GUID / 存档
- 1–4 档 GUID **不变**（旧存档等级映射仍有效）；新增第 5 档走新 GUID。
- ⚠ **5 档等级点变化**（旧 7/13/19 → 新 5/9/13/17）会让**已存档角色的档位在新等级点重新结算**：5/9/17 级为本轮新增发放点，13 级保留。旧存档读档后按当前等级补齐（`AddFeatures` 语义），无需洗点。

---

## 五、P2 性能

### 5.1 `FxTint` 反射缓存（`FxTint.cs`）
- 新增 `ColorMemberCache`（`Type → (Color/Gradient 字段[], Color 属性[])`）与 `MaterialPropCache`（`Type → material 属性`）；
- `TintCustomScripts` 由"每组件现做 `GetFields`+`GetProperties`"改为查缓存；三处 `t.GetProperty("material", …)` 统一走 `MaterialProp(t)`；
- 不引入 Linq（避免每次分配），用 `List` 收集后 `ToArray()`，只在首次为每个 Type 建一次。

### 5.2 `AreaGroundFx.LogAreaView` 补 `[Conditional("RD_VERBOSE")]`
- 原方法只把内部 `Dbg.Log` 编译掉，**调用点仍在**：每次已注册区域生成都白跑一遍"渲染器/粒子/子物体"全树枚举 + 建 3 个 StringBuilder；
- 加特性后 release 下**调用整体消失**（与同文件 `LogInventory` 一致）。

### 5.3 `BombFx` 资源复用（`BombFx.cs`）
- 旧：每次 `PlaceMarker → AddRadiusCircle` 新建 1 个 Mesh（73 顶点）+ 2 个 Material，无缓存无销毁；
- 新：圆盘网格**按单位半径建一次**（实际半径靠 `transform.localScale` 缩放）、两个圈材质**静态缓存**、圆环顶点数组按常量半径缓存一次；
- 共享资源归静态字段所有（不挂 GameObject），`Destroy(root)` 只销毁 GO，不影响共享资源——与 `SwordHitFx` 的正确示范一致。

### 5.4 `Main.Load` 防护（`Main.cs`）
- 新增静态 `_loaded` 重入守卫：UMM 重载 / 热重入时跳过重复订阅（原先会**双重订阅**，特效重建抖动）；
- `Harmony.PatchAll` 包 try/catch：补丁失败记 Error 并 `return false`，不再上抛致 UMM 报"加载失败"、留下半加载状态。

### 5.5 P2-1 反射失败可诊断（附加，低成本）
4 处静默反射写入改为缺失即记 Error：
`ArchetypeSetup.SetAreaTargetType`（`m_TargetType`）、`ArchetypeSetup/SpellsSetup.SetAuraBuff`（`m_Buff`）、
`SpellsSetup.SetPrivateField`（通用）、`TestSetup.MarkAsSpell`（`m_Flags`，原先字段为 null 会 NRE）。
（`FxBorrow.m_WeaponModel` 原本已有失败日志，未动。）

---

## 六、未做（超出本次范围，留档）

- **P2-2** 法术书双来源(+1)与 `TenRingRow=21` 强耦合 → 需进游戏实测法位是否翻倍，属"实测项"，本轮未动。
- **P2-4** 连锁殉爆同步重入深度守卫 → 设计内连锁，需评估改动风险，本轮未动。
- **P2-5** `Specs` 顺序 = GUID 下标绑定 → 建议启动断言，属加固，本轮未动。
- **P2｜§4** `FxTint` 克隆资源显式回收、静态缓存上限/LRU → 结构较大，本轮只做了"每次施放级"的 BombFx 与反射热点；克隆资源回收依赖场景切换兜底，建议后续单独立项。

---

## 七、验证清单

- [x] `dotnet build` **0 警告 0 错误**（增删 `Mastery5Feature`、双接口、BanishRecovery 均通过）。
- [x] 编出 DLL 内含新字符串（`画中世界兜底重试器挂载失败`、`法术穿透与法术豁免难度`）与 `…d03f` GUID；旧文案 `施法者等级检定获得` 已消失。
- [x] `LocalizedStrings.json` 275 键、0 重复。
- [x] 部署后 DLL 与 LocalizedStrings.json 的 md5 与构建产物**逐字节一致**。
- [ ] **待进游戏**：① 读档后 5/9/17 级是否补发精通档位、法术面板 DC 数字是否 +N；② 岁稔 temp hp 存/读档核对；③ 恶灵期间穿透/DC 是否一并倒扣。
