#!/usr/bin/env bash
#
# Puts one generated file onto a long-lived branch and keeps exactly one pull request open for it.
#
# A default branch under a ruleset takes changes through pull requests only, so a job cannot push to
# it. That much is easy to get right. What is easy to get WRONG is how the commit is made: a commit
# created by git on a runner is unsigned, so the pull request satisfies the pull-request rule and
# then fails required_signatures forever — open, correct, and permanently unmergeable.
#
# A commit created through the contents API is signed by GitHub and verifies. That is the whole
# reason this does not use git.
#
# One long-lived branch is reused and force-updated rather than a branch per run, so a generated
# file is worth exactly one open pull request showing the newest content, not one per run.
#
# VOLATILE is what stops this being a perpetual motion machine. A generator that stamps its own run
# id or the time of day into its output produces a different file on every run, so there is always
# something to publish: publish → pull request → merge → push to the default branch → CI → generate →
# publish. DriveBenderUtility ran exactly that loop, on one line reading "Generated from run: NNNN".
# Lines matching VOLATILE are excluded when deciding whether anything CHANGED — they are still
# written when something else did change, so the provenance stays truthful without driving the loop.
set -euo pipefail

: "${BRANCH:?BRANCH is required}"
: "${FILE:?FILE is required}"
: "${MESSAGE:?MESSAGE is required}"
: "${TITLE:?TITLE is required}"
: "${BODY:?BODY is required}"
: "${GITHUB_REPOSITORY:?}"
: "${GH_TOKEN:?GH_TOKEN is required}"
VOLATILE="${VOLATILE:-}"

if [ ! -f "$FILE" ]; then
  echo "::error::$FILE does not exist — the step that generates it produced nothing."
  exit 1
fi

# status, not diff: a file the repository does not track yet — the first screenshot, the first
# generated table — is invisible to `git diff` and would be reported as unchanged forever.
if [ -z "$(git status --porcelain -- "$FILE")" ]; then
  echo "::notice::$FILE is unchanged"
  exit 0
fi

# Something changed textually. Whether it changed MEANINGFULLY is a separate question, and the answer
# is what decides between a pull request and a quiet exit.
if [ -n "$VOLATILE" ]; then
  committed=$(mktemp); regenerated=$(mktemp)
  trap 'rm -f "$committed" "$regenerated"' EXIT

  # Compared against the default branch, not against the bot branch: the pull request proposes a
  # change relative to what is merged, so that is the only comparison that decides whether one is
  # worth opening. A binary file has no lines to filter and git show simply yields it unchanged.
  git show "HEAD:$FILE" 2>/dev/null | grep -vE "$VOLATILE" > "$committed" || true
  grep -vE "$VOLATILE" "$FILE" > "$regenerated" || true

  if cmp -s "$committed" "$regenerated"; then
    echo "::notice::$FILE differs from HEAD only in lines matching /$VOLATILE/ — nothing worth proposing."
    exit 0
  fi
fi

base=$(git rev-parse HEAD)

# Point the branch at what this run was built from, creating it the first time. Force, because the
# branch only ever carries the newest content — its history is not interesting, and letting it
# diverge would only produce conflicts against itself.
gh api -X PATCH "repos/$GITHUB_REPOSITORY/git/refs/heads/$BRANCH" -f sha="$base" -F force=true >/dev/null 2>&1 \
  || gh api -X POST "repos/$GITHUB_REPOSITORY/git/refs" -f ref="refs/heads/$BRANCH" -f sha="$base" >/dev/null

# The blob sha the branch currently has for this path, which the contents API wants in order to
# replace rather than create. Legitimately empty when the file is not committed yet.
blob=$(gh api "repos/$GITHUB_REPOSITORY/contents/$FILE?ref=$BRANCH" --jq '.sha' 2>/dev/null || true)

# The encoded file arrives on stdin, not in the argument list. `-f content="$(base64 …)"` put the
# whole payload into argv, and Windows caps a command line at about 32K — so every file big enough
# to be worth generating died with "gh: Argument list too long" before the API was ever called. A
# 300K screenshot needs 400K of base64; there is no argument list that holds it.
#
# @- is read verbatim as a string: the magic type conversion -F does on literals is not applied to
# a value that came from a file, so a payload cannot be mistaken for a number or a boolean.
args=(-X PUT "repos/$GITHUB_REPOSITORY/contents/$FILE"
      -f message="$MESSAGE" -f branch="$BRANCH" -F content=@-)
if [ -n "$blob" ]; then
  args+=(-f sha="$blob")
fi
base64 -w0 "$FILE" \
  | gh api "${args[@]}" --jq '"committed \(.commit.sha[0:8]) to '"$BRANCH"', verified=\(.commit.verification.verified)"'

if [ -n "$(gh pr list --head "$BRANCH" --state open --json number --jq '.[].number')" ]; then
  echo "::notice::refreshed the open pull request for $BRANCH"
  exit 0
fi

gh pr create --base main --head "$BRANCH" --title "$TITLE" --body "$BODY"
