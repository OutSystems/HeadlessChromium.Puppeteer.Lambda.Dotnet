#!/usr/bin/env bash
#
# Regenerates payloads/wolfi.tar.br - the Wolfi/Chainguard supplement to al2023.tar.br.
#
# Why a supplement exists at all
# ------------------------------
# Chromium dlopen()s NSS's PKCS#11 module, libsoftokn3.so, the first time a page needs TLS.
# libsoftokn3.so has libsqlite3.so.0 in its NEEDED set (NSS stores its cert/key database in the
# SQLite-backed "sql:" format). Amazon Linux and Ubuntu ship libsqlite3.so.0 in the base image, so
# al2023.tar.br never had to carry it. Chainguard's distroless images do not ship it, and neither
# does the bundle - so NSS initialisation fails and Chromium aborts:
#
#   ERROR:crypto/nss_util.cc:256 Error initializing NSS with a persistent database
#       (sql:/tmp/.local/share/pki/nssdb): libsqlite3.so.0: cannot open shared object file
#   FATAL:crypto/nss_util.cc:146  nss_error=-5925
#
# A static `ldd` of the Chromium binary does not reveal this: libsoftokn3 is loaded by NSS at
# runtime, not linked by Chromium.
#
# Why the library is taken from Amazon Linux 2023
# -----------------------------------------------
# It pairs with the AL2023-built libsoftokn3.so that loads it, keeping the whole NSS stack from one
# build, and its glibc 2.34 baseline is older than any Wolfi glibc, so it loads there too. Its only
# other NEEDED entries - libm, libz, libc - are all present in the Chainguard images.
#
# Usage: bash tools/build-wolfi-payload.sh
# Requires: docker.

set -euo pipefail

# Pinned so a rebuild produces the same library rather than whatever is current.
source_image="${SOURCE_IMAGE:-amazonlinux:2023}"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output="$repo_root/payloads/wolfi.tar.br"

mkdir -p "$(dirname "$output")"

echo "Building $(basename "$output") from $source_image"

# ustar, because the library's own TarReader reads that format only. The archive lays the library
# out under lib/ so that extraction to /tmp puts it on the LD_LIBRARY_PATH (/tmp/lib) that
# ChromiumExtractor exports.
docker run --rm "$source_image" sh -c '
  set -e
  dnf install -y --quiet brotli tar >/dev/null 2>&1
  mkdir -p /build/lib
  cp /usr/lib64/libsqlite3.so.0.8.6 /build/lib/libsqlite3.so.0
  chmod 644 /build/lib/libsqlite3.so.0
  tar --format=ustar --owner=0 --group=0 --numeric-owner --mtime=@0 \
      -C /build -cf - lib/libsqlite3.so.0 | brotli -q 11 -c
' > "$output"

echo "Wrote $output ($(wc -c < "$output") bytes)"
