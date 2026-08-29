# `package-readme` action

> Generates the complete public/protected API reference into every package README and checks each one
> against the house template, so packages published from `Hawkynt/*` read like members of one project.

This is the single home of the package README convention. **Nothing here is copied into a consumer
repository** — no `docs/` folder, no vendored script. A repo that contains NuGet package source calls
the action; the template and rules stay here.

## 📦 Usage

Add one step to the repo's `ci.yml`, after the build:

```yaml
      - name: Check package READMEs
        uses: Hawkynt/RepositoryTemplate/package-readme@v1
```

The projects must already be built in the configuration being checked (default `Release`), because
the reference is read from the compiled assembly and its XML documentation file.

**Run it on one operating system, and pick the one where every target assembly resolves.** A package
targeting `net10.0-windows` cannot resolve its `System.Drawing` types on a runner without the
WindowsDesktop framework, so the generated reference would legitimately differ between Ubuntu and
Windows and the drift check would fail for no reason but the runner. In a matrix job, guard the step
with `if: matrix.os == '...'`.

| Input | Required | Default | Description |
| --- | :---: | --- | --- |
| `mode` | no | `check` | `check` fails on drift or a lint violation; `write` rewrites the READMEs. |
| `root` | no | `.` | Repository root to scan. |
| `configuration` | no | `Release` | Build configuration whose output to read. |
| `target-framework` | no | `""` | Which TFM of a multi-targeted package to document. Empty picks the newest. |
| `project` | no | `""` | Newline-separated `.csproj` paths. Empty means discover every packable project. |
| `dotnet-version` | no | `10.0.x` | SDK channel to install, when the workflow has not already set one. |
| `upload-artifact` | no | `true` | On failure, upload the regenerated READMEs so the fix is one download away. |

## ✨ What it enforces

Structural violations fail the build:

- The H1 is exactly the resolved `PackageId`, so the nuget.org page title matches what you install.
- A badge block directly under the title, then a one-line `>` blockquote.
- The required headings, present and in the canonical order below.
- The package's `REFERENCE.md` matches the assembly, and the `## 📚 API reference` region points at
  it. This is the drift check.
- `PackageReadmeFile` is set, points at a file that exists, and that file is actually packed via a
  `Pack="true"` `None` item. `PackageReadmeFile` names the README inside the package; it does not put
  it there, and without the item `dotnet pack` fails NU5039.
- No relative links. A package README is rendered on nuget.org, where `../LICENSE` resolves nowhere.
- No leftover `{{TOKEN}}` placeholders.

Reported but non-blocking: public members with no `<summary>`, and types with no `<example>`.

## 🧩 Canonical heading order

Package-specific sections may be inserted after **Quick start** and before **Dependencies**. Omit an
optional heading only when it genuinely does not apply.

| Heading | Required | Purpose |
| --- | :---: | --- |
| `## 📦 Installation` | ✅ | `dotnet add package …`, before any example, so a reader can act immediately. |
| `## ✨ Features` | ✅ | User-visible capability. Not implementation trivia. |
| `## 🧩 Support matrix` | ⚠️ | Required when capability varies by format, algorithm, codec or operation. |
| `## 🚀 Quick start` | ✅ | Smallest realistic example proving the package's value. |
| `## 📚 API reference` | ✅ | Generated. One line linking to `REFERENCE.md`, which holds the complete list. |
| `## 🏗️ Architecture` | — | Optional, plus any other package-specific sections. |
| `## 🔌 Dependencies` | ✅ | Table when there is more than one meaningful dependency. |
| `## ⚠️ Limitations` | ✅ | What a green check would otherwise conceal. |
| `## ❤️ Support` | ✅ | Identical everywhere. Do not improvise variants. |
| `## 📜 License` | ✅ | LGPL-3.0-or-later, linked absolutely. |

Use the emoji above for these concepts. Package-specific subheadings may carry their own emoji, but
never reuse a mapped one for a different meaning.

## 🚀 Quick start

Copy [`TEMPLATE.md`](https://github.com/Hawkynt/RepositoryTemplate/blob/main/package-readme/TEMPLATE.md)
into the package folder as `README.md`, wire it up in the `.csproj`, then let CI fill the API region:

```xml
<PropertyGroup>
  <IsPackable>true</IsPackable>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
  <PackageReadmeFile>README.md</PackageReadmeFile>
</PropertyGroup>

<ItemGroup>
  <None Include="README.md" Pack="true" PackagePath="\" />
</ItemGroup>
```

To regenerate locally — no checkout of this repo, and nothing copied in:

```bash
curl -sL https://raw.githubusercontent.com/Hawkynt/RepositoryTemplate/v1/scripts/package-readme.cs -o package-readme.cs
dotnet run package-readme.cs -- --write .
```

## 📚 How the API reference is produced

`GenerateDocumentationFile` must be on; the XML file is where summaries and `<example>` blocks come
from.

1. `dotnet msbuild -getProperty:` evaluates each project. MSBuild is asked rather than the `.csproj`
   read, because `PackageId` is frequently implicit (defaulted from the project filename) and the
   README's `Pack` item is often contributed by a `Directory.Build.props`.
2. The built assembly is opened with `MetadataLoadContext` — **load-only; package code is never
   executed** — and every `public` and `protected` type and member is collected. Because the list
   comes from metadata, the reference is complete by construction.
3. The XML documentation file is merged on by doc-comment ID for summaries and examples.
4. The result is written to `REFERENCE.md` next to the package README, and a one-line pointer to it
   is spliced between the README's markers. Ordering is deterministic (namespace → type → member
   kind → name → signature), so regenerating twice never churns either file.

Compiler plumbing is excluded: property and event accessors appear as their property or event,
delegates show the signature they stand for rather than `BeginInvoke`/`EndInvoke`, and enums do not
advertise the framework interfaces every enum implements.

## ⚠️ Limitations

- Nullable reference annotations are not yet rendered; `string` and `string?` both read as `string`.
- One target framework is described per package. A multi-targeted package defaults to its **newest**
  TFM, which is the wrong choice for a polyfill library — its surface is largest on the *oldest*
  target, and on the newest there may be almost nothing left to polyfill. Set `target-framework`
  explicitly for those.
- Inherited members are not repeated on derived types; each type lists what it declares, with its
  base type named above the table.

## ❤️ Support

If this project saves you time or money, consider supporting its development:

[![GitHub Sponsors](https://img.shields.io/badge/GitHub-Sponsor-EA4AAA?logo=githubsponsors)](https://github.com/sponsors/Hawkynt)
[![PayPal](https://img.shields.io/badge/PayPal-Donate-00457C?logo=paypal)](https://www.paypal.me/hawkynt)

## 📜 License

Licensed under LGPL-3.0-or-later — see the repository [LICENSE](https://github.com/Hawkynt/RepositoryTemplate/blob/main/LICENSE).
