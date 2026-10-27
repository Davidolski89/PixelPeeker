#!/usr/bin/env bash
# Check whether this Linux machine can build and run PixelPeeker.
# The script never calls sudo. It only prints the dnf commands to run.
set -euo pipefail

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
ROOT=$(cd "$SCRIPT_DIR/.." && pwd)

APPIMAGE=""
TARBALL=""
SKIP_TESTS=0
DOWNLOAD_FFMPEG=0
SMOKE_SECONDS=12
LOG="${PWD}/pixelpeeker-linux-check.log"
WORK=""
APP_PID=""
FAILS=0
WARNS=0

if [[ -t 1 ]]; then
  C_RED=$'\e[31m'
  C_GREEN=$'\e[32m'
  C_YELLOW=$'\e[33m'
  C_RESET=$'\e[0m'
else
  C_RED=""
  C_GREEN=""
  C_YELLOW=""
  C_RESET=""
fi

usage() {
  cat <<'EOF'
Usage: scripts/test-linux.sh [options]

Check whether this machine can run the Linux PixelPeeker (Avalonia) build.
Designed for Fedora 44 x86_64 and usable on other glibc distributions.
The script does not run sudo. When something is missing it prints a
suggested command such as: sudo dnf install ...

By default it builds a self-contained linux publish from this checkout
(dotnet SDK from global.json, rollForward latestFeature) and runs
dotnet test on FFmpegStudio.Linux.slnf. Pass a CI artifact instead with
--appimage or --tarball.

Options:
  --appimage PATH      Run a downloaded AppImage instead of building
  --tarball PATH       Run a downloaded portable tar.gz instead of building
  --skip-tests         Skip dotnet test when building from source
  --download-ffmpeg    Download the BtbN static GPL FFmpeg build into a temp
                       directory, verify SHA-256, then run ffmpeg -version,
                       a one-second testsrc encode, and ffprobe
  --smoke-seconds N    Seconds to leave the window open (default: 12)
  --log PATH           Full log file (default: ./pixelpeeker-linux-check.log)
  --help               Show this help

Exit status is 1 when any check is FAIL. WARN does not fail the script.
Temporary directories are removed on exit. The log file is kept.
EOF
}

log_line() {
  printf '%s\n' "$*" >>"$LOG"
}

say() {
  printf '%s\n' "$*"
  log_line "$*"
}

die_usage() {
  printf '%s\n' "$*" >&2
  usage >&2
  exit 2
}

record() {
  local status="$1"
  local name="$2"
  local detail="$3"
  case "$status" in
    PASS) ;;
    WARN) WARNS=$((WARNS + 1)) ;;
    FAIL) FAILS=$((FAILS + 1)) ;;
    *) die_usage "internal status $status" ;;
  esac
  RESULTS+=("$status"$'\t'"$name"$'\t'"$detail")
  log_line "[$status] $name — $detail"
}

suggest() {
  say "  suggested: $*"
}

cleanup() {
  local pid
  if [[ -n "$APP_PID" ]]; then
    pid="$APP_PID"
    APP_PID=""
    if kill -0 "$pid" 2>/dev/null; then
      kill -TERM -- "-$pid" 2>/dev/null || kill -TERM "$pid" 2>/dev/null || true
      sleep 1
      kill -KILL -- "-$pid" 2>/dev/null || kill -KILL "$pid" 2>/dev/null || true
    fi
  fi
  if [[ -n "$WORK" && -d "$WORK" ]]; then
    rm -rf "$WORK"
  fi
}
trap cleanup EXIT

