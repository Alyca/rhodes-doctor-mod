// 战术指令法术书（规则文档第二节）。
// 术士式自发法术书：引用术士的每日法位表与已知法术表，施法属性为智力。
// 法术书绑定圣武士职业（m_CharacterClass），否则等级/法位映射不对。
//
// 10 环指令：**20 级时走正常的升级界面获取**（作者 2026-10-11 定案）。
//   走过的两条死路（反编译确认，见 dev-notes.md §10）：
//   · 合书路线：10 环只能由**道途合书特性自己的 10 环表**提供（合书后 `Spellbook.AddSpecialList(道途表)` 自动已知），
//     而职业法术书（IsMythic=false）的 m_MythicSpellList **永远不会被加入**；
//   · 20 级特性直接 `AddKnownSpell`：能生效，但对**旧存档已满 20 级**的角色不补发，而且让玩家"没得选"。
//   ⇒ 现方案（与"其他环完全同一种获取方式"）：把**两张表**都补上第 11 列 10 环 ——
//     ① 每日法位表（`CreateSpellsPerDayTable`）：`GetMaxSpellLevel()` 从每日法位表往下找第一个 >=0 的环，
//        第 11 列有值 → 最高环变成 10（**不需要** `EnableSpellLevel10`，它只在表里没有 10 环列时兜底）；
//     ② 已知法术表（`CreateSpellsKnownTable`）：升级界面按 `GetCount(新, i) - GetCount(旧, i)`（i 循环 0..10）
//        决定每一环给几个"已知"名额，原版术士表只有 0–9 列 → 10 环差值恒 0 → 现在补上 4。
//     两张表的门槛行都是 `TenRingRow = 21`（= 圣武士 20 级；博士法术书等级比职业等级多 1，见该常量说明）。
using System;
using System.Collections.Generic;
using System.Linq;
using BlueprintCore.Blueprints.Configurators.Classes;
using Kingmaker.Blueprints.Classes; // BlueprintFeatureSelectMythicSpellbook
using BlueprintCore.Blueprints.Configurators.Classes.Spells;   // SpellsTableConfigurator
using BlueprintCore.Blueprints.CustomConfigurators.Classes;     // FeatureConfigurator
using BlueprintCore.Blueprints.CustomConfigurators.Classes.Spells; // SpellListConfigurator
using BlueprintCore.Utils;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.UnitLogic.Abilities.Blueprints; // BlueprintAbility
using Kingmaker.UnitLogic.FactLogic;             // AddKnownSpell / AddMechanicsFeature
using Kingmaker.Enums;

namespace RhodesDoctor
{
  internal static class SpellbookSetup
  {
    /// <summary>
    /// **10 环的门槛行 = 法术书等级 21**（对应圣武士职业 20 级）。
    ///
    /// 为什么是 21 而不是 20：博士的法术书等级比职业等级**多 1** —— 2026-10-11 从作者存档核实：
    /// 圣武士 20 级时 `m_Spellbooks[0].Value.m_BaseLevelInternal = 21`；同一存档里原版圣武士 15 级是 15。
    /// 多的这 1 级来自本变体 1 级那个 `AddSpellbook` 特性（它按职业等级推法术书等级，
    /// 与升级流程里的 `ApplySpellbook.AddLevelFromClass` 各加一次）。
    /// 两张表（每日法位 / 已知法术）都用这个常量，别各写各的。
    /// </summary>
    private const int TenRingRow = 21;

    internal static void Create()
    {
      CreateSpellList();
      CreateMythicSpellList();
      CreateSpellsPerDayTable();
      CreateSpellsKnownTable();
      CreateSpellbook();
      RegisterForMythicMerge();
    }

    // ── 法术列表：主表 0–9 环各 6 个 ──
    // 用 SpellListConfigurator 建蓝图（走 BlueprintTool.Create，注册进缓存），
    // 每环用 entry.Spells.Add 挂蓝图本体（BlueprintCore 的自用法），并打数量日志。
    private static void CreateSpellList()
    {
      var byLevel = new List<SpellLevelList>();
      // 0–10 环都写进主表。10 环**不会**提前露出来：最高环由每日法位表决定
      //（自建表里 10 环列在 <20 级写 -1 → `GetMaxSpellLevel()` 到不了 10），
      // 所以把 10 环放进主表是安全的，而且能让"10 环指令"与普通法术一样是**列表内的法术**。
      for (int level = 0; level <= 10; level++)
      {
        var entry = new SpellLevelList(level);
        foreach (var idx in SpellsSetup.SpellsOfLevel(level))
          entry.Spells.Add(BlueprintTool.Get<BlueprintAbility>(Guids.Spell(idx)));
        byLevel.Add(entry);
        Main.Mod.Logger.Log(
          $"[RhodesDoctor] 战术指令列表 第{level}环：写入 {entry.Spells.Count} 个。");
      }

      SpellListConfigurator.New("RhodesDoctorSpellList", Guids.SpellList)
        .SetSpellsByLevel(byLevel.ToArray())
        .Configure();
    }

