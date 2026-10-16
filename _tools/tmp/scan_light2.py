import zipfile, json
z = zipfile.ZipFile(r'D:\steam\steamapps\common\Pathfinder Second Adventure\blueprints.zip')
# 光柱/光束/圣光 关键词
keys = ['beam','ray','radiance','sunbeam','sunburst','pillar','column','light','holy','divine','glow','beacon','flare','shining','brilliant','radiant','luminous','sunlight','dawn','halo','nimbus','sanctuary','bless']
rows=[]
for n in z.namelist():
    if not n.lower().endswith('.jbp'): continue
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
        aid=fx.get('AssetId') if isinstance(fx,dict) else str(fx)
        rows.append((nm, aid, n))
print('light-keyword abilities with AbilitySpawnFx:', len(rows))
for nm,aid,n in rows:
    print(f'{nm} | {aid} | {n}')
