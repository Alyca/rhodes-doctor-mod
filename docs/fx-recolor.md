# 借用原版特效并运行时改色（不改动原特效）

> 维护者备忘。适用：想复用游戏本体某个特效的外观，但需要换色/换调（例：把恶臭云的
> 紫云复用成烟幕掩护的灰白烟），同时**绝对不能改动原版特效**。
> 参考实现：`src/FxBorrow.cs`（借引用）、`src/SmokeFx.cs`（克隆重染 + 生命周期）。

---

## 一、总体思路

1. **借**：运行时从原版蓝图取 `PrefabLink`，不写死 `AssetId`（原版改版也不会失效）。
2. **克隆**：`Instantiate` 一份，只改克隆；模板永远只读。
3. **重染**：把克隆里所有可能带着色的地方改为目标色；**贴图先复制再改**。
4. **挂生命周期**：跟承载它的区域/单位同生同灭（订阅事件总线）。

关键红线：**原贴图、原材质、原 prefab 一律不碰**；改动只落在克隆实例上。

---

## 二、借引用（不写死 GUID）

`BlueprintCore` 的各 `*Refs` 是**静态类**，成员是 `static readonly Blueprint<...>`。
要放进 `params string[]` 之类，用 `.ToString()` 取 GUID 字符串。

```csharp
using BlueprintCore.Blueprints.References;
using BlueprintCore.Utils;

// 借区域常驻 FX
if (BlueprintTool.TryGet<BlueprintAbilityAreaEffect>(AbilityAreaEffectRefs.StinkingCloudArea.ToString(), out var bp)
    && bp != null && bp.Fx != null)
  link = bp.Fx;

// 借增益常驻循环光
if (BlueprintTool.TryGet<BlueprintBuff>(BuffRefs.AngelicAspectBuff.ToString(), out var b)
    && b != null && b.FxOnStart != null)
  link = b.FxOnStart;
```

按顺位给多个候选，逐个 `TryGet`，第一个拿到的即用（见 `FxBorrow.AreaFx/BuffAura`）。

`PrefabLink` 加载：`link.Exists()` 判断存在，`link.Load(ignorePreloadWarning:false, hold:true)` 拿 `GameObject`。

---

## 三、两种“挂特效”方式，别混用

| 方式 | 何时用 | 做法 |
|---|---|---|
| 引擎 `BlueprintAbilityAreaEffect.Fx` | **保持原色**、让引擎自己播/管生命周期 | 直接 `area.Fx = link` |
| 运行时克隆重染 | **要改色** | `area.Fx` **留空**，自己 `Instantiate` 并重染 |

⚠️ 两者同时上会**原色特效照旧上屏**（克隆盖不住它），必须二选一。

---

## 四、用事件总线挂生命周期

区域生成/结束分别派发 `IAreaEffectHandler`。订阅一次常驻即可。

```csharp
using Kingmaker.PubSubSystem;
using Kingmaker.EntitySystem.Entities;

internal sealed class MyWatcher : IAreaEffectHandler {
  public void HandleAreaEffectSpawned(AreaEffectEntityData e) => Spawn(e);
  public void HandleAreaEffectDestroyed(AreaEffectEntityData e) => Destroy(e);
}

// Main.Load 里：
EventBus.Subscribe(new MyWatcher());
```

用 `area.Blueprint.AssetGuid` 比对**自己**那个区域蓝图的 GUID 来过滤（去横线小写再比）。

数据结构：`Dictionary<AreaEffectEntityData, GameObject>`；结束时 `Object.Destroy(go)`。

---

## 五、重染：把“所有”着色来源都覆盖

只看 `startColor` 或 `_Color` 是不够的——Owlcat 的 `Owlcat/Particles` 着色可能藏在
**贴图**或 **colorOverLifetime** 里。稳妥做法是“一把梭”。

### 5.1 粒子系统

```csharp
foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) {
  var main = ps.main;                       // MainModule 是 struct 代理，可赋值
  main.startColor = Tinted(main.startColor, target);   // 覆盖 Color/TwoColors/Gradient/TwoGradients
  var col = ps.colorOverLifetime;
  if (col.enabled) col.color = Tinted(col.color, target);
}
```