parse_args() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --help|-h) usage; exit 0 ;;
      --appimage)
        [[ $# -ge 2 ]] || die_usage "--appimage needs a path"
        APPIMAGE="$2"
        shift 2
        ;;
      --tarball)
        [[ $# -ge 2 ]] || die_usage "--tarball needs a path"
        TARBALL="$2"
        shift 2
        ;;
      --skip-tests) SKIP_TESTS=1; shift ;;
      --download-ffmpeg) DOWNLOAD_FFMPEG=1; shift ;;
      --smoke-seconds)
        [[ $# -ge 2 ]] || die_usage "--smoke-seconds needs a number"
        SMOKE_SECONDS="$2"
        shift 2
        ;;
      --log)
        [[ $# -ge 2 ]] || die_usage "--log needs a path"
        LOG="$2"
        shift 2
        ;;
      *) die_usage "Unknown option: $1" ;;
    esac
  done
  if [[ -n "$APPIMAGE" && -n "$TARBALL" ]]; then
    die_usage "Pass only one of --appimage and --tarball"
  fi
  if [[ ! "$SMOKE_SECONDS" =~ ^[0-9]+$ ]] || [[ "$SMOKE_SECONDS" -lt 1 ]]; then
    die_usage "--smoke-seconds must be a positive integer"
  fi
}

os_id() {
  local id=""
  if [[ -f /etc/os-release ]]; then
    # shellcheck disable=SC1091
    id=$(. /etc/os-release && printf '%s' "${ID:-}")
  fi
  printf '%s' "$id"
}

os_pretty() {
  local pretty=""
  if [[ -f /etc/os-release ]]; then
    # shellcheck disable=SC1091
    pretty=$(. /etc/os-release && printf '%s' "${PRETTY_NAME:-${NAME:-unknown}}")
  else
    pretty="unknown (no /etc/os-release)"
  fi
  printf '%s' "$pretty"
}

text_mentions_musl() {
  local text="$1"
  [[ "$text" == *[Mm][Uu][Ss][Ll]* || "$text" == *ld-musl* ]]
}

os_release_is_alpine() {
  local line key value
  [[ -f /etc/alpine-release ]] && return 0
  [[ -f /etc/os-release ]] || return 1
  while IFS= read -r line; do
    line="${line%%$'\r'}"
    [[ "$line" == *=* ]] || continue
    key="${line%%=*}"
    value="${line#*=}"
    value="${value%\"}"
    value="${value#\"}"
    if [[ "$key" == "ID" && "$value" == "alpine" ]]; then
      return 0
    fi
    if [[ "$key" == "ID_LIKE" && "$value" == *alpine* ]]; then
      return 0
    fi
  done < /etc/os-release
  return 1
}

# Same floor as LinuxLibc: glibc >= 2.28, musl and unknown never qualify.
extract_glibc() {
  local text="$1"
  text_mentions_musl "$text" && return 1
  if [[ "$text" =~ ([Gg][Ll][Ii][Bb][Cc]|[Gg][Nn][Uu][[:space:]]+[Ll][Ii][Bb][Cc])[^0-9]{0,24}([0-9]+)\.([0-9]+) ]]; then
    printf '%s.%s' "${BASH_REMATCH[2]}" "${BASH_REMATCH[3]}"
    return 0
  fi
  return 1
}

glibc_at_least_228() {
  local version="$1" major minor
  IFS=. read -r major minor <<<"$version"
  if (( major > 2 )); then
    return 0
  fi
  if (( major == 2 && minor >= 28 )); then
    return 0
  fi
  return 1
}

# Fedora package names stay aligned with scripts/setup-linux.sh.
fedora_package_for() {
  case "$1" in
    libX11.so.6) printf '%s' libX11 ;;
    libICE.so.6) printf '%s' libICE ;;
    libSM.so.6) printf '%s' libSM ;;
    libfontconfig.so.1) printf '%s' fontconfig ;;
    libfreetype.so.6) printf '%s' freetype ;;
    libXext.so.6) printf '%s' libXext ;;
    libXcursor.so.1) printf '%s' libXcursor ;;
    libXi.so.6) printf '%s' libXi ;;
    libXrandr.so.2) printf '%s' libXrandr ;;
    libGL.so.1) printf '%s' mesa-libGL ;;
    libxkbcommon.so.0) printf '%s' libxkbcommon ;;
    libstdc++.so.6) printf '%s' libstdc++ ;;
    libgcc_s.so.1) printf '%s' libgcc ;;
    libz.so.1) printf '%s' zlib ;;
    libfuse.so.2) printf '%s' fuse-libs ;;
    libvlc.so|libvlc.so.5) printf '%s' vlc-libs ;;
    *) printf '%s' "" ;;
  esac
}

