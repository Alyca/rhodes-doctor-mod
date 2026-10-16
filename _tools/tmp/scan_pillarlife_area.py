import zipfile, json
z = zipfile.ZipFile(r'D:\steam\steamapps\common\Pathfinder Second Adventure\blueprints.zip')
n='Spells/Level5/PillarOfLifeaArea.jbp'
j=json.loads(z.read(n).decode('utf-8-sig'))
d=j.get('Data') or {}
print('=== PillarOfLifeaArea ===')
print('AssetId:', j.get('AssetId'))
print('$type:', d.get('$type'))
print('Shape:', d.get('m_Shape') or d.get('Shape'))
print('Size:', d.get('m_Size') or d.get('Size'))
print('Fx:', d.get('Fx'))
print('FxOnStart:', d.get('FxOnStart'))
# 打印组件
print('Components:')
for c in d.get('Components') or []:
    print('  ', c.get('$type'))
