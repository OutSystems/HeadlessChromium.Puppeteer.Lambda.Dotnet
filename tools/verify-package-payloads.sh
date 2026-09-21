#!/usr/bin/env bash
#
# AC5 - prove that every archive the platform detector can name is actually inside the NuGet
# package.
#
# The payloads are packed as <Content> with PackageCopyToOutput, which puts them in the .nupkg but
# NOT in the library's own bin/ directory. Checking a build output would therefore pass while the
# shipped package is missing a file, so this script inspects the package itself.
#
# Usage: bash tools/verify-package-payloads.sh

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$repo_root/src/HeadlessChromium.Puppeteer.Lambda.Dotnet/HeadlessChromium.Puppeteer.Lambda.Dotnet.csproj"
output_dir="$repo_root/artifacts/payload-verification"

# Every archive ExtractChromium() can open: the three unconditional ones, plus everything
# GetDependencyFileNames() can name for a supported platform. Keep in sync with PlatformDetector.
required_payloads=(
  "chromium.br"
  "fonts.tar.br"
  "swiftshader.tar.br"
  "al2023.tar.br"   # GetDependencyFileNames("al2023"), ("ubuntu-22.04") and ("wolfi")
  "wolfi.tar.br"    # GetDependencyFileNames("wolfi"), on top of al2023.tar.br
)

echo "Packing $project"
rm -rf "$output_dir"
dotnet pack "$project" -c Release -o "$output_dir" >/dev/null

nupkg="$(find "$output_dir" -maxdepth 1 -name '*.nupkg' ! -name '*.snupkg' | head -1)"

if [[ -z "$nupkg" ]]; then
  echo "FAIL: no .nupkg produced in $output_dir" >&2
  exit 1
fi

echo "Inspecting $(basename "$nupkg")"
entries="$(unzip -l "$nupkg")"

failed=0
for payload in "${required_payloads[@]}"; do
  match="$(grep -E "contentFiles/.*/${payload}$" <<<"$entries" || true)"

  if [[ -z "$match" ]]; then
    echo "FAIL: $payload is not in the package under contentFiles/" >&2
    failed=1
  else
    echo "  OK  $(awk '{print $NF}' <<<"$match" | head -1)"
  fi
done

if [[ $failed -ne 0 ]]; then
  echo
  echo "The detector can name an archive that the package does not ship." >&2
  echo "ExtractDependencies() would throw FileNotFoundException at runtime." >&2
  exit 1
fi

echo
echo "All ${#required_payloads[@]} required payloads are present in the package."
