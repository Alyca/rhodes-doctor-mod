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

  /// <summary>影霄·拔刀专用缓动与线段工具（无分配）。</summary>
  internal static class SbMath
  {
    internal static float Out3(float x) { x = 1f - Mathf.Clamp01(x); return 1f - x * x * x; }
    internal static float InQuad(float x) { x = Mathf.Clamp01(x); return x * x; }
    internal static float Seg(float t, float a, float b) => Mathf.Clamp01((t - a) / (b - a));

    /// <summary>线段：MarkFxKit.Line 默认 alpha=0，每帧 SetA 激活；只动 widthMultiplier。</summary>
    internal static void Seg2(LineRenderer lr, Vector3 a, Vector3 b, float width, float alpha)
    {
      if (lr == null) return;
      lr.SetPosition(0, a);
      lr.SetPosition(1, b);
      lr.widthMultiplier = width;
      SetA(lr, Color.white, alpha);
    }
  }

  /// <summary>
  /// 拔刀斩击：0.10 s 极速展开 → 停滞（断层微裂隙） → 空间张力闭合；
  /// 纯黑核心 + 暗红光晕 + 双边缘轮廓；刀光沿切线与核心极速延展，边缘呈锯齿割裂；
  /// 受击法向锥形散射黑曜/绯红晶体碎片 + 阻尼 + 重力 + 正对镜头的冲击环。
  /// </summary>
  public class ShadowBladeSlashAnim : MonoBehaviour
  {
    private const float Dur = 0.60f;
    private const float TExtend = 0.10f, THold = 0.22f, TClose = 0.42f;
    private const float HalfLen = 2.2f;
    private const float Gravity = 9.8f, Drag = 3.5f;
    private const int RingN = 32, JagN = 9, ShardCount = 16;

    private float _t;
    private Vector3 _center, _dir, _perp;

    private LineRenderer _core, _glow, _rimA, _rimB, _jag, _ring;
    private readonly float[] _jagOff = new float[JagN];
    private readonly LineRenderer[] _shards = new LineRenderer[ShardCount];
    private readonly Vector3[] _pos = new Vector3[ShardCount];
    private readonly Vector3[] _vel = new Vector3[ShardCount];
    private readonly float[] _len = new float[ShardCount];
    private readonly bool[] _obsidian = new bool[ShardCount];

    internal bool Init(UnitEntityData caster, UnitEntityData target)
    {
      if (target?.View == null) return false;
      var mBlack = Mat(ShadowBladeFx.PureVoidBlack);
      var mRed   = Mat(ShadowBladeFx.CrimsonEdge);
      var mGlow  = Mat(ShadowBladeFx.HighGlowRed);
      var mWhite = Mat(ShadowBladeFx.WhiteCore);
      var mShard = Mat(ShadowBladeFx.ObsidianShard);
      if (mBlack == null || mRed == null || mGlow == null || mWhite == null || mShard == null) return false;

      float h = ModelHeight(target);
      _center = target.Position + Vector3.up * (h * 0.55f);

      Vector3 toCaster = Vector3.back;
      if (caster?.View != null)
      {
        var d = caster.Position - target.Position;
        d.y = 0f;
        if (d.sqrMagnitude > 1e-4f) toCaster = d.normalized;
      }
      Vector3 side = Vector3.Cross(Vector3.up, toCaster);
      if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
      side.Normalize();
      _dir = (side * 0.8f - Vector3.up * 0.6f).normalized; // 约 37 度倾斜切线

      CamBasis(out var camR, out var camU);
      _perp = Vector3.Cross(_dir, Vector3.Cross(camR, camU)); // 屏幕内垂直切线方向
      if (_perp.sqrMagnitude < 1e-4f) _perp = Vector3.up;
      _perp.Normalize();

      ShadowBladeHitAnim.RememberSlash(_dir); // 豁免成功时重击残痕沿同一方向停顿

      _glow = Line(transform, "RD_SBS_Glow", mRed,   2, false, 0.30f);
      _rimA = Line(transform, "RD_SBS_RimA", mGlow,  2, false, 0.045f);
      _rimB = Line(transform, "RD_SBS_RimB", mGlow,  2, false, 0.045f);
      _core = Line(transform, "RD_SBS_Core", mBlack, 2, false, 0.11f);
      _jag  = Line(transform, "RD_SBS_Jag",  mWhite, JagN, false, 0.025f);
      _ring = Line(transform, "RD_SBS_Ring", mRed,   RingN, true, 0.07f);

      for (int i = 0; i < JagN; i++)
        _jagOff[i] = (i == 0 || i == JagN - 1) ? 0f : UnityEngine.Random.Range(-0.06f, 0.06f);

      for (int i = 0; i < ShardCount; i++)
      {
        _obsidian[i] = (i & 1) == 0;
        _shards[i] = Line(transform, "RD_SBS_Shard" + i, _obsidian[i] ? mShard : mRed, 2, false,
          _obsidian[i] ? 0.06f : 0.035f);
        // 受击法向锥形散射：横向扩散 + 向上速度 + 纵深散射
        float sideSign = (i % 4 < 2) ? 1f : -1f;
        Vector3 v = _perp * sideSign
          + _dir * UnityEngine.Random.Range(-0.45f, 0.45f)
          + Vector3.up * UnityEngine.Random.Range(0.1f, 0.6f);
        _vel[i] = v.normalized * UnityEngine.Random.Range(3.0f, 6.5f);
        _pos[i] = _center + _dir * UnityEngine.Random.Range(-0.6f, 0.6f);
        _len[i] = UnityEngine.Random.Range(0.02f, 0.04f); // 尾迹 = 速度 × 长度系数
      }
      return true;
    }

    private void Update()
    {
      float dt = Time.deltaTime;
      _t += dt;
      if (_t >= Dur) { Destroy(gameObject); return; }
      try { Tick(dt); }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 影霄·拔刀斩击特效失败（移除）：\n" + e);
        Destroy(gameObject);
      }
    }

    private void Tick(float dt)
    {
      // 切线：展开（EaseOut3）→ 维持 → 闭合（EaseInQuad 逐渐收束）
      float ext   = SbMath.Out3(_t / TExtend);
      float close = SbMath.InQuad(SbMath.Seg(_t, THold, TClose));
      float w     = 1f - close;
      float len   = HalfLen * ext * w;
      float flash = 1f - SbMath.Seg(_t, 0f, THold); // 开局爆闪
      Vector3 a = _center - _dir * len, b = _center + _dir * len;
      Vector3 rim = _perp * (0.08f * w);

      SbMath.Seg2(_glow, a, b, 0.30f * w * (1f + 0.6f * flash), 0.85f * w);
      SbMath.Seg2(_rimA, a + rim, b + rim, 0.045f * w, w);
      SbMath.Seg2(_rimB, a - rim, b - rim, 0.045f * w, w);
      SbMath.Seg2(_core, a, b, 0.11f * w, w);

      // 撕裂断层微裂隙：在保持期闪烁的锯齿间隙
      if (_jag != null)
      {
        if (_t >= TExtend * 0.6f && _t < TClose)
        {
          float flick = 0.55f + 0.45f * Mathf.Sin(_t * 90f);
          for (int i = 0; i < JagN; i++)
          {
            float k = i / (float)(JagN - 1) * 2f - 1f;
            _jag.SetPosition(i, _center + _dir * (len * k) + _perp * (_jagOff[i] * w));
          }
          _jag.widthMultiplier = 0.025f * w;
          SetA(_jag, Color.white, flick * w);
        }
        else SetA(_jag, Color.white, 0f);
      }

      // 冲击环：正对镜头（EaseOut3 扩散）
      if (_ring != null)
      {
        float rk = SbMath.Seg(_t, 0f, 0.35f);
        CamBasis(out var camR, out var camU);
        BillCircle(_ring, _center, Mathf.Lerp(0.15f, 2.6f, SbMath.Out3(rk)), camR, camU);
        _ring.widthMultiplier = 0.07f * (1f - rk);
        SetA(_ring, Color.white, 0.9f * (1f - rk));
      }

      // 崩散晶体物理积分：空气阻尼 + 重力与尾迹拉伸
      float damp = Mathf.Exp(-Drag * dt);
      float fade = 1f - SbMath.InQuad(_t / Dur);
      for (int i = 0; i < ShardCount; i++)
      {
        var lr = _shards[i];
        if (lr == null) continue;
        _vel[i] = _vel[i] * damp + Vector3.down * (Gravity * dt);
        _pos[i] += _vel[i] * dt;
        lr.SetPosition(0, _pos[i] - _vel[i] * _len[i]);
        lr.SetPosition(1, _pos[i]);
        lr.widthMultiplier = (_obsidian[i] ? 0.06f : 0.035f) * fade;
        SetA(lr, Color.white, fade);
      }
    }
  }

  /// <summary>
  /// 强韧豁免失败：0~0.22 s 正对镜头收缩坍缩（裂痕亮起），
  /// 0.22 s 爆发消亡：冲击波 + 裂痕外甩 + 黑曜/暗红晶体碎屑漂浮消散 + 虚空残迹缓缓隐去。
  /// </summary>
  public class ShadowBladeKillAnim : MonoBehaviour
  {
    private const float Dur = 0.90f;
    private const float TImplode = 0.22f;
    private const float Drag = 2.5f;
    private const int RingN = 32, Cracks = 10, Debris = 24;

    private float _t, _r0;
    private bool _burst;
    private Vector3 _center;
    private LineRenderer _void, _rim, _wave;
    private readonly LineRenderer[] _crack = new LineRenderer[Cracks];
    private readonly Vector3[] _crackDir = new Vector3[Cracks];
    private readonly LineRenderer[] _deb = new LineRenderer[Debris];
    private readonly Vector3[] _pos = new Vector3[Debris];
    private readonly Vector3[] _vel = new Vector3[Debris];
    private readonly float[] _w = new float[Debris];
    private readonly float[] _g = new float[Debris];

    internal bool Init(UnitEntityData target)
    {
      if (target?.View == null) return false;
      var mBlack = Mat(ShadowBladeFx.PureVoidBlack);
      var mRed   = Mat(ShadowBladeFx.CrimsonEdge);
      var mGlow  = Mat(ShadowBladeFx.HighGlowRed);
      var mShard = Mat(ShadowBladeFx.ObsidianShard);
      if (mBlack == null || mRed == null || mGlow == null || mShard == null) return false;

      float h = ModelHeight(target);
      _center = target.Position + Vector3.up * (h * 0.5f);
      _r0 = Mathf.Clamp(h * 0.55f, 0.8f, 3f);

      _void = Line(transform, "RD_SBK_Void", mBlack, RingN, true, 0.12f);
      _rim  = Line(transform, "RD_SBK_Rim",  mRed,   RingN, true, 0.06f);
      _wave = Line(transform, "RD_SBK_Wave", mGlow,  RingN, true, 0.10f);

      CamBasis(out var camR, out var camU);
      for (int i = 0; i < Cracks; i++)
      {
        float a = (i + UnityEngine.Random.Range(-0.3f, 0.3f)) / Cracks * Mathf.PI * 2f;
        _crackDir[i] = (camR * Mathf.Cos(a) + camU * (Mathf.Sin(a) * 1.3f)).normalized; // 沿椭圆分布裂痕
        _crack[i] = Line(transform, "RD_SBK_Crack" + i, (i & 1) == 0 ? mGlow : mRed, 2, false, 0.04f);
      }
      for (int i = 0; i < Debris; i++)
      {
        int kind = i % 3; // 0 重黑曜石碎片 / 1 暗红晶刺 / 2 向上飘浮的血色光斑
        _deb[i] = Line(transform, "RD_SBK_Deb" + i, kind == 0 ? mShard : (kind == 1 ? mRed : mGlow), 2, false, 0.05f);
        _vel[i] = UnityEngine.Random.onUnitSphere
          * (kind == 2 ? UnityEngine.Random.Range(1.2f, 2.5f) : UnityEngine.Random.Range(2.5f, 6.0f));
        _g[i] = kind == 0 ? 7f : (kind == 1 ? 3.5f : -1.2f);
        _w[i] = kind == 0 ? 0.07f : (kind == 1 ? 0.04f : 0.05f);
        _pos[i] = _center;
      }
      return true;
    }

    private void Update()
    {
      float dt = Time.deltaTime;
      _t += dt;
      if (_t >= Dur) { Destroy(gameObject); return; }
      try { Tick(dt); }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 影霄·拔刀即死消亡特效失败（移除）：\n" + e);
        Destroy(gameObject);
      }
    }

    private void Tick(float dt)
    {
      CamBasis(out var camR, out var camU);

      // 阶段 1：坍缩收束（EaseInQuad 越来越快向中心坍缩）+ 裂痕激亮
      if (_t < TImplode)
      {
        float k = _t / TImplode;
        float r = Mathf.Lerp(_r0, 0.12f, SbMath.InQuad(k));
        BillCircle(_void, _center, r, camR, camU);
        _void.widthMultiplier = 0.12f + 0.10f * k;
        SetA(_void, Color.white, 1f);
        BillCircle(_rim, _center, r + 0.05f, camR, camU, k * 3f);
        _rim.widthMultiplier = 0.06f;
        SetA(_rim, Color.white, 0.6f + 0.4f * k);
        float grow = SbMath.Out3(k * 2f);
        for (int i = 0; i < Cracks; i++)
          SbMath.Seg2(_crack[i], _center + _crackDir[i] * (r * 0.1f),
            _center + _crackDir[i] * (r * (0.3f + 0.8f * grow)), 0.035f, 0.5f + 0.5f * k);
        return;
      }

      if (!_burst)
      {
        _burst = true;
        for (int i = 0; i < Debris; i++) _pos[i] = _center;
      }

      float bk   = SbMath.Seg(_t, TImplode, Dur);
      float fade = 1f - SbMath.InQuad(bk); // 瞬间爆发后缓慢消散

      // 冲击波
      float wk = SbMath.Seg(_t, TImplode, TImplode + 0.30f);
      BillCircle(_wave, _center, Mathf.Lerp(0.1f, _r0 * 2.2f, SbMath.Out3(wk)), camR, camU);
      _wave.widthMultiplier = 0.10f * (1f - wk);
      SetA(_wave, Color.white, 1f - wk);

      // 裂痕外甩后迅速消散
      float ck = SbMath.Seg(_t, TImplode, TImplode + 0.15f);
      float ce = SbMath.Out3(ck);
      for (int i = 0; i < Cracks; i++)
        SbMath.Seg2(_crack[i], _center + _crackDir[i] * (_r0 * 0.6f * ce),
          _center + _crackDir[i] * (_r0 * (0.2f + 1.4f * ce)), 0.05f * (1f - ck), 1f - ck);

      // 虚空残迹圈
      float rr = Mathf.Lerp(0.35f, 0.05f, bk);
      BillCircle(_void, _center, rr, camR, camU);
      _void.widthMultiplier = 0.10f * fade;
      SetA(_void, Color.white, fade);
      BillCircle(_rim, _center, rr + 0.04f, camR, camU, _t * 6f);
      _rim.widthMultiplier = 0.04f * fade;
      SetA(_rim, Color.white, 0.8f * fade);

      // 碎屑粒子积分
      float damp = Mathf.Exp(-Drag * dt);
      for (int i = 0; i < Debris; i++)
      {
        var lr = _deb[i];
        if (lr == null) continue;
        _vel[i] = _vel[i] * damp + Vector3.down * (_g[i] * dt);
        _pos[i] += _vel[i] * dt;
        lr.SetPosition(0, _pos[i] - _vel[i] * 0.035f);
        lr.SetPosition(1, _pos[i]);
        lr.widthMultiplier = _w[i] * fade;
        SetA(lr, Color.white, fade);
      }
    }
  }

  /// <summary>
  /// 强韧豁免成功受创：前 90 ms 强烈顿挫（Hit Stop 帧冻结高光），
  /// 26 Hz 阻尼微颤（垂直切痕震颤，像琴弦被拨动）+ 余辉脉动 + 暗红蒸汽升腾，0.45~0.75 s 平滑淡出。
  /// 方向承接同帧斩击记录的方向，确保残痕与切线精准重合。
  /// </summary>
  public class ShadowBladeHitAnim : MonoBehaviour
  {
    private const float Dur = 0.75f;
    private const float TStop = 0.09f;
    private const float TFade = 0.45f;
    private const float HalfLen = 1.6f;
    private const int SteamN = 6;

    private static Vector3 _lastDir;
    private static int _lastFrame = -1000;
    internal static void RememberSlash(Vector3 dir) { _lastDir = dir; _lastFrame = Time.frameCount; }

    private float _t;
    private Vector3 _center, _dir, _perp;
    private LineRenderer _scar, _glow, _flash;
    private readonly LineRenderer[] _steam = new LineRenderer[SteamN];
    private readonly Vector3[] _steamBase = new Vector3[SteamN];
    private readonly float[] _steamDelay = new float[SteamN];

    internal bool Init(UnitEntityData target)
    {
      if (target?.View == null) return false;
      var mScar  = Mat(ShadowBladeFx.LingeringScar);
      var mGlow  = Mat(ShadowBladeFx.HighGlowRed);
      var mWhite = Mat(ShadowBladeFx.WhiteCore);
      var mRed   = Mat(ShadowBladeFx.CrimsonEdge);
      if (mScar == null || mGlow == null || mWhite == null || mRed == null) return false;

      _center = target.Position + Vector3.up * (ModelHeight(target) * 0.55f);
      CamBasis(out var camR, out var camU);
      _dir = (Time.frameCount - _lastFrame <= 2 && _lastDir.sqrMagnitude > 0.5f)
        ? _lastDir
        : (FlatRight(camR) * 0.8f - Vector3.up * 0.6f).normalized;
      _perp = Vector3.Cross(_dir, Vector3.Cross(camR, camU));
      if (_perp.sqrMagnitude < 1e-4f) _perp = Vector3.up;
      _perp.Normalize();

      _glow  = Line(transform, "RD_SBH_Glow",  mGlow,  2, false, 0.22f);
      _scar  = Line(transform, "RD_SBH_Scar",  mScar,  2, false, 0.07f);
      _flash = Line(transform, "RD_SBH_Flash", mWhite, 2, false, 0.04f);
      for (int i = 0; i < SteamN; i++)
      {
        _steam[i] = Line(transform, "RD_SBH_Steam" + i, mRed, 2, false, 0.05f);
        float k = (i + 0.5f) / SteamN * 2f - 1f;
        _steamBase[i] = _center + _dir * (HalfLen * 0.85f * k);
        _steamDelay[i] = UnityEngine.Random.Range(0f, 0.15f);
      }
      return true;
    }

    private void Update()
    {
      _t += Time.deltaTime;
      if (_t >= Dur) { Destroy(gameObject); return; }
      try { Tick(); }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 影霄·拔刀重击特效失败（移除）：\n" + e);
        Destroy(gameObject);
      }
    }

    private void Tick()
    {
      float fade = 1f - SbMath.InQuad(SbMath.Seg(_t, TFade, Dur));
      float shake = 0f, pulse = 1f, flashA, boost;
      if (_t < TStop)
      {
        flashA = 1f;   // 顿挫帧冻结高光
        boost = 1.5f;
      }
      else
      {
        float s = _t - TStop;
        shake  = Mathf.Exp(-5f * s) * Mathf.Sin(s * (2f * Mathf.PI * 26f)) * 0.07f;
        pulse  = 0.65f + 0.35f * Mathf.Sin(s * 16f);
        flashA = 0.5f * (1f - SbMath.Seg(s, 0f, 0.15f));
        boost  = 1f;
      }
      Vector3 a = _center - _dir * HalfLen + _perp * shake;
      Vector3 b = _center + _dir * HalfLen - _perp * (shake * 0.6f);

      SbMath.Seg2(_glow,  a, b, 0.22f * boost * fade * pulse, 0.85f * fade * pulse);
      SbMath.Seg2(_scar,  a, b, 0.07f * fade, fade);
      SbMath.Seg2(_flash, a, b, 0.04f * flashA, flashA);

      for (int i = 0; i < SteamN; i++)
      {
        var lr = _steam[i];
        if (lr == null) continue;
        float st = _t - TStop - _steamDelay[i];
        if (st <= 0f) { SetA(lr, Color.white, 0f); continue; }
        float rise = SbMath.Out3(st / 0.5f) * 0.45f;
        Vector3 p0 = _steamBase[i] + _perp * shake + Vector3.up * rise;
        lr.SetPosition(0, p0);
        lr.SetPosition(1, p0 + Vector3.up * (0.12f + 0.15f * rise));
        lr.widthMultiplier = 0.05f * (1f + rise);
        SetA(lr, Color.white, Mathf.Clamp01(1f - st / 0.55f) * fade * 0.7f);
      }
    }
  }
}
