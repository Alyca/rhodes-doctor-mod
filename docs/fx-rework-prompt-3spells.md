# 三个法术的特效素材选错了，请改

项目：`D:\SteamLibrary\Mods\rhodes-doctor-mod`（《开拓者：正义之怒》mod，以《明日方舟》角色 / 技能为原型）
游戏本体：`D:\steam\steamapps\common\Pathfinder Second Adventure`

只读环境，请直接输出改好的代码。

---

## 1. 牵制射击 `SuppressingShot`（0 环，克洛丝的弩箭）

现在接线：`src/SpellsSetup.cs:2064` → `FxLibrary.Psychic`
借的是迷梦 / 弱智术那一套**精神系紫圈范围爆发**（`src/FxLibrary.cs:256`）。

两个问题：
- 它和 6 环的**醉梦**（`src/SpellsSetup.cs:2047`）用了**同一份素材**，两发法术在画面上分不出来。
- 一团范围紫圈根本不是"压制射击"该有的样子。

---

## 2. 罪罚 `SinPunishment`（6 环，斥罪）

现在接线：`src/FxLibrary.cs:1013` → `CurseAura()`
借的是**降咒·爆裂**（`ArmyBestowCurseBlastBuff`）那一套诅咒光环。

斥罪应该是**法官天平 / 律法 / 正义惩击**的感觉，现在像死灵法师在施法。

---

## 3. 王冠的回响 `CrownEcho`（10 环，黑冠 · 特雷西娅）

现在接线：`src/SpellsSetup.cs:2051` → `FxLibrary.Shadow`
借的是**幻影杀手**的黑紫鬼影。

完全脱离了黑冠的**精神织网**概念。
