# 离线扫描 blueprints.zip：列出带 AbilitySpawnFx 的法术（可按关键字过滤）。
# 用法：python _tools/tmp/scan_spawnfx_kw.py [关键字...]
# 输出：路径 | AbilitySpawnFx的AssetId | Anchor | Time | 蓝图Name
# 目的：给单体批次选"施法爆发"素材（见 docs/dev-notes.md §7.11.1 / fx-status.md 第三节）。
import zipfile, json, sys

Z = r"D:\steam\steamapps\common\Pathfinder Second Adventure\blueprints.zip"
KEYS = [k.lower() for k in sys.argv[1:]]

z = zipfile.ZipFile(Z)
rows = []
for n in z.namelist():
    if not n.lower().endswith(".jbp"):
        continue
    try:
        j = json.loads(z.read(n).decode("utf-8-sig"))
    except Exception:
        continue
    d = j.get("Data") or {}
    t = d.get("$type") or ""
    if "BlueprintAbility" not in t:
        continue
    for c in d.get("Components") or []:
        ct = c.get("$type") or ""
        if "AbilitySpawnFx" not in ct:
            continue
        fx = c.get("PrefabLink") or c.get("m_PrefabLink")
        aid = fx.get("AssetId") if isinstance(fx, dict) else None
        rows.append((n, str(aid), str(c.get("Anchor")), str(c.get("Time")), d.get("Name") or ""))

print("abilities w/ AbilitySpawnFx:", len(rows))
low = lambda s: (s or "").lower()
for n, aid, anc, tm, nm in sorted(rows):
    if KEYS and not any(k in low(n) or k in low(nm) for k in KEYS):
        continue
    print(f"{n}\t{aid}\t{anc}\t{tm}\t{nm}")
