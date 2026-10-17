// 影霄·拔刀（9 环·阿米娅·升变/魔王）专属自绘视觉特效（纯视觉，不影响数值结算）。
//
// 严格还原《明日方舟》阿米娅升变/近卫形态招牌终结绝技【影霄·绝影 / 影霄·拔刀】：
// 魔王情绪能量暴涌、拔刀撕裂空间、黑红真伤剑影切裂与绝望的斩断意象。
//
// 视觉体系（三位一体）：
//   1. 【拔刀斩击（Slash & Crystal Burst）】：
//      施法命中一瞬（ShadowBladeSlashAnim），刀光斜掠而过，撕裂空间的黑红渐变切线贯穿目标；
//      伴随刀芒切入点高密度黑曜与绯红结晶向外激射崩散（菱形源石晶簇碎裂）。
//   2. 【强韧豁免失败 · 即死消亡（Shatter Annihilation）】：
//      强韧失败即死时（ShadowBladeKillAnim），目标躯干瞬间密布暗红虚空裂痕，
//      空间向内坍缩后伴随大量黑红晶体彻底崩解消亡，留下瞬散的虚空残迹。
//   3. 【强韧豁免成功 · 重击余辉（Heavy Hit Stop & Lingering Scar）】：
//      强韧通过存活时（ShadowBladeHitAnim），在受击点产生重击顿挫停顿（Judder），
//      切开空间的黑红剑痕在空气中悬停微颤，散发暗红蒸汽与余辉脉动（约 0.6s），而后缓缓消隐。
//
// 渲染技术规范：
//   · 材质一律通过 MarkFxKit.Mat（复用 SwordStreakFx.StreakMaterialPlain 模板），严禁 Shader.Find。
//   · 几何网格优先使用动态 Quad/LineRenderer/简易多面体，零运行时 GC。
//   · 严格的生命周期自清理与单位销毁安全防护。
using System;
using Kingmaker.EntitySystem.Entities;
using UnityEngine;
using static RhodesDoctor.MarkFxKit;

namespace RhodesDoctor
{
  internal static class ShadowBladeFx
  {
    // ── 阿米娅魔王黑红能量调色盘 ──
    internal static readonly Color PureVoidBlack = new Color(0.04f, 0.02f, 0.04f, 1f); // 纯黑虚空裂痕
    internal static readonly Color CrimsonEdge   = new Color(0.92f, 0.08f, 0.16f, 1f); // 锋刃血红
    internal static readonly Color ObsidianShard = new Color(0.15f, 0.05f, 0.10f, 1f); // 崩散黑曜结晶
    internal static readonly Color HighGlowRed   = new Color(1.00f, 0.25f, 0.32f, 1f); // 斩击爆燃强光
    internal static readonly Color LingeringScar = new Color(0.75f, 0.06f, 0.14f, 0.8f); // 剑痕余辉
    internal static readonly Color WhiteCore     = new Color(1.00f, 0.95f, 0.95f, 1f); // 空间断层极光

    /// <summary>
    /// 触发拔刀主斩击视觉：自绘空间撕裂切线 + 黑红晶体崩散。
    /// </summary>
    internal static void PlaySlash(UnitEntityData caster, UnitEntityData target)
    {
      try
      {
        if (target?.View == null) return;
        var go = new GameObject("RD_ShadowBladeSlash") { layer = target.View.gameObject.layer };
        var anim = go.AddComponent<ShadowBladeSlashAnim>();
        if (!anim.Init(caster, target)) UnityEngine.Object.Destroy(go);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 影霄·拔刀斩击特效失败（逻辑照常）：\n" + e);
      }
    }

    /// <summary>
    /// 强韧豁免失败：即死碎裂消亡特效（空间坍缩 + 黑红晶体湮灭）。
    /// </summary>
    internal static void PlayKill(UnitEntityData target)
    {
      try
      {
        if (target?.View == null) return;
        var go = new GameObject("RD_ShadowBladeKill") { layer = target.View.gameObject.layer };
        var anim = go.AddComponent<ShadowBladeKillAnim>();
        if (!anim.Init(target)) UnityEngine.Object.Destroy(go);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 影霄·拔刀即死消亡特效失败（逻辑照常）：\n" + e);
      }
    }

    /// <summary>
    /// 强韧豁免成功：承受伤害时的重击顿挫与黑红剑痕余辉微颤。
    /// </summary>
    internal static void PlayHit(UnitEntityData target)
    {
      try
      {
        if (target?.View == null) return;
        var go = new GameObject("RD_ShadowBladeHit") { layer = target.View.gameObject.layer };
        var anim = go.AddComponent<ShadowBladeHitAnim>();
        if (!anim.Init(target)) UnityEngine.Object.Destroy(go);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 影霄·拔刀重击余辉特效失败（逻辑照常）：\n" + e);
      }
    }
  }

  /// <summary>
  /// 拔刀斩击动画：贯穿目标的倾斜空间切线（双层纯黑/暗红）+ 晶体碎屑激射。
  /// </summary>
  public class ShadowBladeSlashAnim : MonoBehaviour
  {
    private const float Dur = 0.55f;
    private float _t;
    private Vector3 _center;
    private Vector3 _slashDir;
    private Vector3 _slashNormal;

