# 影霄·拔刀（9 环·阿米娅·升变/魔王）：专属自绘特效重构规范与任务委托

> 目标：彻底废除原先借用剑雨武器模型（巨剑放大 6m 垂直下落插地）的拼凑方案，
> 全面重构为还原《明日方舟》阿米娅升变/魔王终结绝技【影霄·绝影 / 影霄·拔刀】的专属自绘纯视觉特效。

本规范直接作为 **Macroscope Check-Run Code Review & Generation Agent** 的核心执行指令。Macroscope 负责在 PR Review 中针对 `src/ShadowBladeFx.cs` 输出完整、高精度的自绘视觉算法与替换代码（drop-in replacement code via suggestion blocks）。

---

## 一、原作意象与核心视觉语言

阿米娅以萨卡兹魔王之躯拔出影霄剑时，情绪能量激荡，撕裂因果律与三维物理空间：
1. **纯黑与暗红空间斩痕（Dimensional Slash Rift）**：
   - 并非实体刀刃飞掷，而是刀芒瞬息掠过所留下的空间切口。
   - 视觉呈现为极度深邃的纯黑虚空裂痕（`PureVoidBlack`）外裹暗红高能魔王情绪光晕（`CrimsonEdge` / `HighGlowRed`）。
   - 切线具备高烈度视觉冲击：极速延展（<0.12s）、短暂定格与断层微光（锯齿/微裂隙）、随后空间张力拉回闭合。
2. **拔刀冲击与黑红晶簇崩散（Obsidian Shard Ejection）**：
   - 刀芒切过目标受击点的刹那，具象化的源石/情绪能量晶簇瞬间爆裂。
   - 黑曜石菱面碎片与绯红晶刺向切面法向呈锥形抛射，带物理抛物线重力衰减与空间阻尼。
3. **判定深度同步（Rule & VFX Sync）**：
   - **强韧失败即死（Kill Annihilation）**：目标因果被切断，躯体在黑红裂痕密布中向内短暂坍缩（Implosion），而后化作无数黑红光斑与晶屑彻底解离（Disintegration），不留残躯。
   - **强韧成功存活（Heavy Hit Stop & Lingering Scar）**：目标承受 10d6+CL 真实法术伤害，受击点产生强烈的帧停顿顿挫（Judder / Impact Hit Stop），留在空气中的撕裂剑痕不会立即闭合，而是悬停微颤 0.5s~0.7s 并散发暗红蒸汽余辉（`LingeringScar`），见证魔王一击的沉重。

---

## 二、代码挂接架构与已建立的脚手架

脚手架已在 `src/ShadowBladeFx.cs`、`src/Actions.cs` 与 `src/SpellsSetup.cs` 建立完成并编译通过：
- **施法命中切线**：`effects.Add<ContextActionShadowBladeSlashFx>()` -> `ShadowBladeFx.PlaySlash(caster, target)`。
- **强韧失败分支**：`failed: ActionsBuilder.New().Add<ContextActionShadowBladeKillFx>().Kill()` -> `ShadowBladeFx.PlayKill(target)`。
- **强韧成功分支**：`succeed: ActionsBuilder.New().Add<ContextActionShadowBladeHitFx>().DealDamage(...)` -> `ShadowBladeFx.PlayHit(target)`。

---

## 三、Macroscope 的代码产出任务（不可妥协的边界）

Macroscope 应针对 `src/ShadowBladeFx.cs` 输出完整的优化与细化实现，满足以下技术与视觉标准：

1. **自绘与渲染路径**：
   - 严禁借用原版粒子 PrefabLink，严禁调用 `Shader.Find`。
   - 材质统一复用 `MarkFxKit.Mat(color)`（底层为经过实机验证的 `SwordStreakFx.StreakMaterialPlain`）。
   - 几何支持 `LineRenderer`、动态生成的程序化 `Mesh`（如菱形晶体、多边形切面、环形震波）。
2. **零运行时 GC（Zero Allocation in Update/Tick）**：
   - 所有数组、顶点缓冲区、数学临时变量在 `Init` 或组件字段中预分配。
   - `Update()` / `Tick()` 每帧不允许任何 `new` 操作。
3. **严谨的生命周期与安全清理**：
   - 所有的动效 GameObject 在预定时间结束后必须被 `Destroy(gameObject)`。
   - 必须包含严密的单位 View、坐标有效性检查，目标脱战或场景切换时不引发异常。
4. **高质量的动画数学与插值**：
   - 使用平滑缓动曲线（如 EaseOut3, EaseInQuad, 阻尼振荡、有层次的缓出衰减）。
   - 确保斩击、崩散、即死消亡与余辉震颤具有电影级的速度感与节奏反差（瞬间爆发 vs 缓缓消散）。

---

## 四、Checklist（代码审查与交付验收）

- [ ] `ShadowBladeSlashAnim`：具有纯黑内芯与暗红外焰的双层切裂光带，切线延展与收缩具备强烈的冲击张力。
- [ ] 晶体崩散具有初速度、角度散射与重力/速度衰减数学。
- [ ] `ShadowBladeKillAnim`：即死消亡具有向内收缩与向外崩解的层次变化，符合即死毁灭意象。
- [ ] `ShadowBladeHitAnim`：重击存活具有高频微颤与暗红余辉，时间轴在 0.5s~0.8s 间平滑淡出。
- [ ] 整个文件无编译警告、无运行时分配 GC、无材质泄漏。
