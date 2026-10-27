#!/usr/bin/env bash
# What a release run does (release.yml, job "plan"). Program and addon are released separately:
#   program — tag v<Program.Version>:        exe + full archive → GitHub release (auto-update, the website's button)
#   addon   — tag addon-v<toc ## Version>:   BigWigs packager → CurseForge only (no GitHub release)
# Started by the "Run workflow" button (WHAT = program | addon | both, KIND = release | alpha | beta) or by pushing
# such a tag by hand. A test release gets the next free -alphaN / -betaN.
# Writes key=value lines to $GITHUB_OUTPUT (or stdout): program, addon, program_tag, addon_tag, suffix, prerelease,
# program_version, addon_version, sha.
set -euo pipefail
cd "$(dirname "$0")/.."
out=${GITHUB_OUTPUT:-/dev/stdout}
fail() { echo "::error::$*"; exit 1; }

pv=$(sed -n 's/.*public const string Version = "\([^"]*\)";.*/\1/p' program/Program.cs | head -1)
av=$(sed -n 's/^## Version: *//p' MountSheetSync.toc | tr -d '\r' | head -1)
[ -n "$pv" ] || fail "no Program.Version in program/Program.cs"
[ -n "$av" ] || fail "no ## Version in MountSheetSync.toc"

tag_exists() { git rev-parse -q --verify "refs/tags/$1" >/dev/null; }
next_tag() {   # next_tag v2.9.0-alpha → v2.9.0-alpha4 when alpha1..3 exist
  local n=1
  while tag_exists "$1$n"; do n=$((n + 1)); done
  echo "$1$n"
}

program=false addon=false program_tag="" addon_tag="" suffix=""
event=${GITHUB_EVENT_NAME:-workflow_dispatch}
if [ "$event" = "push" ]; then
  ref=${GITHUB_REF_NAME:?}
  case "$ref" in
    addon-v*)
      addon=true addon_tag=$ref
      base=${ref#addon-v}; base=${base%%-*}
      [ "$base" = "$av" ] || fail "tag $ref, but MountSheetSync.toc has $av"
      ;;
    v*)
      program=true program_tag=$ref
      base=${ref#v}; base=${base%%-*}
      [ "$base" = "$pv" ] || fail "tag $ref, but Program.Version is $pv"
      ;;
    *) fail "unknown tag $ref (v… — program, addon-v… — addon)" ;;
  esac
  rest=${ref#addon-}; rest=${rest#v}   # 2.9.0-alpha1
  if [ "$rest" != "${rest#*-}" ]; then
    suffix=${rest#*-}
  fi
  if [ -n "$suffix" ] && ! echo "$suffix" | grep -Eq '^(alpha|beta)[0-9]*$'; then
    fail "a test tag ends in -alphaN or -betaN"
  fi
else
  what=${WHAT:-program}
  kind=${KIND:-release}
  case "$what" in program) program=true ;; addon) addon=true ;; both) program=true addon=true ;; *) fail "WHAT=$what" ;; esac
  case "$kind" in release|alpha|beta) ;; *) fail "KIND=$kind" ;; esac
  if [ "$kind" = "release" ]; then
    if $program; then
      program_tag=v$pv
      ! tag_exists "$program_tag" || fail "program $pv is already released ($program_tag) — raise Program.Version first"
    fi
    if $addon; then
      addon_tag=addon-v$av
      ! tag_exists "$addon_tag" || fail "addon $av is already released ($addon_tag) — raise ## Version in MountSheetSync.toc first"
    fi
  else
    if $program; then
      program_tag=$(next_tag "v$pv-$kind")
      suffix=${program_tag##*-}
    fi
    if $addon; then
      addon_tag=$(next_tag "addon-v$av-$kind")
    fi
  fi
fi

prerelease=false
if [ -n "$suffix" ]; then
  prerelease=true
elif [ "$event" != "push" ] && [ "${KIND:-release}" != "release" ]; then
  prerelease=true
fi

{
  echo "program=$program"
  echo "addon=$addon"
  echo "program_tag=$program_tag"
  echo "addon_tag=$addon_tag"
  echo "suffix=$suffix"
  echo "prerelease=$prerelease"
  echo "program_version=$pv"
  echo "addon_version=$av"
  echo "sha=$(git rev-parse HEAD)"
} >> "$out"
echo "program: $program ${program_tag:-—} (Program.Version $pv) · addon: $addon ${addon_tag:-—} (toc $av) · prerelease: $prerelease"
