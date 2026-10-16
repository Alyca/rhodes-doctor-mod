# 全库扫描：所有"名字里带 buff"的蓝图里，哪些是 BlueprintBuff 且 FxOnStart 非 null。
# 输出：唯一素材(GUID) + 一个代表蓝图的路径 + 蓝图名 + 蓝图 GUID（后者就是 FxBorrow.BuffAura 要的参数）。
# 用法：python tools/scan_buff_fx_all.py [关键字...]
import zipfile, json, sys

Z = r"D:\steam\steamapps\common\Pathfinder Second Adventure\blueprints.zip"
KEYS = [k.lower() for k in sys.argv[1:]]

z = zipfile.ZipFile(Z)
names = [n for n in z.namelist() if n.lower().endswith(".jbp") and "buff" in n.lower()]
print("candidates:", len(names))

rows, seen, bad, typefail = [], {}, 0, 0
for n in names:
    try:
        j = json.loads(z.read(n).decode("utf-8-sig"))
    except Exception:
        bad += 1
        continue
    d = j.get("Data") or {}
    t = d.get("$type") or ""
    if "BlueprintBuff" not in t.split(",")[-1]:
        typefail += 1
        continue
    fx = d.get("FxOnStart")
    if not fx:
        continue
    aid = fx.get("AssetId") if isinstance(fx, dict) else str(fx)
    if aid in seen:
        continue
    seen[aid] = n
    rows.append((n, aid, j.get("AssetId") or "", d.get("Name") or ""))

print("BlueprintBuff w/ FxOnStart (unique assets):", len(rows),
      " non-buff skipped:", typefail, " parse-fail:", bad)
print("---- path | fxAsset | bpGuid | Name ----")
for n, aid, g, nm in sorted(rows):
    low = n.lower()
    if KEYS and not any(k in low for k in KEYS):
        continue
    print(f"{n}\t{aid}\t{g}\t{nm}")
