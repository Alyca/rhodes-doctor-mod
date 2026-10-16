# 收尾清理：诊断日志与临时埋点

> 日期：2026-10-11　来源：作者"到收尾阶段了，清理那些日志检测，以免占用内存"。
> 编译：`dotnet build` **0 警告 0 错误**；已部署（备份 `RhodesDoctor.dll.bak-20261011-195630`，版本 0.6.0 → **0.6.1**，
> DLL md5 与构建产物一致）。⚠ 部署后**必须重启游戏**。

---

## 一、清理结果

| | 改前 | 改后 |
|---|---|---|
| 正式日志 `Logger.Log` | 132 处 | **24 处**（只剩一次性启动 / 关键状态 / 测试面板） |
| 诊断日志 | 全部按正式日志处理 | **100 处**走 `Dbg.Log`，**发布版被编译器整体删除** |
| 错误日志 `Logger.Error` | 81 处 | **80 处**（全部保留） |
| `src/Diag.cs`（TEMP-DIAG 补丁） | 1 个文件 | **已删除** |
| 临时诊断语句 `[RhodesDoctor-DIAG]` | 9 处 | **0 处** |

### 1.1 删掉的

- **`src/Diag.cs`** —— 文件头写着"TEMP-DIAG：…确认原因后删除"的 Harmony 补丁
  （挂在 `AbilityTargetsAround.Select` 上，**每次范围选择都要跑一遍并拼字符串**）。文件整删。
- **9 条 `[RhodesDoctor-DIAG]` 语句**（`Actions.cs` ×5 / `Components.cs` ×2 / 其余），
  连带**只为它存在的死变量**（`near` / `hit` 计数器、`ipe` / `can` 两个布尔、`d` 的调试分支）。

### 1.2 改成"编译期开关"的

新增 **`src/Dbg.cs`**：

```csharp
internal static class Dbg
{
  [Conditional("RD_VERBOSE")]
  internal static void Log(string message) => Main.Mod.Logger.Log(message);
}
```

**为什么用 `[Conditional]` 而不是"运行时 if"**：带该特性的调用在**未定义符号时被编译器整体删除**，
连参数字符串的拼接 / 插值都不会执行 ⇒ 发布版**零字符串分配、零调用**。
若写成 `if (Dbg.On) Log($"...")`，插值字符串仍会先分配，等于没省。

- 100 处逐事件日志（FX 借用 / 染色的原子手术过程 / 地面特效构建 / 镜像复制体全过程 / 画中世界放逐与返回 /
  连锁殉爆 / 图标加载与借用 / 墨染山河部件坐标……）统一转成 `Dbg.Log`。
- 另外把 **5 个"纯诊断方法"本体**也标了 `[Conditional]`：
  `FxTint.DumpColorsOnce / LogOriginalSizeOnce / DumpPostOnce`、`AreaGroundFx.LogInventory`、`SmokeFx.Inspect`
  —— 这样**连调用点都会消失**，方法体内的 `GetComponentsInChildren` 遍历与 StringBuilder 拼接也就不会跑了
  （只把里面的 `Log` 改掉是不够的：方法照样会被调用、照样遍历）。

**验证**（直接查编出来的 DLL 字符串）：

```
染色诊断 / 顶点漂白 / 特效点染 / 着色来源 / RhodesDoctor-DIAG / DiagTargetsAroundPatch
→ 全部"已移除"
蓝图注入完成 / 注入完成 / 图标文件缺失 / 结束恶灵：移除 / 恶灵形态已开启
→ 仍在（这些是要留的）
```

### 1.3 保留的（有意不删）

- **全部 `Logger.Error`**（80 处）：出错必须能看到。
- **一次性启动日志**：`内容注入完成`（6 条）、`蓝图注入完成`、`法术表就绪`、`测试木桩蓝图就绪`、`图标文件缺失`。
- **恶灵状态 3 条**：`恶灵形态已开启` / `身上已有…忽略本次开启` / `结束恶灵：移除 N 个形态增益`
  —— 这三条是 `docs/dev-notes.md §21.4` 写明的**验证锚点**，且只在开关形态时触发（量极小）。
- **测试面板日志**（`TestPanel.cs`）：玩家主动点才触发。

### 1.4 需要再次排查时怎么开回来

```bash
dotnet build -p:DefineConstants=RD_VERBOSE -p:WrathPath="D:/steam/steamapps/common/Pathfinder Second Adventure"
```

一次性恢复全部 100 条诊断日志（含 5 个诊断方法）。

---

## 二、顺带做的内存体检（真正的"占用内存"来源）

诊断埋点除了写日志，还会喂几个**只增不减的 HashSet**；埋点一停，它们就不再增长：

| 静态集合 | 说明 |
|---|---|
| `FxTint.SizeLogged / ColorLogged / PostLogged` | 原来每次生成 FX 都往里塞 key（**长局内持续增长**）。现在只由已 `[Conditional]` 的诊断方法写入 ⇒ **不再增长**。 |

其余运行时缓存的**回收路径逐个人工核对过，均无泄漏**：

| 缓存 | 回收 |
|---|---|
| `AreaGroundFx.Live` / `BeaconFx.Live` / `SmokeFx.Live` | 销毁时 `Live.Remove` + `Prune()` 扫死键 |
| `HasteTrailFx.Live` / `MedicalDroneFx.Live` / `TintedAuraFx.Live` | 同上（按单位键，单位消失即清） |
| `Icons.Cache / Missing / Borrowed` | 键是蓝图 GUID，**上限 = 图标数** |
| `FxTint.TexCache / AlphaCache` | 键是游戏素材贴图，**上限 = 素材数** |
| `TestPanel.Dummies` | `RemoveAll(null)` + 面板有"清除木桩"按钮 |

另外清理了 `_tools/tmp` 里的 pip 临时目录（`pip-*` 共 10 个）；保留了
`relink.py`（描述改写脚本）与 `LocalizedStrings.orig.json`（**描述回滚备份**，勿删）。

---

## 三、进游戏看什么

日志**应该显著变少**——正常游玩时只会有：

- 启动那一段：`内容注入完成` ×6 + `蓝图注入完成` + `法术表就绪` + `测试木桩蓝图就绪`；
- 开关恶灵时的 3 条；
- 以及任何真正的 `Error`。

若你想确认"诊断确实关掉了"：放一次范围法术（例如闪光弹），`Player.log` 里**不该**再出现
`[RhodesDoctor-DIAG]` 或"特效借用成功"这类逐事件行。
