# 【核心职业特技 · 巴别塔恶灵形态】（Specter of Babel，博士 Doctor）：专属自绘特效重构规范与任务委托

> 目标：彻底废除此前在 `src/TintedAuraFx.cs` 中借用原版血雾重染黑烟的拼凑方案，
> 全面重构为还原《明日方舟》前文明源石主宰者“巴别塔的恶灵”源石神经突触辐射网络与前文明黑冠精神虚影的专属自绘纯视觉特效。

本规范直接作为 **Macroscope Check-Run Code Review & Generation Agent** 的核心执行指令。Macroscope 负责在 PR Review 中针对 `src/SpecterFormFx.cs` 输出完整、高精度的自绘视觉算法与替换代码（drop-in replacement code via suggestion blocks）。

---

## 一、角色背景与核心意象调研

### 1. 对应角色：【博士】（Doctor，前文明源石主宰者 / “巴别塔的恶灵”）
* **角色背景**：前文明遗留的源石工程首席研究员与主宰者，萨卡兹各族敬畏而恐惧的战争幽灵“巴别塔的恶灵”。黑冠（文明的存续）的继承与驾驭者，心智与源石神经网络深度交融。
* **技能机制与意象映射**：
  * **学派与类型**：死灵 / 变化，博士核心职业特技；
  * **形态切换**：
    - **敏捷形态（Agile Form）**：战斗本能转化为极速反应与灵动闪避，神经脉冲如闪电疾驰；
    - **坚韧形态（Enduring Form）**：源石结晶与重力场强化肉体，坚不可摧；
  * **持续与余波**：每日轮数持续维持，形态结束或力竭后承受 2 轮恶灵余波（Aftermath），陷入疲惫与移动限制。
* **现状痛点**：
  - 此前仅在 `TintedAuraFx.cs` 中借用原版血雾 `PlasmaAura` 重染黑烟 (0.26, 0.24, 0.30)，平淡模糊、微弱发虚；
  - 在 Unity 加法着色器（Additive Blending）下，暗色几乎不可见甚至彻底透空；
  - 完全缺乏前文明源石主宰者“巴别塔的恶灵”的威严压迫感，没有源石神经突触向外辐射蔓延与前文明黑冠精神虚影悬浮笼罩的霸道质感。

### 3. 作者实机测试严重反馈（2026-10-17 第一轮反馈）：
> **“不是很满意，现在搞这种线条都成路径依赖了，实际上还是特别简陋的。你懂吧”**

**核心问题与重构要求**：
1. **彻底打破纯线条/空心线框（Wireframe Line）路径依赖**：
   - 坚决杜绝用细线描边拼出来的空心线条（单线锯齿圈、单线突触、空心菱形晶壁）；在 45° 俯视镜头下，这些像 3D 线框线稿草图，既空心又简陋，完全没有实体质感与威严气势。
2. **转向实体厚度与立体体积感（Solid Geometry & Volumetric Presence）**：
   - **头顶前文明黑冠**：绝不能只是单薄线圈！必须具备**实体多面体棱面（Solid Polyhedral / Prismatic Mesh 或密实体积多层结构）**，呈现由厚重黑色源石晶体构筑而成的实体王冠，表面漆黑源石晶体与白金精神棱边相互辉映，内嵌精神核心光冕与精神晶体，向下垂落丰富光瀑流光；
   - **坚韧形态晶壁**：必须是**实体结晶面片（Mesh/Quad 三角棱面、晶体簇或晶盾）**，具备真正的光影遮挡与体积厚度感，而不是几根细线轮廓；
   - **敏捷形态金流与突触**：必须采用**宽幅流体带（Wide Ribbon/Sheet，带 widthCurve 宽幅渐变）**，呈现源石能量奔涌的厚度感；
   - **开启与瓦解**：虚空裂隙具备撕裂遮蔽面与冲击厚度，瓦解时迸射出具有实体尺寸的源石晶屑面片与星尘漫散。

---

## 二、视觉意象与色彩设计标准

