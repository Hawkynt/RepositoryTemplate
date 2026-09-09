# RepositoryTemplate

[![License](https://img.shields.io/github/license/Hawkynt/RepositoryTemplate)](https://github.com/Hawkynt/RepositoryTemplate/blob/main/LICENSE)
[![Language](https://img.shields.io/github/languages/top/Hawkynt/RepositoryTemplate?color=8957D5)](https://github.com/Hawkynt/RepositoryTemplate)

<!-- The CI badge points at self-test.yml, not ci.yml: ci.yml is guarded to no-op in this repo, which
     has no solution to build, so a badge for it would be permanently grey. There are no Release,
     Nightly or Downloads badges either — this repo ships from the moving `v1` tag and cuts no
     releases, so those three would read "no releases" forever. -->
[![CI](https://github.com/Hawkynt/RepositoryTemplate/actions/workflows/self-test.yml/badge.svg?branch=main)](https://github.com/Hawkynt/RepositoryTemplate/actions/workflows/self-test.yml)
![Last Commit](https://img.shields.io/github/last-commit/Hawkynt/RepositoryTemplate?branch=main)
![Activity](https://img.shields.io/github/commit-activity/m/Hawkynt/RepositoryTemplate)

[![Stars](https://img.shields.io/github/stars/Hawkynt/RepositoryTemplate?color=FFD700)](https://github.com/Hawkynt/RepositoryTemplate/stargazers)
[![Forks](https://img.shields.io/github/forks/Hawkynt/RepositoryTemplate?color=008080)](https://github.com/Hawkynt/RepositoryTemplate/network/members)
[![Issues](https://img.shields.io/github/issues/Hawkynt/RepositoryTemplate)](https://github.com/Hawkynt/RepositoryTemplate/issues)
![Code Size](https://img.shields.io/github/languages/code-size/Hawkynt/RepositoryTemplate?color=4CAF50)
![Repo Size](https://img.shields.io/github/repo-size/Hawkynt/RepositoryTemplate?color=FF9800)

> Clean starting point for Hawkynt's C# repositories — the standard scaffolding, the shared CI pipeline, and the reusable **`nuget-publish`** Trusted Publishing action, all in one place.

## 🧭 Vision

One place where the shape of a `Hawkynt/*` repository is decided, so that no repository has to decide
it again. The scaffolding, the CI pipeline, the release machinery and the documentation conventions
live here once and reach every consuming repo through composite actions and reusable workflows — which
means a fix lands everywhere at once, and a repo cannot quietly drift into its own dialect.

The direction is that everything a house rule asserts should also be *checked*. A convention nobody
enforces is a convention that decays: the package README rules became `package-readme`, the repository
README rules became `repo-readme`, and whatever is currently only written down is the next candidate.

## ✨ Features

- **Reusable CI** — `dotnet-ci.yml` and `dotnet-smoke.yml` carry the whole gate; a repo's own
  `ci.yml` is a dozen lines calling one of them.
- **Dated releases and nightlies** — build, changelog, release notes and a grandfather-father-son
  prune of old nightlies, as one reusable workflow.
- **Versions from files, never tags** — `stamp-version` composes each package's version from its own
  manifest plus the commit count of its own folder, across eight language stacks.
- **Trusted Publishing to nuget.org** — `nuget-publish` exchanges the job's OIDC token for a
  short-lived key and fails when a package is accepted but never becomes available.
- **Documentation that is checked, not merely requested** — `package-readme` generates each package's
  API reference from assembly metadata and lints the result; `repo-readme` checks the repository
  README against the house structure and emoji vocabulary.
- **Generated files committed from a branch push** — signed, loop-free, and refusing to touch `main`.

## 📦 Installation

```bash
gh repo create Hawkynt/MyNewApp --template Hawkynt/RepositoryTemplate --private --clone
```

Nothing is vendored into the new repository: it carries no `scripts/` directory, and reaches the
tooling through `Hawkynt/RepositoryTemplate/<action>@v1`.

## 🚀 Quick start

Then, in the new repo:

1. Replace `ProjectName` in `AGENTS.md`, `CONTRIBUTING.md`, and `Directory.Build.props` with the real
   solution/app name, and adjust the `TargetFramework`.
2. Rewrite the README body from [`repo-readme/TEMPLATE.md`](repo-readme/TEMPLATE.md), then switch
   `repo-readme: true` on in `ci.yml` and `smoke.yml` so it stays that way. Rewrite the AGENTS "What
   this is" section too.
3. Point the workflows at the real solution and projects (they carry `ProjectName` placeholders and a
   guard so they no-op until then).
4. **For GUI applications, enumerate the primary dialogs/windows, add deterministic in-app demo
   scenarios for each, reference their screenshots from the README/docs, and make `generate.yml`
   regenerate the full set.** Do this while the UI is built, not as a later documentation cleanup.
   Set `repo-readme-gui: true`, which makes the hero image and the screenshot tour required.
5. Remove any part of the pipeline the project does not need (e.g. the NuGet publish step for a
   binary-only app).

## 🧱 What's in here

| Path                                    | Purpose                                                                                                                                                                          |
| --------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `LICENSE`                               | LGPL-3.0-or-later (full LGPLv3).                                                                                                                                                 |
| `README.md`                             | This file, and a worked example of the convention below — the canonical order lives in `repo-readme/`.                                                                           |
| `AGENTS.md`                             | Binding working agreement for agents and contributors (commits, the loop, code style).                                                                                           |
| `CONTRIBUTING.md`                       | Build/test/CI/release guide.                                                                                                                                                     |
| `.editorconfig`                         | Shared formatting (LF, 2-space indent, K&R braces in C#, tabs for sln/Makefile).                                                                                                 |
| `.gitignore`                            | .NET / IDE / test / NuGet ignores.                                                                                                                                               |
| `Directory.Build.props`                 | Central TFM, nullable, and package/authorship metadata.                                                                                                                          |
| `.github/FUNDING.yml`                   | Sponsors + PayPal button (pairs with the README `## ❤️ Support` section).                                                                                                         |
| `.github/workflows/`                    | `ci` · `_build` · `nightly` · `release` — thin, and they call the actions below. Plus `self-test`, which runs *here*.                                                            |
| `.github/workflows/dotnet-ci.yml`       | **Reusable workflow.** The whole standard CI gate; a repo's own `ci.yml` is a dozen lines calling it.                                                                            |
| `.github/workflows/nightly-publish.yml` | **Reusable workflow.** The marker, the release and the GFS prune; only the build stays per-repo.                                                                                 |
| `scripts/`                              | `version.pl`, `update-changelog.mjs`, `prune-nightlies.mjs`, `package-readme.cs`, `repo-readme.mjs`, `commit-generated-file.sh`, `assert-generated-file.sh` — the single copy, used by the actions. |
| `scripts/fixtures/`                     | The package-readme and repo-readme test fixtures and their golden output.                                                                                                        |
| `nuget-publish/`                        | Composite action: Trusted Publishing push with an acceptance check.                                                                                                              |
| `stamp-version/`                        | Composite action: stamp per-package versions from files.                                                                                                                         |
| `release-notes/`                        | Composite action: commit-prefix changelog / release notes.                                                                                                                       |
| `prune-nightlies/`                      | Composite action: GFS prune of old nightly releases.                                                                                                                             |
| `package-readme/`                       | Composite action + the package README template and rules.                                                                                                                        |
| `repo-readme/`                          | Composite action + the repository README template, the canonical section order and the emoji vocabulary.                                                                         |
| `commit-generated-file/`                | Composite action: put a regenerated file straight onto the working branch, signed, no secret.                                                                                    |
| `assert-generated-file/`                | Composite action: fail a pull request when a generated file is stale. For what cannot be committed.                                                                              |

**Generated repos carry no `scripts/` directory.** The scripts live here once and reach every
repo through the composite actions, so they cannot drift out of sync.

## 📝 README conventions

Three facets of one convention: the README at the repository root, the README that ships inside each
NuGet package, and the screenshots a GUI repository owes its readers. They share one emoji vocabulary,
and a section that appears in both conventions carries the same name and emoji in both.

### Repository READMEs

The README at the root of every `Hawkynt/*` repo follows one structure, and the
[`repo-readme`](repo-readme/) action enforces it. Nothing is copied into a consumer repo; the repo
opts in on the shared workflow:

```yaml
    with:
      repo-readme: true
      repo-readme-gui: true     # the repo ships a user interface
```

The README is a funnel. The pitch and one hero image catch the reader, `## 🧭 Vision` and
`## ✨ Features` say what the thing is, `## 📦 Installation` and `## 🚀 Quick start` get them running,
`## 🖼️ Screenshots` and the free band go deeper, and everything a *contributor* needs closes the
file. The checker reads the frame, the section order, the emoji vocabulary, the Support and License
bodies — which it takes from `.github/FUNDING.yml`, so the README and the Sponsor button can no
longer disagree — and every relative link.

Relative links are *correct* here, which is the one place this convention deliberately contradicts the
package one below: a repo README renders on github.com, where `[LICENSE](LICENSE)` survives a fork and
an absolute blob URL does not.

[`repo-readme/README.md`](repo-readme/README.md) is the single home of the canonical order and the
emoji vocabulary, and [`repo-readme/TEMPLATE.md`](repo-readme/TEMPLATE.md) is the skeleton to copy.

### Package READMEs

Every NuGet package published from a `Hawkynt/*` repo follows one template, and the
[`package-readme`](package-readme/) action enforces it. The template is **never copied into a
consumer repo** — no `docs/` folder, no vendored script; the repo just calls the action:

```yaml
      - name: Check package READMEs
        uses: Hawkynt/RepositoryTemplate/package-readme@v1
```

It also generates each package's API reference — every public and protected type and member, read
from the built assembly's metadata and merged with its XML docs, with show-off examples taken from
`<example>` tags in the source. It is written to the package's own **`REFERENCE.md`**, and the
README's `## 📚 API reference` section carries one line pointing at it. Both are committed, and the
check fails when either no longer matches the assembly, so the reference cannot quietly go stale.

The reference is a file of its own because it outgrew the README: `FrameworkExtensions.Corlib`
generates about 973 KB across 382 types, and a README that size is not a README — nuget.org truncates
it and the paragraphs a consumer needs first are buried under four hundred types. The pointer is an
absolute URL, because a package README renders on nuget.org where a relative link resolves nowhere.

### GUI screenshots

A GUI repository does **not** satisfy its documentation obligation with one startup-window picture.
The README/docs must show the application's primary user-facing surfaces: every main top-level
window or dialog that represents a distinct workflow or substantial state should have its own
committed screenshot. That normally includes the main window and, where the application has them,
settings/preferences, import/open/add flows, editors/configuration dialogs, export/save/publish
flows, previews/results/reports, substantial wizards, and other first-class work surfaces. Trivial
message boxes, confirmation prompts, and visually duplicate variants are not separate documentation
surfaces.

The screenshots are generated product documentation, not manually staged marketing art. **The
application itself must provide deterministic demo scenarios for them.** A documentation-only
command-line mode, internal entry point, or equivalent mechanism should be able to open each target
surface with plausible demo data and capture the real production UI without operator interaction.
The exact command is project-specific; the requirements are not:

- Reuse the production controls and the real presenter/view-model/domain objects. Do not paint fake
  rows over a form, stitch images, or keep a second screenshot-only mock UI that can drift away from
  the application.
- Give the screenshot something worth looking at. Use representative, believable data: multiple
  items/rows where appropriate, meaningful names and values, different statuses, optional fields,
  pending edits, warnings, conversions, edge cases, or other states that demonstrate what the
  surface actually does. An empty dialog is reproducible but useless.
- Keep every scenario deterministic and safe for CI: fixed values/seeds/timestamps, no personal data,
  no live network/cloud dependency, and no required third-party executable when equivalent
  pre-parsed or in-memory data can drive the production UI.
- Make the scenarios independently addressable so CI can capture every primary surface directly.
  Adding or materially changing a primary dialog/window means adding or refreshing its demo scenario
  and screenshot in the same pull request.
- Keep images in a predictable location such as `screenshots/` or `docs/screenshots/`, use
  descriptive kebab-case filenames, and reference them from README/docs with useful alt text. Text
  remains authoritative; screenshots complement it rather than becoming the only documentation.

GitHub's own documentation follows the same broad principle: screenshots should make UI easier to
understand, use descriptive filenames, include enough surrounding context to orient the reader, and
carry alt text. The house rule here is intentionally stronger for application repositories: primary
product surfaces are part of the product documentation and are therefore expected to be shown.

All of these screenshots belong in `generate.yml`. A working-branch push should build the app once,
produce every expected screenshot, sanity-check the files, and commit each changed generated image
through `commit-generated-file@v1`. Do not leave secondary dialogs as manually refreshed images just
because the startup window already has automation.

## 🚦 When things run

Four stages, each doing the cheapest thing that is still true. **A push to `main` is forbidden** —
the `DontDelete` ruleset takes changes through pull requests only.

| Event                            | What runs                                                                                                                                                                                                                       | Cost           |
| -------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------- |
| Push to a working branch         | `smoke.yml` — the fast tier: one OS, fast tests only, no coverage, no package-README check. And `generate.yml` — regenerates the derived files (screenshots, tables, docs) and commits them **straight back onto that branch**. | two small jobs |
| Pull request opened or pushed to | `ci.yml` — the full battery: every OS, every category, coverage. A newer run supersedes the older one.                                                                                                                          | the matrix     |
| Merge to `main`                  | `nightly.yml` — builds and publishes the nightly. It does not re-test.                                                                                                                                                          | one build      |
| Manual dispatch                  | `release.yml` — runs CI itself, packs, publishes, tags.                                                                                                                                                                         | everything     |

The generation stage is what keeps the battery honest without making it expensive. By the time a
pull request exists the derived files are already part of it, so the battery only has to *test*.

The smoke stage is what keeps the battery from being *misused*. Without a fast answer on push, the
way to find out whether your code compiles is to open a pull request — which runs the expensive tier
ten times per change instead of once. So a push gets one OS and the quick tests, in minutes.

**A test is in the fast tier unless it says otherwise.** It opts out with `[Category("Slow")]`, or
with one of the categories that are slow by nature — `EndToEnd`, `OsIntegration`, `ExternalInterop`,
`PolyglotInterop`, `Performance`. That direction matters: tagging every *fast* test would mean
touching thousands of them and remembering each new one, and the one somebody forgets would drop out
of the fast tier silently. Opting out **defers** a test and never skips one — the pull request runs
everything. `CONTRIBUTING.md` has the table and the two rules that keep the tiers honest.

Three properties make committing from a branch push safe, and all three are load-bearing:

1. **The commit goes through the contents API, so GitHub signs it.** A commit made by `git` on a
   runner is unsigned, and `required_signatures` is evaluated over a pull request's *commits* — a
   squash merge does not launder it — so an unsigned commit would make the branch unmergeable later.
   Measured with a probe, not assumed.
2. **A `GITHUB_TOKEN` generated-file commit cannot recursively trigger another `push` workflow.**
   There is therefore no generation loop. For a branch that already belongs to an open pull request,
   GitHub can still emit a `pull_request/synchronize` run in `action_required` state for the bot
   commit. If required checks must attach to that generated head, let `generate.yml` explicitly
   `workflow_dispatch` the repo's `ci.yml` after the generated commit, as demonstrated by
   `MassMediaEdit`; no PAT or separate GitHub App secret is required.
3. **It refuses to touch the default branch.**

Nothing runs on a push to `main` except the nightly. Re-running the battery on the merge commit
proves nothing a green pull request has not already proved.

The trade-off, stated plainly: a squash merge produces a commit no run ever saw — the pull request's
tree on a base that may have moved. A semantic conflict between two separately green pull requests
surfaces as a failed nightly build rather than a failed CI run.

A repo's `smoke.yml` is shorter still:

```yaml
on:
  push:
    branches-ignore: [main]

jobs:
  smoke:
    uses: Hawkynt/RepositoryTemplate/.github/workflows/dotnet-smoke.yml@v1
    with:
      solution: MyThing.sln
      dotnet-version: '10.0.x'
```

A repo's `ci.yml` should be a dozen lines:

```yaml
on:
  pull_request:
    branches: [main]
  workflow_call: {}
  workflow_dispatch: {}

jobs:
  ci:
    uses: Hawkynt/RepositoryTemplate/.github/workflows/dotnet-ci.yml@v1
    with:
      solution: MyThing.sln
      dotnet-version: '10.0.x'
      os-matrix: '["ubuntu-latest","windows-latest"]'
```

A GUI repo's `generate.yml` should generate the whole documented surface set, not a single ceremonial
image. For example:

```yaml
on:
  push:
    branches-ignore: [main]

permissions:
  actions: write
  contents: write
  pull-requests: read

jobs:
  screenshots:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      # Build once, then use the application's own deterministic demo mode for every primary surface.
      - run: dotnet build MyThing/MyThing.csproj -c Release
      - run: |
          ./MyThing/bin/Release/net10.0-windows/MyThing.exe --screenshot-demo=main:docs/screenshots/main-window.png
          ./MyThing/bin/Release/net10.0-windows/MyThing.exe --screenshot-demo=settings:docs/screenshots/settings.png
          ./MyThing/bin/Release/net10.0-windows/MyThing.exe --screenshot-demo=editor:docs/screenshots/editor.png
      # Validate every expected image before committing any of them.
      - name: Validate screenshots
        shell: pwsh
        run: |
          @(
            'docs/screenshots/main-window.png',
            'docs/screenshots/settings.png',
            'docs/screenshots/editor.png'
          ) | ForEach-Object {
            if (-not (Test-Path $_)) { throw "Missing generated screenshot: $_" }
            if ((Get-Item $_).Length -lt 10000) { throw "Implausibly small screenshot: $_" }
          }
      # commit-generated-file handles one generated path per invocation; repeat it for the surface set.
      - uses: Hawkynt/RepositoryTemplate/commit-generated-file@v1
        with:
          file: docs/screenshots/main-window.png
          message: '* refresh the main-window screenshot'
      - uses: Hawkynt/RepositoryTemplate/commit-generated-file@v1
        with:
          file: docs/screenshots/settings.png
          message: '* refresh the settings screenshot'
      - uses: Hawkynt/RepositoryTemplate/commit-generated-file@v1
        with:
          file: docs/screenshots/editor.png
          message: '* refresh the editor screenshot'
      - name: Run CI on the generated head
        shell: bash
        env:
          GH_TOKEN: ${{ github.token }}
        run: |
          branch="$GITHUB_REF_NAME"
          generated_head=$(gh api "repos/$GITHUB_REPOSITORY/branches/$branch" --jq '.commit.sha')
          if [ "$generated_head" = "$GITHUB_SHA" ]; then
            exit 0
          fi

          owner="${GITHUB_REPOSITORY%%/*}"
          open_prs=$(gh api --method GET "repos/$GITHUB_REPOSITORY/pulls" \
            -f state=open -f head="$owner:$branch" --jq 'length')
          if [ "$open_prs" -eq 0 ]; then
            exit 0
          fi

          gh workflow run ci.yml --ref "$branch"
```

Anything exotic — a filesystem driver, a GTK autopilot, an AOT publish — stays a job in the repo's
own `ci.yml` beside the call. "Most of it", not all of it.

## 🔢 Versioning model

Two independent numbers, and they answer different questions:

- **Repo marker** — releases tag `vYYYYMMDD`, nightlies `nightly-YYYYMMDD`. Never derived from a git
  tag's contents; it is just the date the release was cut.
- **Package version** — `MAJOR.MINOR.PATCH` from the package's own manifest, plus a build number that
  is **the commit count of that manifest's parent folder**. Two NuGet packages in sibling folders
  therefore get different build numbers reflecting only their own churn, and a package whose folder
  did not change composes the identical version again — so `--skip-duplicate` re-uses what is already
  published instead of republishing everything.

`scripts/version.pl` reads the base from whichever manifest a repo actually has:

| Stack      | File                                 | Field                            | Composed      |
| ---------- | ------------------------------------ | -------------------------------- | ------------- |
| .NET       | `*.csproj` / `Directory.Build.props` | `<Version>`                      | `X.Y.Z.BUILD` |
| Node       | `package.json`                       | `"version"`                      | `X.Y.Z+BUILD` |
| PHP        | `composer.json`                      | `"version"`                      | `X.Y.Z+BUILD` |
| Rust       | `Cargo.toml`                         | `[package] version`              | `X.Y.Z+BUILD` |
| Perl       | `*.pm`                               | `$VERSION`                       | `X.Y.Z.BUILD` |
| C/C++      | `CMakeLists.txt`                     | `project(… VERSION …)`           | `X.Y.Z.BUILD` |
| QuickBASIC | `*.SUB` / `*.BAS`                    | `%…_VERSION_MAJOR/_MINOR/_PATCH` | `X.Y.Z.BUILD` |
| any        | root `VERSION`                       | the file's contents              | `X.Y.Z.BUILD` |

Node, PHP and Rust are SemVer, which rejects a fourth numeric component, so their build number lands
in build metadata (`+BUILD`). A repo with no manifest of its own just needs a root `VERSION` file.
`.NET` projects may inherit their base from the nearest ancestor `Directory.Build.props`; the build
number then follows the *declaring* file's folder.

## 🧰 Composite actions

Eight of them, each doing one thing and each called by tag, never copied: `nuget-publish`,
`stamp-version`, `release-notes`, `prune-nightlies`, `package-readme`, `repo-readme`,
`commit-generated-file` and `assert-generated-file`.

Only `package-readme` and `repo-readme` carry a README of their own, because both own a convention
that has to be written down somewhere. The other six document themselves through the `description`
of every input in their `action.yml`, and `nuget-publish` is spelled out below because it is the one
with a policy to configure on the far side.

### `nuget-publish`

Publishes packages to nuget.org over
[Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing): it exchanges
the job's GitHub OIDC token for a short-lived API key, pushes every `.nupkg` and `.snupkg` in a
directory, then polls the flat-container index and **fails if a package was accepted but never became
available** (the silent-rejection case). When no Trusted Publishing policy is configured it falls
back to a stored API key.

```yaml
jobs:
  publish-nuget:
    runs-on: ubuntu-latest
    permissions:
      contents: read
      id-token: write          # required for Trusted Publishing
    steps:
      - uses: actions/download-artifact@v4
        with: { name: nuget-packages, path: dist-nuget }
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }

      - uses: Hawkynt/RepositoryTemplate/nuget-publish@v1
        with:
          packages-path: dist-nuget
          user: ${{ secrets.NUGET_USER }}
          nuget-token: ${{ secrets.NUGET_TOKEN }}   # optional fallback
```

Trusted Publishing needs a policy on nuget.org (your username ▸ Trusted Publishing) naming the
repository and the workflow file that calls the action.

| Input             | Required | Default                               | Description                                               |
| ----------------- | -------- | ------------------------------------- | --------------------------------------------------------- |
| `packages-path`   | yes      | —                                     | Directory holding the `.nupkg`/`.snupkg` files to push.   |
| `user`            | no       | `""`                                  | nuget.org account name for Trusted Publishing.            |
| `nuget-token`     | no       | `""`                                  | Fallback API key, used only when no policy is configured. |
| `source`          | no       | `https://api.nuget.org/v3/index.json` | Push source.                                              |
| `timeout-seconds` | no       | `900`                                 | How long to wait for availability before failing.         |

## ❤️ Support

If this project saves you time or money, consider supporting its development:

[![GitHub Sponsors](https://img.shields.io/badge/GitHub-Sponsor-EA4AAA?logo=githubsponsors)](https://github.com/sponsors/Hawkynt)
[![PayPal](https://img.shields.io/badge/PayPal-Donate-00457C?logo=paypal)](https://www.paypal.me/hawkynt)

## 📜 License

Licensed under LGPL-3.0-or-later — see [LICENSE](LICENSE).
