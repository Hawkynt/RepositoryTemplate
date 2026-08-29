#!/usr/bin/env bash
#
# Commits one regenerated file back onto the branch that was just pushed.
#
# This is the cheap half of the pipeline. A push to a working branch regenerates the derived things
# — a screenshot, a table, an API reference — and puts them straight on the branch, so by the time a
# pull request exists they are already part of it. The expensive test battery only runs when that
# pull request opens.
#
# Three properties make it safe, and all three are load-bearing:
#
#   1. The commit is made through the contents API, so GitHub signs it. A commit made by git on a
#      runner is unsigned, and required_signatures is evaluated over a pull request's COMMITS — a
#      squash merge does not launder it — so an unsigned commit here would make the branch
#      unmergeable later. Measured, not assumed.
#   2. Nothing done with GITHUB_TOKEN triggers a workflow, so this commit cannot start another run.
#      There is no loop to fence off; it is impossible by construction.
#   3. It refuses to touch the default branch. That branch takes changes through pull requests only,
#      and this must never be the thing that discovers otherwise.
#
# VOLATILE excludes lines that change every run without meaning anything — a run id, a timestamp.
# Without it a provenance stamp produces a commit on every push, forever.
set -euo pipefail

: "${FILE:?FILE is required}"
: "${MESSAGE:?MESSAGE is required}"
: "${GITHUB_REPOSITORY:?}"
: "${GITHUB_REF_NAME:?}"
: "${GH_TOKEN:?GH_TOKEN is required}"
VOLATILE="${VOLATILE:-}"

# On a pull_request run GITHUB_REF_NAME is "123/merge" — a synthetic ref nothing can be committed
# to. GITHUB_HEAD_REF is the actual source branch, and is empty on a push, so this covers both.
branch="${GITHUB_HEAD_REF:-$GITHUB_REF_NAME}"

default_branch=$(gh api "repos/$GITHUB_REPOSITORY" --jq '.default_branch')
if [ "$branch" = "$default_branch" ]; then
  echo "::error::Refusing to commit to $default_branch. It takes changes through pull requests only."
  exit 1
fi

if [ ! -f "$FILE" ]; then
  echo "::error::$FILE does not exist — the step that generates it produced nothing."
  exit 1
fi

# status, not diff: a file the repository does not track yet is invisible to `git diff`.
if [ -z "$(git status --porcelain -- "$FILE")" ]; then
  echo "::notice::$FILE is unchanged."
  exit 0
fi

if [ -n "$VOLATILE" ]; then
  committed=$(mktemp); regenerated=$(mktemp)
  trap 'rm -f "$committed" "$regenerated"' EXIT
  git show "HEAD:$FILE" 2>/dev/null | grep -vE "$VOLATILE" > "$committed" || true
  grep -vE "$VOLATILE" "$FILE" > "$regenerated" || true
  if cmp -s "$committed" "$regenerated"; then
    echo "::notice::$FILE differs only in lines matching /$VOLATILE/ — nothing worth committing."
    exit 0
  fi
fi

# The blob sha the branch currently has for this path; the contents API needs it to replace rather
# than create. Legitimately empty when the file is not committed yet.
blob=$(gh api "repos/$GITHUB_REPOSITORY/contents/$FILE?ref=$branch" --jq '.sha' 2>/dev/null || true)

# The payload goes in on stdin. Passed as an argument it dies at about 32K on Windows, and a 300K
# screenshot needs 400K of base64. @- is read verbatim, so a payload cannot be mistaken for a number.
args=(-X PUT "repos/$GITHUB_REPOSITORY/contents/$FILE"
      -f message="$MESSAGE" -f branch="$branch" -F content=@-)
if [ -n "$blob" ]; then
  args+=(-f sha="$blob")
fi
base64 -w0 "$FILE" \
  | gh api "${args[@]}" --jq '"committed \(.commit.sha[0:8]) to '"$branch"', verified=\(.commit.verification.verified)"'
