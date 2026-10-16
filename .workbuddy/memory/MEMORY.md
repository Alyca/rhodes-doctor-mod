# 罗德岛博士 mod — 项目长期记忆

## 项目概况
- 《开拓者：正义之怒》（Pathfinder: Wrath of the Righteous）UMM 职业变体 mod
- 圣武士变体「罗德岛博士」，版本 0.6.4，.NET Framework 4.8 + WW-Blueprint-Core 2.8.7
- 工作目录：`D:/SteamLibrary/Mods/rhodes-doctor-mod`

## 关键约定
- **游戏路径**：`D:/steam/steamapps/common/Pathfinder Second Adventure`（无 publicized_assemblies，用非公开化程序集）
- **编译命令**：`dotnet build -p:WrathPath="D:/steam/steamapps/common/Pathfinder Second Adventure" -c Debug`
- **发布备份**：原 DLL 备份为 `bin/Debug/net48/RhodesDoctor.dll.bak-YYYYMMDD-HHMMSS`
- **诊断日志**：`Dbg.Log` 走 `[Conditional("RD_VERBOSE")]`，发布版编译期整体删除；排查时加 `-p:DefineConstants=RD_VERBOSE`
- **字符串验证**：.NET 字符串在 DLL 里是 UTF-16LE，用 python `s.encode('utf-16-le') in data` 验证新字符串是否编入

## 代码风格约定
- OnDeactivate/DidTrigger 触发外部规则的组件**必须有 try/catch**（避免异常打断 buff 移除链）
- 反射写入私有成员失败要记 `Main.Mod.Logger.Error`（不静默吞）
- 单位扫描统一走 `UnitScan.Take().Units`（State.Units + AllCrossSceneUnits + Party 合并去重）
- 法术书等级 = 职业等级 + 1（双来源 quirk），`TenRingRow = 21` 强耦合，10 环门槛行

## 已审计轮次
- 2026-10-11 第一轮：P1 全修 + 源石技艺精通改版 + P2 性能（见 report-audit-p1p2-fixes.md）
- 2026-10-15 第二轮：非法术代码 17 项修复（见 report-audit-round2-20261015.md）
- 2026-10-15 角色创建"法术录入后消失"Bug：GrantKnownOrdersBackfill.OnActivate 加 LevelUpController 守卫，CharGen 期间跳过预给

## 关键 API
- `Game.Instance.LevelUpController`（refs/api/game-api.txt:28307）：角色创建/升级流程期间非 null，读档为 null——用于区分"该不该跑补发逻辑"

## 留档待实测项
- ExecutionStrike 翻倍范围（应只武器段，需核对伤害数字）
- 集火资源最小值 1（ResourceAmountBuilder.WithMin API 未核实）
- FxTint 克隆材质/网格/贴图显式回收（结构大）
- 法术书双来源 TenRingRow=21 强耦合（需实测法位是否翻倍）
