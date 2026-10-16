# 法术特效 · 单体批次「6–9 环」完成报告

> 日期：2026-10-10（晚）　范围：**单体批次最后 9 个**（6–9 环）——把 `docs/fx-status.md` 第三节 3.2「无特效」清空（只剩作者定案不要视觉的墨染山河）。
> 相关文档：`docs/fx-status.md`（已更新）、`docs/dev-notes.md` §15（本轮经验）、`docs/fx-plan.md`。
> 编译：`dotnet build -p:WrathPath="…"` **0 警告 0 错误**；已 `tools/deploy.sh` 部署（旧 DLL 备份 `RhodesDoctor.dll.bak-20261010-214234`）。
> 离线校验：`python _tools/tmp/verify_6_9ring_materials.py` 全部素材确认带 FX（见第四节）。

---

## 一、本轮做了什么

把 9 个"完全没特效"的法术全部接线，做法沿用前几批的现成通道（爆发 / 常驻光环 / 减益光环），并顺手补了**炮火延伸后两轮缺 FX** 这个旧缺口。

| 环 | 法术 | Kind | 做法 | 素材 |
|---|---|---|---|---|
| 6 | 涌潮悲歌 | Buff（自身） | **常驻潮汐蓝护罩**（`TintedAuraFx`） | 天使屏障 `AngelBarrier_Buff` → 重染 `(0.35,0.75,1.45)` |
| 6 | 黑蛇之焰 | Special（单体火） | 落点火焰爆发（`ApplyAbilityFx`） | `FxLibrary.Fire`（焰击术） |
| 6 | 罪罚 | Special（敌方减益） | 目标诅咒光环（`DebuffAuraFor`） | **新 `CurseAura`**：降咒 `ArmyBestowCurseBlastBuff` |
| 7 | 镜中流形 | Special（友军） | 落点幻术爆发（`ApplyAbilityFx`） | `FxLibrary.Shadow`（幻影杀手） |
| 7 | 终末狙击 | Special（敌方标记） | 目标锁定标记光环（`DebuffAuraFor`） | 复用 `MarkAura`（火之烙印） |
| 7 | 归溟 | Special（自身） | **常驻深渊墨绿缠身**（`TintedAuraFx`） | 血雾 `BloodHazeBuff` → 重染 `(0.10,0.34,0.46)` |
| 8 | 画中世界 | Debuff（麻痹） | **常驻画境紫环**（`TintedAuraFx`） | 天使光环 `AngelHaloBuff` → 重染 `(0.68,0.42,1.25)` |
| 9 | 影霄·拔刀 | Special（即死） | 落点死亡爆发（`ApplyAbilityFx`） | **新 `FxLibrary.Death`**：指骨术 `FingerOfDeath` |
| 9 | 旧日的战术家 | Special（自身） | **常驻金色光环**（`TintedAuraFx`，原色） | 天使光环 `AngelHaloBuff`（D 类同款） |

**另补**：炮火延伸（6 环，`ArtilleryBarrage`）第 2/3 轮的追踪爆炸原先**只结算伤害、不播 FX**（首轮由能力自身 `AbilitySpawnFx` 播），现于 `ContextActionArtilleryStrike` 里补一发同源火焰爆发。

---

## 二、逐条说明

### 落点爆发（3 个）
`ApplyAbilityFx` 是"元素 → 法术"的一览表，加三个 case 即可（锚点按 `Range==Personal?施法者:落点` 自动决定，这三者都非 Personal → 锚落点）：

- **黑蛇之焰 / 镜中流形** 直接复用已有元素 `Fire` / `Shadow`，零新素材。
- **影霄·拔刀** 新增元素 `Death`：借原版**指骨术 `FingerOfDeath`** 的 `AbilitySpawnFx`（`e8569f44…`，Anchor=`SelectedTarget`）——原版最经典的"死灵即死"爆发，与"强韧失败即死"最贴；备选 `SlayLiving`（`cc9120c1…`）。

