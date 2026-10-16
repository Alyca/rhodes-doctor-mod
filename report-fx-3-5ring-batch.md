# 法术特效 · 单体批次「3–5 环」报告

> 日期：2026-10-10　范围：把单体 / 其他里 **3–5 环的 7 个指令**（处决 / 梦魇 / 迅捷突破 / 奇美拉 / Mon3tr / 铸铁 / 墨染山河）补上"施法爆发"。
> 相关文档：`docs/fx-status.md`（现状清单，本轮已更新）、`docs/fx-plan.md`、`docs/dev-notes.md` §7.11.1（离线选材）。
> 前序批次：`report-fx-2ring-batch.md`（2 环 3 个）；本批沿用 fx-status 第三节的"单体批次"做法。

---

## 一、本轮结论

3–5 环 **7 个法术全部接线完成**（⚠ 均待进游戏确认）：

| 法术 | 环 | 元素 | 素材（原版法术的 `AbilitySpawnFx`） | 锚点 | 状态 |
|---|---|---|---|---|---|
| 处决 | 3 | 处决 / 处刑 | 刺客·公开处决 `AssassinPublicExecutionAbility`（`d80d51c0…`） | 施法者 | ⚠ 待进游戏确认 |
| 梦魇 | 4 | 恐惧 | 造成恐惧 `CauseFear`（`cbfe312c…`） | 目标 | ⚠ 待进游戏确认 |
| 迅捷突破 | 4 | 加速 | 加速术 `Haste`（`3cf0849b…`） | 施法者 | ⚠ 待进游戏确认 |
| 奇美拉 | 5 | 增益（战力） | 复用 `FxLibrary.Heroism`（英雄气概 `Heroism`，`44295e97…`） | 目标 | ⚠ 待进游戏确认 |
| Mon3tr | 5 | 治疗 | 复用 `FxLibrary.Heal`（治疗轻伤 `CureLightWounds`） | 目标 | ⚠ 待进游戏确认 |
| 铸铁 | 5 | 防护 / 护甲 | 法师护甲 `MageArmor`（`f447e243…`） | 目标 | ⚠ 待进游戏确认 |
| 墨染山河 | 5 | 召唤 / 创造 | 钩锁与召唤之秘 `SecretOfHookingAndSummoning`（`58f7f63f…`） | 落点 | ⚠ 待进游戏确认 |

编译：`dotnet build -p:WrathPath="…"` **0 警告 0 错误**。

做法沿用单体批次既定方案：**给法术挂一发施法爆发**（`ApplyAbilityFx` 一处接线）——
`s.Range == AbilityRange.Personal` 的锚**施法者**、其余锚**目标**（与祝福术一致）。
所以处决 / 迅捷突破（Personal）锚施法者，其余 5 个锚目标 / 落点。

---

## 二、离线选材（`blueprints.zip`，未进游戏）

方法照 `docs/dev-notes.md` §7.11.1：**先离线扫全库 `AbilitySpawnFx`，再按语义挑**，不写死 AssetId
（借到的仍是运行时从原版蓝图读的 `PrefabLink`）。本次用的临时脚本：
`_tools/tmp/scan_spawnfx_kw.py`（按关键字列带 `AbilitySpawnFx` 的法术 + 锚点）、
`_tools/tmp/scan_paths_kw.py`（按路径关键字列候选并标注有无 FX）。

| 法术 | 元素 | 首选素材 | 锚点 | 选它的理由 | 备选 |
|---|---|---|---|---|---|
| 处决 | `Execution` | 刺客·公开处决 `AssassinPublicExecutionAbility` | Caster | 语义 1:1（"处决"），且**锚施法者**正合处决的 Personal 距离 | `ExecutionerAssassinateDemoralizeAction`（刽子手·刺杀威吓，同一份素材 `d80d51c0…`） |
| 梦魇 | `Fear` | 造成恐惧 `CauseFear` | SelectedTarget | 原版恐惧系法术的经典爆发，贴"恐惧、影响心灵" | `RitualCauseFearAbility`（`226a158b…`，锚落点） |
| 迅捷突破 | `Haste` | 加速术 `Haste` | SelectedTarget | 描述即"加速 3 轮"，语义 1:1（`HastedAssaultAbility` 也用它、锚施法者） | 无（同类只此一个） |
| 奇美拉 | 复用 `Heroism` | 英雄气概 `Heroism` | SelectedTarget | 同为"给一名友军的战力增益"（+4 攻击 / 伤害 + 半级 / 无视 DR） | `GoodHope` |
| Mon3tr | 复用 `Heal` | 治疗轻伤 `CureLightWounds` | SelectedTarget | 描述即"恢复每级 1d6 生命"，治疗语义 1:1 | `Heal`（6 环） |
| 铸铁 | `Protection` | 法师护甲 `MageArmor` | SelectedTarget | 同为"给一名友军套护甲"的防护系增益 | `ProtectionFromArrows`（`c30bef03…`）、`ProtectionFromEvil`（`5c83a725…`） |
| 墨染山河 | `Summon` | 钩锁与召唤之秘 `SecretOfHookingAndSummoning` | Caster | 召唤主题的法术闪光；**召唤怪物系全部无 FX**，退到同为召唤主题的 9 环法术 | 无 |

