# 法术特效 · 单体批次「6–9 环」第二轮作者实测修订

> 日期：2026-10-10（深夜）　范围：按作者第二轮实测反馈，再修 6 个法术。
> 相关：`report-fx-6-9ring-revise.md`（第一轮）、`docs/fx-status.md`、`docs/dev-notes.md` §16/§17。
> 编译：`dotnet build -p:WrathPath="…"` **0 警告 0 错误**；已 `tools/deploy.sh` 部署（旧 DLL 备份 `RhodesDoctor.dll.bak-20261010-224451`）。
> ⚠ 部署时游戏在运行——**必须重启游戏**才会加载新 DLL。

---

## 一、作者反馈 → 结果

| # | 法术 | 反馈 | 结果 | 改法 |
|---|---|---|---|---|
| 1 | 涌潮悲歌 | 原特效挺好，只是颜色太浓；换细环反而不行 | ✅ 已改 | **素材回到原来的"天使罩子"**（`BarrierAura`），只把**颜色调淡**（`(0.62,0.84,1.30)`、Lift 0.60） |
| 2 | 黑蛇之焰 | 没问题 | — | 不动 |
| 3 | 镜中流形 | 依旧没修好 | ✅ 再修（第二轮） | 除"注册进场景"外，补 **`Position = 目标处`** + **`UnitPlaceOnGroundController.ForcedTick`**（见第三节） |
| 4 | 归溟 | 依旧只有"归" | ✅ **已查明原因** | **游戏中文字体没有"溟"这个字形**（静态图集）——改代码/JSON 都无效（见第四节） |
| 5 | 画中世界 | 你确定给敌人搞隐身了吗？ | ✅ 已加回 | 上一版为排查抽搐撤过"隐形"，现按设计**加回**（`AddCondition(Invisible)`），麻痹仍用规范 `BuffStatusCondition` |
| 6 | 影霄·拔刀 | 把刀的模型放大几倍 | ✅ 已改 | 剑长 `FitLongestMeters` 2 m → **6 m** |
| 7 | 旧日的战术家 | 就没看到特效 | ✅ 已改 | 原来**不在恶灵形态时法术空放**（什么都不给 → 没增益、没光环）；现改为**无形态时两套一起给** |

---

## 二、逐条说明

### 1. 涌潮悲歌 → 回罩子 + 调淡
上一版把它换成"天使光环"（细环），作者说反而不行。现**回到原来的"天使罩子"**（`FxLibrary.BarrierAura`），
只把**颜色往白里推、Lift 调高**（`(0.62,0.84,1.30)` / Lift 0.60）来降低"浓度"。
要再淡/再浓：改 `TintedAuraFx.Entries` 里 `Guids.Buff(36)` 那条的 `Tint` / `Lift`。

### 6. 影霄·拔刀 → 剑放大
`FitLongestMeters` 由 2 m 改为 **6 m**（约放大 3 倍）。要再大就继续调这个数（`SpellsSetup` 的 `ShadowBladeDraw` 分支）。

### 7. 旧日的战术家 → 无形态也生效
原逻辑：在**体质形态**补敏捷套件、在**敏捷形态**补体质套件、**不在任何形态时什么都不给**。
作者"就没看到特效"就是因为**没开形态 → 一个增益都没挂 → 自然没有光环**。
现改为：**无形态时两套一起给**（设计原文"同时获得敏捷与体质形态的全部收益"；转移量来自智力调整值，与形态无关）。
⇒ 任何情况下都会挂上增益，也就一定会有**蓝色光环**。

### 5. 画中世界 → 加回隐形
上一版为排查"抽搐"把隐形撤了；作者追问后**加回**（设计要"收进画境"）。
现在这条减益 = **规范 `BuffStatusCondition(Paralyzed)`**（修抽搐）+ **`AddCondition(Invisible)`**（收进画境）。
⚠ 若进游戏**抽搐又出现**，说明是"隐形"导致的渲染问题，那就二选一（要隐形就忍抽搐 / 要平稳就撤隐形）。

---

## 三、镜中流形：第二轮修复（反编译确认）

上一轮补了 `EntityCreator.AddEntity`（注册进场景），作者反馈"依旧没修好"。再查反编译，发现还有两处：

