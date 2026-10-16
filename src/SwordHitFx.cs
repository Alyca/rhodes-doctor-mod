// 剑雨的"受击特效"：被剑雨打中的敌人身上炸一小下金光。
//
// 为什么自己做（原版受击反馈不可靠）：
//   · 剑雨原先**没有任何逐目标命中特效**，敌人身上看到的是原版那套受击反馈（血液/火花）。
//     用户反馈"受到伤害但有时不触发特效，而且很随机"——原版那套反馈受伤害类型、单位血型、
//     是否 AoE、单位状态等影响，条件不透明，没法保证每次都出。
//   · 现在把特效挂进**和伤害同一个动作分支**（SavingThrow 的 failed / succeed 两支），
//     伤害走到哪一支就跟着放哪一支的命中特效 → 只要伤害结算了，特效就一定放。
//
// 材质直接复用 SwordStreakFx 那份**已被实机验证能渲染**的金色材质（克隆自原版 FX），
// 不碰 Shader.Find，也不借粒子素材（那两类都翻过车，见 SwordStreakFx 文件头与 dev-notes §0）。
//
// 形状：一张面向相机的柔光面片 + 一道斜劈的细长光条，0.32 s 内胀开并淡出。
using System;
using UnityEngine;

namespace RhodesDoctor
{
  internal static class SwordHitFx
  {
    private static Mesh _quad;

    /// <summary>
    /// 在世界坐标 <paramref name="point"/> 处放一发命中金光。
    /// <paramref name="layer"/> 跟着被打中的单位走（新建 GameObject 默认在 Default 层）。
    /// <paramref name="delay"/> 秒后才亮起 —— **要等于剑的下落时间**（剑雨的 `FallSeconds`）：
    /// 伤害是施法瞬间结算的，但剑要 0.4 s 后才砸到人身上；金光若在伤害那一刻就炸，
    /// 就成了"光先闪、剑还在天上"（上一版"伤害数字先跳、剑没落地"是同一个毛病）。
    /// </summary>
    internal static void Spawn(Vector3 point, Color color, float size, int layer, float delay)
    {
      try
      {
        var mat = SwordStreakFx.StreakMaterial(color);
        if (mat == null) return;

        var root = new GameObject("RhodesDoctorHitFlash");
        root.layer = layer;
        root.transform.position = point;

        // 每次命中都要独立淡出 → 克隆一份材质。数量等于本次被打中的敌人数（通常 1~6），
        // 会在组件销毁时一起销毁，不会攒着泄漏。
        var inst = new Material(mat) { name = "RhodesDoctorHitFlashMat" };

        var glow = MakeQuad(root.transform, "Glow", inst);
        glow.transform.localScale = Vector3.one * size;

        var slash = MakeQuad(root.transform, "Slash", inst);
        slash.transform.localScale = new Vector3(size * 1.7f, size * 0.11f, 1f);
        slash.transform.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(-55f, 55f));

        var fx = root.AddComponent<HitFlashFx>();
        fx.Mat = inst;
        fx.Glow = glow.transform;
        fx.Slash = slash.transform;
        fx.Size = size;
        fx.Delay = Mathf.Max(0f, delay);
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error("[RhodesDoctor] 剑雨命中特效生成失败：\n" + e);
      }
    }

    private static GameObject MakeQuad(Transform parent, string name, Material mat)
    {
      var go = new GameObject(name);
      go.layer = parent.gameObject.layer;
      go.transform.SetParent(parent, worldPositionStays: false);
      go.transform.localPosition = Vector3.zero;
      go.transform.localRotation = Quaternion.identity;
      go.AddComponent<MeshFilter>().sharedMesh = Quad();
      var mr = go.AddComponent<MeshRenderer>();
      mr.sharedMaterial = mat;
      mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
      mr.receiveShadows = false;
      return go;
    }

    /// <summary>1x1 的面片（XY 平面，中心在原点）。生成一次共用。</summary>
    private static Mesh Quad()
    {
      if (_quad != null) return _quad;
      var m = new Mesh { name = "RhodesDoctorQuad" };
      m.vertices = new[]
      {
        new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
        new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
      };
      m.uv = new[]
      {
        new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
      };
      m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
      m.RecalculateBounds();
      _quad = m;
      return m;
    }
  }

  /// <summary>命中金光的动画：面向相机、胀开、淡出，0.32 s 后自删（连带材质）。</summary>
  public class HitFlashFx : MonoBehaviour
  {
    public Material Mat;
    public Transform Glow;
    public Transform Slash;
    public float Size = 1.2f;
    public float Life = 0.32f;

    /// <summary>亮起前的等待（秒）：= 剑的下落时间，让金光和剑落地同步。等待期间渲染器整体关闭。</summary>
    public float Delay;

    private float _t;
    private bool _shown;
    private Renderer[] _renderers;

    private void Awake()
    {
      // 等待期间**关掉渲染器**，而不是只把 alpha 调 0：
      // 万一那份材质是 opaque 混合（alpha 不起作用），alpha=0 也会看见一个白方块。
      _renderers = GetComponentsInChildren<Renderer>(true);
      SetRenderers(false);
    }

    private void Update()
    {
      _t += Time.deltaTime;

      if (!_shown)
      {
        if (_t < Delay) return;
        _shown = true;
        SetRenderers(true);
        _t -= Delay;      // 动画从 0 开始算
      }

      float k = Mathf.Clamp01(_t / Life);

      var cam = Camera.main;
      if (cam != null) transform.rotation = cam.transform.rotation;   // billboard；子物件的 Z 旋转仍保留 → 斜劈

      if (Glow != null)
      {
        float s = Size * Mathf.Lerp(0.35f, 1.15f, Mathf.Sqrt(k));
        Glow.localScale = new Vector3(s, s, s);
      }
      if (Slash != null)
      {
        float s = Size * 1.7f * Mathf.Lerp(0.45f, 1.25f, k);
        Slash.localScale = new Vector3(s, Size * 0.11f, 1f);
      }

      SetAlpha(1f - k);
      if (k >= 1f) Destroy(gameObject);
    }

    private void SetRenderers(bool on)
    {
      if (_renderers == null) return;
      for (int i = 0; i < _renderers.Length; i++)
        if (_renderers[i] != null) _renderers[i].enabled = on;
    }

    private void SetAlpha(float a)
    {
      if (Mat == null) return;
      if (Mat.HasProperty("_Color")) { var c = Mat.color; c.a = a; Mat.color = c; }
      if (Mat.HasProperty("_TintColor")) { var c = Mat.GetColor("_TintColor"); c.a = a; Mat.SetColor("_TintColor", c); }
      if (Mat.HasProperty("_BaseColor")) { var c = Mat.GetColor("_BaseColor"); c.a = a; Mat.SetColor("_BaseColor", c); }
    }

    private void OnDestroy()
    {
      if (Mat != null) Destroy(Mat);
    }
  }
}
