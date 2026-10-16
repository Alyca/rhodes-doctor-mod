# 20 级恶灵形态仍不能放法术 —— 根因定位与修复

日期：2026-10-11（第十三轮）
状态：**已定位、已修复、已部署（诊断版）**

---

## 1. 现象

作者实测：**20 级**、开【巴别塔的恶灵】形态后，仍然不能正常放法术。

## 2. 取证（诊断版日志）

发布版 `Dbg.Log` 被 `[Conditional("RD_VERBOSE")]` 编译期删除 ⇒ 看不到拦截原因。
临时打诊断版（`-p:DefineConstants=RD_VERBOSE`），在 `SpecterCasterRestriction` 里打印锁定状态 +
压制部件 + Capstone + 单位状态位。复现后日志（`Player.log`）拿到决定性一行：

```
施法限制未通过（非【恶灵】指令）：
  d0…027 => 无            （一段锁定：不在身上）
  d0…089 => 无            （二段锁定：不在身上）
  d0…028 => 存在(未压制)   ← 三段锁定在身上，但没有被压制
  压制部件=有               （BlueprintCore 的 UnitPartBuffSuppressFixed 存在）
  20级Capstone=在身上        （20 级「罗德岛的博士」增益确实在身上）
  [CanCast=True UseAbilityForbidden=False SpellcastingForbidden=False
   CantUseStandardActions=False Staggered=False Nauseated=False]
```

单位状态位全绿 ⇒ **唯一的拦截源就是 `SpecterCasterRestriction`**：三段锁定（`…d028`）
`IsSuppressed=false`，于是限制判定为"未通过"，非【恶灵】指令全部放不出。

## 3. 根因

20 级本意是：Capstone 增益挂 `SuppressBuffsFixed(applyToNewBuffs: true)` **压制**一/二/三段锁定，
锁定被压制 → `BuffHelper.HasActive`（已判 `!IsSuppressed`）视为不存在 → 限制解除。

**但这条压制链路不可靠。** 反编译确认其实现方式：

- BlueprintCore 用的是**自己的** `UnitPartBuffSuppressFixed`（不是游戏原生 `UnitPartBuffSuppress`）；
- 让"**之后**才挂上的增益"被压制的关键，是它给 **`Buff.OnAttach` 挂的 Harmony postfix**：
  `IsSuppressed = part.IsSuppressedContinuously(buff)`。
- 而游戏原生 `Buff.OnAttach` 里另有一句 `IsSuppressed = Owner.Get<UnitPartBuffSuppress>()?.IsSuppressed(this) ?? false`
  （查的是**游戏自己的**部件，此处不存在 → 置 false）。

这条 Harmony 补丁的生效，与 **载入顺序 / 其它同样改 `Buff.OnAttach` 的 mod / 增益重新挂载时机**耦合。
本机装了 **TabletopTweaks-Core**（它也自带一份 `UnitPartBuffSupressTTT` + 自己的 `Buff.OnAttach` 补丁），
实测结果就是：**Capstone 在身上、压制部件也在，可锁定就是没被压**。

> 结论：**不要把"20 级人灵合一"建立在"压制能不能及时生效"上**——那是个跨 mod 的脆弱假设。

## 4. 修复（确定性、与压制机制解耦）

核心思路：**只要 20 级 Capstone 增益在场，凡"恶灵锁定"一律视为不生效**——直接判，不依赖压制。

### 4.1 新增 `BuffHelper.SpecterLockActive(unit, lockRef)`

```csharp
internal static bool SpecterLockActive(UnitEntityData unit, BlueprintReferenceBase lockRef)
{
  if (!HasActive(unit, lockRef)) return false;
  // Capstone 在场 ⇒ 人灵合一 ⇒ 锁定一律不算
  return !HasActive(unit, BlueprintTool.GetRef<BlueprintUnitFactReference>(Guids.CapstoneBuff));
}
```

### 4.2 三个锁定判定点全部改用它

| 位置 | 原判定 | 改为 |
|---|---|---|
| `SpecterCasterRestriction.IsCasterRestrictionPassed` | `HasActive` | `SpecterLockActive` |
| `OriginiumMastery.HasAnyLock` / `EffectiveBonus` | `HasActive` | `SpecterLockActive` |
| `CoolHeadSaveBonus.OnEventAboutToTrigger` | `HasActive` | `SpecterLockActive` |

→ 覆盖三大受影响面：**非【恶灵】指令可否施放**、**源石技艺精通的穿透/DC 倒扣**、**冷静头脑的加值减半**。

### 4.3 顺带修：余波限制同样不再依赖压制

原 `RhodesDoctorAftermathRestrict` 用裸 `AddCondition(Nauseated)`，20 级靠压制解除——同样不可靠。
新增 `AftermathRestriction` 组件：`OnActivate` 时**自己判断 Capstone 在不在**，在场则不施加 `Nauseated`，
`OnDeactivate` 撤除。与压制解耦。

## 5. 验证计划

- 诊断版已编译（0 警告 0 错误）并部署（备份 `…bak-20261011-212*.dll`）。
- **待作者进游戏**：20 级开恶灵 → 放非【恶灵】指令，日志应不再出现"施法限制未通过"；
  `OriginiumMastery` 的穿透/DC 不应再倒扣。
- 确认后**用无符号重编译发布版**（把 `Dbg.Log` 诊断段落收回），再走一次 `deploy.sh`。

## 6. 影响面 / 回归点

- 未开恶灵 / 未到 20 级：行为不变（Capstone 不在场 ⇒ `SpecterLockActive` ≡ `HasActive`）。
- 20 级开恶灵：非【恶灵】指令解除限制；穿透/DC 恢复正加值；冷静头脑恢复全额；余波不再"只能移动"。
- 一/二段（8–15 级）无 Capstone：锁定照旧生效，限制/倒扣不变。

> 本文件为游戏 Mod 调试记录，与证券行情无关，不构成任何投资建议。
