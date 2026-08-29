#!/usr/bin/env bash
#
# Fails when a generated file in the working tree differs from the one committed.
#
# The generator has already run and written the file; this only compares. That is the whole design:
# a generated file is CHECKED on a pull request, exactly like the package READMEs, rather than
# published afterwards by a bot.
#
# Why not publish it: main takes changes through pull requests only, and a commit made by git on a
# runner is unsigned — required_signatures is evaluated over a pull request's commits and a squash
# merge does not launder it, so such a pull request can never be merged. Working around that means a
# signed commit through the API plus a personal access token to make its checks run, for a pull
# request that trails the change it describes. Checking instead costs nothing and lands the
# regenerated file in the same pull request as the change that altered it.
#
# VOLATILE excludes lines that change every run without meaning anything — a run id, a timestamp.
# Without it a provenance stamp fails the check on every unrelated pull request.
set -euo pipefail

: "${FILE:?FILE is required}"
: "${HINT:?HINT is required — the failure has to say how to regenerate}"
VOLATILE="${VOLATILE:-}"

if [ ! -f "$FILE" ]; then
  echo "::error::$FILE does not exist — the step that generates it produced nothing."
  exit 1
fi

# status, not diff: a file the repository does not track yet is invisible to `git diff` and would
# pass forever.
if [ -z "$(git status --porcelain -- "$FILE")" ]; then
  echo "$FILE is current."
  exit 0
fi

if [ -n "$VOLATILE" ]; then
  committed=$(mktemp); regenerated=$(mktemp)
  trap 'rm -f "$committed" "$regenerated"' EXIT
  git show "HEAD:$FILE" 2>/dev/null | grep -vE "$VOLATILE" > "$committed" || true
  grep -vE "$VOLATILE" "$FILE" > "$regenerated" || true
  if cmp -s "$committed" "$regenerated"; then
    echo "$FILE is current (it differs only in lines matching /$VOLATILE/)."
    # Leave the tree as it was found, so nothing downstream sees a spurious modification.
    git checkout -- "$FILE" 2>/dev/null || true
    exit 0
  fi
fi

echo "::error file=$FILE::$FILE is out of date. $HINT"
{
  echo "### \`$FILE\` is out of date"
  echo
  echo "$HINT"
  echo
  echo "The regenerated file is attached to this run as an artifact — download it and commit it."
} >> "${GITHUB_STEP_SUMMARY:-/dev/null}"

echo "--- what changed (first 40 lines) ---"
git --no-pager diff --no-color -- "$FILE" | head -40 || true
exit 1
