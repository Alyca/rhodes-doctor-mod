// 自定义上下文动作：在能力/增益的动作序列里使用。
// 2.8.7 约定：GameAction.RunAction 是 public，覆写也必须是 public。
using System;
using System.Linq;
using BlueprintCore.Utils;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.EntitySystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.Enums.Damage;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.ResourceLinks;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Buffs;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.Utility;
using Kingmaker.View;
using UnityEngine;

namespace RhodesDoctor.Components
{
  /// <summary>
  /// 每轮消耗 1 点资源；不足时移除来源增益。
  /// 巴别塔的恶灵形态增益的 NewRound 动作（规则文档：按轮消耗，参考狂暴）。
  /// Context.MaybeBuff 不存在：持有者取 MaybeCaster ?? MaybeOwner，要删的增益按 Context 反查。
  /// </summary>
  public class ContextActionSpendResourceOrEndBuff : ContextAction
  {
    public BlueprintAbilityResourceReference Resource;
    public int Amount = 1;

    public override string GetCaption() => "消耗恶灵轮数（不足时结束恶灵）";

    public override void RunAction()
    {
      var unit = Context.MaybeCaster ?? Context.MaybeOwner;
      if (unit == null || Resource == null) return;
      var bp = Resource.GetBlueprint() as BlueprintScriptableObject;
      if (bp == null) return;

      var resources = unit.Descriptor.Resources;
      if (resources.GetResourceAmount(bp) < Amount)
      {
        foreach (var b in unit.Buffs.RawFacts.ToList())
        {
          if (b.Context == Context) b.Remove();
        }
      }
      else
      {
        resources.Spend(bp, Amount);
      }
    }
  }

  /// <summary>
  /// 在动作的目标点生成一份"克隆 + 重染"的特效（借来的 prefab，改色只落在克隆体上）。
  /// 用在 AdditionalAbilityEffectRunActionOnClickedTarget 上：每次施放只在点击落点跑一次，
  /// 适合点爆发法术的单次视觉。PrefabLink 为空 / 借不到时静默跳过。
  /// </summary>
  public class ContextActionSpawnTintedFx : ContextAction
  {
    public PrefabLink PrefabLink;
    public Color Tint = Color.white;
    public float Lift = 0.25f;
    public float Lifetime = 6f;
    public float Scale = 0f;        // ≤0 用 prefab 原始大小
    public float FitToMeters = 0f;  // >0：按水平跨度自适应（优先于 Scale），适合铺地特效
    public float FitLongestMeters = 0f; // >0：按三轴最长边自适应，适合竖着的物件

    public override string GetCaption() => "生成重染特效";

    public override void RunAction()
    {
      if (Context.DisableFx) return;
      var point = Target != null ? Target.Point : Context.MaybeCaster?.Position ?? Vector3.zero;
      FxTint.Spawn(PrefabLink, point, Quaternion.identity, Tint, Lift, Lifetime, Scale,
        FitToMeters, FitLongestMeters);
    }
  }

  /// <summary>
  /// 在"刚召唤出来的一批同种单位"的**中点**生成一份特效（墨染山河：在两只墨灵中间）。
  /// 找不到目标单位（还没入场景 / GUID 不匹配）时退到施法目标点，不会什么都不画。
  /// 位置：水平取中点、**高度取落点**（召唤物是悬空气元素，用它们的高度会让特效浮起来）。
  /// 放大：只放大**粒子本身**（<see cref="FxTint.ScaleParticleSizes"/>），**不动根缩放**——
  /// 根缩放会把高于根节点的部件按比例抬高、却不放大粒子（墨染山河 2026-10-10 实测"没变大反而升高"）。
  /// </summary>
  /// <summary>
  /// 在目标点生成一个"按**粒子尺寸**放大"的 FX（不重染、只放位置、自动销毁）。
  /// 用 <see cref="FxTint.SpawnPlaced"/>——**只放大粒子本身、不动根节点**（见 dev-notes §14.3：
  /// 根节点缩放会把部件抬高却不放大粒子）。用例：火山"落点火爆"太小 → 放大 2 倍；余焰每轮再放一发。
  /// </summary>
  public class ContextActionSpawnScaledFx : ContextAction
  {
    public PrefabLink PrefabLink;
    public float ParticleSizeScale = 1f;
    /// <summary>
    /// 根节点缩放（2026-10-11 新增）：**网格型**爆发只靠 <see cref="ParticleSizeScale"/> 缩不动
    /// （闪光弹借的阳炎爆就是这种，粒子缩放没反应），要同时给根缩放。1 = 原样。
    /// </summary>
    public float RootScale = 1f;
    public float Lifetime = 2.5f;

    public override string GetCaption() => "生成放大特效";

    public override void RunAction()
    {
      if (Context.DisableFx) return;
      var point = Target != null ? Target.Point : Context.MaybeCaster?.Position ?? Vector3.zero;
      FxTint.SpawnPlaced(PrefabLink, point, Quaternion.identity, ParticleSizeScale, Lifetime, RootScale);
    }
  }

  public class ContextActionSpawnFxBetweenSummons : ContextAction
  {
    /// <summary>素材（借原版法术的 AbilitySpawnFx，见 FxLibrary.Summon）。</summary>
    public PrefabLink PrefabLink;
    /// <summary>要配对的召唤物蓝图 GUID（墨灵 = Guids.InklingUnit）。</summary>
    public string SummonGuid;
    /// <summary>以落点为心、多远内算"这次召唤的"（米）。</summary>
    public float SearchRadiusMeters = 40f;
    /// <summary>粒子尺寸放大倍数（1 = 原始大小）。**不动根缩放**。</summary>
    public float ParticleSizeScale = 1f;
    /// <summary>高度微调（米）：正数往上抬。默认 0 = 用**落点**的高度。</summary>
    public float HeightOffsetMeters = 0f;
    public float Lifetime = 3f;

    public override string GetCaption() => "在召唤物中间生成特效";

    public override void RunAction()
    {
      if (Context.DisableFx) return;
      if (PrefabLink == null || !PrefabLink.Exists()) return;
      var caster = Context.MaybeCaster;
      Vector3 center = Target != null ? Target.Point : caster?.Position ?? Vector3.zero;

      // 取"这次召唤的墨灵"的水平中点；一只都没找到就退回落点。
      Vector3 pos = center;
      int found = 0;
      var scene = Game.Instance?.CurrentScene ?? Game.Instance?.LoadedAreaState;
      var units = scene?.AllEntityData?.OfType<UnitEntityData>();
      if (units != null && !string.IsNullOrEmpty(SummonGuid))
      {
        var want = SummonGuid.Replace("-", "").ToLowerInvariant();
        Vector3 sum = Vector3.zero;
        foreach (var u in units)
        {
          if (u == null || u.Blueprint == null) continue;
          var g = u.Blueprint.AssetGuid.ToString().Replace("-", "").ToLowerInvariant();
          if (g != want) continue;
          if ((u.Position - center).magnitude > SearchRadiusMeters) continue;
          sum += u.Position;
          found++;
        }
        if (found > 0)
        {
          var mid = sum / found;
          // 只取**水平**中点；高度用落点（召唤物是悬空的气元素，直接用它们的高度会让特效浮起来）。
          pos = new Vector3(mid.x, center.y, mid.z);
        }
      }
      pos.y += HeightOffsetMeters;

      var go = FxTint.SpawnPlaced(PrefabLink, pos, Quaternion.identity, ParticleSizeScale, Lifetime);
      // 诊断（作者 2026-10-10 要求"读日志搞清高度"）：把位置链路打全，方便核对。
      float worldScale = go != null ? go.transform.lossyScale.x : 0f;
      Vector3? rootPos = go != null ? go.transform.position : (Vector3?)null;
      Dbg.Log(
        $"[RhodesDoctor] 墨染山河·召唤闪光：落点={Fmt(center)} 施法者={Fmt(caster?.Position)}"
        + $" 找到墨灵={found} 生成点={Fmt(pos)} 根节点={Fmt(rootPos)} 世界缩放={worldScale:0.00}");
      DumpPartsOnce(go);
    }

    /// <summary>每次会话只打一次：把这个素材各部件（粒子系统）的**世界位置**列出来——用于核对"花到底在哪一层高度"。</summary>
    private static bool _partsLogged;
    private static void DumpPartsOnce(GameObject go)
    {
      if (_partsLogged || go == null) return;
      _partsLogged = true;
      try
      {
        Dbg.Log("[RhodesDoctor] 墨染山河·部件世界位置（一次性）：");
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
          Dbg.Log($"[RhodesDoctor]   部件 '{ps.name}' pos={Fmt(ps.transform.position)}");
      }
      catch { }
    }

    private static string Fmt(Vector3? v) => v.HasValue ? $"({v.Value.x:F2},{v.Value.y:F2},{v.Value.z:F2})" : "无";
  }

  /// <summary>
  /// 让特效"从天而降"：从生成点**加速**落到落点，到位后自毁组件（不再每帧计算）。
  /// 落体感用 t² 近似，不用刚体——特效不需要物理，也不会被地形碰撞影响。
  /// 剑雨的剑就是靠它砸下来的。
  /// </summary>
  public class FallingFx : MonoBehaviour
  {
    public Vector3 LandPoint;
    public float FallSeconds = 0.35f;

    private Vector3 _start;
    private float _elapsed;

    private void Start() => _start = transform.position;

    private void Update()
    {
      _elapsed += Time.deltaTime;
      float t = FallSeconds <= 0.01f ? 1f : Mathf.Clamp01(_elapsed / FallSeconds);
      transform.position = Vector3.Lerp(_start, LandPoint, t * t); // t² → 越来越快，像落体
      if (t >= 1f) UnityEngine.Object.Destroy(this);
    }
  }

  /// <summary>
  /// 让一个特效每帧贴到另一个物件上（"拖尾"跟着剑一起落）。
  /// 与 <see cref="FallingFx"/> 同一套思路：不挂父子关系，避免继承主体的随机缩放/歪斜
  /// （剑是横七竖八的，拖尾跟着一起歪就散成一片）；目标被销毁（剑 6 秒后自删）时自己也消失，不留孤儿。
  ///
  /// <see cref="FixedRotation"/> = true 时朝向恒定（世界坐标），用来把拖尾"立起来"——
  /// 拖尾素材是**投射物外观**，它的 +Z 是"飞行方向"；剑是往下掉的，所以把 +Z 转到朝下（Pitch=90），
  /// 光尾就朝上拖在剑后面。若让拖尾继承剑的随机俯仰，光弧会跟着乱转，看不出"尾"。
  /// </summary>
  public class FollowFx : MonoBehaviour
  {
    /// <summary>要跟随的主体（剑）。主体没了就自删。</summary>
    public Transform Target;
    /// <summary>相对主体的世界坐标偏移（米）。</summary>
    public Vector3 WorldOffset;
    /// <summary>true = 朝向恒定（用下面的 Rotation），false = 跟着主体转。</summary>
    public bool FixedRotation;
    public Quaternion Rotation = Quaternion.identity;

    private void LateUpdate()
    {
      // LateUpdate：主体在 Update 里被 FallingFx 移动，这里读到的是本帧最终位置，不会慢一帧。
      if (Target == null) { UnityEngine.Object.Destroy(gameObject); return; }
      transform.position = Target.position + WorldOffset;
      if (FixedRotation) transform.rotation = Rotation;
    }
  }

