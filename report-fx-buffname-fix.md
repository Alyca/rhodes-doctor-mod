# 梦魇 buff 显示修复 + 全模组「buff 显示成法术名」排查

> 日期：2026-10-10（夜）　范围：作者反馈"改成一团乱码" + "还有好多都这样，列出来一起改"。
> 编译：**0 警告 0 错误**；已 `tools/deploy.sh` 部署（旧 DLL 备份 `RhodesDoctor.dll.bak-20261010-210447`），游戏目录里的 `LocalizedStrings.json` 已确认更新。

---

## 一、"乱码"的原因（已修）

截图里显示的是 **`[unknown key: rhodesdoctor.buff.nightterror.panic.name]`** —— 不是乱码，是**本地化键没落到 JSON 里**。

机制：`src/L10n.cs` 的 `L10n.S(key, zh, en)` **运行时只返回键字符串**（`"rhodesdoctor." + key`），
字符串本体在 **`LocalizedStrings.json`**（游戏原生本地化包，**手工维护**，没有生成脚本）。
我上一轮只改了代码、**没往 JSON 里加这 4 条键** → 游戏找不到 → 显示原始键。

已补齐 4 条：
| Key | zhCN | enGB |
|---|---|---|
| `rhodesdoctor.buff.nightterror.panic.name` | 惊惧 | Frightened |
| `rhodesdoctor.buff.nightterror.panic.desc` | 恐惧：必须逃离恐惧源；… | Fear: must flee… |
| `rhodesdoctor.buff.nightterror.shaken.name` | 动摇 | Shaken |
| `rhodesdoctor.buff.nightterror.shaken.desc` | 恐惧：… | Fear: … |

---

## 二、全模组排查：还有 23 个 buff 也"显示成法术名"

方法：扫 `src/SpellsSetup.cs` 里所有 `CreateExtraBuff(...)`，看第二个参数（显示名）是不是 `s.Name`（= 法术名）。
**共 23 个**（另有 20 个已经有专属状态名，如"处决（蓄势）""钙质化（凝滞）"，不用动）：

| # | 代码里的 buff | 之前显示 | 改为 |
|---|---|---|---|
| 1 | ChainDetonationMark | 连锁殉爆 | **连锁殉爆（标记）** |
| 2 | TacticianEnduring | 旧日的战术家 | **旧日的战术家（坚韧）** |
| 3 | TacticianAgile | 旧日的战术家 | **旧日的战术家（敏捷）** |
| 4 | FinalSnipeMark | 终末狙击 | **终末狙击（标记）** |
| 5 | EternalGaze | 永恒的注视 | **永恒的注视**（补键） |
| 6 | ReturnToAbyss | 归溟 | **归溟**（补键） |
| 7 | SwiftBreach | 迅捷突破 | **迅捷突破**（补键） |
| 8 | SmokescreenConceal | 烟幕掩护 | **烟幕掩护（隐蔽）** |
| 9 | BeaconReveal | 信标投射 | **信标投射（显形）** |
| 10 | HarvestYears | 岁稔 | **岁稔（积攒）** |
| 11 | ShadowRealmHolder | 影之疆域 | **影之疆域（领域）** |
| 12 | BlazingSunBlind | 耀阳颔首 | **耀阳颔首（目盲）** |
| 13 | BlazingSunDazzle | 耀阳颔首 | **耀阳颔首（目眩）** |
| 14 | DelayedBlastBomb | 延时爆破 | **延时爆破（炸弹）** |
| 15 | VolcanoEmber | 火山 | **火山（余焰）** |
| 16 | TwilightMelt | 黄昏 | **黄昏（熔火）** |
| 17 | FlashbangDazzle | 闪光弹 | **闪光弹（目眩）** |
| 18 | FlashbangBlind | 闪光弹 | **闪光弹（目盲）** |
| 19 | ScorchResist | 灼烧 | **灼烧（抗性降低）** |
| 20 | BlackSnakeBurn | 黑蛇之焰 | **黑蛇之焰（灼烧）** |
| 21 | FrozenEntangled | 冰封领域 | **冰封领域（定身）** |
| 22 | FrozenHalfSpeed | 冰封领域 | **冰封领域（减速）** |
| 23 | DrunkenDreamSleep | 醉梦 | **醉梦（沉眠）** |

> 5/6/7 三个本来就是"法术自身挂在身上的增益"，名字与法术同名，所以只是**补了键**、没加括号后缀。

---

## 三、改动清单

| 文件 | 改动 |
|---|---|
| `LocalizedStrings.json` | **+27 条**键（梦魇 4 条 + 上面 23 条；键总量 244 → 271） |
| `src/SpellsSetup.cs` | 23 处 `CreateExtraBuff(...)` 的显示名由 `s.Name` 改为 `L10n.S("spell.<法术>.<状态>.name", …)` |

**校验**：写脚本把 `src/*.cs` 里所有 `L10n.S("…")` 键与 `LocalizedStrings.json` 的 `Key` 逐条比对 ——
**代码 251 个键，JSON 全都有，缺失 0**（另有一条 `rhodesdoctor.字面键` 是注释里的示例文本，非真实键）。

---

## 四、轮数按你说的**不改**

"没必要这么同步轮数" —— 保持现状：**恐慌 2 轮，然后动摇 1 轮/级**（总时长比法术多 2 轮）。本轮未动。

---

## 五、附带

- 这些 buff 的**说明文字**仍复用**法术描述**（`s.Desc`），与已有的 20 个专属名 buff 一致；只有梦魇的"惊惧/动摇"给了**状态自己的说明**。若想把别的也换成状态说明，另说。
- 图标暂沿用各法术图标。
