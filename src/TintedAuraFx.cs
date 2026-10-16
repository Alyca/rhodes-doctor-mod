// "跟增益同生同灭、长在单位身上、而且**要改色**"的持续光环（原 SlowAuraFx 的通用版）。
//
// 为什么不直接 `SetFxOnStart`：那是引擎直接播的 PrefabLink，**改不了色**。要非原色就得
// 克隆一份、只改副本的着色（原版特效不碰）——做法同 SmokeFx / HasteTrailFx。
//
// 表里每一项 = 一个"增益/减益 → (素材, 颜色)"；新加一个"要改色的常驻光环"只需往表里加一行。
//
// ⚠ 素材必须是**常驻**视觉（原版增益里"常驻一片"的那种，如缓慢/血雾/加速术）。
//   "施法瞬间爆发"型的 FxOnStart（光亮术、治疗审判等）挂上去"放完就没"、什么都看不见
//   —— 见 dev-notes §7.10.5。加素材前先用 `tools/scan_buff_fx_all.py` 筛，且优先挑
//   "本来就在单位身上常驻一片"的增益（灵光 / 光环 / 血雾这类）。
//
// 生命周期：订阅 IUnitBuffHandler，按 buff 蓝图 GUID 过滤；增益加上 → 生成；移除 → 销毁。
using System;
using System.Collections.Generic;
using Kingmaker.PubSubSystem;
using Kingmaker.ResourceLinks;
using Kingmaker.UnitLogic.Buffs;
using UnityEngine;

namespace RhodesDoctor
{
  internal static class TintedAuraFx
  {
    private sealed class Entry
    {
      /// <summary>归一化后的 buff 蓝图 GUID（去横线、小写）。</summary>
      public string BuffGuid;
      /// <summary>素材（借原版增益的 FxOnStart）。</summary>
      public Func<PrefabLink> Link;
      /// <summary>重染目标色（<see cref="NoTint"/> 为 true 时不使用）。</summary>
      public Color Tint;
      /// <summary>去色后向白提亮（越大越白）。</summary>
      public float Lift;
      /// <summary>true = **原色**挂载、不重染（用于"素材本身就是对的颜色"）。</summary>
      public bool NoTint;
      /// <summary>克隆时是否保留素材里的 Unity `Light`（见 dev-notes §7.10.5）。</summary>
      public bool KeepLights;
      /// <summary>
      /// **整份特效的不透明度倍率**（1 = 原样，越小越"淡"）。用于"**平均地**减弱整个特效"
      /// ——只降不透明度，不改色相，所以不会偏色（作者 10-10：涌潮悲歌"太浓"，要"平均地减少"）。
      /// </summary>
      public float Opacity = 1f;
      /// <summary>
      /// 生成时的额外**本地偏移**（米，挂在单位 View 下；用于微调"偏上 / 偏下 / 偏一侧"）。
      /// ⚠ 曾经有过"按包围盒自动水平居中"（`Recenter`），但**粒子型素材的包围盒会被吹到几百米**
      ///   （见 10-11 Player.log：海之披风 356 米），按它居中会把整份特效**挪飞**（作者："干脆没特效了"）。
      ///   已删除；偏了就用手动偏移量。
      /// </summary>
      public Vector3 LocalOffset = Vector3.zero;
      /// <summary>日志用名。</summary>
      public string Label;
    }

