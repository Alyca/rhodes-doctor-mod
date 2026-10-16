// UMM 管理界面的测试面板（Ctrl+F10 → 本 mod → 下方区域）。
// 三个按钮：召唤敌方木桩 / 回满木桩血量 / 清除木桩；另有一行状态。
// 刷怪点在队伍首个存活成员身边；未读档时只显示提示，不报错。
using System;
using System.Linq;
using BlueprintCore.Utils;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;      // BlueprintSpellbook
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;                      // Spellbook
using Kingmaker.UnitLogic.Abilities.Blueprints; // BlueprintAbility
using Kingmaker.View;
using Kingmaker.View.Spawners;

namespace RhodesDoctor
{
  internal static class TestPanel
  {
    private static readonly System.Collections.Generic.List<UnitEntityData> Dummies = new();
    private static string _status = "就绪。";

    internal static void OnGUI(UnityModManagerNet.UnityModManager.ModEntry modEntry)
    {
      UnityEngine.GUILayout.Label("<b>罗德岛博士 · 测试工具</b>");
      var game = Game.Instance;
      if (game == null || game.CurrentScene == null || game.Player == null)
      {
        UnityEngine.GUILayout.Label("状态：未读档。请先进入游戏读档后再使用。");
        return;
      }
      // P3：UnitEntityData 是托管对象、不实现 Unity fake-null，u == null 测不出"已被回收"；
      // 改判 Descriptor/State 为空（被回收后这些访问会拿到 null/已释放对象）。
      Dummies.RemoveAll(u => u?.Descriptor?.State == null);
      int alive = Dummies.Count(u => !u.Descriptor.State.IsDead);
      UnityEngine.GUILayout.Label($"状态：{_status}");
      UnityEngine.GUILayout.Label(
        $"场上木桩：{alive} 存活 / {Dummies.Count} 已召唤（不攻击，自带 2 个可驱散增益）。");
      if (UnityEngine.GUILayout.Button("召唤木桩（5000 血，队伍身边）")) SpawnDummy(Guids.TestDummyUnit, "木桩");
      if (UnityEngine.GUILayout.Button("召唤脆皮木桩（10 血）")) SpawnDummy(Guids.TestDummyLowHpUnit, "脆皮木桩");
      if (UnityEngine.GUILayout.Button("召唤增益木桩（6 个常见增益）")) SpawnDummy(Guids.TestDummyBuffed, "增益木桩");
      if (UnityEngine.GUILayout.Button("回满木桩血量")) HealDummies();
      if (UnityEngine.GUILayout.Button("清除木桩")) ClearDummies();
      UnityEngine.GUILayout.Space(6f);
      if (UnityEngine.GUILayout.Button("给我 10 环指令（实验）")) GrantTenRing();
      if (UnityEngine.GUILayout.Button("发放全部指令（实验·绕过已知名额）")) GrantAllSpells();
    }

