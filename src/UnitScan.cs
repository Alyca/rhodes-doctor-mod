// 单位扫描：把"当前游戏里存在的全部单位"取成一份**快照**，供自定义动作按阵营 / 距离筛选。
//
// ⚠⚠ 为什么不再用 `Game.Instance.CurrentScene.AllEntityData`（2026-10-12 定位并修）：
//   那个属性只给出**当前区域的场景状态**里的实体——敌人、中立 NPC、区域内召唤物都在里面，
//   但**队伍成员不在**：他们要跨区域跟随玩家，住在 `Game.Instance.Player.CrossSceneState`
//   （一个 `SceneName = "<cross-scene>"` 的独立 `SceneEntitiesState`，见
//   `Kingmaker.Player.CrossSceneState` / `AllCrossSceneUnits`；本 mod 自己在
//   `ContextActionRemoveMyBuffs` 里也早就记过这条："AllCrossSceneUnits 只含队伍成员"）。
//
//   只扫区域状态的实测后果（作者 10-12 反馈 + `Player.log` 证据）：
//     · **重构**：`活着恢复=0 复活=0/0`——一个友军都没找到，而它的视觉恰好是"每个受影响单位各一份"，
//       于是画面上**什么都没有**；
//     · **文明的存续**：施法者处的圣光看得见，但**完全没效果**（没人被治疗 / 复活 / 吃后续增益）；
//     · **王冠回响**：偷来的增益找不到友军可发，全部回落到施法者自己；
//     · **画中世界**：`FindUnit` 按 UniqueId 在区域里找不到被放逐的**队友** → 永远回不来。
//
//   ⇒ 一律走本类。`Game.Instance.State.Units` 就是**原版 AoE 选目标**用的那份全局单位池
//     （`Kingmaker.Designers.GameHelper.GetTargetsAround`：`foreach (UnitEntityData unit in
//     Game.Instance.State.Units)`），队伍与敌人都含在内；这里再并上
//     `Player.AllCrossSceneUnits` 与 `Player.Party` 兜底并去重。
//
//   注意：这是**全局**池，因此调用方**必须**自己做距离（或 UniqueId）筛选——
//   别的区域里残留的单位也可能在池子里（原版同样靠 `IsUnitInRange` 过滤）。
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using UnityEngine;

namespace RhodesDoctor
{
  internal static class UnitScan
  {
    /// <summary>一次扫描的结果快照（列表已去重，可安全地边遍历边加增益 / 改单位）。</summary>
    internal struct Snapshot
    {
      /// <summary>去重后的全部单位（State.Units ∪ 跨场景队伍 ∪ Party）。</summary>
      internal List<UnitEntityData> Units;
      /// <summary>三个来源各自贡献的条数（诊断用，未去重前）。</summary>
      internal int FromState;
      internal int FromCross;
      internal int FromParty;

      /// <summary>一行诊断文本（写日志用）。</summary>
      internal string Describe()
        => $"State.Units={FromState} 跨场景队伍={FromCross} Party={FromParty} 去重后={(Units != null ? Units.Count : 0)}";
    }

    /// <summary>取一份"当前全部单位"的快照。</summary>
    internal static Snapshot Take()
    {
      var snap = new Snapshot { Units = new List<UnitEntityData>() };
      var seen = new HashSet<UnitEntityData>();
      var game = Game.Instance;

      var pool = game?.State?.Units;
      if (pool != null)
      {
        foreach (var u in pool)
        {
          if (!Usable(u)) continue;
          snap.FromState++;
          if (seen.Add(u)) snap.Units.Add(u);
        }
      }

      var cross = game?.Player?.AllCrossSceneUnits;
      if (cross != null)
      {
        foreach (var u in cross)
        {
          if (!Usable(u)) continue;
          snap.FromCross++;
          if (seen.Add(u)) snap.Units.Add(u);
        }
      }

      // `Player.Party` 是属性、每次取值会 `UpdateCharacterLists()`，所以先取数组再遍历。
      var party = game?.Player?.Party;
      if (party != null)
      {
        foreach (var u in party.ToArray())
        {
          if (!Usable(u)) continue;
          snap.FromParty++;
          if (seen.Add(u)) snap.Units.Add(u);
        }
      }

      return snap;
    }

    /// <summary>
    /// 半成品单位（视图/描述符还没初始化完）会出现在全局池里——调用方的第一句话就是
    /// `u.IsPlayerFaction`（= `Descriptor.IsPlayerFaction`），Descriptor 为 null 会直接抛异常、
    /// 把整个动作序列打断。所以这里统一挡掉。
    /// </summary>
    private static bool Usable(UnitEntityData u) => u != null && u.Descriptor != null;

    /// <summary>
    /// 诊断用：列出离 <paramref name="point"/> 最近的 <paramref name="count"/> 个单位，
    /// 每个附上距离与"是不是友军"（<paramref name="caster"/> 视角）。
    /// 用来分辨"池子是空的"还是"阵营筛选写错了"——两者在日志里长得完全不一样。
    /// </summary>
    internal static string DescribeNearest(List<UnitEntityData> units, Vector3 point,
      UnitEntityData caster, int count = 4)
    {
      if (units == null || units.Count == 0) return "（池子里一个单位都没有）";
      var near = units
        .Where(u => u != null && u.Descriptor != null)
        .Select(u => new { u, d = (u.Position - point).magnitude })
        .OrderBy(x => x.d)
        .Take(count)
        .Select(x =>
        {
          bool ally = x.u.IsPlayerFaction || (caster != null && caster.IsAlly(x.u));
          return $"{x.u.CharacterName}{x.d:F1}m{(ally ? "(友)" : "(非友)")}";
        });
      return string.Join(" ", near.ToArray());
    }
  }
}
