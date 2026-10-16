# 法术特效 · 单体批次「6–9 环」第四轮修订（收尾）

> 日期：2026-10-11　范围：按作者第四轮反馈再修 2 项。
> 相关：`report-fx-6-9ring-revise3.md`、`docs/fx-status.md`、`docs/dev-notes.md` §16。
> 编译：`dotnet build -p:WrathPath="…"` **0 警告 0 错误**；已 `tools/deploy.sh` 部署（旧 DLL 备份 `RhodesDoctor.dll.bak-20261011-124442`）。
> ⚠ 部署时游戏在运行——**必须重启游戏**才会加载新 DLL。

---

## 一、作者反馈 → 结果

| # | 法术 | 反馈 | 结果 |
|---|---|---|---|
| 1 | 涌潮悲歌 | 对了 | — 不动 |
| 2 | 镜中流形 | 要**法术位全有，也可以使用** | ✅ 分身复制后**满法术位 + 满资源**，并**撤掉行为锁**（可正常施法/使用能力） |
| 3 | 画中世界 | 没问题 | — 不动 |
| 4 | 岁稔署名 | 把 `黍` 改成"**种地的大姐姐**" | ✅ 描述末尾署名 `黍` → **`种地的大姐姐`** |

---

## 二、镜中流形：分身"满法术位 + 满资源 + 可施法"

- **问题**：之前按设计给分身挂了**行为锁**（`MirrorLockBuff` = `ForbidSpellCasting` 禁施法 +
  `ExAndSuAbilitiesForbidden` 禁超自然/非凡主动能力），设计原文是"只能攻击、移动与使用普通物品"。
  作者现在要它"**法术位全有、也可以使用**"。
- **改法**：
  1. **撤掉行为锁**（本动作不再挂 `MirrorLockBuff`；该蓝图保留在 `SummonsSetup`，未删除）；
  2. **满法术位**：复制后遍历 `mirror.Descriptor.Spellbooks` 逐个 `sb.Rest()`
     （`UnitHelper.Copy` 的 `CopySpellbook` 只抄法术书**结构**，不保证槽位可用）；
  3. **满资源**：`mirror.Descriptor.Resources.FullRestoreAll(null)`。
- **描述同步**：法术描述里的"只能攻击、移动与使用普通物品，不能施法或使用每日能力"
  改为"**法术位与资源均为满值，可正常行动与施法**"（源码 + `LocalizedStrings.json`）。

---

## 三、岁稔署名：`黍` → "种地的大姐姐"

作者澄清："把 `黍` 改成**种地的大姐姐**"（上一轮我误解成改成"黎"）。现已改为
描述末尾的 **`——种地的大姐姐`**（英文 `(the farming big sister)`）。
"种地的大姐姐"六个字都在游戏主字体 `SourceHanSerifSC` 里，可正常显示。

---

## 四、代码改动

| 文件 | 改动 |
|---|---|
| `src/Actions.cs` | `ContextActionSpawnMirrorCopy`：撤行为锁；加 `Spellbook.Rest()`（满法术位）+ `Resources.FullRestoreAll(null)`（满资源） |
| `src/SpellsSetup.cs` | 镜中流形描述改写（可施法/满法术位）；岁稔署名 `黎` → `种地的大姐姐` |
| `LocalizedStrings.json` | 同步这两条 |
| `docs/fx-status.md` / `docs/dev-notes.md` | 对应行 / §16 补"满法术位 + 撤锁" |

---

## 五、待进游戏确认

1. **镜中流形**：分身应能**正常施法**、法术位与资源是**满的**。
2. **岁稔**：描述末尾应显示"**——种地的大姐姐**"。
3. ⚠ **换 DLL 后必须重启游戏**。
