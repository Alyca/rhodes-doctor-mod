# 战术标记 · 专属「战术投影」特效（2026-10-16）

> 作者要求：**去掉原来借的"天界印记"**（教会十字金光，与 PRTS 风格不符），
> 改成**持续显示的 PRTS 战术投影**，颜色**青白**。
> 编译：`dotnet build -p:WrathPath="D:\steam\steamapps\common\Pathfinder Second Adventure"` **0 警告 0 错误**；
> 已部署（旧 DLL 备份 `RhodesDoctor.dll.bak-20261016-133817`，新 DLL md5 `FF8D3081BA66C741428DB245CE0FB885`，
> 与 `bin\Debug\net48` 产物一致）。**待作者进游戏确认**。

## 一、改法

原来「战术标记」在 `SpellsSetup.ApplyAbilityFx` 的 `s.Key switch` 里挂一发施法爆发：

```csharp
"TacticalMark" => FxLibrary.Mark,   // 天界印记 ArmyMarkOfHeaven，锚目标
```

`AbilitySpawnFx` 是**引擎直接播的素材**、**改不了形状也改不了色**，而且它只在施法瞬间闪一下，
与"标记持续 1 轮、被下一次攻击消耗"的语义不匹配。所以这一条**整条撤掉**，
改为**跟增益同生同灭的自绘特效**（做法与 `TintedAuraFx` / `MedicalDroneFx` 同源：订阅 `IUnitBuffHandler`）。

### 1.1 演出（`TacticalProjectionFx`）

| 阶段 | 内容 |
| --- | --- |
| 施法瞬间 | 一道外圈从**约 2.2 倍半径收缩到目标脚下**（代替原来的施法闪光，`IntroDur = 0.35 s`，缓出） |
| 标记期间 | ① 地面**实线准星圈**（半径 = 体型换算，`Clamp(Corpulence × 1.6, 0.7, 3.0)` m）<br>② 外圈**旋转虚线环**（6 段虚线，0.7 rad/s）<br>③ **四角定位角框**：入场从 1.6 倍滑入，之后 ±4% 轻微呼吸<br>④ **扫描环**从脚底反复扫到头顶（周期 1.4 s，越高越淡）<br>⑤ 头顶悬浮**琥珀色菱形**，`LineAlignment.View` 始终正对镜头、上下浮动 |
| 跟随 | 每帧读 `unit.Position`，特效整体跟目标走（不挂在 View 下，避免受单位模型缩放/朝向影响） |
| 消失 | 增益被消耗（下一次攻击）/ 到期 / 驱散 → **0.3 s 淡出**；另**每秒自检**一次"增益还在不在"，防漏事件残留 |

配色（`TacticalProjectionFx` 顶部常量）：青 `(0.25, 0.85, 1.00)` = PRTS 主色；白 `(0.85, 1.00, 1.00)` = 高光 / 扫描线；
琥珀 `(1.00, 0.72, 0.20)` = 头顶"目标"菱形。

### 1.2 材质与形状（为什么不用 `Shader.Find`）

- **形状全部自绘**（`LineRenderer`）：地面件 `LineAlignment.TransformZ` + 局部 Z 朝上 = 平躺；菱形 `LineAlignment.View`。
- **材质一律 `SwordStreakFx.StreakMaterialPlain(色)`** —— 这是**剑雨和重构已经实机验证能上屏**的那一份
  （克隆原版 FX 材质 + 自生成白色软边贴图，所以最终颜色就是传入的颜色）。
  **没有 `Shader.Find`，也没有借任何原版粒子素材** —— 这两条路在本 mod 的历史里都翻过车
  （见 `SwordStreakFx.cs` 文件头、`dev-notes.md` §0 / §7）。
- 高度只量**蒙皮网格**（`SkinnedMeshRenderer.bounds.max.y`）：粒子型素材的包围盒会被吹到几百米，不可信。

### 1.3 生命周期事件

- 过滤条件**两条取"或"**：① 增益蓝图名 `RhodesDoctorBuffTacticalMark`（`CreateSpellBuff` 的命名规则
  `RhodesDoctorBuff{Key}`）；② 增益蓝图 GUID = `Guids.Buff(0)`（`Specs[0].Key == "TacticalMark"`，
  而 `CreateSpellBuff(s, i)` 用的就是 **Specs 下标** i）。
  加 ② 的理由：本项目**其它常驻特效一律按 GUID 过滤**（`TintedAuraFx` / `MedicalDroneFx`，GUID 是存档里真实生效的键），
  ①那一路要依赖 BlueprintCore 把 `New(name, guid)` 的 name 落到 `Blueprint.name` 上；两条都写，
  哪条口径变了都不会让整条特效**静默哑掉**（本项目吃过的"改了但什么都没发生"的亏够多了）。
  ⚠ 若日后重排 `SpellsSetup.Specs`，`Guids.Buff(0)` 这个下标要跟着改（代码里有注释）。
- 同一目标**再次被标记** → 不新建，`Replay()` 重播入场。
- 同目标**两份标记**（两次施放）时，移除事件里用 `UnitHasMark(unit, 已移除的那份)` 复核，
  还剩一份就**不清理**（`OnBuffRemoved`）。
- 淡出期间被重新标记 → 新建一份（旧的自己淡完销毁）；`OnDestroy` 里 `Forget(unit, this)` 按引用比对，
  不会把新实例从表里误删。

## 二、改过的文件

| 文件 | 改动 |
| --- | --- |
| `src/TacticalProjectionFx.cs` | **新增**：特效主体 `TacticalReticleAnim` + 事件订阅 `TacticalProjectionFxWatcher`；材质解析失败会记一行 `Error`（不静默） |
| `src/Main.cs` | 加 `_tacticalProjectionWatcher` 字段 + `SubscribeOne(ref _tacticalProjectionWatcher, "战术投影")`；订阅失败的日志文案补齐 |
| `src/SpellsSetup.cs` | 撤掉 `"TacticalMark" => FxLibrary.Mark`（约 2054 行），改为注释说明走 `TacticalProjectionFx` |
| `docs/fx-status.md` | 3.1 表「战术标记」行、`Mark` 元素表行、0 环清单、手法要点小节同步 |

- `FxLibrary.Mark`（天界印记 `ArmyMarkOfHeaven`）**保留定义但已无引用** —— 方便日后要对比/回退，不影响编译（0 警告）。
- **只动视觉**：+1 洞察加值、被下一次攻击消耗、1 轮持续时间等机制一律未动。

## 三、进游戏要看的点

1. 施法瞬间是否有一圈**青白外圈收拢**到目标脚下（不再是金光十字）。
2. 地面准星圈 / 旋转虚线环 / 四角角框 / 上下扫描环 / 头顶琥珀菱形五件套是否都在、是否跟着目标移动。
3. 被下一次攻击**消耗掉标记**后，特效是否在 0.3 s 内淡出（不留残影）。
4. 光圈是否**平躺**、菱形是否**始终正对镜头**（若角框/地面圈立起来了，说明 `LineAlignment`/朝向那步不对）。

> 日志（需 `RD_VERBOSE`）：生成时会打一行 `[RhodesDoctor] 战术标记·战术投影已生成。`；
> 材质解析失败时（release 也可见）打 `战术投影放弃：StreakMaterialPlain 无可用材质…`。
