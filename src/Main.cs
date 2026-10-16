using System;
using HarmonyLib;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.PubSubSystem;
using UnityModManagerNet;

namespace RhodesDoctor
{
  /// <summary>
  /// UMM 入口。安装方式：编译出 RhodesDoctor.dll，与 Info.json 一起放入 Mods\RhodesDoctor\。
  /// </summary>
  public static class Main
  {
    public static UnityModManager.ModEntry Mod = null!;
    /// <summary>mod 安装目录（LocalizedStrings.json 等运行时文件在这里）。</summary>
    public static string ModPath = "";
    private static SmokeFxWatcher _smokeWatcher;
    private static BeaconFxWatcher _beaconWatcher;
    private static HasteTrailFxWatcher _hasteTrailWatcher;
    private static TintedAuraFxWatcher _tintedAuraWatcher;
    private static MedicalDroneFxWatcher _medicalDroneWatcher;
    private static AreaGroundFxWatcher _areaGroundWatcher;
    private static TacticalProjectionFxWatcher _tacticalProjectionWatcher;
    private static MarkFxWatcher _markFxWatcher;

    /// <summary>P2-C：加载重入守卫——UMM 重载 / 热重入时避免事件订阅与补丁被重复执行。</summary>
    private static bool _loaded;

    public static bool Load(UnityModManager.ModEntry modEntry)
    {
      Mod = modEntry;
      ModPath = modEntry.Path;
      modEntry.OnGUI = TestPanel.OnGUI;
      if (_loaded)
      {
        modEntry.Logger.Log("[RhodesDoctor] 已加载过，跳过重复初始化（避免双重订阅 / 重复打补丁）。");
        return true;
      }
      _loaded = true;
      try
      {
        // P1：每个 Watcher 独立 try/catch —— 原先 6 个订阅在一个 try 里，第 3 个失败会让
        // 后 3 个永久不订阅（_loaded 已置 true，下次 Load 直接跳过整块）。拆开后单个失败只影响自身。
        SubscribeOne(ref _smokeWatcher, "烟雾");
        SubscribeOne(ref _beaconWatcher, "信标");
        SubscribeOne(ref _hasteTrailWatcher, "拖尾");
        SubscribeOne(ref _tintedAuraWatcher, "时之锁光环");
        SubscribeOne(ref _medicalDroneWatcher, "医疗无人机");
        SubscribeOne(ref _areaGroundWatcher, "地面区域");
        SubscribeOne(ref _tacticalProjectionWatcher, "战术投影");
        SubscribeOne(ref _markFxWatcher, "标记特效（火力侦察 / 终末狙击）");
      }
      catch (Exception e)
      {
        modEntry.Logger.Error("[RhodesDoctor] 烟雾/信标/拖尾/时之锁光环/医疗无人机/地面区域/战术投影/标记特效事件订阅失败：\n" + e);
      }
      // P2-C：PatchAll 包 try/catch —— 补丁失败不再上抛导致 UMM 报"加载失败"并留下半加载状态。
      try
      {
        new Harmony(modEntry.Info.Id).PatchAll(typeof(Main).Assembly);
      }
      catch (Exception e)
      {
        modEntry.Logger.Error("[RhodesDoctor] Harmony 补丁失败（部分功能可能不可用）：\n" + e);
        return false;
      }
      modEntry.Logger.Log("[RhodesDoctor] 已加载。");
      return true;
    }

    private static void SubscribeOne<T>(ref T field, string name) where T : class, new()
    {
      try
      {
        field = new T();
        EventBus.Subscribe(field);
      }
      catch (Exception e)
      {
        Mod.Logger.Error($"[RhodesDoctor] {name} Watcher 订阅失败（功能不受影响）：\n" + e);
      }
    }
  }

  /// <summary>
  /// 蓝图缓存初始化完成后注入我们的蓝图（BlueprintCore 官方推荐时机）。
  /// 此时游戏本体与其他 mod 的蓝图都可以读取。
  /// </summary>
  [HarmonyPatch(typeof(BlueprintsCache), nameof(BlueprintsCache.Init))]
  internal static class BlueprintsCacheInitPatch
  {
    private static bool _done;

    private static void Postfix()
    {
      if (_done) return;
      _done = true;
      try
      {
        Content.SetupAll();
        Main.Mod.Logger.Log("[RhodesDoctor] 蓝图注入完成。");
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error($"[RhodesDoctor] 蓝图注入失败:\n{e}");
      }
    }
  }
}
