# 【7 环 · 归溟 / 潮】（ReturnToAbyss，归溟幽灵鲨 / 劳伦缇娜）：专属自绘特效重构规范与任务委托

> 目标：彻底废除此前在 `src/TintedAuraFx.cs` 中借用原版血雾重染蓝色的拼凑方案，
> 全面重构为还原《明日方舟》归溟幽灵鲨（劳伦缇娜）阿戈尔深海狂暴大洋暗流、生命同调逆流回溯与怒涛冲击的专属自绘纯视觉特效。

本规范直接作为 **Macroscope Check-Run Code Review & Generation Agent** 的核心执行指令。Macroscope 负责在 PR Review 中针对 `src/ReturnToAbyssFx.cs` 输出完整、高精度的自绘视觉算法与替换代码（drop-in replacement code via suggestion blocks）。

---

## 一、角色背景与核心意象调研

### 1. 对应干员：【归溟幽灵鲨】（Specter the Unchained，劳伦缇娜 Laurentina）
* **角色背景**：阿戈尔深海猎人二期工程的荣光，优雅与狂暴兼备的艺术家与深海战士。在恶灵形态下以精湛狂热的圆舞曲肢解敌人，驭使深海洋流与浪涌同调生息。
* **技能与意象映射**：
  * **深海狂暴暗流**：幽邃大洋中翻涌的狂暴洋流、漩涡与高速螺旋光带；
  * **生命同调吸血**：近战斩击命中时抽取敌方生机，如深海暗流般自受创目标处逆流回溯施法者；
  * **恶灵延续与怒涛**：暴击与斩杀瞬间引发深海猎人本能爆发，怒涛轰鸣并震荡出炽白浪沫冲击环。

### 2. 本模组机制：【7 环 · 归溟 / 潮】（ReturnToAbyss）
* **学派与标签**：死灵学派，迅捷动作（自身），恶灵形态专属。
* **持续时间**：1 轮/级（增益跟随施法者）。
* **结算效果**：
  * 近战命中伤害恢复 2d6 生命值（每轮上限 3 次）；
  * 每轮首次确认重击（暴击）或击杀敌人时，返还 1 轮恶灵形态持续时间。
* **现状痛点**：当前仅在 `TintedAuraFx` 中借用原版血雾重染蓝色，微弱发虚，在加法着色器下缺乏立体感与压迫感，完全没有深海猎人洋流缠身与生命逆流吸血的冲击力。

---

## 二、视觉意象与色彩设计标准

1. **色彩体系**：
   - **深渊海蓝与苍翠洋流绿交织**：
     · `AbyssalBlue` (`new Color(0.12f, 0.45f, 0.95f, 1f)`)：深渊海蓝·主调狂暴洋流；
     · `OceanicTeal` (`new Color(0.15f, 0.85f, 0.75f, 1f)`)：苍翠洋流绿·生命同调光晕；
     · `WaveFoamWhite` (`new Color(0.92f, 0.98f, 1.00f, 1f)`)：炽白浪沫·高光水粒与怒涛波芯；
     · `DeepOceanViolet` (`new Color(0.38f, 0.18f, 0.75f, 1f)`)：深海暗涌·外层暗流卷带；
   - ⚠️ **材质避坑**：加法（Additive）材质下颜色越暗越透明，严禁使用纯黑或极暗色，保证 45° 俯视远景下清晰完整。

2. **核心视觉构成**：
   - **迅捷施法瞬间（`ReturnToAbyssCastAnim`）**：
     · 施法瞬间脚下深海潮汐螺旋升腾炸开。
   - **增益常驻跟随（`ReturnToAbyssAnim`，持续 1 轮/级，继承 `MarkFxBase`）**：
     · 脚底自旋呼吸的深海洋流漩涡；
     · 周身螺旋缠绕升腾的大洋暗流光带与悬浮浪沫水粒；
     · 持续时间结束或被驱散后 0.3s 平滑淡出。
   - **动作与反馈机制联动**：
     · **近战命中吸血（`ReturnToAbyssHealAnim`）**：幽蓝浪涌自目标处逆流回流施法者；
     · **首暴/首杀续期恶灵（`ReturnToAbyssRefundAnim`）**：深海怒涛暴涨，荡开一道炽白浪沫冲击环！

