# RepositoryTemplate

[![License](https://img.shields.io/github/license/Hawkynt/RepositoryTemplate)](https://github.com/Hawkynt/RepositoryTemplate/blob/main/LICENSE)
[![Language](https://img.shields.io/github/languages/top/Hawkynt/RepositoryTemplate?color=8957D5)](https://github.com/Hawkynt/RepositoryTemplate)

![Last Commit](https://img.shields.io/github/last-commit/Hawkynt/RepositoryTemplate?branch=main)
[![Issues](https://img.shields.io/github/issues/Hawkynt/RepositoryTemplate)](https://github.com/Hawkynt/RepositoryTemplate/issues)
[![Stars](https://img.shields.io/github/stars/Hawkynt/RepositoryTemplate?color=FFD700)](https://github.com/Hawkynt/RepositoryTemplate/stargazers)
[![Forks](https://img.shields.io/github/forks/Hawkynt/RepositoryTemplate?color=008080)](https://github.com/Hawkynt/RepositoryTemplate/network/members)

> Clean starting point for Hawkynt's C# repositories — the standard scaffolding, the shared CI
> pipeline, and the reusable **`nuget-publish`** Trusted Publishing action, all in one place.

## 🧱 What's in here

| Path | Purpose |
|---|---|
| `LICENSE` | LGPL-3.0-or-later (full LGPLv3). |
| `README.md` | This file — the house README frame to copy: title → badges → `>` tagline → body → Support → License. |
| `AGENTS.md` | Binding working agreement for agents and contributors (commits, the loop, code style). |
| `CONTRIBUTING.md` | Build/test/CI/release guide. |
| `.editorconfig` | Shared formatting (LF, 4-space C#, tabs for sln/Makefile). |
| `.gitignore` | .NET / IDE / test / NuGet ignores. |
| `Directory.Build.props` | Central TFM, nullable, and package/authorship metadata. |
| `.github/FUNDING.yml` | Sponsors + PayPal button (pairs with the README `## ❤️ Support` section). |
| `.github/workflows/` | `ci` · `_build` · `nightly` · `release` — thin, and they call the actions below. Plus `self-test`, which runs *here*. |
| `.github/workflows/dotnet-ci.yml` | **Reusable workflow.** The whole standard CI gate; a repo's own `ci.yml` is a dozen lines calling it. |
| `.github/workflows/nightly-publish.yml` | **Reusable workflow.** The marker, the release and the GFS prune; only the build stays per-repo. |
| `scripts/` | `version.pl`, `update-changelog.mjs`, `prune-nightlies.mjs`, `package-readme.cs`, `commit-generated-file.sh`, `assert-generated-file.sh` — the single copy, used by the actions. |
| `scripts/fixtures/` | The package-readme test fixture and its golden output. |
| `nuget-publish/` | Composite action: Trusted Publishing push with an acceptance check. |
| `stamp-version/` | Composite action: stamp per-package versions from files. |
| `release-notes/` | Composite action: commit-prefix changelog / release notes. |
| `prune-nightlies/` | Composite action: GFS prune of old nightly releases. |
| `package-readme/` | Composite action + the package README template and rules. |
| `commit-generated-file/` | Composite action: put a regenerated file straight onto the working branch, signed, no secret. |
| `assert-generated-file/` | Composite action: fail a pull request when a generated file is stale. For what cannot be committed. |

**Generated repos carry no `scripts/` directory.** The scripts live here once and reach every
repo through the composite actions, so they cannot drift out of sync.

## 📦 Package READMEs

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

## 🖼️ GUI screenshots

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

| Event | What runs | Cost |
| --- | --- | --- |
| Push to a working branch | `smoke.yml` — the fast tier: one OS, fast tests only, no coverage, no package-README check. And `generate.yml` — regenerates the derived files (screenshots, tables, docs) and commits them **straight back onto that branch**. | two small jobs |
| Pull request opened or pushed to | `ci.yml` — the full battery: every OS, every category, coverage. A newer run supersedes the older one. | the matrix |
| Merge to `main` | `nightly.yml` — builds and publishes the nightly. It does not re-test. | one build |
| Manual dispatch | `release.yml` — runs CI itself, packs, publishes, tags. | everything |

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

| Stack | File | Field | Composed |
|---|---|---|---|
| .NET | `*.csproj` / `Directory.Build.props` | `<Version>` | `X.Y.Z.BUILD` |
| Node | `package.json` | `"version"` | `X.Y.Z+BUILD` |
| PHP | `composer.json` | `"version"` | `X.Y.Z+BUILD` |
| Rust | `Cargo.toml` | `[package] version` | `X.Y.Z+BUILD` |
| Perl | `*.pm` | `$VERSION` | `X.Y.Z.BUILD` |
| C/C++ | `CMakeLists.txt` | `project(… VERSION …)` | `X.Y.Z.BUILD` |
| QuickBASIC | `*.SUB` / `*.BAS` | `%…_VERSION_MAJOR/_MINOR/_PATCH` | `X.Y.Z.BUILD` |
| any | root `VERSION` | the file's contents | `X.Y.Z.BUILD` |

Node, PHP and Rust are SemVer, which rejects a fourth numeric component, so their build number lands
in build metadata (`+BUILD`). A repo with no manifest of its own just needs a root `VERSION` file.
`.NET` projects may inherit their base from the nearest ancestor `Directory.Build.props`; the build
number then follows the *declaring* file's folder.

## 🚀 Use this template

```bash
gh repo create Hawkynt/MyNewApp --template Hawkynt/RepositoryTemplate --private --clone
```

Then, in the new repo:

1. Replace `ProjectName` in `AGENTS.md`, `CONTRIBUTING.md`, and `Directory.Build.props` with the real
   solution/app name, and adjust the `TargetFramework`.
2. Rewrite the README body (keep the frame) and the AGENTS "What this is" section.
3. Point the workflows at the real solution and projects (they carry `ProjectName` placeholders and a
   guard so they no-op until then).
4. **For GUI applications, enumerate the primary dialogs/windows, add deterministic in-app demo
   scenarios for each, reference their screenshots from the README/docs, and make `generate.yml`
   regenerate the full set.** Do this while the UI is built, not as a later documentation cleanup.
5. Remove any part of the pipeline the project does not need (e.g. the NuGet publish step for a
   binary-only app).

## 📦 `nuget-publish` action

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

| Input | Required | Default | Description |
|---|---|---|---|
| `packages-path` | yes | — | Directory holding the `.nupkg`/`.snupkg` files to push. |
| `user` | no | `""` | nuget.org account name for Trusted Publishing. |
| `nuget-token` | no | `""` | Fallback API key, used only when no policy is configured. |
| `source` | no | `https://api.nuget.org/v3/index.json` | Push source. |
| `timeout-seconds` | no | `900` | How long to wait for availability before failing. |

## ❤️ Support

If this project saves you time or money, consider supporting its development:

[![GitHub Sponsors](https://img.shields.io/badge/GitHub-Sponsor-EA4AAA?logo=githubsponsors)](https://github.com/sponsors/Hawkynt)
[![PayPal](https://img.shields.io/badge/PayPal-Donate-00457C?logo=paypal)](https://www.paypal.me/hawkynt)

## 📜 License

Licensed under LGPL-3.0-or-later — see [LICENSE](LICENSE).
