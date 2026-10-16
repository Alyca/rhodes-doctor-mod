# 描述规范化（2026-10-11）：按 docs/description-style-guide.md 改写 LocalizedStrings.json 全部 .desc
#   1) 去掉手写状态栏（头区由蓝图字段自动渲染）2) 去署名/实现细节 3) 术语按官方 4) 术语加 {g|} 链接（单遍替换，防嵌套）
import json, re

ZH_TOKENS = ['学派：', '施法时间：', '距离：', '目标：', '区域：', '持续时间：', '持续：', '豁免：', '法术抗力：',
             '迅捷动作', '标准动作', '移动动作', '自由动作', '整轮动作', '个人', '接触', '近距', '中距', '极距',
             '轮/级', '分钟/级']
DAMAGE_ZH = r'(钝击|穿刺|挥砍|火焰|寒冷|电击|酸蚀|音波|力场|神圣)'


def zh_clean(t):
    if not t:
        return t
    parts = t.split('。')
    if len(parts) >= 2 and sum(1 for k in ZH_TOKENS if k in parts[0]) >= 2:
        t = '。'.join(parts[1:])
    t = re.sub(r'——[^。]*', '', t)
    t = re.sub(r'（[^）]*实现[^）]*）', '', t)
    t = re.sub(r'[，。；]?\s*(以你为中心|近距内选一点|中距内选一点)[^。]*?。', '', t)
    t = re.sub(r'[，；]?\s*持续\s*[\d]+\s*(轮|分钟)/级\s*。', '。', t)
    t = t.replace('抗力各', '抗性各').replace('火焰抗力', '火焰抗性').replace('能量抗力', '能量抗性')
    t = t.replace('罚值', '减值').replace('轰击', '钝击').replace('定身', '纠缠')
    t = re.sub(r'^【10 环】', '', t)
    t = re.sub(r'(\d+d\d+)\s*点?\s*' + DAMAGE_ZH + r'(?=[、与，。；)])', r'\1 点\2伤害', t)
    t = re.sub(r'。+', '。', t).strip()
    if t and not t.endswith('。'):
        t += '。'
    return t


SCHOOLS = ['Abjuration', 'Conjuration', 'Divination', 'Enchantment', 'Evocation', 'Illusion', 'Necromancy', 'Transmutation']
EN_MARK = ['ft', 'round', 'personal', 'close', 'medium', 'long', 'swift', 'standard', 'burst', 'reflex', 'will',
           'fortitude', 'negates', 'half', 'save', 'action', 'touch', 'instantaneous', 'radius', 'line', 'minute',
           'one ally', 'one enemy', 'one creature']
EN_DMG = r'(fire|cold|electricity|acid|sonic|force|holy|divine|bludgeoning|piercing|slashing)'


def en_clean(t):
    if not t:
        return t
    t = re.sub(r'^\[Specter\]\s*', '', t.lstrip())
    t = re.sub(r'^\[10th level\]\s*', '', t)
    m0 = re.match(r'^([A-Z][a-z]+)( \([^)]*\))?\s*;', t)
    if m0 and m0.group(1) in SCHOOLS:
        for mm in re.finditer(r'\.\s', t):
            if t[max(0, mm.start() - 2):mm.start()] == 'ft':
                continue
            head = t[:mm.start()]
            if sum(1 for k in EN_MARK if k in head.lower()) >= 2:
                t = t[mm.end():]
            break
    t = re.sub(r'\s*\(implemented[^)]*\)', '', t)
    t = re.sub(r'\s*\([A-Za-z][A-Za-z \'!.-]{1,40}\)\s*$', '', t)
    t = re.sub(r'(\d+d\d+) ' + EN_DMG + r'\b(?! damage)', r'\1 \2 damage', t)
    t = re.sub(r'\s+', ' ', t).strip()
    return t


