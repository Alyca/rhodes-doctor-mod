# refs/ —— 给"没有游戏本体"的审阅方的参考包

本目录把**游戏本体里本 mod 依赖的事实**提成纯文本，随源码一起上传。审阅方（高级模型）
只上传这个文件夹即可，不需要游戏安装目录、不需要 `Assembly-CSharp.dll`、不需要 `blueprints.zip`。

**规则：凡是结论取决于游戏程序集或原版蓝图行为的，先在本目录里查到类名/方法名/字段名再下结论，
并在回报里附文件名与关键行。查不到就写"查不到"，不要猜。**（与交接文档"不要猜、做不到就停下"一致。）

---

## 1. 目录内容

| 文件 | 内容 | 大小 |
|---|---|---|
| `api/game-api.txt` | 游戏本体 `Assembly-CSharp.dll` 的**签名级** API（namespace 以 `Kingmaker.` / `Owlcat.` 开头） | ~4.1 MB |
| `api/blueprintcore-api.txt` | `BlueprintCore.dll`（WW-Blueprint-Core 2.8.7）的签名级 API | ~5.7 MB |
| `blueprints/*.json` | 从 `blueprints.zip` 抽出的、本 mod 引用的原版蓝图（含"对照范本"） | 65 个 / ~0.6 MB |
| `blueprints/index.md` | 常量名 → GUID → 游戏内路径 → 本地文件 对照表 | — |
| `tools/build-blueprint-refs.py` | 蓝图抽取脚本（本机重跑用，审阅方不需要） | — |
| `tools/bp-index.tsv.gz` | 全量 GUID→路径 索引（本机重跑用，审阅方不需要） | — |

## 2. API 转储怎么读

每行一条结构，`TYPE` 开头是类型，缩进的两空格行是成员：

```
TYPE pub K Kingmaker.RuleSystem.Rules.Abilities.RuleCastSpell : Kingmaker.RuleSystem.Rules.RulebookEvent
  FIELD pub BlueprintAbility m_Ability
  PROP pub Boolean ForceFail { get; }
  METH pub void OnTrigger()
```

- `K`=类 `S`=结构 `I`=接口 `E`=枚举 `D`=委托；`pub/prot/int` 是可访问性。
- 枚举成员单独列出：`ENUM Fire = 10`，可直接核对枚举名与取值。
- **没有方法体**（只看签名）。要判断"这个方法内部怎么算"（如护甲奥术失败率），
  需要本机用 `ilspycmd` 反编译该方法——把这当成一条 `V` 项写成"需本机反编译"。

查找方法（在 4 MB 文本里）：

```
grep -n "^TYPE .*\.RuleCastSpell\b" api/game-api.txt
grep -n "\.AbilityTargetsAround\b"   api/game-api.txt
```

## 3. VERIFY 热点 → 直接对应的文件（对应 README 第三节）

| README VERIFY | 查这里 |
|---|---|
| 1 原版引用填实 | `blueprints/index.md` + `blueprints/*.json` |
| 2 本地化包格式 / `LocalizedString` 字段 | `api/game-api.txt` 搜 `LocalizationManager`、`LocalizedString`、`LoadPack` |
| 3 法术书 `SpellbookConfigurator.*`、`SpellLevelList.SpellsRefs` | `api/blueprintcore-api.txt` 搜 `SpellbookConfigurator`、`SpellLevelList`；`api/game-api.txt` 搜 `BlueprintSpellbook` |
| 4 变体 `SetReplaceSpellbook`/`SetRemoveSpellbook`/`LevelEntryBuilder` | `api/blueprintcore-api.txt` 搜 `ArchetypeConfigurator`、`LevelEntryBuilder`；`api/game-api.txt` 搜 `AddClassLevels`、`BlueprintArchetype` |
| 5 灵光 `AuraFeatureComponent` 的 `m_Buff`、半径 | `api/game-api.txt` 搜 `AuraFeatureComponent`、`AuraFeatureComponentData` |
| 6 规则钩子（`RuleCalculateWeaponStats` / `RuleCalculateAttacksCount` / `RuleSpellResistanceCheck` / `RuleCastSpell` / `RuleCalculateAttackBonus`） | `api/game-api.txt` 搜各 `Rule*` 类型 |
| 7 恶灵（`ContextRankConfig` / `MaxDexBonusIncrease` / `AddFacts` / 永久增益） | `api/game-api.txt` 搜 `ContextRankConfig`、`MaxDexBonusIncrease`、`AddFacts`、`ContextActionApplyBuff` |
| 8 血浆自伤（伤害指向施法者） | `api/game-api.txt` 搜 `RuleDealDamage`、`ContextActionDealDamage`；`blueprints/` 无对应 |
| 9 复杂机制（召唤/复制/放逐/复活/夺增益/额外回合） | `api/game-api.txt` 搜 `RuleSummonUnit`、`ContextActionSpawnMonster`、`ContextActionKill`、`ContextActionDispelMagic`；蓝图对照见 `blueprints/index.md` 的 TimeStop / Banishment / Resurrection |
| 10 神话合书登记点 | `blueprints/index.md` 里 Angel/Lich 合书；`api/blueprintcore-api.txt` 搜 `FeatureSelectMythicSpellbookConfigurator` |

## 3.5 常用查询速查表（做特效 / 查接口时先看这里）

> 目的：把反复用到的查询模式沉淀下来，**先查这里再 grep**，避免每次从头翻 4 MB 文本。
> 下面每条都给出"查什么 → 去哪查 → 怎么查"。凡结论取决于程序集/蓝图行为的，照此取证，别猜。

