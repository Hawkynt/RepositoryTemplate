# `repo-readme` action

> Checks the repository-root README against the house convention — the frame, the canonical section
> order, and the emoji vocabulary — so that every `Hawkynt/*` repository reads like a member of one
> project rather than like whoever wrote it last.

This is the single home of the repository README convention, and of the emoji vocabulary that the
[`package-readme`](../package-readme/) convention shares with it. **Nothing here is copied into a
consumer repository** — no `docs/` folder, no vendored script; the repo calls the action.

This file is an *action* document, not a repository README, so it is not subject to the rules below.

## 📦 Usage

Opt in on the shared workflow:

```yaml
    uses: Hawkynt/RepositoryTemplate/.github/workflows/dotnet-ci.yml@v1
    with:
      repo-readme: true
      repo-readme-gui: true     # the repo ships a user interface
```

Or call the action directly:

```yaml
      - name: Check the repository README
        uses: Hawkynt/RepositoryTemplate/repo-readme@v1
```

| Input | Required | Default | Description |
| --- | :---: | --- | --- |
| `mode` | no | `check` | `check` fails on a blocking finding; `write` normalizes the badge block. |
| `root` | no | `.` | Repository root holding the `README.md`. |
| `repo` | no | `${{ github.repository }}` | `owner/name` the badges and the H1 are checked against. |
| `gui` | no | `false` | Makes the hero image and `## 🖼️ Screenshots` required. |
| `allow` | no | `""` | Newline-separated rules to waive, `rule` or `rule:argument`. |
| `license` | no | `LGPL-3.0-or-later` | SPDX id the License section must name. |
| `sponsor` / `paypal` | no | from `FUNDING.yml` | Accounts the Support section must point at. |

It needs no toolchain and reads nothing but text — no build, no SDK, no matrix — which is why it runs
as its own job beside the tests rather than after them, and why it can afford to run in the fast tier
as well.

## ✨ What it enforces

The README is a funnel. The pitch and one hero shot catch the reader; Vision and Features say what the
thing is; Installation and Quick start get them running; Screenshots and the free band let them go
deeper; and everything a *contributor* needs closes the file.

```
# Title

<badge block>

> One-paragraph pitch — what it is and why someone would want it.

![Main window](screenshots/main.png)   ← GUI repos: exactly ONE hero image, no heading

## 🧭 Vision
…
## 📜 License
```

Blocking:

- Exactly one H1, and it is the first thing in the file.
- A badge block under it carrying at least a License and a CI badge, every badge pointing at **this**
  repository. A generated repo whose badges still name `RepositoryTemplate`, or a renamed repo whose
  badges now 404, is the commonest real defect and this is the rule that catches it.
- A one-paragraph `>` pitch under the badges.
- For a GUI repo, exactly one image between the pitch and the first section, and a
  `## 🖼️ Screenshots` section that actually contains images.
- The required sections, present, named and decorated as the vocabulary says, and in the canonical
  order. No section said twice. `## 📜 License` last.
- The Support and License bodies, byte for byte, with the sponsor and PayPal accounts read from
  `.github/FUNDING.yml` — which also means the README and the repository's Sponsor button can no
  longer disagree without something noticing.
- Every relative link and image resolving to a file that exists.
- No leftover `{{TOKEN}}`, `ProjectName` or `RepoName` placeholders.

Advisory — reported, never red: an H1 that is a display title rather than the repo slug
(`# 2D Image Filter` is deliberate), missing recommended badges, badge groups out of order, a pitch
that runs long, free-band sections with no emoji, and an absolute self-link that could be relative.

**There is no relative-link ban here, and adding one would be a mistake.** That rule exists in
`package-readme` because a package README is rendered on nuget.org, where `../LICENSE` resolves
nowhere. A repository README is rendered on github.com, where `[LICENSE](LICENSE)` is correct and the
absolute `https://github.com/Owner/Repo/blob/main/LICENSE` form is actively worse — it breaks on a
fork and on a rename.

Formatting is not checked: trailing whitespace, tabs and line length belong to `.editorconfig`, and a
prose linter would be a second opinion nobody asked for.

## 🧩 Canonical heading order

Sections marked 🅿️ are shared with the [package README convention](../package-readme/README.md) and
carry the **identical name and emoji** there. Changing one side without the other is exactly the
drift this exists to prevent.

| Heading | Required | 🅿️ | Purpose |
| --- | :---: | :---: | --- |
| `## 🧭 Vision` | ✅ | | What the project is for and where it is going. Prose, not bullets. |
| `## ✨ Features` | ✅ | 🅿️ | User-visible capability. Not implementation trivia. |
| `## 🧩 Support matrix` | ⚠️ | 🅿️ | When capability varies by format, algorithm, codec or operation. |
| `## 📦 Installation` | ✅ | 🅿️ | Download, package manager, or unpack. |
| `## 🚀 Quick start` | ✅ | 🅿️ | The smallest realistic invocation that proves the value. |
| `## 🖼️ Screenshots` | GUI | | The full UI tour — every primary surface, not one more shot. |
| — | — | | **The free band.** Project-specific sections, any order. |
| `## 🏗️ Architecture` | — | 🅿️ | Closes the free band by convention. |
| `## 🔌 Dependencies` | — | 🅿️ | A table once there is more than one meaningful dependency. |
| `## ⚠️ Limitations` | — | 🅿️ | What a green check would otherwise conceal. |
| `## 🛠️ Building` | ✅ | | Build from source, run the tests. |
| `## 🤝 Contributing` | — | | |
| `## 🆘 Getting Help` | — | | Issues, bug reports, where to ask. |
| `## ❤️ Support` | ✅ | 🅿️ | Sponsors + PayPal. Identical everywhere; do not improvise variants. |
| `## 📜 License` | ✅ | 🅿️ | Linked **relatively**, and last in the file. |

