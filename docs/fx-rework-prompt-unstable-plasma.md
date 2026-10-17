# 不稳定血浆自绘特效重构指令（华法琳 2 环）

项目：`D:\SteamLibrary\Mods\rhodes-doctor-mod`（《开拓者：正义之怒》mod，以《明日方舟》角色 / 技能为原型）
游戏本体：`D:\steam\steamapps\common\Pathfinder Second Adventure`

---

## 1. 目标法术：不稳定血浆 `UnstablePlasma`（2 环 · 华法琳）—— 必须由 Macroscope 重写！
- **目标文件**：`src/UnstablePlasmaFx.cs`
- **原作意象**：《明日方舟》华法琳（血魔医生），罗德岛血疗与神经活性剂，S2 技能【不稳定血浆】。
- **解耦要求**：
  彻底移除了 `TintedAuraFx` 中借用原版血雾 `PlasmaAura` 的简陋换色逻辑，改为纯自绘 3D 程序化视觉。

### 设计与数学重构要点：
1. **三元立体菱形血晶（Tri-Orbital Polyhedral Blood Crystals）**：
   - 3 枚锐利修长的菱形多面体血晶（双棱锥/八面体线框），呈 120° 正三角形环绕躯干（约 `Height * 0.55f`，半径 `Radius * 1.05f`）公转；
   - 带有独立的自旋倾角、三相错位的正弦垂直沉浮（`bob`），以及晶核纵向微光轴线；
2. **二段式心率脉冲电波（Cardiac ECG Pulse Wave）**：
   - 模拟真实心脏的二段式跳动（“lub-dub” 节律，周期约 1.0s）：
     - `t ∈ [0.00, 0.18]s`：第一心音（收缩期强脉冲），波环自胸口高速炸开，周向叠加高频尖锐的 ECG QRS 心电锯齿波；
     - `t ∈ [0.22, 0.38]s`：第二心音（主动脉瓣关闭回波），次级脉冲波扩散；
     - `t ∈ [0.38, 1.00]s`：舒张间歇期平滑回缩与淡入。
3. **升腾血能双螺旋（Ascending Blood Helix）**：
   - 两股互呈反相位的猩红与试剂金螺旋光带自足底盘旋升腾至头顶（约 `Height * 1.1f`）；
   - 半径随高度呈拟人瓶颈曲线（腰部向内收束、顶端自然散逸扩散），表达注射血浆后血液循环与生命燃烧的狂暴活力；
4. **地面试剂刻印环（Ground Reagent Glyph）**：
   - 脚底极细的试剂同心圆与四向刻度针刺，低对比度衬托主体，随心跳节律微幅呼吸。

---

## 2. 产出要求
Macroscope 必须在 PR 评审中提供 `src/UnstablePlasmaFx.cs`（特别是 `UnstablePlasmaAnim` 几何与数学动画计算）的完整 `suggestion` 替换代码！
要求：
- 纯代码 `LineRenderer` + `SwordStreakFx.StreakMaterialPlain` + 相机正交基 `CamBasis`；
- 零运行时 GC（不每帧 new 数组）；
- 严格遵循 `Master` 渐隐控制（0.3s 平滑淡出）。
