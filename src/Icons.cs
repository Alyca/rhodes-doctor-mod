// mod 自带图标：icons\png-64\ 下的 64×64 PNG，启动时按需读成 Sprite 挂到蓝图上。
// 对照表见 icons\README.txt。缺文件不断链：找不到就记一条日志、用游戏默认图标。
// 恶灵 7 个能力共用 4 张图：敏捷三段→agile-form，体质三段→enduring-form，
// 结束恶灵→aftermath（余波图，最贴"结束后进入余波"；想换说一声）。
using System.Collections.Generic;
using System.IO;
using BlueprintCore.Blueprints.CustomConfigurators.Classes;
using BlueprintCore.Blueprints.CustomConfigurators.UnitLogic.Abilities;
using BlueprintCore.Utils;
using UnityEngine;

namespace RhodesDoctor
{
  internal static class Icons
  {
    private static readonly Dictionary<string, Sprite> Cache = new();
    private static readonly HashSet<string> Missing = new();
    /// <summary>借来的原版图标缓存（键 = 原版蓝图 GUID）。null 也缓存，避免反复刷日志。</summary>
    private static readonly Dictionary<string, Sprite> Borrowed = new();

    // 指令 Key → png-64 下的相对路径（对照 icons\README.txt）。
    private static readonly Dictionary<string, string> SpellIcons = new()
    {
      { "TacticalMark", "spells/00-tactical-mark.png" },
      { "FieldDressing", "spells/00-medical-dressing.png" },
      { "BattlefieldSurvey", "spells/00-battlefield-survey.png" },
      { "BeaconProjection", "spells/00-beacon-projection.png" },
      { "SuppressingShot", "spells/00-suppressing-shot.png" },
      { "EmergencyExtraction", "spells/00-emergency-extraction.png" },
      { "HoldTheLine", "spells/01-hold-the-line.png" },
      { "CavalryCharge", "spells/01-cavalry-charge.png" },
      { "FrostPrayer", "spells/01-frost-prayer.png" },
      { "EmergencyTreatment", "spells/01-emergency-treatment.png" },
      { "FireRecon", "spells/01-fire-recon.png" },
      { "MedicalDrone", "spells/01-medic-drone.png" },
      { "Ura", "spells/02-ura.png" },
      { "UnstablePlasma", "spells/02-unstable-plasma.png" },
      { "AntiGravity", "spells/02-anti-gravity.png" },
      { "Smokescreen", "spells/02-smokescreen.png" },
      { "DelayedBlast", "spells/02-delayed-blast.png" },
      { "Silencer", "spells/02-silencer.png" },
      { "Overloading", "spells/03-overloading.png" },
      { "SwordRain", "spells/03-sword-rain.png" },
      { "Calcification", "spells/03-calcification.png" },
      { "LockOfTime", "spells/03-time-lock.png" },
      { "ScorchingBreath", "spells/03-scorching-breath.png" },
      { "Execution", "spells/03-execution.png" },
      { "Sanctuary", "spells/04-sanctuary.png" },
      { "Volcano", "spells/04-volcano.png" },
      { "Dragnet", "spells/04-dragnet.png" },
      { "Nightterror", "spells/04-nightterror.png" },
      { "SwiftBreach", "spells/04-swift-breach.png" },
      { "Flashbang", "spells/04-flashbang.png" },
      { "Chimera", "spells/05-chimera.png" },
      { "Mon3tr", "spells/05-mon3tr.png" },
      { "Ironcast", "spells/05-ironcast.png" },
      { "InkedMountains", "spells/05-inked-mountains.png" },
      { "FrozenDomain", "spells/05-frozen-domain.png" },
      { "RoyalCharge", "spells/05-royal-charge.png" },
      { "TidalElegy", "spells/06-tidal-elegy.png" },
      { "FoxfireDance", "spells/06-foxfire-dance.png" },
      { "DrunkenDream", "spells/06-drunken-dream.png" },
      { "BlackSnakeFlame", "spells/06-black-snake-flame.png" },
      { "ArtilleryBarrage", "spells/06-artillery-barrage.png" },
      { "SinPunishment", "spells/06-sin-punishment.png" },
      { "Twilight", "spells/07-dusk.png" },
      { "MirrorFlow", "spells/07-mirror-flow.png" },
      { "FinalSnipe", "spells/07-final-snipe.png" },
      { "ChainDetonation", "spells/07-chain-detonation.png" },
      { "ThunderousJudgment", "spells/07-thunderous-judgment.png" },
      { "ReturnToAbyss", "spells/07-return-to-abyss.png" },
      { "PaintedWorld", "spells/08-painted-world.png" },
      { "BlazingSunBow", "spells/08-blazing-sun.png" },
      { "TidalVigil", "spells/08-tidal-vigil.png" },
      { "ShadowRealm", "spells/08-shadow-realm.png" },
      { "HarvestYears", "spells/08-harvest-years.png" },
      { "BannerStands", "spells/08-banner-stands.png" },
      { "ShadowBladeDraw", "spells/09-shadow-blade.png" },
      { "Chronostasis", "spells/09-chronostasis.png" },
      { "Cloudpiercer", "spells/09-cloudpiercer.png" },
      { "EarthFury", "spells/09-earth-fury.png" },
      { "Reconstruction", "spells/09-reconstruction.png" },
      { "TacticianOfOld", "spells/09-tactician-of-old.png" },
      { "Civilization", "spells/10-civilization.png" },
      { "EternalGaze", "spells/10-eternal-gaze.png" },
      { "CrownEcho", "spells/10-crown-echo.png" },
      { "Catastrophe", "spells/10-catastrophe.png" },
    };