    private static readonly Entry[] Entries =
    {
      // 时之锁（减益，Guids.Buff(21)）：原版缓慢特效 → 莫斯提马的蓝。
      new Entry
      {
        BuffGuid = Norm(Guids.Buff(21)),
        Link = () => FxLibrary.SlowAura,
        Tint = new Color(0.30f, 0.62f, 1.40f, 1f),
        Lift = 0.25f,
        KeepLights = false, // 与原 SlowAuraFx 行为一致
        Label = "时之锁·蓝色缓慢光环",
      },
      // 反重力（减益，Guids.Buff(14)）：原版缓慢特效 → **原色**常驻（作者 10-10：原来只有一发、不持续）。
      new Entry
      {
        BuffGuid = Norm(Guids.Buff(14)),
        Link = () => FxLibrary.SlowAura,
        NoTint = true,
        KeepLights = true,
        Label = "反重力·缓慢光环",
      },
      // 不稳定血浆（增益，Guids.Buff(13)）：原版血雾 → 血色（作者 10-10：再鲜红一点）。
      new Entry
      {
        BuffGuid = Norm(Guids.Buff(13)),
        Link = () => FxLibrary.PlasmaAura,
        Tint = new Color(1.00f, 0.15f, 0.15f, 1f),
        Lift = 0.20f,
        KeepLights = true,
        Label = "不稳定血浆·血色光环",
      },
      // 迅捷突破（加速效果，Guids.HasteAccelBuff，C34）：原版加速术拖尾 → **风笛的橙**（作者 10-10）。
      // ⚠ 该加速增益由**迅捷突破与永恒的注视共用**（C34/C36）——所以永恒的注视触发后也会带这条橙拖尾。
      //    若要分开，可把 BuffGuid 改用 SwiftBreachBuff（1 轮，仅迅捷突破，但更短）。
      new Entry
      {
        BuffGuid = Norm(Guids.HasteAccelBuff),
        Link = () => FxLibrary.HasteTrail,
        Tint = new Color(1.30f, 0.62f, 0.18f, 1f),
        Lift = 0.20f,
        KeepLights = false, // 与骑兵突进的拖尾（HasteTrailFx）保持一致
        Label = "迅捷突破·橙色加速拖尾",
      },
      // 奇美拉（增益，Guids.Buff(30)）：天使**罩子** → **暗红**（作者 10-10：光环太不明显，换更重的罩子）。
      new Entry
      {
        BuffGuid = Norm(Guids.Buff(30)),
        Link = () => FxLibrary.BarrierAura,
        Tint = new Color(0.85f, 0.12f, 0.12f, 1f),
        Lift = 0.10f,
        KeepLights = true,
        Label = "奇美拉·暗红罩子",
      },
      // 铸铁（增益，Guids.Buff(32)）：寒冰**护甲** → **淡蓝**（作者 10-10：光环在腿上有个诡异物件，换护甲）。
      new Entry
      {
        BuffGuid = Norm(Guids.Buff(32)),
        Link = () => FxLibrary.ArmorAura,
        Tint = new Color(0.62f, 0.90f, 1.35f, 1f),
        Lift = 0.35f,
        KeepLights = true,
        Label = "铸铁·淡蓝护甲",
      },
      // 梦魇（减益，作者 10-10：梦魇也该持续；且原素材看着像"领域"）：
      // 原素材"美味恐惧"（WitchHexDeliciousFrightBuff，粒子 BorderGlow / Border_Loop / Border_ThinActive /
      // DarkBottomSmoke）是**地面圆环 + 底部烟雾**的魔法阵——贴在敌人身上像"给敌人放了个领域"，不像 debuff。
      // 换成**身体型**素材：原版**血雾**（BloodHazeBuff，与"不稳定血浆"同源）→ 重染**恐惧的黑**，
      // 贴在敌人身上是一层"黑影缠身"，读起来就是 debuff。
      // 梦魇挂两个增益（恐慌 2 轮 → 到期转动摇），两个都要挂，恐惧才"全程"。
      new Entry
      {
        BuffGuid = Norm(Guids.NightterrorPanicBuff),
        Link = () => FxLibrary.PlasmaAura,
        Tint = new Color(0.10f, 0.06f, 0.16f, 1f),
        Lift = 0.0f,
        KeepLights = true,
        Label = "梦魇·黑影缠身（恐慌）",
      },
      new Entry
      {
        BuffGuid = Norm(Guids.NightterrorShakenBuff),
        Link = () => FxLibrary.PlasmaAura,
        Tint = new Color(0.10f, 0.06f, 0.16f, 1f),
        Lift = 0.0f,
        KeepLights = true,
        Label = "梦魇·黑影缠身（动摇）",
      },
      // ── 单体批次（6–9 环）：自增益 / 敌方减益的常驻光环（2026-10-10）──
      // 涌潮悲歌（自增益，Guids.Buff(36)）：**原来的天使罩子 + 原来的蓝**（作者 10-10：原特效挺好），
      // 只把整份特效**平均地减弱**——用 `Opacity` 降不透明度（不改色相，所以不偏色）。
      // 想更淡/更浓只改 `Opacity`（0~1）。
      new Entry
      {
        BuffGuid = Norm(Guids.Buff(36)),
        Link = () => FxLibrary.BarrierAura,
        Tint = new Color(0.35f, 0.75f, 1.45f, 1f),
        Lift = 0.30f,
        KeepLights = true,
        Opacity = 0.5f,
        Label = "涌潮悲歌·淡蓝护罩",
      },
      // 归溟（自增益，Guids.ReturnToAbyssBuff）：原版**血雾**（身体型） → **蓝色**（作者 10-10：要蓝色）。
      new Entry
      {
        BuffGuid = Norm(Guids.ReturnToAbyssBuff),
        Link = () => FxLibrary.PlasmaAura,
        Tint = new Color(0.30f, 0.60f, 1.45f, 1f),
        Lift = 0.25f,
        KeepLights = true,
        Label = "归溟·蓝色缠身",
      },
      // 画中世界（重构后，10-10 深夜）：**放逐载体**挂在施法者身上（`Guids.PaintedWorldBanishBuff`）
      // → 紫环，表示"画境进行中"；**返回后麻痹**挂在目标身上（`Guids.PaintedWorldParalysisBuff`）→ 同样的紫环。
      new Entry
      {
        BuffGuid = Norm(Guids.PaintedWorldBanishBuff),
        Link = () => FxLibrary.GoldHalo,
        Tint = new Color(0.68f, 0.42f, 1.25f, 1f),
        Lift = 0.30f,
        KeepLights = true,
        Label = "画中世界·画境紫环（施法者）",
      },
      new Entry
      {
        BuffGuid = Norm(Guids.PaintedWorldParalysisBuff),
        Link = () => FxLibrary.GoldHalo,
        Tint = new Color(0.68f, 0.42f, 1.25f, 1f),
        Lift = 0.30f,
        KeepLights = true,
        Label = "画中世界·画境紫环（麻痹）",
      },
      // 旧日的战术家（自增益，两个套件各一条）：**蓝色**光环（作者 10-10：应该是蓝色的）。
      // 该法术同时给"坚韧/敏捷"两套件，只有对端在场时才补，所以两条都要挂。
      new Entry
      {
        BuffGuid = Norm(Guids.TacticianEnduringBuff),
        Link = () => FxLibrary.GoldHalo,
        Tint = new Color(0.30f, 0.62f, 1.45f, 1f),
        Lift = 0.22f,
        KeepLights = true,
        Label = "旧日的战术家·蓝色光环（坚韧）",
      },
      new Entry
      {
        BuffGuid = Norm(Guids.TacticianAgileBuff),
        Link = () => FxLibrary.GoldHalo,
        Tint = new Color(0.30f, 0.62f, 1.45f, 1f),
        Lift = 0.22f,
        KeepLights = true,
        Label = "旧日的战术家·蓝色光环（敏捷）",
      },
      // ── 10-11：过载模式 / 圣域 改为"**跟随单位**的常驻光环" ──
      // 原来走 D 类的 `SetFxOnStart`，观感**不跟随**单位（作者：这两个是给"当时范围内的单位"挂 buff，要跟着人走）。
      // 过载模式（增益，Guids.Buff(18)）：加速术**拖尾** → **琥珀色**（作者：原来那个罩子**太夸张**）。
      new Entry
      {
        BuffGuid = Norm(Guids.Buff(18)),
        Link = () => FxLibrary.HasteTrail,
        Tint = new Color(1.15f, 0.85f, 0.35f, 1f),
        Lift = 0.20f,
        KeepLights = false,
        Label = "过载模式·琥珀加速拖尾",
      },
      // 圣域（增益，Guids.Buff(24)）：寒冰**护甲** → **淡白金**（作者：换个特效，不要和过载模式一样）。
      new Entry
      {
        BuffGuid = Norm(Guids.Buff(24)),
        Link = () => FxLibrary.ArmorAura,
        Tint = new Color(0.85f, 0.90f, 1.25f, 1f),
        Lift = 0.40f,
        KeepLights = true,
        Label = "圣域·淡白护甲",
      },
      // 乌拉（增益，Guids.Buff(12)）：**天使光环**（一圈细环） → **橙色**（作者 10-11）。
      // ⚠ 之前两次都不橙，两个原因：① `Lift` 太小（0.2/0.4）——`Lift` 是**贴图提亮**，
      //    素材的**金色贴图**没被压下去，上屏就还是"金"的；② 我把 G 压太低（0.45/0.55）→ 成了红、不是橙。
      //    现在 `Lift` 提到 0.80（≈把贴图整体换成目标色）、G 给到 0.85（屏幕色 ≈ tint 的 2~3 次幂，
      //    要"橙"就得让 G 别掉太低）。
      new Entry
      {
        BuffGuid = Norm(Guids.Buff(12)),
        Link = () => FxLibrary.GoldHalo,
        Tint = new Color(1.60f, 0.85f, 0.18f, 1f),
        Lift = 0.80f,
        KeepLights = true,
        Label = "乌拉·橙色光环",
      },
      // ── 10-11：岁稔 / 旗帜不倒 / 潮汐守望 / 永恒的注视 改"以施法者为中心 + **跟随单位**的独立特效" ──
      // 原来这四个都借 D 类的**天使屏障**（金色罩子）`SetFxOnStart`，观感不跟随、四个还一模一样。
      // 现各自换**互不相同**的素材，走 TintedAuraFx（克隆后挂单位 `View`，一定跟随）；
      // 四者的着色也彼此分开（禾绿 / 暖红金 / 青蓝 / 银紫）。
      // 岁稔（岁稔·积攒，Guids.HarvestYearsBuff）：萨满·自然之灵 → **鲜绿（植物绿）**。
      // 作者 10-11："可以改成亲绿植物的绿色"——把 G 给满、R/B 压低，`Lift` 提高把素材自带色压下去。
      new Entry
      {
        BuffGuid = Norm(Guids.HarvestYearsBuff),
        Link = () => FxLibrary.NatureAura,
        Tint = new Color(0.35f, 1.30f, 0.35f, 1f),
        Lift = 0.70f,
        KeepLights = true,
        Label = "岁稔·植物绿自然灵光",
      },
      // 旗帜不倒（Guids.BannerStandsBuff）：阿萨塔·英雄决意之歌 → **暖红金**。
      new Entry
      {
        BuffGuid = Norm(Guids.BannerStandsBuff),
        Link = () => FxLibrary.ValorAura,
        Tint = new Color(1.35f, 0.55f, 0.25f, 1f),
        Lift = 0.60f,
        KeepLights = true,
        Label = "旗帜不倒·暖红金决意光",
      },
      // 潮汐守望（增益，Guids.Buff(50)）：原版"水元素聚能" → **青蓝水膜**（作者：特效要持续在单位身上）。
      // ⚠ 作者 10-11：上一版借"海之披风"其实是**地面水花**、"和人物差好远"；换聚能素材。
      new Entry
      {
        BuffGuid = Norm(Guids.Buff(50)),
        Link = () => FxLibrary.TideAura,
        Tint = new Color(0.25f, 0.95f, 1.25f, 1f),
        Lift = 0.60f,
        KeepLights = true,
        Label = "潮汐守望·青蓝水膜",
      },
      // 永恒的注视（Guids.EternalGazeBuff）：萨满·邪眼 → **银紫"注视"**（作者：原特效不好且重复）。
      new Entry
      {
        BuffGuid = Norm(Guids.EternalGazeBuff),
        Link = () => FxLibrary.GazeAura,
        Tint = new Color(0.95f, 0.75f, 1.35f, 1f),
        Lift = 0.55f,
        KeepLights = true,
        Label = "永恒的注视·银紫注视",
      },

      // ── 巴别塔的恶灵：六个形态增益的"身上冒黑烟"（作者 2026-10-11）──
      // 这六个是**职业能力**（不是法术），原先完全没有视觉；做法与法术光环完全同源：
      // 借原版**血雾**（`PlasmaAura`，身体型常驻雾）→ 重染**黑烟色**，挂到形态持有者身上，
      // 形态增益一消失（结束恶灵 / 20 级压制 / 资源耗尽自动结束）黑烟即灭。
      // 三段共 6 个增益（敏捷/体质 × V1/V2/V3）都要登记——V2/V3 是各自独立的蓝图，不共用。
      // ⚠ 调"烟"的浓淡：`Tint` 越往黑走烟越黑（屏幕色 ≈ tint 的 2~3 次幂，见 dev-notes §7.11.5），
      //   `Lift` 给一点点是为了别变成一块纯黑（0.10 左右就能看出"烟"的层次）；`Opacity` 可整体压淡。
      // ⚠ 想换素材只改这 6 行的 `Link`：备选 `Buffs/ShadowFX`（`b6b3ca16af1fc8c47bb055d94c3a1d54`，未验证是否常驻）。
      new Entry
      {
        BuffGuid = Norm(Guids.AgileBuffV1),
        Link = () => FxLibrary.PlasmaAura,
        Tint = new Color(0.26f, 0.24f, 0.30f, 1f),
        Lift = 0.10f,
        KeepLights = true,
        Label = "巴别塔的恶灵·黑烟（敏捷形态·一段）",
      },
      new Entry
      {
        BuffGuid = Norm(Guids.EnduringBuffV1),
        Link = () => FxLibrary.PlasmaAura,
        Tint = new Color(0.26f, 0.24f, 0.30f, 1f),
        Lift = 0.10f,
        KeepLights = true,
        Label = "巴别塔的恶灵·黑烟（体质形态·一段）",
      },
      new Entry
      {
        BuffGuid = Norm(Guids.AgileBuffV2),
        Link = () => FxLibrary.PlasmaAura,
        Tint = new Color(0.22f, 0.20f, 0.28f, 1f),
        Lift = 0.10f,
        KeepLights = true,
        Label = "巴别塔的恶灵·黑烟（敏捷形态·二段）",
      },
      new Entry
      {
        BuffGuid = Norm(Guids.EnduringBuffV2),
        Link = () => FxLibrary.PlasmaAura,
        Tint = new Color(0.22f, 0.20f, 0.28f, 1f),
        Lift = 0.10f,
        KeepLights = true,
        Label = "巴别塔的恶灵·黑烟（体质形态·二段）",
      },
      new Entry
      {
        BuffGuid = Norm(Guids.AgileBuffV3),
        Link = () => FxLibrary.PlasmaAura,
        Tint = new Color(0.18f, 0.16f, 0.26f, 1f),
        Lift = 0.10f,
        KeepLights = true,
        Label = "巴别塔的恶灵·黑烟（敏捷形态·三段）",
      },
      new Entry
      {
        BuffGuid = Norm(Guids.EnduringBuffV3),
        Link = () => FxLibrary.PlasmaAura,
        Tint = new Color(0.18f, 0.16f, 0.26f, 1f),
        Lift = 0.10f,
        KeepLights = true,
        Label = "巴别塔的恶灵·黑烟（体质形态·三段）",
      },
    };

