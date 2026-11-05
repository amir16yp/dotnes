#!/usr/bin/env bash
# Pack self-contained release zips for one or more RIDs.
# Usage: scripts/pack-release.sh [version] [rid ...]
# Example: scripts/pack-release.sh 0.3.3 linux-x64
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="${1:-}"
if [[ $# -gt 0 ]]; then
  shift
fi

if [[ -z "$VERSION" ]]; then
  VERSION="$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' "$ROOT/Directory.Build.props" | head -1)"
fi

if [[ $# -eq 0 ]]; then
  set -- win-x64 linux-x64 osx-x64 osx-arm64
fi

OUT="$ROOT/artifacts/release"
EXT_DIR="$ROOT/extensions/dotnes-vscode"
mkdir -p "$OUT"

VSIX="$OUT/dotnes-vscode-${VERSION}.vsix"
if [[ ! -f "$VSIX" ]]; then
  echo "==> Building VSIX"
  pushd "$EXT_DIR" >/dev/null
  if [[ ! -d node_modules ]]; then
    npm ci || npm install
  fi
  npm run package:js
  npx vsce package --no-dependencies --out "$VSIX"
  popd >/dev/null
else
  echo "==> Reusing existing VSIX at $VSIX"
fi

README_TXT="$OUT/README.txt"
cat > "$README_TXT" <<EOF
dotNES ${VERSION} — PATH tools + VS Code extension
=================================================

1. Extract this archive to a permanent folder, e.g.:
     ~/tools/dotnes   or   C:\\tools\\dotnes

2. Add that folder to your PATH.

3. Verify:
     dotnes --help
     dotnes-decompiler --help
     dotnes-lsp   (used by the VS Code extension; stdio LSP)

4. Install the VS Code extension from this zip:
     code --install-extension dotnes-vscode-${VERSION}.vsix

5. Open a dotnes project (or Import NES ROM from the Command Palette).
   C# editing uses the C# / C# Dev Kit extension; CHR .s files can be opened
   with "dotNES CHR Tile Editor" via Open With…

Building games still requires the .NET SDK (net10). These binaries are
self-contained for the CLI, decompiler, and language server only.
EOF

# Multi-file self-contained (not PublishSingleFile): the compiler reads
# neslib.dll via Assembly.Location for NESLib C# body loading.
PUBLISH_FLAGS=(
  -c Release
  --self-contained true
  -p:GeneratePackageOnBuild=false
  --nologo
)

for RID in "$@"; do
  echo "==> Publishing $RID"
  STAGE="$OUT/stage-$RID"
  rm -rf "$STAGE"
  mkdir -p "$STAGE"

  dotnet publish "$ROOT/src/dotnes.compiler/dotnes.compiler.csproj" \
    "${PUBLISH_FLAGS[@]}" -r "$RID" -o "$STAGE"
  dotnet publish "$ROOT/src/dotnes.decompiler/dotnes.decompiler.csproj" \
    "${PUBLISH_FLAGS[@]}" -r "$RID" -o "$STAGE"
  dotnet publish "$ROOT/src/dotnes.lsp/dotnes.lsp.csproj" \
    "${PUBLISH_FLAGS[@]}" -r "$RID" -o "$STAGE"

  cp "$VSIX" "$STAGE/"
  cp "$README_TXT" "$STAGE/README.txt"

  ZIP="$OUT/dotnes-${VERSION}-${RID}.zip"
  rm -f "$ZIP"
  if command -v zip >/dev/null 2>&1; then
    (cd "$STAGE" && zip -qr "$ZIP" .)
  else
    python3 - "$STAGE" "$ZIP" <<'PY'
import sys, shutil, os
stage, zip_path = sys.argv[1], sys.argv[2]
base, _ = os.path.splitext(zip_path)
shutil.make_archive(base, "zip", stage)
PY
  fi
  echo "    -> $ZIP"
done

echo "Done. Artifacts in $OUT"
