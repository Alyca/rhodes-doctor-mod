// 王冠的回响（10 环·特雷西娅）的专属自绘特效：黑冠显现 + 60尺粉红微尘与记忆光澜（纯视觉）。
// 摒弃生硬的蛛网/织网机械线条，严格还原《明日方舟》特雷西娅（魔王/纯黑的王冠）的标志性演出：
// 漫天轻柔升腾的【粉红微尘】（Memory Stardust）与灵魂光澜，平静而宏大地抚平全战场敌方魔力。
//
// 视觉构成：
//   1. 施法者头顶【魔王之冠（黑冠）】：
//      纤细优美的五尖冠冕轮廓 + 冠心悬浮白金菱形晶核，周身缠绕着升腾的微尘光晕。
//   2. 60 尺【全战场粉红微尘涟漪（Pink Dust Waves）】：
//      三道如水面轻漾的宽幅粉红与白金色宏大涟漪自中心缓出扩散至全场（18.3 米），
//      伴随 28 团在空间中缓缓漂浮、升腾、律动的粉红微尘星芒群，营造特雷西娅专属的幽邃与神圣感。
//   3. 敌方【增益消散微尘灵涡】：
//      范围内每个敌方目标周身绽开柔和的粉红微尘环与向上升腾的光屑，
//      象征其身上的法术增益化作情感与记忆的粉红微尘，消散于天地间。
using System;
using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using UnityEngine;
using static RhodesDoctor.MarkFxKit;

namespace RhodesDoctor
{
  internal static class CrownEchoFx
  {
    // 特雷西娅标志性粉红微尘配色体系
    internal static readonly Color PinkDust       = new Color(1.00f, 0.45f, 0.72f, 1f); // 标志性粉红微尘
    internal static readonly Color RoseStardust   = new Color(1.00f, 0.70f, 0.88f, 1f); // 记忆光澜粉白
    internal static readonly Color SoftWhite      = new Color(1.00f, 0.95f, 0.98f, 1f); // 纯净星芒白
    internal static readonly Color CrownBlackRose = new Color(0.85f, 0.22f, 0.55f, 1f); // 黑冠冠冕深粉
    internal static readonly Color CrownGold      = new Color(1.00f, 0.85f, 0.50f, 1f); // 冠心金辉

    internal const float DefaultRadius = 18.288f; // 60 尺 ≈ 18.3 米
    internal const int MaxEnemiesReact = 10;
    internal const int DustMoteCount = 28; // 全场漂浮粉红微尘星芒数量

    /// <summary>获取 60 尺内活着、可见的敌方单位（用于消散微尘演出，隐形的不标避免透视）。</summary>
    internal static List<UnitEntityData> GetEnemiesInRange(UnitEntityData caster, float range)
    {
      var list = new List<UnitEntityData>();
      try
      {
        foreach (var u in UnitScan.Take().Units)
        {
          if (list.Count >= MaxEnemiesReact) break;
          if (u == null || u == caster || !u.IsInGame || u.View == null) continue;
          if (u.Descriptor.State.IsDead) continue;
          if (u.Descriptor.State.HasCondition(UnitCondition.Invisible)) continue;
          if (!u.IsEnemy(caster)) continue;
          if (caster.HoldingState != null && u.HoldingState != caster.HoldingState) continue;
          var d = u.Position - caster.Position; d.y = 0f;
          if (d.magnitude <= range) list.Add(u);
        }
      }
      catch { }
      return list;
    }

    /// <summary>播放王冠的回响 10 环专属特效（纯视觉，不影响结算）。</summary>
    internal static void Play(UnitEntityData caster, Vector3 center, float radius = DefaultRadius)
    {
      try
      {
        if (caster?.View == null) return;
        var enemies = GetEnemiesInRange(caster, radius);
        var go = new GameObject("RD_CrownEchoFx") { layer = caster.View.gameObject.layer };
        var anim = go.AddComponent<CrownEchoAnim>();
        if (!anim.Init(caster, center, radius, enemies)) UnityEngine.Object.Destroy(go);
        Dbg.Log($"[RhodesDoctor] 王冠的回响·特雷西娅粉红微尘与黑冠显现（作用于 {enemies.Count} 个敌方单位）。");
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 王冠的回响特效失败（效果照常）：\n" + e);
      }
    }
  }

