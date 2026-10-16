# 开发踩坑记录（接口 / 运行时）

本文件记录**已在游戏里实测过**的接口与运行时经验，供后续改代码时先查这里，避免重复踩坑。

规则与设计以 `rhodes-doctor-archetype.txt` 为准；源码结构与编译见 `../README.txt`。

---


## ⚠️⚠️⚠️ 头号禁令：粒子尺寸的"半径 / 直径"必须先确认（2026-10-09，最贵的低级失误）

> **给任何粒子设尺寸（`startSize` / `sizeOverLifetime` / `transform.localScale`）之前，
> 先确认这个数值是"半径"还是"直径"。**
> **拿不准就现场试一次（本 mod 有 `[` `]` 热键可实时调），绝对不要靠推算。**

**这个失误长什么样**（钙质化，来回折腾近十轮）：

| 环节 | 我的做法 | 实际 |
|---|---|---|
| 给云设尺寸 | `startSize = 2 × 区域半径`（**按直径**） | 这个 FX 的 `startSize` **按半径解释** |
| 结果 | 云**每次都大一倍**：铺满整片场地、远超法术作用圈 | — |
| 更糟的 | **日志里的数字看着完全正确**（`尺寸 0.00→10.73`），于是我在**错误的基础上反复微调**（0.8 → 0.88 → 1.06 → 0.88），越改越乱，用户连续否掉 | 正确做法：**除以 2**（0.88 → 0.44）后立刻正常 |

**为什么会踩**：`startSize` 在 Unity 里对 billboard 通常是"尺寸"（可当直径），
但**不同 shader / prefab 的解释可能不同**，Owlcat 的粒子尤其如此。
**"文档上写的是直径"不等于"这个 FX 按直径算"**。

**防复发措施（已落地）**：
1. 代码里**不再出现 `× 2`**——公式改成 `startSize = 区域半径(米) × CloudSizeFactor`，
   语义写进常量名（`CloudSizeFactor`），从写法上杜绝"直径"这个念头。
2. 常量注释开头就是三行 ⚠️ 警告 + 完整事故复盘。
3. `AreaGroundFx` 保留 `[` `]` **实时调参热键**——尺寸拿不准时**现场试**，别算。

---

## 0. 诊断方法（先看这里）

遇到"点了没反应 / 生成失败 / 法术目标不对"这类问题，别猜，按下面顺序取证：

