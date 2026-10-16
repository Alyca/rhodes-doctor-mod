# 作者反馈批（10 项）— 闪光弹 / 墨灵 / 王者冲锋 / 黑蛇之焰 / 四个光环 / 大地之怒 / 重构·文明

> 日期：2026-10-11　范围：作者逐条反馈的 10 项（特效范围 / 召唤物 / 施法者移动 / 逐次触发 / 以施法者为中心 / 换特效）。
> 编译：`dotnet build -p:WrathPath="…"` **0 警告 0 错误**；已 `tools/deploy.sh` 部署（旧 DLL 备份 `RhodesDoctor.dll.bak-20261011-150115`）。
> ⚠ 部署后**必须重启游戏**（DLL 只在启动时加载）。

---

## 一、总表

| # | 法术 | 作者反馈 | 结果 |
|---|---|---|---|
| 1 | 闪光弹 | 范围只有 15 尺，特效太大 | ✅ 特效**缩小**（`ParticleSizeScale 0.45`） |
| 2 | 墨染山河 | 召唤物没有任何特性/专长，加一些让它符合强度 | ✅ 墨灵补 7 条**种族特性 + 战斗专长** |
| 3 | 王者冲锋 | 应该是施法者移动 | ✅ 去掉冲突的传送组件，改为**施法者传送到落点** |
| 4 | 黑蛇之焰 | 每造成一次伤害触发一次特效 | ✅ 持续灼烧的每一轮也放一发黑焰 |
| 5 | 岁稔 | 以施法者为中心，特效跟随、换一个 | ✅ `Personal` + 跟随光环（禾绿，自然之灵素材） |
| 6 | 旗帜不倒 | 以施法者为中心，会显示被免疫、被当 debuff | ✅ `Personal` + **去掉 `MindAffecting`** + 跟随光环（暖红金） |
| 7 | 潮汐守望 | 以施法者为中心 30 尺，特效持续在单位身上、换一个不重复的 | ✅ `Personal` + 跟随光环（青蓝水膜，海之披风素材） |
| 8 | 大地之怒 | 以施法者为中心 40 尺，特效非常不适配 | ✅ `Personal`、半径 **60 → 40**、特效换**地面岩刺** |
| 9 | 重构 / 文明的存续 | 以施法者为中心，没特效 | ✅ 两者 `Personal` + **去掉视觉**（重构去金光、文明去后续光环） |
| 10 | 永恒的注视 | 以施法者为中心，特效不好而且重复 | ✅ `Personal` + 跟随光环（银紫"注视"，邪眼素材） |

---

## 二、逐条说明

### 1. 闪光弹：缩小特效
- 法术半径 15 尺，但借的是原版**阳炎爆**（`Sunburst`）的爆发，画面比 15 尺大得多。
- `AbilitySpawnFx` 是引擎直接播的 `PrefabLink`、**改不了尺寸**，所以改走"点击目标时执行"的
  `ContextActionSpawnScaledFx`（`FxTint.SpawnPlaced` → **只缩小粒子本身**、不动根节点）。
- 调参：`SpellsSetup.ApplyAbilityFx` 里 Flashbang 分支的 **`ParticleSizeScale = 0.45f`**（想更小就再调小）。

### 2. 墨染山河：给墨灵补特性 / 专长
- 原来 `SetAddFacts` 传的是**空数组**（`NaturalWeapon` 为空）→ 墨灵没有任何种族特性与专长、很弱。
- 现补 `SummonsSetup.InklingFacts`（参照原版**小型气元素**被召单位逐条挑，去掉气元素专属）：
  **元素子类**（免疫流血/麻痹/毒素/睡眠/震慑、不受重击与夹击）、**异界子类**、**绊摔免疫**、
  **武器娴熟**（敏 19 > 力 18）、**精通先攻**、**精通徒手打击**、**天生护甲 +3**。
- 想加强/减弱就改这一张表（GUID 已在 `blueprints.zip` 核实）。

### 3. 王者冲锋：施法者移动到落点
- 原来同时挂 `AbilityCustomTeleportation`（传送）和 `AbilityDeliverProjectile`（直线投射物），
  **两者都是投递组件、同一能力只会跑一个** → 实测"施法者没动"（历史 X2/X7 老问题）。
- 现**去掉传送组件**，把"施法者传送到落点"改成挂在 `AdditionalAbilityEffectRunActionOnClickedTarget`
  上的普通动作（`Actions.cs` 新增 `ContextActionTeleportCasterToPoint`，用 `FreePlaceSelector` 找空地）。
- 直线投射照常选目标 / 结算 4d6 与倒地，两者互不干扰。

### 4. 黑蛇之焰：每造成一次伤害触发一次特效
- 原来只有**施法瞬间**那一发黑焰，之后每轮只掉血、没特效。
- 现 `ContextActionBlackSnakeBurn`（持续灼烧，每轮 3d6）在结算伤害后**在目标身上再放一发黑焰**
  （与施法瞬间同一素材 / 同一暗紫黑色）。
- ⚠ 已知偏差（未动）：规则写"每轮第一次造成伤害"，实现仍是**每轮一次灼烧**（与之前一致）。

### 5–7、10. 岁稔 / 旗帜不倒 / 潮汐守望 / 永恒的注视：施法者为中心 + 各自不同的跟随特效
四个原来都借 D 类的**天使屏障**（金色罩子）`SetFxOnStart`，**观感不跟随、四个还一模一样**。现：