    /// <summary>
    /// 实验用：把 4 个 10 环指令直接加进队伍里"带战术指令法术书"的成员（即博士）。
    /// 只发**法术**（`Spellbook.AddKnown`）；最高环与法位由法术书自带的每日法位表提供
    /// （`SpellbookSetup.CreateSpellsPerDayTable` 的自建表：20 级 4 个 10 环法位）。
    /// </summary>
    private static void GrantTenRing()
    {
      try
      {
        var game = Game.Instance;
        if (game?.Player == null) { _status = "未读档。"; return; }
        var book = BlueprintTool.Get<BlueprintSpellbook>(Guids.Spellbook);
        if (book == null) { _status = "战术指令法术书蓝图缺失。"; return; }

        foreach (var u in game.Player.Party)
        {
          if (u?.Descriptor == null || u.Descriptor.State.IsDead) continue;
          var sb = u.Descriptor.GetSpellbook(book);
          if (sb == null) continue; // 不是博士（没有战术指令法术书）

          int granted = 0;
          foreach (var idx in SpellsSetup.SpellsOfLevel(10))
          {
            var ab = BlueprintTool.Get<BlueprintAbility>(Guids.Spell(idx));
            if (ab == null) continue;
            // 幂等 + 自愈：**先移除再添加**。反复按按钮不会堆出重复项
            //（重复的已知法术会让法术书界面错乱——"第一次行、后面不行"就是这么来的）；
            // 已经堆过重复的存档，按一次这个按钮也会被清干净。
            sb.RemoveSpell(ab);
            sb.AddKnown(10, ab);
            granted++;
          }
          // 诊断：10 环能不能"放"取决于法位，而法位有一条引擎硬规则
          // （Spellbook.GetSpellsPerDay：施法属性 < 10 + 环数 → 直接 0）。把关键数字打出来。
          int lvl10Known = 0;
          foreach (var a in sb.GetAllKnownSpells())
            if (a != null && a.SpellLevel == 10) lvl10Known++;
          int slots10 = sb.GetSpellsPerDay(10);
          var stat = u.Descriptor.Stats.GetStat(sb.Blueprint.CastingAttribute);
          string statTxt = stat != null ? stat.ModifiedValue.ToString("0.#") : "?";
          Main.Mod.Logger.Log(
            $"[RhodesDoctor] 10 环诊断：{u.CharacterName} 施法属性({sb.Blueprint.CastingAttribute})={statTxt}"
            + $"（10 环要求 ≥20）最高环={sb.MaxSpellLevel} 10环已知={lvl10Known} 10环法位={slots10}");

          _status = $"已给 {u.CharacterName} 发放 10 环指令 {granted} 个（已知 {lvl10Known}，法位 {slots10}）。"
            + (slots10 == 0 ? " ⚠ 10 环法位为 0：施法属性需 ≥ 20。" : "");
          Main.Mod.Logger.Log($"[RhodesDoctor] 测试面板：给 {u.CharacterName} 发放 10 环指令 {granted} 个。");
          return;
        }
        _status = "队伍里没有带战术指令法术书的成员。";
      }
      catch (Exception e)
      {
        _status = "异常：" + e.GetType().Name + "（详见日志）。";
        Main.Mod.Logger.Error("[RhodesDoctor] 发放 10 环指令失败：\n" + e);
      }
    }

