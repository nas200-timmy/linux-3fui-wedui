#!/usr/bin/env bash
# 上游同步脚本：把 FFmpegFreeUI 仓库中的纯逻辑文件同步到 linux-3fui。
#
# 用法：tools/sync-upstream.sh /path/to/FFmpegFreeUI [--dry-run]
#
# 基线：上游 v6.2.26。同步规则：
#   - 预设系统（预设数据/命令行生成/存储/滤镜排序/内置预设）：原样复制到 src/3fui-core/预设系统/
#   - 编码进度/编码器数据库/原子文件写入：原样复制到 src/3fui-core/
#   - 引擎文件（编码任务/编码队列/端口监听/启动参数响应）：复制到 src/3fui-core/引擎/ 后
#     自动重放 linux-3fui 的适配补丁（patches/ 目录），若补丁无法应用则中止并提示人工处理
#   - 随后必须运行回归测试确认行为一致
set -euo pipefail

UPSTREAM="${1:?用法: $0 /path/to/FFmpegFreeUI [--dry-run]}"
DRY="${2:-}"
SRC="$UPSTREAM/FFmpegFreeUI"
DST="$(cd "$(dirname "$0")/.." && pwd)/src/3fui-core"
PATCHES="$(cd "$(dirname "$0")/.." && pwd)/patches"

[ -d "$SRC" ] || { echo "错误：$SRC 不存在"; exit 1; }

copy() {
  local rel="$1"
  if [ "$DRY" = "--dry-run" ]; then
    echo "DRY: $rel"
    return
  fi
  mkdir -p "$(dirname "$DST/$rel")"
  cp "$SRC/$rel" "$DST/$rel"
  echo "同步: $rel"
}

# ── 原样复制（纯逻辑，不依赖 UI）──
copy "功能/预设系统/预设数据_v6.vb"
copy "功能/预设系统/预设命令行核心_v6.vb"
copy "功能/预设系统/预设命令行滤镜_v6.vb"
copy "功能/预设系统/预设命令行滤镜图_v6.vb"
copy "功能/预设系统/预设命令行编码_v6.vb"
copy "功能/预设系统/预设命令行输入输出_v6.vb"
copy "功能/预设系统/预设命令行工具_v6.vb"
copy "功能/预设系统/预设存储_v6.vb"
copy "功能/预设系统/预设滤镜排序_v6.vb"
copy "功能/预设系统/开发者内置预设_v6.vb"
copy "功能/编码进度_v6.vb"
copy "功能/视频编码器数据库_v6.vb"
copy "功能/音频编码器数据库_v6.vb"
copy "功能/原子文件写入_v6.vb"

# ── 引擎文件：复制到 引擎/ 后应用适配补丁 ──
for engine in 编码任务_v6.vb 编码队列_v6.vb 端口监听_v6.vb 启动参数响应_v6.vb; do
  if [ "$DRY" = "--dry-run" ]; then
    echo "DRY: 引擎/$engine"
  else
    cp "$SRC/功能/$engine" "$DST/引擎/$engine"
    echo "同步: 引擎/$engine"
  fi
  patch_file="$PATCHES/$engine.patch"
  if [ "$DRY" != "--dry-run" ] && [ -f "$patch_file" ]; then
    if patch -p1 -d "$DST" -N -r /dev/null < "$patch_file"; then
      echo "补丁应用: $engine"
    else
      echo "!! 补丁失败: $engine（上游代码结构可能已变化，需人工适配）" >&2
      exit 1
    fi
  fi
done

# 移除上游 UI 专属 import（LakeUI）
if [ "$DRY" != "--dry-run" ]; then
  sed -i '/^Imports LakeUI$/d' "$DST/预设系统/预设滤镜排序_v6.vb"
fi

echo
echo "同步完成。接下来必须执行："
echo "  1. cd tests/3fui-core.Tests && dotnet run -- --ffmpeg /usr/bin/ffmpeg"
echo "  2. 对照上游预设系统/预设面板映射_v6.vb 中引用的新成员，更新 预设管理_v6_适配成员.vb"
echo "  3. 检查 patches/ 补丁是否仍适用（上游结构变化时更新补丁）"