    /// <summary>
    /// 10 环指令的独立小表。**已不用于解锁 10 环**（改由两张表都补第 11 列、20 级升级界面开放）；
    /// 保留它是为了不破坏"法术书 = 主表 + 这张表"的既有结构，且万一以后想接别的机制。
    /// </summary>
    private static void CreateMythicSpellList()
    {
      var entry = new SpellLevelList(10);
      foreach (var idx in SpellsSetup.SpellsOfLevel(10))
        entry.Spells.Add(BlueprintTool.Get<BlueprintAbility>(Guids.Spell(idx)));
      Main.Mod.Logger.Log(
        $"[RhodesDoctor] 战术指令列表 第10环：写入 {entry.Spells.Count} 个。");

      SpellListConfigurator.New("RhodesDoctorMythicSpellList", Guids.MythicSpellList)
        .SetSpellsByLevel(new[] { entry })
        .Configure();
    }

    /// <summary>
    /// 自建每日法位表 = **原版术士表 + 10 环列**（原版术士表不碰）。
    /// 为什么必须自建：`Spellbook.GetSpellsPerDay` 里 `count = SpellsPerDay.GetCount(CL, 10)`，
    /// 原版术士表的 Count 数组只有 10 项（0–9 环）→ 返回 **-1 → 直接 return 0**，
    /// 连额外法位都加不上去。所以要让法术书有 10 环法位，表里必须有 10 环列。
    /// </summary>
    private static void CreateSpellsPerDayTable()
    {
      var src = BlueprintTool.Get<BlueprintSpellsTable>(Guids.Vanilla.SorcererSpellsDailyTable);
      // P1：行数加下限保护。已知法术表用 Math.Max(TenRingRow+1, 41) 确保 10 环门槛行可索引；
      // 本表此前直接取 src.Levels.Length —— 若原版术士每日法位表只有 21 行（CL 0..20），
      // i 永远到不了 21，count[10] 恒为 -1 ⇒ 10 环法位标签页永不出现、20 级无法施放 10 环指令。
      // 现对齐已知法术表：至少保留到 TenRingRow + 1 行；超原表的行用原表最后一行 + 10 环列覆盖。
      int rows = Math.Max(TenRingRow + 1, src.Levels.Length);
      var levels = new SpellsLevelEntry[rows];
      for (int i = 0; i < rows; i++)
      {
        var old = src.Levels[i]?.Count ?? new int[0];
        int len = Math.Max(11, old.Length); // 下标 10 = 10 环
        var count = new int[len];
        Array.Copy(old, count, old.Length);
        // **超原表的行**：原 src.Levels[i] 取不到（i >= src.Levels.Length），
        // 用原表最后一行做为基底（这样 0–9 环法位不会空，超界回退也有数）。
        if (i >= src.Levels.Length && src.Levels.Length > 0)
        {
          var lastOld = src.Levels[src.Levels.Length - 1]?.Count;
          if (lastOld != null) Array.Copy(lastOld, count, Math.Min(lastOld.Length, len));
        }
        // i == 法术书等级。**< 21 写 -1**：`Spellbook.GetMaxSpellLevel()` 从 10 环往下找第一个
        // `GetCount >= 0` 的环，读不到 10 就停在 9 → 未满 20 级的博士**天然看不到 10 环标签页**。
        // 21 起写 4 → 最高环变成 10，并给 4 个 10 环法位。想调数量就改这个 4。
        // ⚠ 门槛为什么是 21 而不是 20：见 TenRingRow 的说明（博士法术书等级比职业等级多 1）。
        count[10] = i >= TenRingRow ? 4 : -1;
        levels[i] = new SpellsLevelEntry { Count = count };
      }

      SpellsTableConfigurator.New("RhodesDoctorSpellsPerDay", Guids.SpellsPerDayTable).Configure();
      BlueprintTool.Get<BlueprintSpellsTable>(Guids.SpellsPerDayTable).Levels = levels;
      Main.Mod.Logger.Log(
        $"[RhodesDoctor] 法术表就绪：{levels.Length} 行；10 环法位 20 级 4 个（未满 20 级不开放）。");
    }