`MinMaxGradient` 有 4 种模式，都要处理；**只换 RGB、保留 Alpha**，否则烟雾透明度全变。

### 5.2 材质：遍历 shader 的全部属性

```csharp
var sh = mat.shader;
for (int i = 0; i < sh.GetPropertyCount(); i++) {
  var nm = sh.GetPropertyName(i);
  switch (sh.GetPropertyType(i)) {
    case UnityEngine.Rendering.ShaderPropertyType.Color:
      mat.SetColor(nm, target); break;
    case UnityEngine.Rendering.ShaderPropertyType.Texture:
      if (LooksLikeDataMap(nm)) break;      // 法线/遮罩/流动跳去色
      var t = mat.GetTexture(nm);
      if (t != null) mat.SetTexture(nm, Desaturated(t));  // 复制+去色
      break;
  }
}
```

- **必须遍历属性**，而不是只改 `_Color`/`_MainTex`；我们的紫就藏在 `_MainTex` 之外的链路里。
- `LooksLikeDataMap`：名字含 `normal/bump/mask/flow/distort/erosion` 的跳过（去色会破坏其通道语义）。
- 主材质与拖尾材质都要：`ParticleSystemRenderer.material`、`psr.trailMaterial`。

### 5.2.1 贴图"去色成灰白" vs "染成目标色"（别搞混）

`FxTint` 对贴图有两种处理，按用途选：

- **去色成灰白**（`Desaturated`，`Apply` 默认）：`new = Lerp(lum, 1f, lift)`，把贴图变成灰白、向白提亮。
  用于"只要灰白/单色"的场合（SmokeFx 的灰白烟）。
- **染成目标色**（`TintedTexture`，`Apply(..., tintTextures: true)`）：`new = rgb * lum`（lum 是原像素亮度），
  保留纹理明暗细节、把色相换成目标色。**用于"藏在贴图里的着色也要跟着变色"的场合**。

⚠️ **之前漏掉的通道（2026-10-09 已补，待进游戏确认）**：只染粒子 `startColor`/材质 `_Color` 不够，
加速术的 `FxOnStart` 里**人物底部的拖尾**很可能是**缎带类**（`TrailRenderer`/`LineRenderer`）——
顶点渐变与材质色**相乘**，只染材质不染渐变的话，绿色顶点色 × 蓝色材质 = 还是偏绿。
`SpawnAttached` 已默认 `tintTextures: true`，`Apply` 现一并染缎带起止色/渐变、粒子
`colorBySpeed`/`trails`、类颜色名 Vector 属性与自带脚本颜色字段，并在染色前打一次
`[染色诊断]` 日志，进游戏放一次即可确认链路。

### 5.3 改材质是安全的

`Renderer.materials` / `psr.material` 返回的是**该渲染器实例化的材质副本**（Unity 语义），
改它不影响共享的源材质。这是“不动原特效”的关键。

---

## 六、贴图去色（复制 + 改副本）

**绝不原地改共享贴图**。复制一份再改：

```csharp
static Texture2D Desaturated(Texture src) {
  // 1) 取像素：可读贴图直接 GetPixels；否则 RenderTexture + Blit + ReadPixels
  Color[] px = (src is Texture2D t && t.isReadable) ? t.GetPixels() : BlitRead(src);
  // 2) 去色：亮度 + 向白提亮
  for (int i = 0; i < px.Length; i++) {
    var p = px[i];
    float lum = 0.299f*p.r + 0.587f*p.g + 0.114f*p.b;
    float v = Mathf.Lerp(lum, 1f, Lift);   // Lift=0 纯灰(偏深)，=1 纯白
    px[i] = new Color(v, v, v, p.a);       // 保留 Alpha
  }
  // 3) 落成新贴图
  var outTex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
  outTex.SetPixels(px); outTex.Apply(false);
  return outTex;
}
```

- 按源贴图缓存（`Dictionary<Texture, Texture2D>`），失败也缓存 `null` 免得反复报错。
- `BlitRead` 用 `RenderTexture.GetTemporary(w,h,0,ARGB32)` + `Graphics.Blit` + `ReadPixels`；
  读完 `RenderTexture.active` 复位、`ReleaseTemporary`。