lib_present() {
  local soname="$1" dir
  if command -v ldconfig >/dev/null 2>&1 && ldconfig -p 2>/dev/null | grep -F "$soname" >/dev/null; then
    return 0
  fi
  for dir in /usr/lib64 /usr/lib /lib64 /lib /usr/lib64/pipewire-0.3; do
    if [[ -e "$dir/$soname" ]]; then
      return 0
    fi
  done
  return 1
}

sdk_requirement() {
  local version="10.0.100" roll="latestFeature" parsed=""
  if [[ -f "$ROOT/global.json" ]] && command -v python3 >/dev/null 2>&1; then
    if parsed=$(python3 - "$ROOT/global.json" <<'PY'
import json, sys
data = json.load(open(sys.argv[1], encoding="utf-8"))
sdk = data.get("sdk", {})
print(sdk.get("version", "10.0.100"))
print(sdk.get("rollForward", "latestFeature"))
PY
); then
      version=$(printf '%s\n' "$parsed" | sed -n '1p')
      roll=$(printf '%s\n' "$parsed" | sed -n '2p')
    fi
  fi
  printf '%s %s\n' "$version" "$roll"
}

version_ge() {
  local installed="$1" required="$2"
  [[ "$(printf '%s\n%s\n' "$required" "$installed" | sort -V | head -n 1)" == "$required" ]]
}

sdk_satisfies() {
  local installed="$1" required="$2" roll="$3" inst_major req_major
  inst_major="${installed%%.*}"
  req_major="${required%%.*}"
  if [[ "$inst_major" != "$req_major" ]]; then
    return 1
  fi
  case "$roll" in
    latestFeature|latestMajor|major|feature)
      version_ge "$installed" "$required"
      ;;
    *)
      version_ge "$installed" "$required"
      ;;
  esac
}

find_binary() {
  local root="$1"
  local found
  found=$(find "$root" -type f -name 'PixelPeeker.Avalonia' ! -name '*.pdb' -print -quit)
  printf '%s' "$found"
}

prepare_appimage() {
  local image="$1" dest="$2" extracted=""
  mkdir -p "$dest"
  cp -a "$image" "$dest/PixelPeeker.AppImage"
  chmod +x "$dest/PixelPeeker.AppImage"
  if lib_present "libfuse.so.2"; then
    record PASS "AppImage FUSE" "libfuse.so.2 is installed; the AppImage will be started directly"
    LAUNCH=("$dest/PixelPeeker.AppImage")
    if (cd "$dest" && ./PixelPeeker.AppImage --appimage-extract >/dev/null); then
      extracted=$(find_binary "$dest/squashfs-root")
    else
      record WARN "AppImage extract" "Could not extract a copy for ldd. The smoke launch still uses the AppImage."
    fi
  else
    record WARN "AppImage FUSE" "libfuse.so.2 is missing. Falling back to --appimage-extract-and-run"
    suggest "sudo dnf install fuse-libs"
    LAUNCH=(env APPIMAGE_EXTRACT_AND_RUN=1 "$dest/PixelPeeker.AppImage")
    if (cd "$dest" && APPIMAGE_EXTRACT_AND_RUN=1 ./PixelPeeker.AppImage --appimage-extract >/dev/null); then
      extracted=$(find_binary "$dest/squashfs-root")
    else
      record FAIL "AppImage extract" "Could not extract $image"
      LAUNCH=()
      return 1
    fi
  fi
  if [[ -n "$extracted" ]]; then
    BIN_PATH="$extracted"
  fi
}

run_logged() {
  local outfile="$1"
  shift
  set +e
  "$@" >"$outfile" 2>&1
  local code=$?
  set -e
  cat "$outfile" >>"$LOG"
  return "$code"
}

check_ldd() {
  local bin="$1" dir="" missing="" soname packages pkg skia_missing=""
  dir=$(dirname "$bin")
  missing=$(LD_LIBRARY_PATH="${dir}${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}" ldd "$bin" 2>/dev/null | awk '/not found/ { print $1 }' || true)
  if [[ -f "$dir/libSkiaSharp.so" ]]; then
    skia_missing=$(LD_LIBRARY_PATH="${dir}${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}" ldd "$dir/libSkiaSharp.so" 2>/dev/null | awk '/not found/ { print $1 }' || true)
    missing=$(printf '%s\n%s\n' "$missing" "$skia_missing" | awk 'NF && !seen[$0]++')
  fi
  if [[ -z "$missing" ]]; then
    record PASS "Native libraries" "ldd found no missing libraries for the Avalonia binary"
    return 0
  fi
  packages=""
  while IFS= read -r soname; do
    [[ -n "$soname" ]] || continue
    pkg=$(fedora_package_for "$soname")
    if [[ -n "$pkg" ]]; then
      packages="$packages $pkg"
    else
      packages="$packages $soname"
    fi
  done <<<"$missing"
  record FAIL "Native libraries" "Missing: $(echo "$missing" | tr '\n' ' ')"
  suggest "sudo dnf install${packages}"
}