    /// <summary>
    /// 自建「已知法术表」：**累计已学数**，行 = 法术书等级（<see cref="TenRingRow"/> = 职业 20 级）。
    ///
    /// 为什么必须自建（而不是照抄原版术士表）：
    ///   升级界面"这一步能学几个"由 `ApplySpellbook.Apply` 算 —— `GetCount(新等级, i) - GetCount(旧等级, i)`，
    ///   **i 循环 0..10**（反编译确认本来就支持 10 环）。原版术士表有两个问题：
    ///   ① 每一行只有 0–9 共 10 列 → `GetCount(_, 10)` 恒返回 **-1** → 10 环差值恒为 0 → 永远不出现在选择界面；
    ///   ② 术士是"少而精"，各环上限只有 3–5 个，而本 mod 每环有 **6 个指令** → **永远学不全**。
    ///
    /// 本表的目标（作者 2026-10-11 定案，2026-10-15 调整节奏）：**每级均匀铺开，到 20 级恰好学会全套 64 个指令**
    /// （0 环 6 戏法 + 1–9 环各 6 + 10 环 4）。分配见 <see cref="KnownDistribution"/>：
    ///   每个环从它**解锁的那一级**（见 `unlock`）起，**解锁级直接给 2 个名额**（玩家从该环 6 个里选 2 个），
    ///   剩余均摊到"解锁级 → 20 级"之间，于是低环早满、高环在解锁后逐级补齐；累计到第 21 行每一环都恰好 = 6（10 环 = 4）。
    ///   合计发放 58 个（1–9 环 54 + 10 环 4）；0 环戏法**不进本表**（游戏见 `Spellbook.GetMaxSpellLevel`
    ///   变化时会把法术列表 0 环**自动全给**，见 `Spellbook` 里那段 `AddKnown(0, spell)`）。
    /// </summary>
    private static void CreateSpellsKnownTable()
    {
      // 行数对齐原版术士表（41 行 = CL0..40）：`GetCount` 对超界等级回退到最后一行，
      // 本变体职业上限只有 20 级（法术书 21），用不到更高行；多留几行只为与既有结构一致、防越界。
      int rows = Math.Max(TenRingRow + 1, 41);
      var levels = new SpellsLevelEntry[rows];
      int total = 0;
      for (int cl = 0; cl < rows; cl++)
      {
        var count = new int[11]; // 固定 11 列（0–10 环）
        for (int ring = 0; ring <= 10; ring++)
          count[ring] = KnownDistribution(cl, ring);
        levels[cl] = new SpellsLevelEntry { Count = count };
        if (cl > 0 && cl <= TenRingRow)
          for (int ring = 0; ring <= 10; ring++)
            total += count[ring] - KnownDistribution(cl - 1, ring);
      }

      SpellsTableConfigurator.New("RhodesDoctorSpellsKnown", Guids.SpellsKnownTable).Configure();
      BlueprintTool.Get<BlueprintSpellsTable>(Guids.SpellsKnownTable).Levels = levels;
      Main.Mod.Logger.Log(
        $"[RhodesDoctor] 已知法术表就绪：{levels.Length} 行（行=法术书等级，{TenRingRow}=职业 20 级）；" +
        $"累计全程发放 {total} 个已知（1–9 环 54 + 10 环 4），20 级可学满全套（0 环戏法自动全给）。");
    }

    /// <summary>每个环解锁的法术书等级（= 原版术士职业等级 + 1，因为博士法术书等级领先 1）。</summary>
    /// <remarks>取自原版术士每日法位表各环首次 `Count>=0` 的等级 {1,1,4,6,8,10,12,14,16,18} 再各 +1。</remarks>
    private static readonly int[] RingUnlock = { 1, 2, 5, 7, 9, 11, 13, 15, 17, 19, TenRingRow };

    /// <summary>每环的最终名额（20 级时）：0–9 环各 6，10 环 4。</summary>
    private static int RingTotal(int ring) => ring == 10 ? 4 : 6;

    /// <summary>
    /// 法术书等级 <paramref name="cl"/> 时「<paramref name="ring"/> 环」的**累计已学数**。
    /// 解锁前为 0（列存在但计数 0，形同不发放）；解锁后按下面节奏发放。
    /// </summary>
    /// <remarks>
    /// 节奏（作者 2026-10-15 调整）：**每环解锁那一级直接给 2 个名额**（玩家从该环 6 个里选 2 个），
    /// 剩余（total−2）均摊到后续（span−1）级。这样玩家每拿到一个新环就能立即选 2 个，节奏更舒服；
    /// 总数不变（每环仍 6 个 / 10 环 4 个），20 级学满全套。
    /// 10 环例外：span=1（只有 20 级那一行可发），一次性给全部 4 个。
    /// </remarks>
    private static int KnownDistribution(int cl, int ring)
    {
      if (ring == 0) return 0;                 // 0 环戏法不进表（游戏自动全给）
      int start = RingUnlock[ring];
      if (cl < start) return 0;
      int total = RingTotal(ring);
      int span = TenRingRow - start + 1;       // 从解锁级到最高级的可发放级数
      int steps = Math.Min(cl - start + 1, span);
      // 10 环 span=1（只有 20 级那一行可发）：一次性给全部 4 个。
      if (span <= 1) return Math.Min(total * steps, total);
      // 解锁那一级（k=0）给 2 个，剩余（total-2）均摊到后续（span-1）级。
      int firstBurst = Math.Min(2, total);
      int remaining = total - firstBurst;
      int laterSpan = span - 1;
      int acc = firstBurst;
      int laterSteps = steps - 1;
      if (laterSteps > 0 && laterSpan > 0)
      {
        int per = remaining / laterSpan, rem = remaining % laterSpan;
        for (int k = 0; k < laterSteps; k++) acc += per + (k < rem ? 1 : 0);
      }
      return Math.Min(acc, total);
    }

