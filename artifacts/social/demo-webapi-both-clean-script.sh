#!/usr/bin/env bash
set -euo pipefail

cd /home/mart/main/sharp
export DOTNET_ROOT=/home/mart/.local/share/mise/installs/dotnet/11.0.100
export DOTNET_ROOT_X64=$DOTNET_ROOT
export PATH=$DOTNET_ROOT:$PATH
export DOTNET_CLI_TELEMETRY_OPTOUT=1

SHARP=/home/mart/main/sharp/src/Sharp.Cli/bin/Release/net11.0/linux-x64/publish/Sharp.Cli
PROJECT=tests/TestExampleProjects/WebApiWorking/WebApiWorking.csproj
DOTNET_DIR=$(mktemp -d /tmp/sharp-webapi-dotnet-clean-demo.XXXXXX)
SHARP_DIR=$(mktemp -d /tmp/sharp-webapi-sharp-clean-demo.XXXXXX)
trap 'rm -rf "$DOTNET_DIR" "$SHARP_DIR"' EXIT
git archive HEAD | tar -x -C "$DOTNET_DIR"
git archive HEAD | tar -x -C "$SHARP_DIR"
(cd "$DOTNET_DIR" && dotnet restore "$PROJECT" --nologo >/dev/null)
(cd "$SHARP_DIR" && dotnet restore "$PROJECT" --nologo >/dev/null)

bold=$'\033[1m'
cyan=$'\033[36m'
green=$'\033[32m'
reset=$'\033[0m'
url=http://localhost:5000

type_line() {
  printf '%s$ %s%s\n' "$cyan" "$1" "$reset"
  sleep 0.7
}

stop_server() {
  local pid=$1
  kill -INT -- -"$pid" 2>/dev/null || true
  sleep 0.6
  kill -KILL -- -"$pid" 2>/dev/null || true
  wait "$pid" 2>/dev/null || true
}

clear
printf '%s%s%s\n' "$bold" 'sharp v0.3.1 — Web API clean build + run' "$reset"
printf '%sfresh HEAD copies, restore pre-staged, clean before each build%s\n' "$bold" "$reset"
sleep 1.2

type_line 'sharp --version'
"$SHARP" --version
sleep 1.0

type_line 'dotnet build-server shutdown'
dotnet build-server shutdown
sleep 0.8

type_line "dotnet clean $PROJECT --nologo -v:m | hide-warning-lines.py"
(cd "$DOTNET_DIR" && dotnet clean "$PROJECT" --nologo -v:m) | python3 artifacts/social/hide-warning-lines.py
sleep 1.2

type_line "dotnet build $PROJECT --no-restore --no-incremental --nologo -tl:on | hide-warning-lines.py"
(cd "$DOTNET_DIR" && dotnet build "$PROJECT" --no-restore --no-incremental --nologo -tl:on) | python3 artifacts/social/hide-warning-lines.py
sleep 1.4

type_line "sharp clean $PROJECT"
(cd "$SHARP_DIR" && "$SHARP" clean "$PROJECT")
sleep 1.2

type_line "sharp build $PROJECT"
(cd "$SHARP_DIR" && "$SHARP" build "$PROJECT")
sleep 1.4

type_line "sharp run $PROJECT"
(cd "$SHARP_DIR" && setsid "$SHARP" run "$PROJECT") &
pid=$!
for _ in $(seq 1 30); do
  if curl -fsS "$url/" >/dev/null 2>&1; then break; fi
  sleep 0.4
done
type_line "curl -fsS -i $url/"
sleep 0.3
curl -fsS -i "$url/"
printf '\n'
sleep 1.2
stop_server "$pid"
sleep 1.2

printf '%ssame source, same endpoint, no MSBuild at runtime%s\n' "$green" "$reset"
sleep 3.0