smoke_init() {
  local data="$1" outfile="$2"
  shift 2
  mkdir -p "$data"
  local code=0
  set +e
  FFMPEG_STUDIO_DATA="$data" timeout 90s "$@" --smoke >"$outfile" 2>&1
  code=$?
  set -e
  cat "$outfile" >>"$LOG"
  if [[ "$code" -eq 0 ]] && grep -q "PixelPeeker Avalonia smoke ready" "$outfile"; then
    record PASS "--smoke" "Services initialized and the process exited 0"
  else
    record FAIL "--smoke" "Exit $code. Expected 'PixelPeeker Avalonia smoke ready'. See the log."
  fi
}

window_visible() {
  if command -v xdotool >/dev/null 2>&1; then
    xdotool search --name PixelPeeker >/dev/null 2>&1
    return $?
  fi
  if command -v wmctrl >/dev/null 2>&1; then
    wmctrl -l 2>/dev/null | grep -qi PixelPeeker
    return $?
  fi
  return 2
}

smoke_window() {
  local data="$1" outfile="$2"
  shift 2
  mkdir -p "$data"
  set +e
  FFMPEG_STUDIO_DATA="$data" setsid "$@" >"$outfile" 2>&1 &
  APP_PID=$!
  set -e
  sleep "$SMOKE_SECONDS"
  if ! kill -0 "$APP_PID" 2>/dev/null; then
    local code=0
    set +e
    wait "$APP_PID"
    code=$?
    set -e
    APP_PID=""
    cat "$outfile" >>"$LOG"
    record FAIL "Window smoke" "Process exited after ${SMOKE_SECONDS}s (status $code). See the log."
    return
  fi
  cat "$outfile" >>"$LOG"
  record PASS "Window smoke" "Process $APP_PID was still running after ${SMOKE_SECONDS}s"
  set +e
  window_visible
  local window_code=$?
  set -e
  if [[ "$window_code" -eq 0 ]]; then
    record PASS "Window" "xdotool or wmctrl sees a PixelPeeker window"
  elif [[ "$window_code" -eq 2 ]]; then
    record WARN "Window" "Process is alive. xdotool and wmctrl are not installed, so the window was not checked"
    suggest "sudo dnf install xdotool"
  else
    record FAIL "Window" "Process is alive but no PixelPeeker window was found"
  fi
  local pid="$APP_PID" waited=0
  APP_PID=""
  kill -TERM -- "-$pid" 2>/dev/null || kill -TERM "$pid" 2>/dev/null || true
  while [[ "$waited" -lt 5 ]]; do
    kill -0 "$pid" 2>/dev/null || break
    sleep 1
    waited=$((waited + 1))
  done
  if kill -0 "$pid" 2>/dev/null; then
    kill -KILL -- "-$pid" 2>/dev/null || kill -KILL "$pid" 2>/dev/null || true
    record WARN "Shutdown" "The smoke process did not exit on SIGTERM and was killed"
  else
    record PASS "Shutdown" "The smoke process exited after SIGTERM"
  fi
  set +e
  wait "$pid"
  set -e
}

