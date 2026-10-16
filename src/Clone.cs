// Blueprint 实例是普通 C# 对象，不继承 UnityEngine.Object，不能使用 Unity 的 Instantiate。
// 通过游戏存档使用的 OwlcatJsonConvert 配置序列化并反序列化，生成蓝图深拷贝。
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Persistence.JsonUtility;
using Kingmaker.Utility;

namespace RhodesDoctor
{
  internal static class Clone
  {
    /// <summary>
    /// 深拷贝蓝图并注册到 Blueprint cache。
    /// 当前没有调用方；保留为后续克隆蓝图时可复用的工具方法。
    /// 调用方应在需要时为副本设置新的 AssetGuid，避免覆盖原蓝图缓存项。
    /// </summary>
    internal static T Of<T>(T source) where T : SimpleBlueprint
    {
      if (source == null) return null;

      var json = OwlcatJsonConvert.SerializeObject(source);
      var clone = OwlcatJsonConvert.DeserializeObject<T>(json);
      ResourcesLibrary.BlueprintsCache.AddCachedBlueprint(clone.AssetGuid, clone);
      return clone;
    }
  }
}