  /// <summary>
  /// 把借来的特效克隆 N 份，摆成一圈 / 一片——"个体摆放"用（剑雨的剑）。
  ///
  /// 尺寸有两条互斥的路（都不给就用 Scale）：
  ///   · <see cref="FitToMeters"/>：按**水平**跨度（x/z 取大）自适应——铺在地上的特效用它。
  ///   · <see cref="FitLongestMeters"/>：按**三轴最长边**自适应——竖着的物件（剑）必须用它，
  ///     否则只会量到物件的厚度，把整把剑放大好几倍（剑雨踩过这个坑）。
  ///
  /// 随机项（都默认 0 = 完全规整）：<see cref="RadiusJitter"/> 让半径在 [0,Radius] 内随机 → 铺满圆盘；
  /// <see cref="TiltJitter"/> 给俯仰+侧倾加抖动 → 横七竖八；
  /// <see cref="YawJitter"/> 给朝向加抖动（180 ≈ 完全随机）；
  /// <see cref="ScaleJitter"/> / <see cref="OffsetUpJitter"/> / <see cref="FallSecondsJitter"/>
  /// 分别让大小 / 插地深浅 / 下落快慢各不相同 → 像下雨而不是列队。
  ///
  /// <see cref="FallFromMeters"/> &gt; 0 时每份从高处落到落点（"从天而降"）；
  /// <see cref="OffsetUp"/> 把落点整体抬高，用来让竖着的物件"插"在地上而不是陷进地里。
  ///
  /// <see cref="GoldenStreak"/> 为 true 时用自绘的细光带（<see cref="SwordStreakFx"/>，剑雨现在用这个）；
  /// 否则用 <see cref="TrailLink"/> 借一份原版粒子素材，每份带一条跟随拖尾（<see cref="FollowFx"/> 每帧贴上去）。
  /// 剑雨的教训：借来的粒子素材可能带**不受根节点缩放影响的巨型可见体积**，别指望靠 FitToMeters 压小。
  /// </summary>
  public class ContextActionSpawnFxRing : ContextAction
  {
    public PrefabLink PrefabLink;
    public int Count = 1;
    public float Radius = 1.52f;   // 米：最多散布到多远
    /// <summary>
    /// 同心环数量（默认 1 = 旧行为）。>1 时把 Count 均分到各环、半径等分成 1/Rings…1，相邻环错开半个角步，
    /// 摆成整齐的同心阵（配 <see cref="RadiusJitter"/>=0 用）。剑雨用它做"整齐列阵"（用户要求，见 SpellsSetup）。
    /// </summary>
    public int Rings = 1;
    public float Scale = 0f;       // ≤0 用 prefab 原始大小
    public float FitToMeters = 0f; // >0：按水平跨度自适应
    public float FitLongestMeters = 0f; // >0：按三轴最长边自适应（优先）
    public float AngleOffset;      // 整体角度偏移（度）
    public float Pitch;            // 基准俯仰（度）：0=原样，180=倒过来（剑尖朝下）
    public float RadiusJitter = 0f;   // 0=正好落在半径 Radius 的圆周上；1=在 [0,Radius] 圆盘内随机
    public float TiltJitter = 0f;     // 俯仰+侧倾各随机 ±该值（度）
    public float YawJitter = 0f;      // 朝向随机 ±该值（度）
    public float ScaleJitter = 0f;    // 大小随机 ±该比例（0.25 = ±25%）
    public float OffsetUp = 0f;       // 落点整体抬高（米）
    public float OffsetUpJitter = 0f; // 插地深浅随机 ±该值（米）
    public float FallFromMeters = 0f; // >0：从这么高处落下来（"从天而降"）
    public float FallSeconds = 0.35f; // 下落耗时（秒）
    public float FallSecondsJitter = 0f; // 下落耗时随机 ±该值（秒）→ 先后落地
    public Color Tint = Color.white;
    public float Lift = 0.9f;
    public float Lifetime = 5f;

    /// <summary>跟随拖尾素材（空 = 不生成拖尾）。剑雨**已不用**它（改用 <see cref="GoldenStreak"/> 自绘光带，
    /// 原因见 <see cref="SwordStreakFx"/> 文件头）；保留给"确实需要一个现成粒子拖尾"的场合。</summary>
    public PrefabLink TrailLink;
    /// <summary>拖尾尺寸：FitLongestMeters 优先，其次 FitToMeters（水平跨度），再次 TrailScale；都不给用素材原始大小。</summary>
    public float TrailFitMeters = 0f;
    public float TrailFitLongestMeters = 0f;
    public float TrailScale = 0f;
    /// <summary>拖尾相对主体的世界抬升（米）。</summary>
    public float TrailOffsetUp = 0f;
    /// <summary>拖尾的固定俯仰（度）：拖尾素材的 +Z 是"飞行方向"，90 = 朝下 → 光尾朝上拖在剑后。</summary>
    public float TrailPitch = 90f;
    /// <summary>拖尾自删时长（秒）；≤0 表示跟主体同寿（<see cref="Lifetime"/>）。</summary>
    public float TrailLifetime = 0f;

    /// <summary>
    /// true = **不借任何原版粒子素材**，改用自绘的细光带（<see cref="SwordStreakFx"/>）当光尾。
    /// 与上面的 <see cref="TrailLink"/> 二选一；两者都给时以本项为准。
    /// 剑雨用它：借来的"投射物外观"实测是数百米量级的巨型光柱，缩到 1.4 m 画面上仍是一整根大柱子
    /// （粒子可见尺寸不跟根节点缩放走），18 把剑各一条就糊成一根橙色大光柱。
    /// </summary>
    public bool GoldenStreak;
    /// <summary>光尾时长（秒）：剑下落约 1.1 s，取 0.3 → 拖出约 4 m 长的一缕。</summary>
    public float StreakSeconds = 0.3f;
    /// <summary>光尾宽度（米；<see cref="SwordStreakFx"/> 会做世界缩放归一，所以这个数是真实米数）。</summary>
    public float StreakWidth = 0.09f;

    public override string GetCaption() => "绕圈/散布排列特效";

    // 拖尾素材加载失败只提示一次（否则 18 把剑会刷 18 条）。
    private static bool _trailMissLogged;

    private void SpawnTrail(Transform host, Vector3 spawnPoint)
    {
      if (TrailLink == null || host == null) return;
      float life = TrailLifetime > 0f ? TrailLifetime : Lifetime;
      var rot = Quaternion.Euler(TrailPitch, 0f, 0f);
      var trail = FxTint.Spawn(TrailLink, spawnPoint + Vector3.up * TrailOffsetUp, rot, Tint, Lift,
        life, TrailScale, TrailFitMeters, TrailFitLongestMeters);
      if (trail == null)
      {
        if (!_trailMissLogged)
        {
          _trailMissLogged = true;
          Dbg.Log("[RhodesDoctor] 拖尾素材生成失败（跳过拖尾，主体照常）："
            + (TrailLink != null ? TrailLink.AssetId : "<null>"));
        }
        return;
      }
      var follow = trail.AddComponent<FollowFx>();
      follow.Target = host;
      follow.WorldOffset = Vector3.up * TrailOffsetUp;
      follow.FixedRotation = true;
      follow.Rotation = rot;
    }

    private void SpawnOne(Vector3 ground, Quaternion rot, float scaleMul, float up, float fallSec)
    {
      var land = ground + Vector3.up * up;
      var spawn = land + Vector3.up * FallFromMeters;
      var go = FxTint.Spawn(PrefabLink, spawn, rot, Tint, Lift, Lifetime, Scale,
        FitToMeters, FitLongestMeters);
      if (go == null) return;
      if (Mathf.Abs(scaleMul - 1f) > 0.001f) go.transform.localScale *= scaleMul;
      if (FallFromMeters > 0f)
      {
        var fall = go.AddComponent<FallingFx>();
        fall.LandPoint = land;
        fall.FallSeconds = fallSec;
      }
      // 光尾：从**出生点**就跟着剑（同高、同帧）一路落下来，才有完整的一条。
      //   ① GoldenStreak：自绘细光带（剑雨现在用这个，理由见 SwordStreakFx 文件头）；
      //   ② 否则借原版粒子素材当拖尾（<see cref="TrailLink"/>）。
      if (GoldenStreak)
        SwordStreakFx.Attach(go, Tint, StreakSeconds, StreakWidth);
      else
        SpawnTrail(go.transform, spawn);
    }

    public override void RunAction()
    {
      if (Context.DisableFx) return;
      var center = Target != null ? Target.Point : Context.MaybeCaster?.Position ?? Vector3.zero;
      int n = Math.Max(1, Count);
      int rings = Mathf.Clamp(Rings, 1, n > 1 ? n : 1);
      // 每环几把：均分，余数给外圈。
      int per = n / rings, extra = n % rings;
      for (int j = 0; j < rings; j++)
      {
        int cn = per + (j >= rings - extra ? 1 : 0);
        if (cn <= 0) continue;
        // 等分半径：rings=1 时就是 Radius 本身（= 旧行为）。
        float ringR = Radius * (j + 1f) / rings;
        float step = 360f / cn;
        // 相邻环错开半个角步，避免内外圈径向对齐（整齐但不死板）。
        float stagger = rings > 1 && (j & 1) == 1 ? step * 0.5f : 0f;
        for (int i = 0; i < cn; i++)
        {
          float baseDeg = (cn == 1 ? AngleOffset : step * i + AngleOffset) + stagger;
          // 半径：Jitter=1 时在 [0, ringR] 圆盘内均匀随机，否则正好贴在半径 ringR 的圆周上。
          float r = cn == 1 ? 0f
            : Mathf.Lerp(ringR, ringR * UnityEngine.Random.value, Mathf.Clamp01(RadiusJitter));
          float rad = baseDeg * Mathf.Deg2Rad;
          var pos = center + new Vector3(Mathf.Cos(rad) * r, 0f, Mathf.Sin(rad) * r);

          float pitch = Pitch + UnityEngine.Random.Range(-TiltJitter, TiltJitter);
          float roll = UnityEngine.Random.Range(-TiltJitter, TiltJitter);
          float yaw = -baseDeg + UnityEngine.Random.Range(-YawJitter, YawJitter);
          float scaleMul = 1f + UnityEngine.Random.Range(-ScaleJitter, ScaleJitter);
          float up = OffsetUp + UnityEngine.Random.Range(-OffsetUpJitter, OffsetUpJitter);
          float fallSec = Mathf.Max(0.1f,
            FallSeconds + UnityEngine.Random.Range(-FallSecondsJitter, FallSecondsJitter));

          SpawnOne(pos, Quaternion.Euler(pitch, yaw, roll), scaleMul, up, fallSec);
        }
      }
    }
  }

  /// <summary>
  /// 不稳定血浆：每轮开始失去固定生命，但不能降到 1 以下（保 1 线钳制）。
  /// 扣增益持有者（被强化的友军），不是施法者。Damage 是"已受伤害"，掉血 = 加大它。
  /// </summary>
  public class ContextActionLoseHitPointsClamped : ContextAction
  {
    public int Amount = 2;

    public override string GetCaption() => "失去生命（不低于 1）";

    public override void RunAction()
    {
      var unit = Context.MaybeOwner ?? Target.Unit;
      if (unit == null) return;
      int loss = Math.Min(Amount, unit.HPLeft - 1);
      if (loss <= 0) return;
      unit.Damage += loss;
    }
  }

  /// <summary>
  /// 王冠的回响：把目标身上至多 MaxStolen 个法术增益夺走，按距离轮流分给爆发范围内的友军。
  /// 改名 ...Custom：游戏本体已有同名类 ContextActionStealBuffs。
  /// 注意：必须排在驱散/清除之前执行，否则增益已被移除，没得可偷。
  /// 剩余时间：Remove 之前先取 Buff.TimeLeft，再用 BuffCollection.AddBuff(bp, Context, TimeSpan?) 原样挂上。
  /// 分配：按到法术落点距离排序，每位友军先分一个再轮回；范围内无友军时回退给施法者。
  /// R4：只取"可驱散的法术增益"，并且施加者不是博士一方（B6）。
  /// 即：非减益、IsFromSpell、非 IsNotDispelable、非隐藏、有施法者且为施法者的敌人。
  /// 无施法者的增益（怪物天生、Boss 隐藏机制）不再可偷。
  /// </summary>
  public class ContextActionStealBuffsCustom : ContextAction
  {
    public int MaxStolen = 2;

    public override string GetCaption() => "夺取可驱散增益";

    internal static bool IsStealable(Buff buff, UnitEntityData caster)
    {
      if (buff == null || caster == null) return false;
      var bp = buff.Blueprint;
      if (bp == null || bp.Harmful) return false;
      if (buff.IsNotDispelable) return false;
      if (buff.HiddenInInspector) return false;
      if (!buff.IsFromSpell) return false;
      var src = buff.Context?.MaybeCaster;
      if (src == null || !src.IsEnemy(caster)) return false;
      return true;
    }

