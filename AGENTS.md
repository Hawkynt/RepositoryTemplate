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

## Sourcing an implementation

Never write a format, codec, cipher or compression scheme out of your own understanding when
somebody has already got it right. Work **down** this ladder, stop at the first rung that applies,
and say in the commit body which rung you used and why the ones above it did not.

**0 — One of our own packages already owns it.** Before the ladder below, check. These packages are
published, and each owns the byte-level parsing and writing of everything in its domain:

| Package | Owns |
| --- | --- |
| `Hawkynt.FileFormats.Images` | still and animated images, including icon and cursor containers — ICO, CUR, ANI, ICNS, Xcursor — and their frame payloads |
| `Hawkynt.FileFormats.Archives` | archives and general containers |
| `Hawkynt.FileFormats.FileSystems` | on-disk volumes and their directory structures |
| `Hawkynt.FileFormats.Audio` | audio containers and codecs |
| `Hawkynt.FileFormats.Video` | video containers and codecs |
| `Hawkynt.Compression.Core` | compression and entropy-coding primitives |
| `Hawkynt.Algorithms.Hashing`, `.Checksums` | digests and checksums |

Take a `PackageReference` and use it. If it is *nearly* right, fix it there and consume the fix — a
correction in the package reaches every consumer, a correction in your copy reaches one.

**The codec/container split is the line, not the folder.** One file can legitimately be two things:
an `.ico` is an image to a viewer and a listable container of images to an archive tool, so both
`Images` and `Archives` may expose a view of it. What must not happen is both *parsing* it. The
owning package parses; the other package's view delegates. Capabilities that belong to the view
rather than to the bytes — enumerating entries, modifying in place, descriptor metadata — stay with
the view.

**Why this is a rung and not a preference.** This repository has already paid for it inside a single
package: 195 readers each kept two copies of their parse, one taking a span and one taking an array,
and the Prism Paint size correction landed in only one of them. The corpus read files by name and
reported them perfect while the by-bytes tests failed against a reader that looked correct. The note
in `FormatRegistration.cs` states the conclusion plainly — *a second copy is a correction waiting to
be applied to only half of it* — and that is just as true one level up, where the two copies are in
different packages and no single test suite sees both.

So: if you are about to add a parser for a format the table above already covers, you are adding the
second copy. Say so and stop.

**1 — Licence-compatible source you can take.** MIT, BSD, Apache-2.0, LGPL, public domain: anything
this repository's LGPL-3.0-or-later can absorb. Search for it before writing anything. There are two
ways to take it and the choice is not cosmetic:

- **Vendor it** — a verbatim subtree under `Vendored/<Library>/` next to its own `LICENSE.txt`, kept
  in the upstream's own formatting. Do *not* restyle it: the whole point is that the next upstream
  version still applies cleanly, and a reformatted copy conflicts on every update. Keep it out of
  the published API surface with the `exclude-namespace` input of the `package-readme` action rather
  than by editing the source.
- **Convert it** — carry the algorithm across into this codebase properly. Converted code is *our*
  code, so every rule under "Code conventions" applies to it, including the current C# language
  version (C# 14) wherever that says the same thing more plainly. Do not restate those rules
  here or anywhere else: one stale copy of them is how this guide spent years asking for a brace
  style the code had never used. A conversion that still reads like C, or like a decompiler's
  output, is not finished.

Either way, record where it came from — a `THIRD_PARTY_NOTICES.md` in the package, or a
`THIRD-PARTY-NOTICE.<Name>.txt` beside the code. Attribution is a licence term, not a courtesy.

**2 — Licence-incompatible source: use it, but not its code.** GPL where we ship LGPL, anything
proprietary, anything with no licence at all. Read it and *build material from it*: a written
specification, a set of test cases, and a third-party oracle you can run to produce expected output.
Then implement from that derived material. Do not paste it, do not transliterate it line by line,
and do not carry its file layout or its identifier names across — that is still the same copy.

**Constants are not expression.** Tables, S-boxes, magic numbers, CRC polynomials, Huffman code
tables, quantisation matrices, window and filter coefficients: copy them exactly, from whichever
source is authoritative, on every rung of this ladder. A re-derived S-box is simply a wrong S-box,
and a table somebody worked out for themselves is the defect that nothing catches until real files
arrive. Where a value is arbitrary-but-agreed, matching it *is* the specification.

**3 — Original reference material.** The specification, the standard (RFC, ITU-T, ISO, ECMA), the
academic paper, the vendor's own documentation, the format author's write-up. Prefer the normative
text over anybody's description of it; where the two disagree, the normative text wins and the
disagreement is worth a comment.

**4 — Other trusted sources.** Reverse-engineering write-ups, articles and blog posts by named
people with a track record, and long-lived project wikis that cite their evidence.

**5 — Untrusted material, by agreement only.** Forum answers, unattributed gists, wiki edits with no
provenance. Only when nothing above exists, and only where several *independent* sources agree —
majority vote, discounting the ones that plainly copied each other. Treat the result as a hypothesis
and mark it as one in the code.

Whatever rung you land on, the finished implementation is judged the same way: it must agree with an
oracle or with real files, not merely compile and look plausible. When a licence-incompatible
implementation was your oracle, keep the comparison as a test wherever it can run, and where it
cannot, commit the captured expected output with a note saying what produced it.

## Code conventions

- K&R braces (the opening brace ends the line that opens the block; `} else` and `} catch`
  continue the closing brace's line), 2-space indent for C#/csproj/props, file-scoped
  namespaces, `_camelCase` private fields, `this.` qualification, `var` freely,
  single-statement `if` without braces, XML docs on public members. LF line endings.
- Latest C# language version; `Nullable` and `ImplicitUsings` enabled (set in
  `Directory.Build.props`). Warnings-as-errors where the project enables it — do not suppress without
  justification.
- Versions come from files, never git tags. The shared `stamp-version` action stamps each manifest's
  version with the commit count of ITS OWN folder as the build field, so sibling packages version
  independently. The repo-level marker is the date: releases tag `vYYYYMMDD`, nightlies
  `nightly-YYYYMMDD`. This repo carries no `scripts/` — they live in `Hawkynt/RepositoryTemplate`.

## README & repo conventions

- Standard frame: title → grouped shields.io badges → one-line `>` blockquote → for a GUI repo one
  hero image → body → `## ❤️ Support` (Sponsors + PayPal, mirrors `.github/FUNDING.yml`) →
  `## 📜 License`. The body is a funnel: Vision and Features say what the thing is, Installation and
  Quick start get the reader running, the free band goes deeper, and what a *contributor* needs —
  dependencies, building, contributing, issues — comes last.
- **The canonical section order and the emoji vocabulary live in [`repo-readme/README.md`](repo-readme/README.md),
  and the `repo-readme` action enforces them.** One emoji means one thing, a section shared with the
  package convention carries the same name and emoji on both sides, and `## 📜 License` is the last
  heading in the file. The tables are not reproduced here for the reason given under "Sourcing an
  implementation": one stale copy is how this guide spent years asking for a brace style the code had
  never used.
- **GUI repositories document the whole primary UI, not merely the startup window.** Every main
  top-level window/dialog that represents a distinct user workflow or substantial state needs a
  committed screenshot: the main window plus relevant settings/preferences, import/open/add,
  editor/configuration, export/save/publish, preview/results/report, wizard, and comparable primary
  surfaces. Tiny confirmations, trivial message boxes, and duplicate variants do not need their own
  image. A GUI repo sets `repo-readme-gui: true` on the shared workflow, which makes both the single
  hero image under the pitch and the `## 🖼️ Screenshots` tour required rather than merely expected.
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
