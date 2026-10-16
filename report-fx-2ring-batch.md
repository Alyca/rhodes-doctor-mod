# 法术特效 · 单体批次「2 环」报告

> 日期：2026-10-10　范围：把单体 / 其他里 **2 环的 3 个指令**（不稳定血浆 / 反重力 / 缄默）补上"施法爆发"。
> 相关文档：`docs/fx-status.md`（现状清单，本轮已更新）、`docs/fx-plan.md`、`docs/dev-notes.md` §7.11.1（离线选材）。
> 前序批次：`report-fx-c-batch.md`（C 类持续地面区域）；本批沿用 fx-status 第三节的"单体批次"做法。

---

## 一、本轮结论

2 环 3 个法术**全部接线完成**（⚠ 均待进游戏确认）：

| 法术 | 环 | 元素 | 素材（原版法术的 `AbilitySpawnFx`） | 状态 |
|---|---|---|---|---|
| 不稳定血浆 | 2 | 增益（战力） | 英雄气概 `Heroism`（`44295e97…`，锚目标） | ⚠ 待进游戏确认 |
| 反重力 | 2 | 缓慢 | 缓慢术 `Slow`（`b38bf386…`，锚目标） | ⚠ 待进游戏确认 |
| 缄默 | 2 | 禁魔 / 防护 | 解除魔法 `DispelMagicTarget`（`3eda0e7f…`，锚目标） | ⚠ 待进游戏确认 |

编译：`dotnet build -p:WrathPath="…"` **0 警告 0 错误**。

做法沿用单体批次既定方案：**给法术挂一发施法爆发**（`ApplyAbilityFx` 一处接线）——
`s.Range == Personal` 的锚**施法者**、其余锚**目标**（与祝福术一致）。三个法术都是默认距离（近距），
所以都锚在**目标**身上。

---

## 二、离线选材（`blueprints.zip`，未进游戏）

方法照 `docs/dev-notes.md` §7.11.1：**先离线扫全库 `AbilitySpawnFx`，再按语义挑**，不写死 AssetId
（借到的仍是运行时从原版蓝图读的 `PrefabLink`）。本次用的两个临时脚本：
`_tools/tmp/scan_spawnfx_kw.py`（按关键字列带 `AbilitySpawnFx` 的法术 + 锚点）、
`_tools/tmp/scan_paths_kw.py`（按路径关键字列候选并标注有无 FX）。

| 法术 | 元素 | 首选素材 | 锚点 | 选它的理由 | 备选 |
|---|---|---|---|---|---|
| 不稳定血浆 | `Heroism` | 英雄气概 `Heroism` | SelectedTarget | 同为"给一名友军的战力增益"（+2 攻击 / 豁免）；原版最经典的增益施法闪光 | `GoodHope`（3 环群体版，同款） |
| 反重力 | `SlowCast` | 缓慢术 `Slow` | SelectedTarget | 描述即"**如同缓慢术**"，语义 1:1 | 无（同类只此一个） |
| 缄默 | `Dispel` | 解除魔法 `DispelMagicTarget` | SelectedTarget | 同为**防护系、直接作用于魔法**；原版**没有"沉默术"** | `DispelMagicPoint`（同一份素材） |

**搜过但排除的坑**：

- **反重力的"缓慢"素材与迷梦共用**：离线核实 `Slow.jbp` 与 `HazeOfDreams.jbp` 的 `AbilitySpawnFx`
  **是同一个 AssetId（`b38bf386…`）**，而迷梦正是 `FxLibrary.Psychic` 的首选 → 所以反重力这一发在画面上
  会与**醉梦 / 牵制射击**同款。因语义完全对得上（"如同缓慢术"）故仍采用；**若要区分**，可把反重力改成走
  `SlowAura`（持续光环，见 `FxLibrary.DebuffAuraFor`，与"时之锁"同源但原色）。