download_btbn() {
  local dest="$1" token="$2"
  mkdir -p "$dest"
  if ! command -v curl >/dev/null 2>&1; then
    record FAIL "BtbN download" "curl is not installed"
    suggest "sudo dnf install curl"
    return
  fi
  if ! command -v python3 >/dev/null 2>&1; then
    record FAIL "BtbN download" "python3 is required to read the GitHub release list"
    suggest "sudo dnf install python3"
    return
  fi
  local api="$dest/releases.json"
  if ! curl -fsSL --retry 3 -H "User-Agent: PixelPeeker-linux-check" -H "Accept: application/vnd.github+json" \
      -o "$api" "https://api.github.com/repos/BtbN/FFmpeg-Builds/releases?per_page=8"; then
    record FAIL "BtbN download" "Could not read the BtbN release list"
    return
  fi
  local picked
  picked=$(python3 - "$api" "$token" <<'PY'
import json, re, sys
releases = json.load(open(sys.argv[1], encoding="utf-8"))
token = sys.argv[2]
pattern = re.compile(rf"^ffmpeg-N-\d+-g[0-9a-f]+-{re.escape(token)}-gpl\.tar\.xz$", re.I)
chosen = None
for release in sorted(releases, key=lambda item: item.get("tag_name", ""), reverse=True):
    tag = release.get("tag_name") or ""
    if not tag.startswith("autobuild-"):
        continue
    assets = {asset.get("name"): asset.get("browser_download_url") for asset in release.get("assets", [])}
    if "checksums.sha256" not in assets:
        continue
    name = next((item for item in assets if item and pattern.match(item)), None)
    if name:
        chosen = (assets[name], assets["checksums.sha256"], name)
        break
if chosen is None:
    sys.exit(2)
print(chosen[0])
print(chosen[1])
print(chosen[2])
PY
) || {
    record FAIL "BtbN download" "No static GPL autobuild for $token was listed"
    return
  }
  local archive_url checksum_url asset_name
  archive_url=$(printf '%s\n' "$picked" | sed -n '1p')
  checksum_url=$(printf '%s\n' "$picked" | sed -n '2p')
  asset_name=$(printf '%s\n' "$picked" | sed -n '3p')
  if ! curl -fL --retry 3 -H "User-Agent: PixelPeeker-linux-check" -o "$dest/$asset_name" "$archive_url"; then
    record FAIL "BtbN download" "Download failed: $archive_url"
    return
  fi
  if ! curl -fsSL --retry 3 -H "User-Agent: PixelPeeker-linux-check" -o "$dest/checksums.sha256" "$checksum_url"; then
    record FAIL "BtbN checksum" "Could not download checksums.sha256"
    return
  fi
  local expected actual
  expected=$(awk -v name="$asset_name" '
    $1 ~ /^[A-Fa-f0-9]{64}$/ {
      file=$2
      sub(/^\*/, "", file)
      if (file == name) { print toupper($1); exit }
    }
  ' "$dest/checksums.sha256")
  if [[ -z "$expected" ]]; then
    record FAIL "BtbN checksum" "checksums.sha256 has no line for $asset_name"
    return
  fi
  actual=$(sha256sum "$dest/$asset_name" | awk '{ print toupper($1) }')
  if [[ "$actual" != "$expected" ]]; then
    record FAIL "BtbN checksum" "SHA-256 mismatch for $asset_name"
    return
  fi
  record PASS "BtbN checksum" "$asset_name matches SHA-256 $expected"
  local extracted="$dest/extracted"
  mkdir -p "$extracted"
  if ! tar -xJf "$dest/$asset_name" -C "$extracted"; then
    record FAIL "BtbN extract" "tar could not unpack $asset_name (xz support required)"
    suggest "sudo dnf install xz tar"
    return
  fi
  local ffmpeg ffprobe
  ffmpeg=$(find "$extracted" -type f -name ffmpeg -print -quit)
  ffprobe=$(find "$extracted" -type f -name ffprobe -print -quit)
  if [[ -z "$ffmpeg" || -z "$ffprobe" ]]; then
    record FAIL "BtbN extract" "The archive did not contain ffmpeg and ffprobe"
    return
  fi
  chmod +x "$ffmpeg" "$ffprobe"
  local version_out encode_out probe_out
  version_out=$("$ffmpeg" -version 2>&1 | head -n 1 || true)
  local sample="$dest/out.mp4"
  if ! "$ffmpeg" -y -hide_banner -loglevel error -f lavfi -i "testsrc=duration=1:size=320x240:rate=10" -c:v libx264 "$sample" >"$dest/encode.log" 2>&1; then
    cat "$dest/encode.log" >>"$LOG"
    record FAIL "BtbN encode" "ffmpeg -version: $version_out. The one-second testsrc encode failed."
    return
  fi
  encode_out=$(wc -c <"$sample" | tr -d ' ')
  if ! probe_out=$("$ffprobe" -v error -show_entries format=duration -of csv=p=0 "$sample" 2>&1); then
    record FAIL "BtbN ffprobe" "ffprobe could not read the test encode"
    return
  fi
  record PASS "BtbN ffmpeg" "$version_out; wrote ${encode_out} bytes; ffprobe duration ${probe_out}"
}

print_summary() {
  local row status name detail color
  say ""
  say "Summary"
  for row in "${RESULTS[@]}"; do
    status="${row%%$'\t'*}"
    row="${row#*$'\t'}"
    name="${row%%$'\t'*}"
    detail="${row#*$'\t'}"
    case "$status" in
      PASS) color="$C_GREEN" ;;
      WARN) color="$C_YELLOW" ;;
      FAIL) color="$C_RED" ;;
    esac
    log_line "$status  $name — $detail"
    printf '%s%-4s%s  %s — %s\n' "$color" "$status" "$C_RESET" "$name" "$detail"
  done
  say ""
  say "Log: $LOG"
  say "FAIL=$FAILS  WARN=$WARNS"
}