| 法术 | 靶向 | 特效素材（原版 buff 的 `FxOnStart`） | 颜色 |
|---|---|---|---|
| 岁稔 | `Personal`（以你为中心 30 尺） | 萨满·自然之灵 `ShamanNatureSpiritBaseBuff` | **禾绿** |
| 旗帜不倒 | `Personal` | 阿萨塔·英雄决意之歌 `SongOfHeroicResolveBuff` | **暖红金** |
| 潮汐守望 | `Personal` | 8 环·海之披风 `SeamantleBuff` | **青蓝水膜** |
| 永恒的注视 | `Personal` | 萨满·邪眼 `ShamanHexEvilEyeACBuff` | **银紫** |

- 四者全部改走 `TintedAuraFx`（克隆后挂到单位 `View` 下 → **一定跟随**），**素材与颜色两两不同**。
- 旗帜不倒另**去掉 `SpellDescriptor.MindAffecting`**（有益法术挂"影响心灵"会被目标的免疫挡下 →
  跳"免疫"；与上一轮"乌拉"同一处病根），并补 `Friendly=true`。

### 8. 大地之怒：施法者为中心 40 尺 + 换特效
- `Range` 由中距改 **`Personal`**、`AoERadius` 由 60 改 **40**（以施法者为中心）。
- 特效：原来的落点物理爆发"非常不适配" → 换成**地面岩刺/碎石**的区域 FX
  （借原版"尖石术"区域 `SpikeStonesArea` 的常驻 Fx，Cylinder/**40** 与半径相等），锚**施法者**。
- 调参：`FxLibrary.EarthWrathGround`（候选可换 `StoneCallArea` 落石）。

### 9. 重构 / 文明的存续：以施法者为中心 + 去特效
- 两者 `Range` 改 **`Personal`**；重构的 `ContextActionMassRestore` 由 `CenterOnCaster=false` 改 **`true`**。
- **去掉视觉**：重构不再在每个被恢复者身上放金光（`GoldFlash=false`）；文明的存续的后续增益不再挂光环
  （从 `FxLibrary.AuraSpells` 移出）。于是 D 类光环名单**清空**（保留结构备用）。

---

## 三、代码改动

| 文件 | 改动 |
|---|---|
| `src/SpellsSetup.cs` | 闪光弹：`ApplyAbilityFx` 加"缩小"分支；大地之怒：`EarthFury` 换素材、Spec 改 `Personal`/半径 40；`RoyalCharge`：去传送组件 + 加落点传送动作；`BlackSnakeFlame`：灼烧动作带黑焰 FX；`Reconstruction`：`CenterOnCaster=true`/`GoldFlash=false`；`TidalVigil`/`HarvestYears`/`BannerStands`/`Civilization`/`EternalGaze`：Spec 改 `Personal`；`BannerStands`：Meta 去 `MindAffecting`、补 `Friendly` |
| `src/Actions.cs` | 新增 `ContextActionTeleportCasterToPoint`（施法者移动到落点）；`ContextActionBlackSnakeBurn` 加 FX 字段与生成 |
| `src/FxLibrary.cs` | 新增 `NatureAura`/`ValorAura`/`TideAura`/`GazeAura`/`EarthWrathGround`；`AuraSpells` 清空 |
| `src/TintedAuraFx.cs` | 新增 岁稔 / 旗帜不倒 / 潮汐守望 / 永恒的注视 四条跟随光环 |
| `src/SummonsSetup.cs` | 新增 `InklingFacts` + `BuildInklingFacts()`，墨灵补种族特性与专长 |
| `LocalizedStrings.json` | 同步 6 条描述（TidalVigil / HarvestYears / BannerStands / EarthFury / Reconstruction / EternalGaze：改"以你为中心 / 半径 40"） |

---

## 四、待进游戏确认（FX 都是初值，观感要你看）

1. **闪光弹**：爆发应比原来**明显小**、跟 15 尺范围相称。
2. **墨灵**：应免疫流血/麻痹/毒素/睡眠/震慑、不受重击与夹击、AC 更高；攻击用敏。
3. **王者冲锋**：施法后**施法者真的移动到落点**，直线上的敌人照常受 4d6 与倒地。
4. **黑蛇之焰**：施法瞬间一发、之后**每轮灼烧也各一发**黑焰。
5. **岁稔 / 旗帜不倒 / 潮汐守望 / 永恒的注视**：光环应**跟着单位走**，且四个**彼此不同**；
   颜色/素材不合意可改 `TintedAuraFx` 里那四条、或 `FxLibrary` 里对应的候选。
6. **大地之怒**：应以**你**为中心、半径 40 尺的地面岩刺；岩刺观感不合意可换 `FxLibrary.EarthWrathGround` 的候选。
7. **重构 / 文明的存续**：应只影响**以你为中心 30 尺内**的友军，且**没有特效**。
8. **旗帜不倒**：不再跳"免疫"。
9. ⚠ **换 DLL 后必须重启游戏**。

---

## 五、已知风险 / 需要你定的点

- **重构 / 文明的存续"没特效"**：按你的回答"我当时看没特效"理解成**不要特效**（重构去金光、文明去光环）。
  若其实想要一个以施法者为中心的视觉，说一声即可补上。
- **大地之怒的地面特效**用的是"区域 Fx 直接挂 `AbilitySpawnFx`"这条路径（不是 C 类区域系统）。
  若进游戏看不见 / 留痕，改成"纯视觉区域 + `AreaGroundFx`"（照坚守阵地那套）即可。
- **墨灵的特性**是我按"同级召唤物"给的通用包；具体哪几条想要/不要，直接说。