**贴图与恶臭云共用也没关系**：我们改的是副本，恶臭云仍是紫的。

---

## 七、定位“颜色到底藏在哪”的诊断法

改色没效果时，先打印**全部**着色来源，别猜：

- 每个 `ParticleSystem`：`main.startColor.mode/color`、`colorOverLifetime.enabled`；
- 每个材质：遍历 shader 属性，打印所有 `Color` 属性的值、所有 `Texture` 属性的贴图名。

我们就是这样定位到“`startColor` 全白、`_Color` 全白 → 紫色在贴图/其它属性”。诊断代码见
`SmokeFx.Inspect`。

---

## 八、易踩的坑清单

- **`Object` 歧义**：文件里同时 `using System;` 和 `using UnityEngine;` 时，
  `Object.Instantiate/Destroy` 会 CS0104。写全 `UnityEngine.Object.*`。
- **`*Refs` 是静态类**：`params AreaEffectRefs[]` 这种用法会 CS0719，应传 `.ToString()` 的 GUID 字符串。
- **`area.Fx` 与克隆重染二选一**，否则原色照旧显示。
- **只改 `_MainTex`/`_Color` 不够**：Owlcat 特效的色可能在 `colorOverLifetime` 或其它贴图属性。
- **贴图必须复制**：直接改共享贴图会污染所有用它的特效。
- **`MinMaxGradient` 四种模式都要处理**，且只换 RGB 保留 Alpha。
- **UMM 界面**：`modEntry.OnGUI = ...`，`GUILayout` 在 `UnityEngine.IMGUIModule.dll`——csproj 要加该引用。
- **事件订阅**放 `Main.Load`，用 `EventBus.Subscribe`；订阅对象常驻即可。

---

## 九、本项目里的落点

- `src/FxBorrow.cs`：`AreaFx(params string[])` / `BuffAura(params string[])`，借引用 + 日志。
- `src/SmokeFx.cs`：`SmokeFxWatcher`（区域事件）+ `SmokeFx`（克隆重染、贴图去色、生命周期）。
- `src/SpellsSetup.cs` 的 `Smokescreen` 分支：`area.Fx` 留空、写入 `SmokeFx.SourceLink`。

调参：`SmokeFx.Grey`（目标灰白色）、`SmokeFx.Lift`（去色后向白提亮，越大越白）。

---

## 十、"改色改不对"时先读这条（骑兵突进踩出来的，2026-10-09）

上面一~九节讲的是**手法**（怎么把颜色刷进去）。但**"刷进去了"和"屏幕上对了"是两件事**——
骑兵突进为此走了 7 版弯路。完整方法论已单独成节：**`dev-notes.md` §7.10**。这里只放四条最容易踩的：

1. **屏幕色 ≠ 你填的颜色**。tint 被顶点色 × 材质 `_Color` × 贴图**相乘**了多次，
   还叠混合模式与 HDR 钳位。先**从截图量出映射**（拟合幂次/系数）再反推目标色，别凭直觉填。
2. **"抬 B 提亮"是陷阱**。B 一过 1 就先撞 255 钳位，R/G 还低 → 渲成"电光蓝"，比原来更冲。
   想浅只能**抬 R/G**往白走。
3. **同一 prefab 的不同部件可能映射相反**。骑兵突进里：周身缎带 ≈ `tint^1.8`（想浅抬 R/G），
   地面拖尾 ≈ `tint × 暖色内容`（内容偏金黄，**tint 越白越压不住暖色**，想蓝要给更强的蓝）。
4. **部件归属没把握时，别"挑一个改"**。改成 **"全局打底 + 只对已确认的部件点名点染"**——
   对"不知道是哪个"免疫。⚠️ `ApplyTo` 打出"特效点染：粒子 'XXX'"**只证明名字匹配上了，
   不证明改对了部件**；判定标准是**用户看到画面对应位置变了**（与 §7.2 第 1 条同源）。

> 两条命令级结论：**只要"整份一起染"能达到目标，就不要拆**；**拆的时候，把"确定"的那个挑出来特殊化，
> 而不是把特殊色送给"猜"的那个**。
