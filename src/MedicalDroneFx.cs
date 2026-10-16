// 医疗无人机的机体：给每个带"医疗无人机"增益的友军，在**头顶挂一个悬浮的小物件**（借原版武器模型网格）。
// 每次治疗（增益每轮触发）再由增益动作在持有者身上闪一下**治疗光**（见 SpellsSetup 的 MedicalDrone 分支）。
//
// 走过的路（完整记录见 docs/dev-notes.md §7.10.5）：
//   ① 金色光环——太重；② 借光亮术 FX——施法爆发，看不见；③ 自建球/面片 + `Shader.Find`——看不见；
//   ④ 借"给单位挂视觉"的原版增益（提灯 / 治疗审判）——`FxOnStart` 也是施法爆发，看不见。
//   ⇒ **定案（作者 10-10）**：借**物体模型**当机体放头顶跟随。模型是**网格**、一定能渲染，
//     不像"借 FX 素材"那样受"是不是常驻"的影响。
//
// 生命周期：订阅 IUnitBuffHandler，按 buff 蓝图 GUID 过滤（只认医疗无人机增益，= Guids.Buff(11)）。
using System;
using System.Collections.Generic;
using Kingmaker.PubSubSystem;
using Kingmaker.UnitLogic.Buffs;
using UnityEngine;

namespace RhodesDoctor
{
  internal static class MedicalDroneFx
  {
    /// <summary>机体染色：**赫默棕**。想保留模型原色就把 <see cref="TintBody"/> 设成 false。</summary>
    internal static readonly Color SilenceBrown = new Color(0.95f, 0.62f, 0.35f, 1f);

    /// <summary>是否把机体染成赫默棕。</summary>
    private const bool TintBody = true;

    /// <summary>染色时的去色提亮（传给 FxTint）。</summary>
    private const float TintLift = 0.35f;

    /// <summary>机体缩放（模型原始尺寸 × 这个值）。太大 / 太小改这里。</summary>
    private const float BodyScale = 0.35f;

    /// <summary>
    /// 机体**平放**的俯仰角（度）：把模型从"竖着（像拿在手里）"转成"平着（像悬浮的平台）"。
    /// 一般 90 即可；若模型朝向不同（平过来变成竖的 / 躺着朝侧面），试 0 / 180 / 270 或改 Yaw。
    /// </summary>
    private const float BodyPitch = 90f;

    /// <summary>机体偏航（度）：平放后绕竖轴的初始朝向，一般不用动。</summary>
    private const float BodyYaw = 0f;

    /// <summary>悬浮高度（米）。太低会穿模（蹭到角色头/肩），按需调高。</summary>
    private const float AttachHeight = 2.4f;

    /// <summary>自转速度（度/秒）。</summary>
    private const float SpinDegPerSec = 60f;

    /// <summary>上下浮动幅度（米）与频率。</summary>
    private const float BobAmplitude = 0.08f;
    private const float BobSpeed = 1.6f;

    /// <summary>医疗无人机增益的蓝图 GUID（<c>Guids.Buff(11)</c>，见 SpellsSetup.Specs 下标）。</summary>
    private static readonly string MedicalDroneBuffGuid =
      Guids.Buff(11).Replace("-", "").ToLowerInvariant();

    /// <summary>单位 → 挂在它身上的机体根物体。增益移除时据此销毁。</summary>
    private static readonly Dictionary<Kingmaker.EntitySystem.Entities.UnitEntityData, GameObject> Live = new();

