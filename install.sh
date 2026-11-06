#!/usr/bin/env bash
# Install a prebuilt sharp release for Linux x64.
#
#   curl -fsSL https://raw.githubusercontent.com/martasp/sharp/main/install.sh | bash
#
# Environment:
#   SHARP_VERSION       release tag to install (default: latest), e.g. v0.1.0
#   SHARP_INSTALL_DIR   destination directory (default: ~/.local/bin)
set -euo pipefail

repo="martasp/sharp"
install_dir="${SHARP_INSTALL_DIR:-$HOME/.local/bin}"

if [[ "$(uname -s)" != "Linux" ]]; then
  echo "error: this installer supports Linux only" >&2
  exit 1
fi

case "$(uname -m)" in
  x86_64|amd64) arch="x64" ;;
  *)
    echo "error: sharp is currently published for Linux x64 only (detected $(uname -m))" >&2
    exit 1
    ;;
esac

asset="sharp-linux-${arch}.tar.gz"

if [[ -n "${SHARP_VERSION:-}" ]]; then
  version="${SHARP_VERSION#v}"
  base_url="https://github.com/${repo}/releases/download/v${version}"
else
  base_url="https://github.com/${repo}/releases/latest/download"
fi

for cmd in curl tar sha256sum; do
  command -v "$cmd" >/dev/null || { echo "error: $cmd is required" >&2; exit 1; }
done

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

echo "==> Downloading ${asset}"
curl -fsSL "${base_url}/${asset}" -o "${tmp}/${asset}"
curl -fsSL "${base_url}/SHA256SUMS" -o "${tmp}/SHA256SUMS"

echo "==> Verifying checksum"
expected="$(awk -v asset="$asset" '$2 == asset { print $1 }' "${tmp}/SHA256SUMS")"
if [[ -z "$expected" ]]; then
  echo "error: ${asset} not found in SHA256SUMS" >&2
  exit 1
fi
actual="$(sha256sum "${tmp}/${asset}" | awk '{ print $1 }')"
if [[ "$actual" != "$expected" ]]; then
  echo "error: checksum mismatch for ${asset}" >&2
  exit 1
fi

tar -xzf "${tmp}/${asset}" -C "$tmp"
src="${tmp}/sharp-linux-${arch}/sharp"
if [[ ! -f "$src" ]]; then
  echo "error: ${asset} does not contain sharp-linux-${arch}/sharp" >&2
  exit 1
fi

mkdir -p "$install_dir"
echo "==> Installing to ${install_dir}/sharp"
cp "$src" "${install_dir}/sharp"
chmod 0755 "${install_dir}/sharp"

case ":$PATH:" in
  *":$install_dir:"*) ;;
  *)
    rc_file=""
    for candidate in "$HOME/.bashrc" "$HOME/.zshrc" "$HOME/.profile"; do
      [[ -f "$candidate" ]] && { rc_file="$candidate"; break; }
    done

    if [[ -n "$rc_file" ]] && ! grep -Fq "export PATH=\"\$PATH:$install_dir\"" "$rc_file"; then
      {
        echo
        echo "# added by sharp install.sh"
        echo "export PATH=\"\$PATH:$install_dir\""
      } >> "$rc_file"
      echo "==> Added $install_dir to PATH in $rc_file; restart your shell or source it."
    elif [[ -z "$rc_file" ]]; then
      echo "warning: $install_dir is not on PATH; add it manually." >&2
    fi
    ;;
esac

echo
echo "==> Done. Try:"
echo "      sharp --version"
echo "      sharp build"
