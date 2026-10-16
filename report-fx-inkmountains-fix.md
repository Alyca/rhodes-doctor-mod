# 法术特效 · 墨染山河「召唤闪光」专项修复报告

> 日期：2026-10-10（夜）　范围：作者实测反馈——墨染山河的"花"从地面升到两人高、没变大、像"叠加高度"。
> 依据：`C:\Users\DELL\AppData\LocalLow\Owlcat Games\Pathfinder Wrath Of The Righteous\Player.log`（2026-10-10 20:10 那份）。
> 编译：`dotnet build -p:WrathPath="…"` **0 警告 0 错误**；已 `tools/deploy.sh` 部署（旧 DLL 备份 `RhodesDoctor.dll.bak-20261010-201951`）。

---

## 一、从日志读到了什么（不再猜）

| 日志行 | 结论 |
|---|---|
| `[特效尺寸] 素材 30142fb7093d7f84bbb15e2a2d29923c 原始包围盒=351.65x44.55x344.75 米` | 召唤闪光用的素材是 **`30142fb7…`**，是个**巨型** FX：**65 个粒子系统**、包围盒 **351×44×345 米**（横向铺得很开、竖向 44 米）。部件名：`Lotus`（莲）/`FrontPetals`/`BackPetals`（花瓣，网格 `Suramgama00_Petal`）/`Umbrella`×9/`GrowingCenter`/`GrowingBranches`/`RayVertical_GodRays`/`GlowFront`/`BackGlow`/`RingWaveBig`/`Sphere`/`Spirals`。 |
| `Can't remove Light because AnimatedLight (Script) depends on it`（3 次） | 这个素材**自带一盏灯**（`AnimatedLight` 脚本）。我原来的 `FxTint.Spawn → Apply` **默认删灯**（`keepLights=false`），删不掉 → 报错。（也与会话末尾那个 `NullReferenceException` 疑似相关。） |
| `通用档：65 个粒子系统已设为 Hierarchy 缩放模式`（3 次） | 我原来给这个素材套了**根节点缩放 `Scale=2`** + 把 65 个粒子改成 `Hierarchy` 缩放模式。 |
| 借用链 `特效借用成功（爆发·法术·兜底）：72ae181281075c74ea2d1ae74ad95597` | `72ae1812…` = `SecretOfHookingAndSummoning`，走的是**兜底**（它两条 `AbilitySpawnFx` 都是 `Anchor: Caster`，所以取了**第一条** `30142fb7…`，即 `OnPrecastFinished` 那份）。 |

---

## 二、诊断（为什么"没变大 + 反而升高"）

**根节点缩放**（`FxTint.Spawn` 的 `scale` 参数）在 Unity 默认的 **`Local` 缩放模式**下：
- 会把 FX 里**高于根节点**的部件按比例**抬高**（发射器位置跟着缩放走）；
- **却不会放大粒子本身**（粒子尺寸不跟根缩放走）。

所以 `Scale=2` 的实际效果 = **位置升高一倍、粒子尺寸没变** —— 正好对上作者说的"其实没变大、看着像叠加高度"。
我随后加的 `EnableHierarchyScaling`（把粒子改成 `Hierarchy` 模式）**没能救回来**：它是在实例化**之后**才设的，而这个素材是**一次性爆发**（粒子在生成当帧就发射完了），模式改晚了。

> 一句话：**"放大特效"不能用根节点缩放**（对这类粒子 FX 无效且会抬位），得**只放大粒子本身**。

---

## 三、修复

| 改动 | 说明 |
|---|---|
| `FxTint.SpawnPlaced(...)`（新增） | **原样**实例化到世界坐标：**不动根缩放**、**保留素材自带的灯**、自动播放、自动销毁。 |
| `FxTint.ScaleParticleSizes(go, factor)`（新增） | **只放大粒子本身**：`main.startSize`（用 `curveMultiplier` 覆盖曲线/常量各模式）+ 发射形状 `shape.radius`。**不碰根缩放**，所以特效**原地变大**、位置不动。 |
| `ContextActionSpawnFxBetweenSummons` | 改用 `SpawnPlaced` + `ParticleSizeScale`（不再 `Scale`、不再 `EnableHierarchyScaling`）；位置仍是**水平取两只墨灵中点、高度取落点**。 |
| `SpellsSetup`（`InkedMountains` 分支） | `a.ParticleSizeScale = 2f;`（想更大 / 更小改这里）。 |
| 诊断日志（新增） | 每次施放打一行：`落点 / 施法者 / 找到墨灵数 / 生成点 / 根节点 / 世界缩放`；**每次会话再打一次**全部 65 个部件的**世界位置**——下次若还有问题，日志能直接指出"花在哪一层高度"。 |

**注意**：这次**不改根缩放**，所以花应该回到素材**原生**的位置（也就是作者说的"本来在地面"那版），只是粒子被放大了 2 倍。

---

## 四、请作者复测（这次能靠日志定位）

1. 重启游戏，施放**墨染山河**，看花是不是**回到地面**、并且**确实变大了**。
2. 把这一版的 `Player.log` 给我。我会看两行：
   - `墨染山河·召唤闪光：落点=(x,y,z) 施法者=(x,y,z) 找到墨灵=N 生成点=(x,y,z) 根节点=(x,y,z)`
     —— 核对"生成点"的 Y 是不是地面、"根节点"有没有被抬高；
   - `墨染山河·部件世界位置（一次性）：… 部件 'Lotus' pos=(x,y,z) …`
     —— 直接看**花（`Lotus`/`FrontPetals`）在世界里的实际高度**。
3. 若花仍偏高：说明是素材**自身**把花摆在根节点之上（与缩放无关）→ 用 `HeightOffsetMeters`（`SpellsSetup` 的 `InkedMountains` 分支里 `a.HeightOffsetMeters`，负值往下压）把它压回地面。
4. 若 2 倍还不够 / 太大：改 `a.ParticleSizeScale`。

---

## 五、新发现（只记录）

1. `SecretOfHookingAndSummoning` 的两条 `AbilitySpawnFx` 都是 `Anchor: Caster`，`FxBorrow.AbilityBurstFx` 只能取到**第一条**（`30142fb7…`，`OnPrecastFinished`）；第二条 `58f7f63f…`（`OnApplyEffect`）用不到。若这份素材观感不对，可考虑让 `AbilityBurstFx` **优先取 `OnApplyEffect`**。
2. `FxTint.Spawn → Apply` 默认 `keepLights=false`（删灯）——对**自带 `AnimatedLight`** 的素材会报 `Can't remove Light…`，本轮新增的 `SpawnPlaced` 已绕开（不删灯）；其余走 `Spawn` 的调用若碰到带灯素材同样会报，后续可考虑把默认改成"保留灯"。
3. 会话末尾有一个 `NullReferenceException at Kingmaker.View.CameraRig.ApplyBindAction`（退出时）——疑似与上面"删灯失败"有关，本轮已不再删这个素材的灯，若复测后仍出现再单独查。

---

## 六、下一步

墨染山河专项已修（待复测）。其余单体批次仍是 **6–9 环 9 个**（涌潮悲歌 / 黑蛇之焰 / 罪罚 / 镜中流形 / 终末狙击 / 归溟 / 画中世界 / 影霄·拔刀 / 旧日的战术家）。
