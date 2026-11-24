#!/usr/bin/env bash
# Downloads llama-server and Hy-MT2 1.8B Q4_K_M for macOS.
# macOS support is still in progress: this prepares the model, the Accessibility/Vision
# platform layer is not implemented yet.
set -euo pipefail

ROOT="${HOME}/.local/share/HoverTrans"
RUNTIME="${ROOT}/runtime"
MODELS="${ROOT}/models"
mkdir -p "${RUNTIME}" "${MODELS}"

ARCH="$(uname -m)"
case "${ARCH}" in
  arm64) VARIANT="arm64" ;;
  x86_64) VARIANT="x64" ;;
  *) echo "Unsupported architecture: ${ARCH}" >&2; exit 1 ;;
esac

TAG="b12662"
ASSET="llama-${TAG}-bin-macos-${VARIANT}.zip"
URL="https://github.com/ggml-org/llama.cpp/releases/download/${TAG}/${ASSET}"
ZIP="${TMPDIR:-/tmp}/${ASSET}"

if [ ! -x "${RUNTIME}/llama-server" ]; then
  echo "==> llama.cpp ${TAG} (${VARIANT})"
  curl -L --fail --retry 3 --progress-bar -o "${ZIP}" "${URL}"
  unzip -oq "${ZIP}" -d "${RUNTIME}"
  # Releases place the binaries in a subfolder; flatten them.
  if [ ! -x "${RUNTIME}/llama-server" ]; then
    found="$(find "${RUNTIME}" -name llama-server -type f | head -n 1)"
    if [ -n "${found}" ]; then
      cp -R "$(dirname "${found}")/." "${RUNTIME}/"
    fi
  fi
  rm -f "${ZIP}"
fi

MODEL="${MODELS}/Hy-MT2-1.8B-Q4_K_M.gguf"
if [ ! -f "${MODEL}" ]; then
  echo "==> Hy-MT2-1.8B-Q4_K_M (about 1.1 GB)"
  curl -L --fail --retry 3 -C - --progress-bar \
    -o "${MODEL}" \
    "https://modelscope.cn/models/Tencent-Hunyuan/Hy-MT2-1.8B-GGUF/resolve/master/Hy-MT2-1.8B-Q4_K_M.gguf"
fi

echo
echo "Done."
echo "  llama-server: ${RUNTIME}/llama-server"
echo "  model:        ${MODEL}"
shasum -a 256 "${MODEL}"
