// 火力侦察（1 环·克洛丝）的专属特效：侦察锁定框（琥珀色，纯视觉）。
// 取代原来借的"火之烙印"（头顶一团火）。
// 增益名按 CreateSpellBuff 规则：RhodesDoctorBuffFireRecon。
using Kingmaker.UnitLogic.Buffs;
using UnityEngine;
using static RhodesDoctor.MarkFxKit;

namespace RhodesDoctor
{
  internal static class FireReconFx
  {
    internal static readonly Color Amber = new Color(1.00f, 0.60f, 0.12f, 1f);
    internal static readonly Color Pale  = new Color(1.00f, 0.90f, 0.55f, 1f);

    /// <summary>
    /// GUID 兜底 = <c>Guids.Buff(10)</c>：`CreateSpellBuff(s, i)` 用的是 **Specs 下标** i，
    /// 而 `Specs[10].Key == "FireRecon"`。⚠ 若日后重排 `SpellsSetup.Specs`，这个下标要跟着改
    /// （名字那一路仍然有效，这里只是第二条保险，见 <see cref="MarkFxTracker.Is"/>）。
    /// </summary>
    internal static readonly MarkFxTracker Tracker = new MarkFxTracker(
      "RhodesDoctorBuffFireRecon", "火力侦察·侦察锁定框", go => go.AddComponent<FireReconAnim>(),
      Guids.Buff(10));
  }

  public class FireReconAnim : MarkFxBase
  {
    private const float IntroDur = 0.30f;
    private const float PingPeriod = 1.6f;
    private const float ScanPeriod = 1.1f;

    /// <summary>
    /// 侦察框整体的垂直位置 = **模型高度 × 这个系数**（0.5 = 正好居中：框上下各盖 0.55H）。
    /// ⚠ 作者 10-16 进游戏实测："黄方框可以往上一点点" → 0.5 → **0.62**（≈ 2 m 高的模型抬高约 0.24 m）。
    /// **框体 / 框内扫描线 / 框顶倒三角是同一套，一起跟着这个系数走**；想再上/下只改这一个数
    /// （用比例而不是固定米数：大体型也跟着等比抬，不会变成"对大体型没效果"）。
    /// </summary>
    private const float BracketCenter = 0.62f;

    private readonly LineRenderer[] _ping = new LineRenderer[2];
    private readonly LineRenderer[] _br = new LineRenderer[4];
    private LineRenderer _scan, _tri;

    protected override bool Build(Buff buff)
    {
      var mA = Mat(FireReconFx.Amber);
      var mP = Mat(FireReconFx.Pale);
      if (mA == null || mP == null)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 火力侦察·侦察锁定框放弃：StreakMaterialPlain 无可用材质。");
        return false;
      }
      _ping[0] = Flat(transform, "RD_FR_Ping", mA, RingSeg, true, 0.04f);
      _ping[1] = Flat(transform, "RD_FR_Ping2", mP, RingSeg, true, 0.025f);
      for (int i = 0; i < 4; i++) _br[i] = Line(transform, "RD_FR_Bracket", mA, 3, false, 0.045f);
      _scan = Line(transform, "RD_FR_Scan", mP, 2, false, 0.02f);
      _tri  = Line(transform, "RD_FR_Tri", mA, 3, true, 0.04f);
      return true;
    }

    protected override void Tick(float dt)
    {
      var c = Unit.Position;
      CamBasis(out var camRight, out _);
      var right = FlatRight(camRight);
      var up = Vector3.up;
      float appear = Mathf.Clamp01(T / 0.15f) * Master;
      float ki = Mathf.Clamp01(T / IntroDur);
      float e = 1f - (1f - ki) * (1f - ki);

      // ① 地面雷达回波：两道错开半周期的扩散环
      for (int k = 0; k < 2; k++)
      {
        float f = Mathf.Repeat(T + k * PingPeriod * 0.5f, PingPeriod) / PingPeriod;
        float r = Mathf.Lerp(Radius * 0.4f, Radius * 2.4f, 1f - (1f - f) * (1f - f));
        Circle(_ping[k], c, r, 0.05f);
        SetA(_ping[k], k == 0 ? FireReconFx.Amber : FireReconFx.Pale, 0.7f * (1f - f) * appear);
      }

      // ② 竖直侦察框：四角括号，入场从 1.7 倍收紧（锁定），入场期间快闪
      float breathe = 1f + 0.03f * Mathf.Sin(T * 5f);
      float halfW = Radius * 0.75f * Mathf.Lerp(1.7f, 1f, e) * breathe;
      float halfH = Height * 0.55f * Mathf.Lerp(1.4f, 1f, e) * breathe;
      var mid = c + up * (Height * BracketCenter);
      float arm = Mathf.Min(halfW, halfH) * 0.35f;
      float blink = ki < 1f ? (Mathf.Repeat(T * 16f, 1f) > 0.5f ? 1f : 0.35f) : 1f;
      for (int i = 0; i < 4; i++)
      {
        float sx = (i == 0 || i == 3) ? 1f : -1f;
        float sy = (i < 2) ? 1f : -1f;
        var corner = mid + right * (sx * halfW) + up * (sy * halfH);
        _br[i].SetPosition(0, corner - right * (sx * arm));
        _br[i].SetPosition(1, corner);
        _br[i].SetPosition(2, corner - up * (sy * arm));
        SetA(_br[i], FireReconFx.Amber, 0.95f * blink * appear);
      }

      // ③ 框内自上而下的扫描线
      float sf = Mathf.Repeat(T, ScanPeriod) / ScanPeriod;
      var scanPos = mid + up * Mathf.Lerp(halfH, -halfH, sf);
      _scan.SetPosition(0, scanPos - right * (halfW * 0.85f));
      _scan.SetPosition(1, scanPos + right * (halfW * 0.85f));
      SetA(_scan, FireReconFx.Pale, 0.65f * Mathf.Sin(sf * Mathf.PI) * appear);

      // ④ 框顶倒三角（目标指示）
      var tip = mid + up * (halfH + 0.18f + 0.06f * Mathf.Sin(T * 3f));
      _tri.SetPosition(0, tip);
      _tri.SetPosition(1, tip + up * 0.24f + right * 0.14f);
      _tri.SetPosition(2, tip + up * 0.24f - right * 0.14f);
      SetA(_tri, FireReconFx.Amber, 0.95f * appear);
    }
  }
}