    /// <summary>
    /// 实验用：把**全部指令**（0–10 环）直接加进队伍里"带战术指令法术书"的成员（即博士）。
    ///
    /// 为什么要这个按钮：博士是**自发施法者**，"已知法术"名额有限（1 环只能选 3 个），
    /// 升级界面里选不全，导致没法逐个测 FX。这个按钮绕过名额，把所有法术都设为已知。
    ///
    /// ⚠ **光 AddKnown 不够**（"书里有、但拖不出/放不出"就是这么来的）：
    /// 自发施法者的"可用次数"`m_SpontaneousSlots[]` **只在休息时刷新**
    ///（`Spellbook.Rest()` 里 `m_SpontaneousSlots[i] = GetSpellsPerDay(i)`，构造函数里是 0）。
    /// 不刷法位 → `AbilityData.IsAvailable`（= `CanSpend` && 可用次数≠0）为 false
    /// → 法术在书里但灰掉、`MechanicActionBarSlotSpell.IsNotAvailable=true` → 拖不出、放不出。
    /// 所以发放后**必须 `sb.Rest()` 刷新法位**（与 10 环那次同源：缺的是"让法术真正可用"这一步）。
    /// 幂等 + 自愈：**先 RemoveSpell 再 AddKnown**（反复按不会堆重复项）。
    /// </summary>
    private static void GrantAllSpells()
    {
      try
      {
        var game = Game.Instance;
        if (game?.Player == null) { _status = "未读档。"; return; }
        var book = BlueprintTool.Get<BlueprintSpellbook>(Guids.Spellbook);
        if (book == null) { _status = "战术指令法术书蓝图缺失。"; return; }

        foreach (var u in game.Player.Party)
        {
          if (u?.Descriptor == null || u.Descriptor.State.IsDead) continue;
          var sb = u.Descriptor.GetSpellbook(book);
          if (sb == null) continue; // 不是博士（没有战术指令法术书）

          int granted = 0;
          for (int idx = 0; idx < SpellsSetup.SpellCount; idx++)
          {
            var ab = BlueprintTool.Get<BlueprintAbility>(Guids.Spell(idx));
            if (ab == null) continue;
            sb.RemoveSpell(ab);                        // 幂等：先移除（清掉可能堆出的重复项）
            sb.AddKnown(SpellsSetup.RingOf(idx), ab);  // 再按所属环加为已知
            granted++;
          }

          // 关键一步：刷新自发法位（否则 IsAvailable=false → 灰掉、拖不出）。
          sb.Rest();

          // 诊断：把"能不能用"的关键数字打出来（施法属性 / 最高环 / 每环已知数·法位·自发次数）。
          var stat = u.Descriptor.Stats.GetStat(sb.Blueprint.CastingAttribute);
          string statTxt = stat != null ? stat.ModifiedValue.ToString("0.#") : "?";
          var diag = new System.Text.StringBuilder();
          for (int lv = 0; lv <= 10; lv++)
            diag.Append($" [{lv}环:已知{sb.GetKnownSpells(lv).Count}/法位{sb.GetSpellsPerDay(lv)}/自发{sb.GetSpontaneousSlots(lv)}]");
          Main.Mod.Logger.Log(
            $"[RhodesDoctor] 发放诊断：{u.CharacterName} 施法属性({sb.Blueprint.CastingAttribute})={statTxt}"
            + $" 最高环={sb.MaxSpellLevel} 已发={granted}{diag}");

          // 权威诊断：取一个 1 环法术，直接打印它"能不能用"的每一项（IsAvailable = 技能栏是否可用）。
          var sample = sb.GetKnownSpells(1).FirstOrDefault();
          if (sample != null)
            Main.Mod.Logger.Log(
              $"[RhodesDoctor] 可用性诊断：1环样本 '{sample.Blueprint?.name}'"
              + $" IsAvailable={sample.IsAvailable}"
              + $" 在书里可花={sb.CanSpend(sample)}"
              + $" 可用次数={sample.GetAvailableForCastCount()}"
              + $" SpellLevelInSpellbook={sample.SpellLevelInSpellbook} GetSpellLevel={sb.GetSpellLevel(sample)}"
              + $" 临时禁用={sample.TemporarilyDisabled}");

          // 快捷栏诊断：直接调游戏自己的"快捷栏法术来源"（ActionBarSpellbookHelper.Fetch），
          // 看它到底收不收我们的法术。收了 → 是快捷栏/UI 缓存没重建；没收 → 是来源没含。
          try
          {
            var slots = Kingmaker.UI.MVVM._VM.ActionBar.ActionBarSpellbookHelper.Fetch(u);
            int ours = 0;
            foreach (var sl in slots)
            {
              var b = sl?.Spell?.Blueprint;
              if (b == null) continue;
              for (int idx = 0; idx < SpellsSetup.SpellCount; idx++)
                if (b == BlueprintTool.Get<BlueprintAbility>(Guids.Spell(idx))) { ours++; break; }
            }
            Main.Mod.Logger.Log(
              $"[RhodesDoctor] 快捷栏诊断：Fetch 共 {slots.Count} 项，其中战术指令 {ours} 项。");
          }
          catch (Exception ex)
          {
            Main.Mod.Logger.Error("[RhodesDoctor] 快捷栏诊断失败：\n" + ex);
          }

          _status = $"已给 {u.CharacterName} 发放全部指令 {granted} 个（0–10 环，已刷新法位）。";
          return;
        }
        _status = "队伍里没有带战术指令法术书的成员。";
      }
      catch (Exception e)
      {
        _status = "异常：" + e.GetType().Name + "（详见日志）。";
        Main.Mod.Logger.Error("[RhodesDoctor] 发放全部指令失败：\n" + e);
      }
    }