    public override void RunAction()
    {
      var caster = Context.MaybeCaster;
      var target = Target.Unit;
      if (caster == null || target == null) return;

      // 先夺下（存蓝图 + 剩余时间），再统一分配。
      var taken = new System.Collections.Generic.List<(Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff bp, System.TimeSpan left)>();
      foreach (var buff in target.Buffs.RawFacts.ToList())
      {
        if (taken.Count >= MaxStolen) break;
        if (!IsStealable(buff as Buff, caster)) continue;
        var bp = buff.Blueprint;
        taken.Add((bp as Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff, buff.TimeLeft)); // Remove 之前先取值
        buff.Remove();
      }
      if (taken.Count == 0) return;

      // 爆发落点 60 尺内友军按距离排序，无人则回退施法者。
      // N6+R6：只分给友军（IsPlayerFaction），与重构/文明的存续同一套标准；中立 NPC 不分。
      // ⚠ 10-12：单位来源改走 `UnitScan`（原来只扫区域状态 → **队伍成员不在里面**，
      // 于是永远"找不到友军"、全部回落到施法者自己）。见 `UnitScan.cs` 顶部注释。
      var center = Context?.MainTarget?.Point ?? target.Position;
      float radius = new Feet(60).Meters;
      var allies = new System.Collections.Generic.List<UnitEntityData>();
      var units = UnitScan.Take().Units;
      if (units != null)
        foreach (var u in units)
        {
          if (u == null || (!u.IsPlayerFaction && !caster.IsAlly(u))) continue;
          if (u.Descriptor.State.IsDead) continue;
          if ((u.Position - center).magnitude > radius) continue;
          allies.Add(u);
        }
      allies.Sort((a, b) =>
        (a.Position - center).magnitude.CompareTo((b.Position - center).magnitude));
      if (allies.Count == 0) allies.Add(caster);

      for (int k = 0; k < taken.Count; k++)
      {
        if (taken[k].bp == null) continue;
        // ⚠ 必须用**带施法者的重载**，不能用 Context 重载：后者是 `Context.CloneFor(...)`，
        // 会让偷来的增益**继承王冠自己的"影响心灵"描述符** → `RuleCanApplyBuff.OnTrigger` 把它当负面效果
        // （`Context.SpellDescriptor.IsNegativeEffect()` 且 buff 无 `IsPositiveEffect`）→ 判定"免疫"、buff 挂不上。
        // 实测症状：日志「博士对测试增益·护甲免疫」。
        // 带施法者的重载走 `new MechanicsContext(caster, Owner, blueprint)`——**没有技能描述符**，正常挂上。
        var recipient = allies[k % allies.Count];
        recipient.Buffs.AddBuff(taken[k].bp, caster ?? recipient, taken[k].left);
      }
    }
  }

  /// <summary>
  /// R4+D3：王冠回响豁免成功者随机失去一个可偷增益，直接消失（不分给友军）。
  /// 筛选同 Steal（可驱散法术增益、敌方施法者）；随机用 UnityEngine.Random。
  /// </summary>
  public class ContextActionCrownEchoSuccessRemove : ContextAction
  {
    public override string GetCaption() => "王冠回响：成功者失去一个";

    public override void RunAction()
    {
      var caster = Context.MaybeCaster;
      var target = Target.Unit;
      if (caster == null || target == null) return;
      var eligible = new System.Collections.Generic.List<Buff>();
      foreach (var b in target.Buffs.RawFacts.ToList())
      {
        var bf = b as Buff;
        if (bf != null && ContextActionStealBuffsCustom.IsStealable(bf, caster))
          eligible.Add(bf);
      }
      if (eligible.Count == 0) return;
      int idx = UnityEngine.Random.Range(0, eligible.Count);
      eligible[idx].Remove();
    }
  }

  /// <summary>
  /// R4+C37：王冠回响豁免失败者剩余增益直接清除（不过驱散检定）。
  /// 在 Steal（至多 2 个已分给友军）之后执行，把仍符合条件的全部移除（消失）。
  /// </summary>
  public class ContextActionCrownEchoPurgeRemaining : ContextAction
  {
    public override string GetCaption() => "王冠回响：失败者清除剩余";

    public override void RunAction()
    {
      var caster = Context.MaybeCaster;
      var target = Target.Unit;
      if (caster == null || target == null) return;
      foreach (var b in target.Buffs.RawFacts.ToList())
      {
        var bf = b as Buff;
        if (bf != null && ContextActionStealBuffsCustom.IsStealable(bf, caster))
          bf.Remove();
      }
    }
  }

  /// <summary>
  /// 镜中流形：生成目标的镜像复制体。
  /// 引擎自带全量复制 UnitHelper.Copy（属性 / AC / BAB / 豁免 / 速度 / 专长 / 装备 / 外观全部继承）。
  /// 收尾：血量 = 目标当前生命、阵营、行为锁、3 轮召唤挂靠。
  /// </summary>
  public class ContextActionSpawnMirrorCopy : ContextAction
  {
    /// <summary>行为锁增益（ForbidSpellCasting + ExAndSuAbilitiesForbidden）：只能攻击、移动、用普通物品。</summary>
    public BlueprintBuffReference LockBuff;

    public override string GetCaption() => "生成镜像复制体";

    public override void RunAction()
    {
      // 作者 2026-10-10 报告"没有召唤出复制体"（修了一轮仍未成）。
      // 反编译确认的两点：
      //   ① `UnitHelper.Copy` 走 `Game.Instance.CreateUnitVacuum` 造"真空单位"：建视图但**不注册进场景**
      //      （`CopyInternal` 里没有 `AddEntity`）；
      //   ② `RuleSummonUnit.OnTrigger` 拿到 `PreMadeUnit` 后**直接使用、也不注册**。
      // 另外 `CreateView()` 用的是单位**当前 `Position`**（真空单位默认在原点）——所以要**先摆到目标处**再建视图。
      // 现在：Copy → 摆位（Position + 视图）→ AddEntity 注册进场景 → 行为锁 → RuleSummonUnit。
      Dbg.Log("[RhodesDoctor] 镜像复制体：动作开始。");
      try
      {
        var caster = Context.MaybeCaster;
        var target = Target.Unit;
        if (caster == null || target == null)
        {
          Dbg.Log($"[RhodesDoctor] 镜像复制体：caster={(caster == null ? "null" : "ok")} "
            + $"target={(target == null ? "null" : "ok")}，跳过。");
          return;
        }

        var mirror = UnitHelper.Copy(target, true, false, true);
        if (mirror == null)
        {
          Dbg.Log("[RhodesDoctor] 镜像复制体：UnitHelper.Copy 返回 null，跳过。");
          return;
        }

        // ① 摆位：真空单位默认在原点；视图是 `CreateView` 按 `Position` 摆的，所以先改 `Position` 再挪视图。
        var pos = target.Position;
        mirror.Position = pos;
        mirror.SpawnPosition = pos;
        if (mirror.View != null) mirror.View.transform.position = pos;

        // ② 血量：Copy 只抄生命上限，不抄当前伤。镜像生命 = 目标当前生命值。
        mirror.Descriptor.Damage = Math.Max(0, mirror.Descriptor.MaxHP - target.Descriptor.HPLeft);

        // ③ 注册进场景（关键修复，见方法头注释）——否则复制体是孤儿、看不见。
        Game.Instance.EntityCreator.AddEntity(mirror, caster.HoldingState);
        // ③b 贴地（原版 `SpawnEntityWithView` 注册后也会做这一步）。
        try { Kingmaker.Controllers.Units.UnitPlaceOnGroundController.ForcedTick(mirror); } catch { }

        // ④ 法术位与资源：给分身**满的法术位 + 满的资源**
        //    （作者 10-11：分身要"法术位全有，也可以使用"）。
        try
        {
          foreach (var sb in mirror.Descriptor.Spellbooks)
            try { sb.Rest(); } catch { }
        }
        catch (Exception e) { Main.Mod.Logger.Error("[RhodesDoctor] 镜像复制体：法术位重置失败：\n" + e); }
        try { mirror.Descriptor.Resources.FullRestoreAll(null); } catch { }

        // ⑤ 行为锁：**不再施加**（作者 10-11：分身也能正常施法 / 使用能力）。
        //    （旧的 `MirrorLockBuff` 蓝图保留在 `SummonsSetup`，未删除；本动作不再挂它。）

        // ⑥ 召唤挂靠：阵营 / 队伍 / 召唤计时 / 解散，全部由规则负责（不再手写 SwitchFactions）。
        //    持续 = **1 轮/级**（作者 10-11：镜中流形这种都改成轮/级）——取能力上下文里的 Rank。
        int rounds = Math.Max(1, BlueprintCore.Utils.Types.ContextValues.Rank().Calculate(Context));
        Context.TriggerRule(new RuleSummonUnit(caster, mirror, new Rounds(rounds)));
        Dbg.Log($"[RhodesDoctor] 镜像复制体已召唤（目标 {target.CharacterName}，位置 {pos}，{rounds} 轮）。");
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 镜像复制体召唤失败：\n" + e);
      }
    }
  }

  /// <summary>
  /// 炮火延伸后续轮：追踪增益每轮开始时，在记住的目标点 15 尺内爆炸（8d6 轰击 + 8d6 火焰，反射减半）。
  /// 爆炸点与 DC 从持有者身上本次施放那份 ArtilleryBarrageTracker Data 读（R1；N7：按 Context 找自己那份，
  /// 不读第一份，两份施放各算各的，与延时爆破 Round 同做法）。
  /// 反射减半按骰数减半实现（4d6/段，期望与结果减半一致）。
  /// </summary>
  public class ContextActionArtilleryStrike : ContextAction
  {
    public override string GetCaption() => "炮火延伸：追踪点爆炸";

    public override void RunAction()
    {
      var caster = Context.MaybeCaster;
      var owner = Context.MaybeOwner ?? caster;
      if (caster == null || owner == null) return;
      bool found = false;
      UnityEngine.Vector3 point = owner.Position;
      int saveDC = 10;
      foreach (var b in owner.Buffs.RawFacts.ToList())
      {
        var bf = b as Buff;
        if (bf == null) continue;
        if (bf.Context != Context) continue;
        var cr = b.GetComponentWithRuntime<ArtilleryBarrageTracker>();
        if (cr.Runtime == null) continue;
        var data = cr.Runtime.GetData<ArtilleryBarrageTrackerData>();
        if (data == null) continue;
        point = data.Point;
        saveDC = data.SaveDC;
        found = true;
        break;
      }
      if (!found) return;
      // 后两轮缺 FX 的补丁（2026-10-10）：在追踪点再放一发火焰爆发。
      // 首轮由能力自身的 `AbilitySpawnFx`（ApplyAbilityFx 的 ArtilleryBarrage → FxLibrary.Fire）负责，
      // 而第 2/3 轮由本动作触发、能力早已结算完，不会再播——原先画面上"只有第一轮有东西"。
      // 用 SpawnPlaced（原样实例化、不重染、只放位置、自动销毁），素材与首轮同源（FxLibrary.Fire）。
      if (!Context.DisableFx)
        FxTint.SpawnPlaced(FxLibrary.Fire, point, Quaternion.identity, 1f, 2.5f);
      var scene = Game.Instance?.CurrentScene ?? Game.Instance?.LoadedAreaState;
      var units = scene?.AllEntityData?.OfType<UnitEntityData>();
      if (units == null) return;
      foreach (var u in units.ToList())
      {
        if (u == null || !u.IsEnemy(caster)) continue;
        if ((u.Position - point).magnitude > new Feet(15).Meters) continue;
        var save = Context.TriggerRule(new RuleSavingThrow(u, SavingThrowType.Reflex, saveDC));
        int dice = save.IsPassed ? 4 : 8;
        var bundle = new DamageBundle(new BaseDamage[]
        {
          new PhysicalDamage(new ModifiableDiceFormula(new DiceFormula(dice, DiceType.D6)), 0, PhysicalDamageForm.Bludgeoning),
          new EnergyDamage(new ModifiableDiceFormula(new DiceFormula(dice, DiceType.D6)), 0, DamageEnergyType.Fire),
        });
        Context.TriggerRule(new RuleDealDamage(caster, u, bundle));
      }
    }
  }

  /// <summary>
  /// R3乙：炮火延伸首炸扫描（15 尺内全部敌人 8d6 轰击 + 8d6 火焰，反射减半）。
  /// 原首炸走普通单体伤害（SpellsSetup 871–875），点地面完全没效果；现改为动作内扫描范围。
  /// </summary>
  public class ContextActionArtilleryInitialBurst : ContextAction
  {
    public override string GetCaption() => "炮火延伸：首炸扫描";