ZH_FIX = {
    'rhodesdoctor.focusfire.desc': '标记一名敌人：所有友军对它的攻击与伤害获得 +1 洞察加值，每 4 个圣武士等级再 +1（20 级 +6）。标记持续时间等于你的智力调整值（至少 1 轮）。同一时间只能维持一个标记。每日可用 3 + 智力调整值 次。',
    'rhodesdoctor.spell.HoldTheLine.desc': '目标在防御等级（AC）上获得 +2 洞察加值，在所有豁免检定上获得 +1 洞察加值。',
    'rhodesdoctor.spell.CavalryCharge.desc': '目标移动速度获得 +10 尺不具类型加值（10 级起 +20 尺），并对借机攻击获得 +4 闪避加值 AC。',
    'rhodesdoctor.spell.SuppressingShot.desc': '目标的下一次攻击检定承受 –1 减值；若它本轮攻击除你以外的生物，该减值变为 –2。',
    'rhodesdoctor.spell.FrostPrayer.desc': '目标在防御等级（AC）与所有豁免检定上承受 –2 减值。',
    'rhodesdoctor.spell.Overloading.desc': '目标进行整轮攻击时可额外进行一次全力攻击加值的攻击；攻击检定 +1（不具类型），防御等级（AC）与反射豁免 +1（闪避），所有移动速度 +30 尺（不具类型）。各项均可与加速术叠加。',
    'rhodesdoctor.spell.TidalElegy.desc': '攻击检定 +6（不具类型），武器伤害获得等于施法者等级的加值，获得 60 点临时生命，移动速度 +20 尺。',
    'rhodesdoctor.spell.Execution.desc': '你的下一次近战命中若目标当前生命值不高于其最大值的一半，则该次武器伤害骰翻倍；额外精准伤害不翻倍。触发后法术结束。',
    'rhodesdoctor.spell.ReturnToAbyss.desc': '你每次近战命中恢复 2d6 点生命值（每轮至多三次）；每轮首次造成重击或击杀敌人时，返还 1 轮恶灵持续时间（最多返还三轮）。',
    'rhodesdoctor.spell.Ura.desc': '结束目标身上的动摇、恐惧与恐慌；其攻击检定与武器伤害获得 +1 不具类型加值（10 级起 +2）。',
    'rhodesdoctor.spell.MirrorFlow.desc': '制造目标的镜像复制体：其属性、防御等级（AC）、攻击加值与武器都与目标相同，生命值等于目标当前生命值，法术位与资源均为满值；它在目标回合后行动，可以正常行动与施法；持续 1 轮/级，或生命值归零时消散。',
    'rhodesdoctor.aura.desc': '半径 10 尺灵光：博士与灵光内的友军攻击检定与先攻检定获得 +1 胜任加值。博士失去意识或恶灵生效期间失效。此能力替换圣武士的施法能力。',
    'rhodesdoctor.total.desc': '指挥灵光扩大到 30 尺，加值提高到 +2。此能力替换正义灵光。',
    'rhodesdoctor.total.self.desc': '30 尺内的友军获得全军指令。',
    'rhodesdoctor.specter.lock.desc': '恶灵生效期间，博士只能施放【恶灵】指令；源石技艺精通失效且法术穿透倒扣；指挥灵光与全军指令失效。',
    'rhodesdoctor.spell.Sanctuary.desc': '目标对抗法术与类法术能力的豁免检定获得 +4 神圣加值；并获得火焰、寒冷、电击、酸蚀与音波抗性 10，可与其他来源叠加。',
    'rhodesdoctor.spell.Silencer.desc': '目标不能施展法术，也不能使用类法术能力或超自然能力；该效果不妨碍被动能力。',
    'rhodesdoctor.spell.Reconstruction.desc': '只复活友军（召唤物不复活，其余效果照常）；解除除诅咒、疾病、中毒以外的所有负面效果；阵亡者复活并恢复一半生命值，每次施法至多三名。',
    'rhodesdoctor.spell.Civilization.desc': '30 尺内的友军完全恢复生命值、解除除诅咒、疾病、中毒以外的所有负面效果，并复活（至多五名）；其后 1 分钟内，攻击检定、伤害、防御等级（AC）与豁免检定获得 +4 加值，且第一次将降至 0 生命值以下时改为保留 1 点。不清除剧情诅咒。',
    'rhodesdoctor.spell.EternalGaze.desc': '每个目标第一次将要死亡时不会死亡，改为恢复全部生命值、传送至你相邻空格，并立刻获得 1 分钟加速。每个目标仅触发一次。',
    'rhodesdoctor.testdummy.buff.haste.desc': 'AC 与反射豁免 +1（闪避），攻击检定 +1（不具类型）——近似加速术。可被驱散与偷取。',
    'rhodesdoctor.testdummy.buff.bless.desc': '攻击检定 +1（士气）——近似祝福术。可被驱散与偷取。',
    'rhodesdoctor.testdummy.buff.magearmor.desc': 'AC +4（护甲）——近似法师护甲。可被驱散与偷取。',
    'rhodesdoctor.testdummy.buff.shield.desc': 'AC +4（盾牌）——近似护盾术。可被驱散与偷取。',
    'rhodesdoctor.testdummy.buff.heroism.desc': '攻击检定与所有豁免检定 +2（士气）——近似英雄气概。可被驱散与偷取。',
    'rhodesdoctor.testdummy.buff.bearendurance.desc': '体质 +4（增强）——近似熊之坚韧。可被驱散与偷取。',
    'rhodesdoctor.spell.Smokescreen.desc': '区域提供隐蔽（20% 失手几率）；友军可正常视物。',
}
EN_FIX = {
    'rhodesdoctor.focusfire.desc': 'Mark one enemy: all allies gain a +1 insight bonus on attack and damage rolls against it, plus +1 more per 4 paladin levels (+6 at 20th). The mark lasts a number of rounds equal to your Intelligence modifier (minimum 1). Only one mark at a time. Usable 3 + Intelligence modifier times per day.',
    'rhodesdoctor.spell.HoldTheLine.desc': 'Targets gain a +2 insight bonus to AC and a +1 insight bonus on all saving throws.',
    'rhodesdoctor.spell.CavalryCharge.desc': 'Targets gain a +10-foot untyped bonus to speed (+20 feet at 10th level) and a +4 dodge bonus to AC against attacks of opportunity.',
    'rhodesdoctor.spell.SuppressingShot.desc': 'The target takes a –1 penalty on its next attack roll, or –2 if it attacks anyone but you this round.',
    'rhodesdoctor.spell.FrostPrayer.desc': 'The target takes a –2 penalty to AC and on all saving throws.',
    'rhodesdoctor.spell.Overloading.desc': 'The target gains one extra attack at its full base attack bonus on a full attack; a +1 untyped bonus on attack rolls; a +1 dodge bonus to AC and Reflex saves; and a +30-foot untyped bonus to all speeds. Every part stacks with haste.',
    'rhodesdoctor.spell.DelayedBlast.desc': 'You place a bomb on the ground: at the start of each round it explodes if an enemy is within 10 feet, dealing 2d6 fire damage and 2d6 bludgeoning damage in a 10-foot burst. You may also detonate it as a free action.',
    'rhodesdoctor.aura.desc': '半径 10 尺灵光：博士与灵光内的友军攻击检定与先攻检定获得 +1 胜任加值。博士失去意识或恶灵生效期间失效。此能力替换圣武士的施法能力。',
    'rhodesdoctor.total.desc': '指挥灵光扩大到 30 尺，加值提高到 +2。此能力替换正义灵光。',
    'rhodesdoctor.total.self.desc': '30 尺内的友军获得全军指令。',
    'rhodesdoctor.specter.lock.desc': '恶灵生效期间，博士只能施放【恶灵】指令；源石技艺精通失效且法术穿透倒扣；指挥灵光与全军指令失效。',
    'rhodesdoctor.spell.Sanctuary.desc': '目标对抗法术与类法术能力的豁免检定获得 +4 神圣加值；并获得火焰、寒冷、电击、酸蚀与音波抗性 10，可与其他来源叠加。',
    'rhodesdoctor.spell.Silencer.desc': '目标不能施展法术，也不能使用类法术能力或超自然能力；该效果不妨碍被动能力。',
    'rhodesdoctor.spell.Reconstruction.desc': '只复活友军（召唤物不复活，其余效果照常）；解除除诅咒、疾病、中毒以外的所有负面效果；阵亡者复活并恢复一半生命值，每次施法至多三名。',
    'rhodesdoctor.spell.Civilization.desc': '30 尺内的友军完全恢复生命值、解除除诅咒、疾病、中毒以外的所有负面效果，并复活（至多五名）；其后 1 分钟内，攻击检定、伤害、防御等级（AC）与豁免检定获得 +4 加值，且第一次将降至 0 生命值以下时改为保留 1 点。不清除剧情诅咒。',
    'rhodesdoctor.spell.EternalGaze.desc': '每个目标第一次将要死亡时不会死亡，改为恢复全部生命值、传送至你相邻空格，并立刻获得 1 分钟加速。每个目标仅触发一次。',
    'rhodesdoctor.testdummy.buff.haste.desc': 'AC 与反射豁免 +1（闪避），攻击检定 +1（不具类型）——近似加速术。可被驱散与偷取。',
    'rhodesdoctor.testdummy.buff.bless.desc': '攻击检定 +1（士气）——近似祝福术。可被驱散与偷取。',
    'rhodesdoctor.testdummy.buff.magearmor.desc': 'AC +4（护甲）——近似法师护甲。可被驱散与偷取。',
    'rhodesdoctor.testdummy.buff.shield.desc': 'AC +4（盾牌）——近似护盾术。可被驱散与偷取。',
    'rhodesdoctor.testdummy.buff.heroism.desc': '攻击检定与所有豁免检定 +2（士气）——近似英雄气概。可被驱散与偷取。',
    'rhodesdoctor.testdummy.buff.bearendurance.desc': '体质 +4（增强）——近似熊之坚韧。可被驱散与偷取。',
    'rhodesdoctor.spell.Smokescreen.desc': 'The area grants concealment (20% miss chance); allies see through it normally.',
    'rhodesdoctor.spell.FrozenDomain.desc': 'An enemy that enters the area or starts its turn inside takes 4d6 cold damage; on a failed Reflex save it is entangled for 1 round, and on a success its speed is halved for 1 round.',
    'rhodesdoctor.spell.ScorchingBreath.desc': 'Enemies in the line take 1d6 fire damage per caster level (maximum 10d6). A creature that fails its Reflex save loses 10 fire resistance for 1 round per caster level.',
    'rhodesdoctor.spell.AntiGravity.desc': 'The target is slowed, as the slow spell.',
    'rhodesdoctor.spell.Ura.desc': 'Ends shaken, frightened and panicked on the targets. Targets gain a +1 untyped bonus on attack and weapon damage rolls (+2 at 10th level).',
    'rhodesdoctor.spell.Flashbang.desc': 'A creature that fails its Reflex save is blinded for 1 round, then dazzled until the spell ends; on a success it is only dazzled for 1 round. An invisible creature that fails becomes visible for 1 round.',
    'rhodesdoctor.spell.ThunderousJudgment.desc': 'The target takes 12d6 electricity damage; a creature that fails its Reflex save falls prone and is dazed for 1 round, and on a success it takes half damage and does not fall prone.',
    'rhodesdoctor.spell.Nightterror.desc': 'A creature that fails its Will save is panicked for 2 rounds, then shaken until the spell ends; on a success it is shaken for 1 round.',
    'rhodesdoctor.spell.Volcano.desc': 'The target immediately takes 1d6 fire damage per 2 caster levels (maximum 10d6), then 1d6 fire damage per 4 caster levels (maximum 5d6) each round for 2 rounds. Each instance allows its own save.',
    'rhodesdoctor.spell.Twilight.desc': 'The target takes 1d6 fire damage per caster level (maximum 20d6). A creature that fails its Reflex save is set alight, taking 4d6 fire damage on each of the next 2 rounds; each instance allows a new Reflex save for half damage.',
    'rhodesdoctor.spell.BlackSnakeFlame.desc': 'The target takes 1d6 fire damage per caster level (maximum 20d6), half of which is holy damage unaffected by fire resistance; immunity to fire only halves the total. Regardless of the save, the target then takes 3d6 fire damage each round.',
    'rhodesdoctor.spell.BlazingSunBow.desc': 'Enemies in the line take 15d6 holy damage; a creature that fails its Reflex save is blinded for 1 round per caster level, and on a success it takes half damage and is dazzled for 1 round. Allies in the line regain 10d6 hit points and are cured of blindness.',
    'rhodesdoctor.spell.TidalVigil.desc': 'At the start of each round each target gains 15 temporary hit points (refreshing, not stacking) and is immune to charm and compulsion effects.',
}

