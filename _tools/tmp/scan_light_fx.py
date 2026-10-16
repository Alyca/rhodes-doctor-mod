import zipfile, json
z = zipfile.ZipFile(r'D:\steam\steamapps\common\Pathfinder Second Adventure\blueprints.zip')
keys = ['beam','ray','radiance','sunbeam','sunburst','pillar','column','light','holy','divine','glow','beacon','flare','shining','brilliant']
names = [n for n in z.namelist() if n.lower().endswith('.jbp') and ('/spells/' in n.lower() or n.lower().startswith('spells/'))]
print('spell candidates:', len(names))
rows=[]
for n in names:
    try:
        j=json.loads(z.read(n).decode('utf-8-sig'))
    except: continue
    d=j.get('Data') or {}
    t=d.get('$type') or ''
    if 'BlueprintAbility' not in t: continue
    nm=(d.get('Name') or '')
    low=nm.lower()
    if not any(k in low for k in keys): continue
    fx=None
    for c in d.get('Components') or []:
        ct=c.get('$type') or ''
        if 'AbilitySpawnFx' in ct:
            fx=c.get('PrefabLink') or c.get('m_PrefabLink')
            break
    if fx:
        rows.append((nm, fx, n))
print('abilities with AbilitySpawnFx + light keyword:', len(rows))
for nm,fx,n in rows:
    print(f'{nm} | {fx} | {n}')