    private static readonly Dictionary<(Kingmaker.EntitySystem.Entities.UnitEntityData, int), GameObject> Live = new();

    private static string Norm(string guid) => (guid ?? "").Replace("-", "").ToLowerInvariant();

    internal static void OnBuffAdded(Buff buff)
    {
      try
      {
        int idx = IndexOf(buff);
        if (idx < 0) return;
        var unit = buff.Owner?.Unit;
        if (unit == null) return;
        // 单位还没 View（刚加载/未进场景）就跳过；等它 View 就绪后由下次事件兜底。
        var view = unit.View;
        if (view == null) return;

        Prune();
        var key = (unit, idx);
        if (Live.TryGetValue(key, out var old) && old != null) UnityEngine.Object.Destroy(old);

        var e = Entries[idx];
        var link = e.Link();
        var go = e.NoTint
          ? FxTint.SpawnAttachedRaw(link, view.transform, e.LocalOffset)               // 原色
          : FxTint.SpawnAttached(link, view.transform, e.Tint, e.Lift, e.KeepLights, e.Opacity); // 重染
        if (go == null) return;
        // 重染版挂载时把 localPosition 归零了，这里补上每条的本地偏移（默认 0）。
        if (!e.NoTint) go.transform.localPosition = e.LocalOffset;
        Live[key] = go;
        Dbg.Log($"[RhodesDoctor] {e.Label}已生成。");
      }
      catch (Exception ex)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 常驻光环生成失败：\n" + ex);
      }
    }