### 常驻光环（4 个，走 `TintedAuraFx`）
`TintedAuraFx` 是"跟增益同生同灭、长在单位身上、且能改色"的表驱动通道（`Entries` 一行 = 一个 buff GUID）。本轮加 5 行：

- **涌潮悲歌**：用**天使罩子**（`BarrierAura`）重染潮汐蓝——法术给 60 临时生命，"一层护体潮水"的罩子形状最合。
- **归溟**：用**血雾**（`PlasmaAura`，身体型）重染深渊墨绿——幽灵鲨的死灵自增益，要"贴身缠身"的观感（与梦魇同源、异色）。
- **画中世界**：用**天使光环**（`GoldHalo`）重染画境紫——给被"收进画境"的敌人套一圈紫色环。
- **旧日的战术家**：`GoldHalo` **原色**（D 类同款金环）；坚韧 / 敏捷两个套件各一行。
  ⚠ 该法术的两个增益名叫 `TacticianEnduring/Agile`（不以 `TacticianOfOld` 开头），所以 `CreateExtraBuff` 的 `ownerSpell` 反查**匹配不上** `AuraSpells` 名单；但 `TintedAuraFx` 按 **buff GUID** 匹配，不受影响。

### 敌方减益光环（2 个，走 `DebuffAuraFor`）
`DebuffAuraFor` 返回的素材用 `SetFxOnStart` **原色**上屏（改不了色）：

- **终末狙击**：复用 `MarkAura`（火之烙印）——"给敌人做记号"，与"下轮必中狙击"的锁定语义一致。
- **罪罚**：新增 `CurseAura`（借**降咒 `ArmyBestowCurseBlastBuff`**，`FxOnStart`=`9a65b11d…`）——诅咒/反噬主题；备选 `DoomOfServitudeBuff`、`WitchHexDeathCurseBuff`。

### 炮火延伸后两轮补 FX
- 现状：首轮在落点有爆发（`ApplyAbilityFx` 的 `ArtilleryBarrage → Fire`），第 2/3 轮由挂在施法者身上的追踪增益触发，走 `ContextActionArtilleryStrike`——它只做伤害扫描、**没有 FX**，所以画面上"只有第一轮有东西"。
- 补法：在 `ContextActionArtilleryStrike.RunAction` 里、确认追踪点后，加一行
  `FxTint.SpawnPlaced(FxLibrary.Fire, point, Quaternion.identity, 1f, 2.5f)`（**原样实例化、不重染、只放位置、自动销毁**，素材与首轮同源）。`Context.DisableFx` 时跳过。

---

## 三、代码改动

| 文件 | 改动 |
|---|---|
| `src/FxLibrary.cs` | 新增元素 `Death`（指骨术/杀死活物）；新增 `CurseAura()`（降咒/奴役之厄/死亡诅咒）；`DebuffAuraFor` 加 `FinalSnipe` / `SinPunishment` 两个 case；更新注释 |
| `src/TintedAuraFx.cs` | `Entries` 加 5 行：涌潮悲歌 / 归溟 / 画中世界 / 旧日的战术家（坚韧 + 敏捷） |
| `src/SpellsSetup.cs` | `ApplyAbilityFx` 加 3 个 case：`BlackSnakeFlame→Fire` / `MirrorFlow→Shadow` / `ShadowBladeDraw→Death`；加注释说明其余 6 个走光环 |
| `src/Actions.cs` | `ContextActionArtilleryStrike` 补一发 `FxTint.SpawnPlaced(FxLibrary.Fire, point, …)`（后两轮 FX） |
| `docs/fx-status.md` | 3.1 增 9 行、3.2 只剩墨染山河、五·汇总（单体有特效 19→28、无特效 10→1）、批次备忘与下一步同步 |
| `docs/dev-notes.md` | 新增 §15：本轮 9 个法术的素材与做法 + 离线选材/校验命令 |

> 未新增任何 `L10n.S` 键，`LocalizedStrings.json` **无需改动**（纯视觉改动，不涉及文字）。

---

## 四、离线选材证据（`blueprints.zip`，`_tools/tmp/verify_6_9ring_materials.py`）