    private LineRenderer _slashCore;
    private LineRenderer _slashGlow;
    private LineRenderer _shockRing;

    // 晶体碎片点阵
    private const int ShardCount = 12;
    private readonly LineRenderer[] _shards = new LineRenderer[ShardCount];
    private readonly Vector3[] _shardVel = new Vector3[ShardCount];

    internal bool Init(UnitEntityData caster, UnitEntityData target)
    {
      var mBlack = Mat(ShadowBladeFx.PureVoidBlack);
      var mRed   = Mat(ShadowBladeFx.CrimsonEdge);
      var mGlow  = Mat(ShadowBladeFx.HighGlowRed);
      var mWhite = Mat(ShadowBladeFx.WhiteCore);
      if (mBlack == null || mRed == null || mGlow == null || mWhite == null) return false;

      float h = ModelHeight(target);
      _center = target.Position + Vector3.up * (h * 0.55f);

      // 计算斩击切线方向：结合施法者方位产生约 35 度斜掠下劈切面
      Vector3 toCaster = caster?.View != null ? (caster.Position - target.Position).normalized : Vector3.back;
      Vector3 right = Vector3.Cross(Vector3.up, toCaster).normalized;
      if (right.sqrMagnitude < 0.1f) right = Vector3.right;

      _slashDir = (right * 0.8f - Vector3.up * 0.6f).normalized;
      _slashNormal = Vector3.Cross(_slashDir, toCaster).normalized;

      // 双层切线：内层极黑吸收一切光芒，外层烈红加法散发魔王情绪能量
      _slashCore = Line(transform, "RD_SBS_Core", mBlack, 2, false, 0.12f);
      _slashGlow = Line(transform, "RD_SBS_Glow", mGlow,  2, false, 0.24f);
      _shockRing = Line(transform, "RD_SBS_Ring", mRed, 24, true, 0.05f);

      // 崩散晶体碎片初始化
      var mShard = Mat(ShadowBladeFx.ObsidianShard);
      for (int i = 0; i < ShardCount; i++)
      {
        _shards[i] = Line(transform, "RD_SBS_Shard" + i, (i % 2 == 0) ? mShard : mRed, 2, false, 0.035f);
        float angle = (i / (float)ShardCount) * Mathf.PI * 2f;
        float speed = UnityEngine.Random.Range(2.5f, 5.5f);
        _shardVel[i] = (right * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle) + _slashNormal * UnityEngine.Random.Range(-0.3f, 0.3f)).normalized * speed;
      }

      return true;
    }

    private void Update()
    {
      _t += Time.deltaTime;
      if (_t >= Dur)
      {
        Destroy(gameObject);
        return;
      }

      float progress = _t / Dur;
      float slashAlpha = Mathf.Clamp01(1f - progress);

      // 斩击切线延展与收缩：前 0.12s 极速延伸至 4.5m，随后逐渐窄化闭合
      float len = Mathf.Sin(Mathf.Clamp01(_t / 0.18f) * Mathf.PI * 0.5f) * 2.2f;
      Vector3 p0 = _center - _slashDir * len;
      Vector3 p1 = _center + _slashDir * len;

      if (_slashCore != null)
      {
        _slashCore.startWidth = 0.12f * slashAlpha;
        _slashCore.endWidth   = 0.12f * slashAlpha;
        _slashCore.SetPosition(0, p0);
        _slashCore.SetPosition(1, p1);
      }
      if (_slashGlow != null)
      {
        _slashGlow.startWidth = 0.28f * slashAlpha;
        _slashGlow.endWidth   = 0.28f * slashAlpha;
        _slashGlow.SetPosition(0, p0);
        _slashGlow.SetPosition(1, p1);
      }

      // 冲击震波扩张
      if (_shockRing != null)
      {
        float ringR = progress * 2.8f;
        float ringA = Mathf.Clamp01(1f - progress * 1.5f);
        _shockRing.startWidth = 0.05f * ringA;
        _shockRing.endWidth   = 0.05f * ringA;
        for (int i = 0; i < 24; i++)
        {
          float rad = (i / 24f) * Mathf.PI * 2f;
          Vector3 pt = _center + (rightOffset(rad, ringR) + upOffset(rad, ringR));
          _shockRing.SetPosition(i, pt);
        }
      }

      // 崩散晶体飞溅
      for (int i = 0; i < ShardCount; i++)
      {
        if (_shards[i] == null) continue;
        Vector3 curPos = _center + _shardVel[i] * _t + Vector3.down * (4.9f * _t * _t);
        Vector3 tailPos = curPos - _shardVel[i].normalized * 0.15f;
        _shards[i].startWidth = 0.035f * slashAlpha;
        _shards[i].endWidth   = 0.010f * slashAlpha;
        _shards[i].SetPosition(0, tailPos);
        _shards[i].SetPosition(1, curPos);
      }
    }

