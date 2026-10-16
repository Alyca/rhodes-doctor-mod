# 持续时间统一：16 个法术改为「1 轮/级」

> 日期：2026-10-11　范围：作者定案——"镜中流形这种技能都是固定回合，但其实都该改成轮/级"。
> 相关：`docs/fx-status.md`、`docs/dev-notes.md` §19。
> 编译：`dotnet build -p:WrathPath="…"` **0 警告 0 错误**；已 `tools/deploy.sh` 部署（旧 DLL 备份 `RhodesDoctor.dll.bak-20261011-125856`）。
> ⚠ 部署时游戏在运行——**必须重启游戏**才会加载新 DLL。

---

## 一、改了哪 16 个

坚守阵地 / 骑兵突进 / 寒霜祷言 / 不稳定血浆 / 反重力 / 过载模式 / 钙质化 / 时之锁 / 圣域 / 奇美拉 /
涌潮悲歌 / 儿时的舞乐 / 镜中流形 / 归溟(现名"潮") / 画中世界 / 旧日的战术家。

**"1 轮/级" = 圣武士等级轮**（与本 mod 其他"轮/级"法术一致，见 `BuffRank`）。

**没动**：岁稔、炮火延伸仍是固定轮数（不在名单里）；即时/伤害类法术不受影响。

---

## 二、怎么做的

1. **Spec 加 `PerLevel=true`**（把原来的 `Rounds=N` 换掉）——
   `CreateSpell` 的增益/减益分支会自动改用 `ContextDuration.Variable(Rank, Rounds)`，并给能力挂 `BuffRank(l=>l)`。
2. **Meta 的 `DurationKey` → `rhodesdoctor.duration.round1level`** —— 法术表头"持续时间"会显示"1 轮/级"。
3. **`Kind.Special` 的 6 个手动改效果里的时长**：
   - 钙质化 / 儿时的舞乐：区域时长 `SpawnAreaEffect(..., Fixed(N))` → `Variable(Rank, Rounds)`；
   - 镜中流形：分身召唤时长 `new Rounds(3)` → `new Rounds(Rank)`；
   - 归溟 / 画中世界 / 旧日的战术家：增益时长 `ApplyBuff(..., Fixed(N))` → `Variable(Rank, Rounds)`。
4. **描述同步**：`LocalizedStrings.json` 里提及时长的几条 + 源码内嵌描述，`N 轮` → `1 轮/级`。
   （画中世界：**放逐**时长改 1 轮/级，**返回后的麻痹仍为 2 轮**。）

---

## 三、代码改动

| 文件 | 改动 |
|---|---|
| `src/SpellsSetup.cs` | 16 个 Spec 的 `Rounds=N` → `PerLevel=true`；16 个 Meta 的 `DurationKey` → `round1level`；钙质化/儿时舞乐/归溟/画中世界/旧日的战术家 的效果时长改 `Variable(Rank, Rounds)`；16 条内嵌描述同步 |
| `src/Actions.cs` | `ContextActionSpawnMirrorCopy`：召唤时长 = Rank（1 轮/级） |
| `LocalizedStrings.json` | 7 条提及时长的描述改"1 轮/级"（其余 9 条描述本就未写时长） |

---

## 四、待进游戏确认

1. 这 16 个法术的**表头"持续时间"应显示"1 轮/级"**，实际时长随圣武士等级增长。
2. 重点看：**镜中流形**（分身存在轮数）、**画中世界**（放逐轮数）、**钙质化/儿时舞乐**（区域轮数）。
3. ⚠ **换 DLL 后必须重启游戏**。