    /// <summary>供「已知指令补发」组件查询：法术书等级 <paramref name="cl"/> 时该环应有的累计名额。</summary>
    internal static int KnownBudget(int cl, int ring) => KnownDistribution(cl, ring);

    // ── 法术书：术士表 + 智力施法 + 无奥术失败率 ──
    private static void CreateSpellbook()
    {
      // 术士表的真实 GUID 在 Guids.Vanilla。规则：每日法位与已知指令数与术士完全一致。
      // SetIsArcane(true) 是合书登记的前提（神圣道途的登记点会过滤非奥术书）。
      // SetCharacterClass 绑定圣武士：法术书按职业等级取施法者等级与法位进度。
      // 每日法位改用**自建表**（术士表 + 10 环列），见 CreateSpellsPerDayTable。
      SpellbookConfigurator.New("RhodesDoctorSpellbook", Guids.Spellbook)
        .SetName(L10n.S("spellbook.name", "战术指令", "Tactical Orders")) // 法术书只有 SetName
        .SetCharacterClass(BlueprintTool.GetRef<BlueprintCharacterClassReference>(Guids.Vanilla.PaladinClass))
        .SetSpellList(BlueprintTool.GetRef<BlueprintSpellListReference>(Guids.SpellList))
        .SetMythicSpellList(BlueprintTool.GetRef<BlueprintSpellListReference>(Guids.MythicSpellList))
        .SetCastingAttribute(StatType.Intelligence)
        .SetSpellsPerDay(BlueprintTool.GetRef<BlueprintSpellsTableReference>(Guids.SpellsPerDayTable))
        // 已知法术表：自建"术士表 + 10 环列"，这样 20 级升级界面能选 10 环指令（见 CreateSpellsKnownTable）。
        .SetSpellsKnown(BlueprintTool.GetRef<BlueprintSpellsTableReference>(Guids.SpellsKnownTable))
        .SetSpontaneous(true)
        .SetIsArcane(true)
        .SetCantripsType(CantripsType.Cantrips)
        // 不受护甲奥术失败率：不设相关字段即为不受。
        .SetIsMythic(false)
        .Configure();
    }

    // ── 神话合书登记（仍保留：合书后 0–9 环照常可用，只是不再靠它解锁 10 环）──
    // 机制：每条道途一个 BlueprintFeatureSelectMythicSpellbook，关键成员 AllowedSpellbooks；
    // 把博士法术书逐个加进各道途的名单即可，互不干扰。
    private static void RegisterForMythicMerge()
    {
      // 六条登记点：天使、巫妖为游戏本体；AEON / 灵使 / 恶魔 / 诡计大师四条来自
      // SpellbookMerge mod（无该 mod 时该条登记失败，逐条 try/catch 跳过，不加硬依赖）。
      foreach (var pathGuid in new[]
      {
        Guids.Vanilla.AngelIncorporateSpellbook,
        Guids.Vanilla.LichIncorporateSpellbook,
        Guids.Vanilla.AeonIncorporateSpellbook,
        Guids.Vanilla.AzataIncorporateSpellbook,
        Guids.Vanilla.DemonIncorporateSpellbook,
        Guids.Vanilla.TricksterIncorporateSpellbook,
      })
      {
        if (string.IsNullOrEmpty(pathGuid)) continue; // GUID 未填实的先跳过
        try
        {
          FeatureSelectMythicSpellbookConfigurator.For(pathGuid)
            .AddToAllowedSpellbooks(new Blueprint<BlueprintSpellbookReference>[] { Guids.Spellbook })
            .Configure();
        }
        catch (Exception e)
        {
          Main.Mod.Logger.Error($"[RhodesDoctor] 合书登记失败（道途 {pathGuid}，其余道途不受影响）：\n{e}");
        }
      }
      // 10 环不再走合书（作者 2026-10-10 定案）：改由每日法位表 + 已知法术表的第 11 列
      // 在 20 级升级界面开放（见 CreateSpellsPerDayTable / CreateSpellsKnownTable）。
      // 合书只影响 0–9 环的可用性，以及"是否算神话法术书"等原版行为。
    }
  }
}