main() {
  RESULTS=()
  parse_args "$@"
  : >"$LOG"
  WORK=$(mktemp -d "${TMPDIR:-/tmp}/pixelpeeker-linux-check.XXXXXX")
  log_line "PixelPeeker Linux check"
  log_line "Work directory: $WORK"

  local pretty arch
  pretty=$(os_pretty)
  arch=$(uname -m)
  record PASS "Distribution" "$pretty"
  local rid token
  case "$arch" in
    x86_64) rid="linux-x64"; token="linux64" ;;
    aarch64|arm64) rid="linux-arm64"; token="linuxarm64" ;;
    *)
      rid=""
      token=""
      record FAIL "Architecture" "$arch is not linux-x64 or linux-arm64"
      ;;
  esac
  if [[ -n "$rid" ]]; then
    record PASS "Architecture" "$arch ($rid)"
  fi

  local ldd_text getconf_text linker_text version kind
  ldd_text=$(ldd --version 2>&1 || true)
  getconf_text=$(getconf GNU_LIBC_VERSION 2>/dev/null || true)
  linker_text=$(ldd /proc/self/exe 2>&1 || true)
  kind="unknown"
  version=""
  if os_release_is_alpine || text_mentions_musl "$ldd_text" || text_mentions_musl "$getconf_text" || text_mentions_musl "$linker_text"; then
    kind="musl"
  else
    version=$(extract_glibc "$ldd_text" || true)
    if [[ -z "$version" ]]; then
      version=$(extract_glibc "$getconf_text" || true)
    fi
    if [[ -n "$version" ]]; then
      kind="glibc"
    fi
  fi
  if [[ "$kind" == "glibc" ]] && glibc_at_least_228 "$version"; then
    record PASS "libc" "glibc $version (static FFmpeg builds require >= 2.28)"
  elif [[ "$kind" == "glibc" ]]; then
    record WARN "libc" "glibc $version is older than 2.28. PixelPeeker will not download the static BtbN or John Van Sickle builds."
    suggest "sudo dnf install ffmpeg"
    say "  Fedora: ffmpeg-free is in the Fedora repos. The full ffmpeg package is in RPM Fusion (rpmfusion-free)."
  elif [[ "$kind" == "musl" ]]; then
    record WARN "libc" "musl detected. Static glibc FFmpeg builds are not downloaded. Install the distro ffmpeg package."
    suggest "sudo dnf install ffmpeg"
  else
    record WARN "libc" "Could not confirm glibc >= 2.28. The static FFmpeg download will be skipped by the app."
    suggest "sudo dnf install ffmpeg"
  fi

  local session="${XDG_SESSION_TYPE:-unset}"
  if [[ -n "${DISPLAY:-}" || -n "${WAYLAND_DISPLAY:-}" ]]; then
    record PASS "Session" "type=$session DISPLAY=${DISPLAY:-unset} WAYLAND_DISPLAY=${WAYLAND_DISPLAY:-unset}"
  else
    record FAIL "Session" "Neither DISPLAY nor WAYLAND_DISPLAY is set (XDG_SESSION_TYPE=$session). Avalonia needs a Wayland or X11 session."
  fi

  local required=(
    libX11.so.6 libICE.so.6 libSM.so.6 libfontconfig.so.1 libfreetype.so.6
    libXext.so.6 libXcursor.so.1 libXi.so.6 libXrandr.so.2 libGL.so.1 libxkbcommon.so.0
  )
  local missing_libs=() missing_packages="" soname pkg
  for soname in "${required[@]}"; do
    if ! lib_present "$soname"; then
      missing_libs+=("$soname")
      pkg=$(fedora_package_for "$soname")
      missing_packages="$missing_packages ${pkg:-$soname}"
    fi
  done
  if [[ ${#missing_libs[@]} -eq 0 ]]; then
    record PASS "Avalonia libraries" "libX11, libICE, libSM, fontconfig, freetype, X extensions, libGL and libxkbcommon are present"
  else
    record FAIL "Avalonia libraries" "Missing ${missing_libs[*]}"
    suggest "sudo dnf install${missing_packages}"
  fi

  local build_from_source=1
  if [[ -n "$APPIMAGE" || -n "$TARBALL" ]]; then
    build_from_source=0
  fi

  local sdk_version sdk_roll installed_sdk
  read -r sdk_version sdk_roll <<<"$(sdk_requirement)"
  if [[ "$build_from_source" -eq 1 ]]; then
    if ! command -v dotnet >/dev/null 2>&1; then
      record FAIL ".NET SDK" "dotnet is not on PATH. global.json asks for $sdk_version ($sdk_roll)."
      suggest "sudo dnf install dotnet-sdk-${sdk_version%.*}"
    else
      installed_sdk=$(dotnet --version 2>/dev/null || true)
      if sdk_satisfies "$installed_sdk" "$sdk_version" "$sdk_roll"; then
        record PASS ".NET SDK" "$installed_sdk satisfies global.json $sdk_version ($sdk_roll)"
      else
        record FAIL ".NET SDK" "Found $installed_sdk, need $sdk_version with rollForward $sdk_roll"
        suggest "sudo dnf install dotnet-sdk-${sdk_version%.*}"
      fi
    fi
  else
    record PASS ".NET SDK" "Not required for a prebuilt --appimage or --tarball"
  fi

  BIN_PATH=""
  LAUNCH=()
  if [[ "$build_from_source" -eq 1 ]]; then
    if [[ ! -f "$ROOT/FFmpegStudio.Linux.slnf" ]]; then
      record FAIL "Source tree" "FFmpegStudio.Linux.slnf was not found next to scripts/. Run this from a checkout."
    elif [[ "$SKIP_TESTS" -eq 1 ]]; then
      record WARN "dotnet test" "Skipped because --skip-tests was set"
    elif command -v dotnet >/dev/null 2>&1; then
      say "Running dotnet test FFmpegStudio.Linux.slnf"
      if run_logged "$WORK/test.log" dotnet test "$ROOT/FFmpegStudio.Linux.slnf" -c Release --nologo; then
        record PASS "dotnet test" "FFmpegStudio.Linux.slnf passed"
      else
        record FAIL "dotnet test" "FFmpegStudio.Linux.slnf failed. See $LOG"
      fi
    fi
    if [[ -n "$rid" ]] && command -v dotnet >/dev/null 2>&1 && [[ -f "$ROOT/src/FFmpegStudio.Avalonia/FFmpegStudio.Avalonia.csproj" ]]; then
      local publish="$WORK/publish"
      say "Publishing self-contained $rid"
      if run_logged "$WORK/publish.log" dotnet publish "$ROOT/src/FFmpegStudio.Avalonia/FFmpegStudio.Avalonia.csproj" \
          -c Release -r "$rid" --self-contained true -o "$publish" \
          -p:PublishSingleFile=false -p:UseAppHost=true --nologo; then
        BIN_PATH=$(find_binary "$publish")
        if [[ -n "$BIN_PATH" ]]; then
          chmod +x "$BIN_PATH"
          LAUNCH=("$BIN_PATH")
          record PASS "Publish" "$BIN_PATH"
        else
          record FAIL "Publish" "Publish succeeded but PixelPeeker.Avalonia was not found"
        fi
      else
        record FAIL "Publish" "dotnet publish failed. See $LOG"
      fi
    fi
  elif [[ -n "$APPIMAGE" ]]; then
    if [[ ! -f "$APPIMAGE" ]]; then
      record FAIL "AppImage" "File not found: $APPIMAGE"
    else
      if prepare_appimage "$APPIMAGE" "$WORK/appimage"; then
        record PASS "AppImage" "$APPIMAGE"
      fi
    fi
  else
    if [[ ! -f "$TARBALL" ]]; then
      record FAIL "Tarball" "File not found: $TARBALL"
    else
      mkdir -p "$WORK/tarball"
      if tar -xzf "$TARBALL" -C "$WORK/tarball"; then
        BIN_PATH=$(find_binary "$WORK/tarball")
        if [[ -n "$BIN_PATH" ]]; then
          chmod +x "$BIN_PATH"
          LAUNCH=("$BIN_PATH")
          record PASS "Tarball" "$BIN_PATH"
        else
          record FAIL "Tarball" "PixelPeeker.Avalonia was not inside $TARBALL"
        fi
      else
        record FAIL "Tarball" "Could not extract $TARBALL"
      fi
    fi
  fi

  if [[ -n "$BIN_PATH" && -f "$BIN_PATH" ]]; then
    check_ldd "$BIN_PATH"
  fi

  if [[ ${#LAUNCH[@]} -gt 0 && -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]]; then
    smoke_init "$WORK/smoke-data" "$WORK/smoke.out" "${LAUNCH[@]}"
    smoke_window "$WORK/gui-data" "$WORK/gui.out" "${LAUNCH[@]}"
  elif [[ ${#LAUNCH[@]} -gt 0 ]]; then
    record FAIL "--smoke" "Skipped because no Wayland or X11 display is available"
  fi

  local ffmpeg_path ffprobe_path
  ffmpeg_path=$(command -v ffmpeg 2>/dev/null || true)
  ffprobe_path=$(command -v ffprobe 2>/dev/null || true)
  if [[ -n "$ffmpeg_path" && -n "$ffprobe_path" ]]; then
    local ffmpeg_ver ffprobe_ver
    ffmpeg_ver=$("$ffmpeg_path" -version 2>&1 | head -n 1 || true)
    ffprobe_ver=$("$ffprobe_path" -version 2>&1 | head -n 1 || true)
    record PASS "System FFmpeg" "$ffmpeg_path — $ffmpeg_ver"
    record PASS "System ffprobe" "$ffprobe_path — $ffprobe_ver"
  else
    record WARN "System FFmpeg" "ffmpeg and ffprobe were not both on PATH. The app can still download a static build on glibc >= 2.28."
    suggest "sudo dnf install ffmpeg"
    say "  Fedora: install ffmpeg-free from the Fedora repos, or enable RPM Fusion and install ffmpeg."
  fi

  if ldconfig -p 2>/dev/null | grep -E 'libvlc\.so' >/dev/null; then
    local vlc_line
    vlc_line=$(ldconfig -p 2>/dev/null | grep -E 'libvlc\.so' | head -n 1 | sed 's/^[[:space:]]*//')
    record PASS "libvlc" "$vlc_line"
  else
    record WARN "libvlc" "libvlc.so was not found. Playback needs the distro library; the editor still starts."
    suggest "sudo dnf install vlc-libs"
    say "  Fedora: vlc-libs comes from RPM Fusion (rpmfusion-free), not the base Fedora repos."
  fi

  if [[ "$DOWNLOAD_FFMPEG" -eq 1 ]]; then
    if [[ "$kind" == "glibc" && -n "$version" && -n "$token" ]] && glibc_at_least_228 "$version"; then
      download_btbn "$WORK/ffmpeg" "$token"
    else
      record FAIL "BtbN download" "Refused, matching the app: static builds are only downloaded for glibc >= 2.28 on x64 or arm64 (detected $kind ${version:-unknown})."
      suggest "sudo dnf install ffmpeg"
    fi
  fi

  print_summary
  if [[ "$FAILS" -gt 0 ]]; then
    exit 1
  fi
}

RESULTS=()
main "$@"