    public override void RunAction()
    {
      var caster = Context.MaybeCaster;
      if (caster == null) return;
      UnityEngine.Vector3 center;
      if (Context.MainTarget.IsUnit && Context.MainTarget.Unit != null)
        center = Context.MainTarget.Unit.Position;
      else
        center = Context.MainTarget.Point;
      int dc = Context.Params?.DC ?? 10;
      float radius = new Feet(15).Meters;
      var scene = Game.Instance?.CurrentScene ?? Game.Instance?.LoadedAreaState;
      var units = scene?.AllEntityData?.OfType<UnitEntityData>();
      if (units == null) return;
      foreach (var u in units.ToList())
      {
        if (u == null || u.Descriptor.State.IsDead || !u.IsEnemy(caster)) continue;
        if ((u.Position - center).magnitude > radius) continue;
        var save = Context.TriggerRule(new RuleSavingThrow(u, SavingThrowType.Reflex, dc));
        int dice = save.IsPassed ? 4 : 8;
        var bundle = new DamageBundle(new BaseDamage[]
        {
          new PhysicalDamage(new ModifiableDiceFormula(new DiceFormula(dice, DiceType.D6)), 0, PhysicalDamageForm.Bludgeoning),
          new EnergyDamage(new ModifiableDiceFormula(new DiceFormula(dice, DiceType.D6)), 0, DamageEnergyType.Fire),
        });
        Context.TriggerRule(new RuleDealDamage(caster, u, bundle));
      }
    }
  }

  /// <summary>
  /// C4/R3乙 + N1：天灾降临每轮伤害（区域内每个敌人 12d6 火焰 + 12d6 轰击 + 12d6 电击，反射减半，每轮一次）。
  /// 由该区域（半径见法术 Spec，作者 10-11 由 80 改 40）的 Round 动作调用；N1：区域每轮对圈内每个单位各执行一次（AbilityAreaEffectRunAction.OnRound
  /// 对 InGameUnitsInside 循环 GetDataScope(item) + Round.Run()），Target 即本次单位。每次执行只打当前单位，
  /// 且只打敌人，保证每个敌人每轮只受一次。
  /// </summary>
  public class ContextActionCatastropheRound : ContextAction
  {
    public override string GetCaption() => "天灾降临：每轮伤害";

    public override void RunAction()
    {
      var caster = Context.MaybeCaster;
      if (caster == null) return;
      var u = Target.Unit;
      if (u == null || u.Descriptor.State.IsDead || !u.IsEnemy(caster)) return;
      int dc = Context.Params?.DC ?? 10;
      var save = Context.TriggerRule(new RuleSavingThrow(u, SavingThrowType.Reflex, dc));
      int dice = save.IsPassed ? 6 : 12;
      var bundle = new DamageBundle(new BaseDamage[]
      {
        new EnergyDamage(new ModifiableDiceFormula(new DiceFormula(dice, DiceType.D6)), 0, DamageEnergyType.Fire),
        new PhysicalDamage(new ModifiableDiceFormula(new DiceFormula(dice, DiceType.D6)), 0, PhysicalDamageForm.Bludgeoning),
        new EnergyDamage(new ModifiableDiceFormula(new DiceFormula(dice, DiceType.D6)), 0, DamageEnergyType.Electricity),
      });
      Context.TriggerRule(new RuleDealDamage(caster, u, bundle));
    }
  }

  /// <summary>
  /// 潮汐守望：每轮开始补满到固定临时生命（刷新不叠加）。
  /// 只补差额：没受伤不加，受过伤补到 Amount；与其他来源的临时生命取高（临时生命本来就不叠加）。
  /// 来源记到本增益名下，增益结束随增益一起清除。
  /// </summary>
  public class ContextActionRefreshTemporaryHitPoints : ContextAction
  {
    public int Amount = 15;

    public override string GetCaption() => "刷新临时生命（不叠加）";

    public override void RunAction()
    {
      var unit = Context.MaybeOwner ?? Target.Unit;
      if (unit == null) return;
      Kingmaker.EntitySystem.EntityFact source = null;
      foreach (var b in unit.Buffs.RawFacts.ToList())
        if (b.Context == Context) { source = b; break; }
      if (source == null) return;
      int deficit = Amount - unit.Descriptor.TemporaryHP;
      if (deficit <= 0) return;
      unit.Descriptor.Stats.TemporaryHitPoints.AddModifier(
        deficit, source, ModifierDescriptor.UntypedStackable);
    }
  }

  /// <summary>
  /// 重构 / 文明的存续：范围内友军完全恢复 + 驱散全部有害增益，再按名额复活阵亡者。
  /// 顺序按规则先活后死；不限阵亡时间（作者已确认，T45 更新）。
  /// Resurrect 第三个参数按 FullRestore 语义传（满血复活时 true）。
  /// </summary>
  public class ContextActionMassRestore : ContextAction
  {
    public bool CenterOnCaster;
    public int ResurrectMax = 3;
    public float ResurrectHealth = 0.5f;
    /// <summary>后续增益（文明的存续：+4 与免死），挂给范围内全体友军（含复活者），持续 1 分钟。</summary>
    public BlueprintBuffReference AfterBuff;

    /// <summary>
    /// true = 每个被恢复/复活的友军身上炸一下金光。
    /// 给**不挂增益**的群体恢复法术用（重构：它没有增益，挂不了常驻光环 `FxOnStart`，
    /// 是 D 类 11 个里唯一一个走"一次性金光"的）。用的是自绘的命中金光（<see cref="SwordHitFx"/>），
    /// 尺寸/颜色自己可控，不借粒子素材。
    /// </summary>
    public bool GoldFlash;
    /// <summary>金光直径（米）。</summary>
    public float GoldFlashSize = 1.4f;
    /// <summary>金光颜色（由 SpellsSetup 传 SwordTint 进来，与剑雨同一份橙金）。</summary>
    public UnityEngine.Color Tint = new UnityEngine.Color(1f, 0.78f, 0.27f, 1f);

    /// <summary>
    /// **每个受影响单位各一份**的爆发特效（借来的 PrefabLink）。
    /// 设了就在每个被恢复 / 复活的友军身上各放一发（<see cref="FxTint.SpawnPlaced"/>），
    /// 替代"只在施法者处放一发"的旧做法（作者 10-12：重构要"每个受影响的单位各有一份特效"）。
    /// 为空时只走 <see cref="GoldFlash"/>（自绘金光）。
    /// </summary>
    public Kingmaker.ResourceLinks.PrefabLink PerUnitFx;
    /// <summary>逐单位爆发的存活时间（秒）。</summary>
    public float PerUnitFxLifetime = 3f;
    /// <summary>逐单位爆发的根缩放（1 = 原始大小；网格型爆发要缩小才给这个）。</summary>
    public float PerUnitFxRootScale = 1f;

    /// <summary>
    /// 专属特效风格："Reconstruction" / "Civilization"；为空则走旧的 <see cref="PerUnitFx"/> / <see cref="GoldFlash"/>。
    /// 非空时本动作自己收集受影响单位、交由 <see cref="MassRestoreFx"/> 统一播放（见 <see cref="RunAction"/> 末段）。
    /// </summary>
    public string Style;

    public override string GetCaption() => "群体完全恢复并复活";

    public override void RunAction()
    {
      var caster = Context.MaybeCaster;
      if (caster == null)
      {
        Dbg.Log("[RhodesDoctor] 群体恢复：MaybeCaster 为 null，动作未执行。");
        return;
      }
      UnityEngine.Vector3 center;
      if (CenterOnCaster || Context?.MainTarget == null || !Context.MainTarget.IsUnit || Context.MainTarget.Unit == null)
        center = CenterOnCaster ? caster.Position : (Context?.MainTarget?.Point ?? caster.Position);
      else
        center = Context.MainTarget.Unit.Position;
      float radius = new Feet(30).Meters;
      // ⚠ 2026-10-12：单位来源改走 `UnitScan`——**队伍成员不在区域场景状态里**
      //（他们住在 `Player.CrossSceneState`），原来只扫 `CurrentScene.AllEntityData` 会一个友军都找不到：
      // 表现就是"重构一个单位都没恢复 → 每个单位一份的特效也不出现；文明的存续看得见圣光却没效果"。
      // 详见 `UnitScan.cs` 顶部注释与 `Player.log` 证据。
      var scan = UnitScan.Take();
      var units = scan.Units;
      if (units == null)
      {
        Dbg.Log("[RhodesDoctor] 群体恢复：单位池为 null。");
        return;
      }
      var dead = new System.Collections.Generic.List<UnitEntityData>();
      // Style 非空时，这里累积"确实被恢复 / 复活"的单位，交给 MassRestoreFx 播专属特效。
      var affected = new System.Collections.Generic.List<UnitEntityData>();
      int healedAlive = 0;
      int allies = 0;   // 通过友军筛选的（不分距离）
      int inRange = 0;  // 其中在 30 尺内的
      foreach (var u in units)
      {
        if (u == null) continue;
        // R6+B17：只作用于友军（IsPlayerFaction），不作用于中立 NPC。
        // 召唤物只是不复活，其余（完全恢复、解除）照常—— alive 分支不再排除召唤物/宠物。
        // E1：宠物（动物伙伴）有 Master（UnitPartPet），召唤物走 UnitPartSummonedMonster；
        // 用 Master 排除会误伤宠物且漏掉召唤物，故复活排除改用 SummonedMonster。
        // 10-12：补 `caster.IsAlly`（与原版 `AbilityTargetsAround` 的友军判定同源）——
        // 玩家阵营的召唤物 / 同组单位也算友军，语义仍是"友军，不含中立 NPC"。
        if (!u.IsPlayerFaction && !caster.IsAlly(u)) continue;
        allies++;
        if ((u.Position - center).magnitude > radius) continue;
        inRange++;
        if (u.Descriptor.State.IsDead) { dead.Add(u); continue; }
        u.Descriptor.Damage = 0;
        healedAlive++;
        // 逐单位特效：每个被恢复的友军身上各放一发（金光 或 借来的爆发素材）。
        // Style 非空时改由 MassRestoreFx 统一播（色系 / 节奏都在那边），这里只记账。
        affected.Add(u);
        if (string.IsNullOrEmpty(Style)) SpawnPerUnitFx(u);
        // 非致命伤：DamageNonLethal 只读，但底层是 ModifiableValue，直接清全部修正即回满。
        u.Descriptor.Stats.DamageNonLethal.RemoveAllModifiers();
        // B17：只解除"可被复原术解除"的负面状态（近似：排除诅咒 / 疾病 / 毒素类）。
        foreach (var b in u.Buffs.RawFacts.ToList())
        {
          var bp = b.Blueprint;
          if (bp == null || !bp.Harmful) continue;
          var d = bp.SpellDescriptor;
          if (d.HasFlag(SpellDescriptor.Curse) || d.HasFlag(SpellDescriptor.Disease)
            || d.HasFlag(SpellDescriptor.Poison)) continue;
          b.Remove();
        }
      }
      int done = 0;
      foreach (var d in dead)
      {
        if (done >= ResurrectMax) break;
        // R6：召唤物不复活（E1：用 SummonedMonster 判，不用 Master，以免误伤动物伙伴）。
        if (d.Get<Kingmaker.UnitLogic.Parts.UnitPartSummonedMonster>() != null) continue;
        d.Descriptor.Resurrect(caster, ResurrectHealth, ResurrectHealth >= 1.0f);
        affected.Add(d);
        if (string.IsNullOrEmpty(Style)) SpawnPerUnitFx(d);
        done++;
      }
      int afterApplied = 0;
      if (AfterBuff != null)
      {
        var afterBp = AfterBuff.GetBlueprint() as Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff;
        if (afterBp == null)
        {
          Dbg.Log("[RhodesDoctor] 文明的存续：AfterBuff 蓝图为 null（GUID 解析失败）。");
        }
        else
        {
          foreach (var u in units)
          {
            // R6：后续增益与复活用同一套"友军"标准（IsPlayerFaction），中立 NPC 与召唤物之外的敌我判断一致。
            // 注：召唤物活着时也吃后续（"其余照常"），仅死后不复活；此处只排除死亡与非玩家阵营。
            if (u == null || (!u.IsPlayerFaction && !caster.IsAlly(u))) continue;
            if ((u.Position - center).magnitude > radius) continue;
            if (u.Descriptor.State.IsDead) continue; // 名额外的阵亡者不吃后续
            u.Buffs.AddBuff(afterBp, Context, System.TimeSpan.FromMinutes(1));
            afterApplied++;
          }
        }
      }
      // 专属特效（重构 / 文明的存续）：整段视觉由 MassRestoreFx 演出，逐单位的部分它自己按距离对时。
      if (!Context.DisableFx && !string.IsNullOrEmpty(Style))
      {
        int layer = caster.View != null ? caster.View.gameObject.layer : 0;
        if (Style == "Reconstruction")
          MassRestoreFx.PlayReconstruction(center, radius, affected, layer);
        else if (Style == "Civilization")
          MassRestoreFx.PlayCivilization(center, radius, caster, affected, layer);
      }
      Dbg.Log(
        $"[RhodesDoctor] 群体恢复完成：中心=({center.x:F1},{center.y:F1},{center.z:F1}) 半径={radius:F1}m"
        + $" 活着恢复={healedAlive} 复活={done}/{dead.Count}"
        + $" 后续增益挂上={afterApplied}"
        + $" PerUnitFx={(PerUnitFx != null && PerUnitFx.Exists() ? "有" : "无")}"
        + $" GoldFlash={GoldFlash}");
      // 诊断（10-12 加）：把"扫到了什么"打全——下次若还是 0，一眼能看出是池子空还是阵营筛错。
      Dbg.Log(
        $"[RhodesDoctor] 群体恢复·扫描：{scan.Describe()}；友军={allies} 其中 30 尺内={inRange}（阵亡 {dead.Count}）"
        + $"；最近单位：{UnitScan.DescribeNearest(units, center, caster)}");
    }

