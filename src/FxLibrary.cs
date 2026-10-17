// 元素化特效库（游戏内"群体法术视觉"的共用素材池）。
// 做法照 docs/fx-recolor.md：运行时从原版法术借 PrefabLink，不写死 AssetId。
// 每个元素给一串候选（原版热门的同元素法术），按顺序借第一个成功的；借不到返回 null，
// 调用方保留"无特效"（功能不受影响），只打日志。
//
// 为什么按元素而不是按法术：同一个元素的多个法术复用同一份爆发，省事且风格统一；
// 需要区分时再走 SmokeFx 那套运行时克隆重染改色（原版特效不碰）。
using System.Collections.Generic;
using BlueprintCore.Blueprints.References;
using Kingmaker.ResourceLinks;

namespace RhodesDoctor
{
  internal static class FxLibrary
  {
    private static readonly Dictionary<string, PrefabLink> Cache = new();

    /// <summary>
    /// 按元素取"施法/命中爆发"FX（一次性）。同一元素只借一次并缓存（失败也缓存，避免反复报错）。
    /// 先按 abilities 借"法术自带的命中爆发"（AbilitySpawnFx）；全部落空时退到 areas 的区域 FX
    /// （区域的一次性爆发动作 → 区域常驻 FX）。
    ///
    /// 加区域兜底的原因：AbilitySpawnFx 是**可选**组件，很多法术压根没挂，只靠它借会静默拿不到，
    /// 表现为法术完全没特效。物理/精神最初就是这么丢的（StoneCall / ObsidianFlow / MindFog 都没有
    /// AbilitySpawnFx），而它们的**区域**蓝图是有 Fx 的。候选一律用 AbilityRefs/AbilityAreaEffectRefs
    /// 静态引用，不写死 AssetId。
    /// </summary>
    private static PrefabLink Burst(string element, string[] abilities, string[] areas = null)
    {
      if (Cache.TryGetValue(element, out var cached)) return cached;
      var link = FxBorrow.AbilityBurstFx(abilities);
      if (link == null && areas != null && areas.Length > 0)
        link = FxBorrow.AreaBurstFx(areas) ?? FxBorrow.AreaFx(areas);
      Cache[element] = link; // 负缓存：借不到时存 null，后续调用直接返回
      return link;
    }

    // ── 元素表（键 = 元素名，值 = 借用候选，首个成功者即用）──
    // 候选已逐个核对原版蓝图（AbilitySpawnFx 是否存在 / 区域是否有 Fx）；实测借不到的旧猜测
    // 一律往后排或删掉，并在注释里标明，免得下批再踩。

    /// <summary>火焰：火球 / 燃烧之手都没有 AbilitySpawnFx，靠焰击术（实测成功）。</summary>
    internal static PrefabLink Fire => Burst("fire", new[]
    {
      AbilityRefs.FlameStrike.ToString(),
      AbilityRefs.Fireball.ToString(),
      AbilityRefs.BurningHands.ToString(),
    });

    /// <summary>电击：闪电束 / 链状闪电都没有 AbilitySpawnFx，靠召雷术（实测成功）。</summary>
    internal static PrefabLink Electricity => Burst("electricity", new[]
    {
      AbilityRefs.CallLightning.ToString(),
      AbilityRefs.LightningBolt.ToString(),
      AbilityRefs.ChainLightning.ToString(),
    });

    /// <summary>冰霜：待 C 类批次启用。寒冰锥 / 冰风暴**都没有** AbilitySpawnFx，用前必须重选候选。</summary>
    internal static PrefabLink Cold => Burst("cold", new[]
    {
      AbilityRefs.ConeOfCold.ToString(),
      AbilityRefs.IceStorm.ToString(),
    });

    /// <summary>神圣 / 光：阳炎爆 / 圣光裁决 / 荣耀迸发都有 AbilitySpawnFx（实测阳炎爆成功）；灼热光辉没有。</summary>
    internal static PrefabLink Holy => Burst("holy", new[]
    {
      AbilityRefs.Sunburst.ToString(),
      AbilityRefs.HolySmite.ToString(),
      AbilityRefs.BurstOfGlory.ToString(),
      AbilityRefs.SearingLight.ToString(),
    });

    /// <summary>
    /// 物理冲击（挥砍 / 轰击 / 穿刺）：落石 / 黑曜流**都没有** AbilitySpawnFx（实测借不到，
    /// 导致大地之怒完全没特效、王者冲锋/穿云只剩直线）。改用土系冲击法术，并以区域 FX 兜底。
    /// </summary>
    internal static PrefabLink Physical => Burst("physical", new[]
    {
      AbilityRefs.ShaitanEarthblastAbility.ToString(),
      AbilityRefs.BloodlineElementalEarthElementalBlastAbility.ToString(),
      AbilityRefs.SpikeStones.ToString(),
      AbilityRefs.StoneCall.ToString(),
      AbilityRefs.ObsidianFlow.ToString(),
    }, new[]
    {
      AbilityAreaEffectRefs.ObsidianFlowArea.ToString(),
      AbilityAreaEffectRefs.StoneCallArea.ToString(),
    });

    // ── C 类：持续地面区域 ─────────────────────────────────────────────────────────
    //
    // C 类（钙质化 / 冰封领域 / 儿时舞乐 / 天灾降临）要的是"地上一直铺着的一片"，
    // 而不是"炸一下就没"的爆发——所以借的是**区域的常驻 `Fx`**（`FxBorrow.AreaFx`），
    // 不是法术的 `AbilitySpawnFx`。挂法见 `docs/fx-plan.md`：`area.Fx = link`（原色、引擎自己管生命周期）。
    //
    // 选材一律**离线查 `blueprints.zip`**，别进游戏一个个试：
    //   ① 先看 `Shape`——**只有 `Cylinder` 是圆的**（`Wall` 是直线墙，拼不成圆，剑雨为此白折腾两轮）；
    //   ② 再看 `Size`——`Cylinder` 的 `Size` 是**半径（尺）**，直接和法术范围比，**相等就不用缩放**；
    //   ③ 最后确认 `Fx` 非 null（原版大量区域这个字段是空的）。

    /// <summary>
    /// **钙质化**（环 3·20 尺地面区域）的地面素材：原版**石质/结晶质**区域的常驻 `Fx`。
    ///
    /// ⚠️ **别再用 `DeadlyEarthEarthBlastArea`**（2026-10-09 用过一版，用户否掉）：
    /// 它是土元素"致命大地"，画面上是**翻腾的泥土 + 泥浆飞溅**——既不"钙"也难看。
    /// 判据是"语义"不是"元素"：钙质化要的是**石头/结晶**，不是"土"。
    ///
    /// 现在的候选按"① Cylinder ② **Size=20**（与法术半径相等，免缩放）③ Fx 非 null ④ 语义是石/结晶"
    /// 四条筛（全库共 77 个 Cylinder+Size=20+Fx 的区域，逐个看语义后选出）：
    ///   · `ObsidianFlowArea`（黑曜流，Cylinder/20，Fx=c2c0a166e12e9924…）——**首选**：
    ///     一整片**黑曜石碎壳**，是"地面结壳"的形状（不是云、不是泥），染成苍白色后正好是"钙化"；
    ///   · `DeadlyEarthMetalBlastArea`（金属·致命大地，Cylinder/20）——金属碎片，银色，备选；
    ///   · `SpikeStonesArea`（尖石，Cylinder/**40**）——名字最像，但**半径 40，要 scale=0.5**；
    ///   · `CaveFangsStalagmitesArea`（石笋，Cylinder/**5**）——石笋形状最"石化"，但**半径 5，要 scale=4**。
    ///
    /// 换观感只改这一行；**换了尺寸不同的候选，记得同步改 `SpellsSetup` 里 `AreaGroundFx.Register` 的 scale**。
    /// 借不到返回 null（区域没有地面特效，功能完全不受影响，只打日志）。
    /// </summary>
    internal static PrefabLink CalcificationGround
    {
      get
      {
        if (Cache.TryGetValue("calcificationground", out var cached)) return cached;
        var link = FxBorrow.AreaFx(
          AbilityAreaEffectRefs.ObsidianFlowArea.ToString());
        Cache["calcificationground"] = link;
        return link;
      }
    }