    private Vector3 rightOffset(float rad, float r) => Vector3.right * (Mathf.Cos(rad) * r);
    private Vector3 upOffset(float rad, float r) => Vector3.up * (Mathf.Sin(rad) * r);
  }

  /// <summary>
  /// 强韧豁免失败：即死碎裂消亡动画（空间向内聚缩后伴随大量黑红晶体湮灭散开）。
  /// </summary>
  public class ShadowBladeKillAnim : MonoBehaviour
  {
    private const float Dur = 0.85f;
    private float _t;
    private Vector3 _center;
    private LineRenderer _voidSphere;
    private const int Fractures = 8;
    private readonly LineRenderer[] _fractureLines = new LineRenderer[Fractures];

    internal bool Init(UnitEntityData target)
    {
      var mBlack = Mat(ShadowBladeFx.PureVoidBlack);
      var mRed   = Mat(ShadowBladeFx.CrimsonEdge);
      var mGlow  = Mat(ShadowBladeFx.HighGlowRed);
      if (mBlack == null || mRed == null || mGlow == null) return false;

      float h = ModelHeight(target);
      _center = target.Position + Vector3.up * (h * 0.5f);

      _voidSphere = Line(transform, "RD_SBK_Void", mBlack, 24, true, 0.08f);
      for (int i = 0; i < Fractures; i++)
        _fractureLines[i] = Line(transform, "RD_SBK_Frac" + i, (i % 2 == 0) ? mRed : mGlow, 2, false, 0.04f);

      return true;
    }

    private void Update()
    {
      _t += Time.deltaTime;
      if (_t >= Dur)
      {
        Destroy(gameObject);
        return;
      }

      float p = _t / Dur;
      // 0~0.25s 坍缩，0.25s~0.85s 碎裂向外消亡
      float r = (p < 0.3f) ? Mathf.Lerp(1.2f, 0.2f, p / 0.3f) : Mathf.Lerp(0.2f, 2.5f, (p - 0.3f) / 0.7f);
      float alpha = (p < 0.3f) ? 1f : Mathf.Clamp01(1f - (p - 0.3f) / 0.7f);

      if (_voidSphere != null)
      {
        _voidSphere.startWidth = 0.06f * alpha;
        _voidSphere.endWidth   = 0.06f * alpha;
        for (int i = 0; i < 24; i++)
        {
          float rad = (i / 24f) * Mathf.PI * 2f;
          _voidSphere.SetPosition(i, _center + new Vector3(Mathf.Cos(rad) * r, Mathf.Sin(rad) * r * 0.8f, 0f));
        }
      }

      for (int i = 0; i < Fractures; i++)
      {
        if (_fractureLines[i] == null) continue;
        float angle = (i / (float)Fractures) * Mathf.PI * 2f;
        Vector3 dir = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), UnityEngine.Random.Range(-0.2f, 0.2f)).normalized;
        _fractureLines[i].startWidth = 0.04f * alpha;
        _fractureLines[i].endWidth   = 0.01f * alpha;
        _fractureLines[i].SetPosition(0, _center + dir * (r * 0.2f));
        _fractureLines[i].SetPosition(1, _center + dir * r);
      }
    }
  }

  /// <summary>
  /// 强韧豁免成功：受击顿挫停顿与暗红剑痕余辉微颤。
  /// </summary>
  public class ShadowBladeHitAnim : MonoBehaviour
  {
    private const float Dur = 0.75f;
    private float _t;
    private Vector3 _center;
    private LineRenderer _scarLine;
    private LineRenderer _steamGlow;

    internal bool Init(UnitEntityData target)
    {
      var mScar = Mat(ShadowBladeFx.LingeringScar);
      var mGlow = Mat(ShadowBladeFx.HighGlowRed);
      if (mScar == null || mGlow == null) return false;

      float h = ModelHeight(target);
      _center = target.Position + Vector3.up * (h * 0.55f);

      _scarLine  = Line(transform, "RD_SBH_Scar", mScar, 2, false, 0.07f);
      _steamGlow = Line(transform, "RD_SBH_Glow", mGlow, 2, false, 0.15f);
      return true;
    }

    private void Update()
    {
      _t += Time.deltaTime;
      if (_t >= Dur)
      {
        Destroy(gameObject);
        return;
      }

      float p = _t / Dur;
      float alpha = Mathf.Clamp01(1f - p);
      // 余辉顿挫与高频微颤
      float jitter = (1f - p) * Mathf.Sin(_t * 45f) * 0.03f;
      Vector3 dir = (Vector3.right * 0.7f - Vector3.up * 0.5f).normalized;

      Vector3 p0 = _center - dir * (0.8f + jitter);
      Vector3 p1 = _center + dir * (0.8f + jitter);

      if (_scarLine != null)
      {
        _scarLine.startWidth = 0.06f * alpha;
        _scarLine.endWidth   = 0.06f * alpha;
        _scarLine.SetPosition(0, p0);
        _scarLine.SetPosition(1, p1);
      }

      if (_steamGlow != null)
      {
        _steamGlow.startWidth = 0.15f * alpha;
        _steamGlow.endWidth   = 0.15f * alpha;
        _steamGlow.SetPosition(0, p0);
        _steamGlow.SetPosition(1, p1);
      }
    }
  }
}
