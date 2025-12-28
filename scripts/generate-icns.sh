#!/bin/bash
# 在 macOS 上运行此脚本生成 AppIcon.icns

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ASSETS_DIR="$PROJECT_ROOT/src/Watchdog.App/Assets"
ICONSET_DIR="$ASSETS_DIR/AppIcon.iconset"

echo "创建 iconset 目录..."
mkdir -p "$ICONSET_DIR"

echo "从 .ico 生成不同尺寸的 PNG..."
cd "$ICONSET_DIR"

# 使用 sips 生成所有必需的尺寸
sips -s format png "$ASSETS_DIR/avalonia-logo.ico" --out icon_16x16.png -z 16 16
sips -s format png "$ASSETS_DIR/avalonia-logo.ico" --out icon_32x32.png -z 32 32
sips -s format png "$ASSETS_DIR/avalonia-logo.ico" --out icon_32x32@2x.png -z 64 64
sips -s format png "$ASSETS_DIR/avalonia-logo.ico" --out icon_128x128.png -z 128 128
sips -s format png "$ASSETS_DIR/avalonia-logo.ico" --out icon_128x128@2x.png -z 256 256
sips -s format png "$ASSETS_DIR/avalonia-logo.ico" --out icon_256x256.png -z 256 256
sips -s format png "$ASSETS_DIR/avalonia-logo.ico" --out icon_256x256@2x.png -z 512 512
sips -s format png "$ASSETS_DIR/avalonia-logo.ico" --out icon_512x512.png -z 512 512
sips -s format png "$ASSETS_DIR/avalonia-logo.ico" --out icon_512x512@2x.png -z 1024 1024

echo "生成 .icns 文件..."
iconutil -c icns "$ICONSET_DIR" -o "$ASSETS_DIR/AppIcon.icns"

echo "清理临时文件..."
rm -rf "$ICONSET_DIR"

echo "✅ 图标已生成：$ASSETS_DIR/AppIcon.icns"
