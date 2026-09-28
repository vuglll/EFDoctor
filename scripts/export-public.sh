#!/usr/bin/env bash
# Builds the public EFDoctor repository from the committed HEAD of this one, without its history.
#
#   scripts/export-public.sh --out <dir> --author "Name <email>" [--repo owner/name] [--verify]
#   scripts/export-public.sh --into <public-clone> [--repo owner/name] [--verify]
#
# The export contains the tracked files minus the paths in scripts/export-exclude.txt. It fails if
# a term from the denylist appears in an exported text file, or if a kept file mentions an excluded
# path. The denylist holds private terms (project names, work domains, local paths), so it is never
# committed: it lives at ~/.config/efdoctor/export-denylist, one term per line, unless --denylist
# names another file. --out creates a new repository with a single commit; --into replaces the
# working tree of an existing clone and stages it. Nothing is ever pushed.
set -euo pipefail

usage() {
  sed -n '2,12p' "$0" | sed 's/^# \{0,1\}//'
  exit 2
}

fail() {
  echo "export-public: $*" >&2
  exit 1
}

out="" into="" repo="" author="" verify=0
denylist="${XDG_CONFIG_HOME:-$HOME/.config}/efdoctor/export-denylist"
message="Initial public release"
while [ $# -gt 0 ]; do
  case "$1" in
    --out) out="${2:?}"; shift 2 ;;
    --into) into="${2:?}"; shift 2 ;;
    --repo) repo="${2:?}"; shift 2 ;;
    --author) author="${2:?}"; shift 2 ;;
    --denylist) denylist="${2:?}"; shift 2 ;;
    --message) message="${2:?}"; shift 2 ;;
    --verify) verify=1; shift ;;
    -h|--help) usage ;;
    *) echo "export-public: unknown option $1" >&2; usage ;;
  esac
done

[ -n "$out" ] || [ -n "$into" ] || usage
[ -z "$out" ] || [ -z "$into" ] || fail "use --out or --into, not both"
if [ -n "$out" ]; then
  [ -n "$author" ] || fail "--out needs --author \"Name <email>\""
  author_name="${author% <*}"
  author_email="${author##*<}"; author_email="${author_email%>}"
  [ -n "$author_name" ] && [ "$author_email" != "$author" ] || fail "--author must look like \"Name <email>\""
  if [ -e "$out" ] && [ -n "$(ls -A "$out" 2>/dev/null)" ]; then fail "$out exists and is not empty"; fi
fi
if [ -n "$into" ]; then
  git -C "$into" rev-parse --git-dir >/dev/null 2>&1 || fail "$into is not a git repository"
fi
if [ -n "$repo" ]; then
  case "$repo" in */*) ;; *) fail "--repo must be owner/name" ;; esac
fi

root="$(git rev-parse --show-toplevel)"
cd "$root"
git diff --quiet && git diff --cached --quiet || fail "commit or stash your changes first; the export uses HEAD"

[ -f "$denylist" ] || fail "no denylist at $denylist (one private term per line; see the header of this script)"
if git ls-files --error-unmatch "$denylist" >/dev/null 2>&1; then fail "the denylist must not be tracked"; fi
terms="$(grep -v '^[[:space:]]*#' "$denylist" | sed 's/[[:space:]]*$//' | grep -v '^$' || true)"
[ -n "$terms" ] || fail "the denylist $denylist has no terms"

staging="$(mktemp -d)"
trap 'rm -rf "$staging"' EXIT
tree="$staging/tree"
mkdir "$tree"
git archive HEAD | tar -x -C "$tree"

entries="$(grep -v '^[[:space:]]*#' scripts/export-exclude.txt | sed 's/[[:space:]]*$//' | grep -v '^$')"
echo "$entries" | sed 's/^generated:[[:space:]]*//' | while IFS= read -r path; do
  rm -rf "${tree:?}/$path"
done
# Generated paths are recreated locally by the tooling, so kept files may name them.
excludes="$(echo "$entries" | grep -v '^generated:' || true)"

problems="$staging/problems"
: > "$problems"

# Kept files must not point at excluded paths. The archived changes record the repository as it
# was, and scripts/ names the excluded paths on purpose, so both are skipped.
echo "$excludes" | while IFS= read -r path; do
  [ -n "$path" ] || continue
  (cd "$tree" && grep -rIl -F --exclude-dir=.git -- "$path" . 2>/dev/null || true) \
    | sed 's#^\./##' \
    | { grep -v -e '^openspec/changes/archive/' -e '^scripts/' || true; } \
    | while IFS= read -r file; do echo "  $file mentions excluded path $path" >> "$problems"; done
done

echo "$terms" | while IFS= read -r term; do
  (cd "$tree" && grep -rIli -F -- "$term" . 2>/dev/null || true) \
    | sed 's#^\./##' \
    | while IFS= read -r file; do echo "  $file contains denylisted term \"$term\"" >> "$problems"; done
done

if [ -s "$problems" ]; then
  echo "export-public: the export is not clean:" >&2
  cat "$problems" >&2
  exit 1
fi

if [ -n "$repo" ]; then
  url="https://github.com/$repo"
  grep -q '<EFDoctorRepositoryUrl></EFDoctorRepositoryUrl>' "$tree/Directory.Build.props" \
    || fail "Directory.Build.props has no empty EFDoctorRepositoryUrl to fill in"
  sed -i.bak "s#<EFDoctorRepositoryUrl></EFDoctorRepositoryUrl>#<EFDoctorRepositoryUrl>$url</EFDoctorRepositoryUrl>#" "$tree/Directory.Build.props"
  badge="[![CI]($url/actions/workflows/ci.yml/badge.svg)]($url/actions/workflows/ci.yml)"
  sed -i.bak "s#<!-- ci-badge -->#$badge#" "$tree/README.md"
  rm -f "$tree/Directory.Build.props.bak" "$tree/README.md.bak"
fi

if [ "$verify" -eq 1 ]; then
  echo "export-public: building and testing the exported tree..."
  check="$staging/verify"
  cp -R "$tree" "$check"
  (cd "$check" \
    && dotnet restore EFDoctor.sln \
    && dotnet build EFDoctor.sln --no-restore --configuration Release \
    && dotnet test EFDoctor.sln --no-build --configuration Release) \
    || fail "the exported tree does not build or pass its tests"
fi

source_commit="$(git rev-parse --short HEAD)"
if [ -n "$out" ]; then
  mkdir -p "$out"
  cp -R "$tree/." "$out/"
  git -C "$out" init -q -b main
  git -C "$out" add -A
  GIT_AUTHOR_NAME="$author_name" GIT_AUTHOR_EMAIL="$author_email" \
  GIT_COMMITTER_NAME="$author_name" GIT_COMMITTER_EMAIL="$author_email" \
    git -C "$out" -c commit.gpgsign=false commit -q -m "$message"
  echo "export-public: created $out with one commit ($(git -C "$out" ls-files | wc -l | tr -d ' ') files) from $source_commit."
  echo "Next: git -C \"$out\" remote add origin <url> && git -C \"$out\" push -u origin main"
else
  git -C "$into" rm -rq --ignore-unmatch . >/dev/null
  cp -R "$tree/." "$into/"
  git -C "$into" add -A
  echo "export-public: staged the export of $source_commit in $into:"
  git -C "$into" diff --cached --stat | tail -1
  echo "Review, then commit and push from $into."
fi