    /// <summary>
    /// **冰封领域**（环 5·30 尺地面区域）的地面素材：原版**冰霜/寒冷**区域的常驻 `Fx`。
    ///
    /// 选材（离线查 `blueprints.zip`，按 §7.11.1 四条筛）：`Shape=Cylinder` + `Size=30`（= 法术半径，
    /// 免缩放）+ `Fx` 非 null + **语义是"冰"**。Size=30 的候选有 76 个，逐个看语义后选出：
    ///   · `AuraofColdAreaEffect`（寒冷灵光，Cylinder/30）——**首选**：它的 `Fx`
    ///     （`5413cb18d2272ed47a6b18c3af2d79e0`）**同时被 `DLC5_HillGiantBoss_FrostBarrier_AreaEffect`
    ///     （霜障）引用**，等于原版盖章"这是冰霜素材"（离线反查同一 Fx 的引用方得到，见 dev-notes §7.11.1）；
    ///   · `PolarMidnightArea`（极夜，Cylinder/30）——9 环寒冷法术的区域，备选；
    ///   · `AuraofColdAreaSithhudEffect`（锡斯胡德寒冷灵光，Cylinder/30）——与首选同素材，兜底。
    /// 三个候选**尺寸都是 30**，所以 `SpellsSetup` 里 `AreaGroundFx.Register` 的 `scale=1` 对任一候选都成立。
    /// 借不到返回 null（区域没有地面特效，功能完全不受影响，只打日志）。
    /// </summary>
    internal static PrefabLink FrozenDomainGround
    {
      get
      {
        if (Cache.TryGetValue("frozendomground", out var cached)) return cached;
        var link = FxBorrow.AreaFx(
          AbilityAreaEffectRefs.AuraofColdAreaEffect.ToString(),
          AbilityAreaEffectRefs.PolarMidnightArea.ToString(),
          AbilityAreaEffectRefs.AuraofColdAreaSithhudEffect.ToString());
        Cache["frozendomground"] = link;
        return link;
      }
    }

    /// <summary>
    /// **儿时的舞乐**（环 6·40 尺地面区域）的地面素材：原版**影响心灵/幻雾**区域的常驻 `Fx`。
    ///
    /// 选材：40 尺的 Cylinder 候选里**没有**精神系（只有纠缠/冰暴/火山/根雾），所以退回 **Size=20 的精神系**
    /// 并靠 `scale=2` 放大到 40 尺（`AreaGroundFx` 的"通用档"会把粒子设成 Hierarchy 缩放模式，让 scale 生效）。
    ///   · `MindFogArea`（心灵迷雾，Cylinder/20）——**首选**：`Fx`（`597682efc0419a142a3174fd6bb408f7`）
    ///     同时被 `CharmFogArea`（魅惑之雾）与诺提库拉的"心灵迷雾"引用，是"精神雾"的通用素材；
    ///   · `TricksterHallucinogenicCloudArea`（诡术师·致幻云，Cylinder/20）——致幻＝精神，备选。
    /// ⚠️ 两个候选尺寸都是 20，`scale=2` 对任一候选都成立。
    /// ⚠️ **注意与烟幕掩护区分**：烟幕借的是恶臭云（`StinkingCloudArea` 的同一份绿毒雾 `ee38b41b…`），
    /// 与本素材（`597682ef…`）**不是同一份**，所以两者不会撞脸；但仍要在颜色上拉开（见 SpellsSetup 的登记色）。
    /// 借不到返回 null（区域没有地面特效，功能完全不受影响，只打日志）。
    /// </summary>
    internal static PrefabLink FoxfireGround
    {
      get
      {
        if (Cache.TryGetValue("foxfireground", out var cached)) return cached;
        var link = FxBorrow.AreaFx(
          AbilityAreaEffectRefs.MindFogArea.ToString(),
          AbilityAreaEffectRefs.TricksterHallucinogenicCloudArea.ToString());
        Cache["foxfireground"] = link;
        return link;
      }
    }

    /// <summary>
    /// **天灾降临**（环 10·80 尺地面区域）的地面素材：原版**火山风暴**区域的常驻 `Fx`。
    ///
    /// 选材：全库 `Cylinder + Fx` 的区域里**没有 `Size=80`**（尺寸分布到 60 就断了），所以取
    /// **`Size=40` 的复合灾害素材 × `scale=2`** 放大到 80 尺：
    ///   · `VolcanicStormArea`（火山风暴，Cylinder/40）——**首选**：原版 4 环"火山风暴"的区域，
    ///     语义就是"火 + 轰击"的复合灾害，与天灾降临（12d6 火焰 + 12d6 轰击 + 12d6 电击）最贴；
    ///     其 `Fx`（`bc74fab50e8a78645abe18137c04b29c`）**只被这一个蓝图引用**（离线反查确认）。
    /// ⚠️ 80 尺是很大的范围，靠 `scale=2` 把 40 尺素材撑到 80 尺；`AreaGroundFx` 的通用档会把粒子
    /// 设成 Hierarchy 缩放模式让 scale 生效。若进游戏发现放大后粒子发虚/稀疏，再考虑换更大的素材或调色。
    /// 借不到返回 null（区域没有地面特效，功能完全不受影响，只打日志）。
    /// </summary>
    internal static PrefabLink CatastropheGround
    {
      get
      {
        if (Cache.TryGetValue("catastropheground", out var cached)) return cached;
        var link = FxBorrow.AreaFx(
          AbilityAreaEffectRefs.VolcanicStormArea.ToString());
        Cache["catastropheground"] = link;
        return link;
      }
    }

    /// <summary>
    /// **坚守阵地**（环 1·半径 30 的友军区域）的地面**圈**素材：原版**吟游诗人·英勇激励**（`InspireHeroicsArea`）
    /// 的区域常驻 `Fx`——它本身就是"**半径 30 的友军增益光环圈**"，与法术半径**完全相等（免缩放）**。
    /// 作者 10-11：要"**和法术范围相当大小的圈特效**"（原来借天使罩子、放大 4 倍，太浮夸）。
    /// 备选：`InspireGreatnessArea`（英勇壮举）、`SunDomainGreaterAura`（太阳领域灵光）。
    /// </summary>
    internal static PrefabLink HoldTheLineGround
    {
      get
      {
        if (Cache.TryGetValue("holdthelineground", out var cached)) return cached;
        var link = FxBorrow.AreaFx(
          AbilityAreaEffectRefs.InspireHeroicsArea.ToString(),
          AbilityAreaEffectRefs.InspireGreatnessArea.ToString(),
          AbilityAreaEffectRefs.SunDomainGreaterAura.ToString());
        Cache["holdthelineground"] = link;
        return link;
      }
    }

    /// <summary>
    /// **影之疆域**（环 8·30 尺**移动**领域）的地面素材：原版**黑暗/夜**区域的常驻 `Fx`。
    ///
    /// 选材：`Cylinder` + **`Size=30`**（= 法术半径，**免缩放**）+ `Fx` 非 null + 语义偏"暗"：
    ///   · `PolarMidnightArea`（极夜，Cylinder/30）——**首选**：原版 9 环"极夜"的区域，
    ///     语义就是"黑暗"（另带寒冷），半径 30 与法术相等；重染成暗紫后即"影之疆域"的地面；
    ///   · `HellKnightOrderOfTheGodclawTier1Area`（神爪骑士团灵光，Cylinder/30）——同为 30 尺，备选。
    /// ⚠️ `PolarMidnightArea` 同时也是冰封领域的备选素材——两边**各自克隆**，互不影响，原版也不受影响。
    /// 借不到返回 null（区域没有地面特效，功能完全不受影响，只打日志）。
    /// </summary>
    internal static PrefabLink ShadowRealmGround
    {
      get
      {
        if (Cache.TryGetValue("shadowrealmground", out var cached)) return cached;
        var link = FxBorrow.AreaFx(
          AbilityAreaEffectRefs.PolarMidnightArea.ToString(),
          AbilityAreaEffectRefs.HellKnightOrderOfTheGodclawTier1Area.ToString());
        Cache["shadowrealmground"] = link;
        return link;
      }
    }

    /// <summary>
    /// 精神 / 影响心灵：心灵迷雾没有 AbilitySpawnFx（实测借不到，醉梦完全没特效）。
    /// 改用迷梦 / 弱智术 / 梦之斗篷这类影响心灵法术，并以心灵迷雾的区域 FX 兜底。
    /// </summary>
    internal static PrefabLink Psychic => Burst("psychic", new[]
    {
      AbilityRefs.HazeOfDreams.ToString(),
      AbilityRefs.Feeblemind.ToString(),
      AbilityRefs.CloakofDreams.ToString(),
      AbilityRefs.MindFog.ToString(),
    }, new[]
    {
      AbilityAreaEffectRefs.MindFogArea.ToString(),
    });

