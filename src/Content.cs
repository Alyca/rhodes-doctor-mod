using System;
using System.IO;
using BlueprintCore.Utils;

namespace RhodesDoctor
{
  /// <summary>装配入口：蓝图缓存初始化后由 Main 调用。</summary>
  internal static class Content
  {
    internal static void SetupAll()
    {
      LoadLocalizationPack();

      // 各模块独立 try/catch：一个模块失败不拖垮其余（蓝图互相只按 GUID 引用）。
      Run("召唤物与镜像", SummonsSetup.Create);
      Run("测试木桩", TestSetup.Create);
      Run("指令法术", SpellsSetup.Create);
      Run("法术书", SpellbookSetup.Create);
      Run("巴别塔的恶灵", SpecterSetup.Create);
      Run("变体本体", ArchetypeSetup.Create);
    }

    private static void Run(string name, Action module)
    {
      try
      {
        module();
        Main.Mod.Logger.Log($"[RhodesDoctor] {name}注入完成。");
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error($"[RhodesDoctor] {name}注入失败：\n{e}");
      }
    }

    /// <summary>
    /// 加载 mod 目录下的本地化包（LocalizedStrings.json）。
    /// 用 WW-Blueprint-Core 自带的 LocalizationTool.LoadLocalizationPack，参数是 JSON 文件完整路径。
    /// </summary>
    private static void LoadLocalizationPack()
    {
      try
      {
        LocalizationTool.LoadLocalizationPack(Path.Combine(Main.ModPath, "LocalizedStrings.json"));
      }
      catch (Exception e)
      {
        Main.Mod.Logger.Error($"[RhodesDoctor] 本地化包加载失败：\n{e}");
      }
    }
  }
}
