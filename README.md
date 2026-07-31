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
| `.github/workflows/` | `ci` · `_build` · `nightly` · `release` plus `scripts/{version.pl, update-changelog.mjs, prune-nightlies.mjs}`. |
| `nuget-publish/` | Composite action: Trusted Publishing push with an acceptance check. |

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