  /// <summary>
  /// 王冠的回响特效主体（约 3.6 秒，纯视觉）：
  ///   1. 黑冠：凹弧侧翼的五尖冠冕剪影 + 两端上翘的弧形冠带与白金冠沿 + 独立沉浮、绕竖轴自转的菱形晶核 + 冠周升腾微尘漩流；
  ///   2. 涟漪：主涟漪与次级微波的半径带相位相反的谐波起伏（波峰交错），
  ///      两道波先后抵达 60 尺时把外围环"推"得向外一荡再回弹；环身亮度沿周向流动，不是死板的实线圆；
  ///   3. 星尘：36 缕带拖尾的星尘自黑冠旋出、星系臂般舒展到全场，
  ///      径向 / 竖向各叠两组不同频率的正弦谐波随风摇曳、缓缓上升，颜色在 粉红 → 玫瑰粉白 → 柔白 之间呼吸；
  ///   4. 敌方：胸口微尘环旋转展开成上升螺旋，尾端向天空轻柔散逸；脚下柔光环流转，头顶一缕光屑升腾。
  /// </summary>
  public class CrownEchoAnim : MonoBehaviour
  {
    private const float Dur = 3.6f;
    private const float FadeStart = 2.7f;
    private const int MoteCount = 36;        // 全场星尘缕数
    private const int MoteTrail = 7;         // 每缕拖尾采样点（0 = 尾，末位 = 头）
    private const float TrailStep = 0.06f;   // 拖尾采样间隔（秒）
    private const int RippleSeg = 96;        // 涟漪采样（18 m 半径下仍圆润）
    private const int CrownSamples = 41;     // 冠冕剪影采样：步长 0.05，五个峰（-1/-0.5/0/0.5/1）恰好落在采样点上
    private const int CrownTopPts = CrownSamples + 2; // + 左右两个冠带角点
    private const int CrownBandPts = 17;
    private const int SwirlPts = 48;
    private const int SpiralPts = 40;

    private UnitEntityData _caster;
    private Vector3 _center;
    private float _radius, _casterH, _t;
    private readonly List<UnitEntityData> _enemies = new();

    // ── 1. 黑冠 ──
    private LineRenderer _crownTop;    // 冠冕剪影（五尖凹弧）
    private LineRenderer _crownBand;   // 冠带（两端上翘的弧）
    private LineRenderer _crownRim;    // 冠沿白金细光
    private LineRenderer _crownCore;   // 菱形晶核
    private LineRenderer _crownSwirl;  // 冠周微尘漩流

    // ── 2. 涟漪 ──
    private LineRenderer _ripMain, _ripSecond, _ripOuter;

    // ── 3. 星尘 ──
    private struct MoteSeed
    {
      public float Angle, Dist, BaseY, Rise, Orbit, F1, F2, Amp, Phase, Hue, Delay;
    }
    private readonly MoteSeed[] _seeds = new MoteSeed[MoteCount];
    private readonly LineRenderer[] _motes = new LineRenderer[MoteCount];

    // ── 4. 敌方灵涡 ──
    private sealed class EnemyVortex
    {
      public UnitEntityData Unit;
      public float Height, Phase;
      public LineRenderer Spiral, Ground, Wisp;
    }
    private readonly List<EnemyVortex> _vortices = new();

    // 复用的渐变缓冲（避免每帧分配）
    private readonly Gradient _grad = new();
    private readonly GradientColorKey[] _ck = new GradientColorKey[2];
    private readonly GradientAlphaKey[] _ak = new GradientAlphaKey[6];

