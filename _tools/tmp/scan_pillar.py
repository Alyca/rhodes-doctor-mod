import zipfile, json
z = zipfile.ZipFile(r'D:\steam\steamapps\common\Pathfinder Second Adventure\blueprints.zip')
# 搜索所有名字含 pillar/column/obelisk/totem 的蓝图（任意类型），列出 FxOnStart / Fx / AbilitySpawnFx
keys = ['pillar','column','obelisk','totem','monolith','shrine','statue','crystal','beacon','lighthouse']
rows=[]
for n in z.namelist():
    if not n.lower().endswith('.jbp'): continue
    try:
        j=json.loads(z.read(n).decode('utf-8-sig'))
    except: continue
    d=j.get('Data') or {}
    nm=d.get('Name') or ''
    if isinstance(nm, dict): nm=nm.get('en') or nm.get('English') or ''
    nm=str(nm)
    low=nm.lower()
    if not any(k in low for k in keys): continue
    t=d.get('$type') or ''
    # 收集各种 fx 字段
    fxinfo=[]
    for field in ['FxOnStart','Fx','AbilitySpawnFx']:
        v=d.get(field)
        if isinstance(v,dict):
            aid=v.get('AssetId')
            if aid: fxinfo.append(f'{field}={aid}')
    for c in d.get('Components') or []:
        ct=c.get('$type') or ''
        if 'AbilitySpawnFx' in ct:
            fx=c.get('PrefabLink') or c.get('m_PrefabLink')
            if isinstance(fx,dict) and fx.get('AssetId'):
                fxinfo.append('AbilitySpawnFx='+fx['AssetId'])
    if fxinfo:
        print(f'{nm} | {t.split(",")[-1]} | {", ".join(fxinfo)} | {n}')