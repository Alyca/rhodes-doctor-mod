// 信标投射的"物体"：在信标区域中心放一根竖直光柱（借原版"生命之柱"的 Fx，见 FxLibrary.BeaconPillar），
// 跟区域同生同灭（区域生成时放，区域结束时删）——做法照 SmokeFx 的区域事件订阅。
// 区域自己的 area.Fx 放的是照明术的光（见 SpellsSetup 的 BeaconProjection 分支），这里只管那根光柱。
//
// 为什么用区域事件而不是施法时生成：信标是"10 分钟/级"的常驻定点区域，光柱应当**跟着区域走**
// （区域被驱散/到期就一起消失），而不是用一个固定秒数——那会和区域的真实寿命对不上。
using System;
using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using UnityEngine;

namespace RhodesDoctor
{
  internal static class BeaconFx
  {
    /// <summary>光柱颜色：明亮的蓝白（纯发光体观感）。</summary>
    internal static readonly Color PillarColor = new Color(0.68f, 0.80f, 1f, 1f);

    private static readonly Dictionary<AreaEffectEntityData, GameObject> Live = new();

    internal static void OnAreaSpawned(AreaEffectEntityData area)
    {
      try
      {
        if (!IsOurBeacon(area)) return;
        Prune();
        int layer = area.View != null ? area.View.gameObject.layer : 0;
        var go = SpawnPillar(area, layer);
        if (go == null) return;
        Live[area] = go;
        Dbg.Log("[RhodesDoctor] 信标光柱已生成。");
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 信标光柱生成失败：\n" + e);
      }
    }

    /// <summary>
    /// 借原版"生命之柱"的 Fx，克隆重染成蓝白色，放到区域中心。
    /// 尺寸：PillarOfLife 区域是 Cylinder Size=5（半径 5 尺），信标区域是 20 尺——先按原始大小生成，
    /// 首次生成会打一条 `[特效尺寸]` 日志，拿它对比画面上实际多大再决定要不要调 scale。
    /// </summary>
    private static GameObject SpawnPillar(AreaEffectEntityData area, int layer)
    {
      var link = FxLibrary.BeaconPillar;
      if (link == null) return null;
      var go = FxTint.Spawn(link, area.Position, PillarColor, 0f, 0f);
      if (go == null) return null;
      // 跟区域同层，避免被遮挡/渲染层级不对。
      if (layer != 0)
      {
        foreach (var r in go.GetComponentsInChildren<UnityEngine.Renderer>(true))
          r.gameObject.layer = layer;
      }
      return go;
    }

    internal static void OnAreaDestroyed(AreaEffectEntityData area)
    {
      try
      {
        if (Live.TryGetValue(area, out var go))
        {
          Live.Remove(area);
          if (go != null) UnityEngine.Object.Destroy(go);
        }
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 信标光柱清理失败：\n" + e);
      }
    }

    private static bool IsOurBeacon(AreaEffectEntityData area)
    {
      var guid = area?.Blueprint?.AssetGuid.ToString();
      if (string.IsNullOrEmpty(guid)) return false;
      return guid.Replace("-", "").ToLowerInvariant()
        == Guids.BeaconArea.Replace("-", "").ToLowerInvariant();
    }

    private static void Prune()
    {
      var dead = new List<AreaEffectEntityData>();
      foreach (var kv in Live)
        if (kv.Key == null || kv.Value == null) dead.Add(kv.Key);
      foreach (var k in dead) Live.Remove(k);
    }
  }

  /// <summary>区域生成/结束的事件订阅（常驻，Main.Load 里订阅一次）。</summary>
  internal sealed class BeaconFxWatcher : IAreaEffectHandler
  {
    public void HandleAreaEffectSpawned(AreaEffectEntityData entityData)
      => BeaconFx.OnAreaSpawned(entityData);
    public void HandleAreaEffectDestroyed(AreaEffectEntityData entityData)
      => BeaconFx.OnAreaDestroyed(entityData);
  }
}