    internal bool Init(UnitEntityData caster, Vector3 center, float radius, List<UnitEntityData> enemies)
    {
      var mPink  = Mat(CrownEchoFx.PinkDust);
      var mRose  = Mat(CrownEchoFx.RoseStardust);
      var mWhite = Mat(CrownEchoFx.SoftWhite);
      var mCrown = Mat(CrownEchoFx.CrownBlackRose);
      var mGold  = Mat(CrownEchoFx.CrownGold);
      if (mPink == null || mRose == null || mWhite == null || mCrown == null || mGold == null) return false;

      _caster = caster;
      _center = center;
      _radius = radius;
      _casterH = ModelHeight(caster);   // 只量一次：ModelHeight 每次都要遍历蒙皮网格
      if (enemies != null) _enemies.AddRange(enemies);

      // 1. 黑冠
      _crownTop   = Line(transform, "RD_CE_CrownTop",   mCrown, CrownTopPts,  false, 0.050f);
      _crownBand  = Line(transform, "RD_CE_CrownBand",  mCrown, CrownBandPts, false, 0.060f);
      _crownRim   = Line(transform, "RD_CE_CrownRim",   mGold,  CrownBandPts, false, 0.022f);
      _crownCore  = Line(transform, "RD_CE_CrownCore",  mGold,  4,            true,  0.035f);
      _crownSwirl = Line(transform, "RD_CE_CrownSwirl", mPink,  SwirlPts,     false, 0.040f);
      _crownSwirl.widthCurve = Taper(0.15f, 1f);

      // 2. 涟漪（平躺；环身亮度由渐变沿周向流动）
      _ripMain   = Flat(transform, "RD_CE_RipMain",   mPink, RippleSeg, true, 0.38f);
      _ripSecond = Flat(transform, "RD_CE_RipSecond", mRose, RippleSeg, true, 0.22f);
      _ripOuter  = Flat(transform, "RD_CE_RipOuter",  mPink, RippleSeg, true, 0.10f);

      // 3. 星尘种子（固定种子：每次施法同样舒展，不会偶发挤成一团）
      var rng = new System.Random(1014);
      float R() => (float)rng.NextDouble();
      Material[] dustMats = { mPink, mRose, mWhite };
      for (int i = 0; i < MoteCount; i++)
      {
        float dist = Mathf.Lerp(1.2f, radius * 0.92f, Mathf.Sqrt(R()));
        _seeds[i] = new MoteSeed
        {
          Angle = (i + R() * 0.8f) * Mathf.PI * 2f / MoteCount, // 均匀铺开 + 抖动
          Dist  = dist,
          BaseY = Mathf.Lerp(0.4f, 2.4f, R()),
          Rise  = Mathf.Lerp(0.15f, 0.45f, R()),
          Orbit = Mathf.Lerp(0.30f, 0.06f, dist / radius),     // 内圈回旋快、外圈慢
          F1    = Mathf.Lerp(0.9f, 1.8f, R()),
          F2    = Mathf.Lerp(2.3f, 3.7f, R()),
          Amp   = Mathf.Lerp(0.15f, 0.45f, R()),
          Phase = R() * Mathf.PI * 2f,
          Hue   = R() * 3f,
          Delay = Mathf.Lerp(0.05f, 0.55f, R()),
        };
        var lr = Line(transform, "RD_CE_Dust" + i, dustMats[i % 3], MoteTrail, false, Mathf.Lerp(0.035f, 0.085f, R()));
        lr.widthCurve = Taper(0.08f, 1f);
        _motes[i] = lr;
      }

      // 4. 敌方灵涡
      for (int i = 0; i < _enemies.Count; i++)
      {
        var e = _enemies[i];
        if (e == null) continue;
        var v = new EnemyVortex
        {
          Unit   = e,
          Height = ModelHeight(e),
          Phase  = i * 2.399f, // 黄金角错相：相邻敌人的漩涡不同步
          Spiral = Line(transform, "RD_CE_ESpiral" + i, mRose,  SpiralPts, false, 0.045f),
          Ground = Flat(transform, "RD_CE_EGround" + i, mPink,  RingSeg,   true,  0.050f),
          Wisp   = Line(transform, "RD_CE_EWisp"   + i, mWhite, MoteTrail, false, 0.050f),
        };
        v.Spiral.widthCurve = Taper(1f, 0.30f);
        v.Wisp.widthCurve = Taper(0.10f, 1f);
        _vortices.Add(v);
      }

      return true;
    }

