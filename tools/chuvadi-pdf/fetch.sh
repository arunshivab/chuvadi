#!/usr/bin/env bash
# Fetches the Chuvadi.Pdf packages a staged project needs from the chuvadi-pdf GitHub release (D-236, D-241) into one
# folder, checks every file against the hashes pinned in this repository, and writes a nuget.config that restores
# Chuvadi.* from that folder only. The staged projects (staging/) cannot join the solution yet, so the release workflow
# builds them with:  -p:RestoreConfigFile=<folder>/nuget.ci.config
#
# Usage:  bash tools/chuvadi-pdf/fetch.sh <chuvadi-pdf version> <folder>
#
# Inputs, all pinned in this repository:
#   tools/chuvadi-pdf/hashes/<version>.sha256   the 34 packages of that chuvadi-pdf release (sha256sum -c format)
#   tools/chuvadi-pdf/hashes/<version>.fonts    which of our own font packages that chuvadi-pdf release needs (one name per
#                                               line, for example "fonts-1.1.0"). Optional: without the file the release is
#                                               taken to need "fonts-1.0.0", as every chuvadi-pdf release up to 3.32.1 does.
#   tools/chuvadi-pdf/hashes/<name>.sha256      one font package, for example fonts-1.0.0 (Chuvadi.Fonts 1.0.0),
#                                               fonts-1.1.0 or fonts-liberation-1.0.0. The chuvadi-pdf release does not carry
#                                               them; they are downloaded from our own release "<name with -v before the
#                                               version>" (fonts-1.1.0 comes from fonts-v1.1.0, fonts-liberation-1.0.0 from
#                                               fonts-liberation-v1.0.0).
# A hash that does not match, or a file that is missing, stops the release. Nothing falls back to nuget.org for Chuvadi.*.
# To use a new chuvadi-pdf release: add hashes/<version>.sha256 (the release's SHA256SUMS.txt, after checking it) in the
# same pull request that changes ChuvadiPdfVersion.
set -euo pipefail

VERSION="${1:?usage: fetch.sh <chuvadi-pdf version> <folder>}"
DEST="${2:?usage: fetch.sh <chuvadi-pdf version> <folder>}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PDF_URL="https://github.com/arunshivab/chuvadi-pdf/releases/download/v${VERSION}"
MONO_URL="https://github.com/arunshivab/chuvadi/releases/download"

PDF_HASHES="$HERE/hashes/${VERSION}.sha256"
[ -f "$PDF_HASHES" ] || { echo "::error::No pinned hashes for chuvadi-pdf ${VERSION}: add tools/chuvadi-pdf/hashes/${VERSION}.sha256"; exit 1; }

# Which font packages this chuvadi-pdf release needs (default: Chuvadi.Fonts 1.0.0).
FONT_NAMES=(fonts-1.0.0)
if [ -f "$HERE/hashes/${VERSION}.fonts" ]; then
  FONT_NAMES=()
  while read -r line; do [ -n "$line" ] && FONT_NAMES+=("$line"); done < <(tr -d '\r' < "$HERE/hashes/${VERSION}.fonts")
fi
FONT_HASHES=()
for name in "${FONT_NAMES[@]}"; do
  [ -f "$HERE/hashes/${name}.sha256" ] || { echo "::error::Missing tools/chuvadi-pdf/hashes/${name}.sha256"; exit 1; }
  FONT_HASHES+=("$HERE/hashes/${name}.sha256")
done

mkdir -p "$DEST"
DEST="$(cd "$DEST" && pwd)"

# Hash files are read with any carriage returns removed (a Windows checkout may add them).
fetch_list() { # <hash file> <base url>
  while read -r hash name; do
    [ -n "${name:-}" ] || continue
    curl --fail --silent --show-error --location --retry 3 --retry-delay 2 --max-time 300 -o "$DEST/$name" "$2/$name"
  done < <(tr -d '\r' < "$1")
}
verify_list() { # <hash file>
  (cd "$DEST" && tr -d '\r' < "$1" | sha256sum --check --strict --quiet -)
}

echo "Fetching chuvadi-pdf ${VERSION} packages"
fetch_list "$PDF_HASHES" "$PDF_URL"
for name in "${FONT_NAMES[@]}"; do
  tag="${name%-*}-v${name##*-}"   # fonts-1.1.0 -> fonts-v1.1.0, fonts-liberation-1.0.0 -> fonts-liberation-v1.0.0
  echo "Fetching ${name} from our release ${tag}"
  fetch_list "$HERE/hashes/${name}.sha256" "$MONO_URL/$tag"
done

echo "Checking every file against the pinned hashes"
verify_list "$PDF_HASHES"
for h in "${FONT_HASHES[@]}"; do verify_list "$h"; done
echo "All $(cat "$PDF_HASHES" "${FONT_HASHES[@]}" | tr -d '\r' | grep -c .) packages match."

# Chuvadi.* comes from this folder only; everything else (xunit and the other test tools) from nuget.org.
cat > "$DEST/nuget.ci.config" <<CFG
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="chuvadi-pdf" value="$DEST" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="chuvadi-pdf"><package pattern="Chuvadi.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
CFG
echo "Wrote $DEST/nuget.ci.config"