1. **看日志**（中文按 UTF-8 读，否则是乱码）：

   `%LocalAppData%Low\Owlcat Games\Pathfinder Wrath Of The Righteous\`
   - `Player.log`：UMM / `Main.Mod.Logger` / `Debug.Log` 的输出，含我们的 `[RhodesDoctor]` 行。
   - `GameLogFull.txt`：游戏 `PFLog` 的完整日志，**异常堆栈最全**（如 `SpawnUnit` 内部抛的 NRE）。
     - 两个日志会在重开游戏时轮转：当前 `Player.log`、上一次 `Player-prev.log`。
2. **反编译游戏程序集**（本机已装 ilspycmd）：
   ```
   ilspycmd -t Kingmaker.Controllers.EntityCreationController "…\Wrath_Data\Managed\Assembly-CSharp.dll"
   ```
   泛型类型带反引号，如 `-t 'Kingmaker.ResourceLinks.WeakResourceLink`1'\`。
3. **查原版蓝图字段**：游戏根目录 `blueprints.zip`（约 25 万个 `.jbp`）。每个 `.jbp` 是一段 JSON，

   真正字段在 `"Data"` 下；用 Python 读比解压全量快：
   ```python
   import zipfile, json
   z = zipfile.ZipFile(r"…\blueprints.zip")
   d = json.loads(z.read("Spells/Level1/RayOfEnfeeblement.jbp").decode("utf-8-sig"))["Data"]
   print(d["CanTargetEnemies"], d["CanTargetFriends"], d["CanTargetSelf"])
   ```
4. **对照法**：自定义蓝图出问题时，先找一个功能等价的原版蓝图，逐字段 diff。本文几条结论都是这么来的。
5. **查特效有没有"真的上屏"**：别信"编译通过"，两条硬证据：
   - `Player.log` 里 `[RhodesDoctor] 特效借用成功/失败（…）` 行。`FxBorrow` 借不到是**静默跳过**的，

     所以"第几批已完成"不代表画面上有东西。
   - 直接读 `blueprints.zip` 里原版蓝图的字段：法术看有没有 `AbilitySpawnFx` 组件（\*\*可选组件，

     很多法术没挂\*\*），区域看 `Data["Fx"]` 是不是 null。示例：
     ```python
     import zipfile, json
     z = zipfile.ZipFile(r"…\blueprints.zip")
     d = json.loads(z.read("Spells/Level2/StoneCall.jbp").decode("utf-8-sig"))["Data"]
     print([c["$type"] for c in d["Components"]])          # 找 AbilitySpawnFx
     a = json.loads(z.read("Spells/Level2/StoneCallArea.jbp").decode("utf-8-sig"))["Data"]
     print(a.get("Fx"))                                    # 区域常驻 FX
     ```
     注意 `.fx` 素材本身在游戏根目录 `Bundles\*.fx`（Unity bundle，文件名≈素材名），

     但 `PrefabLink.AssetId` ↔ bundle 的映射没在 `blueprints.zip` 里，用文件名反查不到 AssetId。
6. **特效尺寸别猜，先分清素材"是圆的还是条/片的"**：区域的 `Shape` 字段就是答案，而且**决定了能不能拼圆**。

   （**完整流程见 §7**：素材来源决策树、自绘特效三件套、摆位字段速查、时间轴对齐、"看不见"的排查顺序。）
   - `Shape=Wall` 的 `Fx` 是**一条直线墙**，无论缩放/复制多少份都拼不成圆——

     典型：`BladeBarrierArea`（`Size=60`）、`DLC5_SwordOfSithhudAreaEffect`（`Size=180`）。

     剑雨为此白折腾了两轮：一开始复制 12 份绕圈，后来改成 1 份缩小，结果都还是"一条"。
   - `Shape=Cylinder` 的 `Fx` 才是圆的。而且 **`Size` 是半径（尺）**，据此可以直接换算缩放：

     半径 5 尺的素材 → 要覆盖半径 10 尺的爆发就 `scale=2`。这比量包围盒可靠得多。
   - **要"按个数摆放单个物件"（如摆一圈剑），别借投射物外观**：`BlueprintProjectile.View` 借到的是

     **飞行拖尾**（金色光弧），不是物件本身的形状——剑雨第二版就是这么"圆了但不像剑"的。
   - **要"看起来像某个武器"，借武器模型网格**：`BlueprintWeaponType.VisualParameters`（公开属性）

     里的 `m_WeaponModel` 才是**剑身网格**。注意 `WeaponVisualParameters.Model` 那个公开属性返回的是

     已加载的 `GameObject`，不是 `PrefabLink`；要拿 `PrefabLink` 得反射读私有字段 `m_WeaponModel`。

     见 `FxBorrow.WeaponModelFx`。可用 `WeaponTypeRefs.Greatsword / Longsword / BastardSword / Falchion`。
   - 附带一条：`ProjectileLink`（`WeakResourceLink<ProjectileView>`）与 `PrefabLink`（`WeakResourceLink<GameObject>`）

     是**兄弟类型不是父子**，不能直接转；需要时把 `View.AssetId` 抄进 `new PrefabLink { AssetId = ... }`

     （AssetId 仍是运行时从原版蓝图读的，没写死）。见 `FxBorrow.ProjectileViewFx`。
   - ⚠️ **别用 `Renderer.bounds` 反推尺寸**：粒子系统的 `bounds` 会被吹得远大于可见粒子。

     实测刃障那个 prefab 报 **348 米**，画面上可见的散布只有约 **25 米**（差一个数量级）。

     用 `FitToMeters`（按包围盒自适应）会让特效缩到几乎看不见；只有网格/贴地类素材才适合它。
   - ⚠️ **想靠缩放把借来的粒子素材"变小"，是行不通的**：粒子系统的**可见体积不跟根节点缩放走**，

     压 scale 只会白费。实证（剑雨拖尾，2026-10-08）：借"阳炎之刃"投射物外观当"一缕光尾"，

     `[特效尺寸]` 记到 **345.6x62.3x343.8 m、约 40 个粒子系统**；`FitToMeters=1.4` 缩完，

     画面上**仍是一整根橙色大光柱**，18 把剑各一条 → 叠成一根天量光柱（用户截图实证）。

     → **要"淡/小"的装饰（光尾、光点、火花）一律自己画**（`TrailRenderer` / `LineRenderer`，

     见 `src/SwordStreakFx.cs`、`src/BombFx.cs`），别去借粒子素材；借来的只适合"整片爆发"。
   - ⚠️ **自己画的光带别直接挂在借来的物件上**：`TrailRenderer.startWidth` / `LineRenderer.widthMultiplier`

     是**局部**单位，会再乘一次宿主的世界缩放（`lossyScale`）。武器模型 prefab 的根节点缩放很随意

     （可能是 0.01，几何靠子网格的 100 顶回来），12 cm 会被乘成 1 mm。做法：光带挂一个**子物件**，

     `localScale = 1/lossyScale` 把世界缩放归一，宽度就严格等于传入的米数；`layer` 也跟着宿主走。

     （注意：剑雨那把剑实测 `lossyScale=0.7416`，属正常值，所以**这不是**它"看不到光"的原因，

     归一化只是消除一类不确定性，不是已证实的病因。）
   - ✅ **`Shader.Find` 自建材质 = 画不出来；克隆原版 FX 材质 = 立刻就画出来**（2026-10-09 定论）：

     剑雨的光带先用 `Shader.Find("Legacy Shaders/Particles/Alpha Blended")` 自建材质，日志显示 shader

     存在、材质生成成功、宽度 ~9.6 cm、时长 0.3 s，**两次实测画面上都是空的**；同一次施法里"借来的

     原版粒子素材"照常上屏。改成 `new Material(克隆自原版 FX 的材质)`（混合模式 / 贴图 / 关键字一起

     继承）后**立刻可见**。

     **以后本 mod 要自建材质（光带 / 火花 / 光斑）一律走这条路**：来源 `SwordStreakFx.MaterialFor`，

     已克隆成功过的源是 `FxLibrary.SwordRainTrail`（阳炎之刃投射物外观）与 `FxLibrary.Holy`。

     `Shader.Find` 只留作兜底。判断"能不能渲染"的依据是**用户是否亲眼见过它上屏**，不是

     "编译通过 / 日志正常"。日志会打印所选材质、`shader.name` 与 `isSupported`。
   - ⚠️ **原版的"受击反馈"不可依赖，要自己给**：逐目标命中特效（敌人身上那一下）如果只靠原版那套

     反馈（血液 / 火花），会**时不时什么都不出**（受伤害类型、单位血型、是否 AoE、单位状态影响，

     条件不透明），实测表现为"受到伤害但没触发特效，且很随机"。

     做法：把自绘特效动作**挂进和伤害同一个动作列表**（`SavingThrow` 的 `failed` / `succeed` 两支，

     与 `DealDamage` 并列，见 `ContextActionSwordHitFx` + `src/SwordHitFx.cs`）——这样"伤害结算了"

     和"特效放了"是同一条件，不存在对不上的情况。

     **但时间上要补一刀**：伤害是施法瞬间结算的，而剑要 `FallSeconds`（0.4 s）后才落到人身上，

     所以命中特效带一个 `Delay`（= `FallSeconds`）才亮起，否则又变成"光先闪、剑还在天上"。

     实现上等待期间是**关掉 Renderer** 而不是把 alpha 调 0（万一是 opaque 材质，alpha 不起作用）。
   - ℹ️ **逐目标动作里 `Target.Unit` 就是当前被打中的那个单位**：`AbilityEffectRunAction` 的动作列表

     **每目标跑一遍**（所以 `ContextActionEarthFuryHit` / `ContextActionSwordHitFx` 才拿得到各自的目标）。

     要拿"被点击的落点"用 `Context.MainTarget.Point`，两者别混。
   - ⚠️ **竖向物件要用"最长边"而不是"水平面"来缩放**：`FitToMeters` 量的是 x/z 取大，只适合**铺在地上**的

     特效。**立起来的物件**（剑、柱、碑）长度在 **Y 轴**，用水平面去量只会量到它的**厚度**，

     于是把厚度缩放到目标值——整个物件被放大好几倍（剑雨第三版就是这么变的又大又不对）。

     竖向物件一律用 `FitLongestMeters`（三轴取最长）。
   - 每个素材首次生成时会打一条 `[RhodesDoctor][特效尺寸]` 日志，含**缩放前**的总尺寸和**每个渲染器的明细**

     （粒子/网格、各自尺寸、粒子的 `startSize`）。拿它对比"画面上实际多大"就能判断包围盒有没有被吹大。

本项目引用的是**未公开化**的 `Assembly-CSharp.dll`（`publicized_assemblies` 不存在时），只能调 public 成员；

internal/private 字段必须走反射（例：`BlueprintBuff.m_Flags` 是 protected 枚举，只能反射写）。

- ⚠️ **同一个法术里调两次 `AdditionalAbilityEffectRunActionOnClickedTarget`，第二次会被静默丢弃**：

  该方法走 `AddUniqueComponent(..., mergeBehavior = ComponentMerge.Fail)`，**同类型组件只允许一个**。

  第二次调用既不追加也不抛异常，只在蓝图校验时留一行（写在 `GameLogFull.txt`，`Player.log` 里看不到）：
  ```
  BlueprintCore.BlueprintConfigurator: <蓝图名> (BlueprintAbility) failed validation:
    * Multiple AdditionalAbilityEffectRunActionOnClickedTarget present but only one is allowed.
  ```
  剑雨的"金色拖尾"就这么从来没上过屏：编译通过、`Player.log` 无异常、画面上什么都没有；

  唯一痕迹是上面那行校验警告，外加 `[特效尺寸]` 日志里**只有剑、没有拖尾素材**。

  **要挂多个落点效果，就把它们放进同一个 `ActionsBuilder` 里 `Add` 多次。**

  排查同类"特效静默不见"时先 grep `failed validation`。

---

## 1. 手搓 Unit 蓝图必须设 `Skills`，否则 `SpawnUnit` 静默返回 null

- **症状**：`Game.Instance.EntityCreator.SpawnUnit(...)` 返回 null，调用方以为"刷不出来"。
- **真因**：`BlueprintUnit.Skills` 是**类**（`BlueprintUnit.UnitSkills`），默认 `null`。

  `UnitDescriptor.Initialize` 里直接 `unit.SetSkills(blueprint.Skills)`，`SetSkills` 一上来就读

  `skills.Acrobatics` → 抛 `NullReferenceException` → `UnitEntityView.CreateEntityData` 失败 →

  `SpawnUnit` 内层 catch 后返回 null。
- **日志特征**（`GameLogFull.txt`）：
  ```
  Object reference not set to an instance of an object
    at Kingmaker.UnitLogic.UnitDescriptor.SetSkills (BlueprintUnit+UnitSkills skills)
    at Kingmaker.UnitLogic.UnitDescriptor.Initialize (...)
    at Kingmaker.View.UnitEntityView.CreateEntityData ()
    at Kingmaker.Controllers.EntityCreationController.SpawnUnit(..., UnitEntityView, ...)
  ```
- **修法**：
  ```csharp
  UnitConfigurator.New(...)
    .SetSkills(new BlueprintUnit.UnitSkills())            // null → SetSkills NRE
    .SetFactionOverrides(new FactionOverrides())          // null → AttackFactions NRE（见下）
  ```
  本 mod 两个手搓单位都要设：`TestSetup`（测试木桩）、`SummonsSetup`（墨灵）。
- **同一类坑会连着撞**：补完 `Skills` 后，`UnitDescriptor.Initialize` 会继续往下走，撞到下一个

  `null` 字段。实测第二条：
  ```
  at Kingmaker.Blueprints.BlueprintUnit.get_AttackFactions ()
  at Kingmaker.UnitLogic.UnitDescriptor.Initialize (...)
  ```
  `BlueprintUnit.AttackFactions` 里 `FactionOverrides.AttackFactionsToAdd` 解引用 `FactionOverrides`，

  而它是**类**、默认 null（原版单位的 `FactionOverrides` 永远是 `{}` 空对象，不是 null）。

  修法：`.SetFactionOverrides(new FactionOverrides())`。

  排查方法：每改一版就重看 `GameLogFull.txt` 的最上面那行异常，按堆栈里的类型逐个补齐。
- **不是所有的都要给**：`StartingInventory / AdditionalTemplates / AddFacts` 返回的是

  `ReferenceArrayProxy`（结构体，内部对 null 数组安全），`AddFacts.EmptyIfNull()` 也做了保护，

  留 null 不会 NRE。真正会解引用的是 `Skills` 和 `FactionOverrides` 这类**类字段**。
- **还有两个类字段同理：`Visual` / `Body`**。`UnitVisualParams Visual` 默认 null，

  游戏在**被打中触发血迹/碎裂特效**、读 Bark（`UnitDescriptor` 里 `Blueprint.Visual.Barks`）、

  或 `PreloadResources()`（`Visual.ArmorFx.Preload()` 等）时都会解引用它；

  `Body` 虽默认 `new UnitBody()`（非空），但空手/手部设置与模板不一致。

  稳妥做法：直接照抄原版单位的这两个字段：
  ```csharp
  var tpl = BlueprintTool.Get<BlueprintUnit>("04944455200bc224d955a8e9bbd64f3f"); // SummonedAirElementalSmall
  var u = BlueprintTool.Get<BlueprintUnit>(targetGuid);
  u.Visual = tpl.Visual;   // 含 BloodType / 脚印 / 血渍·碎裂特效 / Barks
  u.Body = tpl.Body;
  ```
  见 `SummonsSetup.CopyTemplateVisuals`（木桩与墨灵都调用）。
- **经验**：凡是手搓 `BlueprintUnit`，逐个对照原版单位字段给上，缺哪个报错堆栈会告诉你是哪个类型；

  报错点常常离调用点很远（`CreateEntityData` 深处），别只看自己的调用。

  （非致命但会崩的字段不一定在生成时就炸，`Visual` 就是被打中才炸，要顺着原版字段补齐。）

## 2. 目标限制：`CanTarget*` 只管光标，真正拦截靠 `AbilityTargetIsAlly`

- `BlueprintAbility.CanTargetSelf / CanTargetFriends / CanTargetEnemies`

  **只影响光标图标和描述文本**，不是施放的硬闸。
- 真正决定能否施放的是 `AbilityData.CanTarget(TargetWrapper)`。它对"友军"这条**不读 `CanTargetFriends`**：
  ```csharp
  // AbilityTargetAnchor.Unit 分支
  if (!CanTargetFriends && !target.Unit.IsPlayerFaction && !Caster.Unit.CanAttack(target.Unit))
      return false;   // 友军 IsPlayerFaction=true → 条件为 false → 友军照样通过
  ```
  所以只设 `CanTargetFriends=false` 挡不住友军；`CanTargetSelf=false` 只挡自己

  （`if (!CanTargetSelf && target.Unit == Caster.Unit) return false;`）。
- 原版单目标有害法术（`Ill Omen`、`Implosion` 等）都**额外挂一件**：
  ```csharp
  using Kingmaker.UnitLogic.Abilities.Components.TargetCheckers; // AbilityTargetIsAlly
  ability.AddComponent<AbilityTargetIsAlly>(c => c.Not = true);
  ```
  `Not=true` 时要求 `!target.Unit.IsAlly(caster)`（自己/友军被拦，敌人/中立通过）。

  想"只能点敌人"就照抄这一件，别只改 `CanTarget*`。
- **落点施法的交互**：`AbilityTargetAnchor.Point`（点爆发/直线）时 `target.Unit` 常为 `null`，

  而 `AbilityTargetIsAlly` 对 null 目标返回 `true`。所以给范围法术加这个限制**不妨碍地面选点**，

  只在"直接点在某单位身上"时才拦。据此：`AoETarget=Enemy` 的范围可加，`Any/Ally` 的（烟幕/钙质化/

  儿时舞乐等照顾友军的）不加。
- 光标/优先级另有一套：`ClickWithSelectedAbilityHandler.GetPriority` 用

  `target.Unit.IsEnemy(caster)` + `CanTargetEnemies/CanTargetFriends` 决定指针状态；

  真正点下去施放走的是 `AbilityData.CanTarget` + `Blueprint.TargetRestrictions`。

## 3. 传送/落点：用 `FreePlaceSelector`，别写死世界坐标

- 写死偏移（如 `caster.Position + new Vector3(2,0,2)`）方向恒定，屏幕上永远同一角落，

  还可能落进墙里或与单位重叠。
- 找空格用引擎自带的 `Kingmaker.View.FreePlaceSelector.RelaxPoints`：
  ```csharp
  public static void RelaxPoints(Vector3[] pointsArray, float[] radiusArray, int pointsCount,
                                 Vector3? rootPoint = null)
  ```
  它把落点推离附近单位（按各自 `Corpulence`）并吸附到导航网格；**只改 x/z，y 保持不变**。
- 落点先贴地：`UnitEntityData.Translocate(Vector3 position, float? orientation)` 会设置位置、

  让视图贴地，并置 `CombatState.PreventAttacksOfOpporunityNextFrame = true`（传送不引发借机攻击）。
- 方向类需求用 `UnitEntityData.OrientationDirection`（朝向，水平向量）；取负即"身后"。
- 距离换算：`Kingmaker.Utility.Feet`，`10 尺 ≈ 10 * 0.3048 m`。

## 4. 生成单位的其他注意

- 刷怪放进 `ContextData<UnitSpawningData>.Request()` 里，可 `SetRestrictVariations(true)`

  （蓝图没挂 `CustomizationPreset` 时本来也会跳过变体选择）。`ContextData<T>` 在

  `Kingmaker.ElementsSystem`，`UnitSpawningData` 在 `Kingmaker.View.Spawners`。
- `SpawnUnit` 内部可能把真实外观换&#x6210;**"优化占位模型"**：`ShouldSpawnOptimizedDummy`

  在落点位于当前区域 `MechanicBounds` 之外时返回 true。判据：生成后 `unit.View.IsOptimizationDummy`（public 字段）。

  落点先贴导航网格可避免。
- 蓝图 `Prefab` 是 AssetId（`UnitViewLink`）；`UnitConfigurator.SetPrefab(string)` 内部即

  `new UnitViewLink { AssetId = ... }`，映射到原版 JSON 的 `"Prefab": {"AssetId": "..."}`。
- 单位是**延迟入状态**的：`SpawnUnit` 只入队，`EntityCreationController.Tick` 才 `AddEntityData`。

  返回后立刻读部分属性可能尚未就绪；必要时下一帧再改。生成后立即显式写一次 `unit.Position` 是安全的兜底。

## 5. 编译 / 部署

> ⚠️⚠️ **改完代码 ≠ 生效**：`dotnet build` 只是"验证能编译"，游戏里跑的还是旧 DLL。
> **每次改完必须"编译 + 部署 + 重启游戏"三件套**，否则测的是旧版本——本 mod 为此白白浪费过好几轮
> （代码改了、"编译 0 错误"、进游戏却"一点都看不见"，查了半天，根因是**根本没部署**）。
> 排查这类"看不见"时，**第一步先确认装的是哪一版**：比对
> `Mods\RhodesDoctor\RhodesDoctor.dll` 与 `bin\Debug\net48\RhodesDoctor.dll` 的时间戳 / 内容
> （`RhodesDoctor.dll.bak-<时间戳>` 是历次部署留下的备份，看最后一个就知道上次装到什么时候）。

- 编译：`dotnet build -p:WrathPath="…\Pathfinder Second Adventure"`（csproj 也认环境变量 `WOTR_PATH`）。
- 部署：`bash tools/deploy.sh`（默认游戏路径 `/d/steam/steamapps/common/Pathfinder Second Adventure`，
  也可传参）。它会先备份旧 DLL、再把 `bin\Debug\net48\RhodesDoctor.dll`（和 `.pdb`）覆盖过去。
  手动也行：直接覆盖到 `…\Pathfinder Second Adventure\Mods\RhodesDoctor\`。
  **游戏运行时 DLL 被占用，必须先关掉游戏；覆盖后必须重启游戏**才会加载新版本。
- 改过任何 `L10n.S("字面键", …)` 之后要重新生成本地化包；仅改代码逻辑不用。
- **在 DSH 沙箱会话里编译**：本工作区的既有子目录（`obj` / `bin` / `src` …）对沙箱进程**不可写**

  （根目录可写、会话里新建的目录也可写，但既有子目录的 ACL 挡住了），

  `dotnet build` 会报 `CS2012: 无法打开 …obj\Debug\net48\RhodesDoctor.dll 进行写入`。

  绕法是把中间/输出目录改到工作区内可写的新目录，并关掉程序集信息生成

  （否则残留在 `obj\` 里的旧 `AssemblyInfo.cs` 会被默认通配符再编译一次，报 `CS0579 特性重复`）：
  ```
  dotnet build -p:WrathPath="…\Pathfinder Second Adventure" \
    -p:BaseIntermediateOutputPath="<工作区>\_build\obj\" \
    -p:BaseOutputPath="<工作区>\_build\bin\" \
    -p:GenerateAssemblyInfo=false -p:GenerateTargetFrameworkAttribute=false
  ```
  这样只**验证编译**（产物在 `_build\bin\…`，且缺程序集元数据）；

  **要真正部署，请在沙箱外正常 `dotnet build`**，产物仍在 `bin\Debug\net48\`。

  （`-p:` 里的分号在 PowerShell 下要写成 `%3B`，否则 MSBuild 会当成属性分隔符报 `MSB1006`。）

---

## 6. 本 mod 相关的实现决策（与规则文档的偏差）

- **紧急撤离**：规则写"目标移动至 10 尺内可见的空格"，但无 UI 让玩家自选方向；实现取

  **永远丢到施法者身后**（朝向反方向约 5 尺）再贴最近空地。偏差已记录，如需改距离/方向改

  `Actions.ContextActionTeleportAllyToCaster`。
- **纯敌方指令的目标限制**：单体 + `AoETarget=Enemy` 范围 + 直线 + 集火，统一挂

  `AbilityTargetIsAlly{Not=true}`；`AoETarget=Any/Ally` 的范围法术不加。见 `SpellsSetup.CreateSpell`、

  `ArchetypeSetup`（集火）。

---

## 7. 做一套法术特效的完整经验（以"剑雨"为例，2026-10-09 定案）

一节可照抄的流程。剑雨从"借素材"一路踩到"自绘"，把中间所有分叉都走了一遍，

**下次做别的法术特效先读这一节**，能省掉整轮返工。相关代码：`src/FxBorrow.cs`（借引用）、

`src/FxTint.cs`（实例化 + 重染 + 尺寸）、`src/SwordStreakFx.cs`（自绘光带 + 材质来源）、

`src/SwordHitFx.cs`（自绘命中光斑）、`src/Actions.cs`（`ContextActionSpawnFxRing` / `ContextActionSwordHitFx`）。

### 7.1 第一步：先决定素材从哪来（选错来源 = 整轮返工）

| 要的效果                      | 正确来源                                                                                                | 别用什么                                                                                                               |
| ------------------------- | --------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| **整片爆发**（火球炸开、电光四散）       | 借原版法术的 `AbilitySpawnFx` 或区域的 `Fx`，见 `FxLibrary` / `FxBorrow`                                        | ——                                                                                                                 |
| **单个"物件"**（剑、石头、柱子）       | `BlueprintWeaponType.VisualParameters` 里的武器**网格**（反射读私有 `m_WeaponModel`），见 `FxBorrow.WeaponModelFx` | ❌ 投射物 `View`（借到的是**飞行拖尾**，不是物件形状）<br />❌ `Shape=Wall` 的区域 Fx（那是**一条直线墙**，拼不成圆）                                     |
| **细长/很小的装饰**（光尾、光点、火花、光圈） | **自己画**：`TrailRenderer`（`SwordStreakFx`）/ `LineRenderer`（`BombFx`）/ 自建面片（`SwordHitFx`）              | ❌ **借任何粒子素材**：粒子可见体积**不跟根节点缩放走**，缩不小（实证：剑雨拖尾素材 345.6×62.3×343.8 m、约 40 个粒子系统，`FitToMeters=1.4` 缩完仍是一根橙色大光柱，用户截图实证） |

尺寸换算两条：**`Shape=Cylinder` 区域的 `Size` 是半径（尺）**，据此算 `scale` 最可靠；

**竖着的物件（剑/柱）用 `FitLongestMeters`**，不能用 `FitToMeters`（后者只量水平面，

会只量到剑身厚度 → 把整把剑放大好几倍）。另外 **`Renderer.bounds` 别拿来反推尺寸**

（粒子系统的 bounds 比可见粒子大一个数量级：实测 348 m vs 可见约 25 m）。

### 7.2 自绘特效的三件套（缺一个就会"什么都看不见"）

1. **材质：克隆原版 FX 材质，不要 `Shader.Find`**。

   `Shader.Find("Legacy Shaders/Particles/Alpha Blended")` 自建材质**两次实测画面全空**

   （shader 找得到、材质生成正常、宽度够，就是不画）；`new Material(克隆自原版 FX 的材质)`

   （混合模式/贴图/关键字一起继承）**立刻就可见**。✅ **定论（2026-10-09）**：

   本 mod 自建材质一律走 `SwordStreakFx.MaterialFor(color)` 这条路，`Shader.Find` 只作兜底。

   判断"能不能渲染"的标准是**用户是否亲眼见过它上屏**，不是"编译通过/日志正常"。
2. **缩放归一**：`TrailRenderer.startWidth` / `LineRenderer.widthMultiplier` 是**局部**单位，

   会再乘一次宿主 `lossyScale`。挂一个子物件、`localScale = 1/lossyScale`，传入的米数才是画面上的米数。

   （剑雨那把剑实测 `lossyScale=0.7416` 属正常值，所以这**不是**它当时看不见的原因 —— 材质才是。

   但归一化仍然要做，它消除一整类不确定性。）
3. **`layer` 跟宿主走**（新建 GameObject 默认在 Default 层，原版特效未必在那层）。

### 7.3 摆位 / 形态：`ContextActionSpawnFxRing` 字段速查

一个动作就能摆出"一圈/多圈/随机散布 + 从天而降 + 光尾"：

| 字段                                                     | 作用                                                     |
| ------------------------------------------------------ | ------------------------------------------------------ |
| `Count` / `Radius`                                     | 数量 / 最外半径（米）                                           |
| `Rings`                                                | **同心环数量**（默认 1）。>1 时把 `Count` 均分到各环、半径等分，**相邻环错开半个角步** |
| `RadiusJitter`                                         | 0 = 正好贴在环上（整齐）；1 = 在半径内圆盘里随机（乱）                        |
| `TiltJitter` / `YawJitter`                             | 俯仰+侧倾 / 朝向的随机器（度）。**0 = 笔直竖立、朝向一致**                    |
| `ScaleJitter` / `OffsetUpJitter`                       | 大小 / 插地深度的随机比例                                         |
| `Pitch`                                                | 基准俯仰：`180` = 模型倒过来（剑尖朝下）                               |
| `FallFromMeters` / `FallSeconds` / `FallSecondsJitter` | 从多高、多久落下、先后差（0 = **同时落地**）                             |
| `GoldenStreak` / `StreakSeconds` / `StreakWidth`       | 自绘金色光带（宽度是**真实米数**，见 7.2）                              |
| `TrailLink` + `TrailFit*`                              | 借现成粒子拖尾（**剑雨已弃用**，留给确实需要粒子拖尾的场合）                       |
| `Lifetime`                                             | 落地后留多久（剑雨定案 **2 s**）                                   |

### 7.4 时间轴：三件事必须对齐（最容易做的观感 bug）

伤害是**施法瞬间**就结算的，特效却常常在时间上分叉，一定要一起改：

1. **伤害 vs 落地**：`FallSeconds` 别为了"看得清"拉长。剑雨原本 16 m / 1.1 s，结果"伤害数字先跳、

   剑还在天上"（用户实证）；现为 **9 m / 0.4 s**，数字一跳剑就砸到地上。
2. **命中特效 vs 落地**：命中金光带 `Delay`，**值必须等于 `FallSeconds`**（剑雨 0.4 s），

   否则又变成"光先闪、剑还在天上"。等待期间要**关掉 Renderer**，不是把 alpha 调 0

   （万一是 opaque 材质，alpha 不起作用，会看见一个白方块）。
3. **留存 vs 手感**：`Lifetime` 是纯主观项，剑雨被用户连着砍过两次（6 → 4 → **2 s**）。

### 7.5 逐目标的"受击特效"

- ⚠️ **原版的受击反馈不可依赖**：只靠原版那套（血液/火花），会**时不时什么都不出**

  （受伤害类型、单位血型、是否 AoE、单位状态影响，条件不透明），表现为"受到伤害但没触发特效，很随机"。
- ✅ 做法：**自绘特效动作挂进和伤害同一个动作列表** —— `SavingThrow` 的 `failed` / `succeed` 两支里

  与 `DealDamage` 并列（`ContextActionSwordHitFx`），这样"伤害结算了"和"特效放了"是同一个条件。
- ℹ️ 逐目标动作里 **`Target.Unit` 就是当前被打中的那个单位**（`AbilityEffectRunAction` 的动作列表

  **每目标跑一遍**，`ContextActionEarthFuryHit` / `ContextActionSwordHitFx` 都靠这个）。

  要拿"被点击的落点"用 `Context.MainTarget.Point`，两者别混。

### 7.6 "看不见特效"的排查顺序（按命中率从高到低）

1. **材质来源**（第 1 嫌疑，见 7.2①②）：日志里 `金光尾拖使用内置 shader` = 走错路了，

   应为 `金光尾拖使用原版克隆材质`。
2. **组件被静默丢弃**：`AdditionalAbilityEffectRunActionOnClickedTarget` 走 `AddUniqueComponent`

   （`ComponentMerge.Fail`），**同类型只允许一个**，第二次调用不追加也不报错

   （`GameLogFull.txt` 里只有一行 "Multiple … present but only one is allowed."）。

   要挂多个落点效果 → 放进**同一个 `ActionsBuilder`** 里 `Add` 多次。
3. **尺寸/缩放**（第 7.1 节：粒子缩不小、竖直物件用 `FitLongestMeters`、`bounds` 不可信）。
4. **时间**（7.4：延迟拉太长/`Lifetime` 到期/等待期间没关 Renderer）。
5. **`Context.DisableFx`**：所有自绘特效动作开头都 `if (Context.DisableFx) return;`，

   它在某些游戏状态下为 true，特效会被整体跳过（查日志时记得排除这一条）。


### 7.7 群体"友军增益光环"：借原版增益的 `FxOnStart`（D 类 11 个，2026-10-09）

适用范围：**群加增益**（30 尺内全体友军 +X）想加"身上一直亮着"的光，而不是施法瞬间炸一下。

- 挂法：给**增益蓝图**挂 `FxOnStart`（常驻循环光，只在持有者身上显示、增益消失即灭）。

  BlueprintCore 侧就是 `BuffConfigurator.SetFxOnStart(prefabLink)`；已挂过的样板见信标那次。
- 素材：借原版某个增益的 `FxOnStart`（`FxBorrow.BuffAura("buff 的 GUID")`，按顺序取第一个非 null 的）。

  **别写死 AssetId**；判断"这个 buff 有没有 FX"用下面的离线扫描，不要一个个进游戏试。
- **一个素材点亮一整批**：D 类 11 个法术共用 `BuffRefs.AngelHaloBuff`（金色光环，克制）。

  想换观感只改 `FxLibrary.AuraFor` 里那一行；备选（同一次扫描出来的）：`AngelBarrier_Buff`（罩子）、

  `AngelWingsBuff`（翅膀，偏华丽）、`AngelWardFromHarmBuff`（罩子）。
- **接线只要两处**（这就是它性价比高的原因）：`SpellsSetup.CreateSpellBuff`（`Kind.Buff`/`Kind.Debuff`

  都走这里）与 `SpellsSetup.CreateExtraBuff`（各法术在 `ApplySpecial` 里自建的增益，靠

  `name.StartsWith(s.Key)` 认出属于哪个法术）。新加"要光环的友军增益"= 往 `FxLibrary.AuraFor`

  的名单里加一个 key，**不用动别的**。
- ⚠️ **不是所有群加都有增益可挂**：重构（`Reconstruction`）是 `Kind.Special`，只跑

  `ContextActionMassRestore`、不挂任何增益 → 没有 `FxOnStart`。这类改走"每个受益者身上炸一下

  **自绘**金光"（`ContextActionMassRestore.GoldFlash` + `SwordHitFx`），尺寸自己可控，不借粒子素材。

  做别的批次前先看一眼法术是 `Kind.Buff` 还是 `Kind.Special`。
- ⚠️ **多光环叠加**：每个增益各挂一份，同一友军身上有 N 个增益就有 N 道光（会偏亮）。

  要缓解就分批用不同素材，或只给高环法术挂。

**变体：要"改色"或"跟单位走"时，别用 `FxOnStart`，用运行时克隆重染**（骑兵突进 2026-10-09 的样板）：

- `FxOnStart` 是引擎直接播放的 `PrefabLink`，**没法改色**。要蓝色/别的颜色，就得走

  `FxTint` 那套"克隆 + 重染"（`docs/fx-recolor.md`），原版特效不碰。
- 要"跟着单位移动"（拖尾/残影），把克隆**挂到单位 View 下**：`FxTint.SpawnAttached(link, unit.View.transform, rgb, lift)`。
- 生命周期用 `IUnitBuffHandler`（`HandleBuffDidAdded` / `HandleBuffDidRemoved`）按 buff 蓝图 GUID 过滤，

  增益加上 → 生成，移除 → 销毁。样板见 `src/HasteTrailFx.cs`（骑兵突进借 `HasteBuff` 的 `FxOnStart` 染蓝）。
- ⚠️ 这样做的法术**不要**再往 `FxLibrary.AuraSpells` 名单里加（否则金色光环 + 蓝拖尾叠着）。

**根因定位（2026-10-09，硬证据）**：

- E1：`blueprints.zip` 里 `Spells/Level3/HasteBuff.jbp` 与 `Buffs/Haste.jbp` 的 `FxOnStart.AssetId`

  都是 `91ef30ab58fa0d3449d4d2ccc20cb0f8`——与我们借的是同一份 prefab。
- E2：克隆染色管线端到端通：3 粒子系统 + 6 材质槽全枚举，贴图全可读、零 `染色失败`，周身变蓝；

  `[染色复核]` 证明拖尾双通道染后全蓝（首键 HDR 蓝），写入生效。
- E3：顶点色假说已死：`WhitenMeshes` 随新版跑了一轮，零 `[顶点漂白]`——prefab 里没有偏离白的顶点色。
- E4：原版加速术叠加假说已死：用户确认只上了一个增益。
- 拖尾槽替换实验（2026-10-09）：把 `Trail_cycle` 的 `trailMaterial` 换成自有蓝光材质后，

  屏上出现蓝色光漩（拖尾槽听换装实锤），而绿条原样存在。

  用户判别：增益消失绿条即消失（"不在"）→绿条由本增益门控，结合换装实验证明缎带渲染可控，

  收敛到拖尾槽原烟材质链路。现改拖尾槽**指向同一渲染器上已染蓝的身体材质**

  （柔光质感、与周身统一；身体/火花不动，不会再出光漩）。若仍绿，则绿条彻底排除本 prefab。
- E5（2026-10-09 用户截图复核）：**"提亮版" `TrailColor=(0.45,0.80,1.60)` 实测人物底部是青色**。

  对截图逐像素采样：底部拖尾 ≈ RGB(76,98,96)、H≈180、S≈0.22、V≈0.38（**G≈B、R 最低**＝青）；

  同一时刻周身那圈是 H≈235–290 的蓝紫。即：**同一个 tint 渲出了两种色相**，说明结果不是"tint 直接上屏"，

  而是 `tint × 内容`——底部那截内容偏暖。
- E6（成因推断，与日志一致）：prefab 里 `SparksForTrails` / `Sparks_Body (2)` 的

  `startColor=(1.00,0.949,0.396)` 是**金黄**，`Trail_cycle` 的烟贴图偏白。把实测青色反解

  （`内容 ≈ 观测 / tint`）得到 ≈(1.40,1.00,0.50)＝**暖色**，与"金黄内容没被完全染色"吻合；

  而上版 tint 的 **G/B 已经到 0.5（色相 222°）**，暖色 × 这个偏绿的蓝正好落在青（≈180°）。
- E7（第一次处置）：`TrailColor` 改 **(0.18,0.30,1.40)**——G 从 0.80 砍到 0.30、R 从 0.45 砍到 0.18，

  色相抬到 ≈234°（蓝）；B 从 1.60 降到 1.40，避免 HDR 蓝过曝后向白/青偏。

  按 E6 的模型反推，同一份暖色内容下结果应落在 ≈234°，即蓝。

  ✅ **2026-10-09 进游戏确认：青→蓝，用户认可**（"对位，你简直是天才"）。

  结论：E6 的"暖色内容 × 偏绿蓝 = 青"模型成立，`TrailColor` 的 **G/B 比**就是这条链路的实际控制量。
- E8（第二次处置，"改浅"）：用户要求"周身淡一点、但醒目程度不变"。

  `TrailColor` → **(0.40,0.50,1.60)**：色相 235°（与 E7 同族，不动），饱和度 0.87→0.75（更淡），

  **B 1.40→1.60** 用 HDR 把亮度补回来（醒目度不掉）。

  ⚠️ **"改浅"必须 R/G 同比抬，只抬 G 就会退回 E5 的偏青**——这条写进 `HasteTrailFx` 注释了。

  微调方向：更淡就同比抬 R/G（如 `(0.50,0.60,1.70)`），更艳就往回收（如 `(0.30,0.40,1.55)`）。
- E9（E8 失败 + 量出真正的映射，2026-10-09）：E8 的 `(0.40,0.50,1.60)` 进游戏**反而更浓**

  （用户："还是太深了。太丑"）。对截图采样，最显眼的蓝像素平均 **RGB(26,27,255)、H=240、S=0.90、V=1.00**

  ——**纯电光蓝，蓝通道已经过曝钳到 255**。E8 把 B 从 1.40 抬到 1.60 正是罪魁：抬 B 先撞 255 钳位，

  饱和度被推到 0.9；R/G 又还很低，所以越"提亮"越像电光蓝。
- E10（**映射关系：屏幕色 ≈ tint 的 1.8 次幂**，实测拟合）：

  同一元素上，tint 归一 `(0.250,0.312,1.0)` → 观测归一 `(0.102,0.106,1.0)`，

  解出指数 k≈1.65~1.93（取 1.8）。物理上说得通：顶点色 × 材质 `_Color` × 贴图，tint 被乘了多次。

  **所以"想浅"只有一条路：把 R/G 大幅抬起来往白里走，绝不能靠抬 B。**
- E11（第三次处置，定案）：`TrailColor` → **(0.85,0.88,1.25)**。R 0.40→0.85、G 0.50→0.88，

  **B 反而 1.60→1.25**（不撞钳位）。按 ^1.8 反推屏幕色 ≈ `(0.50,0.53,1.00)` ≈ RGB(127,136,255)：

  饱和度 0.90→0.50（明显变浅），色相 236°（仍在蓝区，远离 222° 那条发青线），

  B 仍 >1 所以有柔光、醒目度基本保住。

  微调方向：更浅就继续抬 R/G（`(0.92,0.94,1.25)`），更艳就往回收（`(0.60,0.66,1.30)`）。

  ⚠️ 教训一句话：**这条链路调"明暗/浓淡"用 R/G，调"色相"才用 G/B 比；抬 B 只会过曝变电光色。**
- E12（拆色，2026-10-09 定案）：E11 的淡蓝让**周身**很好看，但用户发现**底部跟着变深**

  （"为什么底部的又变回来了？是不是因为他们是关联的？"）——**判断正确**：

  整份 prefab 只有一个 `TrailColor`，`FxTint.SpawnAttached` 一把梭刷到全部粒子和材质上，

  周身与底部永远同进同退；而底部那条地面拖尾的原始内容更实/更暗，同一个 tint 乘上去就是深一档。
  - **拆法**：新增 `FxTint.ApplyTo(go, rgb, nameContains)` —— 在 `Apply` 之后**按粒子系统名字点染**一部分

    （粒子 `main.startColor` / `colorOverLifetime` / `colorBySpeed` / `trails.*` 全通道 + 同名渲染器的

    `material` / `trailMaterial`）。名字是不区分大小写的包含匹配。
  - **名字怎么定的**：`Player.log` 的 `[染色诊断]` 里**只有 `Trail_cycle` 的 `trails.enabled=True`**

    （`SparksForTrails` / `Sparks_Body (2)` 都是 False），所以地面那条缎带 = `Trail_cycle`。

    常量 `HasteTrailFx.TrailSystemName = "Trail_cycle"`，原版若改名只改这一行。
  - **两个颜色**：`BodyColor=(0.85,0.88,1.25)`（周身，E11 定案不动）、

    `TrailColor=(0.95,0.97,1.32)`（底部，更白更淡把深的那截顶起来）。此后各调各的，互不牵连。
  - 诊断：日志会打 `特效点染：粒子 'Trail_cycle' → RGBA(...)`，据此确认点染命中了哪个系统。

    若哪天"点染"打不出来，说明原版 prefab 的粒子改名了，改 `TrailSystemName` 即可。
- E13（**底部走的是另一套映射**，2026-10-09 定案）：E12 把底部调白 → 用户截图显示底部

  **变成橄榄黄绿**（把截图饱和度拉高 2.6× 能清楚看到一条横贯地面的暖色带）。

  把五版数据全代进去，底部符合 **屏幕色 ≈ tint × 暖色内容 K**，K≈(1.40, 1.00, 0.50)：
  | 版本        | tint             | 预测 = tint×K         | 实际       |
  | --------- | ---------------- | ------------------- | -------- |
  | ①         | (0.45,0.80,1.60) | (0.63,0.80,0.80) 青  | 青 ✓      |
  | ②         | (0.18,0.30,1.40) | (0.25,0.30,0.70) 蓝  | 蓝 ✓      |
  | ③         | (0.40,0.50,1.60) | (0.56,0.50,0.80) 蓝紫 | 蓝 ✓      |
  | ④         | (0.85,0.88,1.25) | (1.19,0.88,0.63) 橙  | "变回来了" ✓ |
  | ⑤         | (0.95,0.97,1.32) | (1.33,0.97,0.66) 橙  | 橄榄黄绿 ✓   |
  | **五发全中。** |                  |                     |          |
- E14（结论，写进 `HasteTrailFx` 注释）：
  - **周身（火花）= tint^1.8** → 想浅就**抬 R/G**；
  - **底部（缎带）= tint × 暖色** → 想蓝就**压低 R/G**（给更强的蓝）。

    **两边方向相反，这就是必须拆成两个常量的根本原因。**

    定案 `BodyColor=(0.85,0.88,1.25)`、`TrailColor=(0.36,0.54,1.80)`

    （反推底部屏幕色 ≈ (0.50,0.54,0.90) → 235° 蓝、S 0.44、V 0.90，与周身同档观感）。

    调底部的口诀：**只动 R/G 调蓝的强弱**；还偏暖就再压 R/G，太蓝就往回收。
- E15（**点染瞄错了对象**，2026-10-09）：E12 按"谁开了 `trails`"去猜地面拖尾，选了 `Trail_cycle`。

  进游戏日志显示点染**确实命中**了 `Trail_cycle`（`特效点染：粒子 'Trail_cycle' → ...`），

  但**地面那条纹丝不动**，反倒是**周身变浓了**（用户："刚才的周身特效更好看"）。

  ⇒ **`Trail_cycle` 其实是"周身那圈缎带"，不是地面拖尾。**

  正确的那个是 **`SparksForTrails`**：
  - 名字直译"给拖尾用的火花"；
  - `Player.log` 里它是**持续发射、`bursts=0`**（`Sparks_Body (2)` 是 `bursts=1` 的周身爆发）；
  - 内容是**金黄**（`startColor=(1.00,0.949,0.396)`），所以乘上淡蓝 `BodyColor` 会变成**橄榄黄绿**

    ——正是用户看到的地面那条。

    定案：`TrailSystemName = "SparksForTrails"`、`TrailColor=(0.50,0.56,2.00)`

    （反推屏幕色 ≈ (0.50,0.53,0.79) → 234° 蓝、S 0.37、V 0.79）。

    B 给到 2.0 是**故意**的：它正好被暖色的低 B 抵消，屏幕上的蓝不会钳到 1.0，

    所以不会重演周身那种"电光蓝"。

    ⚠️ **教训**：`ApplyTo` 的点染"命中日志"只证明名字匹配上了，**不证明改对了部件**；

    换部件时务必让用户看一眼，或先只改一个部件看画面哪里动了。
- E16（**定案：反过来点染，不再猜部件**，2026-10-09）：E15 把强蓝送给 `SparksForTrails` 后，

  日志显示点染**又命中了**，但地面那条**还是橄榄绿**（用户："你这对吗？之前是怎么改对的？"）。

  ⇒ 三个粒子系统里，`Trail_cycle` 和 `SparksForTrails` 都**不是**地面那条。

  **但不必再猜第三个**——换思路：
  - **之前能改对，是因为当时整份 prefab 一起染**（v2 `(0.18,0.30,1.40)` 刷全部 → 地面自然蓝）。

    拆开时把强蓝从地面那边拿走了，才露出暖色。
  - 所以正确结构是 **"一份强蓝打底 + 只把已确认的周身缎带点染成淡蓝"**，

    而不是"把强蓝送给某个猜出来的系统"。这样**无论地面是哪一个系统**都会变蓝。
  - 定案：`BaseColor=(0.18,0.30,1.40)`（全局打底，管地面与其余部件）
    - `RibbonColor=(0.85,0.88,1.25)`（点染 `Trail_cycle` = 周身缎带，唯一已确认归属的部件）。

      ⚠️ **方法论**：部件归属**没有把握时，不要"挑一个改"，要"改全局 + 把已确认的那个挑出来特殊处理"**——

      后者对"不知道是哪个"是免疫的。

**离线工具（本仓库新增）**：

```
python tools/scan_buff_fx_all.py [关键字...]     # 全库：BlueprintBuff 且 FxOnStart != null（含 buff 的 GUID）
python tools/scan_buff_fx.py [关键字...]         # 只看 Buffs/ 目录，轻量版
```

输出 `路径 | fx素材AssetId | buff的GUID | 名字`，**第三列就是 `FxBorrow.BuffAura` 要的参数**。

参数是关键字过滤（不传则打全部）。用 `.` 当关键字可以打印全部。

实测规模：全库 252,498 条蓝图、其中 path 带 buff 的 8,566 条 → 396 个 buff 有 `FxOnStart`

（原版**大量** buff 这字段是空的，所以必须先筛再选）。

### 7.8 已被用户否掉的方案（**别再改回去**）

- ❌ 剑身"圆盘内随机散布 + 横七竖八"（用户："别让它们歪歪扭扭的了"）→ 现为**同心双环、笔直竖立**。
- ❌ 借粒子素材当光尾 → 画面上一根橙色大柱子（用户截图实证）。
- ❌ `Shader.Find` 自建材质 → 两次全空。
- ❌ 慢速下落（1.1 s）→ "伤害数字先跳、剑还没落地"。
- ❌ 剑留存 6 s / 4 s → 太长，定案 **2 s**。

### 7.9 剑雨当前定案参数（照抄用）

`Count=18`、`Rings=2`、`Radius=2.4`（内环 1.2）、`RadiusJitter=0`、`TiltJitter=0`、`YawJitter=0`、

`ScaleJitter=0`、`OffsetUp=1.2`、`OffsetUpJitter=0`、`Pitch=180`、`FitLongestMeters=1.5`、

`FallFromMeters=9`、`FallSeconds=0.4`、`FallSecondsJitter=0`、`Lifetime=2`、

`GoldenStreak=true`、`StreakSeconds=0.25`、`StreakWidth=0.15`、`Tint=SwordTint`（橙金）；

命中特效：`ContextActionSwordHitFx{ Tint=SwordTint, Size=1.2, Delay=0.4 }`。

### 7.10 运行时"改色"的方法论（骑兵突进，2026-10-09 定案，**做下一个改色法术前先读这节**）

骑兵突进从"青"折腾到"终于对了"共走了 7 版。弯路本身比结论更值钱，下面是可复用的部分。

代码样板：`src/HasteTrailFx.cs` + `src/FxTint.cs`（`SpawnAttached` 整份染 / `ApplyTo` 点名点染）。

逐条原始证据见 §7.7 的 E5–E16。

#### 7.10.1 第一原则：**屏幕色 ≠ 你给的颜色**

`FxTint` 把 tint 刷到**顶点色、材质 `_Color`、贴图**三处，渲染时它们是**相乘**的，

再叠上混合模式、地面底色与 HDR 钳位。所以**别指望"填什么就是什么"**——

必须把"填进去的值"和"屏幕上量到的值"当成两个东西，先测出映射再调。

**怎么测（照抄这套流程，十分钟出结论）**：

1. 让用户给一张进游戏截图；
2. 用 PIL 逐像素取 RGB→HSV，**只统计高饱和像素**，按色相分桶；
3. 把"观测归一化"和"tint 归一化"放一起拟合幂次或系数（见下）；
4. 用拟合出的映射**反推**目标色，再改常量。

⚠️ 分析截图时**先分清哪些是游戏 UI**：范围指示器的绿、选中轮廓、技能栏图标都会被算进统计。

判别法：UI 的颜色**极纯**（如指示器绿恒为 `RGB(61,210,61)`、H=120 且聚集在一小块），

特效的颜色**随地面混色、分布成片**。

#### 7.10.2 同一个 prefab 里，**不同部件可能是相反的映射**

骑兵突进借的加速术 prefab 里有三套粒子系统，实测两套映射**方向相反**：

| 部件                 | 映射                                       | 想"更浅/更亮"        | 想"更蓝"      |
| ------------------ | ---------------------------------------- | --------------- | ---------- |
| 周身缎带 `Trail_cycle` | 屏幕色 ≈ **tint 的 1.8 次幂**（乘了多次）            | **抬 R/G**（往白走）  | 别单独抬 B     |
| 地面拖尾（另一套火花）        | 屏幕色 ≈ **tint × 暖色内容 K≈(1.40,1.00,0.50)** | 抬白**反而会露出原版暖色** | **给它更强的蓝** |

- 幂次那条的拟合数据：tint 归一 `(0.250,0.312,1.0)` → 观测归一 `(0.102,0.106,1.0)`。
- **"抬 B 提亮"是陷阱**：B 一过 1 就先撞 255 钳位，R/G 还低 → 渲成 **H=240 / S=0.90** 的"电光蓝"，

  比原来更冲（v3 就是这么翻车的）。
- **暖色内容那条要反着来**：内容偏金黄时，tint 越白越压不住它，原版橙黄/橄榄就透出来

  （v4/v5 的"底部的又变回来了"）。

#### 7.10.3 部件归属没把握时：**别挑一个改，改全局 + 把已确认的挑出来**

这是这次最贵的教训。为了"只把周身调浅"，必须把 prefab 拆成"两部分分别染色"，

于是得回答"哪个粒子系统是地面拖尾"——**猜名字连着错了两版**：

先按"谁开了 `trails` 字段"猜 `Trail_cycle`（错，它是周身），再按名字直译猜 `SparksForTrails`（也错）。

两次点染日志**都命中**、画面**都没动**。

⚠️ **`ApplyTo` 打出"特效点染：粒子 'XXX'"只证明名字匹配上了，不证明改对了部件。**

（判定"改对了"的唯一标准是**用户看到画面对应位置变了**，和 §7.2 那条"能不能渲染看用户是否见过"同源。）

**正确姿势**：

```
底色 = 那个"已知能整体变蓝"的强色   → SpawnAttached 刷全部（地面/未知部件必然吃到）
点名 = 只对"唯一已确认归属"的部件做特殊处理 → ApplyTo（这里是周身缎带）
```

这样对"不知道地面是哪个系统"是**免疫**的。反过来说：

**只要"整份一起染"能达到目标，就不要拆**——骑兵突进早期能改对，恰恰因为当时没拆。

#### 7.10.4 定案值（照抄用）

```csharp
// HasteTrailFx
BaseColor   = (0.18, 0.30, 1.40)   // 全局打底：地面拖尾 + 其余部件（色相 234° 的强蓝）
RibbonColor = (0.85, 0.88, 1.25)   // 只点染 Trail_cycle（周身缎带）：淡蓝，^1.8 反推屏幕 ≈ (0.50,0.53,1.00)
RibbonSystemName = "Trail_cycle"   // 唯一已确认归属的部件名
```

微调口诀：**周身想浅 → 抬 R/G；地面想蓝 → 压 R/G；两边都别单独抬 B。**

诊断：日志 `特效点染：粒子 'Trail_cycle' → ...` = 点染链路活着（**不代表改对了部件**）。

#### 7.10.5 ⚠ "素材本身就是一盏灯"时别删灯（医疗无人机·光亮术，2026-10-10）

**症状**：借原版**光亮术**（`MageLightBuff` 的 `FxOnStart`）做"跟着单位的小光点"，日志显示
`特效借用成功（增益）：571baa4c…` + `…光点已生成。`，**但画面上什么都没有**。

**根因**：`FxTint.Apply` 结尾有一句
`foreach (Light l in go.GetComponentsInChildren<Light>(true)) Destroy(l);`
—— 默认删灯（防止残留原色的光）。而**光亮术的素材主要就是一盏 Unity `Light`**：灯被删了 → 什么都不剩。

**修法**：给 `Apply` / `SpawnAttached` 加 `keepLights` 开关（默认 `false`，保持旧行为）。
`keepLights: true` 时**保留灯、`l.color = rgb` 染成目标色，并且——关键——若 `l.intensity <= 0.01` 就补一个可见值**。
医疗无人机走它（`MedicalDroneFx`）。

**⚠ 真正的坑（医疗无人机最终定案，2026-10-10）**：**光亮术的 `FxOnStart` 根本不是"常驻光"，而是施法瞬间的星光爆发**。
染色复核（`FxTint` 的 dump）显示它由 `Sparks_Body_Loop / Shockwave / StarGlow / StarCenter / BackGlow` 组成、
`bursts=1`、常驻 `存活=0` —— 挂到单位身上就是"放完就没"，看不见。
另外它的 `Light.intensity` 默认 **0**（由脚本点亮），所以"保留灯"也是一盏黑灯。
⇒ 第 ⑤ 版试过"自己造"：`new GameObject()` + 自建球网格 + `Shader.Find` 材质 + `AddComponent<Light>()`，
**实测仍然看不见**（2026-10-10 作者回报）；第 ⑥ 版又试了"自建面片 + `SwordStreakFx.StreakMaterial` + 点光"，
作者仍反馈没看见，并要求"别用光亮术、换个素材"。

**★ 最终做法（2026-10-10 定案）：换素材来源——借"给单位挂视觉"的原版增益。**
前面几版都在"自己造"或"借发光法术"上打转。改用**原版本来就"给单位挂一片常驻视觉"的增益**
（`*VisualBuff` / 灵光 / 光环类，例如 `HealingJudgmentBuff` 治疗审判【**当前选用**】、
`AeonEyeEnemyVisualBuff` 神盾之眼、`LichSoulsTrailBuff` 巫妖魂迹、
`DLC5_ShadowPlaneBeltLanternVisualBuff` 提灯【作者 10-10 否掉，留作备选】），
**克隆 + 重染 + 挂到 `unit.View.transform` 下**（机制照抄 `HasteTrailFx`，**已实机确认**），
并且 **`FxTint.SpawnAttached(..., keepLights: true)`**（别删素材里的 Light，见下）。

- 候选表：`FxLibrary.MedicalDroneAuraCandidates`（顺序即优先级）；取第一个借到 `FxOnStart` 的。
- 离线加候选：`tools/scan_buff_fx_all.py <关键字>` 扫全库"有 `FxOnStart` 的增益"。

**教训**：
1. 判断"借到的素材是什么"时，**别只看日志"借用成功"**——`FxOnStart` 可能是粒子、网格、**一盏灯**，
   也可能只是**一次性的施法爆发**。加一条**内容诊断**（`Renderer/粒子/灯` + 灯的 `强度/范围/色` +
   粒子的 `isPlaying/particleCount`）能一眼看穿，别猜。
2. **"有内容"不等于"常驻"**：`bursts=1` / `存活=0` = 一次性爆发，不适合"跟单位常驻"。
3. ⚠ **`Shader.Find` 自建材质在本 mod 已翻车三次**（剑雨光尾两次 + 医疗无人机一次）：
   构建里拿不到可用 shader 时材质为 null、网格渲染器什么都不画。**一律改用 `SwordStreakFx.StreakMaterial`**。
4. **别把"可见性"押在自建 `Light` 上**——第 ⑤ 版挂了点光，画面上依然没有主视觉；要看得见就**画面片 / 网格**。
5. ⚠ **"借发光法术的 `FxOnStart`"与"自己造"都不稳**；要"长在单位身上的常驻视觉"，
   **借原版"给单位挂视觉的增益"最稳**（`*VisualBuff` / 灵光 / 光环类天生就是干这个的）。
6. ⚠ **克隆素材时 `keepLights` 必须为 `true`**（很多"光"藏在 Unity `Light` 里，默认会被删掉 → 看不见）。


### 7.11 C 类"持续地面区域"的完整配方（钙质化，2026-10-09 定案）

适用范围：**有范围、在地上持续铺一片**的法术（钙质化 / 冰封领域 / 儿时的舞乐 / 天灾降临）。

代码：`src/AreaGroundFx.cs`（注册表 + 克隆 + 重染 + 生命周期）、`src/FxLibrary.cs`（借素材）。

#### 7.11.1 第一步：离线选材（`blueprints.zip`，不要进游戏一个个试）

C 类借的是**区域的常驻 `Fx`**（`BlueprintAbilityAreaEffect.Fx`），**不是**法术的 `AbilitySpawnFx`。

四条筛法，全库扫一遍就有答案：

| 条件                        | 为什么                                                       | 怎么查                  |
| ------------------------- | --------------------------------------------------------- | -------------------- |
| **`Shape` == `Cylinder`** | 只有圆柱是圆的；`Wall` 是**一条直线墙**，拼不成圆（剑雨为此白折腾两轮）                 | 读区域 `.jbp` 的 `Shape` |
| **`Size` == 法术半径**        | `Cylinder` 的 `Size` 是**半径（尺）**；相等就**免缩放**（差得多就得给 `scale`） | 读 `Size.m_Value`     |
| **`Fx` != null**          | 原版**大量**区域这字段是空的                                          | 读 `Fx.AssetId`       |
| **语义对得上**                 | 见下                                                        | 看名字                  |

钙质化实测：全库 `Cylinder` + `Fx` 非空 的区域约 1500 个，其中 **`Size` 正好 20 的有 77 个**——

逐个看语义就够了，一分钟的事。

#### 7.11.2 ⚠️ 语义优先于元素（钙质化踩过的那一脚）

**别按"元素"选，要按"这东西长什么样"选。** 钙质化是土/石系，第一版就按"土"选了

`DeadlyEarthEarthBlastArea`（土元素"致命大地"）——**结果画面上是翻腾的泥 + 泥浆飞溅**，

用户直接否掉（"太丑，不符合'钙'的感觉"）。

**教训**：同一个"元素"下的候选，形状可能天差地别（土系里既有"泥浆"也有"碎壳"也有"尖刺"）。

选之前先问一句：**这个法术在设定里"看起来是什么"**（钙质化＝结了一层苍白石壳），

再去找**形状**最接近的那个，而不是找"元素标签"最接近的。

#### 7.11.3 要改色 → 必须克隆重染，不能 `area.Fx`

- `area.Fx` 是引擎直接播的 `PrefabLink`，**改不了色**（见 `fx-recolor.md` §三）。
- 而原版**没有**天然就是"钙化的苍白石壳"那个颜色的地面素材（土系要么泥、要么黑曜石、要么灰刺）。

  → 只能克隆 + 重染，做法同 SmokeFx（`IAreaEffectHandler` + `Instantiate` + `FxTint.Apply` + 字典管生命周期）。
- 已经把这套抽成**注册表** `AreaGroundFx`：加一个 C 类地面法术只要一行
  ```csharp
  AreaGroundFx.Register(Guids.XXXArea, FxLibrary.XXXGround, new UnityEngine.Color(...), scale: 1f);
  ```
  **必须在蓝图注入阶段调用**（早于任何施法）；`Main.Load` 里订阅一次 `AreaGroundFxWatcher` 即可。

#### 7.11.4 颜色 = "这东西是什么"

| 法术   | 素材（形状）      | 目标色                           | 理由             |
| ---- | ----------- | ----------------------------- | -------------- |
| 钙质化  | 黑曜流碎壳（地面结壳） | `(0.96, 0.96, 0.97)` **中性偏冷** | "钙"＝石灰岩的**冷白** |
| 烟幕掩护 | 恶臭云（云雾）     | `(0.85, 0.85, 0.88)` 冷灰白      | 烟              |

⚠️ 两个 C 类法术要**能一眼分开**：颜色、形状至少差一样。别让"钙化"和"烟幕"都变成灰白的一坨。

#### 7.11.5 ⚠️ 八条染色坑（钙质化连踩六版才摸清，**改色前必看**）

**坑一：屏幕色 ≈ tint 的 2~3 次幂，不是 tint 本身。**

`FxTint` 把 tint 刷到**材质 `_Color`** 和**贴图**两处，渲染时相乘 → 一填 `(0.86,0.84,0.76)`，

上屏就是**土褐**（≈ 平方后的值），不是石白。**想浅就往白里给**（钙质化最后给到 `(0.95,0.94,0.90)`）。

这和 §7.10.1 是同一条规律，只是这里没有粒子色，幂次略低（≈2 而不是 1.8 次幂后再乘）。

**坑二：暗素材的"贴图提亮"必须调高，否则主体染不动。**

贴图染色是 `new = rgb × lerp(原像素亮度, 1, textureLift)`。

`textureLift` 默认 `0.40`（保留原明暗，适合"改色相不改明暗"的场合），

但**黑曜石那种近黑的素材**用 0.40 染完**主体仍是黑的**——只有发光裂缝被染成白的，

屏幕上就是"**黑壳 + 白线**"（用户第一版的反馈：形状不错，颜色不对）。

要"整体变浅"就把 `textureLift` 调到 **0.85~0.9**（基本抹平原亮度）。

`AreaGroundFx.Register(..., textureLift: 0.88f)` 就是这个旋钮。

**坑三：通道差会被幂次放大——"微暖"上屏会变成"米黄"。**

`(0.95, 0.94, 0.90)` 看着只是"几乎中性的微暖"（R−G=0.01、G−B=0.04），

但屏幕色是 tint 的 2~3 次幂 → **通道差同比翻倍**（R−G 变 0.019、G−B 变 0.074），

上屏就是明显的**米黄/奶油**（用户第二版反馈："颜色更对一点"）。

⇒ **想要中性/冷白，tint 就得写成中性或微冷**，别指望"差不多"：

钙质化最后取 `(0.96, 0.96, 0.97)`（R=G、B 略高），幂次放大后仍是冷白（饱和仅 0.02）。

**坑四：默认跳过的“数据贴图”里，可能就藏着那一层的底色。**

`NeutralizeMaterial` 默认跳过名字含 `mask / flow / distort / erosion` 的贴图（怕破坏通道语义）。

但原版**黑曜流**那类 FX 的**地面残留层**，底色可能正好在一个 `Flow` 类贴图里 —— 跳过 = 这一层永远染不动。

⇒ `FxTint.Apply(..., tintAllTextures: true)` 打开它（只保留 `normal / bump` 永不染，那是几何数据）。

**判据**：如果“一部分变浅了、另一部分纹丝不动”，先怀疑这条，而不是怀疑没染上。

**坑五：残留层可能是“运行后才挂上来的子物件”。**

有些 FX 的残留层不在 prefab 静态子树上，是运行时才 Instantiate 进来的——克隆那一刻染不到它。

⇒ `AreaGroundFx` 给克隆体挂 `DelayedRetint`，在 **0.35s / 1.2s / 3.0s** 再各补染一次。

补染是**幂等**的（贴图有缓存；网格漂白过一次后第二次检测到已全白会跳过），多跑没有副作用。

**坑六：黑可能烘在 shader 的 LUT / 投影贴花里——“提亮”顶不动，只有“抹平”才保证。**

钙质化第四版实测：日志里这套 FX 有一层是 **`Owlcat/Decals/Projector`**（投影贴花），

配 `_BaseMap=LiquidPuddle00_512`；另有一个 `Owlcat/Particles` 带 **`_ColorAlphaRamp=Gradient_Black00`**

（**字面就是“黑渐变”**）。这一层的黑**不在“颜色 × 贴图”里**，而在 shader 的颜色/透明度查找表里——

`textureLift` 只改 `贴图 = rgb × lerp(原亮度, 1, lift)`，**改不动 LUT**，所以怎么提亮都没用

（用户：“是不是你提高亮度的做法错了”——是的，对**这一层**无效）。

⇒ 用 **`textureLift = 1.0`**：此时 `贴图 = rgb × 1 = rgb`，贴图被**抹成纯目标色**、不再依赖原素材亮度，

**那一层也必然跟着变**。代价是失去贴图自身的明暗纹理，所以确认不黑了之后可以往回收（0.5~0.7）。

**坑七（最阴的一条）：黑可能藏在贴图的 Alpha 里——染 RGB 一万遍都没用。**

钙质化第五版：日志里“染色后”的清单证明**所有**颜色属性、**所有**贴图（`*_rdtint`，且已变“可读”＝确实是我们的副本）

都已经是目标色，粒子 `startColor` 也全是目标色 —— **但屏幕上那一层一点没变**。

原因：这类“地面残留层”（`Owlcat/Decals/Projector` 投影贴花 + `LiquidPuddle00_512`）的 shader

用**贴图的 Alpha 当“压暗量”**，而 `TintedTexture` 染贴图时**保留 Alpha**。

⇒ 想让它变淡只能**压 Alpha**：`FxTint.CutAlphaByShader(...)`（只动 Alpha、RGB 不动；

覆盖 Renderer 材质**和**非 Renderer 的材质持有者如 `Projector`）。

**坑八：那层贴花“只负责压暗、不负责上色”，而且它的可见性本身就是 Alpha。**

钙质化第六版：把贴图 Alpha 压到 **0.22** → **整片残留直接消失**（用户：“现在钙质化的残留特效没了”）。

这条反证了两件事：① **那片残留就是这层投影贴花本身**（切它 = 整片没了）；

② 它是 **`地面 × (1 − a)` 式的纯压暗**，颜色不参与 → 所以 RGB 怎么染都不变（坑七的成因）。

⇒ 但**不能全压、更不能压太狠**：全压会连“花纹”一起压没。这类贴花一般是

**主贴图（`_BaseMap`）= 要淡掉的“暗块”，噪声贴图（`_Noise*`）= 花纹/白线**。

所以取 **只压 `_BaseMap` + 压到 0.55**（暗度约减半、花纹保留）：

`FxTint.CutAlphaByShader(go, "Decals/Projector", 0.55f, propNameContains: "basemap")`。

⚠️ **纯压暗的贴花永远做不出“苍白”**——它只能“不那么黑”。真要“发白的地面”必须**换素材**

（换成加法/发光的区域 FX，如原版的“天堂光环 / 清晰之环”那类），或**自己铺一层**。

**症状 → 该动哪条通道（速查）**：

| 症状                   | 通道 / 对策                                                      |
| -------------------- | ------------------------------------------------------------ |
| 整层颜色不对、**跟着 tint 变** | RGB（颜色属性 / 贴图 RGB）→ 调 `tint` / `textureLift`                 |
| **染了完全没反应、形状还在**     | **Alpha**（→ `CutAlphaByShader`）或 shader 里的 LUT               |
| 一部分变了、一部分没变          | 贴图被“数据图”规则跳过（→ `tintAllTextures`）或运行后才挂上来（→ `DelayedRetint`） |
| 压 Alpha 后**整层消失**    | 那层**只靠 Alpha 存在**（纯压暗贴花）→ 只能“不那么黑”，要“发白”得**换素材**             |
| 命中 0 且毫无变化           | 那一层**不在克隆体上**（游戏自己画的 / 别的对象）→ 换思路                            |

**调色旋钮速查（C 类地面）**：

| 想要          | 动哪个                                                       |
| ----------- | --------------------------------------------------------- |
| 整体更浅/更白     | `tint` 往白里给（因为要乘 2~3 次）                                   |
| 去黄、要冷白      | `tint` 写成 **R=G、B 略高**（如 `0.96,0.96,0.97`）；别只"微调一点点"      |
| 暗素材染不动、主体发黑 | `textureLift` 调高（0.85~0.9）；**怎么提都还黑就直接给 1.0（抹平）**         |
| 某层完全不跟着变色   | ① `tintAllTextures: true` ② `DelayedRetint` 补染 ③ 看“染色后”诊断 |
| 那层只会压暗、做不出白 | 它**只能变淡**；要发白得**换素材**或自己铺一层（坑八）                           |
| 想保留素材的明暗层次  | `textureLift` 调低（0.2~0.4）                                 |
| 大小不对        | `scale`（= 法术半径 / 素材 `Size`）                               |

#### 7.11.7 让"循环波纹"变连续（可选打磨，2026-10-09）

**症状**：地上那圈白环一直往外扩，但**扩完会消失、再重新出现再扩**，看着有断档。

**原因**：那是**一颗粒子**的一生——"从小扩到大 → 淡出 → 下一颗重来"。两颗之间有**空档**，

是素材本身的循环节奏，不是 bug。

**做法**：让相邻两颗**在时间上错开重叠**（`AreaGroundFx.MakeLoopsContinuous`）。两种发射方式分开处理：

| 发射方式 | 判据                                      | 处理                                                  |
| ---- | --------------------------------------- | --------------------------------------------------- |
| 持续发射 | `emission.rateOverTime.constantMax > 0` | **速率 × overlap**（粒子更密，自然重叠）                         |
| 爆发发射 | 否则 `emission.burstCount > 0`            | 把每个 `Burst` **复制成 overlap 份**，按 `周期 / overlap` 依次错开 |

- 周期取 `Burst.repeatInterval`（`cycleCount > 1` 时才有意义），没有就退到 `main.startLifetime.constantMax`。
- `overlap = 2` 就够补上那个空档；给太多会过曝 / 变乱。
- 调用：`MakeLoopsContinuous(go, "cycle", 2)`——**只改名字含 `cycle` 的粒子**

  （原版这套 FX 里就是 `ProjectorDecal (1)_Cycle (4)/(5)`，即那圈波纹本身）。
- ⚠️ 只是"更接近连续"，**不是完美无缝**；日志会打"原值 → 新值"，效果不对就把 `overlap` 调回 1 复原。

#### 7.11.9 把"噗一下就淡掉"改成"一直铺着"（2026-10-09）

**症状**：地面那层云**噗一下铺满**、然后淡掉，最后只剩中间一小块。
  
**原因**：素材里那层云是**一次性爆发**（`emission` 里是 `Burst`，不是 `rateOverTime`），
  
粒子寿命一到就没了，不会再冒新的。
  
**用户要的**："释放时那层铺满的云一直保留"（而不是淡成一小块）。

**做法**：`AreaGroundFx.MakePersistent(go, density)` —— 把 burst **折算成持续速率**、然后清掉 burst：

```
rate = (每次爆发的粒子总数) / (粒子寿命) × density      // density = 平均同时存在几层云，2 = 两层叠着
em.rateOverTime = rate;  em.SetBursts(new ParticleSystem.Burst[0]);
```

这样"旧的还没淡完、新的已经在冒" → 观感是**连续的一层云**。
  
⚠️ 只处理**有 burst 的**粒子；**已被 `DisableEmitters` 关掉的会跳过**（靠"渲染器还开着吗"判断），
  
免得把不想要的部件又打开。

**顺带解决"粒子缩不小"（§7.1 那条老坑）**：`main.scalingMode = ParticleSystemScalingMode.Hierarchy`。
  
Unity 默认是 `Local`——**粒子尺寸不跟根节点缩放走**，所以 `FxTint` 传的 `scale` 对粒子无效。
  
改成 `Hierarchy` 后，克隆体的 `scale` 才是**真的能放大/缩小**的旋钮（`scale = 1` 时观感不变，安全）。
  
钙质化现在用 `scale: 1.1f` 做用户要的"扩大一点"。

#### 7.11.6 诊断

`Player.log` 里：

- `地面特效已登记：<区域GUID> → tint=... scale=...` = 蓝图注入阶段登记成功；
- `地面特效模板加载返回 null` = 素材借不到（回到 7.11.1 重选）；
- `地面区域特效已生成（克隆重染）` = 区域生成时挂上了；
- `地面特效重染失败` = 染色报错（会保持原色上屏，功能不受影响）。

#### 7.11.8 "这个部件不要"——直接关掉它（2026-10-09）

比"染色"更省事的一招：**不想要的部件直接关掉**，别去染它。
`AreaGroundFx.DisableEmitters(go, "<关键字>")`：名字匹配的粒子系统 → `emission.enabled = false`
+ `Stop(StopEmittingAndClear)` + 关掉它的 `ParticleSystemRenderer`。
**只动渲染器、不销毁 GameObject**（留着层级，免得影响同一棵树上别的部件）。

⚠️ **顺序：先"关掉不要的"，再"持续化"**。`MakePersistent`（§7.11.9）会**跳过渲染器已关掉的粒子**，
所以 `DisableEmitters` 必须在它**之前**。反了的话，不想要的部件会被一起变成**永久**
——钙质化实测：粗白圈 = 那圈环被持续化了（用户："这个版本非常的烂"）。

⚠️ **先确认"要关的"和"要留的"是不同粒子**：这两组可能**共用同一个材质实例**
（钙质化这里，波纹和地面云都挂在 `Owlcat/Decals/Projector` 那套材质上）——
共用材质**不影响**分别开关，因为开关是按**粒子系统**走的；但**按材质动手**
（如 `CutAlphaByShader`）就会**同时**影响两者，这是"压 Alpha 压到整片消失"的另一个原因。

⚠️ **别靠猜名字**（钙质化猜错过两次：先 `cycle`、再 `wave`）。两个办法：
① 先看 §7.11.9 里的 `LogInventory` 清单（名字 / 渲染器开关 / burst / 速率 / 寿命一目了然）；
② 拿不准就**多个关键字一起关**——钙质化最后是
`foreach (kw in {"wave","cycle","ring","ripple"}) DisableEmitters(go, kw);`
这几个词都指向"外扩的环"，一起关能保证它没了，同时**不会误伤**地面云。

**钙质化的两个部件（用户给的判据）**：
- ❌ **禁止**：外扩的**粗白圈**（波纹环）→ 用上面的关键字关掉；
- ✅ **想要**：释放时那层**铺满区域的灰白云** → 用 §7.11.9 的 `MakePersistent` 让它一直铺着。

#### 7.11.11 定位"屏幕上这个东西是哪个粒子"——用**排除法**，不要猜名字（2026-10-09 定案）

钙质化为了"留云、去环"猜错了三次名字（`cycle` → `wave` → 再猜），代价是几版都被否。
最后靠**排除法**一次锁死，方法可复用：

1. **先打清单**：`AreaGroundFx.LogInventory(go)` 把每个粒子的
   名字 / 渲染器开关 / burst 数 / 持续速率 / **寿命** 打一行到 `Player.log`。
   （寿命是很好的线索：`GroundFireBig` 的 **100000 秒** = "常驻地面层"的签名。）
2. **关掉一批，让用户看一次**，记下"什么没了、什么还在"。
3. **用"关掉之后还在"来排除**：钙质化的证据链是——
   关掉 `hole/fountain/puddle` 后 **云出现了**（说明它们原本盖着云）**但环还在**
   ⇒ 环 ∉ {hole, fountain, puddle}；云 = `GroundFireBig`（唯一常驻层）
   ⇒ 环 ∈ {`Waves`, `ProjectorDecal (1)_Cycle`} —— 剩下的没被碰过的两个。**范围被锁死，不用猜。**
4. 再关掉锁定范围内的全部候选 → 若"环没了、云还在"，收工。

⚠️ **务必让用户确认"换 DLL 后重启过游戏"**：DLL 是启动时加载的，
没重启的话截图反映的是**上一版**，会让整条推理链作废（这次差点因此误判 `Waves`）。

**排除法闭合（钙质化，2026-10-09 定案，两条结论都是实测事实）**

| 观测 | 结论 |
|---|---|
| 只关 `hole`/`fountain`/`puddle` → **云在、环也在** | 云 ∉ {hole, fountain, puddle} |
| 再把 `wave`/`cycle` 也关掉 → **环还在、云没了** | **环 = `GroundFireBig`**（当时唯一还开着的）；**云 ∈ {`Waves`, `ProjectorDecal (1)_Cycle`}** |

⇒ 最终配置：**关掉 `hole` / `fountain` / `puddle` / `groundfire`（环），保留 `wave` / `cycle`（云）。**
两次"关一批、看剩下什么"就把 13 个粒子收敛到唯一答案，比猜名字（错了三次）快得多。

#### 7.11.12 ⚠️ 部件不一定是"粒子"——先分清粒子 / 非粒子（钙质化最贵的一课）

钙质化那圈白环，我**一直在粒子系统里找**（`LogInventory` 列了 13 个粒子，逐个关、逐个排除），
结果关了 `GroundFireBig` 环**照样在**。真正点破的是用户对结构的描述：

> "这其实是几个特效衔接：最开始是**喷溅特效**，然后是**白云扩大消失**，**最后是这个**（环）。"

**环是链条的最后一段，而它根本不在那 13 个粒子系统里** ⇒ 它是**非粒子**部件
（网格 / 贴花 / 投影器）。所以 `DisableEmitters` 怎么关都碰不到它——不是名字猜错，是**类别找错**。

**教训（写进流程）**：
1. 列清单时**必须把"非粒子渲染器"也列出来**（`MeshRenderer` / `SkinnedMeshRenderer` /
   `TrailRenderer` / `LineRenderer` / 任何 `Projector` 组件），不能只列 `ParticleSystem`。
2. 用户描述"这个特效分几段"时，**那段描述就是结构图**，优先按它定位，而不是去数粒子。
3. 想"只要粒子、不要别的"时，直接 `AreaGroundFx.DisableNonParticleRenderers(go)`：
   关掉全部非粒子渲染器 + 任何 `Projector` 组件，**粒子系统一律保留**（逐个打日志）。

**环 = `Waves`，云 = `ProjectorDecal (1)_Cycle`（钙质化，2026-10-09 最终定案）**

依据（全部来自日志与实测，不含猜测）：
- 克隆体清单：13 个粒子 + 1 个 `MeshRenderer 'Decal'`（唯一非粒子）；**区域视图 0 渲染器 / 0 粒子**
  （只有个空定位点 `Locator_GroundFX`）⇒ 环只能在粒子里。
- 粒子参数里**唯一吻合"往外扩 → 消失 → 重现 → 再扩"**的是
  **`Waves`：`burst=1`、速率 1.0、寿命 3.0s**（一次一颗，一颗活 3 秒）—— 正是用户描述的行为。
- 用户认可的那版里 `Waves`/`Cycle` 开着、`GroundFireBig` 关着而云在 ⇒ 云 ∈ {Waves, Cycle}；
  Waves 是环 ⇒ **云 = `ProjectorDecal (1)_Cycle`**。
- `hole` / `fountain` / `puddle` 会**盖住**云，继续关。

⚠️ **最坑的一点**：中途我"关掉 `wave` 之后环还在"，于是把它当反证排除了 `Waves`——
那次几乎肯定是**换 DLL 后没重启游戏**（DLL 启动时加载，没重启跑的还是旧版）。
**换 DLL 后必须重启**，否则用户的截图会指向错误的结论，比不测还糟。

**最终更正：环 = `ProjectorDecal (1)_Cycle`，云 = `Waves`（2026-10-09，直接测量）**

上一版（关 `hole`/`fountain`/`puddle`/`wave`）的日志状态与用户画面**一一对应**，这是硬证据：

| 日志 | 用户画面 | 结论 |
|---|---|---|
| `[13] Waves 渲染器=关` | **无云** | ⇒ **云 = `Waves`** |
| `[1][6] ProjectorDecal (1)_Cycle 渲染器=开` | **有环** | ⇒ **环 = `ProjectorDecal (1)_Cycle`** |

⇒ 正确做法：**关 `cycle`（环）、留 `wave`（云）**，`hole`/`fountain`/`puddle` 继续关（只盖云）。

⚠️ 我一度把 `Waves` 判成环（方向完全反了），根因是**拿了一条"没重启游戏"的无效观测当反证**。
为此加了 **`BuildTag`**：每次改视觉换一个值并打进日志
（`地面特效构建=calc-...`），**先确认日志里是哪一版，再采信截图**。

#### 7.11.13 ★ 最终定案：环 = `GroundFireBig`；**"关渲染器"无效，必须"销毁物体"**（钙质化，2026-10-09）

日志（构建标记 `calc-20261009-1807-destroy` 已确认是新版）：
```
销毁粒子物体：'GroundFireBig (1)'      ×2
销毁非粒子渲染器物体：MeshRenderer 'Decal'
[13] Waves  渲染器=开                  ← 云完好
```
用户反馈：**白圈没了，云在** ✓

**同一批部件，两种处理方式的差别（这是本节最值钱的一条）：**

| 处理方式 | 结果 |
|---|---|
| `renderer.enabled = false`（+ `emission.enabled=false` + `Stop`） | **环照旧在屏幕上**（日志显示那些渲染器确实都是"关"状态） |
| `UnityEngine.Object.Destroy(物体)` | **环消失** ✓ |

⇒ **遇到"关了没用"的部件，别再调参数，直接换手段：销毁物体。**
（`AreaGroundFx` 里两套都留着：`DisableEmitters` / `DestroyEmitters`、`DisableNonParticleRenderers` / `DestroyNonParticleRenderers`，
后者带"同物体还有别的粒子就跳过"的安全阀，避免误杀云。）

**钙质化最终配置**：
- 保留：`Waves`（= 那层灰白云，用户要的）
- 销毁：`GroundFireBig`（= 外扩白环，用户不要的）+ 唯一的 `Decal` 网格
- 关闭：`hole` / `fountain` / `puddle` / `cycle`（它们只会盖住云）
- 染色：冷白 `(0.96,0.96,0.97)`、`textureLift=1.0`、`tintAllTextures`、`CutAlphaByShader("Decals/Projector",0.55,"basemap")`

#### 7.11.14 让"云"覆盖整场 + 铺满范围（钙质化，2026-10-09）

用户两条要求：**① 云要一直留到法术结束；② 云要铺满法术作用范围（原来偏小）。**
做法：`AreaGroundFx.FitCloudToArea(go, "wave", 法术半径m, 兜底缩放)`。

| 要求 | 手段 | 关键点 |
|---|---|---|
| 覆盖整场 | `main.startLifetime = 40s` + `rateOverTime ≥ 1/s` | 原素材是"burst 打一发就淡掉"，所以**既要有足够长的寿命、也要有持续发射**，缺一个都会中途没了 |
| 铺满范围 | `缩放 = 法术半径 / 发射形状半径`，只缩放**该粒子自己的 transform** | ⚠️ **不要用包围盒反推尺寸**：docs §7.1 实测过粒子 `bounds` 会被吹大一个数量级（348 m vs 可见 25 m）。**形状半径是几何参数，可信** |
| 让缩放真的生效 | `main.scalingMode = ParticleSystemScalingMode.Hierarchy` | Unity 默认 `Local`——**粒子尺寸不跟缩放走**，不设的话缩放对粒子无效（就是 §7.1"粒子缩不小"那条的成因） |

日志会打：`云调整：'Waves' 寿命 3.0s→40.0s 速率 1.00→1.00 形状=Box(Box.scale/2=0.50m) 缩放×12.19 → 覆盖面半径≈6.10m`
——**形状类型 / 半径 / 算出的缩放都打出来了**，不对就照着这个数调 `CloudFallbackScale` 或直接改常量。

#### 7.11.15 ★ 关键：**先看发射形状，再决定用哪条尺寸公式**（钙质化，2026-10-09）

日志打出来那一刻才明白：
```
云调整：'Waves' … 形状=Circle(Circle.radius=0.01m) 缩放×2.00
```
**发射形状是半径 0.01 m 的"点"**——这层云**不是靠发射范围铺开，而是靠单颗粒子自己长大**
（`sizeOverLifetime`）。所以：

- 我上一版用"发射形状半径"做几何换算（`缩放 = 法术半径 / 形状半径`）**完全无效**，
  算出来只是兜底值 2.0 —— **公式套错了对象**。
- 真正决定覆盖面的是**粒子自身尺寸**（`main.startSize` × `sizeOverLifetime` 曲线）。
- **而且 `sizeOverLifetime` 是按"寿命的百分比"走的**：我把寿命从 3 s 拉到 40 s，
  等于让云**花 40 秒才长大** —— 这就是用户说的"出现太慢、扩大太缓慢"。

**正确配方（点发射型云）**：

| 要求 | 手段 |
|---|---|
| 施法瞬间出现 | `startDelay = 0`；尺寸曲线从 0 起步但**1 秒内就长满** |
| **1 秒内扩到最大** | ① `startLifetime = 1.4s`（**这条最关键**，寿命决定长大被摊多长）② `sizeOverLifetime` 曲线改成"前 1 s 从 0 升到 1、之后保持 1" |
| 铺满法术范围 | `main.startSize = 法术直径`（点发射时**粒子尺寸就是覆盖面**）；20 尺半径 → 12.192 m |
| 全程不消失 | `rateOverTime ≥ 3/s`（单颗只活 1.4 s，靠持续发射接力） |

**流程教训**：**先读日志里的 `形状=` 再动手**。
`Circle/Sphere/Cone` → 形状半径有效，可以按半径换算；
`Circle.radius≈0`（点）/ `Box` → 尺寸靠粒子自身，必须直接设 `startSize`。

#### 7.11.16 尺寸对齐 AoE 红圈：**用截图量像素，别用理论值**（钙质化，2026-10-09）

把云的 `startSize` 直接设成"法术直径 12.192 m"（20 尺半径）后，用户反馈**太大**——
云盖到了外圈（"是红圈不是黄圈"）。**同一行上量像素**：

| 元素 | 像素宽 |
|---|---|
| 游戏画的 AoE 红圈 | ≈ 712 px |
| 我们染的白云 | ≈ 908 px |

⇒ 云是红圈的 **1.25 倍** ⇒ `CloudDiameterFactor = 1 / 1.25 ≈ 0.8`，即云直径 = 法术直径 × 0.8。

**方法论**：游戏 AoE 指示圈与实际区域的换算关系**不要靠推理**（`AoERadius`/`Size` 都写 20 尺，
但指示圈画出来并不等于"20 尺半径的直径"），**直接拿截图量**最可靠——
两个东西在同一行上的**像素宽度之比**就是真实的尺寸比（与相机、缩放都无关）。

⚠️ 以后凡是"我们的特效 vs 游戏指示圈"对齐，一律走这条路：
**截一张图 → 同一行量两者的像素宽 → 按比值缩放**。

#### 7.11.17 ★★ 尺寸从**游戏数据**推，不要量像素（钙质化，2026-10-09，可复用结论）

上一版我拿截图量像素、算出 1.25 倍、把云缩到 0.8——**方向就是错的**：那个红圈并不是"20 尺"的圈。
正确做法是**查游戏自己的类型**，一次拿到可复用的结论：

```
Kingmaker.Utility.Feet
  CONST pub Single FeetToMetersRatio = 0.3048     ← 游戏自己的尺→米换算
  PROP  pub Single Meters { get; }                ← Feet 自带 .Meters

