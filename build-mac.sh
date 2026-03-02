#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT="$SCRIPT_DIR/RadioSender/RadioSender.csproj"
APP_NAME="RadioSender"
RUNTIME="osx-arm64"
CONFIG="Release"
OUTPUT_DIR="$SCRIPT_DIR/out/mac"
APP_BUNDLE="$OUTPUT_DIR/$APP_NAME.app"
PUBLISH_DIR="$OUTPUT_DIR/publish"

echo "=== Building $APP_NAME for macOS ($RUNTIME) ==="

# Clean previous output
rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

# Publish self-contained single file
dotnet publish "$PROJECT" \
  -c "$CONFIG" \
  -r "$RUNTIME" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o "$PUBLISH_DIR"

echo "=== Creating .app bundle ==="

# Create .app bundle structure
mkdir -p "$APP_BUNDLE/Contents/MacOS"
mkdir -p "$APP_BUNDLE/Contents/Resources"

# Copy published files into MacOS folder
cp -R "$PUBLISH_DIR/"* "$APP_BUNDLE/Contents/MacOS/"

# Copy icon
cp "$SCRIPT_DIR/RadioSender/wwwroot/RadioSender.icns" "$APP_BUNDLE/Contents/Resources/AppIcon.icns"

# Create Info.plist
cat > "$APP_BUNDLE/Contents/Info.plist" << 'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>RadioSender</string>
    <key>CFBundleDisplayName</key>
    <string>RadioSender</string>
    <key>CFBundleIdentifier</key>
    <string>com.radiosender.app</string>
    <key>CFBundleVersion</key>
    <string>1.0</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleExecutable</key>
    <string>RadioSender</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSSupportsAutomaticGraphicsSwitching</key>
    <true/>
</dict>
</plist>
PLIST

# Make executable
chmod +x "$APP_BUNDLE/Contents/MacOS/RadioSender"

# Clean up intermediate publish dir
rm -rf "$PUBLISH_DIR"

echo ""
echo "=== Done ==="
echo "App bundle: $APP_BUNDLE"
echo "Size: $(du -sh "$APP_BUNDLE" | cut -f1)"
echo ""
echo "To run: open $APP_BUNDLE"
echo "To create DMG: hdiutil create -volname RadioSender -srcfolder \"$APP_BUNDLE\" -ov -format UDZO \"$OUTPUT_DIR/RadioSender.dmg\""