Package READMEs put `## 📦 Installation` first instead, before Features, because `dotnet add package`
is the one thing a package reader wants immediately. A repository reader needs to know what the thing
is before installing it. That single ordering difference is deliberate; the names and emoji are not
allowed to differ.

## 🎨 Emoji vocabulary

One emoji, one meaning. A repo may invent as many free-band sections as it likes, but it may not head
one with an emoji this table has already spent — and the fifteen ranked emoji above are enforced.

| Emoji | Concept | | Emoji | Concept |
| --- | --- | --- | --- | --- |
| 🧭 | Vision | | 📚 | Documentation / API reference |
| ✨ | Features | | ⚙️ | How it works / Configuration |
| 🧩 | Support matrix | | 📁 | Project structure / Layout |
| 📦 | Installation | | 🧪 | Testing |
| 🚀 | Quick start | | 📈 | Performance |
| 🖼️ | Screenshots | | 🛡️ | Security |
| 🏗️ | Architecture | | 📋 | Status |
| 🔌 | Dependencies | | 🗺️ | Roadmap |
| ⚠️ | Limitations | | 💡 | Inspiration / Prior art |
| 🛠️ | Building | | 🤖 | CI |
| 🤝 | Contributing | | 💻 | Platform support |
| 🆘 | Getting Help | | | |
| ❤️ | Support | | | |
| 📜 | License | | | |

The left column is enforced; the right column is the vocabulary to reach for in the free band, so that
the same idea looks the same in every repository. `📚` covers reference material in both conventions,
under the name that fits: packages say *API reference*, repositories say *Documentation*.

## 🚀 Quick start

Copy [`TEMPLATE.md`](https://github.com/Hawkynt/RepositoryTemplate/blob/main/repo-readme/TEMPLATE.md)
to the repository root as `README.md`, replace the tokens, then let the badge block write itself:

```bash
curl -sL https://raw.githubusercontent.com/Hawkynt/RepositoryTemplate/v1/scripts/repo-readme.mjs -o repo-readme.mjs
node repo-readme.mjs --write . --repo Hawkynt/MyApp
node repo-readme.mjs --check . --repo Hawkynt/MyApp
```

No checkout of this repository, and nothing vendored in.

`--write` normalizes what is already there: it gives each badge the right slug and the right group
position and adds a missing License or CI badge. It deliberately does **not** add the recommended
ones — a repository that ships from a moving tag has no releases, and inventing a Release badge for it
would have the tool make a claim the repository cannot back.

## 🏗️ Architecture

`scripts/repo-readme.mjs` is one file with no dependencies. Every rule carries a stable kebab name,
which is what makes `allow` addressable, what the golden fixture asserts on, and what appears in the
`::error file=…,line=…::` annotations so findings land on the pull-request diff where they belong.

The order rule is one non-decreasing check over the ranks of **all** headings, with the free band
sitting at a rank in the middle. That single formulation says both "the canonical sections come in
this order" and "project-specific sections live in the band between Screenshots and Architecture",
and unlike checking only the required subset it will not let a project section drift below the
licence.

`scripts/fixtures/repo-readme/defective.md` is assembled from defects that were actually found in the
corpus — a badge block still naming the template, `Usage` for `Quick start`, `🛠️` borrowed for
something that is not Building, a licence in the middle of the file, and a support section said twice
— so the fixture is evidence rather than invention.

## ⚠️ Limitations

- One README per repository: the root `README.md`. Package READMEs are `package-readme`'s job, and
  `docs/` is nobody's.
- The badge check matches on the visible **label**, not the URL, because two repos legitimately use
  different CI badge providers. A badge mislabelled `License` while pointing at something else passes.
- `--write` touches only the badge block. Reordering sections is a judgement call about prose and is
  left to a human.
- Section bodies are unread apart from Support and License. Nothing checks that `## ✨ Features`
  actually lists features.

## ❤️ Support

If this project saves you time or money, consider supporting its development:

[![GitHub Sponsors](https://img.shields.io/badge/GitHub-Sponsor-EA4AAA?logo=githubsponsors)](https://github.com/sponsors/Hawkynt)
[![PayPal](https://img.shields.io/badge/PayPal-Donate-00457C?logo=paypal)](https://www.paypal.me/hawkynt)

## 📜 License

Licensed under LGPL-3.0-or-later — see the repository [LICENSE](../LICENSE).
