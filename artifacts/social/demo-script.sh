#!/usr/bin/env bash
set -euo pipefail

cd /home/mart/main/sharp
export DOTNET_ROOT=/home/mart/.local/share/mise/installs/dotnet/11.0.100
export DOTNET_ROOT_X64=$DOTNET_ROOT
export PATH=$DOTNET_ROOT:$PATH
export DOTNET_CLI_TELEMETRY_OPTOUT=1

SHARP=/home/mart/main/sharp/src/Sharp.Cli/bin/Release/net11.0/linux-x64/publish/Sharp.Cli
DOTNET_DIR=$(mktemp -d /tmp/sharp-self-dotnet.XXXXXX)
SHARP_DIR=$(mktemp -d /tmp/sharp-self-sharp.XXXXXX)
trap 'rm -rf "$DOTNET_DIR" "$SHARP_DIR"' EXIT
git archive HEAD | tar -x -C "$DOTNET_DIR"
git archive HEAD | tar -x -C "$SHARP_DIR"
(cd "$DOTNET_DIR" && dotnet restore Sharp.sln --nologo >/dev/null)

bold=$'\033[1m'
cyan=$'\033[36m'
green=$'\033[32m'
reset=$'\033[0m'

type_line() {
  printf '%s$ %s%s\n' "$cyan" "$1" "$reset"
  sleep 0.45
}

clear
printf '%s%s%s\n' "$bold" 'sharp v0.3.1 — fair compile-only self-build' "$reset"
printf '%sfresh HEAD copies, restore pre-staged, no incremental%s\n' "$bold" "$reset"
sleep 0.8

type_line 'sharp --version'
"$SHARP" --version
sleep 0.7

type_line 'dotnet build Sharp.sln --no-restore --no-incremental --nologo -tl:on | hide-warning-lines.py'
(cd "$DOTNET_DIR" && dotnet build Sharp.sln --no-restore --no-incremental --nologo -tl:on) | python3 artifacts/social/hide-warning-lines.py
sleep 0.7

type_line 'sharp build Sharp.sln'
(cd "$SHARP_DIR" && "$SHARP" build Sharp.sln)
sleep 0.7

type_line 'dotnet run --project tests/TestExampleProjects/HelloParityFixture'
dotnet run --project tests/TestExampleProjects/HelloParityFixture
sleep 0.7

type_line 'sharp run tests/TestExampleProjects/HelloParityFixture'
"$SHARP" run tests/TestExampleProjects/HelloParityFixture
sleep 1.0

printf '%ssame source, same output, no MSBuild at runtime%s\n' "$green" "$reset"
sleep 1.0
