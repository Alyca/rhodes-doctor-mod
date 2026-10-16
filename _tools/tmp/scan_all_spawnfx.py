import zipfile, json
z = zipfile.ZipFile(r'D:\steam\steamapps\common\Pathfinder Second Adventure\blueprints.zip')

# 1) 所有带 AbilitySpawnFx 的法术（不限关键词），列出素材 AssetId 去重
print("=== 所有带 AbilitySpawnFx 的法术素材（去重） ===")
seen=set()
for n in z.namelist():
    if not n.lower().endswith('.jbp'): continue
    try:
        j=json.loads(z.read(n).decode('utf-8-sig'))
    except: continue
    d=j.get('Data') or {}
    t=d.get('$type') or ''
    if 'BlueprintAbility' not in t: continue
    for c in d.get('Components') or []:
        ct=c.get('$type') or ''
        if 'AbilitySpawnFx' in ct:
            fx=c.get('PrefabLink') or c.get('m_PrefabLink')
            if isinstance(fx, dict):
                aid=fx.get('AssetId')
                if aid and aid not in seen:
                    seen.add(aid)
                    print(aid, '|', n)
print('total unique AbilitySpawnFx assets:', len(seen))
