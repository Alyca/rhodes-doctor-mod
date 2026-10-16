import zipfile, json
z = zipfile.ZipFile(r'D:\steam\steamapps\common\Pathfinder Second Adventure\blueprints.zip')
# 搜索所有 BlueprintAbilityAreaEffect 的 Fx，名字含光/柱/beam/light 等
keys = ['beam','ray','radiance','sunbeam','sunburst','pillar','column','light','holy','divine','glow','beacon','flare','shining','radiant','luminous','sunlight','dawn','halo','nimbus','sanctuary','bless','aura','circle','rune','sigil']
rows=[]
for n in z.namelist():
    if not n.lower().endswith('.jbp'): continue
    try:
        j=json.loads(z.read(n).decode('utf-8-sig'))
    except: continue
    d=j.get('Data') or {}
    t=d.get('$type') or ''
    if 'BlueprintAbilityAreaEffect' not in t: continue
    fx=d.get('Fx')
    if not isinstance(fx,dict) or not fx.get('AssetId'): continue
    nm=d.get('Name') or ''
    if isinstance(nm,dict): nm=nm.get('en') or ''
    nm=str(nm)
    low=nm.lower()
    if not any(k in low for k in keys): continue
    shape=d.get('m_Shape') or d.get('Shape')
    size=d.get('m_Size') or d.get('Size')
    rows.append((nm, fx['AssetId'], shape, size, n))
print('AreaEffects with light-keyword Fx:', len(rows))
for nm,aid,shape,size,n in rows:
    print(f'{nm} | fx={aid} | shape={shape} | size={size} | {n}')
