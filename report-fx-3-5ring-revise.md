# 法术特效 · 单体批次「3–5 环」修订报告

> 日期：2026-10-10　范围：按作者 10-10 的口头要求，修订上一批（`report-fx-3-5ring-batch.md`）里 3–5 环的 6 个法术。
> 相关文档：`docs/fx-status.md`（现状清单，本轮已更新）、`docs/dev-notes.md` §7.10（改色方法论）、§7.11.1（离线选材）。
> 编译：`dotnet build -p:WrathPath="…"` **0 警告 0 错误**；已 `tools/deploy.sh` 部署（旧 DLL 备份 `RhodesDoctor.dll.bak-20261010-194443`）。

---

## 一、本轮结论（作者 6 条要求 → 结果）

| # | 作者要求 | 结果 | 实现 |
|---|---|---|---|
| 1 | 梦魇变色成**恐惧的黑** | ✅ 已改 | 走**重染克隆**（`ContextActionSpawnTintedFx`）：借原版"造成恐惧"的爆发，重染黑 `(0.10,0.06,0.16)` |
| 2 | 迅捷突破 → **持续**出现、**风笛的橙** | ✅ 已改 | 改走**常驻光环**（`TintedAuraFx`）：借原版加速术拖尾（`HasteBuff.FxOnStart`），重染橙 `(1.30,0.62,0.18)`，跟加速增益同生同灭 |
| 3 | 奇美拉 → **持续**、**暗红** | ✅ 已改 | 常驻光环（`TintedAuraFx`）：借 D 类天使光环，重染暗红 `(0.85,0.12,0.12)` |
| 4 | Mon3tr **只能对敌人用**（应能对友军用），特效没问题 | ✅ 已修 | 给 Spec 补 `Friendly=true`（此前缺这个标志 → 被判定为敌方法术）；治疗光保留 |
| 5 | 铸铁 → **持续**、**淡蓝** | ✅ 已改 | 常驻光环（`TintedAuraFx`）：借 D 类天使光环，重染淡蓝 `(0.62,0.90,1.35)` |
| 6 | 墨染山河特效**更大**、放**两只召唤物中间** | ✅ 已改 | 新增自定义动作 `ContextActionSpawnFxBetweenSummons`：取两只墨灵的中点、`Scale=2` |

---

## 二、逐条说明

### 1. 梦魇 → 黑（重染克隆）
`AbilitySpawnFx` 是引擎直接播的 PrefabLink、**改不了色**，所以改走缄默那条路：
`AdditionalAbilityEffectRunActionOnClickedTarget` + `ContextActionSpawnTintedFx`（每次施放在点击目标处生成一份重染副本）。
素材仍是 `FxLibrary.Fear`（造成恐惧 `CauseFear`），颜色 `NightterrorBlack = (0.10,0.06,0.16)`、`Lift=0`。
⚠ **别给纯黑**：若恐惧爆发里有加法（additive）材质，乘 0 会**直接看不见**——所以留了 ≈0.10 的分量保形状。

### 2–3、5. 迅捷突破 / 奇美拉 / 铸铁 → 常驻改色光环（`TintedAuraFx`）
这三个是**持续增益**，一次性施法爆发不够，改挂"跟增益同生同灭"的常驻光环。
`TintedAuraFx` 是一张 `(增益 GUID → 素材 + 颜色)` 的表，运行时克隆重染、挂到单位 `View` 下（原版特效不碰）。本轮新增 3 行：

| 增益 | 增益 GUID | 素材 | 目标色 |
|---|---|---|---|
| 迅捷突破 | `HasteAccelBuff`（`…8b`） | 加速术拖尾 `FxLibrary.HasteTrail` | 风笛橙 `(1.30,0.62,0.18)` |
| 奇美拉 | `Buff(30)`（`…5e`） | 天使光环 `FxLibrary.GoldHalo` | 暗红 `(0.85,0.12,0.12)` |
| 铸铁 | `Buff(32)`（`…60`） | 天使光环 `FxLibrary.GoldHalo` | 淡蓝 `(0.62,0.90,1.35)` |

`FxLibrary.GoldHalo` 为本轮新增：把 D 类"天使·光环"的素材单独暴露出来供重染（与 `AuraFor` 共用同一份原版素材、独立缓存）。

### 4. Mon3tr 目标修复
`Mon3tr` 是 `Kind.Special` 且此前**没标 `Friendly`**，在 `CreateSpell` 里被判为"有害法术"→ `canEnemies=true / canFriends=false`，于是**只能选敌人**（还会给敌人回血）。
补 `Friendly=true` 后：可对自己 / 友军施放，并挂上"目标必须是友军"的限制组件，挡住敌人。

