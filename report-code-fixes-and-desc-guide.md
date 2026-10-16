# 四项落实 + 官方描述规范研究

> 日期：2026-10-11　来源：作者四条 —— ①补代码距离；②天灾降临改整轮；③旗帜不倒改轮/级；④0 环施法时间；
> 以及"描述不专业不清晰，去研究官方法术/能力/特性，总结一个描述内在逻辑"。
> 编译：`dotnet build` **0 警告 0 错误**；已部署（备份 `RhodesDoctor.dll.bak-20261011-193724`，版本 0.5.8 → **0.5.9**）。
> ⚠ 部署后**必须重启游戏**。

---

## 一、先更正我上一轮的两处误判

| 上一轮我说的 | 事实 |
|---|---|
| "天灾降临没设施法时间（默认标准动作）" | ❌ **错了**。`SpellsSetup` 里已有 `if (s.Key is "Catastrophe") ability.SetIsFullRoundAction(true);`（注释"设计文档第 300 行"）—— 它**本来就是整轮动作**，只是我上轮的比对脚本只看了 `Cast=` 字段、没看 `IsFullRoundAction`。 |
| "旗帜不倒 `Rounds = 5`，实际只持续 5 轮" | ❌ **错了**。它的效果用的是 `ContextDuration.Variable(ContextValues.Rank(), Rounds)` = **按等级**的轮数 ⇒ 实际就是 **1 轮/级**。`Rounds=5` 在那条分支里**根本没参与**（我给的那份数据表误读了）。 |

⇒ 这两项**不用改**，只做了数据清理（见下）。

---

## 二、实际改了什么（代码）

| # | 内容 | 位置 |
|---|---|---|
| ① | **补固定距离**：新增 `Spec.RangeFeet`，`>0` 时自动 `Range=Custom` + `SetCustomRange(RangeFeet)` —— 战术标记 **60 尺**、信标投射 **30 尺**、牵制射击 **30 尺**（王者冲锋的 40 尺也一并挪到这条通用路径，删掉原来的硬编码） | `SpellsSetup.cs` |
| ② | **天灾降临**：无需改动（已是整轮动作），仅在报告/规范里更正结论 | —— |
| ③ | **旗帜不倒**：把不参与计算的 `Rounds=5` 换成 `PerLevel=true`（与"实际按等级"一致），加注释说明时长由法术内部 `ContextDuration.Variable(Rank)` 决定 | `SpellsSetup.cs` |
| ④ | **0 环整组施法时间显式化**：6 条 0 环规格补 `Cast=UnitCommand.CommandType.Standard`（此前靠默认值，数据表上看不出是"标准动作"） | `SpellsSetup.cs` |

**为什么能用 `AbilityRange.Custom`**（反编译 `BlueprintAbility` 确认）：

```csharp
case AbilityRange.Custom: return CustomRange;   // Custom 距离 = CustomRange 字段，单位 Feet
```

BlueprintCore 侧是 `SetRange(AbilityRange.Custom)` + `SetCustomRange(Feet)`，两者配套即可。

---

## 三、官方描述研究：《描述规范：游戏文本的"内在逻辑"》

产出 **`docs/description-style-guide.md`**。素材来自游戏自带官方中文（`StreamingAssets/Localization/zhCN.json`，21 MB）
与官方蓝图，抽样了 10 个不同类型的法术 + 7 个职业特性/专长。

### 3.1 核心结论（一句话）

> **官方描述 = 头区（UI 用蓝图字段自动生成）+ 正文（只写效果）+ 术语用 `{g|…}` 链接到百科。**
> 正文里**不写**学派 / 施法时间 / 距离 / 目标 / 持续时间 / 豁免 / 法术抗力 —— 那是头区的事。

反编译 `TooltipTemplateAbility` 证实：头区由 `Blueprint.School / Range / ActionType /
LocalizedDuration / LocalizedSavingThrow / SpellResistance / SpellDescriptor` **自动拼**出来，
正文只取 `Blueprint.Description`。官方火球术的正文全文**一个状态栏字段都没有**。

### 3.2 官方的三类标记（我们一处都没用）

全库统计：`{g|Encyclopedia:…}` **32963 处**、`{g|Spells…}` 1542 处、`{g|Condition…}` 1291 处、`{n}` 换行 18515 处。
规范文档里附了**百科键 ↔ 中文词条对照表**（60 个常用键）和 **Condition 键表**，可直接查用。

### 3.3 我们 mod 的 9 类问题（已列证据）

风格不统一（27/67 带手写状态栏）· 与头区重复且会过期 · 0 处术语链接 · 实现细节泄漏
（"（实现为永远落在施法者身后最近空地）"）· 作者署名（"——克洛丝"）· 术语非官方
（**罚值→减值**、**抗力→抗性**、**轰击→钝击**、定身）· 自定义前缀（`【恶灵】`/`【10 环】`）· 句式不统一 · 状态名自造。

### 3.4 规范里给了 3 条前后对照改写示例

战术标记（补链接）· 坚守阵地（删状态栏与署名、改语序）· 紧急撤离（删实现细节）。

---

## 四、下一步（等你点头）

规范文档第 7 节给了落地步骤，建议：

1. 先按规范**只改一条**（战术标记）→ 进游戏确认 `{g|}` 链接可点、不报错；
2. 确认后用脚本**批量校验**（扫 `LocalizedStrings.json` 里"含状态栏字段 / 含非官方术语 / 含实现细节 / `{g|` 计数为 0"的条目）；
3. 分批改写：先 64 条法术，再特性与能力。

说一声我就按这个顺序往下做。
