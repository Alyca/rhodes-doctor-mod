import zipfile, json
z = zipfile.ZipFile(r'D:\steam\steamapps\common\Pathfinder Second Adventure\blueprints.zip')
TARGET='ae7ac3acbb6b9574ab7f01bfaed77d27'
# 1) 打印 PillarOfLife 完整蓝图
n='World/Encounters/ColyphyrDungeon/Buffs/Colyphyr_PillarOfLife.jbp'
j=json.loads(z.read(n).decode('utf-8-sig'))
print('=== PillarOfLife 完整蓝图 ===')
print(json.dumps(j, indent=1)[:3000])
# 2) 搜索所有引用该 AssetId 的蓝图
print('\n=== 引用该 fx AssetId 的蓝图 ===')
for name in z.namelist():
    if not name.lower().endswith('.jbp'): continue
    try:
        raw=z.read(name).decode('utf-8-sig')
    except: continue
    if TARGET in raw:
        try:
            jj=json.loads(raw)
            nm=jj.get('Data') or {}
            nm2=nm.get('Name') or ''
            if isinstance(nm2,dict): nm2=nm2.get('en') or ''
            print(name, '|', nm2)
        except: print(name)