    /// <summary>
    /// 在一个被恢复 / 复活的友军身上放逐单位特效：
    /// 优先用借来的爆发素材（<see cref="PerUnitFx"/>），否则退到自绘金光（<see cref="GoldFlash"/>）。
    /// </summary>
    private void SpawnPerUnitFx(UnitEntityData unit)
    {
      if (Context.DisableFx || unit == null) return;
      // 借来的爆发素材：每个单位各放一发（作者 10-12：重构要"每个受影响的单位各有一份特效"）。
      if (PerUnitFx != null && PerUnitFx.Exists())
      {
        FxTint.SpawnPlaced(PerUnitFx, unit.Position + Vector3.up * 0.5f,
          Quaternion.identity, 1f, PerUnitFxLifetime, PerUnitFxRootScale);
        return;
      }
      // 退路：自绘金光。
      if (GoldFlash) SpawnRestoreFlash(unit);
    }

    /// <summary>在被恢复的友军身上放一发金光（自绘，尺寸受控；见 <see cref="SwordHitFx"/>）。</summary>
    private void SpawnRestoreFlash(UnitEntityData unit)
    {
      if (Context.DisableFx || unit == null) return;
      int layer = 0;
      var view = unit.View;
      if (view != null) layer = view.gameObject.layer;
      // Delay=0：群体恢复是即时生效的，金光当场亮。
      SwordHitFx.Spawn(unit.Position + Vector3.up * 0.9f, Tint, GoldFlashSize, layer, 0f);
    }
  }

  /// <summary>
  /// 儿时舞乐：友军增益每轮开始恢复 2d8 + 施法者等级一半。
  /// HealTarget 写不下"半级"，自写：掷骰走规则（RuleRollDice），加值现算。
  /// </summary>
  public class ContextActionAllyRegen : ContextAction
  {
    public override string GetCaption() => "友军回生（2d8+半级）";

    public override void RunAction()
    {
      var caster = Context.MaybeCaster;
      var unit = Context.MaybeOwner ?? Target.Unit;
      if (caster == null || unit == null) return;
      var roll = Context.TriggerRule(new RuleRollDice(caster,
        new DiceFormula(2, DiceType.D8)));
      int halfLevel = System.Math.Max(1,
        BlueprintCore.Utils.Types.ContextValues.Rank().Calculate(Context) / 2);
      Context.TriggerRule(new RuleHealDamage(caster, unit, roll.Result + halfLevel));
    }
  }

  /// <summary>归溟计数按轮清零（R1：按 Buff 取各自 Data 清零，不再调共用字段）。</summary>
  public class ContextActionResetAbyssCounters : ContextAction
  {
    public override string GetCaption() => "归溟计数清零";

    public override void RunAction()
    {
      var owner = Context.MaybeOwner ?? Context.MaybeCaster;
      if (owner?.Buffs == null) return;
      foreach (var b in owner.Buffs.RawFacts.ToList())
      {
        ReturnToAbyssHunger.ResetRound(b as Buff);
      }
    }
  }

  /// <summary>罪罚计数按轮清零（R1：按 Buff 取各自 Data 清零）。</summary>
  public class ContextActionResetSinCounter : ContextAction
  {
    public override string GetCaption() => "罪罚计数清零";

    public override void RunAction()
    {
      var owner = Context.MaybeOwner ?? Context.MaybeCaster;
      if (owner?.Buffs == null) return;
      foreach (var b in owner.Buffs.RawFacts.ToList())
      {
        SinPunishmentRetribution.ResetRound(b as Buff);
      }
    }
  }

  /// <summary>
  /// 终末狙击：标记正常到期时结算（R7a：被驱散则作废，不开枪）。
  /// 目标隐形则哑火（D4：掩蔽判断已去掉，隐形保留；超射程随 B7 已去掉）；
  /// 否则 15d6 穿刺 + 2d6 体质伤害。
  /// B7：去掉 100 尺硬编码限制，以法术射程（极距）为准。
  /// V9：体质伤害走 RuleDealStatDamage（属性伤害本身持久，不依赖正在移除的标记增益）。
  /// R7a：标记改用 newRound 结算（见 SpellsSetup）；被驱散则标记提前消失，newRound 不再触发。
  /// 结算后（命中/哑火均）移除标记，避免重复开枪。
  /// </summary>
  public class ContextActionFinalSnipe : ContextAction
  {
    // 原版隐形增益（标准 / 强效），覆盖法术隐形；其他隐形来源不在此列。
    internal const string VanillaInvisibility = "525f980cb29bc2240b93e953974cb325";
    internal const string VanillaGreaterInvisibility = "e6b35473a237a6045969253beb09777c";

    public override string GetCaption() => "终末狙击结算";

    private void RemoveMark(UnitEntityData target)
    {
      if (target?.Buffs == null) return;
      foreach (var b in target.Buffs.RawFacts.ToList())
      {
        if (b.Context == Context) b.Remove();
      }
    }

    public override void RunAction()
    {
      var caster = Context.MaybeCaster;
      var target = Target.Unit ?? Context.MaybeOwner;
      if (caster == null || target == null) return;
      bool invisible =
        target.Descriptor.State.HasCondition(Kingmaker.UnitLogic.UnitCondition.Invisible)
        || RhodesDoctor.Components.BuffHelper.HasActive(target,
          BlueprintTool.GetRef<BlueprintBuffReference>(VanillaInvisibility))
        || RhodesDoctor.Components.BuffHelper.HasActive(target,
          BlueprintTool.GetRef<BlueprintBuffReference>(VanillaGreaterInvisibility));
      if (invisible)
      {
        RemoveMark(target);
        return;
      }
      // 专属特效：开火曳光 + 命中闪光（纯视觉；Fire 内部自带 try/catch，失败不影响结算）。
      FinalSnipeFx.Fire(caster, target);
      // C24：补上反射减半（按骰数近似：失败 15d6、成功 7d6）。
      var save = Context.TriggerRule(new RuleSavingThrow(target, SavingThrowType.Reflex,
        Context.Params?.DC ?? 10));
      int pierceDice = save.IsPassed ? 7 : 15;
      var pierce = new DamageBundle(new PhysicalDamage(
        new ModifiableDiceFormula(new DiceFormula(pierceDice, DiceType.D6)), 0,
        Kingmaker.Enums.Damage.PhysicalDamageForm.Piercing));
      Context.TriggerRule(new RuleDealDamage(caster, target, pierce));
      // V9：属性伤害走规则（不挂在正在移除的增益上），持久生效。
      Context.TriggerRule(new RuleDealStatDamage(caster, target, StatType.Constitution,
        new DiceFormula(2, DiceType.D6), 0));
      RemoveMark(target);
    }
  }

  // 连锁殉爆的爆炸逻辑已移到 Components.cs 的 ChainDetonation.Explode（由 ChainDetonationOnDeath
  // 在持有者死亡时调用）。原先这里的 ContextActionChainExplosion 挂在原版 UnitDeathTrigger 上，
  // 而那个触发器**排除"自己死"**（HandleEvent 开头 `if (unit == Owner) return;`），
  // 所以标记者自己一死反而永远不触发（2026-10-10 实测）。留此说明，避免以后又照抄旧写法。

  /// <summary>
  /// 岁稔：友军每轮恢复 4d8 + 施法者等级，溢出部分记到本次施放那份增益 Data 里（R1；N7：按 Context 找自己那份，
  /// 不加进身上所有岁稔，两份施放各算各的，与延时爆破 Round 同做法）。
  /// </summary>
  public class ContextActionHarvestTick : ContextAction
  {
    public override string GetCaption() => "岁稔回生积攒";

    public override void RunAction()
    {
      // P1：补 try/catch —— 内部 TriggerRule(RollDice) + RawFacts 遍历任意异常会冒泡到 ability
      // action 队列迭代，可能跳过同 buff 的后续 Round 动作（对齐 ContextActionSpawnMirrorCopy 等）。
      try
      {
        var caster = Context.MaybeCaster;
        var unit = Context.MaybeOwner ?? Target.Unit;
        if (caster == null || unit == null) return;
        int lvl = System.Math.Max(1,
          BlueprintCore.Utils.Types.ContextValues.Rank().Calculate(Context));
        int roll = Context.TriggerRule(new RuleRollDice(caster,
          new DiceFormula(4, DiceType.D8))).Result;
        int heal = roll + lvl;
        int missing = System.Math.Max(0, unit.Descriptor.MaxHP - unit.Descriptor.HPLeft);
        unit.Descriptor.Damage = System.Math.Max(0, unit.Descriptor.Damage - heal);
        int overheal = System.Math.Max(0, heal - missing);
        if (overheal <= 0) return;
        foreach (var b in unit.Buffs.RawFacts.ToList())
        {
          var bf = b as Buff;
          if (bf == null) continue;
          if (bf.Context != Context) continue;
          var cr = b.GetComponentWithRuntime<HarvestYearsAccumulator>();
          if (cr.Runtime == null) continue;
          var data = cr.Runtime.GetData<HarvestYearsAccumulatorData>();
          if (data == null) continue;
          data.Stored += overheal;
          data.CasterLevel = lvl;
        }
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 岁稔回生积攒失败：\n" + e);
      }
    }
  }

  /// <summary>
  /// 紧急撤离：把目标（自己或友军）丢到**施法者身后**。落点取施法者朝向的反方向、
  /// 约 5 尺处，再用 FreePlaceSelector 推到最近的空地（避开其他单位、贴回导航网格），
  /// 不引发借机攻击（Translocate 自带）。
  /// （此前直接落到 caster.Position + 写死的 (2,0,2)，方向固定，表现为永远出现在施法者左下角。）
  /// </summary>
  public class ContextActionTeleportAllyToCaster : ContextAction
  {
    public override string GetCaption() => "紧急撤离（传送至施法者身后）";

    public override void RunAction()
    {
      var target = Target.Unit;
      if (target == null) return;

      var caster = Context.MaybeCaster ?? target;
      Vector3 back = caster.OrientationDirection; // 朝向（水平）
      back.y = 0f;
      back = back.sqrMagnitude > 0.01f ? -back.normalized : Vector3.back;

      Vector3 here = target.Position;
      Vector3 desired = caster.Position + back * 1.5f; // 约 5 尺，正后方
      desired.y = here.y; // FreePlaceSelector 只改 x/z，y 沿用目标当前高度

      var points = new[] { desired };
      var radii = new[] { target.View != null ? target.View.Corpulence : 0.5f };
      FreePlaceSelector.RelaxPoints(points, radii, 1, desired);
      Vector3 dest = points[0];
      dest.y = here.y;
      target.Translocate(dest, target.Orientation);
      // 专属特效：起点拆解 → 引导弧线 → 落点重组（纯视觉；Play 内部自带 try/catch，失败不影响结算）。
      EmergencyExtractionFx.Play(target, here, dest);
    }
  }

  /// <summary>
  /// 耀阳颔首友军侧：解除目盲（移除带目盲的增益；状态随增益移除自动清除）。
  /// </summary>
  public class ContextActionRemoveBlindness : ContextAction
  {
    public override string GetCaption() => "解除目盲";

