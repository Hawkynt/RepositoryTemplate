# Agent guide — ProjectName

Working agreement for **all** coding agents (Claude Code, Codex, Copilot, …) and human contributors
in this repository. These rules are not optional. This file is the per-repo distillation of the
house standard; rewrite the "What this is" section for the concrete project and keep the rest.

## What this is

<!-- One paragraph: what the project is, its stack (e.g. cross-platform C# .NET 10), the solution
     name, and the project layout at the repo root — Core / Cli / Ui / Tests. -->
A C# (.NET 10) project. Solution `ProjectName.sln`; project folders sit at the repo root —
`ProjectName.Core` / `.Cli` / `.Ui` (apps) and `ProjectName.Tests` (NUnit).

**The README is a forward-looking specification** — it is authoritative. When code and spec disagree,
the spec wins unless the spec itself is being revised in the same change; cite the relevant section
in the commit body.

## Commits

- **Group changes semantically** — one requirement/concern per commit, with a detailed body.
- **Every subject line starts with a prefix**: `+` added · `-` removed · `*` changed · `#` bug fixed
  · `!` critical todo.
- Never start a subject with "fix"/"bugfix"/"changed"/"modified".
- **No AI traces anywhere**: no `Co-Authored-By` AI lines, no "Generated with" footers, no agent
  mentions in messages, comments, authorship, or docs. Author identity is the maintainer's own.

## The loop (always, in this order)

1. **Before committing**: `dotnet build ProjectName.sln -c Release` and
   `dotnet test ProjectName.sln -c Release` until green (CI runs the same on ubuntu + windows). New
   behaviour is test-first (TDD): add the failing test, then make it pass. While iterating,
   `--filter "TestCategory!=Slow"` is the fast tier (`TestCategory`, not `Category` - see CONTRIBUTING.md); run the whole suite before you push.
2. **Commit** (rules above) and **push**.
3. **Wait for CI**; on `main` a green CI triggers the nightly (dated prerelease + GFS prune). Fix and
   loop until everything is green.

Stable releases are **manual** (`gh workflow run release.yml`) — never cut one unless explicitly
asked.

## Tests come in two tiers

A test is in the **fast tier** unless it says otherwise — that direction, never the other, because
the fast test somebody forgets to tag would drop out silently. A test opts out with `Slow` (quick in
kind, expensive in practice) or with one of the tiers that are slow by nature: `EndToEnd`,
`OsIntegration`, `ExternalInterop`, `PolyglotInterop`, `Performance`.

Opting out **defers** a test; it never skips one. The pull request runs everything.

- A fast-tier test finishes in well under a second. If yours does not, make it so or tag it `Slow`.
- A test that reads a RATE — CPU against wall-clock, allocations per operation — needs a sustained
  window, and should run to a *duration* rather than a fixed iteration count so the window holds on
  a fast machine and a slow one alike. Over 90 ms you are measuring thread start-up and tiered JIT.
- **Changing a test filter means checking every category still has a step that runs it.** A category
  excluded from the main filter with no step of its own executes nowhere, and nothing reports that.

## Code conventions

- Allman braces (brace on its own line), 4-space indent for C#/csproj/props, file-scoped namespaces,
  `_camelCase` private fields, `this.` qualification, `var` freely, single-statement `if` without
  braces, XML docs on public members. LF line endings.
- Latest C# language version; `Nullable` and `ImplicitUsings` enabled (set in
  `Directory.Build.props`). Warnings-as-errors where the project enables it — do not suppress without
  justification.
- Versions come from files, never git tags. The shared `stamp-version` action stamps each manifest's
  version with the commit count of ITS OWN folder as the build field, so sibling packages version
  independently. The repo-level marker is the date: releases tag `vYYYYMMDD`, nightlies
  `nightly-YYYYMMDD`. This repo carries no `scripts/` — they live in `Hawkynt/RepositoryTemplate`.

## README & repo conventions

- Standard frame: title → grouped shields.io badges → one-line `>` blockquote → body →
  `## ❤️ Support` (Sponsors + PayPal, mirrors `.github/FUNDING.yml`) → `## 📜 License`.
- **GUI repositories document the whole primary UI, not merely the startup window.** Every main
  top-level window/dialog that represents a distinct user workflow or substantial state needs a
  committed screenshot: the main window plus relevant settings/preferences, import/open/add,
  editor/configuration, export/save/publish, preview/results/report, wizard, and comparable primary
  surfaces. Tiny confirmations, trivial message boxes, and duplicate variants do not need their own
  image.
- GUI screenshots are **generated documentation**, never hand-maintained glamour shots. The
  application itself must expose a documentation/demo mode that can populate each screenshot surface
  with deterministic, plausible, visually useful demo data. Reuse the real production controls,
  presenters/view-models, domain objects, formatting, and validation paths; do not paint fake rows or
  maintain a second mock UI solely for screenshots.
- Demo scenarios must be self-contained and reproducible in CI: fixed values, no personal/user data,
  no network dependency, no clock/random dependence unless fixed, and no required external tools or
  services when equivalent pre-parsed/in-memory data can exercise the real UI. Prefer representative
  variety — multiple rows/items, meaningful labels, different statuses, optional fields, pending
  changes, warnings, edge cases — so screenshots explain what the application can actually do.
- `generate.yml` regenerates **all** committed GUI screenshots on working-branch pushes. Adding or
  materially changing a primary dialog/window therefore includes its demo scenario, generated
  screenshot, README/docs reference, and CI generation step in the same change. Use descriptive
  filenames and useful alt text.
- License is LGPL-3.0-or-later (full `LICENSE`); no per-file license headers in `.cs` files.
- CI lives in `.github/workflows/{ci,_build,nightly,release}.yml` with the shared
  `scripts/{version.pl,update-changelog.mjs,prune-nightlies.mjs}`; all workflows set
  `FORCE_JAVASCRIPT_ACTIONS_TO_NODE24`. Releases are dated `vYYYYMMDD`, nightlies `nightly-YYYYMMDD`.
