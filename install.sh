#!/usr/bin/env bash
# Build the NativeAOT compiler and put `sharp` on your PATH.
#
#   ./install.sh              publish, then install to ~/.local/bin/sharp
#   ./install.sh --no-publish reuse the existing publish output (fast re-install)
#
# Safe to re-run on any machine: it only writes ~/.local/bin/sharp and one line in your shell rc.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$repo_root"

project="src/NativeCompilerCLI/NativeCompilerCLI.csproj"
runtime="linux-x64"
if [[ "$(uname -s)" == "Darwin" ]]; then runtime="osx-arm64"; fi

name="NativeCompilerCLI"
[[ "$(uname -s)" == "Darwin" ]] && name="${name}.app/Contents/MacOS/NativeCompilerCLI"
published="src/NativeCompilerCLI/bin/Release/net10.0/$runtime/publish/$name"

skip_publish=0
[[ "${1:-}" == "--no-publish" ]] && skip_publish=1

if [[ $skip_publish -eq 0 ]]; then
  echo "==> Publishing NativeAOT compiler (this takes a minute)"
  dotnet publish "$project" -r "$runtime" -c Release
fi

if [[ ! -f "$published" ]]; then
  echo "error: publish output not found at $published" >&2
  echo "       run without --no-publish to build it first" >&2
  exit 1
fi

# Prefer a real copy over a symlink: the publish output sits under bin/, which any
# `dotnet clean` would delete, and a symlink would then dangle.
install_dir="$HOME/.local/bin"
mkdir -p "$install_dir"
target="$install_dir/sharp"

echo "==> Installing to $target"
cp "$published" "$target"
chmod +x "$target"

# ~/.local/bin is on PATH on most modern Linux/macOS setups, but not all shells pick that
# up automatically. Add it once, only if it is genuinely missing.
rc_file=""
for candidate in "$HOME/.bashrc" "$HOME/.zshrc" "$HOME/.profile"; do
  [[ -f "$candidate" ]] && { rc_file="$candidate"; break; }
done

case ":$PATH:" in
  *":$install_dir:"*) on_path=1 ;;
  *) on_path=0 ;;
esac

if [[ $on_path -eq 1 ]]; then
  echo "==> $install_dir is already on your PATH"
else
  [[ -n "$rc_file" ]] || { echo "warning: no shell rc file found; add $install_dir to PATH manually" >&2; exit 0; }
  echo "==> Adding $install_dir to PATH in $rc_file"
  {
    echo
    echo "# added by sharp install.sh"
    echo "export PATH=\"\$PATH:$install_dir\""
  } >> "$rc_file"
  echo "    restart your shell, or run: source $rc_file"
fi

echo
echo "==> Done. Try:"
echo "      sharp --version"
echo "      sharp build"