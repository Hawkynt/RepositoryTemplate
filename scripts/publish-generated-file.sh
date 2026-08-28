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
set -euo pipefail

: "${BRANCH:?BRANCH is required}"
: "${FILE:?FILE is required}"
: "${MESSAGE:?MESSAGE is required}"
: "${TITLE:?TITLE is required}"
: "${BODY:?BODY is required}"
: "${GITHUB_REPOSITORY:?}"
: "${GH_TOKEN:?GH_TOKEN is required}"

if [ ! -f "$FILE" ]; then
  echo "::error::$FILE does not exist — the step that generates it produced nothing."
  exit 1
fi

if git diff --quiet -- "$FILE"; then
  echo "::notice::$FILE is unchanged"
  exit 0
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

args=(-X PUT "repos/$GITHUB_REPOSITORY/contents/$FILE"
      -f message="$MESSAGE" -f branch="$BRANCH" -f content="$(base64 -w0 "$FILE")")
if [ -n "$blob" ]; then
  args+=(-f sha="$blob")
fi
gh api "${args[@]}" --jq '"committed \(.commit.sha[0:8]) to '"$BRANCH"', verified=\(.commit.verification.verified)"'

if [ -n "$(gh pr list --head "$BRANCH" --state open --json number --jq '.[].number')" ]; then
  echo "::notice::refreshed the open pull request for $BRANCH"
  exit 0
fi

gh pr create --base main --head "$BRANCH" --title "$TITLE" --body "$BODY"
