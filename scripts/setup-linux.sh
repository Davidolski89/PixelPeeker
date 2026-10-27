#!/usr/bin/env bash
# Install PixelPeeker on a Linux machine and publish a self-contained Avalonia build.
# This file is meant to run on its own, including:
#   curl -fsSL https://raw.githubusercontent.com/Davidolski89/PixelPeeker/main/scripts/setup-linux.sh | bash
# It never enables RPM Fusion unless --enable-rpmfusion is passed.
if [[ -z "${BASH_VERSION:-}" ]]; then
  printf '%s\n' "Run this script with bash." >&2
  exit 1
fi
set -euo pipefail
set -o errtrace

REPO_URL="https://github.com/Davidolski89/PixelPeeker.git"
DIR="${HOME:-}/PixelPeeker"
BRANCH="main"
LOG="${PWD}/pixelpeeker-linux-setup.log"
YES=0
NO_INSTALL=0
DO_TEST=0
DO_RUN=0
DO_DESKTOP=0
ENABLE_RPMFUSION=0

PM="unknown"
PRETTY="unknown"
ARCH=""
RID=""
ARCH_NAME=""
SDK_VERSION="10.0.100"
SDK_ROLL="latestFeature"
LIBVLC_STATE="unavailable"
REQUIRED_ARR=()
OPTIONAL_ARR=()
RESULTS=()
FAILS=0
WARNS=0
FINISHED=0
UPDATED=0
TMP=""
BIN=""
DOTNET_HINT=0
SUDO=()
PREPARED_SUDO=0

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
Usage: setup-linux.sh [options]

Clone PixelPeeker, install the Linux build dependencies, and publish a
self-contained Avalonia build for this machine. Primary target: Fedora 44
x86_64 on KDE/Wayland. Debian, Ubuntu, Arch and openSUSE are also handled.

The script asks once before it installs packages. It uses sudo for the
package manager and does not enable RPM Fusion unless --enable-rpmfusion
is set. ffmpeg is optional: the app can download a static build on glibc
2.28 or newer. On Fedora the default package is ffmpeg-free.

Standalone:
  curl -fsSL https://raw.githubusercontent.com/Davidolski89/PixelPeeker/main/scripts/setup-linux.sh | bash
  curl -fsSL https://raw.githubusercontent.com/Davidolski89/PixelPeeker/main/scripts/setup-linux.sh | bash -s -- --yes

From a downloaded copy:
  bash setup-linux.sh
  bash setup-linux.sh --yes --test --desktop-entry

Options:
  --dir PATH           Checkout directory (default: ~/PixelPeeker)
  --branch NAME        Git branch to clone or fast-forward (default: main)
  --yes                Install without the confirmation prompt
  --no-install         Only report missing packages; do not install them
  --test               Run dotnet test FFmpegStudio.Linux.slnf before publish
  --run                Launch the published binary when a display is set
  --desktop-entry      Install a user .desktop file and icon for the menu
  --enable-rpmfusion   On Fedora, enable RPM Fusion Free if vlc-libs is not
                       already in the enabled repositories, then install it
  --log PATH           Full log (default: ./pixelpeeker-linux-setup.log)
  --help               Show this help

The publish directory matches Publish-Linux.ps1:
  <dir>/artifacts/portable-linux-x64      (RID linux-x64)
  <dir>/artifacts/portable-linux-arm64    (RID linux-arm64)

Exit status is 1 when a required step fails. WARN does not fail the script.
EOF
}