    /// <summary>
    /// 暗影 / 死灵：**幻影杀手**（PhantasmalKiller）有 `AbilitySpawnFx`（`c44b1679…`，锚在目标）——离线核实可用
    /// （见 `dev-notes.md` §7.11.1 的筛法）。第二个候选"幽影塑能"没有 `AbilitySpawnFx`，留作无害兜底。
    /// 用于王冠的回响（夺走敌人增益）。
    /// </summary>
    internal static PrefabLink Shadow => Burst("shadow", new[]
    {
      AbilityRefs.PhantasmalKiller.ToString(),
      AbilityRefs.ShadowEvocation.ToString(),
    });

    /// <summary>
    /// 死亡 / 即死（死灵）：**指骨术**（`FingerOfDeath`）的 `AbilitySpawnFx`
    /// （`e8569f44…`，锚 SelectedTarget）——原版最经典的"死灵即死"爆发。用于**影霄·拔刀**（9 环，强韧失败即死）。
    /// 备选：`SlayLiving`（5 环，"杀死活物"，`cc9120c1…`，锚 ClickedTarget）。
    /// （2026-10-10 离线扫全库 death/slay/finger 类法术的 `AbilitySpawnFx` 后选出，见 dev-notes §7.11.1 筛法。）
    /// </summary>
    internal static PrefabLink Death => Burst("death", new[]
    {
      AbilityRefs.FingerOfDeath.ToString(),
      AbilityRefs.SlayLiving.ToString(),
    });

    /// <summary>
    /// 力场 / 无元素冲击：**魔法飞弹 / 轰击术都没有 `AbilitySpawnFx`**（实测借不到，原候选作废）。
    /// 改用 **力场拳（`ForcePunchEffect`）**——它的 `AbilitySpawnFx`（`01d23d06…`）锚在**目标**上，
    /// 是"力场冲击"的观感，正好给牵引这种"把敌人拽过来"的法术用；备选强迫悔悟。
    /// （2026-10-10 离线扫描全库"force / 拖拽"类法术的 `AbilitySpawnFx` 后选出，见 `dev-notes.md` §7.11.1 的筛法。）
    /// </summary>
    internal static PrefabLink Force => Burst("force", new[]
    {
      AbilityRefs.ForcePunchEffect.ToString(),
      AbilityRefs.ForcedRepentance.ToString(),
    });

    // ── 单体批次（0 环起）：治疗 / 传送 / 标记 / 预言 四个新元素 ─────────────────────
    //
    // 单体的 0 环指令是"接触治疗 / 接触传送 / 标记敌人 / 自身增益"这类，元素表里原本没有
    // 对应通道。候选一律离线查 `blueprints.zip` 的 `AbilitySpawnFx`（见 dev-notes §7.11.1 筛法），
    // 借到的 PrefabLink 仍是运行时从原版蓝图读的，不写死 AssetId。

    /// <summary>
    /// 治疗：**治疗轻伤**（`CureLightWounds`）的 `AbilitySpawnFx`（`61602c5b…`，锚目标）——
    /// 原版治疗系法术的通用金色治疗光（治疗中伤/重伤、医疗领域、自发治疗都引这一份）。
    /// 用于医疗敷料（0 环接触治疗）；高环的治疗/复活类单体也复用它。
    /// 备选：`Heal`（6 环，`319f507857ae`）、`CureModerateWounds`。
    /// </summary>
    internal static PrefabLink Heal => Burst("heal", new[]
    {
      AbilityRefs.CureLightWounds.ToString(),
      AbilityRefs.Heal.ToString(),
      AbilityRefs.CureModerateWounds.ToString(),
    });

    // ── 2026-10-16：医疗敷料 / 紧急救治 / Mon3tr 不再共用"治疗轻伤"的牧师绿光 ──
    // 作者：三个技能全是同一份治疗轻伤绿光，Mon3tr 尤其不对（应是凯尔希的黑色神经结晶）。
    // 各自换成**不同**的原版素材（离线扫 blueprints.zip 的 AbilitySpawnFx 选出，运行时仍从蓝图读）。

    /// <summary>
    /// 医疗敷料（0 环接触包扎）：**次级复原术**（`RestorationLesser`）的 `AbilitySpawnFx`
    /// （`621885df…`，锚 SelectedTarget）——复原/清理伤势的光，不是牧师的绿色治疗光。
    /// 备选：`Restoration`（同一份素材）。
    /// </summary>
    internal static PrefabLink FieldDressingFx => Burst("fielddressing", new[]
    {
      "e84fc922ccf952943b5240293669b171", // Spells/Level2/RestorationLesser
      "f2115ac1148256b4ba20788f7e966830", // Spells/Level4/Restoration
    });

    /// <summary>
    /// 紧急救治（1 环·闪灵）：**圣疗·他人**（`LayOnHandsOthers`）的 `AbilitySpawnFx`
    /// （`224fb8fd…`，锚 SelectedTarget）——法术本身就是"获得博士圣疗的治疗效果"，语义最贴。
    /// ⚠ 作者 10-16：上一版借的**生命气息**（`ee0c5b93…`）与**复活术 / 死者复活**是同一份特效，已弃用。
    /// 备选：`LayOnHandsSelf`（同一份素材）。
    /// </summary>
    internal static PrefabLink EmergencyTreatmentFx => Burst("emergencytreatment", new[]
    {
      "caae1dc6fcf7b37408686971ee27db13", // Classes/Paladin/LayOnHands/LayOnHandsOthers
      "8d6073201e5395d458b8251386d72df1", // Classes/Paladin/LayOnHands/LayOnHandsSelf
    });

    /// <summary>
    /// Mon3tr 的**治疗光**（叠在黑色结晶爆开之上，免得像伤害法术）：**治疗中伤**（`CureModerateWounds`）的
    /// `AbilitySpawnFx`（`e9399b6d…`，锚 SelectedTarget）——作者 10-16 指定。与治疗轻伤（`61602c5b…`）不是同一份。
    /// </summary>
    internal static PrefabLink Mon3trHeal => Burst("mon3trheal", new[]
    {
      AbilityRefs.CureModerateWounds.ToString(),
    });

    /// <summary>
    /// Mon3tr（5 环·凯尔希）：**先知启示·碎片爆发**（`OracleRevelationShardExplosionAbility`）的
    /// `AbilitySpawnFx`（`184fcfe5…`）——一团**晶片爆开**，由 SpellsSetup 克隆后重染成暗墨绿黑，
    /// 当作 Mon3tr 的黑色神经结晶。（原版素材只锚施法者，这里由动作摆到目标身上。）
    /// </summary>
    internal static PrefabLink Mon3trCrystal => Burst("mon3trcrystal", new[]
    {
      "378d4f5fe60457c49b5e110ff0f8e2cb", // Classes/Oracle/OracleRevelations/OracleRevelationShardExplosionAbility
    });

    /// <summary>
    /// 传送：**任意门本身没有 `AbilitySpawnFx`**（离线核实为空），改借**仪式·任意门**
    /// （`RitualDimensionDoorAbility`）的 `AbilitySpawnFx`（`358e9c4b…`，锚目标）——它才带传送闪光。
    /// 用于紧急撤离（0 环接触传送）；后续位移类单体（迅捷突破等）也可复用。
    /// 备选：`DimensionDoor` / `DarkLurkerDimensionDoor`（两者都可能为空，留作无害兜底）。
    /// </summary>
    internal static PrefabLink Teleport => Burst("teleport", new[]
    {
      AbilityRefs.RitualDimensionDoorAbility.ToString(),
      AbilityRefs.DimensionDoor.ToString(),
      AbilityRefs.DarkLurkerDimensionDoor.ToString(),
    });

    /// <summary>
    /// 标记 / 目标指示：**天界印记**（`ArmyMarkOfHeaven`）的 `AbilitySpawnFx`（`45622cc6…`，锚目标）——
    /// 语义就是"给目标盖一个印记"，正好贴"战术标记"（我方对其下一次攻击 +1）。
    /// 用于战术标记（0 环）；后续"给敌人做记号"的减益（火力侦察 / 罪罚等）也可复用。
    /// </summary>
    internal static PrefabLink Mark => Burst("mark", new[]
    {
      AbilityRefs.ArmyMarkOfHeaven.ToString(),
    });

    /// <summary>
    /// 预言 / 自身增益：**真实打击**（`TrueStrike`）的 `AbilitySpawnFx`（`8de64fbe…`，**锚施法者**）——
    /// 一个施法者身上的预言系闪光，适合"自身预言增益"这类法术。
    /// 用于战场测绘（0 环自身预言）。备选：`SeeInvisibility`（锚目标）。
    /// </summary>
    internal static PrefabLink Divination => Burst("divination", new[]
    {
      AbilityRefs.TrueStrike.ToString(),
      AbilityRefs.SeeInvisibility.ToString(),
    });

