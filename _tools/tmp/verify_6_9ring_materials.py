# 校验本轮（6–9 环单体批次）选用的原版素材确实带 FX：按路径关键字定位蓝图，打印锚点/时间/AssetId。
# 用法：python _tools/tmp/verify_6_9ring_materials.py
import zipfile, json

Z = r"D:\steam\steamapps\common\Pathfinder Second Adventure\blueprints.zip"
z = zipfile.ZipFile(Z)
names = [n for n in z.namelist() if n.lower().endswith(".jbp")]


def read(path):
    try:
        return json.loads(z.read(path).decode("utf-8-sig"))
    except Exception:
        return None


def find_paths(*keys):
    return [n for n in names if all(k.lower() in n.lower() for k in keys)]


def report_ability(label, *keys):
    hits = find_paths(*keys)
    if not hits:
        print(f"[找不到] {label}  keys={keys}")
        return
    for n in hits[:2]:
        d = (read(n) or {}).get("Data") or {}
        fx = []
        for c in d.get("Components") or []:
            if "AbilitySpawnFx" in (c.get("$type") or ""):
                p = c.get("PrefabLink") or c.get("m_PrefabLink") or {}
                fx.append((p.get("AssetId"), c.get("Anchor"), c.get("PositionAnchor"), c.get("Time")))
        tag = "OK" if fx else "无FX"
        print(f"[{tag}] {label}  {n}")
        for a, anc, pos, tm in fx:
            print(f"        fx={a}  Anchor={anc}  PositionAnchor={pos}  Time={tm}")


def report_buff(label, *keys):
    hits = find_paths(*keys)
    if not hits:
        print(f"[找不到] {label}  keys={keys}")
        return
    for n in hits[:2]:
        d = (read(n) or {}).get("Data") or {}
        fx = d.get("FxOnStart")
        aid = fx.get("AssetId") if isinstance(fx, dict) else fx
        print(f"[{'OK' if aid else '无FX'}] {label}  {n}  FxOnStart={aid}")


print("===== 爆发素材（AbilitySpawnFx）=====")
report_ability("影霄·拔刀 → FingerOfDeath", "Level7/FingerOfDeath.jbp")
report_ability("影霄·拔刀 备选 SlayLiving", "Level5/SlayLiving.jbp")
report_ability("黑蛇之焰 → FlameStrike（Fire 元素首选）", "FlameStrike.jbp")
report_ability("镜中流形 → PhantasmalKiller（Shadow 元素首选）", "PhantasmalKiller.jbp")
report_ability("终末狙击 → ArmyMarkOfHeaven（Mark 元素）", "ArmyMarkOfHeaven.jbp")

print("===== 光环素材（FxOnStart）=====")
report_buff("罪罚 → ArmyBestowCurseBlastBuff", "ArmyBestowCurseBlastBuff.jbp")
report_buff("罪罚 备选 DoomOfServitudeBuff", "DoomOfServitudeBuff.jbp")
report_buff("涌潮悲歌 → AngelBarrier_Buff（BarrierAura）", "AngelBarrier_Buff.jbp")
report_buff("归溟 → BloodHazeBuff（PlasmaAura）", "BloodHazeBuff.jbp")
report_buff("画中世界/旧日 → AngelHaloBuff（GoldHalo）", "AngelHaloBuff.jbp")
report_buff("罪罚 兜底 WitchHexDeathCurseBuff", "WitchHexDeathCurseBuff.jbp")
