# 离线扫描 blueprints.zip：按关键字列出所有 .jbp 路径，并标注是否有 AbilitySpawnFx（含锚点）。
# 用法：python _tools/tmp/scan_paths_kw.py [关键字...]
import zipfile, json, sys

Z = r"D:\steam\steamapps\common\Pathfinder Second Adventure\blueprints.zip"
KEYS = [k.lower() for k in sys.argv[1:]]

z = zipfile.ZipFile(Z)
names = z.namelist()

def spawnfx(n):
    try:
        j = json.loads(z.read(n).decode("utf-8-sig"))
    except Exception:
        return None
    d = j.get("Data") or {}
    out = []
    for c in d.get("Components") or []:
        if "AbilitySpawnFx" in (c.get("$type") or ""):
            fx = c.get("PrefabLink") or c.get("m_PrefabLink")
            aid = fx.get("AssetId") if isinstance(fx, dict) else None
            out.append((aid, c.get("Anchor"), c.get("Time")))
    return out

hits = [n for n in names if n.lower().endswith(".jbp")
        and any(k in n.lower() for k in KEYS)]
print("path hits:", len(hits))
for n in sorted(hits):
    fx = spawnfx(n)
    if fx:
        print(f"[FX] {n}")
        for aid, anc, tm in fx:
            print(f"      {aid}  {anc}  {tm}")
    else:
        print(f"[--] {n}")