Kingmaker.UnitLogic.Abilities.Blueprints.BlueprintAbilityAreaEffect
  FIELD pub Feet Size                             ← 区域半径，就是 Feet 类型
  PROP  pub Int32 RadiusInCells                   ← 换算成格子数

Kingmaker.Utility.* : CONST pub Single CellSize = 3     ← 游戏格子 = 3 米
```

**可复用结论（C 类地面区域一律照这个来）**：
1. **区域半径直接问游戏**：`area.Blueprint.Size.Meters`（`Feet` 类型的 `.Meters` 属性）。
   **不要自己写 `尺 × 0.3048`，更不要量截图像素**——游戏已经算好了。
2. 于是"把特效铺满法术范围"就是一句：**目标直径 = `2 × area.Blueprint.Size.Meters`**，
   写进 `AreaGroundFx` 后**所有 C 类法术自动正确**，不需要为每个法术配一个数。
3. `RadiusInCells` / `CellSize = 3` 只在需要"按格子对齐"时才用。

⚠️ **别再量像素**：截图里的 AoE 圈不止一个（红/黄含义不同，用户指出黄圈才是 20 尺），
拿错圈去比就会得出错误的比例。**先查数据，数据查不到再考虑测量。**

#### 7.11.18 用**原版法术当参照**验证"Size 就是半径(尺)"（钙质化，2026-10-09）

从 `blueprints.zip` 抽出语义已知的原版区域蓝图对照，**完全吻合**：

| 区域蓝图 | 语义 | Shape | Size |
|---|---|---|---|
| StinkingCloudArea | 恶臭云（半径 20 尺） | Cylinder | **20** |
| CloudkillArea | 死云术（半径 20 尺） | Cylinder | **20** |
| ObsidianFlowArea | 黑曜流（半径 20 尺） | Cylinder | **20** |
| MindFogArea | 心灵迷雾（半径 20 尺） | Cylinder | **20** |
| TarPoolArea | 焦油池（半径 20 尺） | Cylinder | **20** |
| AcidFogArea | 酸雾术 | Cylinder | **20** |
| WebArea | 蛛网术 | Cylinder | **20** |
| StoneCallArea | 落石术（半径 40 尺） | Cylinder | **40** |
| IceStormArea | 冰风暴 | Cylinder | **40** |
| SpikeStonesArea | 尖石术 | Cylinder | **40** |
| BladeBarrierArea | 刃障（直线墙） | **Wall** | 60 |

全库 `Cylinder` 区域的 `Size` 取值分布也全是尺数：`30 / 20 / 5 / 10 / 50 / 13 / 15 / 60 / 40 / 100 / 7`。

⇒ **可复用结论（写死）**：
- **`BlueprintAbilityAreaEffect.Size` = 半径，单位英尺**（`Wall` 时是墙长）。**不是直径。**
- **特效要铺满范围 → 目标直径 = `2 × area.Blueprint.Size.Meters`**（`.Meters` 由游戏换算）。
- **原版区域 FX 是按"该区域的 Size"来配的**（例：我们的烟幕掩护把 `StinkingCloudArea` 的 FX
  **原尺寸**贴到同样 Size=20 的区域上，观感就是对的）⇒ 所以"我们的云要多大"有唯一答案：
  **和它所在区域一样大**，即 `2 × Size.Meters`。

#### 7.11.19 定案：**红圈 = 生效范围、黄圈 = 名义区域**（钙质化，2026-10-09）

场上游戏会画两个圈，含义不同（用户确认）：

| 圈 | 含义 | 依据 |
|---|---|---|
| **红圈** | **实际生效范围** | 用户认可的"法术范围"就是它 |
| **黄圈** | **名义区域**（`Size=20 尺` 那个） | 与蓝图 `Size` 一致 |

**尺寸标定**：同一行量像素 → 红圈 ≈712 px；把云设成"名义直径(12.192 m)"时云宽 ≈908 px
⇒ 云 = 红圈的 1.25 倍 ⇒ **`CloudDiameterFactor = 0.8`**（云直径 = 名义直径 × 0.8 ≈ 9.75 m）。

⚠️ **为什么这块做不到"纯用数据"**：我们这片区域是**运行时创建**的，游戏算给它的
`RadiusInCells = 0`（日志实测），**没有"生效半径"这个数可读**。
所以流程是：**能用数据就用数据**（`Feet.Meters` → 名义直径），
**数据拿不到的（生效范围）才退回标定**，并把标定依据写清楚。等哪天能读到生效半径，换回纯数据。

**结论（C 类地面区域通用）**：
```
名义直径 = 2 × area.Blueprint.Size.Meters          // 纯数据
实际用直径 = 名义直径 × CloudDiameterFactor(0.8)     // 与"红圈(生效范围)"对齐
```

#### 7.11.20 ★ 更正：**目标圈是"黄圈（法术作用范围）"，不是红圈**（钙质化，2026-10-09 定案）

§7.11.19 里我写过"红圈 = 生效范围、按红圈标定"——**这是错的**，用户当场指出：
> "你不感觉这个恰好把黄圈铺满了吗？**一开始设置的就不对，就不能用微调**"

**正确认知**：
- **黄圈 = 法术的作用范围（20 尺）= 特效该铺满的那个圈** ← 目标
- 红圈是另一个指示圈，**不该拿它当尺寸基准**
- 尺寸**不该靠"红圈/黄圈像素比"一点点微调**——那是把目标定错了以后在错误的基准上找补

**最终值**：`云直径 = 2 × area.Blueprint.Size.Meters × 0.88 ≈ 10.73 m`
（基数是**游戏自己给的** `Feet.Meters`，系数 0.88 只是补偿"粒子边缘柔和渐隐、可见范围比四边形略小"。）

**流程教训（第三次同类错误了）**：
1. 先问清楚"**要铺满哪个圈**"——场上的圈不止一个，含义不同，**拿错圈=全盘错**。
2. 基准值**必须来自游戏数据**（`Feet.Meters`），**不要用像素比去反推基准**。
3. 像素比**只能用来补偿渲染差异**（如软边），不能用来定基准。

#### 7.11.21 ★ 更正 §7.11.20：**红圈 = 施法/作用范围，目标就是红圈**（钙质化，2026-10-09 最终定案）

§7.11.20 我写成"目标是黄圈"——**又错了**。用户明确：
> "**红圈才是施法范围**"（要铺满的是**红圈**）

**最终定案（写死，不要再改目标圈）**：

| 圈 | 含义 |
|---|---|
| **红圈** | **施法 / 作用范围** ← **特效要铺满这个** |
| 黄圈 | 另一个圈，**不是**尺寸基准 |

- **最终值**：`CloudDiameterFactor = 0.88` ⇒ 云直径 = `2 × Size.Meters × 0.88 ≈ 10.73 m`（铺满红圈）。
- ⚠️ **这条我连续搞错三次**（追红圈→改追黄圈→又改回），根因是**没有一开始就把"哪个圈是作用范围"问清楚**，
  而是自己看图猜。**以后遇到"要对齐场上某个圈"，先让用户指出是哪一个，再动手。**
- 代码里 `BuildTag` 会打印当前值，日志可核对；改目标圈这种"基准级"改动**必须先确认**。

#### 7.11.22 用**粒子数据**量云的真实尺寸（钙质化，2026-10-09，可复用）

`startSize` 设了多少**不等于**屏幕上多大（粒子可能被速度带开、或有拉伸/尺寸叠加）。
截图量像素又受相机与软边影响。**唯一客观口径是直接读粒子数据**：

```csharp
var buf = new ParticleSystem.Particle[ps.particleCount];
int n = ps.GetParticles(buf);
// 每颗：position（局部）、GetCurrentSize(ps)
可见半径 ≈ max(粒子中心到中心轴的距离 + 该粒子尺寸/2)
```

钙质化实测：
```
云实测：粒子数=5 最远中心距=0.00m 最大粒子=10.77m ⇒ 可见直径≈10.77m（设定 startSize=10.73m）
```
⇒ **设定值 = 上屏值**，`FitCloudToArea` 确实生效 ✓（点发射，粒子全在中心）。

**与红圈交叉验证**：
| 量 | 值 | 来源 |
|---|---|---|
| 云真实直径 | 10.77 m | 粒子数据 |
| 红圈直径 | ≈10.7 m | 像素比反推（系数 0.8 时云 9.75 m = 红圈 0.91 倍） |
| 比值 | **≈1.00** | ⇒ **正好铺满红圈** ✓ |

⇒ `CloudDiameterFactor = 0.88` 定案。
**教训**：之前 0.8→0.88 只差 10%，肉眼必然看不出，却被我当成"改了没用"——
**改动幅度小于 ~20% 时，不要指望用户能看出差异**；要么一次给足，要么先用数据验证再动。

#### 7.11.23 ★ 云"只活 2~3 秒"的根因：原素材 `looping = false`（钙质化，2026-10-09）

**症状**：云铺开 2~3 秒就没了（用户："白云才有二三秒就没了"），
而期望是**一直铺着，直到法术效果结束**。

**根因**：原素材的粒子系统是 **"播一次就停"**（`ParticleSystem.MainModule.loop = false`）——
它按自带的 `main.duration`（2~3 秒）发射完就**不再冒新的**。
我前面只改了 `startLifetime`（单颗活多久）和 `rateOverTime`（发射多快），
**但系统本身停了，速率再高也没用**。

**修法**（`FitCloudToArea`）：
```csharp
main.loop = true;                              // ← 关键：开循环（注意属性名是 loop，不是 looping）
main.duration = Mathf.Max(main.duration, 60f); // 循环下 duration 无所谓，给长点无妨
```
克隆体本身跟着**区域**同生同灭（区域生成时创建、区域结束时销毁），
所以只要开了循环，云就**自动覆盖整个法术持续时间**——不用另算秒数。

**教训（和"半径/直径"同类）**：改粒子行为时要**把四个开关都过一遍**：
`loop`（会不会停）、`duration`（播多久）、`startLifetime`（单颗活多久）、`rateOverTime`（发射多快）。
**少看一个，就会出现"参数都对但行为不对"**。

#### 7.11.24 区域结束时的"渐隐"（钙质化，2026-10-09）

**症状**：区域一到期，克隆体被直接 `Destroy` → 画面上**"啪"地消失**（用户："有点突兀"）。

**修法**：`AreaGroundFx.OnAreaDestroyed` 不再直接销毁，而是挂 `FadeOutThenDestroy`：
1. **先停发射**：`ps.Stop(true, ParticleSystemStopBehavior.StopEmitting)`（不再冒新的，已有的自然演完）；
2. **1.2 秒内把所有材质 alpha 线性降到 0**：`FxTint.SetAlpha(go, a)`（新加的方法，统一改所有
   Color 属性的 alpha，RGB 不动；覆盖 Renderer 材质 + `Projector` 这类非 Renderer 持有者）；
3. alpha 到 0 后才 `Destroy`。

`FadeOutThenDestroy.Seconds = 1.2f` 是渐隐时长，想更慢/更快改这一个数。

⚠️ 注意：**停发射用 `StopEmitting`（不是 `StopEmittingAndClear`）**——
后者会把已有粒子立刻清掉，等于还是"啪"地消失。

#### 7.11.25 ★ C 类"多素材"支持：钙质化的部件手术必须**按素材门控**（2026-10-09）

**背景**：钙质化那套 `AreaGroundFx.OnAreaSpawned` 里，从"关 hole/fountain/puddle/cycle"到
"销毁 groundfire 外扩环""把 wave 云铺满范围""压 Decals/Projector 贴花 Alpha"，**全是对着
黑曜流碎壳那一份素材、进游戏实测调出来的**（§7.11.13）。而这段代码原来是**无条件执行**的——
**任何**登记到 `AreaGroundFx` 的区域都会跑一遍。加上冰封领域 / 儿时的舞乐 / 天灾降临后，
它们会被这段代码**误伤**，最狠的是 `DestroyNonParticleRenderers`（**无差别销毁全部非粒子渲染器**，
会把新素材自己的网格/贴花删掉）。

**修法**：`AreaGroundFx.Spec` 加一个 `CalcProfile` 开关（`Register(..., calcProfile: false)` 默认关）。
- **钙质化**：登记时显式 `calcProfile: true` → 行为与之前**完全一致**（那段手术照跑）；
- **其余 C 类素材**：不传（默认 false）→ 走 `else` 分支，**只染色 + 缩放**，
  另加 `EnableHierarchyScaling(go)`（把全部粒子设成 `Hierarchy` 缩放模式，让 `Register` 的 `scale` 对粒子生效，
  §7.11.9 那条老坑的通用解；`scale=1` 时观感不变）。

**给下一个 C 类素材的流程**：
1. 先**只染色 + 缩放**跑一遍，进游戏看它**原样长什么样**（`LogInventory` 会打粒子/非粒子清单，见 §7.11.11）；
2. 看清结构后，再决定要不要"关哪个粒子 / 销毁哪个部件 / 压哪层贴花"——**这些要单独补**，
   **别照抄钙质化的关键字**（名字只对黑曜流成立）；
3. 要改色仍走 `Register` 的 `tint` + `textureLift`（§7.11.5 八条染色坑照样适用）。

**教训（与 §7.10.3 同源）**：**"一个素材调通的参数"不等于"所有素材通用"**。
把"针对某一份素材实测出来的部件手术"混进通用路径，是下一批素材必然踩的坑——
**要么参数化、要么门控**，别让它默认对所有素材生效。

---

### 7.12 运行时重染的"常驻光环"：`TintedAuraFx`（2026-10-10）

要"跟增益同生同灭、长在单位身上、**而且要改色**"的光环，走 `src/TintedAuraFx.cs`（原 `SlowAuraFx` 的通用版）：

- **表驱动**：`Entries` 里一行 = 一个 buff GUID →（素材, 颜色, lift, keepLights, 日志名）。
- **机制** = `FxTint.SpawnAttached(素材, unit.View.transform, 颜色, lift, keepLights)`（与 `HasteTrailFx` 同款、**已实机确认**）。
- 现管两个：**时之锁**（减益 → 莫斯提马的蓝）、**不稳定血浆**（增益 → 血色，素材借原版"血雾" `BloodHazeBuff`）。
- 新加一个"要改色的常驻光环"：往 `Entries` 加一行 + 在 `FxLibrary` 加一个素材属性即可。

⚠ **`SetFxOnStart` 改不了色**——要非原色就只能"克隆 + 重染"，别在 `SetFxOnStart` 上做文章。
⚠ 克隆时 `keepLights` 视素材而定：像"血雾"这种把光藏在 `Light` 里的必须 `true`（§7.10.5 的教训）。

另：**单次爆发**要改色（不是常驻光环），用 `ContextActionSpawnTintedFx`（`src/Actions.cs`）——
挂在 `AdditionalAbilityEffectRunActionOnClickedTarget` 组件上（`AddComponent` 直接挂，
BlueprintCore 的同名配置器方法在 2.8.7 上取不到），每次施放在点击目标处生成一份重染克隆。用例：**缄默 → 灰白**。

### 7.13 ⚠️ 借 `AbilitySpawnFx` 前必须反查"**这一份 FX 还被谁用**"（2026-10-16）

**症状**：医疗敷料（0 环）/ 紧急救治（1 环）/ Mon3tr（5 环）三个技能**全都在冒同一份牧师绿光**——因为它们
在 `ApplyAbilityFx` 里都映射到了 `FxLibrary.Heal`（治疗轻伤 `CureLightWounds` 的 `AbilitySpawnFx`）。
作者尤其不满 Mon3tr：它本应是凯尔希的**黑色神经结晶**，却在冒牧师绿光。

**第二条症状（更隐蔽）**：把紧急救治换成"生命气息"（`BreathOfLifeTouch`，`ee0c5b93…`）后，作者一眼看出
**它和复活术 / 死者复活是同一个特效**——离线反查属实：`RaiseDead`、`Resurrection`、`RitualBreathOfLifeAbility`、
`HunterRaiseCompanionAbility` 都引用这份素材。

**做法**：离线扫 `blueprints.zip` 全部 `BlueprintAbility` 的 `AbilitySpawnFx`，**按 `PrefabLink.AssetId` 归并**，
输出"这一份素材被哪些法术引用"，再按**语义**挑：

- 治疗类素材**远不止一份**：治疗轻伤 `61602c5b…`、治疗中伤 `e9399b6d…`、治疗重伤 `d95f8959…`、
  治疗致命伤 `474cb5ac…`、治疗术 `319f5078…`、圣疗 `224fb8fd…`、复原术 `621885df…`、生命气息 `ee0c5b93…`。
- **治疗中伤 ≠ 治疗轻伤**（不同 `AssetId`）——所以"在黑色结晶上再叠一层治疗中伤的光"才看得出来是两层。
- 反查还有一个副产品：能直接看出候选有没有**语义串味**（急救 ≠ 复活、包扎 ≠ 群体引导）。

**落实**（2026-10-16，作者已进游戏确认）：

| 技能 | 素材 | 说明 |
| --- | --- | --- |
| 医疗敷料（0 环） | `FieldDressingFx` = 次级复原术 `RestorationLesser`（`621885df…`） | 替掉治疗轻伤绿光 |
| 紧急救治（1 环） | `EmergencyTreatmentFx` = 圣疗·他人 `LayOnHandsOthers`（`224fb8fd…`） | 法术本身即"获得博士圣疗的治疗效果"；**生命气息与复活术同款已弃** |
| Mon3tr（5 环） | `Mon3trCrystal`（先知启示·碎片爆发 `184fcfe5…`，重染暗墨绿黑）+ `Mon3trHeal`（治疗中伤 `e9399b6d…`） | ⚠ **光有"爆开"会被当成伤害法术** → 必须再叠一层治疗光 |

⚠️ **"爆开/爆发"型素材单独用在治疗法术上 = 看着像伤害法术**。要么换成"灌注/护罩"型，
要么像 Mon3tr 这样**再叠一层明确的治疗光**。此判据同样适用于将来任何"治疗却借了爆发素材"的场合。

## 8. ⚠ "自己死时触发"**不能**用原版 `UnitDeathTrigger`（连锁殉爆，2026-10-10 实测）

**症状**：连锁殉爆给敌人挂"标记"（`ChainDetonationMarkBuff`），标记增益上挂
`UnitDeathTrigger`（`m_DeathTrigger = OnUnitDeath`，`Actions = ContextActionChainExplosion`），
期望"标记者一死就在尸体处爆炸"。**实测：打死有标记的敌人，既没有引爆伤害也没有特效。**

**根因**（反编译 `Kingmaker.Designers.Mechanics.Facts.UnitDeathTrigger`）：
它是**"别的单位在我附近死掉时触发"**的组件，**明确排除"自己死"**：

```csharp
private void HandleEvent(UnitEntityData unit)
{
  if (unit == base.Owner) return;          // ← 死的是"自己"→ 直接返回，什么都不做
  if (Faction != 0) { ...敌我过滤... }
  if (CheckUnitTag) { ... }
  if (... && base.Owner.DistanceTo(unit) <= RadiusInMeters.Calculate(...))
    (base.Fact as IFactContextOwner)?.RunActionInContext(Actions, base.Owner);
}
```

⇒ 挂在**自己身上的**增益上时，**持有者自己死亡反而永远不触发**；而且 `RadiusInMeters`
没设时默认 **0**，就算想"别人死在我脚下"也很难命中。代码注释里写的"看自己死"是**误解**。

**正确做法**：用自定义组件（本 mod 已有现成样板：墨灵自爆 `InklingDeathBurst`）：

```csharp
public class ChainDetonationOnDeath : UnitBuffComponentDelegate
{
  protected override void OnDeactivate()      // 增益被移除时跑（死亡会移除增益）
  {
    var owner = Owner;
    if (owner == null || !owner.IsInGame) return;
    if (!owner.Descriptor.State.IsDead) return;   // ★ 用 IsDead 把"到期 / 被驱散"挡掉
    ChainDetonation.Explode(Context ?? Buff?.Context, owner);
  }
}
```

**要点**：
1. **`OnDeactivate` + `IsDead` 判定** 是"自己死才触发"的通用写法（与墨灵自爆同一套）。
   不用 `IsDead` 判定的话，增益**到期或被驱散**时也会触发（误爆）。
2. **敌我过滤用 `u.IsEnemy(caster)`，caster 取 `context.MaybeCaster ?? owner`**——
   ⚠ 别用 `u.IsEnemy(owner)`：`owner` 是那个死掉的敌人，**同阵营的其他敌人对它不算"敌"**，
   会一个都命中不到（测试木桩同阵营，最容易暴露这个坑）。
3. 原 `ContextActionChainExplosion` 已删除，逻辑搬到 `Components.cs` 的 `ChainDetonation.Explode`。
4. 诊断证据的分工：`Player.log` 里 `[RhodesDoctor-DIAG] RhodesDoctorSpellChainDetonation ... selected=N`
   只证明**法术选中了目标**，**不证明标记挂上了、更不证明死亡触发跑了**——三者要分开查。

---

## 9. ⚠ "只打条件标记"不等于"施加了那个效果"（时之锁 / 缓慢，2026-10-10）

**症状**：时之锁（`Kind.Debuff`）挂上后，**看不到任何特效，数值上也感觉不到"变慢"**。

**根因**：原实现只用了

```csharp
cfg.AddComponent<BuffStatusCondition>(c => {
  c.SaveEachRound = true; c.Condition = UnitCondition.Slowed; c.SaveType = SavingThrow.Will;
});
```

反编译 `Kingmaker.Designers.Mechanics.Buffs.BuffStatusCondition`：它 `OnTurnOn` 只做
`Owner.State.AddCondition(Condition, buff)`——**只是给单位打上"缓慢"这个条件标记**，既不套用原版增益、也不带任何数值。

而**原版"缓慢"的可感知效果全在 `Spells/Level3/SlowBuff`（GUID `0bc608c3…`，即 `Guids.Vanilla.SlowBuff`）上**：

| `SlowBuff` 的组件 | 作用 |
|---|---|
| `AddStatBonus{Dodge, AC, −1}` | AC −1 |
| `AddStatBonus{Dodge, SaveReflex, −1}` | 反射 −1 |
| `AddStatBonus{UntypedStackable, AdditionalAttackBonus, −1}` | 攻击 −1 |
| `AddCondition{Staggered}` + `AddCondition{Slowed}` | 蹒跚 + 缓慢 |
| `ChangeImpatience{Delta:−1}` | AI 行为参数（非玩家可见） |
| `FxOnStart = d6a8c6ae…` | **特效** |

⇒ 所以原来"缓慢"**数值没生效、也没有特效**。

**修法**：把 `SlowBuff` 的**可感知组件**照抄到时之锁的减益增益上（3 个 `AddStatBonus` + `AddCondition(Staggered)`，
条件仍用 `BuffStatusCondition` 以便"每轮可再豁免"），特效借 `SlowBuff` 的 `FxOnStart`
（`FxLibrary.DebuffAuraFor("LockOfTime")` → `SlowAura`）。

**教训**：
1. **`UnitCondition` 只是"标记"，不是"效果"**。原版很多状态（缓慢、蹒跚……）的**数值与特效在对应的增益蓝图里**，
   光 `AddCondition` 不会带过来。要复刻一个状态，**先反编译看原版法术实际施加的是哪个增益、里面装了什么**。
2. **原版法术的动作树会直接点名它施加的增益**：`Spells/Level3/Slow.jbp` 的失败分支是
   `ContextActionApplyBuff(m_Buff = 0bc608c3…)`——离线读 `blueprints.zip` 即可，不用进游戏。
3. 顺手结论：**"缓慢"= `SlowBuff`**，它的 `FxOnStart`（`d6a8c6ae…`）就是游戏里"被缓慢"的那圈视觉——
   别的法术想做"缓慢"观感，借它即可。
4. 附：**"蹒跚(Staggered)"的官方定义**（`enGB` 本地化）：每轮只能做一个移动动作**或**一个标准动作
   （不能两者都做，也不能做整轮动作）；自由/迅捷/直觉动作照常。

---

## 10. ⚠ 合书后的 10 环法术只能来自"道途合书特性自己的 10 环表"（2026-10-10）

**症状**：职业等级 20、神话 10、已合书，但**看不到职业的 10 环指令**。

**根因**：原实现把 10 环放在**职业法术书**的 `m_MythicSpellList` 上（`SpellbookConfigurator.SetMythicSpellList`）。
反编译确认这条路**永远不会生效**：

1. `BlueprintSpellbook.m_MythicSpellList` 带 `[ShowIf("IsMythic")]`——只有**神话法术书本身**（`IsMythic=true`，
   构造时 `m_Type = StandaloneMythic; AddSpecialList(blueprint.MythicSpellList)`）才用它。
   原版允许合书的 7 本职业法术书**全部** `IsMythic=false, m_MythicSpellList=None`。
2. 合书后，法术书靠 `Spellbook.AddSpecialList(道途表)` 拿到道途的 10 环法术，并把它们**自动已知**
   （`AddSpecial`）；这张"道途表"来自 `BlueprintFeatureSelectMythicSpellbook.m_MythicSpellList`。
   例：天使 = `Mythic/Angel/AngelMythicSpelllist`（第 10 环 4 个）。
3. 自发施法者的"已知法术"表（`MythicSpontaneousSpellsKnownTable`）**没有 10 环名额**，
   所以把 10 环放**主表**也学不到。
4. `GetMaxSpellLevel()`：最高环 = 9，**除非**角色有 `EnableSpellLevel10` 特性（合书才给）
   —— 这就是"未合书时 10 环天然隐藏"的原因。

**修法一（已废弃）**：把 4 个 10 环指令加进**各道途合书特性的 `m_MythicSpellList`**。
能生效，但合书特性是**原版共享蓝图** → **任何**与该道途合书的角色都会看到这 4 个指令，副作用太大。

**最终修法（作者 2026-10-10 定案：不走合书，改为职业 20 级直接授予）**：

| 需求 | 做法 | 原版参照 |
|---|---|---|
| 让 10 环在法术书界面显示 | `AddMechanicsFeature{ m_Feature = EnableSpellLevel10 }` | `Mythic/Angel/…/Angel10levelSpellsFeature` |
| 把 4 个指令给博士 | 4 × `AddKnownSpell{ m_CharacterClass=圣武士, SpellLevel=10, m_Spell=指令, m_Archetype=本变体 }` | —— |
| 10 环法位 | 自建"术士表 + 10 环列"的 `BlueprintSpellsTable`，法术书 `SetSpellsPerDay` 指向它 | —— |
| 授予时机 | 变体成长表 `(20, new[]{ CapstoneFeature, TenRingFeature })` | —— |

- `AddMechanicsFeature.m_Feature` 是**私有字段**（只有 getter）→ 反射写。
- `AddKnownSpell.m_Archetype` 让它**只对带本变体的角色生效** → 不污染其他角色（比改道途表干净得多）。
- **法术表必须有 10 环列**：`Spellbook.GetSpellsPerDay` 里 `count = SpellsPerDay.GetCount(CL, 10)`，
  表里没有第 11 列就返回 -1 → **直接 `return 0`**（连"额外法位"都加不上去）。
- `GetMaxSpellLevel()` = 9，**除非**有 `EnableSpellLevel10` → 未满 20 级天然看不到 10 环。

**教训**：
1. 判断"某机制该怎么接"时，**先看原版同类内容的数据/蓝图**（原版 7 本可合书职业法术书的字段、
   `Angel10levelSpellsFeature` 的组件），比猜 API 快得多。
2. **`BlueprintComponent` 的私有字段**（`m_Feature` / `m_Spell` / `m_CharacterClass` / `m_Archetype`）
   用反射写；BlueprintCore 的 `BlueprintTool.GetRef<T>(guid)` **直接返回 `T`**，可赋给这些字段。
3. **"只给自己的角色"优先用带 `m_Archetype` 过滤的组件**（`AddKnownSpell` / `AddSpecialSpellList` 都有），
   而不是去改原版共享蓝图。
4. **成长表授予（`SetAddFeatures` 的 `(20, …)`）对"旧存档已经是 20 级"不补发**——那一步早就走过了，
   新加的 20 级特性不会追发。想让**旧存档**立刻拿到，只能挂到**1 级就有的特性**上（加载时会重新激活），
   或者干脆用测试面板按钮发。

**当前落地（2026-10-10，作者定案：实验阶段从简）**：10 环**不自动授予**，测试面板（Ctrl+F10）加了
「给我 10 环指令」按钮 —— 直接 `Spellbook.AddKnown(10, 每个指令)` 发给队伍里带战术指令法术书的成员。
配套保留：`SpellbookSetup.CreateSpellsPerDayTable` 的自建"术士表 + 10 环列"每日法位表
（20 级 4 个 10 环法位；<20 级 10 环列写 -1 → 最高环到不了 10，天然隐藏）。

### 10.1 ★ 定案（2026-10-11）：10 环走**正常升级界面**获取，和别的环一模一样

**做法**：把**两张表**都补上第 11 列，剩下的交给原版升级流程。测试面板那个按钮退化成调试工具。

反编译确认的链路（`ApplySpellbook.Apply`，这是升级时发"已知法术"名额的地方）：

```csharp
for (int i = 0; i <= 10; i++) {                        // ← 本来就循环到 10 环
  int old = Math.Max(0, knownTable.GetCount(旧等级, i));
  int now = Math.Max(0, knownTable.GetCount(新等级, i));
  spellSelectionData.SetLevelSpells(i, Math.Max(0, now - old));   // 这一环给几个名额
}
int maxSpellLevel = spellbook.MaxSpellLevel;           // 可选的环上限，来自**每日法位表**
```

- **每日法位表**决定"最高能到几环"：`Spellbook.GetMaxSpellLevel()` 从 10 环往下找第一个
  `SpellsPerDay.GetCount(CL, n) >= 0` 的 n。**不需要 `EnableSpellLevel10`**
  （那个 flag 只在 `num == 9` 时兜底；全程序集只有这一处用它）。
- **已知法术表**决定"这一环给几个已知名额"：`BlueprintSpellsTable.GetCount` 对**列数不足**的行返回 **-1**
  → 原版术士表每行只有 0–9 共 10 列 ⇒ 10 环差值恒 0 ⇒ **10 环永远不出现在选择界面**（这就是"学不到"的真因）。
- 界面侧本来就支持：`SpellSelectionData.LevelCount = new SpellSelectionState[11]`；
  `CharGenVM.CreateNewSpellPhases` 遍历 `LevelCount[0..10]`，某环有槽位就建一个选择阶段；
  可选列表 = `SpellList.GetSpells(10)`（主法术列表第 10 环，按**下标**取，所以 `SpellsByLevel[10]` 必须在）。

**⚠ 门槛行是 21，不是 20**（这一条最反直觉）：博士的法术书等级比圣武士职业等级**多 1**。
2026-10-11 从作者存档 `Manual_3654…zks` 实测：

| 角色 | 职业等级 | 法术书 `m_BaseLevelInternal` |
|---|---|---|
| **博士（本变体）** | 圣武士 **20** | **21** |
| 原版圣武士 | 15 | 15 |
| 原版术士 | 10 | 10 |

多的这 1 级来自本变体 1 级那个 `AddSpellbook` 特性（`AddSpellbook.UpdateSpellbook` 按 `ContextRank`（= 圣武士等级）
把法术书等级往下推，而升级流程里的 `ApplySpellbook.AddLevelFromClass` 又加一次）。
所以"职业 20 级"对应的表行是 **21** —— 现在两张表统一用 `SpellbookSetup.TenRingRow`。

**副作用与注意**：
1. 因为 +1，博士的**每日法位整体比术士表早一级**（比如 1 级就有 4 个 1 环位，术士是 3 个）。
   这是既有偏差，本次**未动**（要修得去掉 1 级那个 `AddSpellbook` 特性 —— ⚠ 但删特性会触发
   `AddSpellbook.OnDeactivate → Descriptor.DeleteSpellbook`，**旧存档的法术书连同已学法术会被删掉**，
   所以必须另做迁移，别直接删）。
2. **成长表不补发旧存档**（本条 §10 教训 4 仍然成立）；"表 + 升级界面"这条路对**升级中的角色**天然生效，
   所以要给老朋友发 10 环只能**重训/洗点**，或继续用测试面板按钮。
3. 合书后的**神话等级提升**走 `ApplySpellbook` 的另一个分支（用道途的 `SpellKnownForSpontaneous` + 神话等级），
   那条路不给 10 环名额 —— 10 环只在**圣武士 20 级那一步**给，这是刻意的。

---

## 11. ⚠ 把偷来的增益分给友军时，别用技能自己的 `Context`（王冠的回响，2026-10-10）

**症状**：王冠的回响夺走敌人增益后往友军身上分，日志刷「**博士对测试增益·护甲免疫**」，增益挂不上。

**根因**：分配那行原来是
`allies[...].Buffs.AddBuff(taken[k].bp, Context, taken[k].left)` —— 用的是**技能自己的 `Context`**。
`BuffCollection.AddBuff(bp, MechanicsContext, …)` 内部是 `parentContext.CloneFor(blueprint, Owner)`，
于是新增益**继承了王冠的"影响心灵"描述符**。而 `RuleCanApplyBuff.OnTrigger` 里有：

```csharp
if (Context.MaybeCaster != null && Context.MaybeCaster != Initiator
    && GameHelper.IsAttackingGreenNPC(Context.MaybeCaster, Initiator)
    && Context.SpellDescriptor.IsNegativeEffect()
    && !Blueprint.GetComponent<IsPositiveEffect>())
{ CanApply = false; return; }
```

（另有一条 ExtraDecline 难度下的 `StoryModeImmunity`，同样以 `IsNegativeEffect()` 为前提。）
**影响心灵属于负面描述符** → `CanApply = false` → `BuffCollection.PrepareFactForAttach` 直接返回 null
（buff 不上），界面就显示"免疫"。

**修法**：改用**带施法者的重载** `AddBuff(bp, caster, duration)` —— 它走
`new MechanicsContext(caster, Owner, blueprint)`，**没有技能描述符** → 不被当负面效果。

**教训**：
1. `BuffCollection.AddBuff(bp, parentContext, …)` = **`CloneFor`**，会把**施加者技能的描述符/上下文一并带过去**；
   想让 buff"干净"，用 `AddBuff(bp, caster, …)`（或给 buff 蓝图加 `IsPositiveEffect`）。
2. 任何"把 A 的效果转给 B"的实现都要想一遍：**描述符会跟着走吗？**（影响心灵 / 胁迫 / 恐惧这些会被当负面效果拦）。
3. `RuleCanApplyBuff` 是"能不能上 buff"的唯一裁决点（由 `BuffCollection.PrepareFactForAttach` 触发）；
   上不去就查它。

## 12. ⚠ `Kind.Special` 的**有益**法术必须显式标 `Friendly=true`，否则目标判定反成"只能选敌人"（紧急救治，2026-10-10）

**症状**：紧急救治（治疗友军）在游戏里**只能选敌人**、选不了自己/友军。

**根因**：`CreateSpell` 里目标判定是
`bool beneficial = s.Kind is Kind.Heal or Kind.Buff || s.Friendly;`
紧急救治的 `Kind` 是 **`Special`**（它带慈悲 / 圣疗那一套自定义逻辑，不是纯 `Heal`），而 spec **没写 `Friendly=true`**
→ `beneficial=false` → `canFriends=false`、`canEnemies=true`，于是被当成**敌方单体**，
还被追加了 `AbilityTargetIsAlly(Not=true)`（"目标不能是友军"）——正好反了。

**修法**：spec 加 `Friendly=true`（同组的**紧急撤离 / 镜像流形 / 重构 / 文明的存续 / 永恒的注视 / 岁稔 / 影之疆域**都标了）。

**教训**：
1. `Kind.Special` 是"兜底 / 自定义"类——引擎**不知道**它是有益还是有害，目标归属**全靠 `Friendly` 这一位**。
   新加一个 `Special` 法术时**先问一句"目标是谁"**：友军/自己就 `Friendly=true`，敌方就不写。
2. `Kind.Heal` / `Kind.Buff` 自带"有益"，`Kind.Debuff` / `Kind.Special`(默认) 是敌方——
   也就是说**只有 `Special` 会有这个歧义**，别让"没写 = 有益"的直觉坑到。
3. 同源坑：`SetCanTargetEnemies/Friends` **只影响光标与描述，不真正拦截**，真正拦截靠 `AbilityTargetIsAlly`
   （`Not=true` 挡友军、`Not=false` 挡敌人）——所以目标判定写错，表现是"能点但没用 / 点错对象"。

## 13. ⚠ 自发施法者：`AddKnown` 之后**必须刷新自发法位**，否则法术"书里有、拖不出"（10 环 / 全部指令发放，2026-10-10）

**症状**：用测试面板按钮 `AddKnown` 把法术发给博士，**法术书里能看到**（角色面板正常列出），
但**灰掉、拖不到技能栏、也放不出**——"书里有、但用不了"。

**根因链**（反编译确认）：
- `MechanicActionBarSlotSpell.IsNotAvailable => !Spell.IsAvailable`（技能栏槽位"能不能用"看这个）；
- `AbilityData.IsAvailable = IsAvailableInSpellbook && IsAvailableForCast && !TemporarilyDisabled`；
- `IsAvailableInSpellbook` → `Spellbook.CanSpend()`；`IsAvailableForCast` → 可用次数 `!= 0`；
- 对**自发施法者**，这两者最终都落在 **`m_SpontaneousSlots[环] > 0`**
  （`SpendInternal` 里 `int num2 = m_SpontaneousSlots[num]; if (num2 > 0) return true;`；
  `GetAvailableForCastSpellCount` 里 `return GetSpontaneousSlots(spellLevel);`）。

**关键**：`m_SpontaneousSlots[]` **只在休息时刷新**——
`Spellbook.Rest()` 里 `for(i..) m_SpontaneousSlots[i] = GetSpellsPerDay(i)`，
构造函数里初始化成 **0**（`m_SpontaneousSlots[i] = 0`）。
所以**运行时光 `AddKnown` 而不刷法位**，法术虽然"已知"，但自发法位仍可能是 0 → `IsAvailable=false` → 灰掉/拖不出。

**修法**：发放后**调一次 `sb.Rest()`**（刷新 `m_SpontaneousSlots[i] = GetSpellsPerDay(i)`）。
测试面板「发放全部指令」已加这一步 + 诊断日志（打印每环"已知/法位/自发"）。

**教训**：
1. "已知法术"（`AddKnown`）和"能不能用"（`IsAvailable`）是**两套判定**——前者只决定"在不在书里"，
   后者还要过 `CanSpend` + 可用次数。**改了前者别忘了后者**。
2. 自发施法者的"可用次数"是**缓存的**（`m_SpontaneousSlots`），**只在 Rest 时重算**——
   任何"运行时改法术书"的操作都要想一句：**要不要跟着 `Rest()` 一下？**
3. 排查"书里有但用不了"的最快路径：从 `MechanicActionBarSlotSpell.IsNotAvailable`
   顺到 `AbilityData.IsAvailable` → `Spellbook.CanSpend` / `GetAvailableForCastSpellCount`，
   逐层看哪个条件 false（比进游戏猜快）。

---

## 14. ⚠ 特效/显示相关的三个新坑（2026-10-10，单体批次）

### 14.1 `L10n.S` 的键**必须同步进 `LocalizedStrings.json`**（否则显示 `[unknown key]`）

- `src/L10n.cs` 的 `L10n.S(key, zh, en)` **运行时只返回键字符串**（`"rhodesdoctor." + key`）；
  字符串本体在 **`LocalizedStrings.json`**（游戏原生本地化包，**手工维护、没有生成脚本**），
  由 `Content.LoadLocalizationPack` 在启动时加载。
- **改了代码、忘了加 JSON 键** → 游戏里显示 `[unknown key: rhodesdoctor.xxx]`
  —— **看着像乱码，其实是缺键**（梦魇 2026-10-10 踩到）。
- **凡改 `L10n.S`，必须同步往 `LocalizedStrings.json` 加一条 `{Key, zhCN, enGB}`。**
- 可复用校验：扫 `src/*.cs` 里所有 `L10n.S("…")` 键，与 JSON 的 `Key` 逐个比对（缺一个都会暴露）。

### 14.2 buff 的**显示名别用 `s.Name`**（会显示成法术名）

- `CreateExtraBuff(...)` 的第 3 个参数是 **buff 显示名**；传 `s.Name`（= 法术名）会让
  **"惊惧 / 动摇"这类状态 buff 显示成法术名**（敌人身上两条都叫"梦魇"），玩家认不出是状态（作者截图反馈）。
- 正确：用专属状态名 `L10n.S("spell.<法术>.<状态>.name", "…")`（照已有 20 个的写法，如"处决（蓄势）"）。
  2026-10-10 一次性把剩下 **23 个**用 `s.Name` 的改齐。

### 14.3 放大特效**别用根节点缩放**（`Local` 模式下"没变大、反而升高"）

- `Instantiate` 之后设 `transform.localScale = 2`：Unity 默认 **`Local` 缩放模式**下，
  **高于根节点的部件按比例抬高、粒子本身却不放大** → 观感就是"**没变大、位置升到半空**"（墨染山河实测）。
  改成 `Hierarchy` 缩放模式也救不回来（一次性爆发 FX 的粒子在生成当帧就发完了，改晚了）。
- **正确做法**：只放大**粒子本身** —— `main.startSize` 的 `curveMultiplier` + `shape.radius`，
  见 `FxTint.ScaleParticleSizes`；且 **保留素材自带的 `Light`**（`AnimatedLight` 素材删灯会报
  `Can't remove Light because AnimatedLight (Script) depends on it`）。

## 15. 单体批次 6–9 环（2026-10-10，收尾批）

9 个"无特效"法术全部接线，做法分三类（沿用前几批的现成通道）：

- **落点爆发（`ApplyAbilityFx` 加 switch 分支）**：
  - **黑蛇之焰** → `FxLibrary.Fire`（焰击术，已有元素）；
  - **镜中流形** → `FxLibrary.Shadow`（幻影杀手，已有元素）；
  - **影霄·拔刀** → **新元素 `FxLibrary.Death`**（借 **指骨术 `FingerOfDeath`**，`AbilitySpawnFx`=`e8569f44…`，
    Anchor=`SelectedTarget`；备选 `SlayLiving`=`cc9120c1…`，Anchor=`ClickedTarget`）。
    ——离线扫全库 `death / slay / finger` 类法术选出，见 §7.11.1 筛法。
- **常驻光环（`TintedAuraFx` 加表项，跟增益同生同灭）**：
  - **涌潮悲歌**（自增益，`Guids.Buff(36)`）→ 素材 `BarrierAura`（天使罩子）重染**潮汐蓝** `(0.35,0.75,1.45)`
    —— 语义合"60 临时生命＝护体潮水"；
  - **归溟**（自增益，`Guids.ReturnToAbyssBuff`）→ 素材 `PlasmaAura`（血雾，身体型）重染**深渊墨绿** `(0.10,0.34,0.46)`；
  - **画中世界**（敌方减益，`Guids.Buff(48)`）→ 素材 `GoldHalo` 重染**画境紫** `(0.68,0.42,1.25)`；
  - **旧日的战术家**（自增益，`Guids.TacticianEnduringBuff` / `TacticianAgileBuff`）→ `GoldHalo` **原色**（D 类同款金环，两条）。
    ⚠ 这两个增益的名字以 `TacticianEnduring/Agile` 开头，**不**以 `TacticianOfOld` 开头，
    所以 `CreateExtraBuff` 的 `ownerSpell` 反查匹配不上 `AuraSpells`；但 `TintedAuraFx` 是**按 buff GUID** 匹配，不受影响。
- **敌方减益光环（`FxLibrary.DebuffAuraFor` 加 case，`SetFxOnStart` 原色）**：
  - **终末狙击** → 复用 `MarkAura`（火之烙印，`FirebrandBuff`，"被做了记号"）；
  - **罪罚** → **新素材 `CurseAura`**（借 **降咒 `ArmyBestowCurseBlastBuff`**，`FxOnStart`=`9a65b11d…`；
    备选 `DoomOfServitudeBuff`=`9adb9cfa…`、`WitchHexDeathCurseBuff`=`f4adcaac…`）。
- **顺手补的已知缺口——炮火延伸后两轮缺 FX**：`ContextActionArtilleryStrike`（第 2/3 轮的追踪爆发）
  原先只结算伤害、不播 FX（首轮由能力自身的 `AbilitySpawnFx` 播）。补一行
  `FxTint.SpawnPlaced(FxLibrary.Fire, point, Quaternion.identity, 1f, 2.5f)`（原样实例化、不重染、自动销毁）。
- **离线选材 / 校验命令**（下次做单体批次照用）：
  - 扫"有 `AbilitySpawnFx` 的法术"：`python _tools/tmp/scan_spawnfx_kw.py <关键字…>`；
  - 扫"有 `FxOnStart` 的增益"：`python tools/scan_buff_fx_all.py <关键字…>`；
  - 校验本轮 9 个素材：`python _tools/tmp/verify_6_9ring_materials.py`（打印每个素材的锚点 / 时间 / AssetId）。

## 16. ⚠ 用 `UnitHelper.Copy` 造"复制体"必须**手动注册进场景**（镜中流形，2026-10-10）

**症状**：镜中流形（`ContextActionSpawnMirrorCopy`）用 `UnitHelper.Copy(target, true, false, true)` 复制目标、
再 `Context.TriggerRule(new RuleSummonUnit(caster, mirror, new Rounds(3)))`——进游戏**什么都没招出来**。

**根因**（反编译 `Assembly-CSharp.dll` 确认）：
- `UnitHelper.Copy` → `CopyInternal` 走 `Game.Instance.CreateUnitVacuum(unit.OriginalBlueprint)`：
  造一个**真空单位**，建视图、`TurnOn()`，但**不把它加进任何场景**（`CopyInternal` 里没有 `AddEntity`）。
- `RuleSummonUnit.OnTrigger`：`PreMadeUnit ?? EntityCreator.SpawnUnit(...)`——**有 PreMadeUnit 时直接用它、也不注册**；
  只有"自己 `SpawnUnit`"那条路才会注册。
- ⇒ 复制体是个"**不在场景里的孤儿**"：不渲染、不参与战斗（正是作者看到的"没召唤出东西"）。

**修法**：`Copy` 之后补一行
```csharp
Game.Instance.EntityCreator.AddEntity(mirror, caster.HoldingState);
```
（`AddEntity` 把视图挂到场景根 + 并入创建队列；`state` 传 null 会退到 `State.LoadedAreaState.MainState`。）
另：阵营 / 队伍 / 召唤计时 / 到期解散**都由 `RuleSummonUnit` 负责**，**不要**再手写 `SwitchFactions` / `GroupId`
（重复且与规则冲突）。

> 可复用结论：**凡"引擎自带 Copy 造单位"的路子，都要自己把单位注册进场景**——`Copy` 只负责"造"，
> 不负责"放进世界"。判断方法：`ilspycmd -t <类型> <Assembly-CSharp.dll>` 看 `CopyInternal` 里有没有 `AddEntity`。
> 另：`CreateView()` 是按单位**当前 `Position`** 摆视图的，真空单位默认在**原点**，所以要先 `mirror.Position = target.Position`
> 再建/挪视图；注册后最好再 `UnitPlaceOnGroundController.ForcedTick(mirror)`（原版 `SpawnEntityWithView` 也做这一步）。
> ③ 作者 10-11：分身要"**法术位全有、也可以使用**"——`UnitHelper.Copy` 的 `CopySpellbook` 只抄法术书结构
> （等级 / 已知法术 / 特殊表），**不保证槽位可用**；所以复制后对每个法术书 `sb.Rest()`（满法术位）、
> `mirror.Descriptor.Resources.FullRestoreAll(null)`（满资源），并**不再施加行为锁**
> （旧 `MirrorLockBuff` = 禁施法 + 禁超自然/非凡主动能力，蓝图保留但已不挂）。

## 17. ⚠ 游戏的中文字体是"静态图集"，没烘焙进去的字**渲染不出来**（归溟 → 只显示"归"，2026-10-10）

**症状**：`归溟` 在游戏里只显示成"归"（第二个字不见了）。

**根因**：游戏的中文字体是 **TextMeshPro 的静态 FontAsset**（`Bundles/ui_china_font`，内部是**预烘焙的字符图集**，
`m_AtlasPopulationMode = 0`（Static）、`m_SourceFontFileGUID` 为空），**没有动态补字、也没有兜底字体**。
包里有**两个**字体资源，游戏合起来用：

| 字体 | 字符数 | 备注 |
|---|---|---|
| `SourceHanSerifSC` | 2048 | 主字体（常用字）：`归`/`潮`/`海` 在这里 |
| `SourceHanSerifSC-Rare` | 2382 | 生僻字字体：`黎`/`汐`/`澜` 在这里 |

**证据**（`UnityPy` 读 `m_CharacterTable`，可复跑脚本）：
- **`溟`(U+6E9F)、`黍`(U+9ECD) 两个字体都没有** → 渲染为缺字（空白）。
- 交叉验证：游戏 `Wrath_Data/StreamingAssets/Localization/zhCN.json` 里 `溟`/`黍` 出现 **0 次**；
  而本模组里**只有这两个字**是游戏本地化从没用过的。

**结论**：`归溟` 的"溟"在游戏里注定显示不全——**改代码 / 改 JSON 都没用**（JSON 是对的，是**字体没有这个字形**）。
已按作者要求换字：`归溟` → **`潮`**（法术名 / 增益名），`岁稔` 描述里的署名 `黍` → **`黎`**。

**可用的做法**：**换字**（最稳）——先把候选字用脚本确认在（任一）字体里再改。
运行时注入字形（`TMP_FontAsset.TryAddCharacters`）依赖源字体文件，本 FontAsset 没有，**此路不通**。

**自查脚本**：`python _tools/tmp/check_font_glyphs.py [候选字…]`
（① 比对"模组用字 vs 游戏 zhCN.json"；② 读字体包两个 FontAsset 的 `m_CharacterTable` 逐个核对）。
⚠ 别用"在字体包二进制里搜字符码字节"那种土办法——`归` 这种明明有的字也会搜不到（假阴性），
**必须走 `read_typetree()['m_CharacterTable']`**。

## 18. ★ "放逐 N 轮再放回"怎么做：`IsInGame` 开关 + **施法者侧**计时器（画中世界，2026-10-10）

需求（作者定案）：把敌人**放逐**几轮——这几轮它**不能动、我们也打不到它**，到期后**原位返回**并施加**麻痹**。

**没有现成动作**：原版放逐术（`Spells/Level7/Banishment`）失败就是 `ContextActionKill`（对异界生物直接杀），
没有"移除 N 轮再放回"的机制；游戏里也没有 Maze 法术。

**可用的原语**：`UnitEntityData.IsInGame = false` —— 把单位移出游戏：
- 反编译 `UnitEntityData.OnIsInGameChanged` 确认：会 `Descriptor.Buffs.SetActive(false)` /
  `ActivatableAbilities.SetActive(false)` / `Abilities.SetActive(false)`、退出编组 → **不能动**；
- 从场上移除 → **不可被选为目标**；
- 再 `= true` 即原位放回（游戏里宠物随主人开关就是这么用的）。
- 另可 `unit.View?.SetVisible(false, true)` 保证模型看不见。

**⚠ 关键坑：计时器不能挂在被放逐者身上。** `IsInGame = false` 会把它的**增益一起停用**，
挂它身上的增益走不完时长、也就永远"回不来"。⇒ **载体增益挂施法者**，由它的组件在
`OnDeactivate`（时长到 / 被驱散 / 施法者死，都会走）里把目标放回来，并补一个麻痹。

**实现**：`PaintedWorldBanishTracker : UnitBuffComponentDelegate<PaintedWorldBanishTrackerData>`
（`src/Components.cs`）—— `OnActivate` 读 `Context.Root.MainTarget.Unit`、记住 `UniqueId`、`IsInGame=false`；
`OnDeactivate` 按 `UniqueId` 找回、`IsInGame=true` + `UnitHelper.AddBuff(麻痹, TimeSpan)`。
蓝图侧见 `SpellsSetup` 的 `PaintedWorld` 分支（两个新 GUID：`PaintedWorldBanishBuff` / `PaintedWorldParalysisBuff`，
0x9a / 0x9b）。**待进游戏验证**（尤其"移除后模型是否真的看不见 / 能不能被点"）。

## 19. 持续时间统一：16 个"固定轮数"法术改为"1 轮/级"（作者定案，2026-10-11）

作者："镜中流形这种技能都是固定回合，但其实都该改成轮/级"——共 **16 个**：坚守阵地 / 骑兵突进 / 寒霜祷言 /
不稳定血浆 / 反重力 / 过载模式 / 钙质化 / 时之锁 / 圣域 / 奇美拉 / 涌潮悲歌 / 儿时的舞乐 / 镜中流形 /
归溟(潮) / 画中世界 / 旧日的战术家。

做法（"1 轮/级" = **圣武士等级**轮，见 `BuffRank`）：
- **Spec 加 `PerLevel=true`**（把原来的 `Rounds=N` 换掉）：`CreateSpell` 的 Buff/Debuff 分支会自动用
  `ContextDuration.Variable(ContextValues.Rank(), Rounds)`，并给能力挂 `BuffRank(l=>l)`。16 个 Spec 全部加上。
- **Meta 的 `DurationKey` 改 `rhodesdoctor.duration.round1level`**（表头"持续时间"显示用）。
- **`Kind.Special` 的要手动改效果里的时长**：
  - 钙质化 / 儿时舞乐：`SpawnAreaEffect(..., Fixed(N))` → `Variable(ContextValues.Rank(), Rounds)`；
  - 镜中流形：`ContextActionSpawnMirrorCopy` 里 `new Rounds(3)` → `new Rounds(ContextValues.Rank().Calculate(Context))`；
  - 归溟 / 画中世界 / 旧日的战术家：`ApplyBuff(..., Fixed(N))` → `Variable(ContextValues.Rank(), Rounds)`。
- **描述**：`LocalizedStrings.json` 里提及时长的几条 + 源码内嵌描述，`N 轮` → `1 轮/级`。
  ⚠ **只改这 16 个**——**岁稔 / 炮火延伸仍是固定轮数**（不在名单里），别用全局替换误伤。

## 20. ⚠⚠ 扫"单位"别用 `CurrentScene.AllEntityData`：**队伍成员不在里面**（重构/文明的存续，2026-10-12 定位）

> **症状**：作者"重构没有特效了（它应该是每个受影响单位各一份）/ 文明的存续有特效但似乎没有效果"。
> 两者是**同一个根因**：`ContextActionMassRestore` 一个受影响的单位都没找到。

### 20.1 证据链（`Player.log`，10-11 17:25 那次）

```
群体恢复完成：中心=(345.6,40.0,341.9) 半径=9.1m 活着恢复=0 复活=0/0 后续增益挂上=0 PerUnitFx=有
[特效尺寸] 素材 394fedae…（= AngelHolyHymn 的 FX）      ← 文明：施法者处的圣光确实播了
群体恢复完成：… 活着恢复=0 复活=0/0 后续增益挂上=0 PerUnitFx=无
```

- 施法者就站在中心（半径 9.1 m），**连施法者自己都没被算进去** ⇒ 不是"筛选条件太严"，是**根本没扫到人**。
- 两发法术的"施法者处爆发"都播了（重构那次是 PerUnitFx，因为一个单位都没有 → 画面上什么都没有）；
  `复活术` 的 FX（素材 `ee0c5b93…`）在**上一个版本**（15:39 那次，还是"施法者处一发"）是能看见的 ⇒ 素材没问题。
- 对照：同一台机器上 `Diag.cs` 的 `AbilityTargetsAround.Select` 补丁打过
  `RhodesDoctorSpellTidalVigil anchor[unit=博士] selected=3 :: 博士,索希尔,拉米恩`
  —— **原版选目标机制找得到 3 个队友**，说明队友确实在场上，只是**不在我们扫的那份列表里**。

### 20.2 根因

`Game.Instance.CurrentScene` = `State.LoadedAreaState`（`AreaPersistentState`），
它的 `AllEntityData` **只含区域场景状态**里的实体（敌人 / 中立 NPC / 区域内召唤物）。
**队伍成员住在 `Player.CrossSceneState`**（`SceneName = "<cross-scene>"` 的独立 `SceneEntitiesState`，
`Player.AllCrossSceneUnits => CrossSceneState.AllEntityData.OfType<UnitEntityData>()`）——
他们必须跨区域跟随玩家，所以**从来不在区域状态里**。
（本 mod 自己在 `ContextActionRemoveMyBuffs` 的注释里早就记过这条："AllCrossSceneUnits 只含队伍成员，
敌人身上的旧标记扫不到"。）

原版 AoE 选目标用的**不是**区域状态，而是全局单位池：

```csharp
// Kingmaker.Designers.GameHelper
public static IEnumerable<UnitEntityData> GetTargetsAround(Vector3 point, float radius, bool checkLOS = true, bool includeDead = false)
{
  foreach (UnitEntityData unit in Game.Instance.State.Units)   // ← 原版的来源
    if ((!unit.Descriptor.State.IsDead || includeDead) && !unit.Descriptor.State.Features.IsUntargetable
        && unit.IsUnitInRange(point, radius, checkLOS)) yield return unit;
}
```

⇒ **一律用 `Game.Instance.State.Units`**（`PersistentState.Units`，`EntityPool<UnitEntityData>`）。

### 20.3 定案做法：`UnitScan`（`src/UnitScan.cs`）

```csharp
var snap = UnitScan.Take();          // = State.Units ∪ Player.AllCrossSceneUnits ∪ Player.Party（去重 + 挡掉 Descriptor 为 null 的半成品）
var units = snap.Units;              // List 快照：可以边遍历边加增益 / 复活
Main.Mod.Logger.Log(snap.Describe()); // "State.Units=… 跨场景队伍=… Party=… 去重后=…"
```

- ⚠ **这是全局池**，别的区域残留的单位也可能在里面 ⇒ 调用方**必须**自己按距离（或 `UniqueId`）筛选，
  与原版 `IsUnitInRange` 同理。
- **友军判定**：沿用 R6 的 `IsPlayerFaction`（不含中立 NPC），再补 `caster.IsAlly(u)`
  （与原版 `AbilityTargetsAround` 的友军判定同源；玩家阵营的召唤物 / 同组单位也算友军）。
- 本轮改到的三处（**同类根因，一次修完**）：
  1. `ContextActionMassRestore`（重构 / 文明的存续）——报告的那两条现象；
  2. `ContextActionStealBuffsCustom`（王冠回响"把偷来的增益分给 60 尺内友军"）——原来永远找不到友军，
     全部回落到施法者自己；
  3. `PaintedWorldBanishTracker.FindUnit`（画中世界按 `UniqueId` 找回被放逐者）——原来**队友**被放逐后
     永远找不回来（日志"找不到被放逐的单位（可能已离场），跳过返回"）。
- **没动的**：只找**敌人**的自扫（炮火延伸 / 延时爆破 / 王者冲锋 / 连锁殉爆）——敌人在区域状态里，
  本来就扫得到，保持原样（改动越少越好）。
- **诊断**：`ContextActionMassRestore` 现在每次施放多打一行
  `群体恢复·扫描：State.Units=… 跨场景队伍=… Party=… 去重后=…；友军=… 其中 30 尺内=…（阵亡 …）；最近单位：博士0.0m(友) …`
  ——下次若还是 0，一眼分清"池子是空的"还是"阵营筛错"。


---

## 21. ⚠⚠ "恶灵两个形态能共存 / 结束恶灵只取消一个"：**UI 闸门 ≠ 硬保证**（2026-10-11 作者实测）

**症状**（作者 20 级实测）：把"敏捷形态"和"体质形态"**连续点开**，两个形态**共存**；
点"结束恶灵"**只取消其中一个**；而且这时**指令法术可以正常施放**。

### 21.1 这条症状其实混了三件事，别混着修

| 现象 | 判定 | 处理 |
|---|---|---|
| 两个形态共存 | **真 bug** | 动作用**硬互斥**（见 21.2） |
| 结束恶灵只取消一个 | **真 bug** | 改成**一个自定义动作一次性删完**（见 21.3） |
| 这时能放法术 | **它才是"对的"那一侧** | 20 级本来就该能放（Capstone 压制锁定）；**平时不能放才是 bug** → 见 **21.5** |

> ⚠ 作者后来补充：**20 级单开一个形态时是不能施法的**，他之所以觉得双形态状态是 bug，正是因为
> "双形态下能放法术、单形态下不能放"这种**不一致**。追下去发现：双形态那次是"**歪打正着**"——
> 它恰好复现了规则要的行为（20 级恶灵期间可施展全部指令），而正常路径反而是坏的。见 21.5。

### 21.2 共存：别只靠 `IAbilityCasterRestriction`

形态开关上原本挂的是 `AbilityCasterHasNoActiveFacts`（`IAbilityCasterRestriction`）。
这类组件的判定链是**可靠的**（反编译确认 `UnitUseAbility.OnAction` 里
`if (!ForceCastOnBadTarget && !Ability.IsAvailable) return ResultType.Fail;`，
`IsAvailable → IsAvailableForCast → CanBeCastByCaster` 会逐个跑 `Blueprint.CasterRestrictions`），
但**它只回答"这个按钮能不能点"**——只要列表本身有问题（名单不全 / 名单在运行时为空 /
UI 状态没刷新就点下去），状态就会坏掉。作者实测的共存说明它**没有拦住**，
所以**不能**把"同时只有一个形态"这条规则托付给 UI 层。

> **⚠ 同日补充（做"旧日的战术家不给空放"时反编译复核）**：`UnitUseAbility.OnAction` 的**第一句**就是
> `if (!ForceCastOnBadTarget && !Ability.IsAvailable) { SpawnInterruptFx(); return ResultType.Fail; }`，
> 而 `IsAvailable → IsAvailableForCast → CanBeCastByCaster` **确实**逐个跑 `Blueprint.CasterRestrictions`
> ⇒ 这类限制在**施法路径上是生效的**，返回 Fail 时**不会消耗法术位**（`Ability.Spend()` 在后面才走）。
> 也就是说：**限制组件可以用来做"硬闸门"**（见 §23），而不是只能做 UI。
> 那为什么恶灵形态开关当初没被拦住？**真实原因仍未定位**（当时没打日志），但因为已经加了动作层硬互斥，
> 结果不受影响。**下次遇到"限制没拦住"，先补一行日志再判断**，别把结论下死。

**定案做法**（`src/Actions.cs` · `ContextActionApplySpecterForm`）：把判断放进**动作执行时** ——

```csharp
// 施法者身上已有任意形态增益（六个之任一，含自己要开的那个）→ 什么都不做（记一条日志）
// 否则 caster.Buffs.AddBuff(bp, Context, null)   // null = 永久，等价于 ContextActionApplyBuff{Permanent:true}
```

- 名单用 **GUID 字符串（去横线小写）** 比对，不用引用对象的类型解析（`BlueprintTool.GetRef<T>` 虽然
  `GetRef` 只写 `deserializedGuid`、`Guid => deserializedGuid` 是对的，但字符串比对更省心）。
- 名单必须是**全部六个**形态增益（敏捷/体质 × 一/二/三段）——旧写法只列"本阶段那一对"，
  于是**升到 12/16 级换阶段时**（V1 形态还在、V2 的闸门只认 V2 增益）也能共存。
- 顺带把**余波限制**（`AftermathRestrictBuff`）也列为"有就不能开"（规则：余波期间不能重开恶灵）；
  20 级余波被压制 ⇒ `IsSuppressed` 的不算 ⇒ 仍可立即重开 ✓。
- 有意**不**在动作里删别的形态："切形态"会触发旧增益的结束动作 = 余波，人还在恶灵里被"恶心"（V10 的老坑）。

同一处 `AbilityCasterHasNoActiveFacts` 的名单也一并改成**全部六个**（让按钮置灰与硬互斥一致）——
但记住它只是体验层。

### 21.3 "结束恶灵"只删一个：**能力是被它要删的增益授予的**

原实现是六个 `ContextActionRemoveBuff` 排成动作队列：

```csharp
foreach (var guid in new[]{ AgileBuffV1, EnduringBuffV1, AgileBuffV2, EnduringBuffV2, AgileBuffV3, EnduringBuffV3 })
  act.RemoveBuff(BlueprintTool.GetRef<BlueprintBuffReference>(guid));
```

而"结束恶灵"能力本身是**形态增益用 `AddFacts` 授予**的 —— 删掉那个形态增益的瞬间能力就被销毁，
**动作队列后面的项不再执行**。所以"点一次只取消一个形态"（V3 两个形态在队列末尾，
删掉第 5 个后第 6 个就轮不到了）。

**定案做法**（`src/Actions.cs` · `ContextActionEndSpecterForms`）：一个动作里**先快照、再一次性发起全部
`Remove()`**，不把控制权交回动作队列；余波仍由各形态增益自己的 `deactivated` 动作负责。

```csharp
var hits = caster.Buffs.RawFacts.ToList().Where(名字在六个里).ToList();
foreach (var b in hits) b.Remove();          // 同一帧内全部发起
Main.Mod.Logger.Log($"结束恶灵：移除 {hits.Count} 个形态增益（…）");
```

> 通用教训：**"能力由 A 授予、而动作要删 A"时，别把删 A 和后续动作排在同一队列里** ——
> 要么全部塞进一个自定义动作一次性发起，要么把能力改挂到更稳定的特性上。

### 21.4 怎么看有没有生效（`Player.log`）

```
恶灵形态已开启：<增益名>                                   ← 正常开启
恶灵形态：身上已有「<增益名>」，忽略本次开启（<要开的>）。    ← 硬互斥拦下（= 原来能共存的那种操作）
结束恶灵：移除 N 个形态增益（<名字…>）                      ← N 应为 1；若 >1 说明存档里已经存在过共存状态
```

### 21.5 ★★ 真凶：20 级 Capstone 用的是**普通压制**，对"之后才出现的锁定"完全无效

**症状**（作者实测）：20 级开恶灵形态 → **不能施放指令**（本该"可施展全部指令"）。
副作用同理还有：结束恶灵后**仍被余波限制**、穿透仍然**倒扣**、指挥灵光**不生效** —— 因为这四件事
全都挂在同一个"锁定增益"上（`LockBuffV3`：`AddSuppressBuffsFixed` 压灵光 + 各组件读锁定决定穿透/豁免/施法限制）。

**根因**（BlueprintCore `SuppressBuffsFixed` / `UnitPartBuffSuppressFixed` 反编译）：

| 模式 | 构造 | 行为 |
|---|---|---|
| 普通压制 | `new SuppressBuffsFixed()`（默认 `ApplyToNewBuffs = false`）→ `AddNormalEntry` | **只压制"登记那一刻已经存在"的增益**，并把那份名单（`EntityFactRef<Buff>` 列表）记下来 |
| **持续压制** ✅ | `new SuppressBuffsFixed(applyToNewBuffs: true)` → `AddContinuousEntry` | `ActivateSuppression(owner)` 立刻扫一遍现有的，另有 Harmony 补丁挂在 `Buff.OnAttach` 上：**之后新挂上来的匹配增益也会被压制** |

升级到 20 级的那一刻身上**并没有锁定**（恶灵没开）⇒ `AddNormalEntry` 记下的是**空名单** ⇒
之后每次开恶灵**新生成**的 `LockBuffV3` 根本不在压制范围里 ⇒ 20 级"人灵合一"整个失效。

**修法**：`SpecterSetup` 两处改成持续压制 ——
1. `CreateCapstone`：`new SuppressBuffsFixed(applyToNewBuffs: true).ApplyToBuffs(LockBuff, LockBuffV2, LockBuffV3, AftermathRestrictBuff)`
   （顺带把一段锁定 `LockBuff` 也列进来，旧存档里可能残着 8–11 级开的形态）；
2. `Lock()`：**锁定对灵光的压制**也改成持续压制 —— 灵光载体增益是**特性授予的事实**，
   每次升级/特性重算都可能重新挂一遍，普通压制挡不住新挂的那份。

**旧存档不用重训**：`SuppressBuffsFixed.OnPostLoad` 会重跑 `OnActivate`，于是持续条目在**读档时重新登记**
（`AddContinuousEntry` 按 `source.UniqueId` 去重，不会重复堆）；原来那条空的普通条目留着也无害。

**验证（`Player.log` / 游戏内）**：20 级开恶灵 → 指令**全部可用**（按钮不再变灰）、
点"结束恶灵"→ **没有**"只能移动"的限制、重开立刻可以；18–19 级仍然只有【恶灵】指令可用。

---

## 22. ⚠ 别用"改机制"去解决"看不到特效"（旧日的战术家，2026-10-11 回滚）

**症状来源**：作者要求把 9 环【恶灵】指令"旧日的战术家"**改回原设计**。

**它被改坏的过程**（原始记录在 `report-fx-6-9ring-revise2.md` §7）：
当轮任务只是"给单体法术补特效"，作者反馈"就没看到特效"。当时的处理不是去查"为什么光环没生成"，
而是**把法术的机制改掉**——原文写着"原来不在恶灵形态时法术空放（什么都不给 → 没增益、没光环）……
现改为：**无形态时两套一起给** ⇒ 任何情况下都会挂上增益，也就一定会有蓝色光环"。
于是法术在**形态外也**变成一份独立的大 buff（敏捷 + 体质两套齐全），彻底偏离设计。

**规则原文**（`docs/rhodes-doctor-archetype.txt` 十二、9 环）：
> 【旧日的战术家】【恶灵】……迅捷动作；个人；目标：你；1 轮/级（原文 3 轮，作者 10-11 统改为轮/级）。
> 你同时获得敏捷形态与体质形态的全部收益；转移量额外提高 2。该法术不解除恶灵对普通指令的限制。

**正确口径（本次恢复，`SpellsSetup` 的 `TacticianOfOld` 分支）**：
恶灵期间**同时只能有一个形态**（§21），所以"同时获得两形态收益" = **形态已给的那一半 + 法术补的另一半**：
- 在**体质（坚韧）形态** → 补**敏捷**套件；在**敏捷形态** → 补**体质**套件；
- **不在任何形态** → **什么都不给**（空放；它是【恶灵】指令，设计上就是给"已有半个"的博士补齐）。
- ⚠ **别改成"两套无条件一起给"**：在形态内会与形态自身的加值**叠成双份**（这正是"补对端"存在的原因）。
- "转移量额外 +2"的落地：补的那一半用 `SpecterSetup.TransferRank(false, 4)`（= (转移量+2)×2）给属性，
  再给**形态自己那个属性** +4（转移量 +2 → ×2），两边都吃到 +2。

**教训（可复用）**：
1. **"看不到特效"先查特效链路**（`Player.log` 里 `[RhodesDoctor] …光环已生成。`、素材借用成功/失败），
   不要动机制；机制改动的副作用往往在**几轮之后**才被作者发现，还会顺手污染其它功能。
2. 改机制前先回读**规则文档那一节**，并看一眼该法术的**上一版实现注释**——很多注释里就写着"为什么只能这样写"。
3. 若为了调试确实需要临时放宽条件：**在注释里写明"临时 / 待回滚"**，并在同一轮报告里列进"待回滚"清单。

---

## 23. 用 `IAbilityCasterRestriction` 做"硬闸门"：不满足条件就**不给放、也不扣法位**（2026-10-11）

**需求来源**：旧日的战术家是"补当前形态缺的另一半"，**不在恶灵形态时施放毫无意义** ——
作者："没形态也别空放，直接不给放，不然要消耗法术位太亏了。"

**做法**（`src/Components.cs` + `SpellsSetup.CreateSpell`）：

```csharp
// 新组件：身上**至少有一个**指定增益在生效时才允许使用（与已有的 AbilityCasterHasNoActiveFacts 相反）
public bool IsCasterRestrictionPassed(UnitEntityData caster)
  => caster != null && Facts.Any(f => f != null && BuffHelper.HasActive(caster, f));

// 挂到能力上（旧日的战术家：六个形态增益任一在场即可）
ability.AddComponent<AbilityCasterHasAnyActiveFact>(c => c.Facts = SpecterSetup.FormFactRefs());
```

**为什么这就够**（反编译 `UnitUseAbility.OnAction`）：
1. 按钮置灰：`AbilityData.GetUnavailableReason()` 会把限制的 `GetAbilityCasterRestrictionUIText()`
   作为禁用原因显示（本组件 = "需要先开启恶灵形态"）；
2. **点了也不会白扣**：`OnAction()` 第一句就是 `if (!ForceCastOnBadTarget && !Ability.IsAvailable) return ResultType.Fail;`
   —— `IsAvailable` 会跑 `Blueprint.CasterRestrictions`，而**扣法位的 `Ability.Spend()` 在后面才执行**。

**适用场景**：任何"条件不满足时施放毫无意义/只会浪费资源"的能力。比"施法时 ForceFail"体验好
（按钮直接灰、并给出原因），比"动作里判条件后什么都不做"更省资源（后者已经把法术位花掉了）。

---

## 24. ★ "快捷栏里有三套恶灵形态"：`AddFacts` 的 post-load **恢复分支**把被删的承载补回来了（2026-10-11）

**症状**（作者 20 级实测）：快捷栏里出现 **三套**"敏捷形态 / 体质形态"开关（共 6 个）。用哪套都能开，
所以不显眼，但 **V1/V2 那几套是旧版**（V1 = 半额转移量 + 移动动作，V2 = 移动动作）。

### 24.1 证据（从存档直接读，不靠猜）

`party.json` 里博士同时带着 `TogglesV1Holder`、`TogglesV2Holder`、`TogglesV3Holder` 三个承载
（以及它们各自 `AddFacts` 出来的 6 个开关）—— 也就是说 **`RemoveFeatureOnApply` 的删除"没留住"**。

### 24.2 根因：`AddFacts.DoNotRestoreMissingFacts` 默认为 `false`

阶段特性（8 / 12 / 16 级）靠 `RemoveFeatureOnApply(上一阶段的承载)` 换发新版开关。删除本身**是生效的**
（`RemoveFeatureOnApply.OnActivate → Owner.RemoveFact`，读档时事实会重新 `Activate()`，所以它每次都跑）。
问题在**之后的 post-load 修复**：

```csharp
// Kingmaker.UnitLogic.FactLogic.AddFacts
protected override void OnApplyPostLoadFixes()
{
    if (base.Fact.Active) UpdateFacts(postLoad: true);   // ← 读档时每个事实都会跑
}
private void UpdateFacts(bool postLoad)
{
    ...
    if (!postLoad || !DoNotRestoreMissingFacts)          // ← 默认 false ⇒ 条件恒真
        foreach (var fact in Facts)
            if (!Data.AppliedFacts.HasItem(f => f.Blueprint == fact))
                list2.Add(fact);                          // ← 把"已被删掉"的承载重新造出来
}
```

`Data.AppliedFacts` 里那条被删的记录会先被 `RemoveAll(f => … !f.IsAttached)` 剔掉，
于是 `HasItem` 判定失败 → **旧承载被重新挂上**。一读档就长回三套。

> 关键点：**"删掉"和"不让它回来"是两件事。** `RemoveFeatureOnApply` 只管前者。

### 24.3 修法（两处，互补）

1. **从源头掐掉恢复**：阶段特性授予承载的那个 `AddFacts` 加 `doNotRestoreMissingFacts: true`
   （`BlueprintCore` 的 `AddFacts(facts, casterLevel, doNotRestoreMissingFacts, …)` 第 3 个参数）。
   读档时 `UpdateFacts(postLoad:true)` 的条件变成 `(!true || !true) = false` ⇒ 不再补回。
   注意**只影响读档**：正常激活（`postLoad:false`）仍会补齐 —— 洗点回到 8 级依然能拿回 V1 开关。
2. **读档再收敛一次**：新增组件 `EnforceSpecterStage`（`src/Components.cs`），挂在 **二段 / 三段特性**上，
   列出生效时"应该被清掉的旧承载"（二段清 V1Holder；三段清 V1Holder + V2Holder）。
   它在 `OnActivate` 与 `OnApplyPostLoadFixes` 两个时机各跑一次 ⇒ **旧存档**（存档里已经存着三个承载）
   一读档就被收敛成一套。

**旧存档不需要重训 / 洗点**：读档即生效。

### 24.4 日志怎么看（`Player.log`）

本组件不加日志（避免每次读档刷屏）。判断方法是**进游戏看快捷栏**：只应剩一组
"巴别塔的恶灵：敏捷形态 / 体质形态"。若还多，看 `Info.json` 版本是否为 **0.5.8+**、以及是否真的重启了游戏。

### 24.5 通用教训

- 用 `AddFacts` 授予"之后可能被删掉"的事实（换阶段 / 换版本 / 一次性能力）时，
  **默认会读档复活** —— 要么给 `doNotRestoreMissingFacts: true`，要么自己收敛。
- 换阶段的"标准写法"（`RemoveFeatureOnApply`）**不等于**状态收敛；二者要分开确认。
- 查这类问题**先读存档**（`party.json`）确认"事实到底在不在"，比在代码里推演快得多。

---

## 25. 规则文档 / 英文文本：按代码做了一次**全表 diff**（2026-10-11）

作者："持续时间是我想改的每/级，只是规则文档没更新……你可以随便更新一下。"

上一轮只对齐了**持续时间**；这一轮写了个脚本，把 `SpellsSetup` 的每个 `Spec`（施法时间 / 距离 / 半径 / 直线 / Meta 的 `DurationKey`）
与 `rhodes-doctor-archetype.txt` / `rhodes-doctor-en.txt` 的行**逐条比对**，共修 **29 处**：

- **以施法者为中心**（原来文档写"近距内选一点为心"，代码是 `Range = Personal` + `AoERadius = 30`）：
  坚守阵地 / 骑兵突进 / 乌拉 / 圣域 / 潮汐守望 / 岁稔 / 旗帜不倒 / 永恒的注视 / 重构 / 文明的存续；
- **半径**：大地之怒 60 → **30**（且改以你为中心）、天灾降临 80 → **40**；
- **时长**：牵制射击（中）1 轮 → **1 轮/级**、大地之怒（英）1 round/level → **instantaneous**；
- **王者冲锋**：距离"个人 / 两倍速度" → **40 尺（选一点）/ 至多 40 尺**（代码 `MaxDistanceFeet = 40`）。

**判定口径**：只在代码**显式写了**该字段时才对文档动手（`Range = …` / `AoERadius = …` / `Cast = …`）；
代码用默认值的（例如 0 环几个法术的"距离 60 / 30 尺"只是文案，实际是默认 `Close`）**不动文档**，只记录。
剩余 4 条经核对是**合理差异**，保持原样：迅捷突破"加速 3 轮"（标题时长 1 轮、加速增益确实 3 轮）、
王者冲锋距离（`Range = Custom` 是刻意的落点施法）、终末狙击"直到下轮开始"（延迟命中）、时空静止"表观 1d4+1 轮"（直接施放原版时间停止）。

---

## 26. 四个"文档 vs 代码"收尾项（含两条**上一轮误判的更正**，2026-10-11）

作者："补代码距离。天灾降临改成整轮。旗帜不倒改成轮/级。4也改改。"

### 26.1 ⚠ 先更正上一轮的两条错判

- **天灾降临不是"没设施法时间"**：`SpellsSetup` 里早有 `if (s.Key is "Catastrophe") ability.SetIsFullRoundAction(true);`。
  上一轮的比对脚本只读了 `Cast=` 字段，**没读 `IsFullRoundAction`** ⇒ 误判。教训：**整轮动作不是 `CommandType`**
  （枚举只有 `Free/Standard/Swift/Move`），它是 `BlueprintAbility.m_IsFullRoundAction`（BlueprintCore: `SetIsFullRoundAction`）。
- **旗帜不倒实际就是 1 轮/级**：效果走 `ContextDuration.Variable(ContextValues.Rank(), Rounds)`，
  `Spec.Rounds=5` 在那条分支里**不参与计算** ⇒ 我上一轮"只持续 5 轮"的说法是读数据表读错了。
  本轮只把 `Rounds=5` 清理成 `PerLevel=true` 并加注释。

### 26.2 实际改动

- **新增 `Spec.RangeFeet`**：`>0` 时自动 `SetRange(AbilityRange.Custom)` + `SetCustomRange(new Feet(RangeFeet))`。
  反编译确认 `BlueprintAbility.GetRangeInFeet` 的 `case AbilityRange.Custom: return CustomRange;`。
  落成：战术标记 **60** 尺、信标投射 **30** 尺、牵制射击 **30** 尺；王者冲锋的 40 尺也挪到这条通用路径。
- **0 环整组**显式补 `Cast=UnitCommand.CommandType.Standard`（此前靠默认值，数据表看不出）。

---

## 27. 《描述规范：游戏文本的"内在逻辑"》（2026-10-11）

作者："这个 mod 描述非常不专业且不清晰。你多去找找不同类型的官方法术、能力、特性，然后总结一个描述内在逻辑。"

产出 **`docs/description-style-guide.md`**。取证方式（可复用）：

1. **官方文案库**：`Wrath_Data/StreamingAssets/Localization/zhCN.json`（`{"strings": {"<guid>": "文本"}}`，21 MB）。
2. **蓝图字段 → 文本 GUID**：`blueprints.zip` 里的 `m_DisplayName / m_Description / LocalizedDuration /
   LocalizedSavingThrow` 等是 `{m_Key: "<guid>"}` 或 `{Shared:{stringkey:…}}` 形式，取出后再去 (1) 查文本。
3. **反编译 UI**：`TooltipTemplateAbility.Prepare` 证明**头区是自动拼的**
   （`School/Range/ActionType/LocalizedDuration/LocalizedSavingThrow/SpellResistance/SpellDescriptor`），
   正文只取 `Blueprint.Description`。

**结论（三条铁律）**：
1. **正文不写头区字段**（学派/施法时间/距离/目标/持续/豁免/法术抗力）—— 头区由蓝图字段自动渲染，重复写会过期；
2. **游戏术语必须带 `{g|Encyclopedia:…}…{/g}` 链接**（官方 32963 处；本 mod 0 处），
   状态用 `{g|ConditionXxx}`、法术互引用用 `{g|SpellsXxx}`、换行用 `{n}`；游戏文本**不认 Markdown**；
3. **术语按官方**：减值（不是"罚值"）、抗性（不是"抗力"）、钝击（不是"轰击"）、麻痹（不是"定身"）；
   不留作者署名、不写实现细节（"（实现为…）"）。

三类文本差异：**法术**头区完整；**类法术/超自然能力**无学派环级但要写"每天 N 次"；
**职业特性/专长**无头区、纯散文、先写"X 级起"。

---

## 28. 描述改写落地：114 条 `.desc` 全部重写（2026-10-11）

承接 §27 的规范，作者："你改"。

### 28.1 做了什么

- **114 条** `.desc` 过了一遍（中文改 97 条、英文改 93 条）；**手写状态栏 27 → 0**、
  **作者署名 62 处 → 0**、**实现细节 1 → 0**、**术语链接 0 → 中文 259 / 英文 196 处**；
  术语：罚值→减值、轰击→钝击、定身→纠缠、火焰抗力→火焰抗性；`【10 环】`前缀删除（`【恶灵】`保留）。
- 脚本：**`_tools/tmp/relink.py`**；原始备份：**`_tools/tmp/LocalizedStrings.orig.json`**。

### 28.2 三个坑（值得记住）

1. **`{g|}` 链接必须"单遍替换 + 占位符"**：若按规则逐条 `re.sub`，先插入的标记会被后面的规则**再次匹配**，
   产出嵌套（第一版就出现 `{g|Saving_Throw}{g|Saving_Throw}saving throw{/g}s{/g}`）。
   做法：每次命中先替换成 `\x00i\x01`，全部规则跑完再还原。
2. **`/tmp` 不持久**（写完再读就没了）。中间文件要放 **`_tools/tmp/`**；本次靠
   **"部署目录里的那份 JSON"**（部署早于编辑）做还原点才救回来。
3. **整轮动作不在 `CommandType` 里**（见 §26.1），比对脚本别只看 `Cast=`。

### 28.3 还没做的

- **进游戏先验一条 `{g|}`**（战术标记）：确认链接可点、且头区没有与正文重复。
  回滚：`cp _tools/tmp/LocalizedStrings.orig.json LocalizedStrings.json && bash tools/deploy.sh`。
- 代码里 `L10n.S(key, zh, en)` 的**文本参数已与 JSON 脱节**（运行时只返回键）；
  建议下一轮写脚本从 JSON 反向同步，或删掉这两个参数。
- `MirrorLockBuff`：动作已不施加（行为正确），`SpellsSetup` 里的 `a.LockBuff = …` 与
  `SummonsSetup` 的蓝图都是**死引用**，可择日清理。

---

## 29. 收尾：诊断日志改成**编译期开关**（2026-10-11）

作者："到收尾阶段了，清理那些日志检测，以免占用内存。"

### 29.1 做法：`[Conditional]` 而不是"运行时 if"

新增 **`src/Dbg.cs`**：

```csharp
internal static class Dbg
{
  [Conditional("RD_VERBOSE")]
  internal static void Log(string message) => Main.Mod.Logger.Log(message);
}
```

**要点**：带 `[Conditional]` 的调用在**未定义符号时被编译器整体删除** —— 连参数字符串的
拼接 / 插值都不执行 ⇒ 发布版**零分配、零调用**。写成 `if (Dbg.On) Log($"…")` 是**没用的**，
插值字符串照样先分配。需要排查时：`dotnet build -p:DefineConstants=RD_VERBOSE`。

### 29.2 三条经验

1. **"把方法里的日志改掉"不等于"省掉开销"**：`FxTint.DumpColorsOnce` / `AreaGroundFx.LogInventory` /
   `SmokeFx.Inspect` 这类**纯诊断方法**，必须**给方法本体加 `[Conditional]`**，调用点才会消失；
   否则方法照跑（照样 `GetComponentsInChildren` 遍历、照样拼 StringBuilder）。
2. **删日志要连带删"只为它存在的变量"**：那 9 条 `[RhodesDoctor-DIAG]` 里带着 `near`/`hit` 计数器、
   `ipe`/`can` 布尔 —— 留着就是纯粹的浪费与误导。
3. **诊断埋点常有"只增不减"的副作用**：`FxTint` 的 `SizeLogged / ColorLogged / PostLogged`
   三个 HashSet 就是**每次生成 FX 都塞 key**（长局内持续增长）。埋点一停它们就不再增长 ——
   清理日志时顺手查一遍这类集合。

### 29.3 保留清单（有意不删）

- **全部 `Logger.Error`**（80 处）。
- **一次性启动日志**：内容注入 ×6 / 蓝图注入 / 法术表就绪 / 测试木桩就绪 / 图标文件缺失。
- **恶灵状态 3 条**（`恶灵形态已开启` / `忽略本次开启` / `结束恶灵：移除 N 个`）——§21.4 的验证锚点，触发量极小。
- **测试面板日志**（玩家主动触发）。

结果：`Logger.Log` **132 → 24**；诊断 **100 处**走 `Dbg.Log`（发布版被裁）。
验证方式：直接 `grep` 编出的 DLL 里的 UTF-16 字符串，确认 `RhodesDoctor-DIAG` / `染色诊断` /
`特效借用成功` 等已消失，而 `蓝图注入完成` / `图标文件缺失` 仍在。

---

## 30. ★ 自绘"整段演出"的自定义特效：扫描波 + 逐单位对时（重构 / 文明的存续，2026-10-16）

> 前二十九节的做法基本都是"借原版 prefab → 克隆重染"，形状是原版定的、时间轴也是原版定的。
> 本节记录**另一条路**：形状和时间轴**全自己画**，只用那份已验证能上屏的材质。
> 代码：`src/MassRestoreFx.cs`（第十二批；报告 `report-fx-10-16-recon-civ-special.md`）。

### 30.1 什么时候该走这条路

- 要的观感**在原版素材里找不到**（"冷静的扫描重建"这种语义，借素材只能借到"复活术之光"）；
- 需要**多个部件按自己的时间轴配合**（扫描波 → 逐单位收拢 → 螺旋 → 爆发）；
- 需要**"波扫到谁、谁才开始"**这种与距离相关的对时——原版 prefab 给不了。

### 30.2 材质：仍然只认那一份

```csharp
var mat = SwordStreakFx.StreakMaterialPlain(col);   // 剑雨/王者冲锋实机验证过
if (mat == null) return;                            // 拿不到就整段不画（不抛、不静默重试）
```

- **别回头去试 `Shader.Find`**（本 mod 两次什么都没画出来）；**也别借巨型粒子素材**（见 `SwordStreakFx.cs` 文件头）。
- `StreakMaterialPlain` 会把 `_MainTex` 换成自生成软边贴图 ⇒ **传入什么颜色，画出来就是什么颜色**
  （不做这一步会被原版贴图的色相带偏）；按颜色缓存，多次调用不会漏材质。
- 圆环宽度用 `widthMultiplier`（**局部**单位，会乘宿主 `lossyScale`）；这里自建物体缩放是 1，所以传米数即米数。

### 30.3 四个自绘小件的要点

| 组件 | 关键点 |
| --- | --- |
| `RdRingAnim`（圆环） | `LineRenderer` + `useWorldSpace` + `loop`，**每帧重算 72 个顶点**；半径用 `1-(1-k)²` **缓出**；延时期间 `_lr.enabled=false`，否则会先闪一个半径 0 的点 |
| `RdRiserAnim`（螺旋光尾） | `TrailRenderer` + **物体本身沿螺旋线移动**（顶点自动留痕，不用手动 `AddPosition`）；`height` 传负即"自上而下"；到点后 `emitting=false` 再 `Destroy(gameObject, tr.time + 0.1f)` 等尾迹淡完 |
| `RdLinkAnim`（弧线丝线） | 二次贝塞尔，控制点 = 中点**上抬** ⇒ 自然下垂感；`alignment = LineAlignment.View` 让它始终面向相机，细线在任何视角都看得见；"从 A 长到 B"= 按 `reach` 比例截取贝塞尔曲线 |
| `RdDelayed`（延时一次） | 纯计时器，用来把爆发对到"波扫到"的那一刻；异常要 catch + `Logger.Error`，绝不让它打断演出 |

### 30.4 对时的算法（本批最有复用价值的一段）

"扫描波扫到谁、谁才开始演出"= 每个单位一个 `t0`：

```csharp
float t0 = Flat(unit.Position - center) / WaveSpeed;   // 波前半径 == 距离 的时刻
```

- `Flat()` **把 y 归零再取模**：波是贴地扩散的，判定也必须只看平面距离（否则站在高处/低处的单位会对错时）。
- 之后所有延时都是相对于**自己的 `t0`** 而不是绝对时间：`Delayed(t0 + 0.55f, …)`。
- **特效的"起点位置"要在单位位置取，而不是 `center`**：扫描波只是"发令枪"，演出发生在各自身上。

### 30.5 视觉入口挂在哪：给动作加"风格"开关，而不是堆 `PerUnitFx`

- `ContextActionMassRestore` 加了 `public string Style`：**空 = 旧行为（`PerUnitFx` / `GoldFlash` 原样跑）**，
  非空 = 收集 `affected` 后交给 `MassRestoreFx` **统一演出**。这样新观感不会影响其它调用方，回归风险为 0。
- 需要"整段演出"时必须**从动作层拿全量视角**（中心 / 半径 / 全部受影响单位 / 图层）——
  逐单位的 `SpawnPerUnitFx(unit)` 拿不到这些。
- ⚠ `MassRestoreFx` 在 `namespace RhodesDoctor`，而 `Actions.cs` 在 `RhodesDoctor.Components`：
  命名空间嵌套所以能直接引用；**反过来**（在 `RhodesDoctor` 里写 `ContextActionMassRestore` 的短名）才需要 `using`。
- 多部件生成时**每个部件一个空 GameObject + 一个组件**，各自计时各自销毁；不要试图用一个总控脚本管所有部件
  （谁先销毁、谁被异常打断都会变成隐性耦合）。

### 30.6 两个必须记住的坑

1. **新增的成功日志默认看不见**：`Dbg.Log` 带 `[Conditional("RD_VERBOSE")]`（§29），
   本次两条 `…·专属特效：…` 在发布版里**连字符串都不存在**。
   验证新增字符串是否编入时用 UTF-16LE 搜（`…重构·专属特效…` 搜不到是**正常的**），
   或者干脆按 §29 编一份 `RD_VERBOSE` 版。**别把"日志没出现"当成"代码没编进去"。**
2. **"看着能改朝向、其实没被用"的参数要删掉**：本次手里的 `Ring(..., bool flatY)` 从头到尾没被读过，
   留着会让下一个人以为能画竖直环。删掉 + 注释写明"环一律平躺"。
   同理，**验收前先 `grep` 一遍新参数的使用点**。

### 30.7 配色：加法混合下"深色必然发淡"

- 这份材质很可能走**加法混合**：绯红 / 暗紫这类**低亮度色**折算到屏幕上会很淡，
  而冷白 / 青白 / 白金天然占便宜。
- 想显眼：**提亮度**（`(0.9,0.10,0.22)` → `(1.0,0.25,0.35)`）或**加粗线宽 / 提高 alpha 峰值**，
  而不是去"提高饱和度"（加法混合下饱和度不解决亮度问题）。
- **参数集中在一个文件顶部**（`MassRestoreFx` 的 `CivCrimson` / `CivViolet` / `CivGold` /
  两个 `WaveSpeed` / `crownH`）——现场微调只改那一处，别散到 `SpellsSetup` 里。