    // ── 单体批次（2 环起）：增益 / 缓慢 / 禁魔 三个新元素 ─────────────────────────────
    //
    // 2 环的三个单体指令元素表里没有对应通道，按"语义借原版同类法术"补（见 fx-status.md 第三节）。
    // 候选一律离线查 `blueprints.zip` 的 `AbilitySpawnFx`（dev-notes §7.11.1 筛法），
    // 借到的 PrefabLink 仍是运行时从原版蓝图读的，不写死 AssetId。

    /// <summary>
    /// 增益施法（给友军的"战力增益"）：**英雄气概**（`Heroism`）的 `AbilitySpawnFx`
    /// （`44295e97…`，锚目标）——原版经典"+2 攻击 / 豁免 / 技能"增益的施法闪光。
    /// 用于**不稳定血浆**（死灵；攻击 / 伤害 +2，代价每轮失血）——同为"给一名友军的战力增益"。
    /// 备选：`GoodHope`（3 环群体版，同款金光）、`DivineFavor`（同系但锚施法者、只给自己）。
    /// </summary>
    internal static PrefabLink Heroism => Burst("heroism", new[]
    {
      AbilityRefs.Heroism.ToString(),
      AbilityRefs.GoodHope.ToString(),
    });

    /// <summary>
    /// 缓慢施法（"目标陷入缓慢，如同缓慢术"）：**缓慢术**（`Slow`）的 `AbilitySpawnFx`
    /// （`b38bf386…`，锚目标）——原版缓慢术的施法闪光。用于**反重力**。
    /// ⚠ 离线核实：该 AssetId 与 **`HazeOfDreams`（迷梦）共用同一份**，而迷梦正是 `Psychic`
    /// 元素的首选——所以反重力这一发在画面上与**醉梦 / 牵制射击**同款。因语义完全对得上
    ///（"如同缓慢术"）故采用；若要区分，可把反重力改成走 `SlowAura`（持续光环，见 `DebuffAuraFor`）。
    /// </summary>
    internal static PrefabLink SlowCast => Burst("slowcast", new[]
    {
      AbilityRefs.Slow.ToString(),
    });

    /// <summary>
    /// 禁魔 / 防护（禁止目标施法）：**解除魔法**（`DispelMagicTarget`）的 `AbilitySpawnFx`
    /// （`3eda0e7f…`，锚目标）——原版防护系"拆解魔法"的闪光。
    /// 原版**没有"沉默术"**这种法术（离线全库搜 silence / mute 只有 DLC6 假面魔像的专用 buff），
    /// 因此退到同为**防护系、直接作用于魔法**的解除魔法。用于**缄默**（防护；目标不能施法 / 类法术 / 超自然能力）。
    /// 备选：`DispelMagicPoint`（同一份素材）。
    /// </summary>
    internal static PrefabLink Dispel => Burst("dispel", new[]
    {
      AbilityRefs.DispelMagicTarget.ToString(),
      AbilityRefs.DispelMagicPoint.ToString(),
    });

    // ── 单体批次（3–5 环）：处决 / 梦魇 / 迅捷突破 / 铸铁 / 墨染山河 五个新元素 ─────────────
    //
    // 3–5 环的单体指令同样按"语义借原版同类法术"补（见 fx-status.md 第三节）。
    // 候选一律离线查 `blueprints.zip` 的 `AbilitySpawnFx`（dev-notes §7.11.1 筛法），
    // 借到的 PrefabLink 仍是运行时从原版蓝图读的，不写死 AssetId。
    // （奇美拉复用 `Heroism`、Mon3tr 复用 `Heal`，不另立元素。）

    /// <summary>
    /// 处决（蓄势 / 致命一击）：**刺客·公开处决**（`AssassinPublicExecutionAbility`）的
    /// `AbilitySpawnFx`（`d80d51c0…`，**锚施法者**）——原版"处决"主题的施法爆发，锚点正合
    /// 处决的 Personal 距离。用于**处决**（3 环）。
    /// 备选：`ExecutionerAssassinateDemoralizeAction`（刽子手·刺杀威吓，同一份素材）。
    /// </summary>
    internal static PrefabLink Execution => Burst("execution", new[]
    {
      AbilityRefs.AssassinPublicExecutionAbility.ToString(),
      AbilityRefs.ExecutionerAssassinateDemoralizeAction.ToString(),
    });

    /// <summary>
    /// 恐惧（使目标陷入恐慌 / 动摇）：**造成恐惧**（`CauseFear`）的 `AbilitySpawnFx`
    /// （`cbfe312c…`，锚目标）——原版恐惧系法术的经典爆发。用于**梦魇**（4 环）。
    /// 备选：`RitualCauseFearAbility`（`226a158b…`，锚落点）。
    /// </summary>
    internal static PrefabLink Fear => Burst("fear", new[]
    {
      AbilityRefs.CauseFear.ToString(),
      AbilityRefs.RitualCauseFearAbility.ToString(),
    });

    /// <summary>
    /// 加速（自身加速、如同加速术）：**加速术**（`Haste`）的 `AbilitySpawnFx`
    /// （`3cf0849b…`）——原版加速术的施法闪光（`HastedAssaultAbility` 也用它、锚施法者）。
    /// 用于**迅捷突破**（4 环，Personal，锚施法者）。
    /// </summary>
    internal static PrefabLink Haste => Burst("haste", new[]
    {
      AbilityRefs.Haste.ToString(),
    });

    /// <summary>
    /// 防护 / 护甲（给友军的防护增益，如伤害减免）：**法师护甲**（`MageArmor`）的 `AbilitySpawnFx`
    /// （`f447e243…`，锚目标）——原版最经典的"给一名友军套护甲"防护闪光。用于**铸铁**（5 环，DR 10/—）。
    /// 备选：`ProtectionFromArrows`（`c30bef03…`）、`ProtectionFromEvil`（`5c83a725…`）。
    /// </summary>
    internal static PrefabLink Protection => Burst("protection", new[]
    {
      AbilityRefs.MageArmor.ToString(),
      AbilityRefs.ProtectionFromArrows.ToString(),
      AbilityRefs.ProtectionFromEvil.ToString(),
    });

    /// <summary>
    /// 召唤 / 创造（在落点召唤造物）：**钩锁与召唤之秘**（`SecretOfHookingAndSummoning`）的
    /// `AbilitySpawnFx`（`58f7f63f…`，锚施法者）——原版"召唤"主题的法术闪光。用于**墨染山河**
    /// （5 环，召唤两只墨灵）。
    /// ⚠ 原版**召唤怪物系（SummonMonster）全部没有 `AbilitySpawnFx`**（离线核实为空），
    ///   所以退到同为召唤主题的 9 环法术。
    /// </summary>
    internal static PrefabLink Summon => Burst("summon", new[]
    {
      AbilityRefs.SecretOfHookingAndSummoning.ToString(),
    });

    /// <summary>
    /// 剑雨的**拖尾**素材：天使神话 4 级"阳炎之刃"的投射物外观（金色光弧）。
    /// 用户看过之后明确要留着它（"把拖尾加回来"）——它负责"发光/华丽"，剑身网格负责"像剑"。
    /// 注意：投射物的 View 是**飞行拖尾**，不是物件形状，所以不要拿它当"剑"用。
    /// </summary>
    internal static PrefabLink SwordRainTrail
    {
      get
      {
        if (Cache.TryGetValue("swordraintrail", out var cached)) return cached;
        var link = FxBorrow.ProjectileViewFx(
          ProjectileRefs.Mythic4lvlAngel_BladeOfTheSun00.ToString());
        Cache["swordraintrail"] = link;
        return link;
      }
    }

