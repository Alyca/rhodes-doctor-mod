# 法术之外的**能力 / 特效**核查报告

> ⚠ **2026-10-11 已按作者要求动手修了一部分，本报告作为"体检底稿"保留**：
> ① 20 级 10 环指令**改走正常升级界面**（§一 #12 那条缺口已补，另见 `report-10ring-specter-fx-icons.md`）；
> ② **恶灵六形态已加黑烟常驻光环**（§一 #7 从"❌ 无"变"✅ 已接线"）；
> ③ **缺图标已补齐**（§四 ③，另加圣疗 / 引导正能量 / 恶灵三阶段）；四个**仍然没有特效**的项见 §五。
> 下面正文的"现状"是 10-11 之前的体检结果，未逐行重写。

> 日期：2026-10-11　范围：64 个指令以外的全部内容 —— 变体本体特性、职业能力、巴别塔的恶灵三阶段。
> 问的是三件事：**① 有没有生效　② 有没有准确生效　③ 需不需要 / 有没有特效**。
> 核查方式：源码逐文件读 + `Player.log`（17:44 那次）+ `blueprints.zip` 离线核对 + API 转储对照。
> **本报告只核查，未改任何代码。**

---

## 一、结论速览

| # | 能力 | 逻辑生效 | 特效 | 备注 |
|---|---|---|---|---|
| 1 | 战术指令法术书（1 级授予） | ✅ 生效 | — | ⚠ 与 `SetReplaceSpellbook` **双来源**，见 §四① |
| 2 | **集火**（能力 + 标记） | ✅ 生效 | ❌ **无** | 专属图标已有（`status/focus-fire-mark.png` 已加载） |
| 3 | 源石技艺精通 1–4（穿透 +2/+4/+6/+8） | ✅ 生效 | ❌ 无 | 被动，不需要特效；⚠ **没有专属图标** |
| 4 | 冷静头脑（豁免 + 智力调整值，恶灵中减半） | ✅ 生效 | ❌ 无 | 被动，不需要特效；⚠ 没有专属图标 |
| 5 | **指挥灵光 / 全军指令**（10 / 30 尺） | ✅ 生效 | ❌ **无** | 结构已是原版三段式，`m_TargetType` 反射**已核实有效** |
| 6 | 圣疗 / 引导正能量（克隆原版） | ✅ 生效 | ✅ **自带原版特效** | 唯一有视觉的非法术能力 |
| 7 | **恶灵开启 / 敏捷形态 / 体质形态**（8/12/16 级各一对） | ✅ 生效 | ❌ **无** | 三段共 6 个开关 + 6 个形态增益，全无视觉 |
| 8 | 恶灵锁定（只能施放【恶灵】指令） | ✅ 生效 | — | `SpecterOnly` 6 个与规则文档第 10 版**完全一致** |
| 9 | 恶灵余波（2 轮只能移动 + 速度 +50） | ✅ 生效 | ❌ 无 | 用游戏原生"恶心"近似，**对恶心免疫者会跳过**（已记偏差） |
| 10 | 结束恶灵（自由动作） | ✅ 生效 | ❌ 无 | 可选 |
| 11 | 20 级"罗德岛的博士" | ✅ 生效 | ❌ 无 | 压制锁定/余波、保留 +50 速度，都已接线 |
| 12 | **10 环指令的解锁（20 级）** | ❌ **未接** | — | 唯一确定的"没生效"，见 §三 |

**一句话**：**能力全部落地**（注入层 6 个模块零失败），但**法术之外的能力一个特效都没有** —— 4 张特效表里没有任何一条指向集火 / 恶灵 / 灵光的 GUID。

---

## 二、证据

### 2.1 注入层：全部成功，没有静默失败

`%LOCALAPPDATA%Low\Owlcat Games\Pathfinder Wrath Of The Righteous\Player.log`（17:44）：

```
994  召唤物与镜像注入完成。
996  测试木桩注入完成。
1080 指令法术注入完成。
1094 法术书注入完成。
1099 巴别塔的恶灵注入完成。
1102 变体本体注入完成。        ← 变体/集火/灵光/圣疗全在这一步
1103 蓝图注入完成。
```

- **无任何 `注入失败`**（`Content.Run` 的 try/catch 没被触发过）；
- **`图标文件缺失` 计数 = 0**；`specter/aftermath|agile-form|enduring-form|doctor-capstone.png`、
  `status/focus-fire-mark.png`、`status/command-aura.png` 全部加载成功
  → 集火与灵光**有专属图标**，只有源石技艺精通 / 冷静头脑 没调 `SetIcon`（用默认图标）。