**搜过但排除的坑**：

- **石肤术 `Stoneskin` 没有 `AbilitySpawnFx`**（离线核实为空），所以"铸铁"的防护素材退到**法师护甲**；
- **召唤怪物系（`SummonMonster` I–IX）全部没有 `AbilitySpawnFx`**（离线核实为空），所以"墨染山河"退到
  9 环的召唤主题法术 `SecretOfHookingAndSummoning`；
- ⚠ **`d80d51c0…`（处决用的那份）同时被多条"军团领袖能力"引用**（天使召唤天军 / 恶魔召唤恶魔等），
  是一份**偏通用**的"施法者爆发"——但正因为它被 `AssassinPublicExecutionAbility` 引用，语义上就是"处决"，故采用；
- **`Haste` 的爆发素材与"骑兵突进"的拖尾素材不是同一份**：骑兵突进用的是 `HasteBuff` 的 `FxOnStart`
  （持续拖尾），本批迅捷突破用的是 `Haste` **法术**的 `AbilitySpawnFx`（一次性施法闪光），两者不冲突。

---

## 三、代码改动

| 文件 | 改动 |
|---|---|
| `src/FxLibrary.cs` | 新增五个元素属性：`Execution` / `Fear` / `Haste` / `Protection` / `Summon`（各带候选与语义注释，走 `FxBorrow.AbilityBurstFx`，不写死 AssetId）；奇美拉复用 `Heroism`、Mon3tr 复用 `Heal`，不另立元素 |
| `src/SpellsSetup.cs` | `ApplyAbilityFx` 的 `switch` 新增七个 case：`Execution`→`Execution`、`Nightterror`→`Fear`、`SwiftBreach`→`Haste`、`Chimera`→`Heroism`、`Mon3tr`→`Heal`、`Ironcast`→`Protection`、`InkedMountains`→`Summon` |
| `docs/fx-status.md` | 7 个法术从"无特效"移到"已有特效"表；计数更新（单体有特效 13→20、无特效 16→9）；单体批次备忘补 3–5 环元素表与进度；医疗无人机按作者口径改为 ✅ 完善 |

改动只有"接线 + 素材表"两处，**不碰任何逻辑 / 数值**（7 个法术的效果、持续、豁免均未动）。

---

## 四、待进游戏确认（作者实测清单）

1. **观感是否贴题**（七个素材都是"语义借"，实际画面未验）：
   - 处决 = 公开处决的施法者爆发（是否够"致命一击"的观感？）；
   - 梦魇 = 造成恐惧的恐惧爆发（是否偏"红紫"？）；
   - 迅捷突破 = 加速术的闪光（与骑兵突进的蓝拖尾是否撞脸？）；
   - 奇美拉 = 英雄气概的金色增益闪光；Mon3tr = 治疗光；铸铁 = 法师护甲的防护闪光；
   - 墨染山河 = 召唤闪光（落在召唤点上）。
2. **锚点**：处决 / 迅捷突破锚在**施法者**，其余锚在**目标 / 落点**，确认没有偏到地面 / 施法者。
3. **能否借到**：看 `Player.log` 里的 `特效借用成功 / 失败` 行——`FxBorrow` 借不到时**静默跳过**，
   "编译通过"不等于画面上有东西（见 fx-status 第二节的复核提醒）。
4. ⚠ **换 DLL 后必须重启游戏**（DLL 启动时加载）；部署用 `bash tools/deploy.sh`。

---

## 五、新发现（只记录，不改）

1. **奇美拉 / 铸铁是 `Kind.Buff`（持续增益）**，本批按"单体批次"既定做法给了**一次性施法爆发**。
   若作者要的是"跟增益同生同灭的常驻光环"（像 2 环的不稳定血浆 / 反重力后来改成 `TintedAuraFx` 那样），
   可另行改造——本轮不擅自改。
2. **`d80d51c0…` 是军团领袖能力的通用爆发**（见第二节），处决借用它属于"够用但非专属"；
   若进游戏观感不对，可换 `LeadBlades`（`352469f2…`，锚目标）等"武器强化"类素材。
3. 原版**召唤 / 防护（石肤）两条语义线都没有可直接借的 `AbilitySpawnFx`**（召唤怪物系、石肤术全为空），
   后续遇到"召唤 / 护甲"类法术要预留这条（退到主题相近的高环法术）。
4. 医疗无人机（1 环）按作者口径**已完成**，本轮把 fx-status 里的状态从"⚠ 待确认"改为"✅ 完善"，未动其代码。

---

## 六、下一步

按环序继续单体批次：**6 环（涌潮悲歌 / 黑蛇之焰 / 罪罚）→ 7 环（镜中流形 / 终末狙击 / 归溟）
→ 8 环（画中世界）→ 9 环（影霄·拔刀 / 旧日的战术家）**，共剩 **9 个**。
其中涌潮悲歌（Personal 自增益）、归溟（Personal）、影霄·拔刀（恶灵专用）按既定规则应锚**施法者**；
挑素材前先查目标素材的 AssetId 是否已被别的元素占用（避免撞脸，见 `report-fx-2ring-batch.md` 第五节）。
