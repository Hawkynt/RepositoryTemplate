# {{REPO}}

<!--
  The badge block below is generated. Replace {{OWNER}}/{{REPO}} once, or just run
  `node repo-readme.mjs --write . --repo {{OWNER}}/{{REPO}}` and let it write itself.

  Drop the fourth group in a repository that has no releases — a Release badge on a repo that ships
  from a moving tag reads "no releases" forever. Language-specific badges (Go Report Card, a NuGet
  version) go after the groups below.

  This file keeps its {{TOKEN}} placeholders on purpose, and the checker bans them: it only ever
  looks at <root>/README.md, so the template itself is never in scope.
-->

[![License](https://img.shields.io/github/license/{{OWNER}}/{{REPO}})](https://github.com/{{OWNER}}/{{REPO}}/blob/main/LICENSE)
[![Language](https://img.shields.io/github/languages/top/{{OWNER}}/{{REPO}}?color=8957D5)](https://github.com/{{OWNER}}/{{REPO}})

[![CI](https://github.com/{{OWNER}}/{{REPO}}/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/{{OWNER}}/{{REPO}}/actions/workflows/ci.yml)
![Last Commit](https://img.shields.io/github/last-commit/{{OWNER}}/{{REPO}}?branch=main)
![Activity](https://img.shields.io/github/commit-activity/m/{{OWNER}}/{{REPO}})

[![Stars](https://img.shields.io/github/stars/{{OWNER}}/{{REPO}}?color=FFD700)](https://github.com/{{OWNER}}/{{REPO}}/stargazers)
[![Forks](https://img.shields.io/github/forks/{{OWNER}}/{{REPO}}?color=008080)](https://github.com/{{OWNER}}/{{REPO}}/network/members)
[![Issues](https://img.shields.io/github/issues/{{OWNER}}/{{REPO}})](https://github.com/{{OWNER}}/{{REPO}}/issues)
![Code Size](https://img.shields.io/github/languages/code-size/{{OWNER}}/{{REPO}}?color=4CAF50)
![Repo Size](https://img.shields.io/github/repo-size/{{OWNER}}/{{REPO}}?color=FF9800)

[![Release](https://img.shields.io/github/v/release/{{OWNER}}/{{REPO}})](https://github.com/{{OWNER}}/{{REPO}}/releases/latest)
[![Nightly](https://img.shields.io/github/v/release/{{OWNER}}/{{REPO}}?include_prereleases&sort=date&filter=nightly-*&label=nightly&color=FF9800)](https://github.com/{{OWNER}}/{{REPO}}/releases)
[![Downloads](https://img.shields.io/github/downloads/{{OWNER}}/{{REPO}}/total)](https://github.com/{{OWNER}}/{{REPO}}/releases)

> One paragraph. What this is, who it is for, and why they would want it — written so that someone who
> has never heard of it knows by the end of the sentence whether to keep reading. Name the problem, not
> the implementation. This is the hook, so no hedging and no feature list; the sections below have room.

<!--
  GUI repositories: exactly ONE image here, the shot that makes a reader want the rest. The full tour
  belongs under "## 🖼️ Screenshots" further down. Non-GUI repositories delete this line.
-->
![The main window](screenshots/main.png)

## 🧭 Vision

What the project is *for*, and where it is going. Prose, not bullets — this is the paragraph that says
what the thing means, and it is the one section a feature list cannot replace. If the project claims a
whole domain rather than a feature, say so outright: the claim is a commitment, and the gap between
"covers an unusually broad range of X" and "covers every X, and tracks what it does not yet reach" is
the difference between a reader asking whether theirs is in there and knowing the answer.

## ✨ Features

- User-visible capability, not implementation trivia.
- Keep the bullets short. A large capability set belongs in the support matrix below.

## 🧩 Support matrix

Delete unless capability genuinely varies by format, algorithm, codec, container, filesystem, profile
or operation. Link the **name** to a neutral overview, and keep a separate **Reference** column for the
normative specification.

| Format / algorithm | Read | Write | Notes | Reference |
| --- | :---: | :---: | --- | --- |
| [Name](https://en.wikipedia.org/wiki/Example) | ✅ | ⚠️ | The supported profile or deliberate subset | [Specification](https://example.org/spec) |

`✅` full · `⚠️` partial, explained in the row · `—` unsupported.

## 📦 Installation

How to get it running: the release to download, the package to install, the thing to unpack. State the
platform requirements here, so a reader finds out before the example rather than after it.

## 🚀 Quick start

The smallest realistic invocation that proves the value. It must actually work.

```bash
{{REPO}} --help
```

## 🖼️ Screenshots

Every primary user-facing surface — the main window plus settings, import/open, editors, export,
previews, wizards. Not one startup shot. These are generated documentation: the application exposes a
demo mode that populates each surface with deterministic data, and CI regenerates them. Delete this
section for a repository with no user interface.

## 🛠️ Building

```bash
dotnet build -c Release
dotnet test -c Release
```

## 🤝 Contributing

How to propose a change, and what the review expects. Optional.

## 🆘 Getting Help

Where to open an issue and what to put in it. Optional.

## ❤️ Support

If this project saves you time or money, consider supporting its development:

[![GitHub Sponsors](https://img.shields.io/badge/GitHub-Sponsor-EA4AAA?logo=githubsponsors)](https://github.com/sponsors/{{OWNER}})
[![PayPal](https://img.shields.io/badge/PayPal-Donate-00457C?logo=paypal)](https://www.paypal.me/{{PAYPAL}})

## 📜 License

Licensed under LGPL-3.0-or-later — see [LICENSE](LICENSE).