1. **位置**：`UnitHelper.Copy` 内部走 `Game.Instance.CreateUnitVacuum`，造出的**真空单位默认在原点**；
   而 `CreateView()` 是**按单位当前 `Position`** 摆视图的 → 视图建在了世界原点（可能在地图外/地形里）。
   → 补：`mirror.Position = target.Position`（并同步 `SpawnPosition` 与视图 `transform.position`）。
2. **贴地**：原版 `EntityCreationController.SpawnEntityWithView` 注册后会调
   `UnitPlaceOnGroundController.ForcedTick(unit)` 把单位贴到地面。我们的路径没有这一步。
   → 补：`UnitPlaceOnGroundController.ForcedTick(mirror)`。

现流程：`Copy` → **摆位** → 血量 → **`AddEntity` 注册** → **`ForcedTick` 贴地** → 行为锁 → `RuleSummonUnit`。
并且动作**每一步都打日志**（`[RhodesDoctor] 镜像复制体…`）——若仍不成，请把这几行日志发我。

---

## 四、归溟只显示"归"：**已查明是字体缺字**（不是代码问题）

**结论**：游戏的中文字体是 **TextMeshPro 静态 FontAsset**（`Bundles/ui_china_font`，预烘焙字符图集），
字符表**只含游戏本地化用到的字**，没有动态补字。

**证据**（可复跑 `python _tools/tmp/check_font_glyphs.py`）：
- 用 `UnityPy` 读 `Bundles/ui_china_font` 的字体字符表（那个 203664 字节的 MonoBehaviour）：
  `汐`(U+6C50)、`稔`(U+7A14) **在**；**`溟`(U+6E9F)、`黍`(U+9ECD) 不在**。
- 游戏 `Wrath_Data\StreamingAssets\Localization\zhCN.json` 里 **`溟` 出现 0 次**；
  而本模组里**只有 `溟`、`黍` 两个字**是游戏本地化从没用过的。

⇒ **`归溟` 的"溟"渲染不出来**，跟代码/JSON 无关（JSON 里是对的）。**要显示完整只能换字**。
可选替代（都需先在字体字符表里确认）：`汐`/`海`/`渊`/`潮`/`澜`…——请作者定一个，我改（法术名 + 增益名 + 描述里的"溟"一并改）。
另外描述里的"——黍"（`岁稔` 的作者署名）也会缺字，同样处理。

---

## 五、代码改动

| 文件 | 改动 |
|---|---|
| `src/TintedAuraFx.cs` | 涌潮悲歌：素材回到 `BarrierAura`、颜色调淡 |
| `src/SpellsSetup.cs` | 影霄·拔刀 `FitLongestMeters` 2 → 6；画中世界加回 `AddCondition(Invisible)`；旧日的战术家"无形态 → 两套一起给" |
| `src/Actions.cs` | 镜像复制体：补 `Position`/`SpawnPosition`/视图摆位 + `UnitPlaceOnGroundController.ForcedTick`；全流程日志 |
| `docs/fx-status.md` | 各对应行 + 表头批次注 + 3.2 镜中流形行 |
| `docs/dev-notes.md` | §16 补"摆位 / 贴地"；**新增 §17**（字体静态图集缺字，含自查脚本） |
| `_tools/tmp/check_font_glyphs.py` | 新增：比对"模组用字 vs 游戏 zhcn 用字"，并在字体包里核对字形 |

> 未新增 `L10n.S` 键，`LocalizedStrings.json` 无需改动。

---

## 六、待进游戏确认

1. **涌潮悲歌**：回到罩子、颜色应比第一版**淡**。
2. **镜中流形**：应能**招出复制体**（且**没有**特效）；不成请发 `Player.log` 里 `镜像复制体…` 几行。
3. **归溟**：名字若仍显示"归"——**这是字体缺字，需换字**（见第四节），请定替代字。
4. **画中世界**：敌人应**看不见（隐形）且不抽搐**；若抽搐回归，说明是隐形引起的，请告知。
5. **影霄·拔刀**：红剑应**明显变大**（约 3 倍）。
6. **旧日的战术家**：**不开形态**也应有蓝色光环 + 两套增益。
7. ⚠ **换 DLL 后必须重启游戏**。
