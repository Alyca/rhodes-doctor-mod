# 定向 / 时长 / 特效 一批修订（作者实测反馈）

> 日期：2026-10-11　范围：按作者反馈修 8 项（定向改为以施法者为中心、时长改分钟/级、D 类光环看不见、火山特效）。
> 相关：`docs/fx-status.md`、`docs/dev-notes.md` §19。
> 编译：`dotnet build -p:WrathPath="…"` **0 警告 0 错误**；已 `tools/deploy.sh` 部署（旧 DLL 备份 `RhodesDoctor.dll.bak-20261011-132046`）。
> ⚠ 部署时游戏在运行——**必须重启游戏**才会加载新 DLL。

---

## 一、作者反馈 → 结果

| # | 法术 | 反馈 | 结果 |
|---|---|---|---|
| 1 | 画中世界 | 该按"**放逐出来之后的麻痹轮数**"走等级，不是放逐轮数 | ✅ 放逐固定 **2 轮**；返回后**麻痹 1 轮/级** |
| 2 | 坚守阵地 | 没特效；应**以施法者为中心**；改成**分钟/级** | ✅ 三条都改 |
| 3 | 骑兵突进 | 应**以施法者为中心**；改成**分钟/级** | ✅ 两条都改 |
| 4 | 乌拉！ | 应**以施法者为中心**；效果好像没生效 | ✅ 改为中心；效果见第四节 |
| 5 | 牵制射击 | 应改成**轮/级** | ✅ 改 |
| 6 | 过载模式 | 没特效 | ✅ 换 D 类光环素材（见第三节） |
| 7 | 圣域 | 没特效；应**以施法者为中心** | ✅ 两条都改 |
| 8 | 火山 | 柱子特效太小；应**一轮一轮触发特效** | ✅ 首轮放大 2 倍；余焰**每轮**再放一发 |

---

## 二、定向：改成"以施法者为中心"

**坚守阵地 / 骑兵突进 / 乌拉！/ 圣域** 原来是"近距内选一点为中心，30 尺爆发"（`Range` 默认近距 + 点选）。
现改为 `Range = AbilityRange.Personal`——`CreateSpell` 里 `personal` 的法术**不设点选**，AoE 圈直接**挂在施法者身上**：
- 施法者**自己也算在内**（`canSelf = personal || ...`）；
- 描述同步改为"**以你为中心**，30 尺爆发内全部友军"。

## 时长：改成"分钟/级"

**坚守阵地 / 骑兵突进** 加 `Unit = DurationRate.Minutes`（原为轮）+ 保留 `PerLevel` → 时长 = **圣武士等级 分钟**；
Meta 的 `DurationKey` 改 `rhodesdoctor.duration.minute1level`（表头显示）。

**牵制射击**：`Rounds=1` → `PerLevel=true`（**1 轮/级**），Meta 改 `round1level`。

**画中世界**：按作者意思，**放逐固定 2 轮**（`ApplyBuff(banish, Fixed(2))`），**返回后的麻痹 = 1 轮/级**
（由 `PaintedWorldBanishTracker.OnDeactivate` 取能力 Rank 计算）。

---

## 三、D 类光环"看不见"：换素材

作者反馈 **坚守阵地 / 过载模式 / 圣域 看不到光环**。原来 D 类（友军增益）借的是原版"**天使·光环**"
（`AngelHaloBuff` 的 `FxOnStart`）——**一圈细环、且贴地**，太不显眼（与 铸铁"腿上有个物件"同源）。
现改用**天使屏障**（`AngelBarrier_Buff`，一个包裹身体的**金色罩子**，与奇美拉同一份素材），明显得多。
（接线只有一处：`FxLibrary.AuraFor`。备选：`AngelWingsBuff` / `AngelWardFromHarmBuff`，换一行即可。）

## 火山：放大 + 逐轮特效

- **首轮落点爆发放大 2 倍**：新增动作 `ContextActionSpawnScaledFx`，用 `FxTint.SpawnPlaced`
  **只放大粒子本身、不动根节点**（dev-notes §14.3 的教训）。火山从 `ApplyAbilityFx` 的通用 `Fire` 挪到自己的分支。
- **余焰每轮也放一发**：给 `VolcanoEmber` 增益的 `newRound` 加一发（1.2 倍）。原来只有首轮有特效。

---

## 四、乌拉！的"效果"说明

代码里**效果是实现了的**：`Ura` 分支给增益挂
`AddContextStatBonus(攻击 +Rank)`、`AddContextStatBonus(武器伤害 +Rank)`（10 级起 2、否则 1）、
并在激活时 `ContextActionRemoveBuffsByDescriptor(Shaken|Frightened)`（结束动摇/恐惧）。
"没生效"最可能的原因与另外三个一样——**原先是"点选一点为中心"的范围法术**，施法时圈没罩到人 → 增益没挂上。
现在改成**以施法者为中心**后应恢复正常；若仍无效，请把 `Player.log` 里相关行发我。

---

## 五、代码改动

| 文件 | 改动 |
|---|---|
| `src/SpellsSetup.cs` | 坚守阵地/骑兵突进/乌拉/圣域 改 `Range=Personal`（+ 前两者 `Unit=Minutes`）；牵制射击 `PerLevel=true`；对应 Meta 的 `DurationKey`；画中世界放逐回 `Fixed(2)`；火山首轮放大 + 余焰逐轮 FX；对应描述同步 |
| `src/FxLibrary.cs` | D 类光环素材 `AngelHaloBuff` → `AngelBarrier_Buff` |
| `src/Actions.cs` | 新增 `ContextActionSpawnScaledFx`（按粒子尺寸放大的一次性 FX） |
| `src/Components.cs` | 画中世界返回后的**麻痹轮数取 Rank**（1 轮/级） |
| `LocalizedStrings.json` | 坚守阵地/骑兵突进/乌拉/圣域/画中世界 描述同步 |

---

## 六、待进游戏确认

1. **坚守阵地 / 骑兵突进 / 乌拉 / 圣域**：施法时**圈在自己身上**，附近友军都吃到；前两者持续应是**分钟/级**。
2. **坚守阵地 / 过载模式 / 圣域**：身上应有明显的**金色罩子**。
3. **牵制射击**：持续 1 轮/级。
4. **画中世界**：放逐 2 轮；回来后的麻痹随等级变长。
5. **火山**：首轮爆发明显变大；**余焰每轮都有**火光。
6. **乌拉**：确认增益是否生效（见第四节）。
7. ⚠ **换 DLL 后必须重启游戏**。