### 1. 色彩体系（加法材质避坑至上）
* **加法着色器红线**：加法材质下颜色越暗越透明，纯黑 100% 隐形！
* **调色盘标准**：
  - **漆黑源石晶体（Originium Obsidian）**：`new Color(0.12f, 0.08f, 0.18f, 1f)`
    · 加法材质下**必须配合黑金/暗紫边缘辉光防透空隐形**；
  - **炽烈源石金（Originium Gold）**：`new Color(1.40f, 0.85f, 0.18f, 1f)`
    · 敏捷形态神经突触网络与能量脉冲流束；
  - **幽邃精神紫（Neural Violet）**：`new Color(0.68f, 0.28f, 1.25f, 1f)`
    · 坚韧形态深紫结晶晶壁与重力场波纹；
  - **文明余辉白金（Crown Platinum）**：`new Color(0.95f, 0.95f, 1.20f, 1f)`
    · 头顶前文明“黑冠”精神虚影核心光冕；
  - **恶灵余波暗沉色（AftermathDim）**：`new Color(0.40f, 0.30f, 0.45f, 1f)`
    · 疲惫虚弱的暗沉余光与微弱神经余颤。
* **厚度与实体感**：必须具备立体厚度与实体感，保证 45° 俯视远景下清晰完整，坚决拒绝单薄空心细线。

### 2. 四大动效组件构成

1. **恶灵形态开启瞬间（`SpecterActivateAnim : MonoBehaviour`，~0.8s）**：
   - 角色胸口与脚下源石神经突触猛烈向外放射激荡；
   - 头顶上方虚空撕裂，前文明黑冠精神虚影显现；
   - 播放完毕自动销毁。

2. **形态常驻跟随（`SpecterFormAnim : MarkFxBase`，随形态增益全程跟随）**：
   - **头顶**：悬浮立体自旋的前文明“黑冠”精神虚影（文明的存续），向下垂落细碎微光精神粒子；
   - **脚底与周身**：蔓延脉动的源石神经突触网络：
     · **敏捷形态（`IsAgile = true`）**：偏炽烈源石金光流束与迅捷脉冲；
     · **坚韧形态（`IsAgile = false`）**：偏幽邃精神紫结晶晶壁与重力场波纹；
   - 持续时间结束或主动关闭时 0.3s 平滑淡出。

3. **恶灵形态结束/关闭瞬间（`SpecterDeactivateAnim : MonoBehaviour`，~0.8s）**：
   - 黑冠瓦解化为漫天源石星芒光尘散落；
   - 源石突触收缩敛入体内；
   - 播放完毕自动销毁。

4. **恶灵余波期间（`SpecterAftermathAnim : MarkFxBase`，随 Aftermath 增益跟随 2 轮）**：
   - 疲惫虚弱的暗沉余光与微弱神经余颤；
   - 持续时间结束时 0.3s 平滑淡出。

---

## 三、代码挂接架构与已建立的脚手架

脚手架已在 `src/SpecterFormFx.cs`、`src/MarkFxKit.cs`、`src/TintedAuraFx.cs` 中建立完成并编译通过（0 警告 0 错误）：
- **脚手架文件**：`src/SpecterFormFx.cs`
  - 声明调色盘与 GUID 映射；
  - 提供 `PlayActivate(caster, isAgile)` 与 `PlayDeactivate(caster)`；
  - 提供 `OnBuffAdded(Buff)` 与 `OnBuffRemoved(Buff)`；
  - 声明四个动效组件类骨架：
    · `SpecterActivateAnim`
    · `SpecterFormAnim`（继承 `MarkFxBase`）
    · `SpecterDeactivateAnim`
    · `SpecterAftermathAnim`（继承 `MarkFxBase`）
- **事件监听挂接**：`MarkFxWatcher` 在 `HandleBuffDidAdded` 和 `HandleBuffDidRemoved` 中统一调用 `SpecterFormFx.OnBuffAdded(buff)` 与 `SpecterFormFx.OnBuffRemoved(buff)`。
- **旧素材清理**：已彻底移除 `src/TintedAuraFx.cs` 中借用血雾 `PlasmaAura` 重染黑烟的 6 个旧条目。

---

## 四、Macroscope 的代码产出任务（不可妥协的边界）

Macroscope 应针对 `src/SpecterFormFx.cs` 输出完整的自绘视觉实现，满足以下技术与视觉标准：

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
   - 使用平滑缓动曲线（如 EaseOut3, EaseInQuad, 阻尼振荡、有层次的突触与晶壁数学）。
   - 确保黑冠立体自旋、突触辐射脉动、星芒光尘散落与余波余颤具备前文明主宰者的威严压迫感。

---

## 五、交付与 Review 要求
- Macroscope 必须在 Check-Run Review 中针对 `src/SpecterFormFx.cs` 提供**完整的、可直接应用的 drop-in suggestion replacement code blocks**，补齐四个动效组件的具体自绘实现；
- 严禁空洞的文字描述，必须交付直接可编译运行的 C# 代码。

<!-- trigger check-run for Phase 3.5 solid geometry overhaul -->