    /// <summary>
    /// 剑雨的"剑"素材：**原版武器模型**（巨剑的剑身网格），一把一个物件——这才真的"看起来像剑"。
    ///
    /// 三次翻车的记录，别回头：
    ///   ① 刃障 `BladeBarrierArea`（`Shape=Wall, Size=60`）与锡斯胡德 `DLC5_SwordOfSithhudAreaEffect`
    ///      （`Wall, Size=180`）都是**直线墙**，拼不成圆，怎么缩放都是"一条"。
    ///   ② 改用投射物外观（阳炎之刃）虽然成了圆，但借到的是**飞行拖尾**（金色光弧），形状不像剑。
    ///   ③ 用武器模型后剑的形状对了，但竖直的剑用 `FitToMeters`（只量水平面）去缩放，只量到剑身厚度，
    ///      整把剑被放大好几倍——竖直物件必须用 `FitLongestMeters`。
    /// 借法走 <see cref="FxBorrow.WeaponModelFx"/>（反射读 `WeaponVisualParameters.m_WeaponModel`），
    /// AssetId 运行时从原版蓝图读，不写死。
    /// </summary>
    internal static PrefabLink SwordRainBlade
    {
      get
      {
        if (Cache.TryGetValue("swordrainblade", out var cached)) return cached;
        var link = FxBorrow.WeaponModelFx(
          // 巨剑最大最显眼，其次长剑 / 重剑
          WeaponTypeRefs.Greatsword.ToString(),
          WeaponTypeRefs.Longsword.ToString(),
          WeaponTypeRefs.BastardSword.ToString(),
          WeaponTypeRefs.Falchion.ToString());
        Cache["swordrainblade"] = link;
        return link;
      }
    }

    /// <summary>
    /// 剑雨的**兜底**素材：一个圆形地面符文（牧师"符文领域"基础能力的区域 FX）。
    /// 该区域是 <c>Shape=Cylinder, Size=5</c>（半径 5 尺 → 直径 10 尺），所以它天生就是圆的；
    /// 要覆盖半径 10 尺的爆发就把 scale 设成 2。
    /// 选它当兜底是因为它**已在本 mod 里实测能上屏**（延时爆破的地面标记 `BombFx` 用的就是它）。
    /// </summary>
    internal static PrefabLink SwordRainRune
    {
      get
      {
        if (Cache.TryGetValue("swordrainrune", out var cached)) return cached;
        var link = FxBorrow.AreaFx(
          AbilityAreaEffectRefs.RuneDomainBaseAbilityFireArea.ToString(),
          AbilityAreaEffectRefs.RuneDomainGreaterArea.ToString());
        Cache["swordrainrune"] = link;
        return link;
      }
    }

    /// <summary>
    /// 成片铺地的剑阵（= 刃障的区域常驻 FX）。与 <see cref="SwordWall"/> 取的是同一个素材，
    /// 只是语义命名不同；当前无调用方，留作"C 类地面区域"批次备用。
    /// </summary>
    internal static PrefabLink SwordField
    {
      get
      {
        if (Cache.TryGetValue("swordfield", out var cached)) return cached;
        var link = FxBorrow.AreaFx(
          AbilityAreaEffectRefs.BladeBarrierArea.ToString(),
          AbilityAreaEffectRefs.DLC5_SwordOfSithhudAreaEffect.ToString());
        Cache["swordfield"] = link;
        return link;
      }
    }

    /// <summary>
    /// 刃障的区域常驻 FX（<c>Shape=Wall, Size=60</c>，**一条 60 尺直线墙**）。
    /// 剑雨曾用它当"剑阵"，但墙拼不成圆，已改用 <see cref="SwordRainBlade"/> 摆环。
    /// 当前无调用方；留作 C 类"地面区域"批次参考（若要做**墙形**效果才用它）。
    /// </summary>
    internal static PrefabLink SwordWall
    {
      get
      {
        if (Cache.TryGetValue("swordwall", out var cached)) return cached;
        var link = FxBorrow.AreaFx(
          AbilityAreaEffectRefs.BladeBarrierArea.ToString(),
          AbilityAreaEffectRefs.DLC5_SwordOfSithhudAreaEffect.ToString());
        Cache["swordwall"] = link;
        return link;
      }
    }

    /// <summary>
    /// 单把剑（落剑）：给每个中招敌人头顶落一把。当前**无调用方**，且**借不到任何素材**：
    /// 原生候选全是空的——AngelSwordEffectBuff / AngelSwordEffectEffectBuff / AnimatedWeaponEnchantmentBuff
    /// 的 FxOnStart 都是 null（第三个还标错了，它是"活化武器附魔"增益、不是活化武器的视觉），
    /// DarkLurkerBladeFromShadowsTargetArea / BladeboundIndependentEgoArea 的 Fx 也都是 null。
    /// 也就是说本属性必定返回 null。若将来要做"落剑"版剑雨，必须重新找有 FX 的单剑素材
    /// （候选方向：AbilityRefs.AngelBladeOfTheSun / AbilityRefs.HolySword 这类能挂 AbilitySpawnFx 的法术）。
    /// </summary>
    internal static PrefabLink SwordDrop
    {
      get
      {
        if (Cache.TryGetValue("sworddrop", out var cached)) return cached;
        var link = FxBorrow.BuffAura(
            // 天使之剑（这两个 Buff 实测 FxOnStart 为 null）
            BuffRefs.AngelSwordEffectBuff.ToString(),
            BuffRefs.AngelSwordEffectEffectBuff.ToString())
          ?? FxBorrow.AreaFx(
            AbilityAreaEffectRefs.DarkLurkerBladeFromShadowsTargetArea.ToString(),
            AbilityAreaEffectRefs.BladeboundIndependentEgoArea.ToString());
        Cache["sworddrop"] = link;
        return link;
      }
    }

    // ── 信标投射：一根竖直光柱（借原版"生命之柱"）+ 照明术的周围光 ─────────────────
    //
    // 信标原先借"天使之貌"的 FxOnStart 当光（画面上什么都没有）、借"发光石"当物体
    // （一块实心蓝石头，用户不满意）。自绘光柱（BeaconPillarFx）用户也不满意，要求**复用原版素材**。
    // 现在物体改为**借用原版 5 环法术"生命之柱"（PillarOfLife）区域的常驻 Fx**——名字就是"柱"，
    // 很可能是一根竖直光柱，由 BeaconFx 克隆重染成蓝白色、放到区域中心、跟区域同生同灭：
    //   · 物体：借 PillarOfLifeaArea 的 Fx（原版区域常驻光柱），FxTint 克隆 + 重染成蓝白；
    //   · 周围光：照明术（MageLight）增益的常驻 FxOnStart——挂在区域上（area.Fx）。

    /// <summary>
    /// 信标投射的**周围光**：照明术（MageLight）增益的常驻 `FxOnStart`。
    /// 挂在信标区域上（<c>beaconArea.Fx</c>），跟区域同生同灭。借不到返回 null（区域无光，功能不受影响）。
    /// </summary>
    internal static PrefabLink BeaconGlow
    {
      get
      {
        if (Cache.TryGetValue("beaconglow", out var cached)) return cached;
        var link = FxBorrow.BuffAura(BuffRefs.MageLightBuff.ToString());
        Cache["beaconglow"] = link;
        return link;
      }
    }

    /// <summary>
    /// **医疗无人机**的常驻光：原版**光亮术**（`Spells/Level0/MageLight`）增益 `MageLightBuff` 的
    /// `FxOnStart`（`72938ec0a6e4…`）——一盏小光点（与信标光柱同一份素材）。
    ///
    /// ⚠ **已废弃，不要再拿它做"跟着单位的常驻光"**（2026-10-10，见 dev-notes §7.10.5）：
    /// 光亮术的 `FxOnStart` 其实是**施法瞬间的星光爆发**（bursts=1、常驻存活=0），
    /// 且素材里的 `Light.intensity` 默认为 0（由脚本点亮）——挂到单位身上"放完就没"、什么都看不见。
    /// 医疗无人机已改为借**"给单位挂视觉"的增益**（见 <see cref="MedicalDroneAuraCandidates"/>）。
    /// 保留此属性仅供将来参考。
    /// </summary>
    internal static PrefabLink MedicalDroneGlow
    {
      get
      {
        if (Cache.TryGetValue("medicaldroneglow", out var cached)) return cached;
        var link = FxBorrow.BuffAura(BuffRefs.MageLightBuff.ToString());
        Cache["medicaldroneglow"] = link;
        return link;
      }
    }

    // ── 医疗无人机：借"给单位挂视觉"的原版增益（常驻、长在单位身上）────────────────────
    //
    // 走过的路见 dev-notes §7.10.5：① 借光亮术的 FxOnStart → 是施法爆发 + 黑灯，看不见；
    // ② 自建球网格 + `Shader.Find` 材质 + 自建点光 → 还是看不见（`Shader.Find` 那条路本 mod 翻车三次）；
    // ③ 借"给单位挂视觉"的原版增益（提灯 / 治疗审判）→ 日志说"已生成"、画面仍看不见
    //    （那些增益的 `FxOnStart` 也是**施法爆发**）。
    //
    // ⇒ **作者 10-10 定案：借一个"物体模型"当机体，放头顶跟随 + 每次治疗闪一下治疗光。**
    //    机体见 <see cref="MedicalDroneBody"/>（借原版**武器模型网格**），由 `MedicalDroneFx` 实例化、
    //    缩小、挂到单位 `View` 下（机制同 `HasteTrailFx`）；治疗闪光在增益的每轮动作里挂
    //    `ContextActionSpawnFx`（素材 = <see cref="Heal"/>）。
    //
    // 下面这套"借增益 FxOnStart"的候选**当前无调用方**（留档：真要用得先确认它的 FxOnStart 是常驻的）。

