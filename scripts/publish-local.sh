#!/usr/bin/env bash
# Publish self-contained PATH tools + local NuGet packs into a folder
# (default: ~/Downloads/dotnes). Decompiled projects restore from this
# folder when the decompiler runs from it (FindLocalPackageSource).
# Usage: scripts/publish-local.sh [outdir] [rid]
# Example: scripts/publish-local.sh
#          scripts/publish-local.sh ~/Downloads/dotnes linux-x64
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${1:-$HOME/Downloads/dotnes}"
RID="${2:-linux-x64}"
REPO_PACKS="$ROOT/bin/Debug"

PUBLISH_FLAGS=(
  -c Release
  --self-contained true
  -p:GeneratePackageOnBuild=false
  --nologo
)

mkdir -p "$OUT" "$REPO_PACKS"

echo "==> compiler → $OUT ($RID)"
dotnet publish "$ROOT/src/dotnes.compiler/dotnes.compiler.csproj" \
  "${PUBLISH_FLAGS[@]}" -r "$RID" -o "$OUT"

echo "==> decompiler → $OUT"
dotnet publish "$ROOT/src/dotnes.decompiler/dotnes.decompiler.csproj" \
  "${PUBLISH_FLAGS[@]}" -r "$RID" -o "$OUT"

echo "==> lsp → $OUT"
dotnet publish "$ROOT/src/dotnes.lsp/dotnes.lsp.csproj" \
  "${PUBLISH_FLAGS[@]}" -r "$RID" -o "$OUT"

echo "==> pack NuGet (neslib enums for decompiled projects)"
# Pack into OUT so PATH installs can act as a package source; mirror to bin/Debug
# for decompiles run from a repo build.
dotnet pack "$ROOT/src/dotnes/dotnes.csproj" -c Release --nologo -o "$OUT"
dotnet pack "$ROOT/src/dotnes.mesen/dotnes.mesen.csproj" -c Release --nologo -o "$OUT"
cp -f "$OUT"/dotnes.*.nupkg "$OUT"/dotnes.mesen.*.nupkg "$REPO_PACKS/" 2>/dev/null || true

# Same PackageVersion as nuget.org — drop the global cache so restore picks up the new local pack.
VER="$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' "$ROOT/Directory.Build.props" | head -1)"
VERSION="${VER}-beta"
cache="$HOME/.nuget/packages/dotnes/$VERSION"
if [[ -n "$VER" && -d "$cache" ]]; then
  echo "==> clear NuGet cache $cache"
  rm -rf "$cache"
fi

echo "==> verify"
"$OUT/dotnes" --help | head -8
"$OUT/dotnes-decompiler" --help | head -6
ls -la "$OUT/dotnes" "$OUT/dotnes-decompiler" "$OUT/dotnes-lsp"
ls -la "$OUT"/dotnes.*.nupkg "$OUT"/dotnes.mesen.*.nupkg 2>/dev/null | head -10
echo "Done. Tools + packs in $OUT"
