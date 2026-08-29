# `publish-generated-file`

> Commits one generated file onto a long-lived branch with a GitHub-signed commit and keeps exactly
> one pull request open for it — without starting a loop.

```yaml
      - name: Publish it through a pull request
        uses: Hawkynt/RepositoryTemplate/publish-generated-file@v1
        with:
          file: docs/EndToEndCoverage.md
          branch: ci/coverage-matrix
          message: '* end-to-end coverage matrix regenerated'
          title: End-to-end coverage matrix (regenerated)
          body: Regenerated from the Windows and Linux end-to-end results.
          volatile: '^Generated from run: '
          token: ${{ secrets.BOT_PR_TOKEN || github.token }}
```

## ⚠️ The two ways this goes wrong

Both were found in production, in this account, and both are the reason this action exists rather
than twenty lines of `git` in each workflow.

### The commit must be signed

A default branch under a ruleset requires a pull request *and* signed commits. `git commit` on a
runner produces an **unsigned** commit, so the pull request satisfies the pull-request rule and then
fails `required_signatures` forever — open, correct, and permanently unmergeable.
DriveBenderUtility#9 sat blocked until it was re-signed by hand.

The commit is therefore made through the **contents API**, which GitHub signs.

### The output must converge, or you have built a perpetual motion machine

A generator that stamps its own run id or the time of day into its output produces a different file
on every run. So there is *always* something to publish:

```
CI on main → generate → publish → pull request → merge → push to main → CI on main → …
```

DriveBenderUtility ran precisely that loop, driven by one line reading
`Generated from run: [33160692079]`. Every turn cost a full matrix on three runners and sent mail.

`volatile` is an extended regex for the lines that change every run without meaning anything. They
are ignored when deciding whether the file **changed**, and still written when something else did —
so the provenance stays truthful without driving the loop. **If your generator stamps provenance,
this input is not optional in spirit.**

Belt and braces, in the calling workflow:

```yaml
on:
  push:
    branches: [main]
    paths-ignore:
      - 'docs/EndToEndCoverage.md'   # merging the bot's own pull request must not restart CI
```

## 🔌 Inputs

| Input | Required | Purpose |
| --- | :---: | --- |
| `file` | ✅ | Repository-relative path. It must already be written when this runs. |
| `branch` | ✅ | The long-lived branch that carries it. Force-updated by every run. |
| `message` | ✅ | Commit message. House style: starts with `+ - * # !`. |
| `title` | ✅ | Pull request title. Used only when there is no open one for the branch. |
| `body` | ✅ | Pull request body. Used only when there is no open one for the branch. |
| `volatile` | — | Extended regex for lines that change every run without meaning anything. |
| `token` | — | Defaults to `github.token`. See below. |

## 🔑 About the token

Nothing done with `GITHUB_TOKEN` can trigger another workflow. That cuts both ways:

- It is why merging is safe — the bot's own commit cannot start a run.
- It is why a pull request opened with it arrives with **no checks**, and therefore can never
  satisfy a required-status-checks rule.

Where the repository requires checks, pass a personal access token with contents and pull-requests
write. Without one the pull request is still opened and still mergeable; it just has no checks.

## ❤️ Support

If any of this saved you an afternoon, [sponsor the work](https://github.com/sponsors/Hawkynt).

## 📜 License

LGPL-3.0-or-later. See [LICENSE](https://github.com/Hawkynt/RepositoryTemplate/blob/main/LICENSE).