    private void Update()
    {
      _t += Time.deltaTime;
      if (_t >= Dur || _caster == null || _caster.View == null) { Destroy(gameObject); return; }
      try
      {
        float master = _t < FadeStart ? 1f : 1f - Mathf.Clamp01((_t - FadeStart) / (Dur - FadeStart));
        CamBasis(out var right, out var up);
        DrawCrown(_caster.Position, right, up, master);
        DrawRipples(master);
        DrawDust(master);
        DrawVortices(right, master);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 王冠的回响特效更新失败（已移除）：\n" + e);
        Destroy(gameObject);
      }
    }

    // ════════════ 1. 黑冠 ════════════
    private void DrawCrown(Vector3 casterPos, Vector3 right, Vector3 up, float master)
    {
      float enter = Smooth(Mathf.Clamp01(_t / 0.6f));
      float appear = enter * master;
      float breath = 1f + 0.025f * Mathf.Sin(_t * 1.7f);
      // 缓慢沉浮；入场时自上方 0.35 m 轻轻落定
      var c = casterPos + Vector3.up * (_casterH + 1.05f + 0.05f * Mathf.Sin(_t * 1.1f) + 0.35f * (1f - enter));

      const float halfW = 0.40f, spikeH = 0.42f, lift = 0.10f;
      float hw = halfW * breath;

      // 冠冕剪影：左角 → 五尖峰（中 1.0 / 次 0.72 / 侧 0.50，侧翼为凹弧）→ 右角
      _crownTop.SetPosition(0, c - right * hw + up * BandY(-1f));
      for (int i = 0; i < CrownSamples; i++)
      {
        float x = -1f + 2f * i / (CrownSamples - 1);
        _crownTop.SetPosition(i + 1, c + right * (x * hw) + up * (lift + CrownProfile(x) * spikeH * breath));
      }
      _crownTop.SetPosition(CrownTopPts - 1, c + right * hw + up * BandY(1f));
      SetA(_crownTop, CrownEchoFx.CrownBlackRose, appear);

      // 冠带：两端微微上翘的弧（略高视角下冠圈的前沿），内侧一线白金冠沿
      for (int i = 0; i < CrownBandPts; i++)
      {
        float x = -1f + 2f * i / (CrownBandPts - 1);
        _crownBand.SetPosition(i, c + right * (x * hw) + up * BandY(x));
        _crownRim.SetPosition(i,  c + right * (x * hw * 0.90f) + up * (BandY(x) + 0.065f));
      }
      SetA(_crownBand, CrownEchoFx.CrownBlackRose, appear * 0.95f);
      SetA(_crownRim, CrownEchoFx.CrownGold, appear * (0.55f + 0.25f * Mathf.Sin(_t * 2.3f)));

      // 冠心菱形晶核：独立的慢沉浮 + 绕竖轴自转（水平半宽随 |cos| 变化，模拟立体转动）
      var core = c + up * (lift + 0.20f + 0.035f * Mathf.Sin(_t * 1.3f + 0.8f));
      float cw = 0.075f * (0.30f + 0.70f * Mathf.Abs(Mathf.Cos(_t * 1.15f)));
      const float ch = 0.11f;
      _crownCore.SetPosition(0, core + up * ch);
      _crownCore.SetPosition(1, core + right * cw);
      _crownCore.SetPosition(2, core - up * ch);
      _crownCore.SetPosition(3, core - right * cw);
      SetA(_crownCore, CrownEchoFx.CrownGold, appear * (0.80f + 0.20f * Mathf.Sin(_t * 2.9f)));

      // 冠周微尘漩流：绕冠缓旋上升的开口螺旋，头亮尾隐
      for (int i = 0; i < SwirlPts; i++)
      {
        float s = i / (float)(SwirlPts - 1);
        float a = -_t * 1.4f + s * Mathf.PI * 3.2f;
        float r = 0.30f + 0.38f * s;
        float y = -0.30f + 0.85f * s + 0.04f * Mathf.Sin(_t * 2f + s * 9f);
        _crownSwirl.SetPosition(i, c + new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
      }
      var sc = Breath(_t * 0.4f);
      _crownSwirl.startColor = new Color(sc.r, sc.g, sc.b, 0f);
      _crownSwirl.endColor   = new Color(sc.r, sc.g, sc.b, appear * 0.75f);
    }

    // ════════════ 2. 交错涟漪 ════════════
    private void DrawRipples(float master)
    {
      const float tMain = 1.5f, delay2 = 0.24f;

      // 主涟漪：三次缓出扩散到 60 尺，亮斑沿环流动
      float w1 = Mathf.Clamp01(_t / tMain);
      Ripple(_ripMain, Mathf.Lerp(0.6f, _radius, EaseOut3(w1)), +1f, 0.08f);
      SoftLoop(_ripMain, CrownEchoFx.PinkDust, (1f - w1 * w1) * 0.80f * master, _t * 0.35f);

      // 次级微波：晚 0.24 s，谐波相位取反 → 与主涟漪波峰交错（彼此"推"着走）
      _ripSecond.enabled = _t > delay2;
      if (_ripSecond.enabled)
      {
        float w2 = Mathf.Clamp01((_t - delay2) / tMain);
        Ripple(_ripSecond, Mathf.Lerp(0.4f, _radius * 0.97f, EaseOut3(w2)), -1f, 0.10f);
        SoftLoop(_ripSecond, CrownEchoFx.RoseStardust, (1f - w2 * w2) * 0.65f * master, -_t * 0.5f + 0.25f);
      }

      // 外围环：两道波先后抵达时被推得向外一荡（高斯脉冲），随后轻轻回弹
      float push = 0.050f * Gauss(_t - tMain, 0.22f)
                 + 0.030f * Gauss(_t - tMain - delay2, 0.22f)
                 - 0.015f * Gauss(_t - tMain - 0.55f, 0.30f);
      Ripple(_ripOuter, _radius * (1f + push), 0.35f, 0.14f);
      float outA = Smooth(Mathf.Clamp01((_t - 0.5f) / 0.9f)) * (0.45f + 6f * Mathf.Max(0f, push)) * master;
      SoftLoop(_ripOuter, CrownEchoFx.PinkDust, outA, _t * 0.12f);
    }

    /// <summary>带谐波起伏的平躺环：r(θ) = r·(1 + sign·(0.030·sin(6θ+2.2t) + 0.018·sin(11θ−3.1t)))。</summary>
    private void Ripple(LineRenderer lr, float r, float sign, float y)
    {
      int n = lr.positionCount;
      for (int i = 0; i < n; i++)
      {
        float a = i * Mathf.PI * 2f / n;
        float k = 1f + sign * (0.030f * Mathf.Sin(6f * a + _t * 2.2f) + 0.018f * Mathf.Sin(11f * a - _t * 3.1f));
        lr.SetPosition(i, _center + new Vector3(Mathf.Cos(a) * r * k, y, Mathf.Sin(a) * r * k));
      }
    }

    // ════════════ 3. 漫天星尘 ════════════
    private void DrawDust(float master)
    {
      float crownY = _casterH + 1.2f;
      for (int i = 0; i < MoteCount; i++)
      {
        var s = _seeds[i];
        var lr = _motes[i];
        float age = _t - s.Delay;
        if (age <= 0f) { lr.enabled = false; continue; }
        lr.enabled = true;

        // 拖尾 = 同一缕星尘在过去几个时刻的位置（尾 → 头）
        for (int j = 0; j < MoteTrail; j++)
        {
          float tj = Mathf.Max(s.Delay, _t - (MoteTrail - 1 - j) * TrailStep);
          lr.SetPosition(j, MotePos(s, tj, crownY));
        }

        float twinkle = 0.70f + 0.30f * Mathf.Sin(_t * s.F2 * 1.6f + s.Phase);
        float a = Smooth(Mathf.Clamp01(age / 0.35f)) * twinkle * master * 0.90f;
        var c = Breath(s.Hue + _t * 0.55f);
        lr.startColor = new Color(c.r, c.g, c.b, 0f);
        lr.endColor   = new Color(c.r, c.g, c.b, a);
      }
    }

    /// <summary>
    /// 一缕星尘在时刻 t 的位置：自黑冠旋出（星系臂般舒展，二次缓出）→ 绕施法点缓慢回旋；
    /// 径向与竖向各叠两组不同频率的正弦谐波（随风摇曳），整体缓缓上升。
    /// </summary>
    private Vector3 MotePos(in MoteSeed s, float t, float crownY)
    {
      float k = Mathf.Clamp01((t - s.Delay) / 2.2f);
      float spread = 1f - (1f - k) * (1f - k);
      float sway = (Mathf.Sin(t * s.F1 + s.Phase) + 0.45f * Mathf.Sin(t * s.F2 + s.Phase * 1.7f)) * s.Amp;
      float dist = Mathf.Lerp(0.35f, s.Dist, spread) + sway * spread;
      float ang = s.Angle + (1f - spread) * 1.4f + t * s.Orbit;
      float y = Mathf.Lerp(crownY, s.BaseY, spread)
              + s.Rise * Mathf.Max(0f, t - s.Delay)
              + 0.10f * Mathf.Sin(t * s.F2 * 0.7f + s.Phase);
      return _center + new Vector3(Mathf.Cos(ang) * dist, y, Mathf.Sin(ang) * dist);
    }

    // ════════════ 4. 敌方增益剥离微尘灵涡 ════════════
    private void DrawVortices(Vector3 right, float master)
    {
      float appear = Smooth(Mathf.Clamp01((_t - 0.30f) / 0.40f)) * master;
      float unfurl = Smooth(Mathf.Clamp01((_t - 0.70f) / 1.80f)); // 0 = 闭合微尘环，1 = 上升螺旋
      for (int i = 0; i < _vortices.Count; i++)
      {
        var v = _vortices[i];
        bool alive = appear > 0f && v.Unit != null && v.Unit.View != null && v.Unit.IsInGame;
        v.Spiral.enabled = v.Ground.enabled = v.Wisp.enabled = alive;
        if (!alive) continue;

        var feet = v.Unit.Position;
        var chest = feet + Vector3.up * (v.Height * 0.55f);

        // 胸口微尘环 → 旋转展开为上升螺旋：起初 s∈[0,1] 恰好绕一整圈（闭合环），
        // 展开后圈数增至 2.5、半径向外 ×1.9、尾端抬升 2.9 m 并渐隐散逸
        for (int j = 0; j < SpiralPts; j++)
        {
          float s = j / (float)(SpiralPts - 1);
          float a = v.Phase + _t * 2.2f + s * Mathf.PI * 2f * (1f + 1.5f * unfurl);
          float r = 0.55f * (1f + 0.9f * unfurl * s) * (1f + 0.04f * Mathf.Sin(_t * 5f + s * 12f));
          float y = unfurl * (0.5f + 2.4f * s * s);
          v.Spiral.SetPosition(j, chest + new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
        }
        var sc = Breath(v.Phase + _t * 0.6f);
        v.Spiral.startColor = new Color(sc.r, sc.g, sc.b, appear * 0.90f * (1f - 0.5f * unfurl));
        v.Spiral.endColor   = new Color(sc.r, sc.g, sc.b, appear * 0.85f * (1f - unfurl));

        // 脚下柔光环：五瓣微波起伏、亮度流转，随展开轻轻外扩
        float rg = 0.95f + 0.35f * unfurl;
        int gn = v.Ground.positionCount;
        for (int j = 0; j < gn; j++)
        {
          float a = j * Mathf.PI * 2f / gn;
          float k = 1f + 0.05f * Mathf.Sin(5f * a + _t * 2.6f + v.Phase);
          v.Ground.SetPosition(j, feet + new Vector3(Mathf.Cos(a) * rg * k, 0.06f, Mathf.Sin(a) * rg * k));
        }
        SoftLoop(v.Ground, CrownEchoFx.PinkDust, appear * 0.65f, _t * 0.5f + v.Phase);

        // 头顶一缕光屑：自头顶升起、左右摇曳，拖尾渐隐
        for (int j = 0; j < MoteTrail; j++)
        {
          float tj = Mathf.Max(0.30f, _t - (MoteTrail - 1 - j) * TrailStep);
          float hy = v.Height + 0.25f + (tj - 0.30f) * 0.75f;
          v.Wisp.SetPosition(j, feet + Vector3.up * hy + right * (0.18f * Mathf.Sin(tj * 2.4f + v.Phase)));
        }
        var wc = CrownEchoFx.SoftWhite;
        v.Wisp.startColor = new Color(wc.r, wc.g, wc.b, 0f);
        v.Wisp.endColor   = new Color(wc.r, wc.g, wc.b, appear * 0.85f);
      }
    }

    // ════════════ 工具 ════════════
    /// <summary>冠冕上沿：五个尖峰叠加，每峰 h·(1-|d|/0.5)²（凹弧侧翼，尖而不硬）。</summary>
    private static float CrownProfile(float x)
      => Peak(x, 0f, 1.00f) + Peak(x, -0.5f, 0.72f) + Peak(x, 0.5f, 0.72f) + Peak(x, -1f, 0.50f) + Peak(x, 1f, 0.50f);

    private static float Peak(float x, float at, float h)
    {
      float k = 1f - Mathf.Abs(x - at) / 0.5f;
      return k > 0f ? h * k * k : 0f;
    }

    private static float BandY(float x) => 0.05f * x * x;

    /// <summary>粉红 → 玫瑰粉白 → 柔白 → 粉红 的平滑循环（h 每 +1 走一段）。</summary>
    private static Color Breath(float h)
    {
      h = Mathf.Repeat(h, 3f);
      if (h < 1f) return Color.Lerp(CrownEchoFx.PinkDust, CrownEchoFx.RoseStardust, Smooth(h));
      if (h < 2f) return Color.Lerp(CrownEchoFx.RoseStardust, CrownEchoFx.SoftWhite, Smooth(h - 1f));
      return Color.Lerp(CrownEchoFx.SoftWhite, CrownEchoFx.PinkDust, Smooth(h - 2f));
    }

    /// <summary>闭合环的"流动亮度"：6 个透明度关键帧按两个周期的正弦分布，flow 推动亮斑沿环流转（首尾无缝）。</summary>
    private void SoftLoop(LineRenderer lr, Color c, float a, float flow)
    {
      _ck[0] = new GradientColorKey(c, 0f);
      _ck[1] = new GradientColorKey(c, 1f);
      for (int k = 0; k < _ak.Length; k++)
      {
        float x = k / (float)(_ak.Length - 1);
        float m = 0.55f + 0.45f * Mathf.Sin((x * 2f + flow) * Mathf.PI * 2f);
        _ak[k] = new GradientAlphaKey(Mathf.Clamp01(a * m), x);
      }
      _grad.SetKeys(_ck, _ak);
      lr.colorGradient = _grad;
    }

    private static AnimationCurve Taper(float start, float end)
      => new AnimationCurve(new Keyframe(0f, start), new Keyframe(1f, end));

    private static float Smooth(float x) => x * x * (3f - 2f * x);
    private static float EaseOut3(float x) { float k = 1f - x; return 1f - k * k * k; }
    private static float Gauss(float x, float sigma) => Mathf.Exp(-(x * x) / (2f * sigma * sigma));
  }}
