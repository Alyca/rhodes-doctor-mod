# 医疗敷料 / 紧急救治 / Mon3tr 特效替换（2026-10-16）

> 作者反馈：**医疗敷料（0 环）/ 紧急救治（1 环）/ Mon3tr（5 环）三个技能全都在冒原版「治疗轻伤」的牧师绿光**，
> 其中 Mon3tr 尤其不对——它本应是凯尔希的**神经结晶 / 黑色结晶体**，却在冒牧师绿光。
> 编译：`dotnet build -p:WrathPath="D:\steam\steamapps\common\Pathfinder Second Adventure"` **0 警告 0 错误**；
> 已部署（旧 DLL 备份 `RhodesDoctor.dll.bak-20261016-072637`，新 DLL md5 `7D06545C0144D6FAE3ACD73D65E50ADC`，
> 与 `bin\Debug\net48` 产物一致）。**作者已进游戏确认**。

## 一、根因

三个技能在 `SpellsSetup.ApplyAbilityFx` 的 `s.Key switch` 里**都映射到同一个素材**：

```csharp
"FieldDressing"      => FxLibrary.Heal,   // 治疗轻伤 CureLightWounds 的绿光
"EmergencyTreatment" => FxLibrary.Heal,
"Mon3tr"             => FxLibrary.Heal,
```

`FxLibrary.Heal` 借的是原版 `CureLightWounds` 的 `AbilitySpawnFx`（`61602c5b…`）。
本身不是 bug（原先按元素共用一个"治疗通道"），但**语义不分**——0 环包扎、1 环急救、5 环的"黑色神经结晶"看起来一模一样。

## 二、改法（每个技能一份独立素材，全部运行时从原版蓝图借，不写死 AssetId）

| 技能 | 原 | 现 | 素材出处 |
| --- | --- | --- | --- |
| 医疗敷料（0 环） | `Heal` 绿光 | `FxLibrary.FieldDressingFx` | **次级复原术** `RestorationLesser`（`621885df…`，锚目标） |
| 紧急救治（1 环） | `Heal` 绿光 | `FxLibrary.EmergencyTreatmentFx` | **圣疗·他人** `LayOnHandsOthers`（`224fb8fd…`，锚目标） |
| Mon3tr（5 环） | `Heal` 绿光 | 重染分支：`FxLibrary.Mon3trCrystal` **+** `FxLibrary.Mon3trHeal` | 结晶＝**先知启示·碎片爆发** `OracleRevelationShardExplosionAbility`（`184fcfe5…`）；治疗光＝**治疗中伤** `CureModerateWounds`（`e9399b6d…`） |

- **紧急救治**选圣疗的理由：法术描述本身就是"目标获得**博士圣疗**的治疗效果（可附加慈悲）"，语义最贴。
- **Mon3tr**：`ContextActionSpawnTintedFx` 把晶片爆发克隆重染成**暗墨绿黑** `(0.16, 0.26, 0.22)`
  （纯黑会把粒子整片压没，见 `dev-notes.md` §7.11.5，所以留一点青绿让结晶轮廓可见），
  **再叠一层**治疗中伤的原色治疗光（`AbilitySpawnFx`，锚目标）。两层同时出现，解决"只看爆开像伤害法术"的问题。
- 只动视觉：治疗量、解除震慑/恍惚/麻痹/恶心、慈悲链等机制一律未动。

### 改过的文件

- `src/FxLibrary.cs`：新增 `FieldDressingFx` / `EmergencyTreatmentFx` / `Mon3trCrystal` / `Mon3trHeal` 四个元素。
- `src/SpellsSetup.cs`：`ApplyAbilityFx` 里换掉两个映射；Mon3tr 移到"重染"分支并叠治疗光；
  新增重染色常量 `Mon3trCrystalBlack`。`FxLibrary.Heal` 仍保留给**医疗无人机的每轮治疗闪光**等场合。

## 三、被否掉的候选（别再拿回来）

- **生命气息 `BreathOfLifeTouch`（`ee0c5b93…`）**：一度用于紧急救治，作者指出它**与复活术 / 死者复活是同一份特效**
  （离线复核属实：`RaiseDead`、`Resurrection`、`RitualBreathOfLifeAbility`、`HunterRaiseCompanionAbility` 共用这一份）→ 弃用。
- 教训：**治疗类素材不能只看名字**。"同一份 FX 被哪些法术引用"要离线反查（见 `dev-notes.md` §7.11.1），
  否则会出现"急救和复活长得一模一样"这种语义串味。

## 四、离线选材依据

扫 `blueprints.zip` 全部 `BlueprintAbility` 的 `AbilitySpawnFx`，按 `PrefabLink.AssetId` 归并"同一份素材被谁引用"，再按语义挑：

- 治疗系素材并非只有一份：治疗轻伤 `61602c5b…`、治疗中伤 `e9399b6d…`、治疗重伤 `d95f8959…`、
  治疗致命伤 `474cb5ac…`、治疗术 `319f5078…`、圣疗 `224fb8fd…`、复原术 `621885df…`、生命气息 `ee0c5b93…`。
- **治疗中伤（`e9399b6d…`）与治疗轻伤（`61602c5b…`）不是同一份**——这是 Mon3tr"叠一层治疗光"能成立的前提。

## 五、文档同步

- `docs/fx-status.md`：标题日期改 2026-10-16；新增"第十一批"批次说明；3.1 表里三行改为实际情况并标 ✅ 完善；
  单体元素表补 4 个新元素；0 环 / 1 环 / 3–5 环三处清单里的 `Heal` 换成新名字。
- 本文即本轮报告。
