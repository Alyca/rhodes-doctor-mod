// 骑兵突进的"拖尾"特效：给每个吃到骑兵突进增益的友军挂一条**加速术的拖尾**，并重染成蓝色。
//
// 为什么自己做（不直接挂增益的 FxOnStart）：
//   · D 类友军增益默认走金色光环（FxLibrary.AuraFor，借"天使·光环"的 FxOnStart）。
//     骑兵突进要的是"加速术的拖尾 + 蓝色"，而 FxOnStart 是引擎直接播放的 PrefabLink，
//     没法改色。所以这里走 SmokeFx / BeaconFx 那套"运行时克隆重染"：借原版加速术增益的
//     FxOnStart（FxLibrary.HasteTrail），克隆一份、重染成蓝色、挂到单位 View 下跟着走，
//     增益移除时销毁。原版加速术特效不碰。
//
// 生命周期：订阅 IUnitBuffHandler，按 buff 蓝图 GUID 过滤（只认骑兵突进那个增益）。
// 增益加上 → 生成；增益移除 → 销毁。单位移动时特效跟着 View 走。
using System;
using System.Collections.Generic;
using Kingmaker.PubSubSystem;
using Kingmaker.UnitLogic.Buffs;
using UnityEngine;

namespace RhodesDoctor
{
  internal static class HasteTrailFx
  {
    // ─────────────────────────────────────────────────────────────────────────────
    // 调色结构：**一份强蓝打底 + 只把"周身缎带"点染成淡蓝**。
    //
    // 为什么是这个方向（别再反过来，反过来的坑都踩过了）：
    //   · 早先能"改对"，是因为当时**整份 prefab 一起染**——同一个强蓝刷到全部粒子系统上，
    //     地面那条自然就蓝了（v2 = (0.18,0.30,1.40)，用户认可）。
    //   · 后来为了"只把周身调浅"把它拆开，**把强蓝从地面那边拿走了**，地面立刻露出原版暖色
    //     （v4/v5/v6/v7 连续四版，用户："底部的又变回来了"）。
    //   · 根因：**周身和地面是两套相反的映射**——
    //       周身（`Trail_cycle` 缎带）：屏幕色 ≈ tint 的 **1.8 次幂**（乘了多次）→ 想浅就**抬 R/G**；
    //       地面（另一套火花）：屏幕色 ≈ tint **× 暖色内容** K≈(1.40,1.00,0.50)
    //         ——原始内容是**金黄**，tint 越白越压不住它 → 想蓝就**给它更强的蓝**。
    //   · 所以正确做法 = **底色保持强蓝（地面就蓝了），只把已确认的"周身缎带"点染成淡蓝**。
    //     而不是"把强蓝送给某个猜出来的系统"——猜名字连着错了两版（先 Trail_cycle、再 SparksForTrails，
    //     两次点染日志都命中、画面都没动），因为**命中日志只证明名字匹配，不证明改对了部件**。
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// **全局底色**：刷到 prefab 的**全部**粒子与材质上，管住"地面那条拖尾"和其余部件。
    /// 用 v2 那支被认可的强蓝 `(0.18, 0.30, 1.40)`（色相 234°）。
    /// ⚠️ 别为了"浅"去抬它的 R/G —— 地面那套映射是 tint × 暖色，抬白了暖色就透出来。
    /// 想动地面就改这一支；想动周身改 <see cref="RibbonColor"/>。
    /// </summary>
    internal static readonly Color BaseColor = new Color(0.18f, 0.30f, 1.40f, 1f);

    /// <summary>
    /// **周身那圈缎带**（原版 prefab 里的 `Trail_cycle` 粒子系统）单独的颜色：**淡蓝**。
    /// 这就是用户说的"周身"，也是唯一需要单独提亮的部分——地面那套不归它管。
    ///
    /// 取 `(0.85, 0.88, 1.25)`：反推（^1.8）屏幕色 ≈ (0.50, 0.53, 1.00) ≈ RGB(127,136,255)，
    /// 色相 236°（蓝）、饱和 0.50。⚠️ 这条链路**"想浅不能靠抬 B"**——抬 B 会先撞 255 的钳位、
    /// 把饱和度推到 0.9，渲成"电光蓝"（v3 的翻车就是这样）。只能**把 R/G 抬起来往白里走**。
    /// 再浅一档：继续抬 R/G（`(0.92,0.94,1.25)`）；想更艳：往回收（`(0.60,0.66,1.30)`）。
    /// </summary>
    internal static readonly Color RibbonColor = new Color(0.85f, 0.88f, 1.25f, 1f);

    /// <summary>
    /// 哪一类粒子系统算"**周身缎带**"。这是**唯一**已确认的部件归属：
    /// 改它 → 画面上的周身变浓（用户 2026-10-09 实测："刚才的周身特效更好看"），
    /// 而地面那条纹丝不动。名字是不区分大小写的包含匹配；原版若改名只改这一行。
    /// </summary>
    private const string RibbonSystemName = "Trail_cycle";

