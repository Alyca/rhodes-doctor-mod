// 逐事件诊断日志（默认关闭）。
//
// 用法：把"开发期排查用"的 Logger.Log 换成 Dbg.Log。
// 关键：这里用 [Conditional] 而不是"运行时 if 判断" —— 带该特性的调用在**未定义符号时会被编译器
// 整体删除**，连参数字符串的拼接 / 插值都不会执行 ⇒ 发布版**零分配、零调用**。
// （若写成 `if (Dbg.On) Log($"...")`，插值字符串仍会先分配，等于没省。）
//
// 需要排查时，编译时加上符号即可把全部诊断日志一次性恢复：
//   dotnet build -p:DefineConstants=RD_VERBOSE -p:WrathPath="D:/steam/steamapps/common/Pathfinder Second Adventure"
using System.Diagnostics;

namespace RhodesDoctor
{
  internal static class Dbg
  {
    [Conditional("RD_VERBOSE")]
    internal static void Log(string message) => Main.Mod.Logger.Log(message);
  }
}