| 用途 | 蓝图 | FX | 锚点 / 时间 |
|---|---|---|---|
| 影霄·拔刀 → 死亡爆发 | `Spells/Level7/FingerOfDeath` | `e8569f44…` | SelectedTarget / OnStart |
| （备选） | `Spells/Level5/SlayLiving` | `cc9120c1…` | ClickedTarget / OnApplyEffect |
| 镜中流形 → 幻术爆发 | `Spells/Level4/PhantasmalKiller` | `c44b1679…` | SelectedTarget / OnApplyEffect |
| 终末狙击 → 标记 | `Armies/Abilities/ArmyMarkOfHeaven` | `45622cc6…` | SelectedTarget / OnApplyEffect |
| 罪罚 → 诅咒光环 | `Armies/Abilities/ArmyBestowCurseBlastBuff` | `9a65b11d…` | FxOnStart |
| （备选） | `Mythic/Lich/LichSpells/DoomOfServitudeBuff` | `9adb9cfa…` | FxOnStart |
| 涌潮悲歌 → 护罩 | `Mythic/Angel/AngelBarrier_Buff` | `2f8982b7…` | FxOnStart |
| 归溟 → 缠身 | `Mythic/Demon/DemonSpells/BloodHazeBuff` | `5d6fcb6c…` | FxOnStart |
| 画中世界/旧日 → 光环 | `Mythic/Angel/AngelHalo/AngelHaloBuff` | `2559a7b0…` | FxOnStart |

> 注：`Fire` / `Shadow` 是已在本 mod 内实测上屏过的既有元素（火山 / 黄昏 / 王冠的回响等在用），未重复取证。

---

## 五、待进游戏确认（作者实测清单）

1. **涌潮悲歌**：身上应有一层**蓝色护罩**（3 轮）。若太淡，把 `TintedAuraFx` 里该条的 `Tint` 抬向 `(0.5,0.85,1.5)`。
2. **黑蛇之焰**：目标身上应有一次**火焰爆发**。
3. **罪罚**：被罚者身上应有一圈**诅咒光环**（1 轮/级）。
4. **镜中流形**：被复制的友军身上应有**幻术爆发**。
5. **终末狙击**：被标记者身上应有**标记光环**（1 轮）。
6. **归溟**：身上应有一层**墨绿缠身**（3 轮）。
7. **画中世界**：被麻痹的敌人身上应有**紫色环**（2 轮）。
8. **影霄·拔刀**：目标身上应有**死亡爆发**。
9. **旧日的战术家**：身上应有**金色光环**（3 轮）。
10. **炮火延伸**：三轮**都要**在落点看到火焰爆发（此前只有第一轮）。
11. ⚠ **换 DLL 后必须重启游戏**。

> 颜色都是离线初值，进游戏看实际观感再微调（`TintedAuraFx.Entries` 各条的 `Tint` / `Lift`）。

---

## 六、新发现（只记录，不改）

1. `AngelHaloBuff` / `AngelBarrier_Buff` / `BloodHazeBuff` 三个素材被多个法术复用（改色后各自克隆，互不影响，原版也不受影响）；同屏多光环叠加的观感问题见 `fx-status.md` D 类备注。
2. 黑蛇之焰目前用**普通火焰爆发**（焰击术，橙红）。若想突出"黑焰"，可改走 `ContextActionSpawnTintedFx`（同缄默那条路）重染成暗色——但火焰素材本身很亮，重染暗色有"压成灰"的风险，本轮未做。

---

## 七、下一步

- **接线阶段收尾**：64 个指令全部有视觉（唯一例外墨染山河，作者定案无视觉）。
- 后续转入**作者进游戏确认 + 观感微调**：按第五节清单逐项看，颜色/浓淡不合意就改 `TintedAuraFx.Entries` 或 `SpellsSetup.ApplyAbilityFx` 对应行。
- 若确认无误，可把 `docs/fx-status.md` 里各行的 ⚠ 状态改为 ✅。
