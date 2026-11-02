#!/bin/bash
# Regenerates the reference corpus (D-40): the docx/xlsx sources from staging/samples/Chuvadi.Layout.ReferenceCorpus
# and the reference PDFs from LibreOffice headless, then rewrites corpus/manifest.tsv. Linux or WSL, with LibreOffice
# installed (set SOFFICE to its path if it is not on PATH) and the .NET 10 SDK. LibreOffice itself is not committed.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../../.." && pwd)
soffice=${SOFFICE:-soffice}
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
sed -e "s|@FONTS@|$repo/src/Chuvadi.Layout.Fonts/Fonts|" -e "s|@CACHE@|$work/fc-cache|" "$here/fonts.conf.in" > "$work/fonts.conf"
dotnet run -c Release --project "$repo/staging/samples/Chuvadi.Layout.ReferenceCorpus" -- "$work/src"
FONTCONFIG_FILE="$work/fonts.conf" SAL_USE_VCLPLUGIN=svp "$soffice" -env:UserInstallation="file://$work/profile" \
  --headless --norestore --convert-to pdf --outdir "$work/ref" "$work"/src/*.docx "$work"/src/*.xlsx > "$work/soffice.log" 2>&1
version=$("$soffice" --version | head -n 1 | awk '{print $1" "$2}')
find "$here/corpus" -maxdepth 1 -type f \( -name '*.docx' -o -name '*.xlsx' -o -name '*.pdf' -o -name '*.tsv' \) -delete
cp "$work"/src/*.docx "$work"/src/*.xlsx "$work"/src/cases.tsv "$work"/ref/*.pdf "$here/corpus/"
{
  printf 'source\treference\tprinter\tnotes\n'
  for f in "$work"/src/*.docx "$work"/src/*.xlsx; do
    b=$(basename "$f")
    printf '%s\t%s\t%s\t\n' "$b" "${b%.*}.pdf" "$version (headless PDF export)"
  done
} > "$here/corpus/manifest.tsv"
echo "Corpus written with $version: $(ls "$here"/corpus/*.pdf | wc -l) reference PDFs."
