// 影霄·拔刀（9 环·阿米娅·升变/魔王）专属自绘视觉特效（纯视觉，不影响数值结算）。
//
// 严格还原《明日方舟》阿米娅升变/近卫形态招牌终结绝技【影霄·绝影 / 影霄·拔刀】：
// 魔王情绪能量暴涌、拔刀撕裂空间、纯正赤红真伤剑影切裂与绝望的斩断意象。
//
// 视觉体系（三位一体）：
//   1. 【拔刀斩击（Slash & Crystal Burst）】：
//      施法命中一瞬（ShadowBladeSlashAnim），刀光斜掠而过，撕裂空间的赤红渐变切线贯穿目标；
//      伴随刀芒切入点高密度绯红与白热结晶向外激射崩散。
//   2. 【强韧豁免失败 · 即死消亡（Shatter Annihilation）】：
//      强韧失败即死时（ShadowBladeKillAnim），目标躯干瞬间密布白热/赤红虚空裂痕，
//      空间向内坍缩后伴随大量晶体彻底崩解消亡，留下瞬散的残迹。
//   3. 【强韧豁免成功 · 重击余辉（Heavy Hit Stop & Lingering Scar）】：
//      强韧通过存活时（ShadowBladeHitAnim），在受击点产生重击顿挫停顿（Judder），
//      切开空间的赤红剑痕在空气中悬停微颤，散发暗红蒸汽与余辉脉动（约 0.8s），而后缓缓消隐。
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
    // ── 阿米娅魔王·赤红能量调色盘 ──
    // MarkFxKit.Mat 的模板是加法/发光类材质：颜色越暗越透明，纯黑完全不上屏。
    // 因此整套只用高亮红系；"深邃"的层次靠宽幅 DeepBloodRed 外晕衬托，而不是黑色。蓝分量全部 ≤0.08，杜绝偏粉。
    internal static readonly Color ScarletRed     = new Color(1.00f, 0.10f, 0.04f, 1f); // 主刃身·晶刺·纯正赤红
    internal static readonly Color BlazingCrimson = new Color(1.00f, 0.24f, 0.06f, 1f); // 刃缘·冲击波·爆燃碎屑（炽烈橙赤）
    internal static readonly Color WhiteHotCore   = new Color(1.00f, 0.86f, 0.70f, 1f); // 白热内芯·断层闪光（暖白，不偏粉）
    internal static readonly Color DeepBloodRed   = new Color(0.72f, 0.02f, 0.02f, 1f); // 外晕·余辉·残迹（暗而仍发光）

    /// <summary>
    /// 触发拔刀主斩击视觉：自绘空间撕裂切线 + 赤红晶体崩散。
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
    /// 强韧豁免失败：即死碎裂消亡特效（空间坍缩 + 赤红晶体湮灭）。
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
    /// 强韧豁免成功：承受伤害时的重击顿挫与赤红剑痕余辉微颤。
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

  /// <summary>影霄·拔刀专用缓动、线段与定位工具（Tick 内调用的方法均无堆分配）。</summary>
  internal static class SbMath
  {
    internal static float Out3(float x) { x = 1f - Mathf.Clamp01(x); return 1f - x * x * x; }
    internal static float InQuad(float x) { x = Mathf.Clamp01(x); return x * x; }
    internal static float Seg(float t, float a, float b) => Mathf.Clamp01((t - a) / (b - a));

    /// <summary>两点线段：MarkFxKit.Line 默认 alpha=0，每帧 SetA 激活；只动 widthMultiplier。</summary>
    internal static void Seg2(LineRenderer lr, Vector3 a, Vector3 b, float width, float alpha)
    {
      if (lr == null) return;
      lr.SetPosition(0, a);
      lr.SetPosition(1, b);
      lr.widthMultiplier = width;
      SetA(lr, Color.white, alpha);
    }

    /// <summary>多点直线（配合 Taper 形成两端尖细的刀芒）。</summary>
    internal static void Span(LineRenderer lr, Vector3 a, Vector3 b, float width, float alpha)
    {
      if (lr == null) return;
      int n = lr.positionCount;
      float inv = 1f / (n - 1);
      for (int i = 0; i < n; i++) lr.SetPosition(i, Vector3.LerpUnclamped(a, b, i * inv));
      lr.widthMultiplier = width;
      SetA(lr, Color.white, alpha);
    }

    /// <summary>两端收尖的宽度曲线（只在 Init 调用一次）。</summary>
    internal static void Taper(LineRenderer lr, float tip)
    {
      if (lr == null) return;
      lr.widthCurve = new AnimationCurve(new Keyframe(0f, tip), new Keyframe(0.5f, 1f), new Keyframe(1f, tip));
    }

    /// <summary>受击锚点：按模型高度取点，并朝镜头拉近 pull 米，避免刀光中段被目标模型深度遮挡"断成两截"。</summary>
    internal static Vector3 Anchor(UnitEntityData target, float heightFrac, float pull, out float groundY)
    {
      groundY = target.Position.y;
      CamBasis(out var camR, out var camU);
      Vector3 fwd = Vector3.Cross(camR, camU);
      return target.Position + Vector3.up * (ModelHeight(target) * heightFrac) - fwd * pull;
    }

    /// <summary>斜下劈方向：下倾量受离地高度限制，保证刀光两端不插进地面被裁掉。</summary>
    internal static Vector3 SlashDir(Vector3 side, float halfLen, float clearance)
    {
      float s = Mathf.Clamp(clearance / halfLen, 0.12f, 0.55f);
      return (side - Vector3.up * s).normalized;
    }
  }

  /// <summary>
  /// 拔刀斩击（赤红双层空间撕裂）：0.09 s 极速展开（EaseOut3）→ 定格（白热断层锯齿闪烁）→ EaseInQuad 张力闭合；
  /// 由外到内：DeepBloodRed 宽外晕 → ScarletRed 刃身 → 两侧 BlazingCrimson 刃缘 → WhiteHotCore 白热内芯；
  /// 切面法向锥形抛射赤红/白热晶屑（初速散射 + 阻尼 + 重力 + 触地反弹）+ 正对镜头的赤红冲击环。
  /// </summary>
  public class ShadowBladeSlashAnim : MonoBehaviour
  {
    private const float Dur = 0.70f;
    private const float TExtend = 0.09f, THold = 0.26f, TClose = 0.48f;
    private const float HalfLen = 3.6f; // 全长 7.2 m，45° 俯视拉远后仍横贯画面
    private const float WHalo = 0.95f, WBody = 0.42f, WRim = 0.11f, WCore = 0.16f, WJag = 0.07f;
    private const float RimGap = 0.24f;
    private const float Gravity = 9.8f, Drag = 2.8f;
    private const int BladeN = 9, RingN = 40, JagN = 11, ShardCount = 22;

    private float _t, _groundY;
    private Vector3 _center, _dir, _perp;

    private LineRenderer _halo, _body, _rimA, _rimB, _core, _jag, _ring;
    private readonly float[] _jagOff = new float[JagN];
    private readonly LineRenderer[] _shards = new LineRenderer[ShardCount];
    private readonly Vector3[] _pos = new Vector3[ShardCount];
    private readonly Vector3[] _vel = new Vector3[ShardCount];
    private readonly float[] _len = new float[ShardCount];
    private readonly float[] _w = new float[ShardCount];

    internal bool Init(UnitEntityData caster, UnitEntityData target)
    {
      if (target?.View == null) return false;
      var mDeep  = Mat(ShadowBladeFx.DeepBloodRed);
      var mRed   = Mat(ShadowBladeFx.ScarletRed);
      var mBlaze = Mat(ShadowBladeFx.BlazingCrimson);
      var mWhite = Mat(ShadowBladeFx.WhiteHotCore);
      if (mDeep == null || mRed == null || mBlaze == null || mWhite == null) return false;

      _center = SbMath.Anchor(target, 0.6f, 0.8f, out _groundY);
      CamBasis(out var camR, out var camU);

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
      Vector3 camFlat = FlatRight(camR);
      float sgn = Vector3.Dot(side, camFlat) >= 0f ? 1f : -1f;
      side = (side + camFlat * (sgn * 1.2f)).normalized; // 偏向屏幕横向，避免被镜头看成一个点
      _dir = SbMath.SlashDir(side, HalfLen, _center.y - _groundY - 0.15f);

      _perp = Vector3.Cross(_dir, Vector3.Cross(camR, camU)); // 屏幕内垂直切线方向
      if (_perp.sqrMagnitude < 1e-4f) _perp = Vector3.up;
      _perp.Normalize();

      ShadowBladeHitAnim.RememberSlash(_dir); // 豁免成功时重击残痕沿同一方向停顿

      _halo = Line(transform, "RD_SBS_Halo", mDeep,  BladeN, false, WHalo);
      _body = Line(transform, "RD_SBS_Body", mRed,   BladeN, false, WBody);
      _rimA = Line(transform, "RD_SBS_RimA", mBlaze, BladeN, false, WRim);
      _rimB = Line(transform, "RD_SBS_RimB", mBlaze, BladeN, false, WRim);
      _core = Line(transform, "RD_SBS_Core", mWhite, BladeN, false, WCore);
      _jag  = Line(transform, "RD_SBS_Jag",  mWhite, JagN, false, WJag);
      _ring = Line(transform, "RD_SBS_Ring", mRed,   RingN, true, 0.16f);
      SbMath.Taper(_halo, 0.25f);
      SbMath.Taper(_body, 0.12f);
      SbMath.Taper(_rimA, 0.10f);
      SbMath.Taper(_rimB, 0.10f);
      SbMath.Taper(_core, 0.10f);

      for (int i = 0; i < JagN; i++)
        _jagOff[i] = (i == 0 || i == JagN - 1) ? 0f : UnityEngine.Random.Range(-0.18f, 0.18f);

      for (int i = 0; i < ShardCount; i++)
      {
        int kind = i % 3; // 0 赤红晶刺 / 1 炽烈碎屑 / 2 白热火星
        var m = kind == 0 ? mRed : (kind == 1 ? mBlaze : mWhite);
        _w[i] = kind == 0 ? 0.14f : (kind == 1 ? 0.10f : 0.07f);
        _shards[i] = Line(transform, "RD_SBS_Shard" + i, m, 2, false, _w[i]);
        // 切面法向锥形抛射：两侧法向 + 沿刀路散射 + 上抛分量
        float sideSign = (i & 1) == 0 ? 1f : -1f;
        Vector3 v = _perp * sideSign
          + _dir * UnityEngine.Random.Range(-0.6f, 0.6f)
          + Vector3.up * UnityEngine.Random.Range(0.2f, 0.9f);
        _vel[i] = v.normalized * UnityEngine.Random.Range(4.5f, 9.5f);
        _pos[i] = _center + _dir * (HalfLen * UnityEngine.Random.Range(-0.5f, 0.5f));
        _len[i] = UnityEngine.Random.Range(0.035f, 0.06f); // 尾迹 = 速度 × 系数
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
      // 刀路：展开（EaseOut3）→ 定格 → 闭合（EaseInQuad）；外晕闭合更慢，留一抹赤红残光
      float ext   = SbMath.Out3(_t / TExtend);
      float close = SbMath.InQuad(SbMath.Seg(_t, THold, TClose));
      float w     = 1f - close;
      float flash = 1f - SbMath.Seg(_t, 0f, THold); // 开刃爆闪
      float lenA  = HalfLen * ext;
      float lenB  = lenA * w;
      Vector3 a = _center - _dir * lenB, b = _center + _dir * lenB;
      Vector3 rim = _perp * (RimGap * (0.6f + 0.4f * w));

      float haloLen = lenA * (0.35f + 0.65f * w);
      float haloA   = 0.9f * (1f - SbMath.Seg(_t, THold, TClose + 0.12f));
      SbMath.Span(_halo, _center - _dir * haloLen, _center + _dir * haloLen,
        WHalo * (1f + 0.5f * flash) * (0.5f + 0.5f * w), haloA);
      SbMath.Span(_body, a, b, WBody * (1f + 0.35f * flash) * w, w);
      SbMath.Span(_rimA, a + rim, b + rim, WRim * w, w);
      SbMath.Span(_rimB, a - rim, b - rim, WRim * w, w);
      SbMath.Span(_core, a, b, WCore * (0.6f + 0.4f * flash) * w, w);

      // 白热断层锯齿：定格期闪烁
      if (_jag != null)
      {
        if (_t >= TExtend * 0.6f && _t < TClose)
        {
          float flick = 0.6f + 0.4f * Mathf.Sin(_t * 85f);
          for (int i = 0; i < JagN; i++)
          {
            float k = i / (float)(JagN - 1) * 2f - 1f;
            _jag.SetPosition(i, _center + _dir * (lenB * 0.92f * k) + _perp * (_jagOff[i] * w));
          }
          _jag.widthMultiplier = WJag * w;
          SetA(_jag, Color.white, flick * w);
        }
        else SetA(_jag, Color.white, 0f);
      }

      // 赤红冲击环：正对镜头 EaseOut3 扩散
      if (_ring != null)
      {
        float rk = SbMath.Seg(_t, 0f, 0.40f);
        CamBasis(out var camR, out var camU);
        BillCircle(_ring, _center, Mathf.Lerp(0.3f, 3.4f, SbMath.Out3(rk)), camR, camU);
        _ring.widthMultiplier = 0.16f * (1f - rk) + 0.02f;
        SetA(_ring, Color.white, 1f - rk);
      }

      // 晶屑物理积分：阻尼 + 重力 + 触地反弹，尾迹随速度拉伸（保底 12 cm 可见）
      float damp = Mathf.Exp(-Drag * dt);
      float fade = 1f - SbMath.InQuad(_t / Dur);
      for (int i = 0; i < ShardCount; i++)
      {
        var lr = _shards[i];
        if (lr == null) continue;
        _vel[i] = _vel[i] * damp + Vector3.down * (Gravity * dt);
        _pos[i] += _vel[i] * dt;
        if (_pos[i].y < _groundY + 0.05f)
        {
          _pos[i].y = _groundY + 0.05f;
          if (_vel[i].y < 0f) _vel[i].y *= -0.3f;
        }
        Vector3 tail = _vel[i] * _len[i];
        if (tail.sqrMagnitude < 0.0144f) tail = tail.normalized * 0.12f;
        lr.SetPosition(0, _pos[i] - tail);
        lr.SetPosition(1, _pos[i]);
        lr.widthMultiplier = _w[i] * (0.4f + 0.6f * fade);
        SetA(lr, Color.white, fade);
      }
    }
  }

  /// <summary>
  /// 强韧豁免失败·即死湮灭（赤红）：0~0.24 s 正对镜头的深血红坍缩环 EaseInQuad 向内急收、白热/赤红裂痕收束增亮；
  /// 0.24 s 起一次性崩解：白热闪爆 + 炽烈冲击波 + 裂痕外甩 + 赤红晶刺 / 炽烈碎屑 / 上飘血红光斑（阻尼 + 分类重力 + 触地反弹）；
  /// 中心留一圈深血红残迹缓缓收拢消散。全程无黑色，加法材质下完整可见。
  /// </summary>
  public class ShadowBladeKillAnim : MonoBehaviour
  {
    private const float Dur = 1.05f;
    private const float TImplode = 0.24f;
    private const float Drag = 2.2f;
    private const int RingN = 40, Cracks = 12, Debris = 30;

    private float _t, _r0, _groundY;
    private bool _burst;
    private Vector3 _center;
    private LineRenderer _void, _rim, _wave, _flash;
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
      var mDeep  = Mat(ShadowBladeFx.DeepBloodRed);
      var mRed   = Mat(ShadowBladeFx.ScarletRed);
      var mBlaze = Mat(ShadowBladeFx.BlazingCrimson);
      var mWhite = Mat(ShadowBladeFx.WhiteHotCore);
      if (mDeep == null || mRed == null || mBlaze == null || mWhite == null) return false;

      float h = ModelHeight(target);
      _center = SbMath.Anchor(target, 0.5f, 0.6f, out _groundY);
      _r0 = Mathf.Clamp(h * 0.7f, 1.1f, 3.5f);

      _void  = Line(transform, "RD_SBK_Void",  mDeep,  RingN, true, 0.30f);
      _rim   = Line(transform, "RD_SBK_Rim",   mRed,   RingN, true, 0.12f);
      _wave  = Line(transform, "RD_SBK_Wave",  mBlaze, RingN, true, 0.22f);
      _flash = Line(transform, "RD_SBK_Flash", mWhite, RingN, true, 0.40f);

      CamBasis(out var camR, out var camU);
      for (int i = 0; i < Cracks; i++)
      {
        float a = (i + UnityEngine.Random.Range(-0.3f, 0.3f)) / Cracks * Mathf.PI * 2f;
        _crackDir[i] = (camR * Mathf.Cos(a) + camU * (Mathf.Sin(a) * 1.3f)).normalized; // 沿椭圆分布裂痕
        _crack[i] = Line(transform, "RD_SBK_Crack" + i, (i & 1) == 0 ? mWhite : mRed, 2, false, 0.10f);
      }
      for (int i = 0; i < Debris; i++)
      {
        int kind = i % 3; // 0 赤红晶刺（重）/ 1 炽烈碎屑 / 2 上飘血红光斑（微粒）
        _deb[i] = Line(transform, "RD_SBK_Deb" + i, kind == 0 ? mRed : (kind == 1 ? mBlaze : mDeep), 2, false, 0.12f);
        _vel[i] = (UnityEngine.Random.onUnitSphere + Vector3.up * 0.4f).normalized
          * (kind == 2 ? UnityEngine.Random.Range(1.5f, 3.0f) : UnityEngine.Random.Range(3.5f, 8.0f));
        _g[i] = kind == 0 ? 7f : (kind == 1 ? 3.5f : -1.4f);
        _w[i] = kind == 0 ? 0.15f : (kind == 1 ? 0.10f : 0.16f);
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

      // 阶段 1：坍缩（EaseInQuad 越收越快）+ 裂痕收束增亮
      if (_t < TImplode)
      {
        float k = _t / TImplode;
        float r = Mathf.Lerp(_r0, 0.15f, SbMath.InQuad(k));
        BillCircle(_void, _center, r, camR, camU);
        _void.widthMultiplier = 0.30f + 0.20f * k;
        SetA(_void, Color.white, 0.6f + 0.4f * k);
        BillCircle(_rim, _center, r + 0.08f, camR, camU, k * 3f);
        _rim.widthMultiplier = 0.12f;
        SetA(_rim, Color.white, 0.7f + 0.3f * k);
        float grow = SbMath.Out3(k * 2f);
        for (int i = 0; i < Cracks; i++)
          SbMath.Seg2(_crack[i], _center + _crackDir[i] * (r * 0.1f),
            _center + _crackDir[i] * (_r0 * (0.25f + 0.6f * grow) * (1f - 0.5f * k)), 0.10f, 0.6f + 0.4f * k);
        return;
      }

      if (!_burst)
      {
        _burst = true;
        for (int i = 0; i < Debris; i++) _pos[i] = _center;
      }

      float bk   = SbMath.Seg(_t, TImplode, Dur);
      float fade = 1f - SbMath.InQuad(bk); // 瞬间爆发后缓慢消散

      // 白热瞬闪
      float fk = SbMath.Seg(_t, TImplode, TImplode + 0.14f);
      BillCircle(_flash, _center, Mathf.Lerp(0.2f, 1.2f, SbMath.Out3(fk)), camR, camU);
      _flash.widthMultiplier = 0.40f * (1f - fk) + 0.01f;
      SetA(_flash, Color.white, 1f - fk);

      // 炽烈冲击波
      float wk = SbMath.Seg(_t, TImplode, TImplode + 0.38f);
      BillCircle(_wave, _center, Mathf.Lerp(0.2f, _r0 * 2.4f, SbMath.Out3(wk)), camR, camU);
      _wave.widthMultiplier = 0.22f * (1f - wk) + 0.02f;
      SetA(_wave, Color.white, 1f - wk);

      // 裂痕外甩后消散
      float ck = SbMath.Seg(_t, TImplode, TImplode + 0.20f);
      float ce = SbMath.Out3(ck);
      for (int i = 0; i < Cracks; i++)
        SbMath.Seg2(_crack[i], _center + _crackDir[i] * (_r0 * 0.6f * ce),
          _center + _crackDir[i] * (_r0 * (0.3f + 1.5f * ce)), 0.12f * (1f - ck), 1f - ck);

      // 深血红残迹圈
      float rr = Mathf.Lerp(0.5f, 0.08f, SbMath.Out3(bk));
      BillCircle(_void, _center, rr, camR, camU);
      _void.widthMultiplier = 0.22f * fade;
      SetA(_void, Color.white, fade);
      BillCircle(_rim, _center, rr + 0.06f, camR, camU, _t * 6f);
      _rim.widthMultiplier = 0.08f * fade;
      SetA(_rim, Color.white, 0.85f * fade);

      // 碎屑粒子积分：阻尼 + 重力 + 触地反弹，尾迹保底 10 cm
      float damp = Mathf.Exp(-Drag * dt);
      for (int i = 0; i < Debris; i++)
      {
        var lr = _deb[i];
        if (lr == null) continue;
        _vel[i] = _vel[i] * damp + Vector3.down * (_g[i] * dt);
        _pos[i] += _vel[i] * dt;
        if (_pos[i].y < _groundY + 0.05f)
        {
          _pos[i].y = _groundY + 0.05f;
          if (_vel[i].y < 0f) _vel[i].y *= -0.3f;
        }
        Vector3 tail = _vel[i] * 0.045f;
        if (tail.sqrMagnitude < 0.01f) tail = tail.normalized * 0.10f;
        lr.SetPosition(0, _pos[i] - tail);
        lr.SetPosition(1, _pos[i]);
        lr.widthMultiplier = _w[i] * (0.35f + 0.65f * fade);
        SetA(lr, Color.white, fade);
      }
    }
  }

  /// <summary>
  /// 强韧豁免成功·重击余辉（赤红）：前 90 ms 过曝定格（Hit Stop，零位移）→
  /// 22 Hz 阻尼驻波微颤（两端钉住、中段摆动，像被拨动的弦）+ 赤红余辉脉动 + 炽烈蒸汽上飘 → 0.50~0.80 s EaseInQuad 淡出。
  /// 方向与锚点承接同帧斩击，确保残痕与刀路精准重合。
  /// </summary>
  public class ShadowBladeHitAnim : MonoBehaviour
  {
    private const float Dur = 0.80f;
    private const float TStop = 0.09f;
    private const float TFade = 0.50f;
    private const float HalfLen = 2.6f;
    private const int ScarN = 9, SteamN = 8;

    private static Vector3 _lastDir;
    private static int _lastFrame = -1000;
    internal static void RememberSlash(Vector3 dir) { _lastDir = dir; _lastFrame = Time.frameCount; }

    private float _t;
    private Vector3 _center, _dir, _perp;
    private LineRenderer _halo, _scar, _core;
    private readonly LineRenderer[] _steam = new LineRenderer[SteamN];
    private readonly float[] _steamU = new float[SteamN];
    private readonly float[] _steamDelay = new float[SteamN];

    internal bool Init(UnitEntityData target)
    {
      if (target?.View == null) return false;
      var mDeep  = Mat(ShadowBladeFx.DeepBloodRed);
      var mRed   = Mat(ShadowBladeFx.ScarletRed);
      var mBlaze = Mat(ShadowBladeFx.BlazingCrimson);
      var mWhite = Mat(ShadowBladeFx.WhiteHotCore);
      if (mDeep == null || mRed == null || mBlaze == null || mWhite == null) return false;

      _center = SbMath.Anchor(target, 0.6f, 0.8f, out float groundY);
      CamBasis(out var camR, out var camU);
      _dir = (Time.frameCount - _lastFrame <= 2 && _lastDir.sqrMagnitude > 0.5f)
        ? _lastDir
        : SbMath.SlashDir(FlatRight(camR), HalfLen, _center.y - groundY - 0.15f);
      _perp = Vector3.Cross(_dir, Vector3.Cross(camR, camU));
      if (_perp.sqrMagnitude < 1e-4f) _perp = Vector3.up;
      _perp.Normalize();

      _halo = Line(transform, "RD_SBH_Halo", mDeep,  ScarN, false, 0.70f);
      _scar = Line(transform, "RD_SBH_Scar", mRed,   ScarN, false, 0.24f);
      _core = Line(transform, "RD_SBH_Core", mWhite, ScarN, false, 0.10f);
      SbMath.Taper(_halo, 0.25f);
      SbMath.Taper(_scar, 0.12f);
      SbMath.Taper(_core, 0.10f);
      for (int i = 0; i < SteamN; i++)
      {
        _steam[i] = Line(transform, "RD_SBH_Steam" + i, mBlaze, 2, false, 0.10f);
        _steamU[i] = 0.075f + 0.85f * (i + 0.5f) / SteamN;
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

    /// <summary>弦振动：u∈[0,1]，位移 = shake·sin(πu)，两端钉住。</summary>
    private Vector3 StringPoint(float u, float shake) =>
      _center + _dir * (HalfLen * (u * 2f - 1f)) + _perp * (shake * Mathf.Sin(u * Mathf.PI));

    private void Pluck(LineRenderer lr, float shake, float width, float alpha)
    {
      if (lr == null) return;
      for (int i = 0; i < ScarN; i++) lr.SetPosition(i, StringPoint(i / (float)(ScarN - 1), shake));
      lr.widthMultiplier = width;
      SetA(lr, Color.white, alpha);
    }

    private void Tick()
    {
      float fade = 1f - SbMath.InQuad(SbMath.Seg(_t, TFade, Dur));
      float shake = 0f, pulse = 1f, coreA, boost;
      if (_t < TStop)
      {
        coreA = 1f;   // 顿挫定格：零位移、过曝
        boost = 1.6f;
      }
      else
      {
        float s = _t - TStop;
        shake = Mathf.Exp(-4.5f * s) * Mathf.Sin(s * (2f * Mathf.PI * 22f)) * 0.22f;
        pulse = 0.7f + 0.3f * Mathf.Sin(s * 18f);
        coreA = Mathf.Lerp(1f, 0.35f, SbMath.Seg(s, 0f, 0.18f));
        boost = 1f;
      }

      Pluck(_halo, shake * 0.8f, 0.70f * boost * (0.75f + 0.25f * pulse) * fade, 0.9f * pulse * fade);
      Pluck(_scar, shake, 0.24f * boost * fade, fade);
      Pluck(_core, shake, 0.10f * boost * fade, coreA * fade);

      // 炽烈蒸汽：沿振动中的残痕上飘
      for (int i = 0; i < SteamN; i++)
      {
        var lr = _steam[i];
        if (lr == null) continue;
        float st = _t - TStop - _steamDelay[i];
        if (st <= 0f) { SetA(lr, Color.white, 0f); continue; }
        float rise = SbMath.Out3(st / 0.55f) * 0.7f;
        Vector3 p0 = StringPoint(_steamU[i], shake) + Vector3.up * rise;
        lr.SetPosition(0, p0);
        lr.SetPosition(1, p0 + Vector3.up * (0.2f + 0.25f * rise));
        lr.widthMultiplier = 0.10f * (1f + 0.8f * rise);
        SetA(lr, Color.white, Mathf.Clamp01(1f - st / 0.6f) * fade * 0.85f);
      }
    }
  }
}