### 2.2 特效层：非法术部分 0 处接线

- `src/ArchetypeSetup.cs`、`src/SpecterSetup.cs` 里**没有任何** `AbilitySpawnFx` / `SetFxOnStart` /
  `FxLibrary` / `TintedAuraFx` / `AreaGroundFx` 调用（全库 grep 只命中 `SpellsSetup.cs` / `Actions.cs` / `SummonsSetup.cs`）；
- 四张特效表（`FxLibrary`、`TintedAuraFx.Entries`、`AreaGroundFx`、`HasteTrailFx`）里
  **没有一条** 指向 `LockBuff/V2/V3`、`AuraCommand*`、`TotalCommand*`、`FocusFire*`、
  `AgileBuff*/EnduringBuff*`、`Aftermath*`、`Capstone*`
  —— 唯一沾边的 `TacticianAgileBuff` / `TacticianEnduringBuff` 是**法术**"旧日的战术家"的套件，不是变体能力。

### 2.3 顺手解除了两个旧 VERIFY（都在非法术侧）

| 旧 VERIFY | 结论 | 依据 |
|---|---|---|
| `ContextRankConfigs.StatBonus` 给的是**调整值**还是**属性值**（`SpecterSetup` 转移量表的前提） | **调整值**（修正值） | 原版"添加某属性调整值"的标准写法就是 `ContextRankConfig{m_BaseValueType: StatBonus, m_Stat: <属性>}`：炼金/掷弹兵的炸弹伤害（+智力调整值）、军队破邪（+魅力调整值）都是这个组合；枚举里另有独立的 `BaseStat = 13` 专门给"属性值"。⇒ 转移量 ×2 的表写法正确 |
| 灵光区域 `m_TargetType` 反射写入是否有效（`ArchetypeSetup.SetAreaTargetType`） | **有效** | `blueprints.zip` 里 **638/638** 个 `BlueprintAbilityAreaEffect` 全部序列化 `m_TargetType`，取值 `Any`(438) / `Enemy`(111) / `Ally`(89) —— 字段名与取值都对得上；且日志显示变体本体注入成功 ⇒ `Enum.Parse(..., "Ally")` 没抛异常 |

---

## 三、唯一确定的"没生效"：20 级 10 环指令未授予

- **规则文档（修订第 11 版，实现备注第 5 条）**：10 环指令不依赖合书，**改为职业 20 级直接授予**。
- **代码现状**：`ArchetypeSetup.CreateArchetype` 的 20 级行只有 `CapstoneFeature`；
  `SpellbookSetup.Create()` 只建了列表 / 每天法位表 / 法术书 / 合书登记，**没有 `CreateTenRingFeature`**
  （注释里写着"已改由 `CreateTenRingFeature` 授予"，但**该函数不存在**）。
- **现状替代**：只能靠测试面板（Ctrl+F10）的「给我 10 环指令」按钮发放，见 `dev-notes.md` §10
  （那里明确记为"实验阶段从简，正式解锁方案待定"）。
- ⇒ 这一条**不是特效问题，是能力没接**。要接就是 dev-notes §10 表格里的四件套
  （`AddMechanicsFeature{EnableSpellLevel10}` + 4 × `AddKnownSpell{m_Archetype}` + 自建法位表 + 成长表 20 级行）。
  法位表那一格**已经做好了**（20 级 4 个 10 环法位），缺的是前两格。
  ⚠ 注意 §10 教训 4：成长表对**旧存档的 20 级角色不补发**。

---

## 四、低风险 / 待确认三条（不影响"能不能用"）

### ① 法术书有**两个来源**（可能重复给书）

| 位置 | 作用 |
|---|---|
| `ArchetypeSetup.CreateSpellcastingFeature` → `AddSpellbook`（特性，1 级授予） | 让博士 1 级就有书（圣武士本体到 4 级才进施法） |
| `ArchetypeSetup.CreateArchetype` → `SetReplaceSpellbook`（变体链） | 用博士法术书替换圣武士的法术书 |