log_line() {
  if [[ -n "${LOG:-}" ]]; then
    printf '%s\n' "$*" >>"$LOG"
  fi
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

expand_user_path() {
  local path="$1"
  if [[ "$path" == "~" ]]; then
    printf '%s' "${HOME:-}"
  elif [[ "$path" == \~/* ]]; then
    printf '%s' "${HOME:-}/${path:2}"
  else
    printf '%s' "$path"
  fi
}

make_absolute() {
  local path="$1"
  case "$path" in
    /*) printf '%s' "$path" ;;
    *) printf '%s' "$PWD/$path" ;;
  esac
}

# Invoked from the EXIT/ERR traps; shellcheck does not see those references.
# shellcheck disable=SC2317
cleanup() {
  if [[ -n "${TMP:-}" && -d "$TMP" ]]; then
    rm -rf "$TMP"
  fi
}

# shellcheck disable=SC2317
on_err() {
  local line="$1"
  if [[ "$FINISHED" -eq 1 ]]; then
    exit 1
  fi
  FINISHED=1
  record FAIL "Setup" "The script stopped at line $line"
  print_summary
  exit 1
}

print_summary() {
  local row status name detail color
  say ""
  say "Summary"
  if [[ ${#RESULTS[@]} -eq 0 ]]; then
    say "No checks were recorded."
  else
    for row in "${RESULTS[@]}"; do
      status="${row%%$'\t'*}"
      row="${row#*$'\t'}"
      name="${row%%$'\t'*}"
      detail="${row#*$'\t'}"
      case "$status" in
        PASS) color="$C_GREEN" ;;
        WARN) color="$C_YELLOW" ;;
        FAIL) color="$C_RED" ;;
        *) color="" ;;
      esac
      log_line "$status  $name — $detail"
      printf '%s%-4s%s  %s — %s\n' "$color" "$status" "$C_RESET" "$name" "$detail"
    done
  fi
  say ""
  if [[ -n "$BIN" && -f "$BIN" ]]; then
    say "Start PixelPeeker:"
    say "  $BIN"
  fi
  if [[ "$DO_DESKTOP" -eq 1 && -f "${HOME}/.local/share/applications/pixelpeeker.desktop" ]]; then
    say "Application menu:"
    say "  ${HOME}/.local/share/applications/pixelpeeker.desktop"
  fi
  if [[ "$DOTNET_HINT" -eq 1 ]]; then
    say "Add the user-local .NET SDK to new shells:"
    say "  export DOTNET_ROOT=\"\$HOME/.dotnet\""
    say "  export PATH=\"\$DOTNET_ROOT:\$PATH\""
  fi
  say "Log: $LOG"
  say "FAIL=$FAILS  WARN=$WARNS"
}

finish() {
  local code="$1"
  if [[ "$FINISHED" -eq 0 ]]; then
    FINISHED=1
    print_summary
  fi
  exit "$code"
}

parse_args() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --help|-h) usage; exit 0 ;;
      --dir)
        [[ $# -ge 2 ]] || die_usage "--dir needs a path"
        DIR="$2"
        shift 2
        ;;
      --branch)
        [[ $# -ge 2 ]] || die_usage "--branch needs a name"
        BRANCH="$2"
        shift 2
        ;;
      --yes) YES=1; shift ;;
      --no-install) NO_INSTALL=1; shift ;;
      --test) DO_TEST=1; shift ;;
      --run) DO_RUN=1; shift ;;
      --desktop-entry) DO_DESKTOP=1; shift ;;
      --enable-rpmfusion) ENABLE_RPMFUSION=1; shift ;;
      --log)
        [[ $# -ge 2 ]] || die_usage "--log needs a path"
        LOG="$2"
        shift 2
        ;;
      *) die_usage "Unknown option: $1" ;;
    esac
  done
  if [[ "$YES" -eq 1 && "$NO_INSTALL" -eq 1 ]]; then
    die_usage "Pass only one of --yes and --no-install"
  fi
  if [[ -z "${HOME:-}" ]]; then
    die_usage "HOME is not set"
  fi
  if [[ ! "$BRANCH" =~ ^[A-Za-z0-9._/-]+$ || "$BRANCH" == *..* || "$BRANCH" == -* ]]; then
    die_usage "Invalid branch name: $BRANCH"
  fi
  DIR=$(expand_user_path "$DIR")
  DIR=$(make_absolute "$DIR")
  LOG=$(expand_user_path "$LOG")
  LOG=$(make_absolute "$LOG")
}

os_id() {
  local id=""
  if [[ -f /etc/os-release ]]; then
    # shellcheck disable=SC1091
    id=$(. /etc/os-release && printf '%s' "${ID:-}")
  fi
  printf '%s' "$id"
}

os_id_like() {
  local like=""
  if [[ -f /etc/os-release ]]; then
    # shellcheck disable=SC1091
    like=$(. /etc/os-release && printf '%s' "${ID_LIKE:-}")
  fi
  printf '%s' "$like"
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

detect_pm() {
  local id like
  id=$(os_id)
  like=$(os_id_like)
  case "$id" in
    fedora|rhel|centos|almalinux|rocky|nobara|amzn|ol) PM=dnf ;;
    debian|ubuntu|linuxmint|pop|raspbian|neon) PM=apt ;;
    arch|manjaro|endeavouros|garuda) PM=pacman ;;
    opensuse*|sles|sled) PM=zypper ;;
    *)
      case "$like" in
        *fedora*|*rhel*) PM=dnf ;;
        *debian*|*ubuntu*) PM=apt ;;
        *arch*) PM=pacman ;;
        *suse*) PM=zypper ;;
        *) PM=unknown ;;
      esac
      ;;
  esac
}

detect_arch() {
  ARCH=$(uname -m)
  case "$ARCH" in
    x86_64) RID="linux-x64"; ARCH_NAME="x64" ;;
    aarch64|arm64) RID="linux-arm64"; ARCH_NAME="arm64" ;;
    *) RID=""; ARCH_NAME="" ;;
  esac
}

add_unique() {
  local bucket="$1"
  local pkg="$2"
  local existing
  [[ -n "$pkg" ]] || return 0
  if [[ "$bucket" == required ]]; then
    for existing in "${REQUIRED_ARR[@]+"${REQUIRED_ARR[@]}"}"; do
      [[ "$existing" == "$pkg" ]] && return 0
    done
    REQUIRED_ARR+=("$pkg")
  else
    for existing in "${OPTIONAL_ARR[@]+"${OPTIONAL_ARR[@]}"}"; do
      [[ "$existing" == "$pkg" ]] && return 0
    done
    OPTIONAL_ARR+=("$pkg")
  fi
}

lib_present() {
  local soname="$1"
  local dir
  if command -v ldconfig >/dev/null 2>&1; then
    if ldconfig -p 2>/dev/null | grep -F "$soname" >/dev/null; then
      return 0
    fi
  fi
  for dir in /usr/lib64 /usr/lib /lib64 /lib \
    /usr/lib/x86_64-linux-gnu /usr/lib/aarch64-linux-gnu \
    /lib/x86_64-linux-gnu /lib/aarch64-linux-gnu; do
    if [[ -e "$dir/$soname" ]]; then
      return 0
    fi
  done
  return 1
}

libvlc_present() {
  lib_present "libvlc.so.5" && return 0
  lib_present "libvlc.so" && return 0
  if command -v ldconfig >/dev/null 2>&1; then
    ldconfig -p 2>/dev/null | grep -F "libvlc.so" >/dev/null && return 0
  fi
  return 1
}

icu_present() {
  if command -v ldconfig >/dev/null 2>&1; then
    ldconfig -p 2>/dev/null | grep -E 'libicuuc\.so' >/dev/null && return 0
  fi
  return 1
}

# Fedora soname packages match scripts/test-linux.sh.
package_for_lib() {
  local soname="$1"
  case "$PM" in
    dnf)
      case "$soname" in
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
        libz.so.1) printf '%s' zlib-ng-compat ;;
        libssl.so.3) printf '%s' openssl-libs ;;
        libgssapi_krb5.so.2) printf '%s' krb5-libs ;;
        libfuse.so.2) printf '%s' fuse-libs ;;
        libwayland-client.so.0) printf '%s' libwayland-client ;;
        libwayland-cursor.so.0) printf '%s' libwayland-cursor ;;
        *) printf '%s' "" ;;
      esac
      ;;
    apt)
      case "$soname" in
        libX11.so.6) printf '%s' libx11-6 ;;
        libICE.so.6) printf '%s' libice6 ;;
        libSM.so.6) printf '%s' libsm6 ;;
        libfontconfig.so.1) printf '%s' libfontconfig1 ;;
        libfreetype.so.6) printf '%s' libfreetype6 ;;
        libXext.so.6) printf '%s' libxext6 ;;
        libXcursor.so.1) printf '%s' libxcursor1 ;;
        libXi.so.6) printf '%s' libxi6 ;;
        libXrandr.so.2) printf '%s' libxrandr2 ;;
        libGL.so.1) printf '%s' libgl1 ;;
        libxkbcommon.so.0) printf '%s' libxkbcommon0 ;;
        libstdc++.so.6) printf '%s' libstdc++6 ;;
        libgcc_s.so.1) printf '%s' libgcc-s1 ;;
        libz.so.1) printf '%s' zlib1g ;;
        libssl.so.3) printf '%s' libssl3 ;;
        libgssapi_krb5.so.2) printf '%s' libgssapi-krb5-2 ;;
        libfuse.so.2) printf '%s' libfuse2 ;;
        libwayland-client.so.0) printf '%s' libwayland-client0 ;;
        libwayland-cursor.so.0) printf '%s' libwayland-cursor0 ;;
        *) printf '%s' "" ;;
      esac
      ;;
    pacman)
      case "$soname" in
        libX11.so.6) printf '%s' libx11 ;;
        libICE.so.6) printf '%s' libice ;;
        libSM.so.6) printf '%s' libsm ;;
        libfontconfig.so.1) printf '%s' fontconfig ;;
        libfreetype.so.6) printf '%s' freetype2 ;;
        libXext.so.6) printf '%s' libxext ;;
        libXcursor.so.1) printf '%s' libxcursor ;;
        libXi.so.6) printf '%s' libxi ;;
        libXrandr.so.2) printf '%s' libxrandr ;;
        libGL.so.1) printf '%s' mesa ;;
        libxkbcommon.so.0) printf '%s' libxkbcommon ;;
        libstdc++.so.6) printf '%s' gcc-libs ;;
        libgcc_s.so.1) printf '%s' libgcc ;;
        libz.so.1) printf '%s' zlib ;;
        libssl.so.3) printf '%s' openssl ;;
        libgssapi_krb5.so.2) printf '%s' krb5 ;;
        libfuse.so.2) printf '%s' fuse2 ;;
        libwayland-client.so.0|libwayland-cursor.so.0) printf '%s' wayland ;;
        *) printf '%s' "" ;;
      esac
      ;;
    zypper)
      case "$soname" in
        libX11.so.6) printf '%s' libX11-6 ;;
        libICE.so.6) printf '%s' libICE6 ;;
        libSM.so.6) printf '%s' libSM6 ;;
        libfontconfig.so.1) printf '%s' libfontconfig1 ;;
        libfreetype.so.6) printf '%s' libfreetype6 ;;
        libXext.so.6) printf '%s' libXext6 ;;
        libXcursor.so.1) printf '%s' libXcursor1 ;;
        libXi.so.6) printf '%s' libXi6 ;;
        libXrandr.so.2) printf '%s' libXrandr2 ;;
        libGL.so.1) printf '%s' Mesa-libGL1 ;;
        libxkbcommon.so.0) printf '%s' libxkbcommon0 ;;
        libstdc++.so.6) printf '%s' libstdc++6 ;;
        libgcc_s.so.1) printf '%s' libgcc_s1 ;;
        libz.so.1) printf '%s' zlib ;;
        libssl.so.3) printf '%s' libopenssl3 ;;
        libgssapi_krb5.so.2) printf '%s' krb5 ;;
        libfuse.so.2) printf '%s' libfuse2 ;;
        libwayland-client.so.0) printf '%s' libwayland-client0 ;;
        libwayland-cursor.so.0) printf '%s' libwayland-cursor0 ;;
        *) printf '%s' "" ;;
      esac
      ;;
    *)
      printf '%s' ""
      ;;
  esac
}

ffmpeg_package() {
  case "$PM" in
    dnf) printf '%s' ffmpeg-free ;;
    apt|pacman|zypper) printf '%s' ffmpeg ;;
    *) printf '%s' "" ;;
  esac
}

icu_package() {
  local found=""
  case "$PM" in
    dnf) printf '%s' libicu ;;
    pacman) printf '%s' icu ;;
    zypper) printf '%s' libicu ;;
    apt)
      found=$(apt-cache search --names-only '^libicu[0-9]+$' 2>/dev/null | awk '{print $1}' | sort -V | tail -n 1 || true)
      printf '%s' "$found"
      ;;
    *) printf '%s' "" ;;
  esac
}

package_available() {
  local pkg="$1"
  local out
  case "$PM" in
    dnf)
      dnf -q --setopt=timeout=60 info "$pkg" >/dev/null 2>&1
      ;;
    apt)
      out=$(apt-get install -s -y "$pkg" 2>&1) || return 1
      grep -qE '^(Inst |Conf )|already the newest version' <<<"$out"
      ;;
    pacman)
      pacman -Si "$pkg" >/dev/null 2>&1 || pacman -Qi "$pkg" >/dev/null 2>&1
      ;;
    zypper)
      zypper --non-interactive info "$pkg" >/dev/null 2>&1
      ;;
    *)
      return 1
      ;;
  esac
}

first_available() {
  local candidate
  for candidate in "$@"; do
    if package_available "$candidate"; then
      printf '%s' "$candidate"
      return 0
    fi
  done
  return 1
}

require_cmd() {
  local cmd="$1"
  local pkg="$2"
  if command -v "$cmd" >/dev/null 2>&1; then
    return 0
  fi
  if [[ -n "$pkg" ]]; then
    add_unique required "$pkg"
  else
    record FAIL "$cmd" "Missing and this distribution has no package mapping"
  fi
}

require_lib() {
  local soname="$1"
  local pkg
  if lib_present "$soname"; then
    return 0
  fi
  pkg=$(package_for_lib "$soname")
  if [[ -n "$pkg" ]]; then
    add_unique required "$pkg"
  else
    record FAIL "$soname" "Missing and this distribution has no package mapping"
  fi
}

optional_lib() {
  local soname="$1"
  local pkg
  if lib_present "$soname"; then
    return 0
  fi
  pkg=$(package_for_lib "$soname")
  if [[ -n "$pkg" ]]; then
    add_unique optional "$pkg"
  fi
}

plan_packages() {
  local ffmpeg_pkg icu_pkg
  require_cmd git git
  require_cmd curl curl
  if [[ ! -e /etc/ssl/certs/ca-certificates.crt && ! -e /etc/pki/tls/certs/ca-bundle.crt ]]; then
    add_unique required ca-certificates
  fi
  require_lib libX11.so.6
  require_lib libICE.so.6
  require_lib libSM.so.6
  require_lib libfontconfig.so.1
  require_lib libfreetype.so.6
  require_lib libXext.so.6
  require_lib libXcursor.so.1
  require_lib libXi.so.6
  require_lib libXrandr.so.2
  require_lib libGL.so.1
  require_lib libxkbcommon.so.0
  require_lib libstdc++.so.6
  require_lib libgcc_s.so.1
  require_lib libz.so.1
  require_lib libssl.so.3
  require_lib libgssapi_krb5.so.2
  if ! icu_present; then
    icu_pkg=$(icu_package)
    if [[ -n "$icu_pkg" ]]; then
      add_unique required "$icu_pkg"
    else
      record FAIL "libicu" "Missing and this distribution has no ICU package mapping"
    fi
  fi
  optional_lib libwayland-client.so.0
  optional_lib libwayland-cursor.so.0
  optional_lib libfuse.so.2
  if command -v ffmpeg >/dev/null 2>&1 && command -v ffprobe >/dev/null 2>&1; then
    :
  else
    ffmpeg_pkg=$(ffmpeg_package)
    if [[ -n "$ffmpeg_pkg" ]]; then
      add_unique optional "$ffmpeg_pkg"
    fi
  fi
  plan_libvlc
}

plan_libvlc() {
  local pkg=""
  if libvlc_present; then
    LIBVLC_STATE=present
    return 0
  fi
  case "$PM" in
    dnf)
      if pkg=$(first_available vlc-libs vlc); then
        LIBVLC_STATE=package
        add_unique optional "$pkg"
      elif [[ "$ENABLE_RPMFUSION" -eq 1 ]]; then
        LIBVLC_STATE=rpmfusion
      else
        LIBVLC_STATE=instructions
      fi
      ;;
    apt)
      if pkg=$(first_available libvlc5); then
        LIBVLC_STATE=package
        add_unique optional "$pkg"
        if pkg=$(first_available vlc-plugin-base); then
          add_unique optional "$pkg"
        fi
      else
        LIBVLC_STATE=unavailable
      fi
      ;;
    pacman)
      LIBVLC_STATE=package
      add_unique optional vlc
      ;;
    zypper)
      if pkg=$(first_available libvlc5 vlc); then
        LIBVLC_STATE=package
        add_unique optional "$pkg"
      else
        LIBVLC_STATE=unavailable
      fi
      ;;
    *)
      LIBVLC_STATE=unavailable
      ;;
  esac
}

join_by_space() {
  local item
  if [[ $# -eq 0 ]]; then
    printf '%s' "(none)"
    return 0
  fi
  printf '%s' "$1"
  shift
  for item in "$@"; do
    printf ' %s' "$item"
  done
}

print_plan() {
  local sdk_pkg
  sdk_pkg="dotnet-sdk-${SDK_VERSION%.*}"
  say "Distribution: $PRETTY"
  say "Package manager: $PM"
  say "Architecture: $ARCH (${RID:-unsupported})"
  say "Directory: $DIR"
  say "Branch: $BRANCH"
  say "Required packages: $(join_by_space "${REQUIRED_ARR[@]+"${REQUIRED_ARR[@]}"}")"
  say "Optional packages: $(join_by_space "${OPTIONAL_ARR[@]+"${OPTIONAL_ARR[@]}"}")"
  if sdk_satisfies; then
    say ".NET SDK: already satisfies $SDK_ROLL >= $SDK_VERSION"
  else
    say ".NET SDK: install $sdk_pkg when the repositories have it, otherwise Microsoft's dotnet-install.sh into ~/.dotnet"
  fi
  case "$LIBVLC_STATE" in
    present) say "libvlc: already installed" ;;
    package) say "libvlc: included in the optional packages" ;;
    rpmfusion) say "libvlc: --enable-rpmfusion will enable RPM Fusion Free and install vlc-libs" ;;
    instructions) say "libvlc: not in the enabled Fedora repositories. RPM Fusion is not enabled automatically." ;;
    *) say "libvlc: no distro package will be installed" ;;
  esac
  if [[ "$PM" == dnf ]]; then
    say "FFmpeg: ffmpeg-free is the Fedora package. The app can also download FFmpeg. RPM Fusion's ffmpeg package is optional and is not installed by this script."
  else
    say "FFmpeg: the distro ffmpeg package is optional. The app can also download a static build on glibc >= 2.28."
  fi
}

needs_install_action() {
  if [[ ${#REQUIRED_ARR[@]} -gt 0 || ${#OPTIONAL_ARR[@]} -gt 0 ]]; then
    return 0
  fi
  if ! sdk_satisfies; then
    return 0
  fi
  if [[ "$LIBVLC_STATE" == rpmfusion ]]; then
    return 0
  fi
  return 1
}

confirm_install() {
  local answer
  if ! needs_install_action; then
    say "Nothing to install."
    return 0
  fi
  if [[ "$NO_INSTALL" -eq 1 ]]; then
    say "--no-install: packages were not installed."
    return 1
  fi
  if [[ "$YES" -eq 1 ]]; then
    return 0
  fi
  if [[ ! -r /dev/tty ]]; then
    say "No terminal is available to confirm installation. Re-run with --yes, or run bash setup-linux.sh in a terminal."
    return 1
  fi
  printf '%s' "Install these packages? [y/N] " >/dev/tty
  IFS= read -r answer </dev/tty || answer=""
  log_line "Confirmation answer: ${answer:-<empty>}"
  case "$answer" in
    y|Y|yes|YES) return 0 ;;
    *) return 1 ;;
  esac
}

run_logged() {
  local status
  set +e
  "$@" 2>&1 | tee -a "$LOG"
  status=${PIPESTATUS[0]}
  set -e
  return "$status"
}

prepare_sudo() {
  if [[ "$PREPARED_SUDO" -eq 1 ]]; then
    return 0
  fi
  if [[ "$(id -u)" -eq 0 ]]; then
    SUDO=()
    PREPARED_SUDO=1
    return 0
  fi
  if ! command -v sudo >/dev/null 2>&1; then
    record FAIL "sudo" "Package installation needs sudo, or run this script as root."
    return 1
  fi
  SUDO=(sudo)
  say "Requesting sudo credentials once."
  "${SUDO[@]}" -v || return 1
  PREPARED_SUDO=1
}

pm_update() {
  if [[ "$UPDATED" -eq 1 ]]; then
    return 0
  fi
  case "$PM" in
    apt)
      run_logged env DEBIAN_FRONTEND=noninteractive NEEDRESTART_MODE=a "${SUDO[@]}" apt-get update || return 1
      ;;
    zypper)
      run_logged "${SUDO[@]}" zypper --non-interactive refresh || return 1
      ;;
    *)
      ;;
  esac
  UPDATED=1
}

pm_install() {
  [[ $# -gt 0 ]] || return 0
  prepare_sudo || return 1
  pm_update || return 1
  case "$PM" in
    dnf)
      run_logged "${SUDO[@]}" dnf install -y --setopt=timeout=60 "$@"
      ;;
    apt)
      run_logged env DEBIAN_FRONTEND=noninteractive NEEDRESTART_MODE=a "${SUDO[@]}" apt-get install -y "$@"
      ;;
    pacman)
      run_logged "${SUDO[@]}" pacman -Sy --needed --noconfirm "$@"
      ;;
    zypper)
      run_logged "${SUDO[@]}" zypper --non-interactive --gpg-auto-import-keys install --no-recommends "$@"
      ;;
    *)
      return 1
      ;;
  esac
}

install_planned_packages() {
  local pkg
  if [[ "$PM" == unknown && ( ${#REQUIRED_ARR[@]} -gt 0 || ${#OPTIONAL_ARR[@]} -gt 0 ) ]]; then
    record FAIL "Package manager" "Could not detect dnf, apt, pacman or zypper."
    return 1
  fi
  if [[ ${#REQUIRED_ARR[@]} -gt 0 ]]; then
    say "Installing required packages: $(join_by_space "${REQUIRED_ARR[@]}")"
    if ! pm_install "${REQUIRED_ARR[@]}"; then
      record FAIL "Packages" "Installing required packages failed. See $LOG"
      return 1
    fi
  fi
  if [[ ${#OPTIONAL_ARR[@]} -gt 0 ]]; then
    say "Installing optional packages: $(join_by_space "${OPTIONAL_ARR[@]}")"
    if ! pm_install "${OPTIONAL_ARR[@]}"; then
      for pkg in "${OPTIONAL_ARR[@]}"; do
        if ! pm_install "$pkg"; then
          record WARN "Optional package" "$pkg was not installed"
        fi
      done
    fi
  fi
  hash -r || true
  return 0
}

rpmfusion_release_url() {
  local rel
  rel=$(rpm -E %fedora 2>/dev/null || true)
  if [[ -z "$rel" || "$rel" == "%fedora" ]]; then
    return 1
  fi
  printf '%s' "https://mirrors.rpmfusion.org/free/fedora/rpmfusion-free-release-${rel}.noarch.rpm"
}

print_rpmfusion_vlc() {
  local url
  say "RPM Fusion is not enabled automatically. To install libvlc yourself:"
  if url=$(rpmfusion_release_url); then
    say "  sudo dnf install $url"
  else
    say "  sudo dnf install https://mirrors.rpmfusion.org/free/fedora/rpmfusion-free-release-\$(rpm -E %fedora).noarch.rpm"
  fi
  say "  sudo dnf install vlc-libs"
  say "Or re-run this script with --enable-rpmfusion."
}

print_rpmfusion_ffmpeg() {
  local url
  say "RPM Fusion's ffmpeg package is optional and is not installed by this script. After enabling RPM Fusion Free it can replace ffmpeg-free:"
  if url=$(rpmfusion_release_url); then
    say "  sudo dnf install $url"
  else
    say "  sudo dnf install https://mirrors.rpmfusion.org/free/fedora/rpmfusion-free-release-\$(rpm -E %fedora).noarch.rpm"
  fi
  say "  sudo dnf swap ffmpeg-free ffmpeg --allowerasing"
}

enable_rpmfusion() {
  local url
  if [[ "$PM" != dnf ]]; then
    record WARN "RPM Fusion" "--enable-rpmfusion applies to Fedora. This machine uses $PM."
    return 0
  fi
  if ! url=$(rpmfusion_release_url); then
    record FAIL "RPM Fusion" "rpm -E %fedora did not return a Fedora release."
    return 1
  fi
  say "Enabling RPM Fusion Free and installing vlc-libs."
  prepare_sudo || return 1
  if ! run_logged "${SUDO[@]}" dnf install -y --setopt=timeout=60 "$url"; then
    record FAIL "RPM Fusion" "Could not install the rpmfusion-free release package."
    return 1
  fi
  if ! run_logged "${SUDO[@]}" dnf install -y --setopt=timeout=60 vlc-libs; then
    record FAIL "libvlc" "RPM Fusion was enabled but vlc-libs did not install."
    return 1
  fi
  hash -r || true
  return 0
}

version_ge() {
  local left="$1"
  local right="$2"
  [[ "$(printf '%s\n%s\n' "$right" "$left" | sort -V | head -n 1)" == "$right" ]]
}

version_matches_roll() {
  local installed="$1"
  local installed_major installed_mm required_major required_mm
  installed_major="${installed%%.*}"
  required_major="${SDK_VERSION%%.*}"
  installed_mm=$(printf '%s' "$installed" | cut -d. -f1-2)
  required_mm=$(printf '%s' "$SDK_VERSION" | cut -d. -f1-2)
  case "$SDK_ROLL" in
    disable)
      [[ "$installed" == "$SDK_VERSION" ]]
      ;;
    latestPatch)
      [[ "$installed_mm" == "$required_mm" ]] && version_ge "$installed" "$SDK_VERSION"
      ;;
    latestMajor|major|latestMinor|minor)
      if [[ "$installed_major" != "$required_major" ]]; then
        version_ge "$installed_major" "$required_major"
      else
        version_ge "$installed" "$SDK_VERSION"
      fi
      ;;
    latestFeature|feature|*)
      [[ "$installed_major" == "$required_major" ]] && version_ge "$installed" "$SDK_VERSION"
      ;;
  esac
}

read_global_json() {
  local file="$1"
  local ver="" roll=""
  SDK_VERSION="10.0.100"
  SDK_ROLL="latestFeature"
  [[ -f "$file" ]] || return 0
  ver=$(sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([0-9][0-9.]*\)".*/\1/p' "$file" | head -n 1 || true)
  roll=$(sed -n 's/.*"rollForward"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$file" | head -n 1 || true)
  if [[ -n "$ver" ]]; then
    SDK_VERSION="$ver"
  fi
  if [[ -n "$roll" ]]; then
    SDK_ROLL="$roll"
  fi
}

ensure_user_dotnet() {
  if [[ -x "${HOME}/.dotnet/dotnet" ]]; then
    export DOTNET_ROOT="${HOME}/.dotnet"
    case ":$PATH:" in
      *":${HOME}/.dotnet:"*) ;;
      *) export PATH="${HOME}/.dotnet:${PATH}" ;;
    esac
    hash -r || true
  fi
}

sdk_satisfies() {
  local line ver
  ensure_user_dotnet
  command -v dotnet >/dev/null 2>&1 || return 1
  while IFS= read -r line; do
    ver="${line%% *}"
    [[ -n "$ver" ]] || continue
    if version_matches_roll "$ver"; then
      return 0
    fi
  done < <(dotnet --list-sdks 2>/dev/null || true)
  return 1
}

sdk_description() {
  local line ver match=""
  while IFS= read -r line; do
    ver="${line%% *}"
    if version_matches_roll "$ver"; then
      match="$line"
    fi
  done < <(dotnet --list-sdks 2>/dev/null || true)
  if [[ -n "$match" ]]; then
    printf '%s satisfies %s >= %s' "$match" "$SDK_ROLL" "$SDK_VERSION"
  else
    printf '%s >= %s is not installed' "$SDK_ROLL" "$SDK_VERSION"
  fi
}

install_dotnet_script() {
  local channel script
  channel=$(printf '%s' "$SDK_VERSION" | cut -d. -f1-2)
  script="$TMP/dotnet-install.sh"
  say "Downloading Microsoft dotnet-install.sh (channel $channel) into ${HOME}/.dotnet"
  if ! curl -fsSL "https://dot.net/v1/dotnet-install.sh" -o "$script"; then
    return 1
  fi
  if ! run_logged bash "$script" --channel "$channel" --install-dir "${HOME}/.dotnet"; then
    return 1
  fi
  export DOTNET_ROOT="${HOME}/.dotnet"
  export PATH="${HOME}/.dotnet:${PATH}"
  hash -r || true
  DOTNET_HINT=1
  return 0
}

ensure_sdk() {
  local sdk_pkg
  sdk_pkg="dotnet-sdk-${SDK_VERSION%.*}"
  if sdk_satisfies; then
    record PASS ".NET SDK" "$(sdk_description)"
    case "$(command -v dotnet)" in
      "${HOME}/.dotnet/"*) DOTNET_HINT=1 ;;
    esac
    return 0
  fi
  if [[ "$NO_INSTALL" -eq 1 ]]; then
    record FAIL ".NET SDK" "Need $SDK_ROLL >= $SDK_VERSION. Re-run without --no-install."
    return 1
  fi
  if [[ "$PM" != unknown ]]; then
    say "Trying distro package $sdk_pkg"
    if pm_install "$sdk_pkg"; then
      hash -r || true
      if sdk_satisfies; then
        record PASS ".NET SDK" "$(sdk_description) from $sdk_pkg"
        return 0
      fi
    fi
    say "$sdk_pkg is not available from the current repositories."
  fi
  if install_dotnet_script && sdk_satisfies; then
    record PASS ".NET SDK" "$(sdk_description) via dotnet-install.sh"
    return 0
  fi
  record FAIL ".NET SDK" "Could not install $SDK_ROLL >= $SDK_VERSION"
  return 1
}

origin_matches() {
  local url="$1"
  [[ "$url" == *Davidolski89/PixelPeeker* ]]
}

sync_source() {
  local url parent
  export GIT_TERMINAL_PROMPT=0
  if [[ -d "$DIR/.git" ]]; then
    url=$(git -C "$DIR" remote get-url origin 2>/dev/null || true)
    if ! origin_matches "$url"; then
      record FAIL "Source" "$DIR is a git checkout but origin is '${url:-unset}'."
      return 1
    fi
    if [[ -n "$(git -C "$DIR" status --porcelain)" ]]; then
      record FAIL "Source" "$DIR has local changes. Commit or stash them, then re-run."
      return 1
    fi
    say "Updating $DIR ($BRANCH) with git pull --ff-only"
    if ! run_logged git -C "$DIR" fetch origin "$BRANCH"; then
      record FAIL "Source" "git fetch origin $BRANCH failed"
      return 1
    fi
    if git -C "$DIR" show-ref --verify --quiet "refs/heads/$BRANCH"; then
      if ! run_logged git -C "$DIR" checkout "$BRANCH"; then
        record FAIL "Source" "Could not check out $BRANCH"
        return 1
      fi
    elif ! run_logged git -C "$DIR" checkout -b "$BRANCH" "origin/$BRANCH"; then
      record FAIL "Source" "Could not check out origin/$BRANCH"
      return 1
    fi
    if ! run_logged git -C "$DIR" pull --ff-only origin "$BRANCH"; then
      record FAIL "Source" "git pull --ff-only failed. The local branch is not a fast-forward of origin/$BRANCH."
      return 1
    fi
    record PASS "Source" "$DIR updated from origin/$BRANCH"
    return 0
  fi
  if [[ -e "$DIR" ]]; then
    if [[ -n "$(ls -A "$DIR" 2>/dev/null || true)" ]]; then
      record FAIL "Source" "$DIR exists and is not a PixelPeeker checkout."
      return 1
    fi
  else
    parent=$(dirname "$DIR")
    if ! mkdir -p "$parent"; then
      record FAIL "Source" "Could not create $parent"
      return 1
    fi
  fi
  say "Cloning $REPO_URL ($BRANCH) into $DIR"
  if ! run_logged git clone --branch "$BRANCH" --single-branch "$REPO_URL" "$DIR"; then
    record FAIL "Source" "git clone failed"
    return 1
  fi
  record PASS "Source" "Cloned $BRANCH into $DIR"
}

parse_project_sdk() {
  if [[ ! -f "$DIR/global.json" ]]; then
    record WARN "global.json" "Missing. Using SDK $SDK_VERSION $SDK_ROLL."
    return 0
  fi
  read_global_json "$DIR/global.json"
  say "global.json requests SDK $SDK_VERSION ($SDK_ROLL)"
}

publish_app() {
  local project out
  project="$DIR/src/FFmpegStudio.Avalonia/FFmpegStudio.Avalonia.csproj"
  out="$DIR/artifacts/portable-linux-${ARCH_NAME}"
  if [[ ! -f "$project" ]]; then
    record FAIL "Publish" "Missing $project"
    return 1
  fi
  export DOTNET_CLI_TELEMETRY_OPTOUT=1
  export DOTNET_NOLOGO=1
  export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
  say "Restoring $project"
  if ! run_logged dotnet restore "$project"; then
    record FAIL "Restore" "dotnet restore failed"
    return 1
  fi
  record PASS "Restore" "$project"
  if [[ "$DO_TEST" -eq 1 ]]; then
    say "Running dotnet test FFmpegStudio.Linux.slnf"
    if ! run_logged dotnet test "$DIR/FFmpegStudio.Linux.slnf" -c Release --nologo; then
      record FAIL "dotnet test" "FFmpegStudio.Linux.slnf failed"
      return 1
    fi
    record PASS "dotnet test" "FFmpegStudio.Linux.slnf"
  fi
  say "Publishing self-contained $RID to $out"
  if ! run_logged dotnet publish "$project" -c Release -r "$RID" --self-contained true -o "$out" -p:PublishSingleFile=false -p:UseAppHost=true --nologo; then
    record FAIL "Publish" "dotnet publish failed"
    return 1
  fi
  BIN="$out/PixelPeeker.Avalonia"
  if [[ ! -f "$BIN" ]]; then
    record FAIL "Publish" "PixelPeeker.Avalonia was not written to $out"
    return 1
  fi
  chmod +x "$BIN"
  record PASS "Publish" "$BIN"
}

escape_desktop() {
  local value="$1"
  value=${value//\\/\\\\}
  value=${value// /\\ }
  printf '%s' "$value"
}

install_desktop_entry() {
  local apps icons exec_path icon_src icon_dest desktop
  apps="${HOME}/.local/share/applications"
  icons="${HOME}/.local/share/icons/hicolor/256x256/apps"
  icon_src="$DIR/src/FFmpegStudio.Desktop/Assets/PixelPeeker.png"
  icon_dest="$icons/pixelpeeker.png"
  desktop="$apps/pixelpeeker.desktop"
  if [[ ! -f "$icon_src" ]]; then
    record FAIL "Desktop entry" "Missing icon $icon_src"
    return 1
  fi
  if ! mkdir -p "$apps" "$icons"; then
    record FAIL "Desktop entry" "Could not create $apps"
    return 1
  fi
  if ! cp "$icon_src" "$icon_dest"; then
    record FAIL "Desktop entry" "Could not copy the icon"
    return 1
  fi
  exec_path=$(escape_desktop "$BIN")
  cat >"$desktop" <<EOF
[Desktop Entry]
Type=Application
Name=PixelPeeker
Comment=Video encoding editor
Exec=${exec_path}
Icon=${icon_dest}
Categories=AudioVideo;Video;
Terminal=false
StartupWMClass=PixelPeeker.Avalonia
TryExec=${BIN}
EOF
  if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "$apps" >/dev/null 2>&1 || true
  fi
  if command -v kbuildsycoca6 >/dev/null 2>&1; then
    kbuildsycoca6 --noincremental >/dev/null 2>&1 || true
  fi
  record PASS "Desktop entry" "$desktop"
}

report_runtime() {
  local soname missing=""
  for soname in libX11.so.6 libICE.so.6 libSM.so.6 libfontconfig.so.1 libfreetype.so.6 \
    libXext.so.6 libXcursor.so.1 libXi.so.6 libXrandr.so.2 libGL.so.1 libxkbcommon.so.0; do
    if ! lib_present "$soname"; then
      missing="${missing:+$missing }$soname"
    fi
  done
  if [[ -n "$missing" ]]; then
    record FAIL "Avalonia libraries" "Still missing: $missing"
  else
    record PASS "Avalonia libraries" "libX11, libICE, libSM, fontconfig, freetype, X extensions, libGL and libxkbcommon are present"
  fi
  if lib_present "libwayland-client.so.0" && lib_present "libwayland-cursor.so.0"; then
    record PASS "Wayland libraries" "libwayland-client and libwayland-cursor are present"
  else
    record WARN "Wayland libraries" "libwayland-client or libwayland-cursor is missing. X11 or XWayland can still start the app."
  fi
  if command -v ffmpeg >/dev/null 2>&1 && command -v ffprobe >/dev/null 2>&1; then
    record PASS "FFmpeg" "$(command -v ffmpeg) — $(ffmpeg -version 2>&1 | head -n 1 || true)"
  else
    record WARN "FFmpeg" "ffmpeg and ffprobe are not both on PATH. The app can download a static build on glibc >= 2.28."
    if [[ "$PM" == dnf ]]; then
      say "  Fedora package: sudo dnf install ffmpeg-free"
    fi
  fi
  if [[ "$PM" == dnf ]]; then
    print_rpmfusion_ffmpeg
  fi
  if libvlc_present; then
    record PASS "libvlc" "Playback library is installed"
  elif [[ "$LIBVLC_STATE" == rpmfusion ]]; then
    record FAIL "libvlc" "vlc-libs is still missing after --enable-rpmfusion"
  else
    record WARN "libvlc" "Playback library is not installed. The editor still starts."
    if [[ "$PM" == dnf && "$LIBVLC_STATE" == instructions ]]; then
      print_rpmfusion_vlc
    fi
  fi
  if lib_present "libfuse.so.2"; then
    record PASS "FUSE" "libfuse.so.2 is present for AppImage files"
  else
    record WARN "FUSE" "libfuse.so.2 is missing. AppImages need fuse-libs or libfuse2, or --appimage-extract-and-run."
  fi
}

launch_app() {
  if [[ "$DO_RUN" -ne 1 ]]; then
    return 0
  fi
  if [[ -z "${DISPLAY:-}" && -z "${WAYLAND_DISPLAY:-}" ]]; then
    record WARN "Launch" "DISPLAY and WAYLAND_DISPLAY are unset. Start the binary from a desktop session."
    return 0
  fi
  say "Launching $BIN"
  if [[ "$FINISHED" -eq 0 ]]; then
    FINISHED=1
    print_summary
  fi
  exec "$BIN"
}

main() {
  local approved=0
  parse_args "$@"
  mkdir -p "$(dirname "$LOG")"
  : >"$LOG"
  TMP=$(mktemp -d "${TMPDIR:-/tmp}/pixelpeeker-linux-setup.XXXXXX")
  trap cleanup EXIT
  trap 'on_err $LINENO' ERR
  log_line "PixelPeeker Linux setup $(date -u +%Y-%m-%dT%H:%M:%SZ)"

  if [[ "$(uname -s)" != Linux ]]; then
    record FAIL "Operating system" "This script supports Linux. Detected $(uname -s)."
    finish 1
  fi
  PRETTY=$(os_pretty)
  detect_pm
  detect_arch
  record PASS "Distribution" "$PRETTY ($PM)"
  if [[ -z "$RID" ]]; then
    record FAIL "Architecture" "$ARCH is not linux-x64 or linux-arm64"
    finish 1
  fi
  record PASS "Architecture" "$ARCH ($RID)"

  plan_packages
  print_plan
  if confirm_install; then
    approved=1
  fi
  if [[ "$approved" -eq 1 ]]; then
    if needs_install_action; then
      install_planned_packages || finish 1
      if [[ "$LIBVLC_STATE" == rpmfusion ]]; then
        enable_rpmfusion || finish 1
      fi
    fi
  elif [[ ${#REQUIRED_ARR[@]} -gt 0 || "$LIBVLC_STATE" == rpmfusion ]]; then
    record FAIL "Packages" "Required installation was skipped."
    if [[ "$PM" == dnf && "$LIBVLC_STATE" == instructions ]]; then
      print_rpmfusion_vlc
    fi
    finish 1
  fi

  if ! command -v git >/dev/null 2>&1; then
    record FAIL "git" "git is still missing"
    finish 1
  fi
  sync_source || finish 1
  parse_project_sdk
  ensure_sdk || finish 1
  publish_app || finish 1
  if [[ "$DO_DESKTOP" -eq 1 ]]; then
    install_desktop_entry || finish 1
  fi
  report_runtime
  if [[ "$FAILS" -gt 0 ]]; then
    finish 1
  fi
  launch_app
  finish 0
}

main "$@"
