# 第六轮回报（report-round6.md）

- 执行者：本机 AI
- 依据：`rhodes-doctor-local-ai-handoff-round6.md`（冲突以本文件为准）；设计文档指 `docs/rhodes-doctor-archetype.txt` 与 `docs/rhodes-doctor-en.txt`。
- 范围：只改文字、内嵌原文和注释，不改任何代码效果。编译：`dotnet build -p:WrathPath="D:\steam\steamapps\common\Pathfinder Second Adventure"` 通过，0 警告 0 错误。
- 第 5 节 X 项未动；本文件没提到的东西未改（除 T51 允许的按游戏内文字直接改，见单独栏 1）。
- 行号按改后版内容定位。

## 主表

| 编号 | 结果 | 改动位置 | 改后原文 | 备注 |
|---|---|---|---|---|
| G15 | 已改 | 中文设计文档第 206–207 行；`docs/rhodes-doctor-en.txt` 醉梦段；`LocalizedStrings.json`（`spell.DrunkenDream.desc` 中英文）；`SpellsSetup.cs`（`DrunkenDream` Spec 内嵌中英文） | 中文设计改后第 207 行：`失败敌人陷入魔法睡眠，不受生命骰限制。受到伤害后醒来，但恍惚 1 轮。对睡眠免疫的目标：若其免疫挡下整个法术（如精灵），则完全不受影响；若只免疫睡眠效果，则不会沉眠，但仍恍惚 1 轮。`；英文设计改后：`Sleep-immune targets: if the immunity stops the whole spell (e.g. elves), they are unaffected; if it stops only the sleep effect, they do not sleep but are still dazed for 1 round.`；JSON 改后 zh：`学派：惑控（睡眠、影响心灵）；标准动作；中距；半径 20 尺爆发；1 轮/级；意志无效。失败敌人魔法沉眠（不受 HD 限制），被打醒后恍惚 1 轮；对睡眠免疫的目标：若其免疫挡下整个法术（如精灵）则完全不受影响，若只免疫睡眠效果则不沉眠但仍恍惚 1 轮。——令`；JSON 改后 en：`Enchantment (sleep, mind-affecting); standard; medium; 20-ft. burst; 1 round/level; Will negates. Failures fall into magical slumber (no HD limit) and are dazed 1 round when woken; sleep-immune targets: if the immunity stops the whole spell (e.g. elves) they are unaffected, if it stops only the sleep effect they do not sleep but are still dazed 1 round. (Ling)`；代码内嵌与 JSON 逐字一致 | 代码效果未动（`Descriptor Sleep\|MindAffecting`、`DrunkenDreamWake` 未动）。证据措辞更正见本表备注栏下：第五轮 `E9-SpellDescriptorImmunity.txt` 引文外的总结句“睡眠免疫者恍惚照常”说宽了，只对“只挡睡眠效果”的目标成立；对精灵这类挡下整个法术的目标完全不受影响。证据文件本身未改 |
| T48 | 已改 | `LocalizedStrings.json`（`spell.Flashbang.desc` 英文）；`SpellsSetup.cs`（`Flashbang` Spec 内嵌英文） | 改后 en（两处逐字一致）：`Evocation (light); standard; a 15-ft. burst centered on a point within medium range; Reflex partial. Failure: blinded 1 round then dazzled; success: dazzled 1 round. Invisible creatures that fail become visible for 1 round. (Ash)` | 中文设计第 172 行、en.txt 闪光弹段、JSON 中文、代码内嵌中文核对一致，未动。代码效果未动（`ContextActionRemoveInvisibility` 本来就有） |
| T49 | 已改 | `SpellsSetup.cs` 三处 Spec 内嵌（醉梦英文随 G15、圣域英文、王者冲锋中英文），改成和 `LocalizedStrings.json` 逐字一致 | 醉梦内嵌 en = G15 行 JSON en；圣域内嵌 en 改后：`Abjuration; swift; close; all allies in a 30-ft. burst centered on a point within close range; 3 rounds. +4 sacred on saves vs spells and spell-like abilities; resistance 10 to the five energies, stacking with other sources. (Nightingale)`（补 `and spell-like abilities`）；王者冲锋内嵌 zh 改后：`【恶灵】学派：变化；标准动作；个人。沿直线冲锋至多两倍速度并攻击一次；路径上所有敌人受 4d6 轰击（反射成功仅免倒地，不减伤）。——推进之王`；王者冲锋内嵌 en 改后：`[Specter] Transmutation; standard; personal. Charge up to double speed in a line with one attack; every enemy in the path takes 4d6 bludgeoning (a successful Reflex only avoids being knocked prone, not the damage). (Siege)` | 中文设计圣域/冲锋段、en.txt 对应段、JSON 中英文核对效果一致，未动。醉梦中文内嵌同时随 G15 改（见 G15 行）。代码效果未动 |
| T50 | 已知，转实测 | 无改动 | — | 王者冲锋文字“个人” vs 代码 `Range=Medium`（Spec `RoyalCharge Range=Medium`，直线 60 尺），本轮不改，并入 X5 实测看提示框顶栏显示 |
| T51 | 已改（含作者追认 5 项） | `SpellsSetup.cs` 10 处 Spec 内嵌 + `LocalizedStrings.json` 2 处 + 设计文档 2 处（文明/王冠），见单独栏 1 | 见单独栏 1 | 全搜方法：正则提取全部 `L10n.S("字面键", zh, en)` 与 `LocalizedStrings.json` 按 `rhodesdoctor.+key` 比对中英文。中文仅表头简称差异（约40处，效果同义）不计，未改 |

