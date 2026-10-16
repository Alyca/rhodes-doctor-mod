#!/usr/bin/env bash
# 部署：把新编的 RhodesDoctor.dll / .pdb 覆盖到游戏 Mods\RhodesDoctor\（先备份旧 DLL）。
#
# 用法：bash tools/deploy.sh ["游戏安装目录"]
#
# ⚠ 游戏运行时 DLL 被占用 → **先关掉游戏**再部署；覆盖后**必须重启游戏**才会加载新版本。
# ⚠ 只 `dotnet build` 不跑本脚本 = 游戏里跑的还是旧 DLL（本 mod 为此浪费过好几轮：改了代码、
#    "编译通过"、进游戏却"一点都看不见"，因为根本没部署）。改完代码请**编译 + 本脚本**一起做。
set -e

GAME="${1:-/d/steam/steamapps/common/Pathfinder Second Adventure}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC="$ROOT/bin/Debug/net48"
DST="$GAME/Mods/RhodesDoctor"
TS="$(date +%Y%m%d-%H%M%S)"

if [ ! -f "$SRC/RhodesDoctor.dll" ]; then
  echo "✗ 找不到 $SRC/RhodesDoctor.dll —— 先 dotnet build。" >&2
  exit 1
fi
if [ ! -d "$DST" ]; then
  echo "✗ 找不到 mod 安装目录 $DST（检查游戏路径）。" >&2
  exit 1
fi

if [ -f "$DST/RhodesDoctor.dll" ]; then
  cp "$DST/RhodesDoctor.dll" "$DST/RhodesDoctor.dll.bak-$TS"
  echo "已备份旧 DLL → RhodesDoctor.dll.bak-$TS"
fi

cp "$SRC/RhodesDoctor.dll" "$DST/RhodesDoctor.dll"
[ -f "$SRC/RhodesDoctor.pdb" ] && cp "$SRC/RhodesDoctor.pdb" "$DST/RhodesDoctor.pdb"

# 同步随 mod 发布的运行时文件（有才拷，避免覆盖成空）。
for f in Info.json LocalizedStrings.json; do
  [ -f "$SRC/$f" ] && cp "$SRC/$f" "$DST/$f"
done

echo "✓ 已部署到 $DST"
echo "⚠ 请重启游戏后生效（DLL 只在启动时加载）。"