### 6. 墨染山河 → 放大 + 居中于两只墨灵
新增自定义动作 `ContextActionSpawnFxBetweenSummons`（`Actions.cs`）：
在能力效果序列里**排在 `SpawnMonster` 之后**运行，扫描场景内**蓝图 GUID = 墨灵**的单位、取**中点**，在那里生成借来的召唤闪光（`FxLibrary.Summon`），并把克隆体的粒子缩放模式设成 `Hierarchy`（否则默认 `Local` 模式下 `Scale` 对粒子无效）→ **Scale=2**。
找不到墨灵（还没入场景 / GUID 不匹配）时**退回落点**，不会什么都不画。

---

## 三、代码改动

| 文件 | 改动 |
|---|---|
| `src/FxLibrary.cs` | 新增 `GoldHalo` 属性（D 类天使光环素材，供 `TintedAuraFx` 重染） |
| `src/TintedAuraFx.cs` | 新增 3 条表项：迅捷突破（橙）/ 奇美拉（暗红）/ 铸铁（淡蓝） |
| `src/SpellsSetup.cs` | ①`ApplyAbilityFx` 的"改色分支"扩成 `Silencer \|\| Nightterror`（梦魇重染黑）；②`switch` 里移除 迅捷突破 / 奇美拉 / 铸铁 / 墨染山河（改走常驻光环 / 自定义动作），保留 处决、Mon3tr；③新增颜色常量 `NightterrorBlack`；④`Mon3tr` Spec 补 `Friendly=true`；⑤`InkedMountains` 分支在 `SpawnMonster` 后加自定义 FX 动作 |
| `src/Actions.cs` | 新增 `ContextActionSpawnFxBetweenSummons`（召唤物中点生成特效） |
| `src/AreaGroundFx.cs` | `EnableHierarchyScaling` 由 `private` 改 `internal`（给上面那个动作用） |
| `docs/fx-status.md` | 3.1 表 6 行按新版更新；批次备忘同步 |

**不碰任何逻辑 / 数值**（除 Mon3tr 的目标标志修复外，其余法术的效果、持续、豁免均未动）。

---

## 四、待进游戏确认（作者实测清单）

1. **梦魇的"黑"**：重染后若**完全看不见**，说明恐惧爆发是加法材质（乘 0 = 没）→ 把 `NightterrorBlack` 三分量整体抬到 ≈0.25（`SpellsSetup` 常量处）。
2. **迅捷突破的橙**：素材是加速术拖尾，原版这份 prefab 有"周身 / 地面两套相反的映射"（见 dev-notes §7.10.2）——单色重染可能偏某一边；若观感不对，按 §7.10 的"全局打底 + 点染"手法调（`TintedAuraFx` 目前是单色版）。
3. **⚠ 迅捷突破的橙会同时出现在"永恒的注视"上**：两者共用同一个加速增益 `HasteAccel`（C34/C36）。若不要这样，把 `TintedAuraFx` 里那条的 `BuffGuid` 改用 `SwiftBreachBuff`（1 轮，仅迅捷突破，但更短）。
4. **奇美拉 / 铸铁的暗红 / 淡蓝**：屏幕色 ≈ tint 的 2~3 次幂（dev-notes §7.10.1），初值是离线猜的；进游戏看观感再调 `TintedAuraFx` 里的 `Tint` / `Lift`。
5. **Mon3tr**：确认现在能对**友军 / 自己**施放、且**不能**选敌人。
6. **墨染山河**：确认特效**变大**、且落在**两只墨灵中间**；若仍落在落点（不在中间），说明动作跑时墨灵还没入场景——届时改成"延迟一帧"再取中点。
7. ⚠ **换 DLL 后必须重启游戏**。

---

## 五、新发现（只记录，不改）

1. `FxLibrary` 里的 `Haste`（加速术爆发）/ `Protection`（法师护甲）两个元素**本轮后暂时没人引用**（迅捷突破 / 铸铁改走了常驻光环）；保留在库里备用，未删。
2. `TintedAuraFx` 目前是**单色**重染；加速术拖尾那种"两套映射"的素材，单色做不到"周身 / 地面分别调"（骑兵突进为此单独写了 `HasteTrailFx`）。若以后常驻光环也要这种双色，需要给 `TintedAuraFx` 加"点染"能力。

---

## 六、下一步

3–5 环 7 个已按作者要求修订完毕。剩余单体批次仍是 **6 环（涌潮悲歌 / 黑蛇之焰 / 罪罚）→ 7 环（镜中流形 / 终末狙击 / 归溟）→ 8 环（画中世界）→ 9 环（影霄·拔刀 / 旧日的战术家）**，共 **9 个**。