## 单独栏 1：T51 搜索结果

方法：`src/*.cs` 内全部 `L10n.S`（含 Spec、增益、引爆能力）与 `LocalizedStrings.json` 同键比对。英文不一致 14 处起步，其中 T49 的 3 处（醉梦/圣域/王者冲锋）本轮已改、闪光弹英文两处同步加句后一致，剩余 5 处英文不一致如下。中文仅表头简称差异（45 处，效果同义，如战术标记/医疗敷料等 code 含学派头、JSON 为短述）不计为效果不一致，未改。

| 法术 | 代码内嵌 vs 游戏内文字 | 处理 |
|---|---|---|
| 战场测绘 | code zh 缺“距离/目标/持续时间”头、`+2 insight on Perception` 缺 `checks`；JSON 与中文设计第 74–75 行一致 | 已改：内嵌中英文改成和 JSON 逐字一致 |
| 牵制射击 | code zh 为缩写、缺 `——克洛丝`；code en 缺 ` (Kroos)`；JSON 与设计第 80–81 行一致 | 已改：内嵌中英文改成和 JSON 逐字一致 |
| 紧急撤离 | code 缺实现注“（实现为永远落在施法者身后最近空地）/`(implemented as the nearest open square behind the caster)`”；JSON 与设计第 83–84 行一致 | 已改：内嵌中英文改成和 JSON 逐字一致 |
| 铸铁 | code 缺“近距”/“常规”/`close`/`standard DR that does not stack...`；JSON 与设计第 184–185 行一致 | 已改：内嵌中英文改成和 JSON 逐字一致 |
| 潮汐守望 | code en 缺 `, not stacking`（中文两处均为“刷新不叠加”，一致）；JSON en 更全 | 已改：内嵌 en 补 `, not stacking`，与 JSON 逐字一致，中文未动 |
| 旗帜不倒 | code 缺“该效果不阻止即死效果。/`Does not stop death effects.`”；JSON 与设计第 260 行一致 | 已改：内嵌中英文改成和 JSON 逐字一致 |
| 灼烧 | code en 多 `to enemies in the line`（与 en.txt 一致），JSON en 无该短语 | 已改（作者决定：删code按JSON）：内嵌 en 删 `to enemies in the line`，与 JSON 逐字一致，中文未动 |
| 牵引 | code 有 `——歌蕾蒂娅`/` (Gladiia)` 与“不能送入危险地形”，JSON 无后缀与后半句；设计第 162–163 行有后半句与干员名 | 已改（作者决定：补JSON）：JSON zh/en 按 code/design 补后缀与后半句，与 code 逐字一致 |
| 文明的存续 | code/JSON 在免死句式不同，且 JSON 多 `不清除剧情诅咒。/Does not remove story curses.`（原设计第 291–292 行无该句） | 已改（作者决定：按JSON补齐）：code 中英文按 JSON 改；中文设计第 292 行与 en.txt 文明段补剧情诅咒句 |
| 王冠的回响 | code/JSON 在“可驱散增益 vs 可驱散的法术增益”、“恍惚 1 轮 vs dazed”、“不过驱散检定/只取敌方/按原持续生效”取舍不同 | 已改（作者决定：按JSON统一）：code 中英文按 JSON 改；中文设计第 298 行与 en.txt 王冠段补“法术/不过检定/只取敌方”，保留“按原持续/区域内友军” |

非Instruction：`ArchetypeSetup.cs:294` 圣疗（`rhodesdoctor.loh.desc`）见单独栏 2 第 1 条（作者决定按code改JSON，已改）。

## 单独栏 2：新发现（只记录，不改）

1. 圣疗内嵌与游戏内文字不一致（非Instruction，T51 搜出但不属法术）：已改（作者决定按code改JSON）：JSON（`rhodesdoctor.loh.desc`）中英文按 `ArchetypeSetup.cs:294` 内嵌缩写改，改后 zh `每日次数与伤害不死生物的豁免 DC 改用智力调整值，其余同原版圣疗。`、en `Uses per day and the DC against undead use the Intelligence modifier; otherwise as the paladin ability.`。注：游戏内显示将不再含“半级 + 智力”公式，为作者明确选择。
2. 本轮未发现设计文档自相矛盾（原 4 处需作者决定已由作者追认解决，见单独栏 1）。
3. `t51_raw.txt`/`t51_en.txt` 为本轮比对中间文件，已删除，未打包。

## X 项

- X1、X2、X4–X16 保持不变（X3 已关闭）。X5 新增一项：王者冲锋提示框顶栏的距离显示“个人”还是“中距”（文字个人 vs 代码 `AbilityRange.Medium`），等作者实测。