    public override void RunAction()
    {
      var unit = Target.Unit ?? Context.MaybeOwner;
      if (unit?.Buffs == null) return;
      foreach (var b in unit.Buffs.RawFacts.ToList())
      {
        var bb = b.Blueprint;
        if (bb?.ComponentsArray == null) continue;
        foreach (var c in bb.ComponentsArray)
        {
          if (c is Kingmaker.UnitLogic.FactLogic.AddCondition ac
            && ac.Condition == UnitCondition.Blindness)
          {
            b.Remove();
            break;
          }
          // V11：原版目盲有相当一部分走 BuffStatusCondition（Glitterdust / Power Word Blind 等），
          // 旧版只认 AddCondition，漏掉这些。这里一并纳入。
          if (c is Kingmaker.Designers.Mechanics.Buffs.BuffStatusCondition bsc
            && bsc.Condition == UnitCondition.Blindness)
          {
            b.Remove();
            break;
          }
        }
      }
    }
  }
  /// <summary>
  /// C16：Mon3tr 解除震慑、恍惚、麻痹、恶心（设计文档第 182 行）。
  /// 复用 RemoveBlindness 模式：同时认 AddCondition 与 BuffStatusCondition。
  /// </summary>
  public class ContextActionRemoveMon3trConditions : ContextAction
  {
    public override string GetCaption() => "解除震慑/恍惚/麻痹/恶心";

    private static bool IsMon3trCondition(UnitCondition c)
      => c == UnitCondition.Stunned || c == UnitCondition.Dazed
        || c == UnitCondition.Paralyzed || c == UnitCondition.Nauseated;

    public override void RunAction()
    {
      var unit = Target.Unit ?? Context.MaybeOwner;
      if (unit?.Buffs == null) return;
      foreach (var b in unit.Buffs.RawFacts.ToList())
      {
        var bb = b.Blueprint;
        if (bb?.ComponentsArray == null) continue;
        foreach (var c in bb.ComponentsArray)
        {
          if (c is Kingmaker.UnitLogic.FactLogic.AddCondition ac
            && IsMon3trCondition(ac.Condition))
          {
            b.Remove();
            break;
          }
          if (c is Kingmaker.Designers.Mechanics.Buffs.BuffStatusCondition bsc
            && IsMon3trCondition(bsc.Condition))
          {
            b.Remove();
            break;
          }
        }
      }
    }
  }
  /// <summary>
  /// 移除同一施法者施加的指定增益。集火更换目标时使用（同一时间只有一个标记）。
  /// 用 UnitScan 统一全局单位池（State.Units + AllCrossSceneUnits + Party），本 mod 其它动作
  /// （ContextActionStealBuffsCustom / ContextActionMassRestore / PaintedWorldBanishTracker.FindUnit）
  /// 已统一走 UnitScan，本动作对齐以免"换组新单位扫描方式"后再次踩坑。
  /// </summary>
  public class ContextActionRemoveMyBuffs : ContextAction
  {
    public BlueprintBuffReference Buff;

    public override string GetCaption() => "移除我施加的旧标记";

    public override void RunAction()
    {
      var caster = Context.MaybeCaster;
      if (caster == null || Buff == null) return;
      foreach (var unit in UnitScan.Take().Units)
      {
        if (unit?.Buffs == null) continue;
        foreach (var buff in unit.Buffs.RawFacts.ToList())
        {
          if (buff.Blueprint != null
              && buff.Blueprint.AssetGuid == Buff.Guid
              && buff.Context?.MaybeCaster == caster)
            buff.Remove();
        }
      }
    }
  }

  /// <summary>
  /// C2：把失败目标传送到法术点附近（牵引用）。用 FreePlaceSelector 找最近空地（开发笔记第 3 条），
  /// 不引发借机攻击（Translocate 自带）。
  /// </summary>
  public class ContextActionTeleportToPoint : ContextAction
  {
    public override string GetCaption() => "牵引：传送至法术点";

    public override void RunAction()
    {
      var unit = Target.Unit ?? Context.MaybeOwner;
      if (unit == null) return;
      var point = Context?.MainTarget?.Point ?? unit.Position;
      var points = new[] { point };
      var radii = new[] { unit.View != null ? unit.View.Corpulence : 0.5f };
      FreePlaceSelector.RelaxPoints(points, radii, 1, point);
      var dest = points[0];
      dest.y = unit.Position.y;
      unit.Translocate(dest, unit.Orientation);
    }
  }

  /// <summary>
  /// 王者冲锋（作者 10-11 的"替代办法"）：一次自定义动作里**同时**完成
  /// ①施法者沿直线冲到落点（≤ <see cref="MaxDistanceFeet"/> 尺）、②**路径上**的敌人受 4d6 轰击（反射失败倒地）。
  ///
  /// 为什么不用原版 `AbilityCustomCharge`：挂上去后**整个法术直接无效**（作者实测）。
  /// 也不用"直线投射物 + 传送"：两者都是投递组件、会打架。这里干脆全部自己算——
  /// 从施法者位置到落点拉一条线段，命中"到线段距离 ≤ <see cref="HalfWidthFeet"/> 尺"的敌人，
  /// 最后把施法者 `Translocate` 到落点（`FreePlaceSelector` 保证落点是空地）。
  /// </summary>
  public class ContextActionRoyalCharge : ContextAction
  {
    /// <summary>最远冲锋距离（尺）。</summary>
    public float MaxDistanceFeet = 40f;
    /// <summary>路径判定半宽（尺）：到"施法者→落点"线段的距离小于它就算在路径上。</summary>
    public float HalfWidthFeet = 5f;

    public override string GetCaption() => "王者冲锋：冲锋并撞击路径上的敌人";

    public override void RunAction()
    {
      var caster = Context.MaybeCaster;
      if (caster == null) return;
      Vector3 start = caster.Position;
      Vector3 aim = Target != null ? Target.Point : (Context?.MainTarget?.Point ?? start);

      // 水平方向 + 距离上限
      Vector3 flat = aim - start; flat.y = 0f;
      if (flat.sqrMagnitude < 0.01f) return;
      float maxMeters = new Feet(MaxDistanceFeet).Meters;
      Vector3 dir = flat.normalized;
      float dist = Mathf.Min(maxMeters, flat.magnitude);
      Vector3 dest = start + dir * dist;

      // 落点找空地（避开单位、贴回导航网格）
      var points = new[] { dest };
      var radii = new[] { caster.View != null ? caster.View.Corpulence : 0.5f };
      FreePlaceSelector.RelaxPoints(points, radii, 1, dest);
      dest = points[0];
      dest.y = start.y;

      // 路径上的敌人：到线段 start→dest 的水平距离 ≤ 半宽
      float half = new Feet(HalfWidthFeet).Meters;
      float travel = (dest - start).magnitude;
      int dc = Context.Params?.DC ?? 10;
      var units = UnitScan.Take().Units;
      if (units != null && travel > 0.01f)
      {
        Vector3 seg = (dest - start) / travel;
        foreach (var u in units)
        {
          if (u == null || u == caster || u.Descriptor.State.IsDead) continue;
          if (!u.IsEnemy(caster)) continue;
          Vector3 rel = u.Position - start; rel.y = 0f;
          float t = Mathf.Clamp(Vector3.Dot(rel, seg), 0f, travel);
          Vector3 proj = start + seg * t;
          Vector3 d = u.Position - proj; d.y = 0f;
          if (d.magnitude > half) continue;

          var save = Context.TriggerRule(new RuleSavingThrow(u, SavingThrowType.Reflex, dc));
          var bundle = new DamageBundle(new PhysicalDamage(
            new ModifiableDiceFormula(new DiceFormula(4, DiceType.D6)), 0, PhysicalDamageForm.Bludgeoning));
          Context.TriggerRule(new RuleDealDamage(caster, u, bundle));
          if (!save.IsPassed)
          {
            var prone = BlueprintCore.Utils.BlueprintTool.GetRef<Kingmaker.Blueprints.BlueprintBuffReference>("24cf3deb078d3df4d92ba24b176bda97");
            var bp = prone?.Get();
            if (bp != null)
              u.Buffs.AddBuff((Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff)bp, Context, null);
          }
        }
      }

      // 冲锋轨迹（作者 10-16）：金色光痕 + 贴地尘带 + 沿途土石冲击 + 身上金色加速拖尾，纯视觉。
      if (!Context.DisableFx)
        RhodesDoctor.RoyalChargeFx.Play(start, dest, caster.View != null ? caster.View.transform : null);

      // 施法者移动（最后做，避免用移动后的位置算路径）；落地后面朝冲锋方向。
      float facing = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
      caster.Translocate(dest, facing);
    }
  }

  /// <summary>
  /// 王者冲锋（作者 10-11 定案："**施法者移动到落点**"）：把**施法者**传送到本次点击的落点
  /// （用 FreePlaceSelector 推离单位 / 贴回导航网格，避免落点被占）。
  /// ⚠ 已被 <see cref="ContextActionRoyalCharge"/> 取代（后者同时做路径伤害），保留备用。
  /// </summary>
  public class ContextActionTeleportCasterToPoint : ContextAction
  {
    public override string GetCaption() => "王者冲锋：施法者移动到落点";

    public override void RunAction()
    {
      var caster = Context.MaybeCaster;
      if (caster == null) return;
      var point = Target != null ? Target.Point : (Context?.MainTarget?.Point ?? caster.Position);
      var points = new[] { point };
      var radii = new[] { caster.View != null ? caster.View.Corpulence : 0.5f };
      FreePlaceSelector.RelaxPoints(points, radii, 1, point);
      var dest = points[0];
      dest.y = caster.Position.y;
      caster.Translocate(dest, caster.Orientation);
    }
  }

  /// <summary>
  /// C33：延时爆破引爆（R1：用独立 DelayedBlastTracker Data，不借用炮火）。
  /// 在 10 尺内对敌人造成 2d6 火焰 + 2d6 钝击（反射减半），随后移除对应追踪增益。
  /// 手动引爆（自由动作能力）与自然触发（下轮开始）都走这里；手动不要求附近有敌，-round 要求见 Round。
  /// N3：只读延时爆破自己的记录，任何情况下都不碰炮火延伸（炸弹是本轮新功能，不存在旧存档，无兼容退路）。
  /// </summary>
  public class ContextActionDelayedBlastBurst : ContextAction
  {
    public BlueprintBuffReference Marker;

    public override string GetCaption() => "延时爆破：引爆";

    public override void RunAction()
    {
      var caster = Context.MaybeCaster;
      var owner = Context.MaybeOwner ?? caster;
      if (caster == null || owner == null) return;
      var trackers = new System.Collections.Generic.List<(Buff buff, UnityEngine.Vector3 point, int dc)>();
      foreach (var b in owner.Buffs.RawFacts.ToList())
      {
        var bf = b as Buff;
        if (bf == null) continue;
        var cr = b.GetComponentWithRuntime<DelayedBlastTracker>();
        if (cr.Runtime == null) continue;
        var data = cr.Runtime.GetData<DelayedBlastTrackerData>();
        if (data == null) continue;
        trackers.Add((bf, data.Point, data.SaveDC));
      }
      if (trackers.Count == 0) return;
      var scene = Game.Instance?.CurrentScene ?? Game.Instance?.LoadedAreaState;
      var units = scene?.AllEntityData?.OfType<UnitEntityData>();
      if (units == null) return;
      var allUnits = units.ToList();
      foreach (var t in trackers)
      {
        foreach (var u in allUnits)
        {
          if (u == null || u.Descriptor == null) continue;
          float d = (u.Position - t.point).magnitude;
          bool dead = u.Descriptor.State.IsDead;
          bool ise = !dead && u.IsEnemy(caster);
          if (dead || !ise) continue;
          if (d > new Feet(10).Meters) continue;
          var save = Context.TriggerRule(new RuleSavingThrow(u, SavingThrowType.Reflex, t.dc));
          int dice = save.IsPassed ? 1 : 2;
          var bundle = new DamageBundle(new BaseDamage[]
          {
            new EnergyDamage(new ModifiableDiceFormula(new DiceFormula(dice, DiceType.D6)), 0, DamageEnergyType.Fire),
            new PhysicalDamage(new ModifiableDiceFormula(new DiceFormula(dice, DiceType.D6)), 0, PhysicalDamageForm.Bludgeoning),
          });
          Context.TriggerRule(new RuleDealDamage(caster, u, bundle));
        }
        BombFx.BoomAt(t.point);
        // 标记由 tracker 组件的 OnDeactivate（t.buff.Remove 时）删除。
        t.buff.Remove();
      }
      if (Marker != null)
      {
        var mb = Marker.Get() as Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff;
        if (mb != null) owner.Buffs.RemoveFact(mb);
      }
    }
  }

