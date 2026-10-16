import zipfile, json
z = zipfile.ZipFile(r'D:\steam\steamapps\common\Pathfinder Second Adventure\blueprints.zip')
n='Spells/Level5/PillarOfLife.jbp'
j=json.loads(z.read(n).decode('utf-8-sig'))
d=j.get('Data') or {}
print('=== PillarOfLife 法术 ===')
print('AssetId:', j.get('AssetId'))
print('Name:', d.get('m_DisplayName'))
print('Description:', d.get('m_Description'))
print('Components:')
for c in d.get('Components') or []:
    print('  ', c.get('$type'))