    /// <summary>
    /// 医疗无人机的**机体**素材：借原版**武器模型**（网格），做成"头顶悬浮的小物件"。
    /// 首选 **尖刺轻盾**（`SpikedLightShield`）——一面**圆形小盾**，正面看就是个"圆盘机体"，最像无人机；
    /// 备选 星刃 `Starknife`（四角星）、轻锤 `LightHammer`。
    /// 借法走 <see cref="FxBorrow.WeaponModelFx"/>（反射读 `WeaponVisualParameters.m_WeaponModel`，不写死 AssetId）。
    /// </summary>
    internal static PrefabLink MedicalDroneBody
    {
      get
      {
        if (Cache.TryGetValue("medicaldronebody", out var cached)) return cached;
        var link = FxBorrow.WeaponModelFx(
          WeaponTypeRefs.SpikedLightShield.ToString(), // 圆盘小盾（最像机体）
          WeaponTypeRefs.Starknife.ToString(),         // 四角星刃
          WeaponTypeRefs.LightHammer.ToString());      // 轻锤
        Cache["medicaldronebody"] = link;
        return link;
      }
    }


    /// <summary>
    /// 医疗无人机的素材候选（原版增益 GUID）。**按顺序取第一个借到 `FxOnStart` 的**。
    /// 顺序 = 优先级：提灯（跟身发光小物件）→ 治疗审判（治疗主题灵光）→ 神盾之眼（悬浮眼球）
    /// → 巫妖魂迹（漂浮灵魂）→ 天使光环（金色，D 类同款，兜底）。
    /// 换观感只改这个数组（顺序 / 元素）。离线加候选：`tools/scan_buff_fx_all.py <关键字>`。
    /// </summary>
    internal static readonly string[] MedicalDroneAuraCandidates =
    {
      BuffRefs.HealingJudgmentBuff.ToString(),                   // 治疗审判：治疗主题的常驻灵光（作者 2026-10-10 选定）
      BuffRefs.AeonEyeEnemyVisualBuff.ToString(),                // 神盾之眼：悬浮眼球
      BuffRefs.LichSoulsTrailBuff.ToString(),                    // 巫妖魂迹：漂浮灵魂
      BuffRefs.DLC5_ShadowPlaneBeltLanternVisualBuff.ToString(), // 提灯：跟身发光小物件（作者否掉，留作备选）
      BuffRefs.AngelHaloBuff.ToString(),                         // 天使光环：金色光环（兜底）
    };

    /// <summary>
    /// 医疗无人机的素材：按 <see cref="MedicalDroneAuraCandidates"/> 顺序取**第一个借到 `FxOnStart`** 的。
    /// 借不到返回 null（只打日志，功能不受影响）。日志里会写明是哪个候选借到的（`特效借用成功（增益）：<guid>`）。
    /// </summary>
    internal static PrefabLink MedicalDroneAura
    {
      get
      {
        if (Cache.TryGetValue("medicaldroneaura", out var cached)) return cached;
        PrefabLink link = null;
        foreach (var guid in MedicalDroneAuraCandidates)
        {
          link = FxBorrow.BuffAura(guid);
          if (link != null) break;
        }
        Cache["medicaldroneaura"] = link;
        return link;
      }
    }

    /// <summary>
    /// 信标投射的**光柱**：原版 5 环法术"生命之柱"（PillarOfLife）区域的常驻 Fx。
    /// 名字就是"柱"，很可能是一根竖直光柱；借来后由 BeaconFx 克隆重染成蓝白色、调整大小。
    /// 借不到返回 null（BeaconFx 保持无光柱，功能不受影响）。
    /// </summary>
    internal static PrefabLink BeaconPillar
    {
      get
      {
        if (Cache.TryGetValue("beaconpillar", out var cached)) return cached;
        var link = FxBorrow.AreaFx(AbilityAreaEffectRefs.PillarOfLifeaArea.ToString());
        Cache["beaconpillar"] = link;
        return link;
      }
    }

    /// <summary>
    /// 骑兵突进的**拖尾**素材：原版"加速术"（Haste）增益的 `FxOnStart`。
    /// 加速术在持有者身上会拖一条残影/光尾，正好契合"骑兵突进"的速度主题。
    /// 借来后由 <see cref="HasteTrailFx"/> 克隆重染成蓝色、挂到每个受益者身上（跟增益同生同灭）。
    /// 借不到返回 null（HasteTrailFx 保持无拖尾，功能不受影响）。
    /// </summary>
    internal static PrefabLink HasteTrail
    {
      get
      {
        if (Cache.TryGetValue("hastetrail", out var cached)) return cached;
        var link = FxBorrow.BuffAura(BuffRefs.HasteBuff.ToString());
        Cache["hastetrail"] = link;
        return link;
      }
    }

    // ── D 类：友军群体增益的"常驻光环" ───────────────────────────────────────────────
    //
    // 11 个友军增益法术原先完全没有视觉。做法：给**增益蓝图**挂 `FxOnStart`
    // （常驻循环光，只在持有者身上显示、增益消失即灭），素材借原版"天使·光环"增益的
    // `FxOnStart` —— 一圈金色光环，克制、正好是"金"，一个素材点亮整批。
    //
    // ⚠️ 借增益的 FxOnStart 时**必须挑一个确实非 null 的候选**：原版大量增益这个字段是空的
    //（`SwordDrop` 那组候选就全是 null，见上）。可用 `tools/scan_buff_fx_all.py` 离线扫
    // `blueprints.zip` 里所有 `BlueprintBuff && FxOnStart != null` 的蓝图（含 GUID），
    // 比进游戏一个个试快得多（见 docs/dev-notes.md §0 / §7.1）。
    //
    // 备选（同一次扫描出来的"金光/庇护"类，想换观感直接换这一行）：
    //   BuffRefs.AngelBarrier_Buff         天使屏障（金色罩子，比光环重）
    //   BuffRefs.AngelWingsBuff            天使之翼（很华丽，11 个法术一起挂会偏闹）
    //   BuffRefs.AngelWardFromHarmBuff     庇护（罩子）
    private static PrefabLink _auraGold;
    private static bool _auraTried;

    /// <summary>
    /// 按法术 key 取该法术的常驻光环素材；不属于 D 类（或借不到）返回 null，调用方保持"无光环"。
    /// 同一个素材给整批共用，所以只借一次（失败也记着，避免每个增益刷一条日志）。
    /// </summary>
    internal static PrefabLink AuraFor(string spellKey)
    {
      if (string.IsNullOrEmpty(spellKey) || !AuraSpells.Contains(spellKey)) return null;
      return AuraMaterial;
    }

    /// <summary>
    /// D 类"友军增益"的常驻光环素材本身（**不按法术 key 过滤**）——供"坚守阵地的**单个大范围特效**"等复用。
    /// ⚠ 作者 10-11：坚守阵地 / 过载模式 / 圣域 原来借的是"天使·光环"（`AngelHaloBuff`，一圈细环、且贴地），
    /// 太不显眼。现改用**天使屏障**（`AngelBarrier_Buff`，一个包裹身体的**金色罩子**，与奇美拉同一份素材）。
    /// 备选（换一行即可）：`AngelWingsBuff`（天使之翼）、`AngelWardFromHarmBuff`（庇护罩子）。
    /// </summary>
    internal static PrefabLink AuraMaterial
    {
      get
      {
        if (!_auraTried)
        {
          _auraTried = true;
          _auraGold = FxBorrow.BuffAura(BuffRefs.AngelBarrier_Buff.ToString())
            ?? FxBorrow.BuffAura(BuffRefs.AngelHaloBuff.ToString());
        }
        return _auraGold;
      }
    }