    internal static void OnBuffAdded(Buff buff)
    {
      try
      {
        if (!IsMedicalDrone(buff)) return;
        var unit = buff.Owner?.Unit;
        if (unit == null) return;
        var view = unit.View;
        if (view == null) return; // 单位还没 View（刚加载/未进场景）就跳过

        Prune();
        RemoveFor(unit);

        var prefab = FxLibrary.MedicalDroneBody?.Load(false, true);
        if (prefab == null)
        {
          Dbg.Log("[RhodesDoctor] 医疗无人机·机体素材借不到（看上面的『武器模型』借用日志）。");
          return;
        }

        // 根物体挂在单位 View 下、置于头顶——单位移动时跟着走，增益移除时销毁（与 HasteTrailFx 同款）。
        var root = new GameObject("RhodesDoctorMedicalDrone");
        root.layer = view.gameObject.layer;
        root.transform.SetParent(view.transform, false);
        root.transform.localPosition = new Vector3(0f, AttachHeight, 0f);
        root.transform.localRotation = Quaternion.identity;

        // 机体：实例化借来的模型网格，缩小到"小物件"大小、**平着放**（作者 10-10）。
        var body = UnityEngine.Object.Instantiate(prefab, root.transform);
        body.name = "Body";
        body.transform.localPosition = Vector3.zero;
        body.transform.localRotation = Quaternion.Euler(BodyPitch, BodyYaw, 0f);
        body.transform.localScale = Vector3.one * BodyScale;
        if (TintBody) FxTint.Apply(body, SilenceBrown, TintLift, tintTextures: true);

        // 悬浮感：缓慢自转 + 上下浮动。
        var hover = root.AddComponent<MedicalDroneHover>();
        hover.SpinDegPerSec = SpinDegPerSec;
        hover.BobAmplitude = BobAmplitude;
        hover.BobSpeed = BobSpeed;

        Live[unit] = root;
        Dbg.Log(
          $"[RhodesDoctor] 医疗无人机·机体已生成（模型缩放={BodyScale} 世界缩放={root.transform.lossyScale.x:0.00}）。");
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 医疗无人机·机体生成失败：\n" + e);
      }
    }

    internal static void OnBuffRemoved(Buff buff)
    {
      try
      {
        if (!IsMedicalDrone(buff)) return;
        var unit = buff.Owner?.Unit;
        if (unit == null) return;
        RemoveFor(unit);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 医疗无人机·机体清理失败：\n" + e);
      }
    }

    private static void RemoveFor(Kingmaker.EntitySystem.Entities.UnitEntityData unit)
    {
      if (!Live.TryGetValue(unit, out var go)) return;
      Live.Remove(unit);
      if (go != null) UnityEngine.Object.Destroy(go);
    }

    private static bool IsMedicalDrone(Buff buff)
    {
      var guid = buff?.Blueprint?.AssetGuid.ToString();
      if (string.IsNullOrEmpty(guid)) return false;
      return guid.Replace("-", "").ToLowerInvariant() == MedicalDroneBuffGuid;
    }

    private static void Prune()
    {
      var dead = new List<Kingmaker.EntitySystem.Entities.UnitEntityData>();
      foreach (var kv in Live)
        if (kv.Key == null || kv.Value == null) dead.Add(kv.Key);
      foreach (var k in dead) Live.Remove(k);
    }
  }

  /// <summary>机体的"悬浮"动画：绕自身竖轴缓慢自转 + 上下浮动（基准位置 = 挂载时的 localPosition）。</summary>
  public class MedicalDroneHover : MonoBehaviour
  {
    public float SpinDegPerSec = 60f;
    public float BobAmplitude = 0.08f;
    public float BobSpeed = 1.6f;

    private Vector3 _base;
    private float _t;

    private void Start() => _base = transform.localPosition;

    private void Update()
    {
      _t += Time.deltaTime;
      transform.Rotate(0f, SpinDegPerSec * Time.deltaTime, 0f, Space.Self);
      var p = _base;
      p.y += Mathf.Sin(_t * BobSpeed) * BobAmplitude;
      transform.localPosition = p;
    }
  }

  /// <summary>增益加上/移除的事件订阅（常驻，Main.Load 里订阅一次）。</summary>
  internal sealed class MedicalDroneFxWatcher : IUnitBuffHandler
  {
    public void HandleBuffDidAdded(Buff buff) => MedicalDroneFx.OnBuffAdded(buff);
    public void HandleBuffDidRemoved(Buff buff) => MedicalDroneFx.OnBuffRemoved(buff);
  }
}
