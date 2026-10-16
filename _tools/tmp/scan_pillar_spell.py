import zipfile, json
z = zipfile.ZipFile(r'D:\steam\steamapps\common\Pathfinder Second Adventure\blueprints.zip')
# 找 PillarOfLife 法术
for n in z.namelist():
    if not n.lower().endswith('.jbp'): continue
    if 'pillar' not in n.lower(): continue
    try:
        j=json.loads(z.read(n).decode('utf-8-sig'))
    except: continue
    d=j.get('Data') or {}
    t=d.get('$type') or ''
    nm=d.get('Name') or ''
    if isinstance(nm,dict): nm=nm.get('en') or ''
    print('===', n)
    print('  type:', t.split(',')[-1])
    print('  name:', nm)
    print('  AssetId:', j.get('AssetId'))
