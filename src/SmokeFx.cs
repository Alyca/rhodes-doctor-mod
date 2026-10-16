// 烟幕掩护的灰白烟：运行时把借来的云雾 prefab 克隆并重染为灰白，
// 跟烟幕区域同生同灭（区域生成时播，区域结束时删）。
// 因此引擎的 area.Fx 保持为空（否则原色云原样上屏）。
//
// 紫色定位（诊断日志）：不在 startColor（烟雾系统 startColor 全白）、也不在材质 _Color（全白），
// 故对两条链路都做中性化：
//   ① 复制的贴图去色；② 粒子 colorOverLifetime 也改成灰。
// 重染本体已抽到通用 FxTint（Apply(go, Grey, Lift)），一次性爆发等其它特效共用同一套。
// 重染失败则回退为原始云，功能（隐蔽/显形）不受任何影响。
using System;
using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using Kingmaker.ResourceLinks;
using UnityEngine;

namespace RhodesDoctor
{
  internal static class SmokeFx
  {
    /// <summary>借到的云雾 prefab（Smokescreen 分支写入）。</summary>
    internal static PrefabLink SourceLink;

    private static GameObject _template; // 加载一次的原 prefab（只读模板，不改它）
    private static readonly Dictionary<AreaEffectEntityData, GameObject> Live = new();

    private static readonly Color Grey = new Color(0.85f, 0.85f, 0.88f, 1f); // 灰白目标色
    private const float Lift = 0.5f; // 去色后向白提亮：0=纯按亮度(偏深灰)，1=纯白

    internal static void OnAreaSpawned(AreaEffectEntityData area)
    {
      try
      {
        if (!IsOurSmoke(area)) return;
        if (_template == null) LoadTemplate();
        if (_template == null) return;
        Prune();
        var go = UnityEngine.Object.Instantiate(_template, area.Position, Quaternion.identity);
        try
        {
          Inspect(go);
          FxTint.Apply(go, Grey, Lift);
        }
        catch (Exception e)
        {
          Main.Mod.Logger.Error("[RhodesDoctor] 烟雾重染失败（用原色云）：\n" + e);
        }
        Live[area] = go;
        Dbg.Log("[RhodesDoctor] 烟雾已生成（灰白实例）。");
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 烟雾生成失败：\n" + e);
      }
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
        Main.Mod.Logger.Error("[RhodesDoctor] 烟雾清理失败：\n" + e);
      }
    }

    private static bool IsOurSmoke(AreaEffectEntityData area)
    {
      var guid = area?.Blueprint?.AssetGuid.ToString();
      if (string.IsNullOrEmpty(guid)) return false;
      return guid.Replace("-", "").ToLowerInvariant()
        == Guids.SmokescreenArea.Replace("-", "").ToLowerInvariant();
    }

    private static void LoadTemplate()
    {
      try
      {
        if (SourceLink == null || !SourceLink.Exists())
        {
          Dbg.Log("[RhodesDoctor] 烟雾模板缺失（借用失败，见注入日志）。");
          return;
        }
        _template = SourceLink.Load(false, true);
        if (_template == null)
          Dbg.Log("[RhodesDoctor] 烟雾模板加载返回 null。");
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 烟雾模板加载失败：\n" + e);
      }
    }

    private static void Prune()
    {
      var dead = new List<AreaEffectEntityData>();
      foreach (var kv in Live)
        if (kv.Key == null || kv.Value == null) dead.Add(kv.Key);
      foreach (var k in dead) Live.Remove(k);
    }

    /// <summary>诊断：打印粒子着色来源与材质全部颜色/贴图属性，便于继续定位。</summary>
    [System.Diagnostics.Conditional("RD_VERBOSE")]
    private static void Inspect(GameObject go)
    {
      try
      {
        int n = 0;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
          n++;
          var main = ps.main;
          var col = ps.colorOverLifetime;
          Dbg.Log(
            $"[RhodesDoctor][烟雾诊断] 粒子#{n} name={ps.name} "
            + $"startColor({main.startColor.mode})={main.startColor.color} "
            + $"colOL.enabled={col.enabled}");
        }
        int mi = 0;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
          foreach (var mat in r.materials)
          {
            mi++;
            if (mat == null) { Dbg.Log($"[RhodesDoctor][烟雾诊断] 材质#{mi}=null"); continue; }
            var sh = mat.shader;
            var sb = new System.Text.StringBuilder();
            if (sh != null)
            {
              for (int i = 0; i < sh.GetPropertyCount(); i++)
              {
                var nm = sh.GetPropertyName(i);
                var ty = sh.GetPropertyType(i);
                if (ty == UnityEngine.Rendering.ShaderPropertyType.Color)
                  sb.Append(nm).Append('=').Append(mat.GetColor(nm)).Append("; ");
                else if (ty == UnityEngine.Rendering.ShaderPropertyType.Texture)
                {
                  var t = mat.GetTexture(nm);
                  sb.Append(nm).Append('=').Append(t != null ? t.name : "null").Append("; ");
                }
              }
            }
            Dbg.Log($"[RhodesDoctor][烟雾诊断] 材质#{mi} shader={sh?.name} {sb}");
          }
        }
        Dbg.Log($"[RhodesDoctor][烟雾诊断] 共 {n} 个粒子系统 / {mi} 个材质实例。");
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 烟雾诊断失败：\n" + e);
      }
    }
  }

  /// <summary>区域生成/结束的事件订阅（常驻，Main.Load 里订阅一次）。</summary>
  internal sealed class SmokeFxWatcher : IAreaEffectHandler
  {
    public void HandleAreaEffectSpawned(AreaEffectEntityData entityData)
      => SmokeFx.OnAreaSpawned(entityData);
    public void HandleAreaEffectDestroyed(AreaEffectEntityData entityData)
      => SmokeFx.OnAreaDestroyed(entityData);
  }
}
