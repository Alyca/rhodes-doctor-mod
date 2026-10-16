# 离线扫描 blueprints.zip：找出"能挂在增益上的循环光"（buff 的 FxOnStart 非 null）。
# 用法：python tools/scan_buff_fx.py [关键字...]
# 目的：D 类（11 个友军增益光环）要借一个原版 buff 的 FxOnStart。原版很多 buff 这个字段是空的，
#       先离线筛出来，比进游戏一个个试快得多（见 docs/dev-notes.md §0 / §7.1）。
import zipfile, json, sys, re

Z = r"D:\steam\steamapps\common\Pathfinder Second Adventure\blueprints.zip"
KEYS = [k.lower() for k in (sys.argv[1:] or [
    "angel", "sacred", "holy", "divine", "bless", "aura", "glory", "radian",
    "nimbus", "halo", "heroism", "prayer", "archon", "celestial", "light",
    "sun", "gold", "sanct", "faith", "zeal", "courage", "consecrat", "dawn",
    "empyreal", "aegis", "ward", "shield", "grace", "anoint",
])]

z = zipfile.ZipFile(Z)
names = z.namelist()
buffs = [n for n in names if n.lower().startswith("buffs/") and n.lower().endswith(".jbp")]
print("total entries:", len(names), " buff jbp:", len(buffs))

hits, withfx, bad, seen = [], 0, 0, set()
for n in buffs:
    try:
        d = json.loads(z.read(n).decode("utf-8-sig")).get("Data", {})
    except Exception:
        bad += 1
        continue
    fx = d.get("FxOnStart")
    if not fx:
        continue
    withfx += 1
    # fx 可能是字符串 GUID，也可能是对象
    if isinstance(fx, dict):
        aid = fx.get("AssetId") or fx.get("guid") or json.dumps(fx, ensure_ascii=False)
    else:
        aid = str(fx)
    if aid in seen:            # 同一素材被多个 buff 共用，只报一次
        continue
    seen.add(aid)
    low = n.lower()
    if any(k in low for k in KEYS):
        hits.append((n, aid, d.get("Name") or ""))

print("buff with FxOnStart:", withfx, " unique assets:", len(seen), " parse-fail:", bad)
print("---- keyword hits (path | assetId | Name) ----")
for n, aid, nm in sorted(hits):
    print(f"{n}\t{aid}\t{nm}")
