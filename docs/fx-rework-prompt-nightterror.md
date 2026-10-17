# 【4 环 · 梦魇】（Nightterror，夜魔 / 格罗莉亚）：专属自绘特效重构规范与任务委托

> 目标：彻底废除原先借用原版血雾重染深紫黑的拼凑方案，
> 全面重构为还原《明日方舟》夜魔（格罗莉亚）二技能【夜魇魔影】、双重人格交织与心智恐惧恶咒的专属自绘纯视觉特效。

本规范直接作为 **Macroscope Check-Run Code Review & Generation Agent** 的核心执行指令。Macroscope 负责在 PR Review 中针对 `src/NightterrorFx.cs` 输出完整、高精度的自绘视觉算法与替换代码（drop-in replacement code via suggestion blocks）。

---

## 一、角色背景与核心意象调研

### 1. 对应角色：【夜魔】（Nightmare，格罗莉亚 Gloria）
* **角色背景**：罗德岛 5 星术师干员，罕见的**双重人格**感染者。
  * **白天/主人格·格罗莉亚**：温驯乖巧、柔弱胆怯的小女孩，拥有治愈他人的温柔力量（白蓝光辉）；
  * **黑夜/里人格·夜魔**：狂暴残忍、轻佻冷酷的暗影恶灵，擅长噩梦操纵与心理折磨（深紫与幽暗梦魇）。
* **技能对应**：二技能**【夜魇魔影】（Nightmare's Shadow）**——在敌人脚下与周身植入如影随形的梦魇恶咒。

### 2. 本模组机制：【4 环 · 梦魇】（Nightterror）
* **学派与标签**：附魔学派（恐惧、心智影响），中距，单体目标，意志部分通过。
* **判定分支**：
  * **意志豁免失败**：陷入极度**恐慌（Panicked / Frightened）仓皇逃跑 2 轮**，随后转化为**战栗（Shaken，持续 1 轮/级）**；
  * **意志豁免成功**：心智震颤，仅**战栗 1 轮**。
* **现状痛点**：当前仅借用原版血雾光环重染深紫黑 `(0.10, 0.06, 0.16)`，属于临时借用原版粒子的方案，在加法着色器下微弱发灰，完全缺乏夜魔人格特有的“噩梦侵蚀、恶灵鬼影缠绕、心智恐慌撕扯”的标志性视觉。

---

## 二、视觉意象与色彩设计标准

1. **色彩体系**：
   - **幽魅深紫与格罗莉亚冰蓝交织**（体现夜魔双重人格交织与撕裂感）：
     · `NightmarePurple` (`new Color(0.68f, 0.22f, 0.95f, 1f)`)：夜魔里人格·幽魅深紫；
     · `GloriaBlue` (`new Color(0.42f, 0.75f, 1.00f, 1f)`)：格罗莉亚主人格·冰蓝/心智微光；
     · `MindWhite` (`new Color(0.95f, 0.95f, 1.00f, 1f)`)：白炽心智裂光·高光内芯；
     · `ShadowViolet` (`new Color(0.40f, 0.12f, 0.65f, 1f)`)：幽暗梦魇底色·残影外晕；
   - ⚠️ **材质避坑**：加法（Additive）材质下颜色越暗越透明，严禁使用纯黑或极暗色，保证 45° 俯视远景下清晰完整。

2. **施法一瞬（植入魔影 · `NightterrorCastAnim`）**：
   - 心智冲击波贯穿目标头部，双重人格的微裂痕闪烁激荡。

3. **判定深度同步（Rule & VFX Sync）**：
   - **意志豁免失败（恐慌逃跑 2 轮 · `NightterrorPanicAnim`）**：
     · 目标头顶盘旋浮现狂乱扭曲的【夜魇魔面/恐惧之眼】（正对相机，深紫与冰蓝双色交错撕扯）；
     · 脚底泛起深紫幽暗【梦魇漩涡】；
     · 身周数道【暗影怨灵之触】在周围疯狂撕扯纠缠，恐惧感拉满；
     · 由 `MarkFxTracker` 订阅 `RhodesDoctorBuffNightterrorPanic`，随目标逃窜全程跟随，到期或被驱散后 0.3s 平滑淡出。
   - **意志豁免成功（战栗 1 轮 · `NightterrorPassAnim`）**：
     · 仅受创一瞬在胸口与周身泛起微弱的幽蓝震颤波纹与轻度心智微光（约 0.8s 迅速敛去）。

---

## 三、代码挂接架构与已建立的脚手架

脚手架已在 `src/NightterrorFx.cs`、`src/Actions.cs`、`src/SpellsSetup.cs`、`src/MarkFxKit.cs` 中建立完成并编译通过（0 警告 0 错误）：
- **施法植入魔影**：`effects.Add<ContextActionNightterrorCastFx>()` -> `NightterrorFx.PlayCast(caster, target)`。
- **意志失败分支**：
  - 增益应用：`failed: ActionsBuilder.New().ApplyBuff(panic, ContextDuration.Fixed(2, DurationRate.Rounds)).Add<ContextActionNightterrorFailFx>()`。
  - 常驻跟随：`MarkFxWatcher` 自动向 `NightterrorFx.PanicTracker` 同步增益生命周期，驱动 `NightterrorPanicAnim`（继承自 `MarkFxBase`）。
- **意志成功分支**：
  - 增益与动效：`succeed: ActionsBuilder.New().ApplyBuff(shakenLong, ContextDuration.Fixed(1, DurationRate.Rounds)).Add<ContextActionNightterrorPassFx>()` -> `NightterrorFx.PlayPass(target)`。
- **旧素材清理**：已彻底移除 `src/TintedAuraFx.cs` 中借用血雾重染的旧表项。

---

## 四、Macroscope 的代码产出任务（不可妥协的边界）

Macroscope 应针对 `src/NightterrorFx.cs` 输出完整的自绘视觉实现，满足以下技术与视觉标准：

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
   - 使用平滑缓动曲线（如 EaseOut3, EaseInQuad, 阻尼振荡、有层次的缓出衰减）。
   - 确保施法冲击、梦魇魔面、漩涡波动与怨灵触手撕扯具有强烈的视觉张力与电影感。

---

## 五、Checklist（代码审查与交付验收）

- [ ] `NightterrorCastAnim`：心智冲击波贯穿目标头部，双重人格微裂痕闪烁。
- [ ] `NightterrorPanicAnim`：
  - [ ] 头顶正对镜头的夜魇魔面/恐惧之眼（深紫与冰蓝双重人格交织）；
  - [ ] 脚底幽暗梦魇漩涡与旋转波动；
  - [ ] 周身撕扯纠缠的暗影怨灵之触，跟随目标移动；
  - [ ] 0.3s 平滑淡出，零运行时 GC。
- [ ] `NightterrorPassAnim`：心智震颤顿挫与轻度幽蓝余波，平滑敛去。
- [ ] 整个文件无编译警告、无运行时分配 GC、无材质泄漏。
