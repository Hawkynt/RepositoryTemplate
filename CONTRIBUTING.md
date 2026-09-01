# Contributing

Thanks for helping improve **ProjectName**. This guide covers building, testing, and the conventions
every change is expected to follow. Coding agents must also read [`AGENTS.md`](AGENTS.md).

## Prerequisites

- A recent [.NET SDK](https://dotnet.microsoft.com/download) matching the `TargetFramework` in
  `Directory.Build.props`.
- `perl` and `node` are only needed to run the release/versioning scripts locally; CI provides them.

## Build

```bash
dotnet restore ProjectName.sln
dotnet build ProjectName.sln -c Release --no-restore
```

## Test

```bash
dotnet test ProjectName.sln -c Release
```

Tests are [NUnit](https://nunit.org). New behaviour is test-first: add the failing test, then make it
pass. Keep test data deterministic (fixed seeds/strings) and generate it in setup rather than
committing large binary fixtures.

## Commit conventions

- One concern per commit, with a descriptive body.
- Subject lines start with a prefix — `+` added · `-` removed · `*` changed · `#` bug fixed ·
  `!` critical todo. Never begin with "fix"/"changed"/"modified".
- Write everything as if authored by hand: no AI attribution anywhere.

## Code style

- Allman braces, 4-space indent (C#), file-scoped namespaces, `_camelCase` private fields, `this.`
  qualification, XML docs on public members, LF endings.
- `Nullable` and `ImplicitUsings` are enabled centrally in `Directory.Build.props`.

## GUI screenshots

GUI applications treat screenshots as generated product documentation. The README/docs should show
**all primary dialogs and top-level windows that represent distinct workflows**, not just whatever
window appears at startup. Typical candidates are the main window, settings/preferences,
import/open/add flows, editors/configuration dialogs, export/save/publish flows, previews/results,
reports, and substantial wizards. Do not multiply screenshots for trivial confirmation boxes or
visually identical variants.

Each documented surface needs an application-owned demo scenario. Prefer a hidden/documentation-only
startup option such as `--screenshot-demo=<scenario>:<output>` or an equivalent internal entry point
that opens the real UI in a deterministic state and writes the image without operator interaction.
The exact mechanism is project-specific; these properties are not:

- Build the scenario from the application's real domain models, presenters/view-models and controls.
  Do not draw fake table rows over the UI, stitch screenshots, or maintain a parallel mock screen.
- Populate enough plausible data to make the screenshot useful: multiple representative items,
  meaningful names and values, different statuses, optional fields, edits/warnings where relevant,
  and edge cases worth seeing. An empty dialog is technically reproducible and practically useless.
- Keep it deterministic and private: fixed values/seeds/timestamps, no personal data, no live network
  or cloud dependency, and no dependency on locally installed third-party tools when equivalent
  in-memory/pre-parsed data can drive the same production UI.
- Make scenarios independently addressable so CI can capture each primary dialog/window directly.
  Adding a new primary surface means adding its scenario and screenshot in the same change.
- Store generated images under a descriptive path such as `screenshots/` or `docs/screenshots/`, use
  descriptive kebab-case filenames, reference them from README/docs with useful alt text, and keep
  the surrounding text authoritative — screenshots complement documentation rather than replacing it.

`generate.yml` should regenerate every committed screenshot on a working-branch push and commit the
changed files through `Hawkynt/RepositoryTemplate/commit-generated-file@v1`. The generation job should
also sanity-check that each expected image exists and is a plausible image before committing it.

## Continuous integration

Workflows live in `.github/workflows/`:

| Workflow | Trigger | Purpose |
|---|---|---|
| `ci.yml` | push / PR to `main` | Cross-platform (Ubuntu + Windows) build and test. |
| `_build.yml` | called by release/nightly | Shared build/pack step so both paths produce identical artifacts. |
| `nightly.yml` | automatically after green CI on `main` | Dated `nightly-YYYYMMDD` prerelease, GFS-pruned. |
| `release.yml` | manual dispatch | Cuts a dated `vYYYYMMDD` release; publishes NuGet packages when configured. |

Versions come from files, not git tags: the shared `stamp-version` action stamps each manifest with
its own folder's commit count, so sibling packages version independently, while the repo-level marker
is simply the date (`vYYYYMMDD` / `nightly-YYYYMMDD`). The versioning, changelog and prune scripts
live in `Hawkynt/RepositoryTemplate` and reach this repo through composite actions — there is no
`scripts/` directory here to keep in sync.

To validate workflow edits, [`actionlint`](https://github.com/rhysd/actionlint) is the recommended
linter:

```bash
actionlint .github/workflows/*.yml
```

## Releases

Stable releases are cut manually by the maintainer:

```bash
gh workflow run release.yml
```

Never cut a release unless explicitly asked.