ZH_RULES = [
    (r'施法者等级', '{g|Encyclopedia:Caster_Level}施法者等级{/g}'),
    (r'(强韧|反射|意志)豁免检定', r'{g|Encyclopedia:Saving_Throw}\1豁免检定{/g}'),
    (r'豁免检定', '{g|Encyclopedia:Saving_Throw}豁免检定{/g}'),
    (r'(强韧|反射|意志)豁免(?!检定)', r'{g|Encyclopedia:Saving_Throw}\1豁免{/g}'),
    (r'所有豁免(?!检定)', '{g|Encyclopedia:Saving_Throw}所有豁免{/g}'),
    (r'攻击检定', '{g|Encyclopedia:Attack}攻击检定{/g}'),
    (r'防御等级（AC）', '{g|Encyclopedia:Armor_Class}防御等级（AC）{/g}'),
    (r'技能检定', '{g|Encyclopedia:Skills}技能检定{/g}'),
    (r'属性检定', '{g|Encyclopedia:Ability_Scores}属性检定{/g}'),
    (r'先攻检定', '{g|Encyclopedia:Initiative}先攻检定{/g}'),
    (r'生命值', '{g|Encyclopedia:HP}生命值{/g}'),
    (r'伤害减免', '{g|Encyclopedia:Damage_Reduction}伤害减免{/g}'),
    (r'法术抗力', '{g|Encyclopedia:Spell_Resistance}法术抗力{/g}'),
    (r'借机攻击', '{g|Encyclopedia:Attack_Of_Opportunity}借机攻击{/g}'),
    (r'接触攻击', '{g|Encyclopedia:TouchAttack}接触攻击{/g}'),
    (r'临时生命', '{g|Encyclopedia:Temporary_HP}临时生命{/g}'),
    (r'(\d+d\d+)', r'{g|Encyclopedia:Dice}\1{/g}'),
    (r'(钝击|穿刺|挥砍)伤害', r'{g|Encyclopedia:Damage_Type}\1伤害{/g}'),
    (r'(火焰|寒冷|电击|酸蚀|音波|力场|神圣)伤害', r'{g|Encyclopedia:Energy_Damage}\1伤害{/g}'),
    (r'整轮动作', '{g|Encyclopedia:Full_Round_Action}整轮动作{/g}'),
    (r'标准动作', '{g|Encyclopedia:Standard_Actions}标准动作{/g}'),
    (r'迅捷动作', '{g|Encyclopedia:Swift_Action}迅捷动作{/g}'),
    (r'自由动作', '{g|Encyclopedia:Free_Action}自由动作{/g}'),
    (r'移动动作', '{g|Encyclopedia:Move_Action}移动动作{/g}'),
    (r'恍惚', '{g|ConditionStaggered}恍惚{/g}'),
    (r'目盲', '{g|ConditionBlind}目盲{/g}'),
    (r'目眩', '{g|ConditionDazzled}目眩{/g}'),
    (r'麻痹', '{g|ConditionParalyzed}麻痹{/g}'),
    (r'战栗', '{g|ConditionShaken}战栗{/g}'),
    (r'惊恐', '{g|ConditionFrightened}惊恐{/g}'),
    (r'震慑', '{g|ConditionStunned}震慑{/g}'),
    (r'反胃', '{g|ConditionNauseated}反胃{/g}'),
    (r'恶心', '{g|ConditionSickened}恶心{/g}'),
    (r'纠缠', '{g|ConditionEntangled}纠缠{/g}'),
    (r'眩晕', '{g|ConditionDaze}眩晕{/g}'),
    (r'隐形', '{g|ConditionInvisible}隐形{/g}'),
    (r'倒地', '{g|ConditionProne}倒地{/g}'),
    (r'缓慢', '{g|ConditionSlowed}缓慢{/g}'),
    (r'治疗', '{g|Encyclopedia:Healing}治疗{/g}'),
    (r'减值', '{g|Encyclopedia:Penalty}减值{/g}'),
    (r'加值', '{g|Encyclopedia:Bonus}加值{/g}'),
    (r'重击', '{g|Encyclopedia:Critical}重击{/g}'),
    (r'隐蔽', '{g|Encyclopedia:Concealment}隐蔽{/g}'),
]
EN_RULES = [
    (r'caster level', '{g|Encyclopedia:Caster_Level}caster level{/g}'),
    (r'(Fortitude|Reflex|Will) saving throws', r'{g|Encyclopedia:Saving_Throw}\1 saving throws{/g}'),
    (r'saving throws', '{g|Encyclopedia:Saving_Throw}saving throws{/g}'),
    (r'(Fortitude|Reflex|Will) saving throw', r'{g|Encyclopedia:Saving_Throw}\1 saving throw{/g}'),
    (r'saving throw', '{g|Encyclopedia:Saving_Throw}saving throw{/g}'),
    (r'attack rolls', '{g|Encyclopedia:Attack}attack rolls{/g}'),
    (r'attack roll', '{g|Encyclopedia:Attack}attack roll{/g}'),
    (r'\bAC\b', '{g|Encyclopedia:Armor_Class}AC{/g}'),
    (r'hit points', '{g|Encyclopedia:HP}hit points{/g}'),
    (r'\bhp\b', '{g|Encyclopedia:HP}hit points{/g}'),
    (r'damage reduction', '{g|Encyclopedia:Damage_Reduction}damage reduction{/g}'),
    (r'spell resistance', '{g|Encyclopedia:Spell_Resistance}spell resistance{/g}'),
    (r'attacks of opportunity', '{g|Encyclopedia:Attack_Of_Opportunity}attacks of opportunity{/g}'),
    (r'attack of opportunity', '{g|Encyclopedia:Attack_Of_Opportunity}attack of opportunity{/g}'),
    (r'touch attacks', '{g|Encyclopedia:TouchAttack}touch attacks{/g}'),
    (r'touch attack', '{g|Encyclopedia:TouchAttack}touch attack{/g}'),
    (r'initiative checks', '{g|Encyclopedia:Initiative}initiative checks{/g}'),
    (r'initiative check', '{g|Encyclopedia:Initiative}initiative check{/g}'),
    (r'temporary hit points', '{g|Encyclopedia:Temporary_HP}temporary hit points{/g}'),
    (r'\b(\d+d\d+)\b', r'{g|Encyclopedia:Dice}\1{/g}'),
    (r'(bludgeoning|piercing|slashing) damage', r'{g|Encyclopedia:Damage_Type}\1 damage{/g}'),
    (r'(fire|cold|electricity|acid|sonic|force|holy|divine) damage', r'{g|Encyclopedia:Energy_Damage}\1 damage{/g}'),
    (r'free action', '{g|Encyclopedia:Free_Action}free action{/g}'),
    (r'swift action', '{g|Encyclopedia:Swift_Action}swift action{/g}'),
    (r'full-round action', '{g|Encyclopedia:Full_Round_Action}full-round action{/g}'),
    (r'blinded', '{g|ConditionBlind}blinded{/g}'),
    (r'dazzled', '{g|ConditionDazzled}dazzled{/g}'),
    (r'paralyzed', '{g|ConditionParalyzed}paralyzed{/g}'),
    (r'staggered', '{g|ConditionStaggered}staggered{/g}'),
    (r'shaken', '{g|ConditionShaken}shaken{/g}'),
    (r'frightened', '{g|ConditionFrightened}frightened{/g}'),
    (r'panicked', '{g|ConditionFrightened}panicked{/g}'),
    (r'stunned', '{g|ConditionStunned}stunned{/g}'),
    (r'nauseated', '{g|ConditionNauseated}nauseated{/g}'),
    (r'entangled', '{g|ConditionEntangled}entangled{/g}'),
    (r'prone', '{g|ConditionProne}prone{/g}'),
    (r'invisible', '{g|ConditionInvisible}invisible{/g}'),
    (r'slowed', '{g|ConditionSlowed}slowed{/g}'),
    (r'bonuses', '{g|Encyclopedia:Bonus}bonuses{/g}'),
    (r'bonus', '{g|Encyclopedia:Bonus}bonus{/g}'),
    (r'penalties', '{g|Encyclopedia:Penalty}penalties{/g}'),
    (r'penalty', '{g|Encyclopedia:Penalty}penalty{/g}'),
]


def link(text, rules):
    """单遍替换：每次命中先换成占位符，之后的规则不会再匹配到已插入的标记。"""
    if not text:
        return text
    store = []

    def make(rep):
        def f(m):
            store.append(m.expand(rep))
            return '\x00%d\x01' % (len(store) - 1)
        return f

    out = text
    for pat, rep in rules:
        out = re.sub(pat, make(rep), out)
    return re.sub(r'\x00(\d+)\x01', lambda m: store[int(m.group(1))], out)


def main():
    path = 'LocalizedStrings.json'
    data = json.load(open(path, encoding='utf-8-sig'))
    n = 0
    for e in data:
        if not e['Key'].endswith('.desc'):
            continue
        z = ZH_FIX.get(e['Key']) or zh_clean(e['zhCN'])
        en = EN_FIX.get(e['Key']) or en_clean(e.get('enGB') or '')
        e['zhCN'] = link(z, ZH_RULES)
        e['enGB'] = link(en, EN_RULES)
        n += 1
    json.dump(data, open(path, 'w', encoding='utf-8'), ensure_ascii=False, indent=2)
    print('已改写', n, '条描述')


main()