---

## 三、代码挂接架构与已建立的脚手架

脚手架已在 `src/ReturnToAbyssFx.cs`、`src/Actions.cs`、`src/Components.cs`、`src/SpellsSetup.cs`、`src/MarkFxKit.cs` 中建立完成并编译通过（0 警告 0 错误）：
- **施法潮汐动作**：`effects.Add<ContextActionReturnToAbyssCastFx>()` -> `ReturnToAbyssFx.PlayCast(caster)`。
- **常驻大洋流增益**：`MarkFxWatcher` 自动向 `ReturnToAbyssFx.Tracker` 同步增益生命周期，驱动 `ReturnToAbyssAnim`（继承自 `MarkFxBase`）。
- **命中吸血回流**：在 `ReturnToAbyssHunger.OnEventDidTrigger(RuleAttackRoll)` 结算近战回血时调用 `ReturnToAbyssFx.PlayHeal(evt.Target, Owner)`。
- **首暴/首杀恶灵续期**：在暴击确认与击杀返还恶灵持续时间时调用 `ReturnToAbyssFx.PlayRefund(Owner)`。
- **旧素材清理**：已彻底移除 `src/TintedAuraFx.cs` 中借用血雾重染的旧表项。

---

## 四、Macroscope 的代码产出任务（不可妥协的边界）

Macroscope 应针对 `src/ReturnToAbyssFx.cs` 输出完整的自绘视觉实现，满足以下技术与视觉标准：

1. **自绘与渲染路径**：
   - 严禁借用原版粒子 PrefabLink，严禁调用 `Shader.Find`。
   - 材质统一复用 `MarkFxKit.Mat(color)`（底层为经过实机验证的 `SwordStreakFx.StreakMaterialPlain`）。
   - 几何支持 `LineRenderer`、动态生成的程序化 `Mesh` 或 Quad，保证 45° 俯视镜头下线条与几何饱满可见。
2. **零运行时 GC（Zero Allocation in Update/Tick）**：
   - 所有数组、顶点缓冲区、数学临时变量在 `Init` / `Build` 或组件字段中预分配。
   - `Update()` / `Tick()` 每帧不允许任何 `new` 操作。
3. **严谨的生命周期与安全清理**：
   - 所有的动效 GameObject 在预定时间结束后必须被 `Destroy(gameObject)`。
   - 必须包含严密的单位 View、坐标有效性检查，目标脱战或场景切换时不引发异常。
4. **高质量的动画数学与插值**：
   - 使用平滑缓动曲线（如 EaseOut3, EaseInQuad, 阻尼振荡、有层次的流体螺旋数学）。
   - 确保潮汐升腾、洋流漩涡自旋呼吸、周身光带盘旋、逆流回溯与怒涛冲击环具有深海猎人磅礴霸道的视觉张力。

---

## 五、Checklist（代码审查与交付验收）

- [ ] `ReturnToAbyssCastAnim`：施法瞬间脚下深海潮汐螺旋升腾炸开。
- [ ] `ReturnToAbyssAnim`：
  - [ ] 脚底自旋呼吸的深海洋流漩涡；
  - [ ] 周身螺旋缠绕升腾的大洋暗流光带与悬浮浪沫水粒；
  - [ ] 0.3s 平滑淡出，零运行时 GC。
- [ ] `ReturnToAbyssHealAnim`：命中吸血时幽蓝浪涌自受创目标逆流回流施法者。
- [ ] `ReturnToAbyssRefundAnim`：每轮首暴/首杀续期恶灵时，深海怒涛暴涨，荡开一道炽白浪沫冲击环。
- [ ] 整个文件无编译警告、无运行时分配 GC、无材质泄漏。
