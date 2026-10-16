# 查"模组用到的汉字"是否在游戏的中文字体里（游戏字体是静态图集，没烘焙进去的字渲染不出来）。
# 用法：python _tools/tmp/check_font_glyphs.py [要额外核对的字...]
#   ① 比对 模组 LocalizedStrings.json 的 zhCN 用字 vs 游戏 zhCN.json 用字；
#   ② 用 UnityPy 读 Bundles/ui_china_font 里两个 TMP FontAsset 的字符表（权威）。
# 依赖：UnityPy（装在 .workbuddy-ai 的 default venv 里）。
import json, os, sys
try:
    import UnityPy
except ImportError:
    UnityPy = None

GAME = r"D:\steam\steamapps\common\Pathfinder Second Adventure"
MOD = r"D:\SteamLibrary\Mods\rhodes-doctor-mod"

game_txt = open(os.path.join(GAME, r"Wrath_Data\StreamingAssets\Localization\zhCN.json"), encoding="utf-8").read()
mod = json.load(open(os.path.join(MOD, "LocalizedStrings.json"), encoding="utf-8-sig"))

missing = {}
for e in mod:
    for ch in (e.get("zhCN") or ""):
        if "\u4e00" <= ch <= "\u9fff" and ch not in game_txt:
            missing.setdefault(ch, []).append(e.get("Key"))
print("模组里用到、但游戏 zhCN.json 里没有的汉字：")
for ch, keys in sorted(missing.items()):
    print(f"  {ch} (U+{ord(ch):04X})  出现于 {len(keys)} 条，例：{keys[0]}")

if UnityPy is None:
    print("\n(未安装 UnityPy，跳过字体包核对)")
    sys.exit()

env = UnityPy.load(os.path.join(GAME, r"Bundles\ui_china_font"))
fonts = {}
for obj in env.objects:
    if obj.type.name != "MonoBehaviour":
        continue
    tt = obj.read_typetree()
    name = tt.get("m_Name")
    codes = {int(c["m_Unicode"]) for c in (tt.get("m_CharacterTable") or []) if c.get("m_Unicode") is not None}
    if codes:
        fonts[name] = codes
print("\n字体：" + " / ".join(f"{n}({len(c)} 字)" for n, c in fonts.items()))

targets = list(missing) + sys.argv[1:] + ["归", "潮", "黎", "汐", "海", "澜"]
seen = set()
print("\n逐个核对（在任一字体里 = 可渲染）：")
for ch in targets:
    if ch in seen:
        continue
    seen.add(ch)
    where = [n for n, c in fonts.items() if ord(ch) in c]
    print(f"  {ch} (U+{ord(ch):04X})  {'OK ' + '/'.join(where) if where else '缺（两个字体都没有，渲染不出来）'}")