    internal static void OnBuffRemoved(Buff buff)
    {
      try
      {
        int idx = IndexOf(buff);
        if (idx < 0) return;
        var unit = buff.Owner?.Unit;
        if (unit == null) return;
        var key = (unit, idx);
        if (Live.TryGetValue(key, out var go))
        {
          Live.Remove(key);
          if (go != null) UnityEngine.Object.Destroy(go);
        }
      }
      catch (Exception ex)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 常驻光环清理失败：\n" + ex);
      }
    }

    private static int IndexOf(Buff buff)
    {
      var guid = buff?.Blueprint?.AssetGuid.ToString();
      if (string.IsNullOrEmpty(guid)) return -1;
      var norm = Norm(guid);
      for (int i = 0; i < Entries.Length; i++)
        if (Entries[i].BuffGuid == norm) return i;
      return -1;
    }

    private static void Prune()
    {
      var dead = new List<(Kingmaker.EntitySystem.Entities.UnitEntityData, int)>();
      foreach (var kv in Live)
        if (kv.Key.Item1 == null || kv.Value == null) dead.Add(kv.Key);
      foreach (var k in dead) Live.Remove(k);
    }
  }

  /// <summary>增益加上/移除的事件订阅（常驻，Main.Load 里订阅一次）。</summary>
  internal sealed class TintedAuraFxWatcher : IUnitBuffHandler
  {
    public void HandleBuffDidAdded(Buff buff) => TintedAuraFx.OnBuffAdded(buff);
    public void HandleBuffDidRemoved(Buff buff) => TintedAuraFx.OnBuffRemoved(buff);
  }
}
