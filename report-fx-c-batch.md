# 法术特效 · C 类（持续地面区域）批次报告

> 日期：2026-10-09　范围：把群体法术里 **C 类"持续地面区域"** 剩余的 3 个补齐（钙质化已于本轮前完成）。
> 相关文档：`docs/fx-status.md`（现状清单，本轮已更新）、`docs/fx-plan.md`、`docs/fx-recolor.md`、
> `docs/dev-notes.md` §7.11（C 类完整配方）、**§7.11.25（本轮新增：C 类多素材门控）**。

---

## 一、本轮结论

C 类 4 个法术**全部接线完成**：

| 法术 | 环 | 区域半径 | 状态 |
|---|---|---|---|
| 钙质化 | 3 | 20 尺 | ✅ 已进游戏确认（本轮之前） |
| 冰封领域 | 5 | ~~30~~ → **20 尺** | ✅ 观感通过（10-09）；半径改 20，待复测 |
| 儿时的舞乐 | 6 | ~~40~~ → **20 尺** | ✅ 观感通过（10-09）；半径改 20，待复测 |
| 天灾降临 | 10 | 80 尺 | ⚠ **已接线，待进游戏确认** |

编译：`dotnet build` **0 警告 0 错误**（沙箱内用 `_build/` 中间目录验证，未部署）。

---

## 二、离线选材（`blueprints.zip`，未进游戏）

方法照 `dev-notes.md` §7.11.1 四条筛：`Shape=Cylinder` + `Size≈法术半径` + `Fx` 非 null + **语义对得上**。
全库 `Cylinder + Fx` 的区域共 **252 个**，逐个看语义后选出：

| 法术 | 素材（区域蓝图 ref） | Cylinder Size | scale | 选它的理由 |
|---|---|---|---|---|
| 冰封领域 | `AuraofColdAreaEffect`（寒冷灵光） | 30 | **0.667**（30→20） | 其 `Fx`（`5413cb18…`）**同时被 `DLC5_HillGiantBoss_FrostBarrier_AreaEffect`（霜障）引用** → 离线反查确认是冰霜素材。备选 `PolarMidnightArea`、`AuraofColdAreaSithhudEffect`（同为 Size=30） |
| 儿时的舞乐 | `MindFogArea`（心灵迷雾） | 20 | **1**（免缩放） | 40 尺的 Cylinder 里**没有**精神系（只有纠缠/冰暴/火山/根雾），退回 20 尺精神雾；**法术半径已改 20，正好免缩放**。其 `Fx`（`597682ef…`）同时被 `CharmFogArea`（魅惑之雾）引用。备选 `TricksterHallucinogenicCloudArea` |
| 天灾降临 | `VolcanicStormArea`（火山风暴） | 40 | 2 | 全库**没有 `Size=80`** 的 Cylinder（尺寸到 60 就断），取 40 尺复合灾害 × 2 → 80 尺。火山风暴 = 火 + 轰击，贴合"12d6 火焰 + 12d6 轰击 + 12d6 电击" |

**反查排除掉的坑**：`HauntingMistsAreaEffect`（萦绕雾）与**恶臭云（烟幕掩护用的绿毒雾）是同一份 Fx**（`ee38b41b…`），
若拿它做"精神"会与烟幕撞脸，已排除。

---

## 三、代码改动

| 文件 | 改动 |
|---|---|
| `src/FxLibrary.cs` | 新增三个素材属性：`FrozenDomainGround` / `FoxfireGround` / `CatastropheGround`（各带候选与语义注释，走 `FxBorrow.AreaFx`，不写死 AssetId） |
| `src/SpellsSetup.cs` | 三个法术的 `ApplySpecial` 分支各加一行 `AreaGroundFx.Register(...)`；钙质化的登记补 `calcProfile: true` |
| `src/AreaGroundFx.cs` | **新增 `Spec.CalcProfile` 开关**：把"钙质化专用"的部件手术（关 hole/fountain/puddle/cycle、销毁 groundfire 外扩环、把 wave 云铺满范围、压 Decals/Projector 贴花 Alpha）用开关门控，默认关闭；新增 `EnableHierarchyScaling`（通用档：把粒子设成 Hierarchy 缩放模式，让 `scale` 生效） |
| `docs/fx-status.md` | 3 个法术从"完全没特效"移到"已有特效"表；更新汇总计数（无特效 8→5）；补 C 类批次备忘 |
| `docs/dev-notes.md` | 新增 **§7.11.25**：为什么部件手术必须按素材门控（附给下一个 C 类素材的流程） |

### 为什么必须加 `CalcProfile` 开关

钙质化那段部件手术**是对着"黑曜流碎壳"这一份素材实测调出来的**，原来**无条件执行**——任何登记的区域都会跑一遍。
新素材会被误伤，最狠的是 `DestroyNonParticleRenderers`（**无差别销毁全部非粒子渲染器**，会把新素材自己的网格/贴花删掉）。

- **钙质化**：`calcProfile: true` → 行为与之前**完全一致**；
- **其余素材**：默认 `false` → 只染色 + 缩放（缩放靠 `Hierarchy` 模式生效）。

---

## 四、待进游戏确认（作者实测清单）

1. **各素材"原样长什么样"**：三个新素材都只做了"染色 + 缩放"，没动任何部件。
   进游戏看 `Player.log` 的 `[RhodesDoctor] 地面特效粒子清单` / `非粒子渲染器`（`LogInventory` 打的），
   对照画面确认：**有没有多余的部件要关/销毁、要不要把某层铺满范围**（照钙质化的 §7.11.11 排除法，别猜名字）。
2. **颜色**（离线初值，进游戏看观感再调，改 `SpellsSetup` 各自 `Register` 的颜色参数）：
   - 冰封领域 = 冰蓝白 `(0.82, 0.92, 1.00)`；
   - 儿时的舞乐 = 柔暖淡紫 `(0.96, 0.86, 0.98)`；
   - 天灾降临 = 炽热橙红 `(1.00, 0.62, 0.34)`。
   三个 C 类要**能一眼分开**（钙质化冷白 / 烟幕冷灰白 / 冰封冰蓝 / 儿时淡紫 / 天灾橙红）。
3. **尺寸**：冰封领域（素材 30 → 法术 20，`scale=0.667`）、儿时舞乐（素材 20 = 法术 20，免缩放）、
   天灾降临（素材 40 → 法术 80，`scale=2`）。进游戏看有没有铺满作用圈（若偏大/偏小，调 `Register` 的 `scale`）。

> **追加（10-09 晚，作者定）**：冰封领域半径 **30 → 20**、儿时的舞乐 **40 → 20**。
> 已同步：spec `AoERadius`、区域 `SetSize`、特效 `scale`、代码内嵌描述（中英）、
> `LocalizedStrings.json`（英文描述）、设计文档 `rhodes-doctor-archetype.txt` 与 `rhodes-doctor-en.txt`。
4. ⚠ **换 DLL 后必须重启游戏**（DLL 启动时加载），否则截图反映的是上一版——钙质化为此吃过亏（§7.11.11）。

---

## 五、下一步

群体法术剩余缺口：**E 类（敌方减益 4 个：时之锁 / 牵引 / 连锁殉爆 / 王冠的回响）+ F 类（影之疆域 1 个）**。
⚠ E 类的 `Force` 候选（魔法飞弹 / 轰击术）实测**不可用**，动手前按 §7.11.1 重选素材（同 C 类的筛法，但挂法不同：E 类挂"减益增益的 `FxOnStart`"）。
