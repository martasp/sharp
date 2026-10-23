#!/usr/bin/env bash
# Publish the compiler as a JIT (CoreCLR) single-file, ReadyToRun, self-contained binary.
#
# This is the alternative to the default NativeAOT publish (publish.sh). Instead of compiling
# to native code, it ships the CoreCLR runtime next to the app and precompiles the IL:
#
#   SelfContained=true        carry the runtime, so the binary needs no installed .NET
#   PublishSingleFile=true    one executable with the assemblies bundled in
#   PublishReadyToRun=true    ReadyToRun-compiled IL, so startup skips most JIT work
#   PublishAot=false          stay on the JIT: dynamic code and source-generator discovery
#                             keep working the way they do under a normal `dotnet` app
#
# Unlike AOT, ReadyToRun is not a closed world - the JIT is still present at run time, so
# reflection, runtime assembly loading and dynamic source generators behave normally. It is
# the middle ground between the in-process JIT build (Sharp.Cli.dll) and AOT: faster
# startup than pure JIT, full dynamic-code support unlike AOT.
#
# Output lands in artifacts/ (gitignored) rather than bin/Release/.../publish, so it never
# collides with or clobbers the AOT publish output.
#
#   ./publishJitSingleFileReadyToRunSelfContained.sh
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$repo_root"

project="src/Sharp.Cli/Sharp.Cli.csproj"
runtime="linux-x64"
if [[ "$(uname -s)" == "Darwin" ]]; then runtime="osx-arm64"; fi

out="$repo_root/artifacts/publish-jit-singlefile-r2r/$runtime"

echo "==> Publishing JIT single-file ReadyToRun self-contained compiler ($runtime)"
dotnet publish "$project" \
  -c Release \
  -r "$runtime" \
  -p:PublishAot=false \
  -p:SelfContained=true \
  -p:PublishSingleFile=true \
  -p:PublishReadyToRun=true \
  -o "$out"

exe="$out/Sharp.Cli"
if [[ ! -f "$exe" ]]; then
  exe="$(find "$out" -type f -name Sharp.Cli | head -n1)"
fi

echo
echo "==> Done. Published to: $out"
echo "      binary: $exe"