    /// <summary>按 png-64 下的相对路径取图标；没有返回 null（调用方直接 SetIcon，null 等于默认图标）。</summary>
    internal static Sprite Get(string relativePath)
    {
      if (Cache.TryGetValue(relativePath, out var cached)) return cached;
      if (Missing.Contains(relativePath)) return null;
      try
      {
        var path = Path.Combine(Main.ModPath, "icons", "png-64", relativePath);
        if (!File.Exists(path)) { Missing.Add(relativePath); Main.Mod.Logger.Log($"[RhodesDoctor] 图标文件缺失：{path}"); return null; }
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(File.ReadAllBytes(path))) { Missing.Add(relativePath); return null; }
        var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        sprite.name = relativePath;
        Cache[relativePath] = sprite;
        Dbg.Log($"[RhodesDoctor] 图标已加载：{relativePath}");
        return sprite;
      }
      catch (System.Exception e)
      {
        Main.Mod.Logger.Error($"[RhodesDoctor] 图标加载失败 {relativePath}：{e.Message}");
        Missing.Add(relativePath);
        return null;
      }
    }

    /// <summary>取得指令图标，也供该指令施加的状态使用。</summary>
    internal static Sprite Spell(string spellKey)
      => SpellIcons.TryGetValue(spellKey, out var rel) ? Get(rel) : null;

    /// <summary>优先读取独立状态图；未入包前用现有指令图兜底。</summary>
    internal static Sprite Status(string name, string fallback)
      => Get("status/" + name + ".png") ?? Get(fallback);

    /// <summary>给指令挂图标（缺文件时不断链）。</summary>
    internal static void ApplySpellIcon(AbilityConfigurator ability, string spellKey)
    {
      if (SpellIcons.TryGetValue(spellKey, out var rel))
        ability.SetIcon(Get(rel));
    }

    /// <summary>给恶灵能力挂图标（缺文件时不断链）。</summary>
    internal static void ApplySpecterIcon(AbilityConfigurator ability, string name)
    {
      ability.SetIcon(Get("specter/" + name + ".png"));
    }

    /// <summary>给特性挂图标（20 级罗德岛的博士，缺文件时不断链）。</summary>
    internal static void ApplyFeatureIcon(FeatureConfigurator feature, string name)
    {
      feature.SetIcon(Get("specter/" + name + ".png"));
    }

    /// <summary>
    /// **借游戏内现成蓝图**的图标（2026-10-11）：按 GUID 取原版蓝图（一般是 BlueprintFeature）的
    /// `BlueprintUnitFact.Icon` Sprite，直接挂到本模组特性上——**不新增 PNG、不进 icons 目录**。
    /// 用途：源石技艺精通 / 冷静头脑 / 圣疗 / 引导正能量 / 巴别塔的恶灵（三段特性）这些"没做专属图"的特性。
    /// 取不到（蓝图不存在 / 该蓝图没有 m_Icon）时返回 null —— `SetIcon(null)` 等于用游戏默认图标，不会断链。
    /// </summary>
    internal static Sprite Borrow(string guid)
    {
      if (string.IsNullOrEmpty(guid)) return null;
      if (Borrowed.TryGetValue(guid, out var cached)) return cached;

      Sprite icon = null;
      try
      {
        icon = BlueprintTool.Get<Kingmaker.Blueprints.Facts.BlueprintUnitFact>(guid)?.Icon;
      }
      catch (System.Exception e)
      {
        Main.Mod.Logger.Error($"[RhodesDoctor] 借用图标失败 {guid}：{e.Message}");
      }

      Borrowed[guid] = icon;
      if (icon != null) Dbg.Log($"[RhodesDoctor] 图标已借用：{guid} → {icon.name}");
      else Dbg.Log($"[RhodesDoctor] 图标借用为空（蓝图缺失或无 m_Icon）：{guid}");
      return icon;
    }
  }
}
