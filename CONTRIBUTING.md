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

## Continuous integration

Workflows live in `.github/workflows/`:

| Workflow | Trigger | Purpose |
|---|---|---|
| `ci.yml` | push / PR to `main` | Cross-platform (Ubuntu + Windows) build and test. |
| `_build.yml` | called by release/nightly | Shared build/pack step so both paths produce identical artifacts. |
| `nightly.yml` | automatically after green CI on `main` | Dated `nightly-YYYYMMDD` prerelease, GFS-pruned. |
| `release.yml` | manual dispatch | Cuts a dated `vYYYYMMDD` release; publishes NuGet packages when configured. |

Versions come from files, not git tags — `scripts/version.pl` stamps each project's `<Version>` with
its folder's commit count. To validate workflow edits, [`actionlint`](https://github.com/rhysd/actionlint)
is the recommended linter:

```bash
actionlint .github/workflows/*.yml
```

## Releases

Stable releases are cut manually by the maintainer:

```bash
gh workflow run release.yml
```

Never cut a release unless explicitly asked.
