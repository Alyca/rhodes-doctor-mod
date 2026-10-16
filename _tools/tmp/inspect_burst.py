import zipfile, json, sys

Z = r'D:\steam\steamapps\common\Pathfinder Second Adventure\blueprints.zip'
z = zipfile.ZipFile(Z)
names = z.namelist()

TARGETS = sys.argv[1:] or ['AngelHolyHymn.jbp', 'Resurrection.jbp']

def walk(a, out):
    if isinstance(a, dict):
        t = a.get('$type', '')
        if 'SpawnFx' in t:
            out.append(a)
        for v in a.values():
            walk(v, out)
    elif isinstance(a, list):
        for v in a:
            walk(v, out)

for n in names:
    if not any(n.endswith(t) for t in TARGETS):
        continue
    j = json.loads(z.read(n).decode('utf-8-sig'))
    d = j.get('Data') or {}
    print('===', n, '| guid', j.get('AssetId'))
    out = []
    walk(d, out)
    for c in out:
        print('   ', json.dumps(c, ensure_ascii=False)[:700])
