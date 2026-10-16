namespace RhodesDoctor
{
  /// <summary>
  /// 本 mod 全部蓝图的 GUID。发布后不能再改（存档引用它们）。
  /// 前缀 d0c7 是随手定的"博士"谐音，后缀按分区编号。
  ///
/// 分区：
    ///   0x01–0x1f  核心：变体 / 集火 / 源石技艺精通 / 灵光 / 圣疗与正能量克隆
    ///   0x20–0x3f  巴别塔的恶灵 + 0x38–0x3c 测试木桩 + 0x3d–0x3e 机制补件 + 0x3f Mastery5Feature
    ///   0x40–0x7f  指令法术附带的动态增益（Guids.Buff(i)，i=0..63）
    ///   0x80–0x9f  静态机制补件（不进动态段）
    ///   0xa0–0xdf  指令法术本体（Guids.Spell(i)，共 64 个）
    ///   0xe0–0xff  法术书与法术列表 + 召唤 + 指令附带增益 Predicate
  /// </summary>
  internal static class Guids
  {
    private const string P = "d0c7d0c7-d0c7-4d0c-8d0c-d0c7d0c7d0";
    /// <summary>第二前缀：测试木桩·增益（P 的空位不够用了）。发布后同样不能再改。</summary>
    private const string P2 = "d0c7d0c7-d0c7-4d0c-8d0c-d0c7d0c8d0";

    // ── 核心 ──
    internal const string Archetype            = P + "01";
    internal const string SpellcastingFeature  = P + "02"; // 保留位（法术书改由 SetReplaceSpellbook 挂载）
    internal const string FocusFireResource    = P + "03";
    internal const string CoolHeadFeature      = P + "04";
    internal const string AuraCommandFeature   = P + "05";
    internal const string AuraCommandSelfBuff  = P + "06";
    internal const string AuraCommandAllyBuff  = P + "07";
    internal const string TotalCommandFeature  = P + "08";
    internal const string TotalCommandSelfBuff = P + "09";
    internal const string TotalCommandAllyBuff = P + "0a";
    internal const string FocusFireFeature     = P + "0b";
    internal const string FocusFireAbility     = P + "0c";
    internal const string FocusFireMarkBuff    = P + "0d";
    internal const string LohResource          = P + "0e";
    internal const string LohSelfAbility       = P + "0f";
    internal const string LohOthersAbility     = P + "10";
    internal const string LohHarmAbility       = P + "11"; // 保留位（圣疗不再单独克隆 harm）
    internal const string LohFeature           = P + "12";
    internal const string ChannelHealAbility   = P + "13";
    internal const string ChannelHarmAbility   = P + "14";
    internal const string ChannelFeature       = P + "15";
    internal const string Mastery1Feature      = P + "16"; // 1 级：穿透/DC +1
    internal const string Mastery2Feature      = P + "17"; // 5 级：+2（替换 +1）
    internal const string Mastery3Feature      = P + "18"; // 9 级：+3
    internal const string Mastery4Feature      = P + "19"; // 13 级：+4
    internal const string Mastery5Feature      = P + "3f"; // 17 级：+5（2026-10-11 改版新增，唯一空闲核心段后缀）
    internal const string CapstoneFeature      = P + "1a";
    internal const string CapstoneBuff         = P + "1b";

    // ── 巴别塔的恶灵（0x20 段）──
    internal const string SpecterResource         = P + "20";
    internal const string Stage1Feature           = P + "21"; // 8 级
    internal const string Stage2Feature           = P + "22"; // 12 级
    internal const string Stage3Feature           = P + "23"; // 16 级
    internal const string TogglesV1Holder         = P + "24";
    internal const string TogglesV2Holder         = P + "25";
    internal const string TogglesV3Holder         = P + "26";
    internal const string LockBuff                = P + "27"; // 一段锁定（8–11 级，半额倒扣，禁集火）
    internal const string LockBuffV2               = P + "89"; // 二段锁定（12–15 级，全额倒扣，禁集火；R2 新增）
    internal const string LockBuffV3              = P + "28"; // 三段锁定（16 级起，全额倒扣，允许集火）
    internal const string AftermathRestrictBuff   = P + "29";
    internal const string AftermathSpeedBuff      = P + "2a";
    internal const string AgileToggleV1           = P + "2b";
    internal const string AgileBuffV1             = P + "2c";
    internal const string EnduringToggleV1        = P + "2d";
    internal const string EnduringBuffV1          = P + "2e";
    internal const string AgileToggleV2           = P + "2f";
    internal const string AgileBuffV2             = P + "30";
    internal const string EnduringToggleV2        = P + "31";
    internal const string EnduringBuffV2          = P + "32";
    internal const string AgileToggleV3           = P + "33";
    internal const string AgileBuffV3             = P + "34";
    internal const string EnduringToggleV3        = P + "35";
    internal const string EnduringBuffV3          = P + "36";
    internal const string EndSpecterAbility       = P + "37"; // 结束恶灵（自由动作，删全部形态增益）

    // ── 测试木桩（UMM 测试面板专用）──
    internal const string TestFaction             = P + "38"; // 木桩阵营（对所有人敌对）
    internal const string TestDummyUnit           = P + "39"; // 木桩单位（5000 血，不攻击）
    internal const string TestDummyLowHpUnit      = P + "3c"; // 脆皮木桩（10 血，速度正常，不攻击）——测"死亡触发"类效果（连锁殉爆等）

    // ── 测试木桩·增益（P2）：6 个"常见增益"克隆（都标 IsFromSpell → 可被王冠的回响偷取）+ 1 个带它们的木桩 ──
    internal const string TestBuffHaste           = P2 + "01"; // 测试增益·加速
    internal const string TestBuffBless           = P2 + "02"; // 测试增益·祝福
    internal const string TestBuffMageArmor       = P2 + "03"; // 测试增益·法师护甲
    internal const string TestBuffShield          = P2 + "04"; // 测试增益·护盾
    internal const string TestBuffHeroism         = P2 + "05"; // 测试增益·英雄气概
    internal const string TestBuffBearEndurance   = P2 + "06"; // 测试增益·熊之坚韧
    internal const string TestDummyBuffed         = P2 + "07"; // 增益木桩（自带上面 6 个）
    internal const string TestBuffArmor           = P + "3a"; // 木桩增益·护甲（可驱散，可偷取）
    internal const string TestBuffResist          = P + "3b"; // 木桩增益·抗力（可驱散，可偷取）

    // ── 机制补件（v0.5.2 修目标/持续时新增）──
    internal const string FieldDressingTreatedBuff = P + "1c"; // 医疗敷料·24 小时标记
    internal const string CalcificationArea        = P + "1d"; // 钙质化·区域
    internal const string BlazingSunBlindBuff      = P + "81"; // 耀阳颔首·目盲
    internal const string BlazingSunDazzleBuff     = P + "9c"; // 耀阳颔首·目眩（N15，豁免成功者 1 轮）
    internal const string ShadowRealmDazzleBuff    = P + "9d"; // 影之疆域·目眩（G12，豁免成功者 1 轮）

    // ── 灵光区域（V3：原版灵光 = 持有者增益 → AddAreaEffect → 区域 → 友军增益）──
    internal const string AuraCommandArea          = P + "1e"; // 指挥灵光·区域（10 尺）
    internal const string TotalCommandArea         = P + "1f"; // 全军指令·区域（30 尺）

    // ── 更多机制补件（V/B/C 批次）──
    internal const string ShadowRealmArea          = P + "85"; // 影之疆域·领域（30 尺，敌我分挂）
    internal const string CatastropheArea          = P + "86"; // 天灾降临·区域（80 尺，每轮伤害）
    internal const string DelayedBlastBombBuff      = P + "87"; // 延时爆破·炸弹载体（挂地面区域）
    internal const string DelayedBlastDetonateAbility = P + "88"; // 延时爆破·手动引爆（自由动作）
    internal const string DelayedBlastArea            = P + "9e"; // 延时爆破·地面标记（纯视觉区域）
    internal const string HarvestYearsResidualBuff   = P + "8a"; // 岁稔·余温（结束时临时生命，1 分钟）
    internal const string HasteAccelBuff             = P + "8b"; // 加速效果（迅捷突破 / 永恒注视通用，C34）
    internal const string NightterrorPanicBuff       = P + "8c"; // 梦魇·恐慌 2 轮（到期转动摇，C12）
    internal const string NightterrorShakenBuff      = P + "8d"; // 梦魇·动摇（C12）

    // ── 第三轮新增（N/C/T；0x90 段空闲）──
    internal const string VolcanoEmberBuff           = P + "90"; // 火山·余焰 2 轮（C11，每轮单独豁免）
    internal const string TwilightMeltBuff           = P + "91"; // 黄昏·熔火 2 轮（C19，每轮单独豁免）
    internal const string FlashbangDazzleBuff        = P + "92"; // 闪光弹·目眩（C13，成功 1 轮 / 失败后接力）
    internal const string FlashbangBlindBuff         = P + "93"; // 闪光弹·目盲 1 轮（C13，到期转目眩）
    internal const string ScorchResistBuff           = P + "94"; // 灼烧·火焰抗力 −10（C10，1 轮/级，Valmallos 同款负值）
    internal const string BlackSnakeBurnBuff         = P + "95"; // 黑蛇之焰·灼烧（C17，每轮 3d6）
    internal const string FrozenEntangledBuff        = P + "96"; // 冰封领域·定身 1 轮（C15，失败者 Entangled）
    internal const string FrozenHalfSpeedBuff        = P + "97"; // 冰封领域·减速 1 轮（C15，成功者半速，IronBody 同款）
    internal const string FoxfireTouchBuff           = P + "98"; // 儿时舞乐·影触（C27，敌方进场/每轮意志豁免，仿影之疆域）
    internal const string CalcHalfSpeedBuff          = P + "99"; // 钙质化·半速（C26，敌方半速，IronBody 同款；豁免 −2 另挂）
    internal const string FrozenDomainArea           = P + "8e"; // 冰封领域·区域（C15，30 尺，进入/每轮结算）
    internal const string DrunkenDreamSleepBuff       = P + "8f"; // 醉梦·沉眠（C18，睡眠+醒来恍惚，免疫仍恍惚）

    // ── 法术书与法术列表 ──
    internal const string Spellbook               = P + "e0";
    internal const string SpellList               = P + "e1";
    internal const string SpellsPerDayTable       = P + "e2"; // 自建"术士表 + 10 环列"（10 环法位）
    internal const string SpellsKnownTable        = P + "e3"; // 自建"术士表 + 10 环列"（20 级升级界面选 10 环）
    internal const string MythicSpellList         = P + "e4"; // 10 环指令的独立小表（挂 MythicSpellList）

    // ── 召唤物与镜像 ──
    internal const string InklingUnit             = P + "e5"; // 墨灵单位蓝图
    internal const string InklingDeathBurstBuff   = P + "e6"; // 墨灵自爆增益
    internal const string MirrorLockBuff          = P + "e7"; // 镜像行为锁（禁施法与主动能力）

    // ── 指令的额外增益（v0.4.0 好做批）──
    internal const string SilencerDebuffBuff      = P + "e8"; // 缄默减益
    internal const string ExecutionBuff           = P + "e9"; // 处决蓄势
    internal const string CalcificationEnemyBuff  = P + "ea"; // 钙质化·敌方
    internal const string CalcificationAllyBuff   = P + "eb"; // 钙质化·友军
    internal const string ArtilleryBarrageBuff    = P + "ec"; // 炮火延伸·炮击追踪（挂施法者）
    internal const string CivilizationAfterBuff    = P + "ed"; // 文明的存续·后续（+4 与免死，挂全队）
    internal const string BannerStandsBuff          = P + "ee"; // 旗帜不倒（0 血不倒，30 尺爆发内友军各挂）
    internal const string ShadowRealmBlindBuff      = P + "ef"; // 影之疆域·目盲（敌方）
    internal const string ShadowRealmHideBuff       = P + "f0"; // 影之疆域·隐形（友方）
    internal const string ShadowRealmHolderBuff     = P + "f1"; // 影之疆域·载体（挂目标友军，随人走）
    internal const string ShadowRealmTouchBuff      = P + "f2"; // 影之疆域·影触（敌方，进场/每轮豁免）
    internal const string SmokescreenArea          = P + "f3"; // 烟幕掩护·区域
    internal const string SmokescreenConcealBuff   = P + "f4"; // 烟幕掩护·隐蔽（敌方）
    internal const string FoxfireArea             = P + "f5"; // 儿时舞乐·区域
    internal const string FoxfireSlowBuff         = P + "f6"; // 儿时舞乐·缓慢易伤（敌方）
    internal const string FoxfireRegenBuff        = P + "f7"; // 儿时舞乐·恢复（友方）
    internal const string ReturnToAbyssBuff       = P + "f8"; // 归溟（挂自己）
    internal const string SinPunishmentBuff       = P + "f9"; // 罪罚（挂敌人）
    internal const string BeaconArea              = P + "fa"; // 信标投射·区域
    internal const string BeaconRevealBuff        = P + "fb"; // 信标投射·显形（敌方）
    internal const string SwiftBreachBuff         = P + "fc"; // 迅捷突破（挂自己）
    internal const string FinalSnipeMarkBuff      = P + "fd"; // 终末狙击·标记
    internal const string ChainDetonationMarkBuff = P + "fe"; // 连锁殉爆·标记
    internal const string TacticianOfOldBuff      = P + "ff"; // 旧日的战术家（挂自己）
    internal const string EternalGazeBuff         = P + "80"; // 永恒的注视（挂友军）
    internal const string HarvestYearsBuff          = P + "84"; // 岁稔·积攒（30 尺爆发内友军各挂）
    internal const string TacticianEnduringBuff     = P + "82"; // 旧日·体质套件（敏捷在场时补）
    internal const string TacticianAgileBuff        = P + "83"; // 旧日·敏捷套件（体质在场时补）

    // ── 画中世界重构（10-10 深夜：改成"放逐 N 轮 → 原位返回 + 麻痹"）──
    internal const string PaintedWorldBanishBuff    = P + "9a"; // 画中世界·放逐载体（挂施法者：放逐/计时/放回）
    internal const string PaintedWorldParalysisBuff = P + "9b"; // 画中世界·返回后麻痹（挂目标）

    // ── 坚守阵地"单个大范围特效"（10-11：不再每个友军各一个光环）──
    internal const string HoldTheLineArea           = P + "9f"; // 坚守阵地·纯视觉区域（半径 30，跟法术同生同灭）

    // ── 火山余焰"单个落点特效"（10-11：不再每个敌人各一根火柱）──
    internal const string VolcanoTrackerBuff        = P + "3d"; // 火山·余焰落点追踪（挂施法者，记住落点）

    // ── 大地之怒"以施法者为中心"的地面特效（10-11）──
    internal const string EarthFuryArea              = P + "3e"; // 大地之怒·纯视觉区域（半径 = 法术半径，落石地面 FX）

    /// <summary>第 i 个指令法术（0–63，顺序见 SpellsSetup.Specs）。</summary>
    internal static string Spell(int i) => P + (0xa0 + i).ToString("x2");

    /// <summary>第 i 个指令法术附带的增益（仅 Buff 类法术使用）。</summary>
    internal static string Buff(int i) => P + (0x40 + i).ToString("x2");

    /// <summary>
    /// 原版蓝图 GUID 常量（2.8.7 里对应的 Refs 常量不存在，用真实 GUID 代替）。
    /// 全部已在游戏数据中核实。
    /// </summary>
    internal static class Vanilla
    {
      internal const string PaladinClass                = "bfa11238e7ae3544bbeb4d0b92e897ec";
      internal const string SlowBuff                    = "0bc608c3f2b548b44b7146b7530613ac"; // 未使用（反重力走自建 Slowed），留档
      internal const string EvasionFeature              = "576933720c440aa4d8d42b0c54b77e80"; // Evasion
      internal const string SmiteEvilFeature            = "3a6db57fce75b0244a6a5819528ddf26";
      internal const string PaladinSmiteEvil            = "1d1c02976b82476abf792dd2902f5a88"; // 未使用，留档
      internal const string SmiteEvilAdditionalFeature  = "0f5c99ffb9c084545bbbe960b825d137"; // SmiteEvilAdditionalUse
      internal const string DivineGraceFeature          = "8a5b5e272e5c34e41aa8b4facbb746d3"; // DivineGrace
      internal const string LayOnHandsFeature           = "858a3689c285c844d9e6ce278e686491";
      internal const string ChannelEnergyPaladinFeature = "cb6d55dda5ab906459d18a435994a760";
      internal const string AuraOfResolveFeature        = "a28693b24cc412c478b8b85877f2dad2";
      internal const string AuraOfJusticeFeature        = "9f13fdd044ccb8a439f27417481cb00e";
      internal const string HolyChampionFeature         = "eff3b63f744868845a2f511e9929f0de"; // HolyChampion
      internal const string LayOnHandsSelfAbility       = "8d6073201e5395d458b8251386d72df1"; // LayOnHandsSelf
      internal const string LayOnHandsOthersAbility     = "caae1dc6fcf7b37408686971ee27db13"; // LayOnHandsOthers
      internal const string ChannelPositiveHealAbility  = "6670f0f21a1d7f04db2b8b115e8e6abf"; // ChannelEnergyPaladinHeal
      internal const string ChannelPositiveHarmAbility  = "4937473d1cfd7774a979b625fb833b47"; // ChannelEnergyPaladinHarm
      internal const string ChannelPositiveResource     = "5e2bba3e07c37be42909a12945c27de7"; // ChannelEnergyResource（通用池，无圣武士专用）
      internal const string SorcererSpellsDailyTable    = "dbff16956df2eda48a1da5c9617cc836"; // SorcererDailyTable
      internal const string SorcererSpellsKnownTable    = "d232bc78d967a964bac4d4d38e7ca5f4"; // SorcererKnownTable

      // ── 抄机制用的原版蓝图（已核实）──
      internal const string TimeStopAbility      = "70e55e5b0ac84af78c084eb6cf47ae06"; // TimeStop
      internal const string TimeStopBuff         = "f4d026d3bf5a4e8183ded452e8d87260"; // TimeStopBuff（备用方案）
      internal const string SummonMonsterIIIBase = "5d61dde0020bbf54ba1521f7ca0229dc"; // 召唤动作对照
      internal const string BanishmentAbility    = "d361391f645db984bbf58907711a146a"; // 画中世界对照
      internal const string ResurrectionAbility  = "80a1a388ee938aa4e90d427ce9a7a3e9"; // Resurrection
      internal const string RaiseDeadAbility     = "a0fc99f0933d01643b2b8fe570caa4c5"; // RaiseDead
      internal const string DominatePerson       = "d7cbd2004ce66a042aeab2e95a3c5c61"; // 未使用（王冠回响是驱散+分赃），留档
      internal const string DominatePersonBuff   = "c0f4e1c24c9cd334ca988ed1bd9d201f"; // 未使用，留档
      internal const string ConeOfCold           = "e7c530f8137630f4d9d7ee1aa7b1edc0"; // 锥形范围参照，留档
      internal const string LightningBolt        = "d2cff9243a7ee804cb6d5be47af30c73"; // 直线范围参照，留档
      // 直线投递用的投射物，V2：直线法术必须挂真实投射物才有目标。
      internal const string LightningBoltProjectile = "c7734162c01abdc478418bfb286ed7a5"; // 闪电束（默认/电击）
      internal const string FireLineProjectile       = "ecf79fc871f15074e95698a3fef47aee"; // FireLine00（火焰直线）
      internal const string HolyLineProjectile       = "2511627d593387d4d89004bec111ba31"; // SearingLight00（光/神圣射线）
      internal const string SwordTraceLineProjectile = "b8e9b14366e046309a1fa7d9589fb1a0"; // Sithhud_SwordTraceLine00（剑痕直线）
      internal const string EarthLineProjectile      = "5d66a6c3cac5124469b2d0474e53ecab"; // Kinetic_EarthBlastLine00（土石直线）


      // ── 借图标用的原版特性（2026-10-11：给"没做专属图"的本模组特性补图，见 Icons.Borrow）──
      // 只借 Sprite（蓝图里的 m_Icon），不新增 PNG；一个 GUID 换一个观感，随时可换。
      internal const string SpellPenetrationFeature     = "ee7dc126939e4d9438357fbd5980d459"; // 法术穿透 → 源石技艺精通（克服法术抗力）
      internal const string IronWillFeat                = "175d1577bb6c9a04baf88eec99c66334"; // 钢铁意志 → 冷静头脑（豁免加值）
      internal const string RageFeature                 = "2479395977cfeeb46b482bc3385f4647"; // 狂暴 → 巴别塔的恶灵（一/二/三段）

      // ── 神话合书登记点（BlueprintFeatureSelectMythicSpellbook，AllowedSpellbooks 字段）──
      // 前两条是游戏本体，后四条来自 SpellbookMerge mod（无该 mod 时 GetRef 运行时找不到，
      // RegisterForMythicMerge 逐条 try/catch 跳过，不加硬依赖）。
      internal const string AngelIncorporateSpellbook     = "e1fbb0e0e610a3a4d91e5e5284587939";
      internal const string LichIncorporateSpellbook      = "3f16e9caf7c683c40884c7c455ed26af";
      internal const string AeonIncorporateSpellbook      = "2b7027ee76cb4c58b2cff0475bc69fbb";
      internal const string AzataIncorporateSpellbook     = "83385d9f4d714e4e94618703be762a20";
      internal const string DemonIncorporateSpellbook     = "f3ff8515355e4738b128c3d01483f1ca";
      internal const string TricksterIncorporateSpellbook = "c4ef6975167d4cf5acbfd66b60e63f9c";
    }
  }
}