    /// <summary>去色后向白提亮：越大越白。加速术拖尾偏白，取 0.25 保留一点灰阶。</summary>
    private const float Lift = 0.25f;

    /// <summary>骑兵突进增益的蓝图 GUID（Guids.Buff(7)，见 SpellsSetup.Specs 下标）。</summary>
    private static readonly string CavalryChargeBuffGuid =
      Guids.Buff(7).Replace("-", "").ToLowerInvariant();

    /// <summary>单位 → 当前挂着的拖尾克隆。增益移除时据此销毁。</summary>
    private static readonly Dictionary<Kingmaker.EntitySystem.Entities.UnitEntityData, GameObject> Live = new();

    internal static void OnBuffAdded(Buff buff)
    {
      try
      {
        if (!IsCavalryCharge(buff)) return;
        var unit = buff.Owner?.Unit;
        if (unit == null) return;
        // 单位还没 View（如刚加载/未进场景）就跳过；等它 View 就绪后再挂由下次事件兜底。
        var view = unit.View;
        if (view == null) return;

        Prune();
        if (Live.TryGetValue(unit, out var old) && old != null) UnityEngine.Object.Destroy(old);
        // 先整份刷强蓝底（地面那条靠它变蓝），再把"周身缎带"点染成淡蓝——两个常量各管一边。
        var go = FxTint.SpawnAttached(FxLibrary.HasteTrail, view.transform, BaseColor, Lift);
        if (go == null) return;
        FxTint.ApplyTo(go, RibbonColor, RibbonSystemName);
        PointTrailToBodyMaterial(go);
        Live[unit] = go;
        Dbg.Log("[RhodesDoctor] 骑兵突进·加速术拖尾已生成。");
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 骑兵突进·拖尾生成失败：\n" + e);
      }
    }

    internal static void OnBuffRemoved(Buff buff)
    {
      try
      {
        if (!IsCavalryCharge(buff)) return;
        var unit = buff.Owner?.Unit;
        if (unit == null) return;
        if (Live.TryGetValue(unit, out var go))
        {
          Live.Remove(unit);
          if (go != null) UnityEngine.Object.Destroy(go);
        }
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 骑兵突进·拖尾清理失败：\n" + e);
      }
    }

    private static bool IsCavalryCharge(Buff buff)
    {
      var guid = buff?.Blueprint?.AssetGuid.ToString();
      if (string.IsNullOrEmpty(guid)) return false;
      return guid.Replace("-", "").ToLowerInvariant() == CavalryChargeBuffGuid;
    }

    /// <summary>
    /// 拖尾槽指向身体同款材质：`Trail_cycle` 的缎带槽原烟材质（`Horizontal_Smoke00_128_Haste_01`）
    /// 在全部输入（粒子色/双渐变/材质色/贴图）已蓝的情况下仍 render 出绿——黑盒 shader 链路，
    /// 而替换实验证明拖尾槽听换装。于是拖尾槽直接复用**同一渲染器上已染蓝的身体材质**
    /// （柔光 Glow 质感，与周身统一；身体/火花原样不动，不会再出现上版的光漩）。
    /// 若此后仍是绿，则绿条彻底排除本 prefab。
    /// </summary>
    private static void PointTrailToBodyMaterial(GameObject go)
    {
      try
      {
        int n = 0;
        foreach (var psr in go.GetComponentsInChildren<ParticleSystemRenderer>(true))
        {
          try
          {
            if (psr == null) continue;
            var ps = psr.GetComponent<ParticleSystem>();
            if (ps == null || !ps.trails.enabled) continue;
            var body = psr.material;
            if (body == null) continue;
            psr.trailMaterial = body;
            n++;
            Dbg.Log($"[RhodesDoctor] 骑兵突进·拖尾槽已指向身体材质：'{psr.name}'。");
          }
          catch { }
        }
        if (n == 0)
          Dbg.Log("[RhodesDoctor] 骑兵突进·没找到开拖尾的粒子渲染器，拖尾槽未动。");
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 骑兵突进·拖尾槽指向失败（保持原状）：\n" + e);
      }
    }

    private static void Prune()
    {
      var dead = new List<Kingmaker.EntitySystem.Entities.UnitEntityData>();
      foreach (var kv in Live)
        if (kv.Key == null || kv.Value == null) dead.Add(kv.Key);
      foreach (var k in dead) Live.Remove(k);
    }
  }

  /// <summary>增益加上/移除的事件订阅（常驻，Main.Load 里订阅一次）。</summary>
  internal sealed class HasteTrailFxWatcher : IUnitBuffHandler
  {
    public void HandleBuffDidAdded(Buff buff) => HasteTrailFx.OnBuffAdded(buff);
    public void HandleBuffDidRemoved(Buff buff) => HasteTrailFx.OnBuffRemoved(buff);
  }
}
