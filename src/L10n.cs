namespace RhodesDoctor
{
  /// <summary>
  /// 本地化：字符串本体在 LocalizedStrings.json（游戏原生本地化包格式，
  /// 由 Content.LoadLocalizationPack 在游戏启动时加载）；这里只返回键。
  /// 注意：LocalizedString 没有可按键解析的 m_Key 字段，所以 S 直接返回 string 键；
  /// BlueprintCore 的 SetDisplayName / SetDescription 等均接受 string 键。
  /// zh/en 参数只用于生成 LocalizedStrings.json 的脚本对照，运行时不使用。
  /// </summary>
  internal static class L10n
  {
    internal static string S(string key, string zh, string en) => "rhodesdoctor." + key;
  }
}