    /// <summary>
    /// D 类"天使·光环"素材本身（借 `AngelHaloBuff` 的 `FxOnStart`），供 <see cref="TintedAuraFx"/>
    /// 克隆后**重染**（奇美拉暗红 / 铸铁淡蓝）。与 <see cref="AuraFor"/> 共用同一份原版素材，
    /// 但**独立缓存**：AuraFor 只在 D 类法术查询时才加载，这里给"要改色的单体增益光环"用。
    /// </summary>
    internal static PrefabLink GoldHalo
    {
      get
      {
        if (!_goldHaloTried)
        {
          _goldHaloTried = true;
          _goldHalo = FxBorrow.BuffAura(BuffRefs.AngelHaloBuff.ToString());
        }
        return _goldHalo;
      }
    }
    private static PrefabLink _goldHalo;
    private static bool _goldHaloTried;

    /// <summary>带缓存的"增益常驻光"借用（借不到也缓存，避免反复报错）。</summary>
    private static PrefabLink BuffAuraCached(string key, params string[] buffGuids)
    {
      if (Cache.TryGetValue(key, out var cached)) return cached;
      var link = FxBorrow.BuffAura(buffGuids);
      Cache[key] = link;
      return link;
    }

    /// <summary>
    /// ⚠ **未使用**（保留备用）：原版**女巫·美味恐惧**（`WitchHexDeliciousFrightBuff`）的 `FxOnStart`。
    /// 2026-10-10 曾用于梦魇，但实测它其实是**地面圆环 + 底部烟雾**的魔法阵
    /// （粒子 BorderGlow / Border_Loop / Border_ThinActive / DarkBottomSmoke），贴在敌人身上像
    /// "给敌人放了个领域"、不像 debuff，故梦魇改走 <see cref="PlasmaAura"/>（身体型血雾）。
    /// 备选：死亡诅咒 `WitchHexDeathCurseBuff`。
    /// </summary>
    internal static PrefabLink FearAura => BuffAuraCached("fearaura",
      BuffRefs.WitchHexDeliciousFrightBuff.ToString(),
      BuffRefs.WitchHexDeathCurseBuff.ToString());

    /// <summary>
    /// 奇美拉的"战力增益"常驻素材：原版**天使屏障**（`AngelBarrier_Buff`）的 `FxOnStart`
    /// —— 一个金色**罩子**，比 D 类光环更明显（作者 10-10：光环太不明显）。重染成**暗红**。
    /// </summary>
    internal static PrefabLink BarrierAura => BuffAuraCached("barrieraura",
      BuffRefs.AngelBarrier_Buff.ToString());

    /// <summary>
    /// 铸铁的"护甲"常驻素材：原版**寒冰护甲**（`DLC5_IcyArmorArmorBuff`）的 `FxOnStart`
    /// —— 一套护甲外观（作者 10-10：光环在腿上有个诡异物件，换成护甲）。重染成**淡蓝**。
    /// </summary>
    internal static PrefabLink ArmorAura => BuffAuraCached("armoraura",
      BuffRefs.DLC5_IcyArmorArmorBuff.ToString());

    // ── 2026-10-11：岁稔 / 旗帜不倒 / 潮汐守望 / 永恒的注视 换"跟随单位"的独立素材 ──
    // 作者：这四个原来都借 D 类的**天使屏障**（金色罩子），观感不跟随、且四个一模一样。
    // 现各自换成**互不相同**的常驻素材，由 `TintedAuraFx` 克隆后挂到单位 `View` 上（一定跟随）。
    // ⚠ 素材必须"本来就常驻在单位身上"，不能是"施法瞬间爆发"型（见 TintedAuraFx 文件头）。

    /// <summary>岁稔：原版**萨满·自然之灵**（`ShamanNatureSpiritBaseBuff`）→ 禾绿自然灵光。</summary>
    internal static PrefabLink NatureAura => BuffAuraCached("natureaura",
      BuffRefs.ShamanNatureSpiritBaseBuff.ToString());

    /// <summary>旗帜不倒：原版**阿萨塔·英雄决意之歌**（`SongOfHeroicResolveBuff`）→ 暖红金决意光。</summary>
    internal static PrefabLink ValorAura => BuffAuraCached("valoraura",
      BuffRefs.SongOfHeroicResolveBuff.ToString());

    /// <summary>
    /// 潮汐守望：原版**水元素聚能**（`GatherPowerWaterBuff`）→ 青蓝水膜。
    /// ⚠ 上一版借的是 8 环**海之披风**（`SeamantleBuff`），但它的 `FxOnStart` 实际是**地面水花/水洼**
    ///   （粒子 Puddles / BottomWater / FrontCaustics，见 10-11 Player.log）→ 挂在身上"偏到一侧"。
    ///   换成**聚能**那种"包在身上"的水。
    /// </summary>
    internal static PrefabLink TideAura => BuffAuraCached("tideaura",
      BuffRefs.GatherPowerWaterBuff.ToString());

    /// <summary>永恒的注视：原版**萨满·邪眼**（`ShamanHexEvilEyeACBuff`）→ 银紫"注视"。</summary>
    internal static PrefabLink GazeAura => BuffAuraCached("gazeaura",
      BuffRefs.ShamanHexEvilEyeACBuff.ToString());

    /// <summary>
    /// **大地之怒**（9 环·以施法者为中心）的地面素材：原版**落石术**区域的常驻 `Fx`
    /// （`StoneCallArea`，Cylinder/**40**）—— 一片**落石/碎石**从地面砸起。
    /// ⚠ 作者 10-11：原来借的**尖石术**区域（`SpikeStonesArea`）"看起来像缠绕术、太丑"，换掉。
    /// 备选：`DLC3_ElementalEarthAreaEffect`（土元素区域，Cylinder/40）。
    /// </summary>
    internal static PrefabLink EarthWrathGround
    {
      get
      {
        if (Cache.TryGetValue("earthwrathground", out var cached)) return cached;
        var link = FxBorrow.AreaFx(
          AbilityAreaEffectRefs.StoneCallArea.ToString(),
          "493a488e5c7ea0546a958ccd18144b83"); // DLC3_ElementalEarthAreaEffect
        Cache["earthwrathground"] = link;
        return link;
      }
    }

    /// <summary>
    /// **重构**（9 环·群体完全恢复 + 复活）的施法爆发：原版 7 环**复活术**（`Resurrection`）的 `AbilitySpawnFx`
    /// —— 一道复活之光。作者 10-11："本来应该有的特效，我没看见" → 补上（锚施法者）。
    /// ⚠ 上一版把扫描表里的 **Fx 素材 ID** 误当成**能力 GUID** 传了进来 → `特效借用失败（爆发·法术）`
    ///   （见 10-11 的 Player.log）。这里改用 BlueprintCore 的 `AbilityRefs.Resurrection`（真能力 GUID）。
    /// </summary>
    internal static PrefabLink ReconstructionBurst
    {
      get
      {
        if (Cache.TryGetValue("reconburst", out var cached)) return cached;
        var link = FxBorrow.AbilityBurstFx(AbilityRefs.Resurrection.ToString());
        Cache["reconburst"] = link;
        return link;
      }
    }

    /// <summary>
    /// **文明的存续**（10 环·群体完全恢复 + 复活 + 后续增益）的施法爆发：原版**天使·圣歌**
    /// （`AngelHolyHymn`）的 `AbilitySpawnFx` —— 一圈宏大的圣光。作者 10-11：补上特效（锚施法者）。
    /// （同样修掉了"Fx 素材 ID 当能力 GUID"的错。）
    /// </summary>
    internal static PrefabLink CivilizationBurst
    {
      get
      {
        if (Cache.TryGetValue("civburst", out var cached)) return cached;
        var link = FxBorrow.AbilityBurstFx(AbilityRefs.AngelHolyHymn.ToString());
        Cache["civburst"] = link;
        return link;
      }
    }

