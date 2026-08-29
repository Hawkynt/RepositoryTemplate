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
| `scripts/` | `version.pl`, `update-changelog.mjs`, `prune-nightlies.mjs`, `package-readme.cs`, `publish-generated-file.sh` — the single copy, used by the actions. |
| `scripts/fixtures/` | The package-readme test fixture and its golden output. |
| `nuget-publish/` | Composite action: Trusted Publishing push with an acceptance check. |
| `stamp-version/` | Composite action: stamp per-package versions from files. |
| `release-notes/` | Composite action: commit-prefix changelog / release notes. |
| `prune-nightlies/` | Composite action: GFS prune of old nightly releases. |
| `package-readme/` | Composite action + the package README template and rules. |
| `publish-generated-file/` | Composite action: commit a generated file through a pull request, signed, without starting a CI loop. |

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

## 🚦 When things run

One rule: **a pull request is the only thing that runs CI, and it has to be green to merge.**

| Event | What runs |
| --- | --- |
| Pull request opened or pushed to | `ci.yml` — the gate. A newer run supersedes the older one. |
| Merge to `main` | `nightly.yml` — builds and publishes the nightly. It does not re-test. |
| Manual dispatch | `release.yml` — runs CI itself, packs, publishes, tags. |

Nothing runs on a push to `main` except the nightly. Re-running the same matrix on the merge commit
proves nothing a green pull request has not already proved, and it was costing a machine every time.

The trade-off, stated plainly: a squash merge produces a commit no run ever saw — the pull request's
tree on a base that may have moved. A semantic conflict between two separately green pull requests
would first surface as a failed nightly build rather than as a failed CI run.

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

Anything exotic — a filesystem driver, a GTK autopilot, an AOT publish — stays a job in the repo's
own `ci.yml` beside that call. "Most of it", not all of it.

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
4. Remove any part of the pipeline the project does not need (e.g. the NuGet publish step for a
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