  /// <summary>
  /// C33：延时爆破自然触发检查（挂在追踪增益的 newRound）。仅当 10 尺内有敌人才引爆；
  /// 无敌则保留炸弹。爆炸延迟至下轮开始由 newRound 时机天然保证（布设当轮不触发）。
  /// </summary>
  public class ContextActionDelayedBlastRound : ContextAction
  {
    public override string GetCaption() => "延时爆破：下轮检查";

    public override void RunAction()
    {
      var caster = Context.MaybeCaster;
      var owner = Context.MaybeOwner ?? caster;
      if (caster == null || owner == null) return;
      // 找到当前 newRound 所属的那一份追踪（按 Context 反查，避免一次清掉所有炸弹）。
      Buff self = null;
      UnityEngine.Vector3 point = owner.Position;
      int saveDC = 10;
      bool found = false;
      foreach (var b in owner.Buffs.RawFacts.ToList())
      {
        var bf = b as Buff;
        if (bf == null) continue;
        if (bf.Context != Context) continue;
        var cr = b.GetComponentWithRuntime<DelayedBlastTracker>();
        if (cr.Runtime == null) continue;
        var data = cr.Runtime.GetData<DelayedBlastTrackerData>();
        if (data == null) continue;
        self = bf;
        point = data.Point;
        saveDC = data.SaveDC;
        found = true;
        break;
      }
      if (!found) return;
      var scene = Game.Instance?.CurrentScene ?? Game.Instance?.LoadedAreaState;
      var units = scene?.AllEntityData?.OfType<UnitEntityData>();
      if (units == null) return;
      bool enemyNear = false;
      foreach (var u in units.ToList())
      {
        if (u == null || u.Descriptor.State.IsDead || !u.IsEnemy(caster)) continue;
        if ((u.Position - point).magnitude <= new Feet(10).Meters) { enemyNear = true; break; }
      }
      if (!enemyNear) return;
      foreach (var u in units.ToList())
      {
        if (u == null || u.Descriptor.State.IsDead || !u.IsEnemy(caster)) continue;
        if ((u.Position - point).magnitude > new Feet(10).Meters) continue;
        var save = Context.TriggerRule(new RuleSavingThrow(u, SavingThrowType.Reflex, saveDC));
        int dice = save.IsPassed ? 1 : 2;
        var bundle = new DamageBundle(new BaseDamage[]
        {
          new EnergyDamage(new ModifiableDiceFormula(new DiceFormula(dice, DiceType.D6)), 0, DamageEnergyType.Fire),
          new PhysicalDamage(new ModifiableDiceFormula(new DiceFormula(dice, DiceType.D6)), 0, PhysicalDamageForm.Bludgeoning),
        });
        Context.TriggerRule(new RuleDealDamage(caster, u, bundle));
      }
      BombFx.BoomAt(point);
      // 标记由 tracker 组件的 OnDeactivate（self.Remove 时）删除。
      self.Remove();
    }
  }

  /// <summary>
  /// C11：火山余焰（挂在敌人身上 2 轮，每轮开始单独豁免：每 4 级 1d6 火焰，最多 5d6，反射减半按骰数）。
  /// 按本次施放找自己那份（N7：bf.Context==Context），两份施放各算各的。
  /// </summary>
  public class ContextActionVolcanoEmber : ContextAction
  {
    public override string GetCaption() => "火山：余焰";

    public override void RunAction()
    {
      var caster = Context.MaybeCaster;
      var unit = Target.Unit ?? Context.MaybeOwner;
      if (caster == null || unit == null) return;
      if (unit.Descriptor.State.IsDead) return;
      int lvl = System.Math.Max(1,
        BlueprintCore.Utils.Types.ContextValues.Rank().Calculate(Context));
      int dice = System.Math.Min(5, System.Math.Max(1, lvl / 4));
      int dc = Context.Params?.DC ?? 10;
      var save = Context.TriggerRule(new RuleSavingThrow(unit, SavingThrowType.Reflex, dc));
      int d = save.IsPassed ? System.Math.Max(1, dice / 2) : dice;
      var bundle = new DamageBundle(new EnergyDamage(
        new ModifiableDiceFormula(new DiceFormula(d, DiceType.D6)), 0, DamageEnergyType.Fire));
      Context.TriggerRule(new RuleDealDamage(caster, unit, bundle));
    }
  }

  /// <summary>
  /// C19：黄昏熔火（挂在敌人身上 2 轮，每轮开始单独豁免：4d6 火焰，反射减半按骰数 2d6/4d6）。
  /// </summary>
  public class ContextActionTwilightMelt : ContextAction
  {
    public override string GetCaption() => "黄昏：熔火";

    public override void RunAction()
    {
      var caster = Context.MaybeCaster;
      var unit = Target.Unit ?? Context.MaybeOwner;
      if (caster == null || unit == null) return;
      if (unit.Descriptor.State.IsDead) return;
      int dc = Context.Params?.DC ?? 10;
      var save = Context.TriggerRule(new RuleSavingThrow(unit, SavingThrowType.Reflex, dc));
      int d = save.IsPassed ? 2 : 4;
      var bundle = new DamageBundle(new EnergyDamage(
        new ModifiableDiceFormula(new DiceFormula(d, DiceType.D6)), 0, DamageEnergyType.Fire));
      Context.TriggerRule(new RuleDealDamage(caster, unit, bundle));
    }
  }

  /// <summary>
  /// C13：移除隐形（闪光弹显形 1 轮的前置：去掉目标身上带隐形的增益；状态随增益移除自动清除）。
  /// 复用 RemoveBlindness 模式：同时认 AddCondition 与 BuffStatusCondition 的 Invisible/GreaterInvisibility。
  /// </summary>
  public class ContextActionRemoveInvisibility : ContextAction
  {
    public override string GetCaption() => "移除隐形";

    public override void RunAction()
    {
      var unit = Target.Unit ?? Context.MaybeOwner;
      if (unit?.Buffs == null) return;
      foreach (var b in unit.Buffs.RawFacts.ToList())
      {
        var bb = b.Blueprint;
        if (bb?.ComponentsArray == null) continue;
        foreach (var c in bb.ComponentsArray)
        {
          if (c is Kingmaker.UnitLogic.FactLogic.AddCondition ac
            && (ac.Condition == UnitCondition.Invisible || ac.Condition == UnitCondition.GreaterInvisibility))
          {
            b.Remove();
            break;
          }
          if (c is Kingmaker.Designers.Mechanics.Buffs.BuffStatusCondition bsc
            && (bsc.Condition == UnitCondition.Invisible || bsc.Condition == UnitCondition.GreaterInvisibility))
          {
            b.Remove();
            break;
          }
        }
      }
    }
  }

  /// <summary>
  /// C21：大地之怒（10d6 轰击，反射部分通过；失败者倒地；召唤物受双倍伤害）。
  /// 召唤物判定用 UnitPartSummonedMonster（R6/E1，不用 Master）。
  /// 倒地用原版倒地增益（王者冲锋/牵引同款 24cf3deb…）。
  ///
  /// 作者 10-11：法术改成**以施法者为中心**（不再挂 `AbilityTargetsAround`，否则视觉区域会按目标数重复生成），
  /// 所以这里改成**自扫场景**：遍历施法者 <see cref="RadiusFeet"/> 尺内的所有敌人逐个结算。
  /// </summary>
  public class ContextActionEarthFuryHit : ContextAction
  {
    /// <summary>以施法者为中心的半径（尺）。</summary>
    public float RadiusFeet = 30f;

    public override string GetCaption() => "大地之怒（以施法者为中心）";

    public override void RunAction()
    {
      var caster = Context.MaybeCaster;
      if (caster == null) return;
      float radius = new Feet(RadiusFeet).Meters;
      var scene = Game.Instance?.CurrentScene ?? Game.Instance?.LoadedAreaState;
      var units = scene?.AllEntityData?.OfType<UnitEntityData>();
      if (units == null) return;
      int dc = Context.Params?.DC ?? 10;
      foreach (var unit in units.ToList())
      {
        if (unit == null || unit.Descriptor.State.IsDead) continue;
        if (!unit.IsEnemy(caster)) continue;
        if ((unit.Position - caster.Position).magnitude > radius) continue;
        var save = Context.TriggerRule(new RuleSavingThrow(unit, SavingThrowType.Reflex, dc));
        bool isSummon = unit.Get<Kingmaker.UnitLogic.Parts.UnitPartSummonedMonster>() != null;
        int dice = save.IsPassed ? 5 : 10;
        if (isSummon) dice *= 2;
        var bundle = new DamageBundle(new PhysicalDamage(
          new ModifiableDiceFormula(new DiceFormula(dice, DiceType.D6)), 0, PhysicalDamageForm.Bludgeoning));
        Context.TriggerRule(new RuleDealDamage(caster, unit, bundle));
        if (!save.IsPassed)
        {
          var prone = BlueprintCore.Utils.BlueprintTool.GetRef<Kingmaker.Blueprints.BlueprintBuffReference>("24cf3deb078d3df4d92ba24b176bda97");
          var bp = prone?.Get();
          if (bp != null)
            unit.Buffs.AddBuff((Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff)bp, Context, null);
        }
      }
    }
  }

  /// <summary>
  /// C17：黑蛇之焰持续灼烧（挂在敌人身上，每轮 3d6 火焰，无豁免）。
  /// G6 已做：照原版焰击术，一半火焰 + 一半神圣（各带 half；火免仅减半），成败都挂本灼烧（T45 更新）。
  /// 作者 10-11：**每造成一次伤害触发一次特效**——每次结算后就在目标身上放一发黑焰
  /// （原来只有施法瞬间那一发，之后每轮只掉血、没特效）。
  /// </summary>
  public class ContextActionBlackSnakeBurn : ContextAction
  {
    /// <summary>黑焰素材（借原版焰击术的火焰爆发，见 <see cref="FxLibrary.Fire"/>）。</summary>
    public PrefabLink Fx;
    /// <summary>重染色（黑蛇之焰的**暗紫黑**，与施法瞬间那一发同色）。</summary>
    public UnityEngine.Color Tint = new UnityEngine.Color(0.30f, 0.10f, 0.38f, 1f);
    public float Lift = 0.10f;
    public float Lifetime = 1.6f;

    public override string GetCaption() => "黑蛇之焰：灼烧";

    public override void RunAction()
    {
      var caster = Context.MaybeCaster;
      var unit = Target.Unit ?? Context.MaybeOwner;
      if (caster == null || unit == null) return;
      if (unit.Descriptor.State.IsDead) return;
      var bundle = new DamageBundle(new EnergyDamage(
        new ModifiableDiceFormula(new DiceFormula(3, DiceType.D6)), 0, DamageEnergyType.Fire));
      Context.TriggerRule(new RuleDealDamage(caster, unit, bundle));
      // 每造成一次伤害 → 目标身上放一发黑焰。
      if (!Context.DisableFx && Fx != null && Fx.Exists())
        FxTint.Spawn(Fx, unit.Position + Vector3.up * 0.9f, Tint, Lift, Lifetime);
    }
  }

  /// <summary>
  /// C22：穿云（20d6 穿刺，反射减半，忽略伤害减免）。
  /// 实现：本次 RuleDealDamage 直接 IgnoreDamageReduction（规则层字段，只作用于本次伤害）。
  /// N14：直线只作用于敌人（耀阳颔首同款敌我判断，动作内直接返回）。
  /// </summary>
  public class ContextActionCloudpiercerHit : ContextAction
  {
    public override string GetCaption() => "穿云：破甲";

    public override void RunAction()
    {
      var caster = Context.MaybeCaster;
      var unit = Target.Unit;
      if (caster == null || unit == null) return;
      if (unit.Descriptor.State.IsDead || !unit.IsEnemy(caster)) return;
      int dc = Context.Params?.DC ?? 10;
      var save = Context.TriggerRule(new RuleSavingThrow(unit, SavingThrowType.Reflex, dc));
      int dice = save.IsPassed ? 10 : 20;
      var bundle = new DamageBundle(new PhysicalDamage(
        new ModifiableDiceFormula(new DiceFormula(dice, DiceType.D6)), 0, PhysicalDamageForm.Piercing));
      var evt = new RuleDealDamage(caster, unit, bundle) { IgnoreDamageReduction = true };
      Context.TriggerRule(evt);
    }
  }