`Guids.cs` 里把 `SpellcastingFeature` 注为"保留位（法术书改由 `SetReplaceSpellbook` 挂载）"，
但代码仍在用它 —— 文档与实现不一致（`report.md` 单独栏 3 第 3 条已记过）。
风险：4 级起"替换来的书"与"特性给的书"可能是**两个 `Spellbook` 实例指向同一蓝图** ⇒ 法位翻倍。
**验证只要 30 秒**：开一个 4 级以上博士，看法术书页的每日法位有没有变成术士表的两倍。
若重复，删掉 1 级那条 `AddSpellbook`（或改成"只在 <4 级生效"）即可。

### ② 集火换目标用的还是"旧扫法"

`Actions.ContextActionRemoveMyBuffs` 仍在用 `CurrentScene.AllEntityData ∪ Player.AllCrossSceneUnits`。
这里是**两家都扫**（敌人在区域状态、队友在跨场景状态），所以**功能是对的**；但 `dev-notes.md` §20 已经定案
"自扫一律用 `UnitScan.Take()`"，为一致性建议改过去（一行）。另保留旧提示：
它靠 `buff.Context?.MaybeCaster == caster` 找旧标记，玩家自己施放没问题。

### ③ 源石技艺精通 / 冷静头脑 没有专属图标

两个特性都建了蓝图但没调 `SetIcon` ⇒ 游戏里是默认空白图标。要的话把 `Mastery()` / `CreateCoolHead`
补上 `SetIcon(Icons.Status(...))`，再往 `icons/png-64/status/` 放两张图。

---

## 五、要不要给非法术能力做特效？（改动量都很小）

按"值不值得"排序，给出**具体落点**（都是现成机制，不用新写系统）：

| 优先 | 对象 | 建议做法 | 改动量 |
|---|---|---|---|
| ★★★ | **恶灵开启 / 三阶段形态** | ① 开启能力挂一发施法爆发（`AddAbilitySpawnFx`，锚施法者）；② 形态增益挂**常驻光环** —— `TintedAuraFx.Entries` 加 6 行（`AgileBuffV1/V2/V3` + `EnduringBuffV1/V2/V3` 指向同一素材，配色自选；素材用原版"常驻一片"的，如 AngelBarrier / Heroism） | 表里加 6 行 |
| ★★★ | **指挥灵光 / 全军指令** | 区域蓝图**本身就有 `Fx` 字段**（原版 638 个区域都用它）→ `AbilityAreaEffectConfigurator` 上设一下 Fx；或走现成的 `AreaGroundFx.Register(Guids.AuraCommandArea, ...)` 复刻"坚守阵地的圈" | 2 行 |
| ★★ | **集火标记** | 给 `FocusFireMarkBuff` 加一行 `TintedAuraFx.Entry`（`BuffGuid = Norm(Guids.FocusFireMarkBuff)`）——素材复用**已上屏验证过**的标记类（`FxLibrary.DebuffAuraFor` 那套：火之烙印 / 天界印记），语义就是"这个敌人被做记号了" | 表里加 1 行 |
| ★ | 恶灵余波 / 结束恶灵 | 余波限制增益挂个减益光环（可选）；结束恶灵加一发爆发（可选） | 各 1 行 |
| ☆ | 20 级"罗德岛的博士" | 可选：给 `CapstoneBuff` 挂一个金色光环，做"人灵合一"的观感 | 1 行 |
| — | 源石技艺精通 / 冷静头脑 / 圣疗 / 引导 | **不需要**（被动，或原版已自带特效） | — |

> 注意叠加问题：恶灵形态若挂常驻光环，会和玩家身上**已有**的 D 类法术光环（如"潮"/"铸铁"）叠在一起偏亮 ——
> 与 `fx-status.md` 第四节记的"多个 D 类增益叠光环"是同一类问题，可先挑一个不常见的色系。

---

## 六、复刻用的核对命令（下次直接跑）

```bash
# ① 模块注入结果 / 图标缺失
grep -n "注入完成\|注入失败\|图标文件缺失" \
  "$LOCALAPPDATA/Owlcat Games/Pathfinder Wrath Of The Righteous/Player.log"

# ② 非法术侧有没有接线特效（应为空）
grep -n "FxLibrary\|SetFxOnStart\|AbilitySpawnFx" src/ArchetypeSetup.cs src/SpecterSetup.cs

# ③ 特效表里有没有非法术 GUID（应为空）
grep -n "LockBuff\|AuraCommand\|TotalCommand\|FocusFire\|AgileBuff\|EnduringBuff\|Aftermath\|Capstone" \
  src/TintedAuraFx.cs src/HasteTrailFx.cs src/AreaGroundFx.cs src/FxLibrary.cs
```
