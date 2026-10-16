# 法术特效 · 梦魇换素材 + 墨染山河去特效 报告

> 日期：2026-10-10（夜）　范围：作者两条反馈——① 梦魇的特效看着像"领域"不像 debuff；② 墨染山河没必要加特效。
> 依据：`Player.log`（2026-10-10 20:10）里梦魇光环的 `染色诊断` 明细。
> 编译：`dotnet build -p:WrathPath="…"` **0 警告 0 错误**；已 `tools/deploy.sh` 部署（旧 DLL 备份 `RhodesDoctor.dll.bak-20261010-202849`）。

---

## 一、梦魇：为什么像"领域"（日志给的答案）

梦魇光环用的素材是 `5dfe8b5fa37cae543870069937d14ba3`（= 原版**女巫·美味恐惧** `WitchHexDeliciousFrightBuff` 的 `FxOnStart`）。
日志 `染色诊断` 列出了它的粒子名：

```
Notes / BorderGlow1 / Border_Loop (6) / Border_ThinActive / Rays_Shockwave (1) / DarkBottomSmoke / Border_Loop (7) / Sparks_Body (2)
```

**`Border*`（边框）+ `DarkBottomSmoke`（底部烟）** —— 这是个**地面圆环 + 底部烟雾**的**魔法阵/领域**型特效。
贴到敌人身上自然读成"给敌人放了个领域"，而不是"敌人中了 debuff"。作者的观感完全对得上。

**改法**：换成**身体型**素材 —— 原版**血雾** `BloodHazeBuff`（`FxLibrary.PlasmaAura`，与"不稳定血浆"同源，作者已确认它是"常驻在身上的光环"），重染**恐惧的黑** `(0.10,0.06,0.16)`。贴到敌人身上是一层**黑影缠身**，读起来就是 debuff。
恐慌 / 动摇两个增益都挂（跟减益同生同灭），恐惧"全程"有黑影。

> `FxLibrary.FearAura`（美味恐惧素材）**保留在库里备用**，注释已标明"是地面圆环、像领域，梦魇不再用"。

---

## 二、墨染山河：去掉特效

作者定案："能召唤出来就行"。已**移除** `InkedMountains` 分支里接的召唤闪光（`effects.Add<ContextActionSpawnFxBetweenSummons>(...)`）。
- 现在墨染山河只做召唤，**没有额外 FX**。
- 之前的自定义动作 `ContextActionSpawnFxBetweenSummons`、以及为它加的 `FxTint.SpawnPlaced` / `FxTint.ScaleParticleSizes` **保留在代码里备用**（未接线），不删。
- 清单里墨染山河**从 3.1「已有特效」移回 3.2「无特效」**，计数同步（单体有特效 20→19、无特效 9→10）。

---

## 三、代码改动

| 文件 | 改动 |
|---|---|
| `src/TintedAuraFx.cs` | 梦魇两条表项：素材 `FearAura` → `PlasmaAura`（身体型血雾），标签改"黑影缠身" |
| `src/FxLibrary.cs` | `FearAura` 注释更新为"⚠ 未使用（地面圆环、像领域）"，保留备用 |
| `src/SpellsSetup.cs` | `InkedMountains` 分支**移除**召唤闪光接线（保留注释说明） |
| `docs/fx-status.md` | 梦魇行更新；墨染山河移回 3.2；3.1/3.2 计数与汇总同步 |

---

## 四、待进游戏确认

1. **梦魇**：目标身上应是**一层黑影**（身体型），不再像地面圆环/领域。若黑得看不见，把 `TintedAuraFx` 里梦魇两条的 `Tint` 抬到 ≈0.25；若觉得太像"不稳定血浆"（同素材），可换 `FxLibrary.SlowAura`（缓慢术的身体光环）。
2. **墨染山河**：确认召唤后**没有**多余特效（只剩两只墨灵）。
3. ⚠ 换 DLL 后必须重启游戏。

---

## 五、下一步

剩余单体批次：**6 环（涌潮悲歌 / 黑蛇之焰 / 罪罚）→ 7 环（镜中流形 / 终末狙击 / 归溟）→ 8 环（画中世界）→ 9 环（影霄·拔刀 / 旧日的战术家）**，共 **10 个**（含墨染山河）。
