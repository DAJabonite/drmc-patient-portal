#!/usr/bin/env bash
set -euo pipefail

# Ubuntu 24.04 x64 runtime dependencies for the pinned Tesseract 5.2 .NET wrapper.
source /etc/os-release
[[ "$ID" == ubuntu && "$VERSION_ID" == 24.04 && "$(uname -m)" == x86_64 ]] || {
  echo 'This script supports Ubuntu 24.04 x64 only.' >&2; exit 1;
}
[[ "$EUID" == 0 ]] || { echo 'Run this script with sudo.' >&2; exit 1; }
native_root="${1:-/opt/drmc/ocr}"
[[ "$native_root" == /* ]] || { echo 'Supply an absolute native library directory.' >&2; exit 1; }
apt-get update
DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
  tesseract-ocr libtesseract5 libleptonica-dev tesseract-ocr-eng
install -d -m 0755 "$native_root/x64"

# The wrapper resolves these names under <NativeLibraryPath>/x64.
# Keep distro-maintained binaries; do not replace system libraries.
ln -sfn /usr/lib/x86_64-linux-gnu/libtesseract.so.5 "$native_root/x64/libtesseract50.so"
ln -sfn /usr/lib/x86_64-linux-gnu/liblept.so.5 "$native_root/x64/libleptonica-1.82.0.so"
ln -sfn /usr/lib/x86_64-linux-gnu/libdl.so.2 "$native_root/x64/libdl.so"
for library in "$native_root/x64/"*.so; do
  [[ -e "$library" ]] || { echo "Missing native library target: $library" >&2; exit 1; }
done
[[ -s /usr/share/tesseract-ocr/5/tessdata/eng.traineddata ]] || { echo 'English language data is missing.' >&2; exit 1; }
tesseract --version
printf '\nConfigure the portal service environment:\nOcr__NativeLibraryPath=%s\nOcr__TessDataPath=/usr/share/tesseract-ocr/5/tessdata\nLD_LIBRARY_PATH=%s/x64\n' "$native_root" "$native_root"
echo 'Portal startup additionally checks native engine initialization and JPEG/PNG decoding.'