    /// <summary>
    /// 有"常驻光环"的 D 类法术（键 = <see cref="SpellsSetup"/> 的 Spec.Key）。
    /// **2026-10-11 起这份名单已清空**：原 11 个友军增益里，坚守阵地改纯视觉区域、骑兵突进改蓝色加速拖尾、
    /// 过载模式/圣域/乌拉改 `TintedAuraFx`，潮汐守望/岁稔/旗帜不倒/永恒的注视也改 `TintedAuraFx`（各自不同素材），
    /// 文明的存续去掉光环、重构（Kind.Special，本来就不挂增益）去掉金光。保留空结构备用。
    /// </summary>
    private static readonly HashSet<string> AuraSpells = new()
    {
      // ⚠ 坚守阵地（HoldTheLine）**不在**这里：作者 10-11 要"**一个覆盖范围的大特效**"而不是每个友军各一个，
      //   走 `SpellsSetup` 的纯视觉区域（`Guids.HoldTheLineArea`）。
      // ⚠ 过载模式（Overloading）/ 圣域（Sanctuary）/ 乌拉（Ura）**也不在**：它们要"**跟随**单位"的常驻光环
      //   （`SetFxOnStart` 的观感不跟随，且罩子太浮夸），改走 `TintedAuraFx`（克隆后挂到单位 `View`，一定跟随）。
      // ⚠ 2026-10-11：潮汐守望 / 岁稔 / 旗帜不倒 / 文明的存续 / 永恒的注视 **全部移出**——
      //   作者要它们"以施法者为中心 + 各自不同的**跟随**特效（文明的存续不要特效）"，
      //   前四者改走 `TintedAuraFx`（NatureAura / ValorAura / TideAura / GazeAura），文明不给光环。
      //   于是这份 D 类名单**现在是空的**（保留结构备用；`AuraMaterial` 仍给别处复用）。
      // ⚠ 医疗无人机（MedicalDrone）**不在**这里：它借的是**原版"给单位挂视觉"的增益**
      // （提灯 / 治疗审判等，见 MedicalDroneAuraCandidates），由 MedicalDroneFx 克隆重染后挂到单位
      // `View` 下；不走本名单的金色光环（借光亮术 / `Shader.Find` 自建两条旧路都已废，见 dev-notes §7.10.5）。
    };

    // ── E 类：敌方减益的"常驻光环" ───────────────────────────────────────────────────
    //
    // E 类（敌方减益）要的是"被做了记号的敌人身上有一圈提示"。与 D 类（友军金色光环）**分开**：
    // 素材借原版"烙印 / 诅咒"类增益的 `FxOnStart`（读起来就是"这个敌人被标记了"）。
    // 与 D 类共用 `FxBorrow.BuffAura`（借增益常驻光）这条现成通道，只是换一批候选。
    // 第一个用例：**连锁殉爆**的标记（`ChainDetonation`，见 SpellsSetup 的 case "ChainDetonation"）。

    private static PrefabLink _markAura;
    private static bool _markAuraTried;

    /// <summary>
    /// 按法术 key 取该法术"敌方减益"的常驻光环素材；不属于这里（或借不到）返回 null，调用方保持"无光环"。
    /// 素材借原版增益的 `FxOnStart`，**原色上屏**（`SetFxOnStart` 是引擎直接播的 PrefabLink，改不了色）：
    ///   · `ChainDetonation`（连锁殉爆）→ **火之烙印**（`FirebrandBuff`，"被做了记号"；备选死亡诅咒 / 灾祸）；
    ///   · ~~`FireRecon`（火力侦察）/ `FinalSnipe`（终末狙击）~~ → **2026-10-16 已移出**：不再借火之烙印
    ///     （头顶一团火，与 PRTS / 狙击语义不符），改由 `FireReconFx` / `FinalSnipeFx` **自绘专属特效**
    ///     （`MarkFxKit` 管生命周期，跟增益同生同灭）；
    ///   · `SinPunishment`（罪罚）→ **降咒**类素材（`ArmyBestowCurseBlastBuff`，见 <see cref="CurseAura"/>）；
    ///   · `FrostPrayer`（寒霜祷言）→ **原版 `ColdBuff`**（持续寒气，见 <see cref="ColdAura"/>）。
    /// ⚠ **时之锁不走这里**：它要"改成蓝色"，原色挂载做不到，改由 <see cref="TintedAuraFx"/> 运行时克隆重染
    ///   （素材见 <see cref="SlowAura"/>）；**不稳定血浆**同理由 <see cref="TintedAuraFx"/> 挂血色光环
    ///   （素材见 <see cref="PlasmaAura"/>）。
    /// </summary>
    internal static PrefabLink DebuffAuraFor(string spellKey)
    {
      switch (spellKey)
      {
        // 连锁殉爆：标记（火之烙印——"被做了记号"）。
        case "ChainDetonation": return MarkAura();
        // 火力侦察 / 终末狙击：不再借"火之烙印"（头顶一团火，与 PRTS / 狙击语义不符），
        // 改为专属自绘特效，见 FireReconFx.cs / FinalSnipeFx.cs（MarkFxKit 管生命周期）。
        // 罪罚：不再借"降咒"黑雾（像死灵法师施法），改为专属自绘特效
        // （正义天平 + 律法法阵 + 惩击圣光，见 SinPunishmentFx.cs，MarkFxKit 管生命周期）。
        // 寒霜祷言：寒冷减益 → 目标身上一层持续寒气（借原版 ColdBuff 的 FxOnStart）。
        case "FrostPrayer": return ColdAura();
        default: return null;
      }
    }

    private static PrefabLink _curseAura;
    private static bool _curseAuraTried;

    /// <summary>
    /// 罪罚的减益光环素材：原版**诅咒**类增益的 `FxOnStart`（读起来就是"被诅咒了"）。
    ///   · `ArmyBestowCurseBlastBuff`（降咒·爆裂，`9a65b11d…`）——**首选**：语义就是"降咒"；
    ///   · `DoomOfServitudeBuff`（巫妖·奴役之厄，`9adb9cfa…`）——诅咒/厄运主题，备选；
    ///   · `WitchHexDeathCurseBuff`（女巫·死亡诅咒，`f4adcaac…`）——与连锁殉爆的标记同源，兜底。
    /// 借不到返回 null（只打日志，功能不受影响）。
    /// </summary>
    private static PrefabLink CurseAura()
    {
      if (!_curseAuraTried)
      {
        _curseAuraTried = true;
        _curseAura = FxBorrow.BuffAura(
          BuffRefs.ArmyBestowCurseBlastBuff.ToString(),
          BuffRefs.DoomOfServitudeBuff.ToString(),
          BuffRefs.WitchHexDeathCurseBuff.ToString());
      }
      return _curseAura;
    }

    private static PrefabLink _coldAura;
    private static bool _coldAuraTried;

    /// <summary>
    /// 寒霜祷言的减益光环素材：**原版 `Buffs/ColdBuff`**（寒冷环境增益）的 `FxOnStart`
    /// （`21b65d17…`）——一层持续的寒气，正好贴"寒冷减益"。备选 `ColdVisualBuff`（DLC5 暗影位面）。
    /// </summary>
    private static PrefabLink ColdAura()
    {
      if (!_coldAuraTried)
      {
        _coldAuraTried = true;
        _coldAura = FxBorrow.BuffAura(
          BuffRefs.ColdBuff.ToString(),
          BuffRefs.ColdVisualBuff.ToString());
      }
      return _coldAura;
    }

    private static PrefabLink MarkAura()
    {
      if (!_markAuraTried)
      {
        _markAuraTried = true;
        _markAura = FxBorrow.BuffAura(
          BuffRefs.FirebrandBuff.ToString(),
          BuffRefs.WitchHexDeathCurseBuff.ToString(),
          BuffRefs.DoombringingCreatureCruelBuff.ToString());
      }
      return _markAura;
    }

    private static PrefabLink _slowAura;
    private static bool _slowAuraTried;

    /// <summary>
    /// 时之锁的"缓慢"特效素材：原版缓慢增益（`Spells/Level3/SlowBuff`，`Guids.Vanilla.SlowBuff`）的 `FxOnStart`
    /// ——与游戏内"缓慢术"**同一份视觉**。由 <see cref="TintedAuraFx"/> 克隆后**重染成蓝色**、挂到每个受益者身上。
    /// </summary>
    internal static PrefabLink SlowAura
    {
      get
      {
        if (!_slowAuraTried)
        {
          _slowAuraTried = true;
          _slowAura = FxBorrow.BuffAura(Guids.Vanilla.SlowBuff);
        }
        return _slowAura;
      }
    }

    /// <summary>
    /// **不稳定血浆**的持续光环素材：原版**血雾**（`BloodHazeBuff`，恶魔神话法术"血雾"）的 `FxOnStart`
    /// —— 语义就是"血"。由 <see cref="TintedAuraFx"/> 克隆后**重染成血色**、挂到受益者身上（跟增益同生同灭）。
    /// 备选：血怒者狂暴 `BloodragerStandartRageBuff`、流血 `BleedEnchantment1d6Buff`。
    /// </summary>
    internal static PrefabLink PlasmaAura
    {
      get
      {
        if (Cache.TryGetValue("plasmaaura", out var cached)) return cached;
        var link = FxBorrow.BuffAura(
          BuffRefs.BloodHazeBuff.ToString(),
          BuffRefs.BloodragerStandartRageBuff.ToString(),
          BuffRefs.BleedEnchantment1d6Buff.ToString());
        Cache["plasmaaura"] = link;
        return link;
      }
    }
  }
}
