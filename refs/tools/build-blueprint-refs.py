#!/usr/bin/env python3
# 从游戏 blueprints.zip 抽取本 mod 引用的原版蓝图，供没有游戏本体的审阅方对照。
# 用法: python build-blueprint-refs.py [游戏根目录]
import os, re, sys, json, zipfile, gzip, glob

HERE = os.path.dirname(os.path.abspath(__file__))
REF  = os.path.dirname(HERE)                       # refs/
ROOT = os.path.dirname(REF)                        # 模组根
GAME = sys.argv[1] if len(sys.argv) > 1 else r"D:\steam\steamapps\common\Pathfinder Second Adventure"
ZIP  = os.path.join(GAME, "blueprints.zip")
IDX  = os.path.join(HERE, "bp-index.tsv.gz")
OUT  = os.path.join(REF, "blueprints")
os.makedirs(OUT, exist_ok=True)

# 1. 收集源码里出现的 32 位 GUID，以及 Guids.cs 里的常量名
guid_to_name = {}
name_to_guid = {}
pat_const = re.compile(r'internal\s+const\s+string\s+(\w+)\s*=\s*"([0-9a-fA-F]{32})"')
pat_guid  = re.compile(r'"([0-9a-fA-F]{32})"')
for cs in glob.glob(os.path.join(ROOT, "src", "*.cs")) + [os.path.join(ROOT, "src", "Guids.cs")]:
    txt = open(cs, encoding="utf-8", errors="replace").read()
    for name, g in pat_const.findall(txt):
        name_to_guid[name] = g
    for g in pat_guid.findall(txt):
        guid_to_name.setdefault(g.lower(), None)
for name, g in name_to_guid.items():
    guid_to_name[g.lower()] = name
want = set(guid_to_name)

# 2. 建 / 读 GUID -> 路径 索引
index = {}
if os.path.exists(IDX):
    with gzip.open(IDX, "rt", encoding="utf-8") as f:
        for line in f:
            g, p = line.rstrip("\n").split("\t", 1)
            index[g] = p
else:
    print("[*] 首次运行：扫描 blueprints.zip 建索引（约 25 万条，数分钟）…")
    z = zipfile.ZipFile(ZIP)
    names = z.namelist()
    asset_re = re.compile(rb'"AssetId"\s*:\s*"([0-9a-fA-F]{32})"')
    for i, n in enumerate(names):
        try:
            head = z.read(n)[:400] if False else z.open(n).read(400)
        except Exception:
            continue
        m = asset_re.search(head)
        if m:
            index[m.group(1).decode().lower()] = n
        if i % 50000 == 0:
            print(f"    {i}/{len(names)} … 已索引 {len(index)}")
    z.close()
    with gzip.open(IDX, "wt", encoding="utf-8") as f:
        for g, p in index.items():
            f.write(f"{g}\t{p}\n")
    print(f"[*] 索引完成：{len(index)} 条 -> {IDX}")

# 3. 抽取命中的蓝图
z = zipfile.ZipFile(ZIP)
manifest, miss = [], []
for g in sorted(want):
    path = index.get(g)
    if not path:
        miss.append(g); continue
    label = guid_to_name.get(g) or os.path.splitext(os.path.basename(path))[0]
    safe = re.sub(r'[^0-9A-Za-z_]+', '_', label).strip('_') or g[:8]
    fn = f"{safe}__{g[:8]}.json"
    raw = z.read(path).decode("utf-8-sig")
    obj = json.loads(raw)
    with open(os.path.join(OUT, fn), "w", encoding="utf-8") as f:
        json.dump({"AssetId": obj.get("AssetId"),
                   "SourcePath": path,
                   "Data": obj.get("Data")}, f, ensure_ascii=False, indent=2)
    manifest.append((label, g, path, fn))
z.close()

with open(os.path.join(OUT, "index.md"), "w", encoding="utf-8") as f:
    f.write("# 原版蓝图参考（从 blueprints.zip 抽取）\n\n")
    f.write("每行：常量名 / GUID / 游戏内路径 / 本目录文件。字段全在 JSON 的 `Data` 下。\n\n")
    f.write("| 常量名 | GUID | 路径 | 文件 |\n|---|---|---|---|\n")
    for label, g, p, fn in manifest:
        f.write(f"| `{label}` | `{g}` | `{p}` | `{fn}` |\n")
    if miss:
        f.write("\n## 未在 blueprints.zip 找到（应为模组自有 GUID，可忽略）\n\n")
        for g in miss:
            f.write(f"- `{guid_to_name.get(g)}` = `{g}`\n")

print(f"[*] 抽取 {len(manifest)} 个，未命中 {len(miss)} 个 -> {OUT}")