    private static void SpawnDummy(string dummyGuid, string label)
    {
      try
      {
        var game = Game.Instance;
        var state = game?.State?.LoadedAreaState?.MainState;
        if (state == null) { _status = "无场景。"; Main.Mod.Logger.Log("[RhodesDoctor] 召唤木桩：无场景（未读档？）。"); return; }
        var anchor = game.Player.Party.FirstOrDefault(u => u != null && u.Descriptor != null && !u.Descriptor.State.IsDead);
        if (anchor == null) { _status = "找不到存活的队伍成员。"; Main.Mod.Logger.Log("[RhodesDoctor] 召唤木桩：队伍为空。"); return; }
        if (!BlueprintTool.TryGet<BlueprintUnit>(dummyGuid, out var bp) || bp == null)
        {
          _status = $"{label}蓝图未注入（看日志 TestSetup）。";
          Main.Mod.Logger.Log($"[RhodesDoctor] 召唤{label}：蓝图缺失。");
          return;
        }

        // 种子点直接用队伍成员脚下（一定在当前区域、且贴着导航网格），再由 FreePlaceSelector
        // 推离该成员到一个相邻空地。原先固定落在成员 +(2,2)，可能正好在区域边界外或墙里，
        // 游戏会改用一个"优化占位模型"顶替真实外观，看起来就像没刷出来。
        var pos = anchor.Position;
        try
        {
          var seed = anchor.Position;
          var pts = new[] { seed };
          var radii = new[] { 0.75f };
          FreePlaceSelector.RelaxPoints(pts, radii, 1, seed);
          pos = pts[0];
          pos.y = anchor.Position.y;
        }
        catch (Exception e)
        {
          Main.Mod.Logger.Log("[RhodesDoctor] 召唤木桩：落点校正失败，改用锚点位置。\n" + e.Message);
        }

        UnitEntityData unit;
        using (var request = ContextData<UnitSpawningData>.Request())
        {
          request.SetRestrictVariations(true); // 蓝图未挂自定义预设，保险跳过变体选择
          unit = game.EntityCreator.SpawnUnit(
            bp, pos, UnityEngine.Quaternion.identity, state, null);
        }
        if (unit == null)
        {
          _status = "刷怪返回 null。";
          Main.Mod.Logger.Log("[RhodesDoctor] 召唤木桩：SpawnUnit 返回 null。");
          return;
        }
        unit.Position = pos;        // 部分视图路径不套用 SpawnUnit 传入的坐标，这里兜底
        unit.IsAIEnabled = false;   // 不移动、不攻击
        unit.Descriptor.Damage = 0; // 满血
        if (!unit.IsPlayersEnemy)
        {
          // 兜底：自建阵营若未被识别为敌方，借场景内已有敌人的阵营一切。
          var foe = state.AllEntityData.OfType<UnitEntityData>()
            .FirstOrDefault(u => u != null && u != unit && u.IsPlayersEnemy);
          if (foe != null) unit.SwitchFactions(foe.Faction, true);
        }
        bool placeholder = unit.View != null && unit.View.IsOptimizationDummy;
        Dummies.Add(unit);
        _status = (placeholder ? "【占位模型：落点可能在区域边界外】" : "")
          + (unit.IsPlayersEnemy
            ? $"已召唤{label}（血量 {unit.HPLeft}）。"
            : $"已召唤{label}，但阵营非敌方——把它当成中立目标测试，或进战斗场景再试。");
        Main.Mod.Logger.Log(
          $"[RhodesDoctor] {label}已召唤：HP={unit.HPLeft} IsEnemy={unit.IsPlayersEnemy} Placeholder={placeholder}");
      }
      catch (Exception e)
      {
        _status = "异常：" + e.GetType().Name + "（详见日志）。";
        Main.Mod.Logger.Error("[RhodesDoctor] 召唤木桩失败：\n" + e);
      }
    }

    private static void HealDummies()
    {
      try
      {
        int n = 0;
        foreach (var u in Dummies.ToList())
        {
          if (u == null || u.Descriptor == null || u.Descriptor.State.IsDead) continue;
          u.Descriptor.Damage = 0;
          n++;
        }
        _status = $"已回满 {n} 个木桩。";
      }
      catch (Exception e)
      {
        _status = "异常：" + e.GetType().Name + "（详见日志）。";
        Main.Mod.Logger.Error("[RhodesDoctor] 回满木桩失败：\n" + e);
      }
    }

    private static void ClearDummies()
    {
      try
      {
        int n = 0;
        foreach (var u in Dummies.ToList())
        {
          if (u == null) continue;
          try
          {
            Game.Instance.EntityCreator.RemoveEntity(u);
            n++;
          }
          catch (Exception e)
          {
            // 删不掉就地正法：至少不留在场上。
            Main.Mod.Logger.Error("[RhodesDoctor] 移除木桩失败，改用击杀：\n" + e);
            if (u.Descriptor != null && !u.Descriptor.State.IsDead)
              u.Descriptor.Damage = u.Descriptor.MaxHP + 100;
          }
        }
        Dummies.Clear();
        _status = $"已清除 {n} 个木桩。";
      }
      catch (Exception e)
      {
        _status = "异常：" + e.GetType().Name + "（详见日志）。";
        Main.Mod.Logger.Error("[RhodesDoctor] 清除木桩失败：\n" + e);
      }
    }
  }
}