### A. 查 API 签名（`api/game-api.txt` = 游戏程序集，`api/blueprintcore-api.txt` = BlueprintCore）

| 想查 | 去哪查 | 怎么查 |
|---|---|---|
| 某个类 / 接口 / 方法 / 字段**是否存在** | `game-api.txt` | `grep -n "TYPE .*\.IUnitBuffHandler\b" api/game-api.txt`（`\b` 防匹配到别的） |
| 某个 `*Refs` 常量（如 `BuffRefs.HasteBuff`） | `blueprintcore-api.txt` | `grep -n "HasteBuff" api/blueprintcore-api.txt`（`FIELD pub static readonly Blueprint<...> XxxBuff`） |
| 某个接口的**完整方法列表** | `game-api.txt` | `grep -n "TYPE pub I Kingmaker.PubSubSystem.IUnitBuffHandler" api/game-api.txt`，再读该行往下几行 |
| 某个类的**成员**（属性/字段/方法） | `game-api.txt` | `grep -n "TYPE pub K Kingmaker.UnitLogic.Buffs.Buff" api/game-api.txt`，再读该类型块 |
| 某个类型**继承自谁**（如 `Buff : UnitFact<BlueprintBuff>`） | `game-api.txt` | 看 `TYPE` 行冒号后的基类，再 grep 基类类型块 |

**常用接口 / 类速查**（做特效生命周期时直接抄）：
- `IUnitBuffHandler`（`Kingmaker.PubSubSystem`）：`HandleBuffDidAdded(Buff)` / `HandleBuffDidRemoved(Buff)` —— 增益加上/移除事件，做"跟增益同生同灭"的特效用。
- `Buff`（`Kingmaker.UnitLogic.Buffs`）：`Owner`（`UnitDescriptor`）、`Blueprint`（`BlueprintUnitFact`，其 `AssetGuid` 可比对 GUID）。
- `UnitDescriptor`：`Unit`（`UnitEntityData`）。
- `UnitEntityData`：`View`（`EntityViewBase`，是 `MonoBehaviour` → 有 `.transform` 可挂特效）、`Position`。
- `EntityViewBase`：`MonoBehaviour`，`gameObject` / `transform` 直接可用。

### B. 查原版蓝图字段（`blueprints.zip`，本机用 Python 读，比解压全量快）

| 想查 | 怎么查 |
|---|---|
| 某个 buff 的 `FxOnStart` 是否非 null（做光环前必查） | `python tools/scan_buff_fx_all.py [关键字]`（见 dev-notes §7.7），或直接读 `Data["FxOnStart"]` |
| 某个法术有没有 `AbilitySpawnFx`（做爆发前必查） | 读 `Data["Components"]` 里有没有 `$type` 含 `AbilitySpawnFx` |
| 某个区域的 `Fx` / `Shape` / `Size`（做区域前必查） | 读 `Data["Fx"]`（null?）、`Data["Shape"]`（`Cylinder`=圆 / `Wall`=直线墙）、`Data["Size"]`（尺） |

```python
import zipfile, json
z = zipfile.ZipFile(r"…\blueprints.zip")
d = json.loads(z.read("Spells/Level2/StoneCall.jbp").decode("utf-8-sig"))["Data"]
print([c["$type"] for c in d["Components"]])          # 找 AbilitySpawnFx
a = json.loads(z.read("Spells/Level2/StoneCallArea.jbp").decode("utf-8-sig"))["Data"]
print(a.get("Fx"), a.get("Shape"), a.get("Size"))     # 区域常驻 FX / 形状 / 半径
```

### C. 查本 mod 的 GUID（`src/Guids.cs`）

- `Guids.Buff(i)` / `Guids.Spell(i)`：`i` 是 `SpellsSetup.Specs` 数组的**下标**（0 起）。例：骑兵突进是第 7 个 Spec → `Guids.Buff(7)`。
- 原版蓝图 GUID 常量在 `Guids.Vanilla` 里（已核实）；`*Refs` 常量在 `blueprintcore-api.txt` 里查。

### D. 特效专项（做特效前先看 dev-notes §7 与 fx-plan / fx-status）

- 借增益 `FxOnStart` 做光环 → `tools/scan_buff_fx_all.py`（离线扫全库，输出 `路径 | fx素材AssetId | buff的GUID | 名字`）。
- 借法术 `AbilitySpawnFx` / 区域 `Fx` 做爆发 → `FxBorrow.AbilityBurstFx` / `AreaFx` / `AreaBurstFx`（`src/FxBorrow.cs`）。
- 运行时克隆重染改色 → `FxTint`（`src/FxTint.cs`，`Spawn` / `SpawnAttached`）。
- 跟增益同生同灭的**单位身上**特效 → `IUnitBuffHandler`（见上 A 节）+ `FxTint.SpawnAttached`（挂到 `unit.View.transform`）。

## 4. 已经用本包验证过的结论（dev-notes 那几个坑）

- `BlueprintUnit.Skills` / `FactionOverrides` / `Visual` / `Body` 是**类字段**、默认 null：搜
  `BlueprintUnit`，对照 `blueprints/` 里的原版单位 JSON（如 `SmallAirElemental__*.json`）看原版怎么赋值。
- `UnitEntityData.Master` vs 召唤物：`api/game-api.txt` 搜 `UnitEntityData`、`UnitPartSummonedMonster`。

## 5. 给审阅方的产出自检

出稿时每条改动请写清：(a) 结论依据的**文件名 + 类型/方法名**；(b) 把握程度；(c) 需要本机
反编译方法体或进游戏验证的，单列为"需本机验证"。这样本机 agent 能直接编译核对，不必回头问你。