- **缄默没有直接对应物**：全库搜 `silence / silenc / mute` 只找到 DLC6 假面魔像的专用 buff
  （`MaskGolemSilencedBuff`）与几件"沉默"武器（无 FX），**没有**可借的"沉默术"法术；退到解除魔法。
- **"重力 / 悬浮"类法术不存在**：搜 `gravity / levit / float` 只有战斗系统 / 过场（无 `AbilitySpawnFx`），
  所以反重力只能借"缓慢"这条语义线。

---

## 三、代码改动

| 文件 | 改动 |
|---|---|
| `src/FxLibrary.cs` | 新增三个元素属性：`Heroism` / `SlowCast` / `Dispel`（各带候选与语义注释，走 `FxBorrow.AbilityBurstFx`，不写死 AssetId） |
| `src/SpellsSetup.cs` | `ApplyAbilityFx` 的 `switch` 新增三个 case：`UnstablePlasma`→`Heroism`、`AntiGravity`→`SlowCast`、`Silencer`→`Dispel` |
| `docs/fx-status.md` | 3 个法术从"无特效"移到"已有特效"表；计数更新（单体有特效 10→13、无特效 19→16）；单体批次备忘补 2 环元素表与进度 |

改动只有"接线 + 素材表"两处，**不碰任何逻辑 / 数值**（三个法术的效果、持续、豁免均未动）。

---

## 四、待进游戏确认（作者实测清单）

1. **观感是否贴题**（三个素材都是"语义借"，实际画面未验）：
   - 不稳定血浆 = 英雄气概的金色增益闪光（是否偏"圣光"？血浆是死灵主题，若太亮可换 `FalseLife` 等死灵系素材）；
   - 反重力 = 与醉梦 / 牵制射击同款的闪光（**若嫌撞脸**，改走 `SlowAura` 持续光环）；
   - 缄默 = 解除魔法的防护系闪光（是否够"禁魔"的观感）。
2. **锚点**：三个都锚在目标身上（`ClickedTarget`），确认没有偏到施法者 / 地面。
3. **能否借到**：看 `Player.log` 里的 `特效借用成功 / 失败` 行——`FxBorrow` 借不到时**静默跳过**，
   "编译通过"不等于画面上有东西（见 fx-status 第二节的复核提醒）。
4. ⚠ **换 DLL 后必须重启游戏**（DLL 启动时加载）。

---

## 五、新发现（只记录，不改）

1. **反重力只打"缓慢"条件标记、没有缓慢的数值**：与 `dev-notes.md` §9（时之锁）**同源的老问题**——
   `AntiGravity` 的 Spec 只有 `Condition=UnitCondition.Slowed`、没有 `Bonuses`，所以目标只被打上"缓慢"
   状态标记，**没有**原版缓慢的 AC / 反射 / 攻击 −1 与"蹒跚"（那些在 `Spells/Level3/SlowBuff` 上）。
   本轮是**特效任务**，未动逻辑；若要和"如同缓慢术"完全对齐，可按时之锁的做法补 `SlowBuff` 的可感知组件。
2. **反重力的缓慢特效素材与迷梦共用**（见第二节）——这是原版素材复用，不是我们引入的。
3. `HazeOfDreams` / `Slow` / `DismissAreaEffect` / `DispelMagicPoint` / `DispelMagicTarget` 等多份原版法术
   共用少数几个 `AbilitySpawnFx` 素材；**按"元素"选素材时容易撞脸**，下一批（3 环起）挑素材前
   建议先查目标素材的 AssetId 是否已被别的元素占用（本轮已把这条写进 `SlowCast` 的注释）。

---

## 六、下一步

按环序继续单体批次：**3 环（处决）→ 4 环（梦魇 / 迅捷突破）→ ……**。
处决是 `Kind.Special`、`Range=Personal`、`SpecterOnly`（恶灵专用），按既定规则应锚**施法者**；
素材可考虑"武器 / 挥砍"类语义（离线扫 `AbilitySpawnFx` 后再定）。
