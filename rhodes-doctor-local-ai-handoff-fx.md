# 罗德岛博士 · 法术特效 — 交接（给下一个对话）

> 日期：2026-10-10 夜　用途：作者要换对话，这里把当前状态、约定、下一步讲清楚。
> **权威清单永远是 `docs/fx-status.md`**；本文件只是"从哪读、做什么、别踩什么"的索引。

---

## 0. 一句话现状

64 个指令法术：**群体 35 个已全部接线**（只等进游戏确认）；**单体 / 其他 29 个**里 **19 个有特效、10 个还没有**（都在 6–9 环）。
最近在做「单体批次」（按环序逐个补 FX），并顺手修了一批"buff 显示名 / 目标 / 特效放大"的问题。

---

## 1. 先读这几份

| 文档 | 讲什么 |
|---|---|
| `docs/fx-status.md` | **现状清单（权威）**：每个法术有没有特效、怎么做的、待确认什么、计数 |
| `docs/dev-notes.md` | 踩坑记录。特效看 **§7**（选材决策树 / 自绘 / 摆位 / 时间轴 / §7.10 改色方法论 / §7.11 C 类地面区域配方 / §7.12 TintedAuraFx） |
| `docs/fx-plan.md` / `docs/fx-recolor.md` | 分类与素材库 / 借引用 + 克隆重染手法 |
| `report-fx-*.md`（仓库根） | 各批次报告（2 环 / 3–5 环 / C 类 / 墨染山河 / 梦魇效果 / buff 命名） |

---

## 2. 编译与部署

```bash
dotnet build -p:WrathPath="D:/steam/steamapps/common/Pathfinder Second Adventure"
bash tools/deploy.sh          # 覆盖到游戏 Mods/RhodesDoctor，自动备份旧 DLL
```

- ⚠ **换 DLL 后必须重启游戏**（DLL 只在启动时加载）。
- 游戏日志：`%USERPROFILE%\AppData\LocalLow\Owlcat Games\Pathfinder Wrath Of The Righteous\Player.log`
  （模组自打的行以 `[RhodesDoctor]` 开头；战斗/UI 另看 `GameLogFull.txt`）。
- 离线素材库：游戏目录的 `blueprints.zip`（`tools/scan_buff_fx_all.py`、`_tools/tmp/scan_spawnfx_kw.py` 等可复用）。

---

## 3. 最近做完的（都"待作者进游戏确认"）

- **单体 0–5 环 19 个**已接线（0 环 5 + 1 环 4 + 2 环 3 + 3–5 环 6 + 时空静止自带）。做法：给法术挂**施法爆发**（`AbilitySpawnFx`）或**常驻光环**（`TintedAuraFx`）。
- **3–5 环的改色/改素材**（作者三轮反馈）：处决爆发；**梦魇→黑影缠身**（血雾素材重染黑）；迅捷突破→常驻橙；奇美拉→常驻暗红**罩子**；铸铁→常驻淡蓝**护甲**；**Mon3tr 修 `Friendly`**（原来只能选敌人）。
- **墨染山河**：作者定案**去掉**召唤特效（保留自定义动作代码备用）。
- **buff 显示名全模组修正**：23 个 buff 原来显示成**法术名**（如两条都叫"梦魇"），已改成专属状态名（惊惧 / 动摇 / 火山（余焰）…）；`LocalizedStrings.json` **+27 条键**。
- **梦魇恐惧 buff** 对齐原版 `Buffs/Frightened`/`Buffs/Shaken`：补了 `SpellDescriptorComponent(Fear)`。

---

## 4. 下一步（还没做）

1. **单体 6–9 环 10 个**（无特效）：涌潮悲歌 / 黑蛇之焰 / 罪罚 / 镜中流形 / 终末狙击 / 归溟 / 画中世界 / 影霄·拔刀 / 旧日的战术家（+ 墨染山河，作者不要特效）。
   做法沿用既定批次：**语义借原版同类法术的 `AbilitySpawnFx`**；`Range==Personal` 锚施法者、其余锚目标。
2. **群体侧**：全部已接线，只剩"进游戏确认"；**炮火延伸后两轮缺 FX** 是已知遗留。

---

## 5. 关键约定 / 别踩的坑（本轮新增的加粗）

- 素材**离线查 `blueprints.zip`**、运行时用 `AbilityRefs` / `BuffRefs` 借，**不写死 AssetId**。
- `AbilitySpawnFx` **改不了色** → 要改色走"重染克隆"（`ContextActionSpawnTintedFx`）或常驻用 `TintedAuraFx`。
- **⚠ 放大特效别用根节点缩放**：`Local` 模式下会"**没变大、反而把高于根的部分抬高**"（墨染山河实测）。用 `FxTint.ScaleParticleSizes`（只放大粒子）。
- **⚠ `L10n.S(key, zh, en)` 运行时只返回键**，字符串在 **`LocalizedStrings.json`（手工维护、无生成脚本）**。
  改了代码**必须同步加 JSON 键**，否则显示 `[unknown key: rhodesdoctor.xxx]`（看着像乱码）。
- **⚠ buff 显示名别用 `s.Name`**（会显示成法术名）→ 用 `L10n.S("spell.<法术>.<状态>.name", …)`。
- 原版**恐惧**参照：`Spells/Level1/CauseFear` 失败挂 `Buffs/Frightened`、成功挂 `Buffs/Shaken`（两者都带 `SpellDescriptorComponent(Fear)`）。
- `FxLibrary` 里 `GoldHalo` / `FearAura` / `Haste` / `Protection` 等元素**当前没被引用**，保留备用。

---

## 6. 等作者实测确认的点

- 单体 0–5 环那 19 个的观感 / 锚点 / 能不能借到素材（看 `Player.log` 的 `特效借用成功/失败`）。
- 梦魇：黑影缠身是否像 debuff、buff 名「惊惧 / 动摇」是否显示正常（轮数按作者要求**不**同步，保持"恐慌 2 轮 + 动摇 1 轮/级"）。
- 奇美拉罩子是否够明显、铸铁护甲跑动时是否正常、迅捷突破橙拖尾（⚠ 与"永恒的注视"共用加速增益）。
- 群体侧全部待确认 + 炮火延伸后两轮。