  /// <summary>
  /// C15：冰封领域单次结算（敌人进入区域或在区域内开始回合：4d6 寒冷；失败定身 1 轮，成功速度减半 1 轮）。
  /// 定身用 Entangled；减半用 BuffMovementSpeed 半速（E6：IronBody 同款 Value0/Cap0.5），由调用方挂增益，这里只做伤害+挂增益。
  /// 本动作只做伤害，增益由 SpellsSetup 的条件分支挂（失败 Entangled，成功半速），保证豁免结果可读。
  /// </summary>
  public class ContextActionFrozenDomainHit : ContextAction
  {
    public Kingmaker.Blueprints.BlueprintBuffReference EntangledBuff;
    public Kingmaker.Blueprints.BlueprintBuffReference HalfSpeedBuff;

    public override string GetCaption() => "冰封领域";

    public override void RunAction()
    {
      var caster = Context.MaybeCaster;
      var unit = Target.Unit;
      if (caster == null || unit == null) return;
      if (unit.Descriptor.State.IsDead || !unit.IsEnemy(caster)) return;
      int dc = Context.Params?.DC ?? 10;
      var save = Context.TriggerRule(new RuleSavingThrow(unit, SavingThrowType.Reflex, dc));
      int dice = save.IsPassed ? 2 : 4;
      var bundle = new DamageBundle(new EnergyDamage(
        new ModifiableDiceFormula(new DiceFormula(dice, DiceType.D6)), 0, DamageEnergyType.Cold));
      Context.TriggerRule(new RuleDealDamage(caster, unit, bundle));
      if (save.IsPassed)
      {
        if (HalfSpeedBuff != null)
        {
          var bp = HalfSpeedBuff.Get() as Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff;
          if (bp != null) unit.Buffs.AddBuff(bp, Context, System.TimeSpan.FromSeconds(6));
        }
      }
      else
      {
        if (EntangledBuff != null)
        {
          var bp = EntangledBuff.Get() as Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff;
          if (bp != null) unit.Buffs.AddBuff(bp, Context, System.TimeSpan.FromSeconds(6));
        }
      }
    }
  }

  /// <summary>
  /// 剑雨的"受击特效"：在被本次伤害打中的敌人身上炸一小下金光。
  ///
  /// 为什么要有它：剑雨原先没有任何逐目标命中特效，敌人身上看到的只有原版那套受击反馈，
  /// 而原版反馈受伤害类型 / 血型 / 是否 AoE / 单位状态影响，**会时不时什么都不出**
  /// （用户实测："受到伤害但没触发特效，很随机"）。现在特效挂进和伤害**同一个动作分支**
  /// （SavingThrow 的 failed / succeed 两支），只要伤害结算了，特效就一定跟着放。
  ///
  /// 逐目标生效：本动作在"每目标一份"的动作列表里跑（与 ContextActionEarthFuryHit 同款），
  /// 所以 <c>Target.Unit</c> 就是当前被打中的那个敌人。
  /// </summary>
  public class ContextActionSwordHitFx : ContextAction
  {
    /// <summary>金光颜色（由 SpellsSetup 传 SwordTint 进来）。</summary>
    public UnityEngine.Color Tint = UnityEngine.Color.white;

    /// <summary>光斑直径（米）：默认按人形敌人身高给。</summary>
    public float Size = 1.2f;

    /// <summary>
    /// 亮起延迟（秒）：**必须等于剑雨的剑下落时间**（`SpellsSetup` 里的 `FallSeconds`，当前 0.4）。
    /// 伤害在施法瞬间结算，剑 0.4 s 后才砸到人身上；不延迟的话就成了"光先闪、剑还在天上"。
    /// </summary>
    public float Delay = 0.4f;

    public override string GetCaption() => "剑雨：命中金光";

    public override void RunAction()
    {
      if (Context.DisableFx) return;
      var unit = Target?.Unit;
      Vector3 point;
      int layer = 0;
      if (unit != null)
      {
        point = unit.Position + Vector3.up * 0.9f;     // 脚底往上到胸口
        var view = unit.View;
        if (view != null) layer = view.gameObject.layer;
      }
      else if (Target != null)
      {
        point = Target.Point + Vector3.up * 0.9f;
      }
      else
      {
        var caster = Context.MaybeCaster;
        if (caster == null) return;
        point = caster.Position + Vector3.up * 0.9f;
      }

      SwordHitFx.Spawn(point, Tint, Size, layer, Delay);
    }
  }

  /// <summary>
  /// 开启巴别塔的恶灵形态（**硬互斥**版，2026-10-11）。
  ///
  /// 规则上"开启时选择敏捷或体质形态，直到结束前不可更换"，而**同时只能有一个形态**。
  /// 原来靠能力上的 `AbilityCasterHasNoActiveFacts`（`IAbilityCasterRestriction`，只管"按钮能不能点"）
  /// —— 作者实测 **20 级时两个形态能被连续点开并共存**，说明这道闸门在实际点按路径上没拦住。
  /// 本动作把判断挪到**动作执行时**：
  ///   · 施法者身上已有**任意**形态增益（六个之任一，含自己要开的那个）→ 什么都不做（记一条日志）；
  ///   · 都没有 → 按 `ContextActionApplyBuff{Permanent:true}` 的语义，把形态增益永久挂到**施法者**身上。
  /// 于是不管 UI 层判不判，状态上永远只有一个形态。
  /// ⚠ 有意**不**在这里删别的形态（"切形态"会触发旧增益的结束动作=余波，人还在恶灵里被"恶心"，见 V10）。
  /// </summary>
  public class ContextActionApplySpecterForm : ContextAction
  {
    /// <summary>本次要开的形态增益。</summary>
    public BlueprintBuffReference FormBuff;
    /// <summary>全部六个形态增益的 GUID（**去横线小写**，敏捷/体质 × 一/二/三段），用于"已有形态就不开"。</summary>
    public string[] FormBuffGuids = new string[0];
    /// <summary>另外"有就不能开"的增益 GUID（同上格式）：恶灵余波限制（规则：余波期间不能重开恶灵）。</summary>
    public string[] BlockedBuffGuids = new string[0];

    public override string GetCaption() => "开启恶灵形态（已有形态则忽略）";

    /// <summary>蓝图 GUID 统一成"去横线小写"再比对（与 SpellsSetup.Norm 同一套写法，不依赖引用对象的类型解析）。</summary>
    internal static string Norm(BlueprintGuid guid) => guid.ToString().Replace("-", "").ToLowerInvariant();

    public override void RunAction()
    {
      var caster = Context?.MaybeCaster ?? Context?.MaybeOwner;
      if (caster == null) { Main.Mod.Logger.Error("[RhodesDoctor] 开启恶灵形态：找不到施法者。"); return; }
      var bp = FormBuff?.Get() as Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff;
      if (bp == null) { Main.Mod.Logger.Error("[RhodesDoctor] 开启恶灵形态：形态增益引用为空。"); return; }

      bool hasCapstone = RhodesDoctor.Components.BuffHelper.HasActive(
        caster, BlueprintCore.Utils.BlueprintTool.GetRef<BlueprintUnitFactReference>(Guids.CapstoneBuff));

      foreach (var b in caster.Buffs.RawFacts.ToList())
      {
        if (b?.Blueprint == null || b.IsSuppressed) continue;
        var guid = Norm(b.Blueprint.AssetGuid);
        bool inForms = Array.IndexOf(FormBuffGuids, guid) >= 0;
        bool inBlocked = !hasCapstone && Array.IndexOf(BlockedBuffGuids, guid) >= 0;
        if (inForms || inBlocked)
        {
          Main.Mod.Logger.Log($"[RhodesDoctor] 恶灵形态：身上已有「{b.Blueprint.name}」，忽略本次开启（{bp.name}）。");
          return;
        }
      }

      var applied = caster.Buffs.AddBuff(bp, Context, null);   // null = 永久（同 ContextActionApplyBuff.Permanent）
      Main.Mod.Logger.Log(applied != null
        ? $"[RhodesDoctor] 恶灵形态已开启：{bp.name}"
        : $"[RhodesDoctor] 恶灵形态开启失败（AddBuff 返回 null）：{bp.name}");
    }
  }

  /// <summary>
  /// 结束巴别塔的恶灵：**在一次 RunAction 里**移除全部六个形态增益（2026-10-11）。
  ///
  /// 为什么不用原版的"六个 `ContextActionRemoveBuff` 排一队"：
  /// `结束恶灵` 能力本身是**由形态增益通过 `AddFacts` 授予**的 —— 删掉那个形态增益的瞬间能力就没了，
  /// 作者实测表现是"**点一次只取消一个形态，另一个还在**"（动作队列在能力被销毁后被中断）。
  /// 本动作先在快照里挑出目标、再在同一帧内**一次性**发起全部 `Remove()`，
  /// 不把控制权交回动作队列，所以六个都能删掉。余波由各形态增益自己的 `deactivated` 动作负责。
  /// </summary>
  public class ContextActionEndSpecterForms : ContextAction
  {
    /// <summary>六个形态增益的 GUID（**去横线小写**）。</summary>
    public string[] FormBuffGuids = new string[0];

    public override string GetCaption() => "结束恶灵（移除全部形态增益）";

    public override void RunAction()
    {
      var caster = Context?.MaybeCaster ?? Context?.MaybeOwner ?? Target?.Unit;
      if (caster == null) { Main.Mod.Logger.Error("[RhodesDoctor] 结束恶灵：找不到施法者。"); return; }

      var hits = caster.Buffs.RawFacts.ToList()
        .Where(b => b?.Blueprint != null
          && Array.IndexOf(FormBuffGuids, ContextActionApplySpecterForm.Norm(b.Blueprint.AssetGuid)) >= 0)
        .ToList();

      foreach (var b in hits)
      {
        try { b.Remove(); }
        catch (Exception e) { Main.Mod.Logger.Error($"[RhodesDoctor] 结束恶灵：移除 {b.Blueprint?.name} 失败：{e.Message}"); }
      }

      Main.Mod.Logger.Log($"[RhodesDoctor] 结束恶灵：移除 {hits.Count} 个形态增益"
        + (hits.Count > 0 ? $"（{string.Join(", ", hits.Select(b => b.Blueprint.name).ToArray())}）" : "（身上没有形态增益）"));
    }
  }

  /// <summary>
  /// 牵制射击（0 环·克洛丝）：播放专属自绘弩矢飞射与命中针刺爆闪特效。
  /// </summary>
  public class ContextActionSuppressingShotFx : ContextAction
  {
    public override string GetCaption() => "牵制射击·自绘弩箭飞射特效";

    public override void RunAction()
    {
      var caster = Context?.MaybeCaster;
      var target = Target?.Unit;
      if (caster != null && target != null)
        SuppressingShotFx.PlayShot(caster, target);
    }
  }

  /// <summary>
  /// 王冠的回响（10 环·特雷西娅）：播放专属自绘黑冠悬浮 + 60 尺精神织网扩散 + 敌方增益剥离索取特效。
  /// </summary>
  public class ContextActionCrownEchoFx : ContextAction
  {
    public override string GetCaption() => "王冠的回响·黑冠精神织网特效";

    public override void RunAction()
    {
      var caster = Context?.MaybeCaster ?? Context?.MaybeOwner ?? Target?.Unit;
      if (caster != null)
        CrownEchoFx.Play(caster, caster.Position, CrownEchoFx.DefaultRadius);
    }
  }

  /// <summary>
  /// 影霄·拔刀（9 环·阿米娅）：播放专属自绘空间断裂斩击与黑红晶体崩散特效。
  /// </summary>
  public class ContextActionShadowBladeSlashFx : ContextAction
  {
    public override string GetCaption() => "影霄·拔刀·自绘空间斩裂特效";

    public override void RunAction()
    {
      var caster = Context?.MaybeCaster;
      var target = Target?.Unit;
      if (target != null)
        ShadowBladeFx.PlaySlash(caster, target);
    }
  }

  /// <summary>
  /// 影霄·拔刀（9 环·阿米娅）：强韧失败即死时播放碎裂消亡特效。
  /// </summary>
  public class ContextActionShadowBladeKillFx : ContextAction
  {
    public override string GetCaption() => "影霄·拔刀·即死碎裂消亡特效";

    public override void RunAction()
    {
      var target = Target?.Unit;
      if (target != null)
        ShadowBladeFx.PlayKill(target);
    }
  }

  /// <summary>
  /// 影霄·拔刀（9 环·阿米娅）：强韧成功承受伤害时播放重击顿挫与残痕余辉特效。
  /// </summary>
  public class ContextActionShadowBladeHitFx : ContextAction
  {
    public override string GetCaption() => "影霄·拔刀·重击余辉特效";

    public override void RunAction()
    {
      var target = Target?.Unit;
      if (target != null)
        ShadowBladeFx.PlayHit(target);
    }
  }
}

