罗德岛博士 mod · 技能图标
========================

png-64/    64×64 PNG，游戏技能图标尺寸，可直接放进 mod 工程使用。
original/  原始大图（webp），需要别的尺寸时从这里重新缩放。

代码已在 src/Icons.cs 接入指令和恶灵图标；状态增益也会尽量使用对应图标。
集火与指挥灵光优先读取 png-64/status/ 下的独立 64×64 PNG；缺少时回退到现有指令图标。
安装时必须把 icons/png-64/ 文件夹连同 DLL 一起复制，游戏重新启动后才会重新加载图标。

文件与能力对照：
  spells/00-tactical-mark              战术标记（0 环戏法）
  spells/00-medical-dressing           医疗敷料（0 环戏法）
  spells/00-battlefield-survey         战场测绘（0 环戏法）
  spells/00-beacon-projection          信标投射（0 环戏法）
  spells/00-suppressing-shot           牵制射击（0 环戏法）
  spells/00-emergency-extraction       紧急撤离（0 环戏法）
  spells/01-hold-the-line              坚守阵地（1 环）
  spells/01-cavalry-charge             骑兵突进（1 环）
  spells/01-frost-prayer               寒霜祷言（1 环）
  spells/01-emergency-treatment        紧急救治（1 环）
  spells/01-fire-recon                 火力侦察（1 环）
  spells/01-medic-drone                医疗无人机（1 环）
  spells/02-ura                        乌拉！（2 环）
  spells/02-unstable-plasma            不稳定血浆（2 环）
  spells/02-anti-gravity               反重力（2 环）
  spells/02-smokescreen                烟幕掩护（2 环）
  spells/02-delayed-blast              延时爆破（2 环）
  spells/02-silencer                   缄默（2 环）
  spells/03-overloading                过载模式（3 环）
  spells/03-sword-rain                 剑雨（3 环）
  spells/03-calcification              钙质化（3 环）
  spells/03-time-lock                  时之锁（3 环）
  spells/03-scorching-breath           灼烧（3 环）
  spells/03-execution                  处决（3 环）
  spells/04-sanctuary                  圣域（4 环）
  spells/04-volcano                    火山（4 环）
  spells/04-dragnet                    牵引（4 环）
  spells/04-nightterror                梦魇（4 环）
  spells/04-swift-breach               迅捷突破（4 环）
  spells/04-flashbang                  闪光弹（4 环）
  spells/05-chimera                    奇美拉（5 环）
  spells/05-mon3tr                     Mon3tr（5 环）
  spells/05-ironcast                   铸铁（5 环）
  spells/05-inked-mountains            墨染山河（5 环）
  spells/05-frozen-domain              冰封领域（5 环）
  spells/05-royal-charge               王者冲锋（5 环）
  spells/06-tidal-elegy                涌潮悲歌（6 环）
  spells/06-foxfire-dance              儿时的舞乐（6 环）
  spells/06-drunken-dream              醉梦（6 环）
  spells/06-black-snake-flame          黑蛇之焰（6 环）
  spells/06-artillery-barrage          炮火延伸（6 环）
  spells/06-sin-punishment             罪罚（6 环）
  spells/07-dusk                       黄昏（7 环）
  spells/07-mirror-flow                镜中流形（7 环）
  spells/07-final-snipe                终末狙击（7 环）
  spells/07-chain-detonation           连锁殉爆（7 环）
  spells/07-thunderous-judgment        雷霆审判（7 环）
  spells/07-return-to-abyss            归溟（7 环）
  spells/08-painted-world              画中世界（8 环）
  spells/08-blazing-sun                耀阳颔首（8 环）
  spells/08-tidal-vigil                潮汐守望（8 环）
  spells/08-shadow-realm               影之疆域（8 环）
  spells/08-harvest-years              岁稔（8 环）
  spells/08-banner-stands              旗帜不倒（8 环）
  spells/09-shadow-blade               影霄·拔刀（9 环）
  spells/09-chronostasis               时空静止（9 环）
  spells/09-cloudpiercer               穿云（9 环）
  spells/09-earth-fury                 大地之怒（9 环）
  spells/09-reconstruction             重构（9 环）
  spells/09-tactician-of-old           旧日的战术家（9 环）
  spells/10-civilization               文明的存续（10 环）
  spells/10-eternal-gaze               永恒的注视（10 环）
  spells/10-crown-echo                 王冠的回响（10 环）
  spells/10-catastrophe                天灾降临（10 环）
  specter/agile-form                   巴别塔的恶灵·敏捷形态
  specter/enduring-form                巴别塔的恶灵·体质形态
  specter/aftermath                    恶灵余波
  specter/doctor-capstone              罗德岛的博士（20 级）
  status/focus-fire-mark               集火标记（可选，缺少时使用战术标记图标）
  status/command-aura                  指挥灵光（可选，缺少时使用坚守阵地图标）
