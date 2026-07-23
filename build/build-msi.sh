#!/usr/bin/env bash
# Builds the WinAgentNotification MSI on Linux.
#
# Pipeline: dotnet publish (win-x64, self-contained)
#           -> wixl-heat  (harvest the publish folder into a WiX fragment)
#           -> wixl       (msitools' Linux-native MSI builder)
#
# Requirements: Microsoft-built .NET 10 SDK, `apt-get install msitools wixl`.
#
# Usage: build/build-msi.sh VERSION [OUTDIR]
#   VERSION  numeric x.y.z (MSI requirement; strip any leading 'v' first)
#   OUTDIR   output directory, default: dist
set -euo pipefail

VERSION=${1:?usage: build-msi.sh VERSION [OUTDIR]}
OUTDIR=${2:-dist}

ROOT=$(cd "$(dirname "$0")/.." && pwd)
OUT="$ROOT/$OUTDIR"
PUBLISH="$OUT/publish"
MSI="$OUT/WinAgentNotification-$VERSION.msi"

dotnet publish "$ROOT/src/WinAgentNotification.App" -c Release -r win-x64 \
  --self-contained true -p:Version="$VERSION" -o "$PUBLISH"

find "$PUBLISH" -type f | wixl-heat \
  -p "$PUBLISH/" \
  --component-group CG.App \
  --directory-ref INSTALLFOLDER \
  --var var.PublishDir \
  --win64 > "$OUT/harvest.wxs"

wixl -a x64 \
  -D ProductVersion="$VERSION" \
  -D PublishDir="$PUBLISH" \
  -D Win64=yes \
  "$ROOT/installer/Product.wxs" "$OUT/harvest.wxs" \
  -o "$MSI"

echo "Built: $MSI"
