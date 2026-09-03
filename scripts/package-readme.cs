#!/usr/bin/env dotnet
#:package System.Reflection.MetadataLoadContext@9.0.0
// -----------------------------------------------------------------------------
//  package-readme.cs — the package README generator + linter, identical in every
//  Hawkynt repo. Consumed through the composite action; never copied into a repo.
//
//  It does two things for every packable project it finds:
//
//   1. GENERATES the "## 📚 API reference" body from the BUILT ASSEMBLY's metadata
//      merged with its XML documentation file, and splices it between the
//      <!-- API:BEGIN --> / <!-- API:END --> markers. The member list comes from
//      metadata, so the reference is complete by construction — it cannot drift
//      into being partial the way a hand-written one does. Prose outside the
//      markers is never touched.
//
//   2. LINTS the README against the house package template (see
//      package-readme/README.md): heading set and order, H1 == PackageId, badge
//      block, blockquote, absolute links, and that the README is actually wired
//      into the package via PackageReadmeFile + a Pack'd None item.
//
//  Examples are authored as XML <example> tags on the type in the C# source, so
//  they live next to the code they demonstrate, show up in IntelliSense, and are
//  regenerated rather than maintained by hand.
//
//  The assembly is read through MetadataLoadContext — load-only. Package code is
//  never executed.
//
//  Usage:
//    dotnet run package-readme.cs -- --check [repoRoot]   lint + fail on drift
//    dotnet run package-readme.cs -- --write [repoRoot]   rewrite API regions
//    dotnet run package-readme.cs -- --self-test          run the built-in suite
//
//  Options:
//    --configuration <cfg>       build configuration to read (default: Release)
//    --target-framework <tfm>    which TFM of a multi-targeted package to document.
//                                Defaults to the newest, which is wrong for a polyfill
//                                library whose surface is largest on the OLDEST target.
//    --project <path>        restrict to one .csproj (repeatable)
//    --exclude-namespace <ns>    omit types in this namespace or below it (repeatable).
//                                For VENDORED third-party source that a package bundles: it is
//                                public because the vendor made it public, it is not this
//                                package's API, and documenting it buries the real surface.
//                                Excluding it here beats editing the vendored source, which
//                                would conflict on every upstream sync.
//    --verbose
//
//  Exit: 0 success · 1 lint/drift failure · 2 bad usage or environment error.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

return Cli.Run(args);

// =============================================================================
//  Command line
// =============================================================================

static class Cli {
  public static int Run(string[] args) {
    var mode = (string?)null;
    var root = (string?)null;
    var configuration = "Release";
    var targetFramework = (string?)null;
    var projects = new List<string>();
    var verbose = false;

    for (var i = 0; i < args.Length; ++i) {
      var a = args[i];
      switch (a) {
        case "--check" or "--write" or "--self-test":
          if (mode != null)
            return Fail($"--check, --write and --self-test are mutually exclusive (got both {mode} and {a}).");

          mode = a;
          break;
        case "--configuration":
          if (++i >= args.Length)
            return Fail("--configuration needs a value.");

          configuration = args[i];
          break;
        case "--target-framework":
          if (++i >= args.Length)
            return Fail("--target-framework needs a value.");

          targetFramework = args[i];
          break;
        case "--project":
          if (++i >= args.Length)
            return Fail("--project needs a value.");

          projects.Add(args[i]);
          break;
        case "--exclude-namespace":
          if (++i >= args.Length)
            return Fail("--exclude-namespace needs a value.");

          Visibility.ExcludeNamespace(args[i]);
          break;
        case "--verbose":
          verbose = true;
          break;
        default:
          if (a.StartsWith('-'))
            return Fail($"unknown option '{a}'.");

          if (root != null)
            return Fail($"more than one repository root given ('{root}' and '{a}').");

          root = a;
          break;
      }
    }

    if (mode == null)
      return Fail("one of --check, --write or --self-test is required.");

    if (mode == "--self-test")
      return SelfTest.Run(verbose);

    root = Path.GetFullPath(root ?? ".");
    if (!Directory.Exists(root))
      return Fail($"repository root '{root}' does not exist.");

    return Runner.Execute(root, mode == "--write", configuration, targetFramework, projects, verbose);
  }

  static int Fail(string message) {
    Console.Error.WriteLine($"error: {message}");
    Console.Error.WriteLine();
    Console.Error.WriteLine("usage: package-readme.cs (--check | --write) [repoRoot] [--configuration <cfg>] [--project <csproj>]");
    Console.Error.WriteLine("       package-readme.cs --self-test");
    return 2;
  }
}

// =============================================================================
//  Orchestration
// =============================================================================

static class Runner {
  public static int Execute(string root, bool write, string configuration, string? targetFramework, List<string> explicitProjects, bool verbose) {
    List<string> candidates;
    if (explicitProjects.Count > 0)
      candidates = explicitProjects.Select(Path.GetFullPath).ToList();
    else {
      var all = ProjectDiscovery.FindProjectFiles(root);
      candidates = all.Where(ProjectDiscovery.DeclaresPackagingIntent).ToList();
      if (verbose)
        Console.WriteLine($"found {all.Count} project file(s) under {root}; {candidates.Count} declare packaging intent");
    }

    var packages = new List<PackageProject>();
    foreach (var proj in candidates) {
      var info = ProjectDiscovery.Describe(proj, configuration, targetFramework, verbose);
      if (info == null)
        continue;

      packages.Add(info);
    }

    if (packages.Count == 0) {
      Console.WriteLine("no packable projects found — nothing to check.");
      return 0;
    }

    packages.Sort((a, b) => string.CompareOrdinal(a.PackageId, b.PackageId));

    var findings = new List<Finding>();
    var rewritten = 0;

    foreach (var pkg in packages) {
      Console.WriteLine($"==> {pkg.PackageId}");
      var pkgFindings = Process(pkg, root, write, ref rewritten);
      foreach (var f in pkgFindings)
        Console.WriteLine($"    {(f.Advisory ? "warning" : "ERROR  ")} {f.Message}");

      findings.AddRange(pkgFindings);
    }

    var errors = findings.Count(f => !f.Advisory);
    var warnings = findings.Count(f => f.Advisory);

    Console.WriteLine();
    if (write)
      Console.WriteLine($"{rewritten} file(s) rewritten, {errors} error(s), {warnings} warning(s).");
    else
      Console.WriteLine($"{packages.Count} package(s) checked, {errors} error(s), {warnings} warning(s).");

    if (errors > 0 && !write) {
      Console.WriteLine();
      Console.WriteLine("To regenerate the API sections locally:");
      Console.WriteLine("  curl -sL https://raw.githubusercontent.com/Hawkynt/RepositoryTemplate/v1/scripts/package-readme.cs -o package-readme.cs");
      Console.WriteLine("  dotnet run package-readme.cs -- --write .");
    }

    return errors > 0 ? 1 : 0;
  }

  static List<Finding> Process(PackageProject pkg, string root, bool write, ref int rewritten) {
    var findings = new List<Finding>();

    if (pkg.ReadmePath == null) {
      findings.Add(Finding.Error(
        $"{pkg.ProjectName}: <PackageReadmeFile> is not set. A package without a README ships blank on nuget.org."));
      return findings;
    }

    if (!File.Exists(pkg.ReadmePath)) {
      findings.Add(Finding.Error(
        $"{pkg.ProjectName}: <PackageReadmeFile> points at '{pkg.ReadmeRelative}', which does not exist."));
      return findings;
    }

    if (!pkg.ReadmeIsPacked)
      findings.Add(Finding.Error(
        $"{pkg.ProjectName}: '{pkg.ReadmeRelative}' is not packed. Add " +
        $"<None Include=\"{pkg.ReadmeRelative}\" Pack=\"true\" PackagePath=\"\\\" /> — PackageReadmeFile alone does not include the file."));

    var original = File.ReadAllText(pkg.ReadmePath);
    findings.AddRange(Linter.Check(original, pkg));

    if (pkg.AssemblyPath == null || !File.Exists(pkg.AssemblyPath)) {
      findings.Add(Finding.Error(
        $"{pkg.ProjectName}: built assembly not found at '{pkg.AssemblyPath ?? "(unknown)"}'. Build the project first."));
      return findings;
    }

    // .NET Framework targets are refused rather than attempted. MetadataLoadContext cannot resolve
    // mscorlib from the .NET shared frameworks and recurses until the stack overflows -- which is
    // uncatchable, so the process simply dies. Detecting it first is the only available defense.
    if (Regex.IsMatch(pkg.TargetFramework, @"^net\d+$")) {
      findings.Add(Finding.Error(
        $"{pkg.ProjectName}: cannot document the .NET Framework target '{pkg.TargetFramework}'. Its core " +
        "library is not resolvable here. Pass --target-framework with a .NET or .NET Standard target " +
        "(for example netstandard2.0) that the package also builds."));
      return findings;
    }

    if (pkg.MissingBundles.Count > 0)
      findings.Add(Finding.Error(
        $"{pkg.ProjectName}: {pkg.MissingBundles.Count} bundled assembly/assemblies were not found in the build output " +
        $"({string.Join(", ", pkg.MissingBundles.Take(5))}{(pkg.MissingBundles.Count > 5 ? ", ..." : "")}). " +
        "Build the whole package before checking, or the reference would omit types the package ships."));

    var inputs = new List<(string, string?)> { (pkg.AssemblyPath, pkg.DocumentationPath) };
    inputs.AddRange(pkg.BundledAssemblies.Select(a => ((string)a, (string?)null)));

    ApiModel model;
    try {
      model = ApiExtractor.Extract(inputs);
    } catch (ApiExtractionException e) {
      findings.Add(Finding.Error($"{pkg.ProjectName}: {e.Message}"));
      return findings;
    } catch (Exception e) {
      findings.Add(Finding.Error($"{pkg.ProjectName}: could not read assembly metadata — {e.Message}"));
      return findings;
    }

    if (pkg.DocumentationPath == null || !File.Exists(pkg.DocumentationPath))
      findings.Add(Finding.Warning(
        $"{pkg.ProjectName}: no XML documentation file. Set <GenerateDocumentationFile>true</GenerateDocumentationFile> " +
        "so summaries and <example> blocks reach the API reference."));

    findings.AddRange(model.Advisories.Select(Finding.Warning));

    string generated;
    try {
      generated = Renderer.Render(model);
    } catch (ApiExtractionException e) {
      findings.Add(Finding.Error($"{pkg.ProjectName}: {e.Message}"));
      return findings;
    }

    // The reference is a file of its own. FrameworkExtensions.Corlib generates ~973 KB of tables,
    // and a README that size is not a README any more — nuget.org truncates it, GitHub asks before
    // rendering it, and the six hand-written paragraphs a consumer actually needs first are buried
    // under four hundred types. README keeps the prose and points at REFERENCE.md.
    var referencePath = Path.Combine(Path.GetDirectoryName(pkg.ReadmePath)!, ReferenceDocument.FileName);
    var referenceRelative = Relative(root, referencePath);

    var referenceUrl = ReferenceDocument.Url(pkg, referenceRelative);
    if (referenceUrl == null) {
      findings.Add(Finding.Error(
        $"{pkg.ProjectName}: no <RepositoryUrl> or <PackageProjectUrl>, so the README cannot link to " +
        $"{referenceRelative}. A package README renders on nuget.org, where a relative link resolves nowhere."));
      return findings;
    }

    string updated;
    try {
      updated = ReadmeSplicer.Splice(original, ReadmeSplicer.Pointer(referenceUrl, model.TypeCount));
    } catch (SpliceException e) {
      findings.Add(Finding.Error($"{pkg.ProjectName}: {e.Message}"));
      return findings;
    }

    var existingReference = File.Exists(referencePath) ? File.ReadAllText(referencePath) : "";
    var newReference = ReferenceDocument.Build(pkg, generated, existingReference, original,
      ReferenceDocument.Url(pkg, Relative(root, pkg.ReadmePath)));

    if (write) {
      if (!string.Equals(updated, original, StringComparison.Ordinal)) {
        File.WriteAllText(pkg.ReadmePath, updated);
        ++rewritten;
        Console.WriteLine($"    rewrote {pkg.ReadmeRelative}");
      }

      if (!string.Equals(newReference, existingReference, StringComparison.Ordinal)) {
        File.WriteAllText(referencePath, newReference);
        ++rewritten;
        Console.WriteLine($"    rewrote {referenceRelative}");
      }

      return findings;
    }

    if (!string.Equals(updated, original, StringComparison.Ordinal))
      findings.Add(Finding.Error(
        $"{pkg.ProjectName}: the API reference section in '{pkg.ReadmeRelative}' is out of date. " +
        Diff.Describe(original, updated)));

    if (!string.Equals(newReference, existingReference, StringComparison.Ordinal))
      findings.Add(Finding.Error(
        existingReference.Length == 0
          ? $"{pkg.ProjectName}: '{referenceRelative}' does not exist. The generated API reference lives there now."
          : $"{pkg.ProjectName}: the API reference in '{referenceRelative}' is out of date with the assembly. " +
            Diff.Describe(existingReference, newReference)));

    return findings;
  }

  /// <summary>Repository-relative, forward-slashed — it goes into a URL and into a log line.</summary>
  static string Relative(string root, string path) =>
    Path.GetRelativePath(Path.GetFullPath(root), path).Replace('\\', '/');
}

record Finding(string Message, bool Advisory) {
  public static Finding Error(string m) => new(m, false);
  public static Finding Warning(string m) => new(m, true);
}

static class Diff {
  /// <summary>Reports the first differing line so a CI log says what changed, not just "it changed".</summary>
  public static string Describe(string oldText, string newText) {
    var a = oldText.Replace("\r\n", "\n").Split('\n');
    var b = newText.Replace("\r\n", "\n").Split('\n');
    for (var i = 0; i < Math.Max(a.Length, b.Length); ++i) {
      var lineA = i < a.Length ? a[i] : "(end of file)";
      var lineB = i < b.Length ? b[i] : "(end of file)";
      if (!string.Equals(lineA, lineB, StringComparison.Ordinal))
        return $"First difference at line {i + 1}:{Environment.NewLine}      committed: {Truncate(lineA)}{Environment.NewLine}      generated: {Truncate(lineB)}";
    }

    return "(files differ only in trailing whitespace)";
  }

  static string Truncate(string s) => s.Length <= 120 ? s : s[..117] + "...";
}

// =============================================================================
//  Project discovery — MSBuild is the authority on every property.
// =============================================================================

record PackageProject(
  string ProjectPath,
  string ProjectName,
  string PackageId,
  string? ReadmeRelative,
  string? ReadmePath,
  bool ReadmeIsPacked,
  string? AssemblyPath,
  string? DocumentationPath,
  List<string> BundledAssemblies,
  List<string> MissingBundles,
  string TargetFramework,
  string RepositoryUrl);

static class ProjectDiscovery {
  static readonly string[] SkipDirectories = [
    "bin", "obj", ".git", ".vs", ".idea", "node_modules", "TestResults",
    "artifacts", "publish", "dist", "stage", "coverage", "vendor", "target",
    "fixtures" // this tool's own test fixtures are not packages of the host repo
  ];

  public static List<string> FindProjectFiles(string root) {
    var result = new List<string>();
    Walk(root, result);
    result.Sort(StringComparer.Ordinal);
    return result;
  }

  /// <summary>
  ///   Whether the project file itself declares an intent to ship a package.
  ///   <para>
  ///     This deliberately reads the .csproj text rather than trusting the evaluated
  ///     <c>IsPackable</c>: the SDK defaults every class library to packable, so the evaluated value
  ///     is <c>true</c> for projects nobody ever publishes. In a repo like CompressionWorkbench that
  ///     is 639 projects, only 4 of which are packages. What the maintainer wrote in the project file
  ///     is the actual declaration; a repo-wide <c>PackageReadmeFile</c> in Directory.Build.props is
  ///     not, which is why it is not one of the markers here.
  ///   </para>
  ///   <para>
  ///     It is also what keeps this fast: the text scan is instant, so MSBuild is only ever invoked
  ///     for the handful of projects that could possibly be packages.
  ///   </para>
  /// </summary>
  public static bool DeclaresPackagingIntent(string projectPath) {
    string text;
    try {
      text = File.ReadAllText(projectPath);
    } catch (IOException) {
      return false;
    }

    return Regex.IsMatch(text, @"<\s*(IsPackable|PackageId|GeneratePackageOnBuild)\s*>", RegexOptions.IgnoreCase);
  }

  static void Walk(string dir, List<string> into) {
    // A repository can contain a dangling symlink or a directory the runner cannot read -- neither
    // is a reason to abandon the scan. CompressionWorkbench, for instance, carries a broken
    // .claude/worktrees link to a sibling checkout that may not exist.
    string[] files;
    string[] subdirectories;
    try {
      files = Directory.GetFiles(dir, "*.csproj");
      subdirectories = Directory.GetDirectories(dir);
    } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
      return;
    }

    foreach (var f in files)
      into.Add(Path.GetFullPath(f));

    foreach (var sub in subdirectories) {
      var name = Path.GetFileName(sub);
      if (SkipDirectories.Contains(name, StringComparer.OrdinalIgnoreCase))
        continue;

      // Never follow a symlink or junction. It usually points outside the repository -- a worktree
      // link to a sibling checkout would otherwise pull that repo's packages into this one's
      // results -- and it is also the only way this walk could cycle forever.
      try {
        if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0)
          continue;
      } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
        continue;
      }

      Walk(sub, into);
    }
  }

  /// <summary>
  ///   Asks MSBuild for the evaluated properties. This matters: some repos declare PackageId
  ///   explicitly, others rely on it defaulting from the .csproj filename. Parsing the XML would
  ///   only see the first kind.
  /// </summary>
  public static PackageProject? Describe(string projectPath, string configuration, string? targetFramework, bool verbose) {
    string[] wanted = [
      "PackageId", "IsPackable", "OutputType", "PackageReadmeFile",
      "TargetPath", "DocumentationFile", "TargetFramework", "TargetFrameworks",
      "RepositoryUrl", "PackageProjectUrl"
    ];

    var evaluated = MsBuild.Evaluate(projectPath, configuration, targetFramework, wanted);

    if (evaluated == null) {
      if (verbose)
        Console.WriteLine($"  skipped (MSBuild evaluation failed): {projectPath}");

      return null;
    }

    // A multi-targeted project has no single TargetPath until a framework is chosen, so it comes
    // back empty. Re-evaluate against one framework and document that -- the public surface is
    // normally the same across them, and the chosen one is reported.
    var frameworks = evaluated.Properties.GetValueOrDefault("TargetFrameworks", "");
    if (targetFramework == null
        && string.IsNullOrEmpty(evaluated.Properties.GetValueOrDefault("TargetPath", ""))
        && !string.IsNullOrEmpty(frameworks)) {
      var first = frameworks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
      if (!string.IsNullOrEmpty(first)) {
        if (verbose)
          Console.WriteLine($"  {Path.GetFileName(projectPath)} multi-targets '{frameworks}'; documenting {first}");

        evaluated = MsBuild.Evaluate(projectPath, configuration, first, wanted) ?? evaluated;
      }
    }

    var props = evaluated.Properties;
    var isPackable = props.GetValueOrDefault("IsPackable", "");
    var outputType = props.GetValueOrDefault("OutputType", "");

    // The project declared packaging intent to get this far; MSBuild has the final say on whether
    // it is actually packable. Test and benchmark projects typically opt back out here.
    if (string.Equals(isPackable, "false", StringComparison.OrdinalIgnoreCase))
      return null;

    if (string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(isPackable, "true", StringComparison.OrdinalIgnoreCase))
      return null;

    var readmeRelative = NormalizeSeparators(props.GetValueOrDefault("PackageReadmeFile", ""));
    var projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
    var packageId = props.GetValueOrDefault("PackageId", "");
    if (string.IsNullOrEmpty(packageId))
      packageId = Path.GetFileNameWithoutExtension(projectPath);

    var readmePath = string.IsNullOrEmpty(readmeRelative)
      ? null
      : Path.GetFullPath(Path.Combine(projectDir, readmeRelative));

    var targetPath = props.GetValueOrDefault("TargetPath", "");
    var docFile = NormalizeSeparators(props.GetValueOrDefault("DocumentationFile", ""));
    var docPath = string.IsNullOrEmpty(docFile) ? null : Path.GetFullPath(Path.Combine(projectDir, docFile));

    var (bundled, missing) = ResolveBundledAssemblies(evaluated.ProjectReferences, projectDir, targetPath);

    return new PackageProject(
      Path.GetFullPath(projectPath),
      Path.GetFileNameWithoutExtension(projectPath),
      packageId,
      string.IsNullOrEmpty(readmeRelative) ? null : readmeRelative,
      readmePath,
      readmePath != null && IsReadmePacked(evaluated.NoneItems, readmeRelative),
      string.IsNullOrEmpty(targetPath) ? null : targetPath,
      docPath,
      bundled,
      missing,
      props.GetValueOrDefault("TargetFramework", ""),
      FirstNonEmpty(props.GetValueOrDefault("RepositoryUrl", ""), props.GetValueOrDefault("PackageProjectUrl", "")));
  }

  /// <summary>
  ///   Finds assemblies the package ships in <c>lib/</c> that are not its own output.
  ///   <para>
  ///     A meta-package bundles its ProjectReferences into the package instead of declaring them as
  ///     transitive NuGet dependencies, and marks them <c>PrivateAssets="all"</c> to stop them being
  ///     emitted as <c>&lt;dependency&gt;</c> entries. Those assemblies ARE the package's public
  ///     surface: Hawkynt.FileFormats.Archives has no source file of its own and bundles 192 of
  ///     them, so documenting only the facade would describe an empty package.
  ///   </para>
  ///   <para>
  ///     They are located by name in the build output rather than by evaluating each referenced
  ///     project, which would mean one MSBuild invocation per reference. Anything not found is
  ///     reported rather than silently dropped, so a miss can never quietly shrink the reference.
  ///   </para>
  /// </summary>
  static (List<string> Bundled, List<string> Missing) ResolveBundledAssemblies(
    List<Dictionary<string, string>> projectReferences, string projectDir, string targetPath) {
    var bundled = new List<string>();
    var missing = new List<string>();
    if (string.IsNullOrEmpty(targetPath))
      return (bundled, missing);

    var outputDir = Path.GetDirectoryName(targetPath);
    if (outputDir == null || !Directory.Exists(outputDir))
      return (bundled, missing);

    foreach (var reference in projectReferences) {
      // A source generator is referenced as an Analyzer and contributes no assembly to lib/, so it
      // is not part of the package's surface even though it is marked PrivateAssets="all".
      if (string.Equals(reference.GetValueOrDefault("OutputItemType", ""), "Analyzer", StringComparison.OrdinalIgnoreCase))
        continue;

      if (string.Equals(reference.GetValueOrDefault("ReferenceOutputAssembly", ""), "false", StringComparison.OrdinalIgnoreCase))
        continue;

      var privateAssets = reference.GetValueOrDefault("PrivateAssets", "");
      if (!privateAssets.Split(';').Any(p => p.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)))
        continue;

      var identity = NormalizeSeparators(reference.GetValueOrDefault("Identity", ""));
      if (string.IsNullOrEmpty(identity))
        continue;

      var name = Path.GetFileNameWithoutExtension(identity);
      var referencedProject = Path.GetFullPath(Path.Combine(projectDir, identity));
      var resolved = ResolveAssemblyFile(outputDir, name, referencedProject);
      if (resolved != null)
        bundled.Add(resolved);
      else
        missing.Add(name);
    }

    bundled.Sort(StringComparer.Ordinal);
    missing.Sort(StringComparer.Ordinal);
    return (bundled, missing);
  }

  /// <summary>
  ///   The output file is usually named after the project, but not always: a project may rename its
  ///   output with AssemblyName (FileFormat.Ani.csproj produces CompressionWorkbench.FileFormat.Ani.dll
  ///   in CompressionWorkbench). Falling back to the declared AssemblyName keeps those assemblies in
  ///   the reference instead of dropping them.
  /// </summary>
  static string? ResolveAssemblyFile(string outputDir, string projectName, string referencedProject) {
    var byProjectName = Path.Combine(outputDir, projectName + ".dll");
    if (File.Exists(byProjectName))
      return byProjectName;

    if (!File.Exists(referencedProject))
      return null;

    string text;
    try {
      text = File.ReadAllText(referencedProject);
    } catch (IOException) {
      return null;
    }

    var declared = Regex.Match(text, @"<\s*AssemblyName\s*>\s*([^<]+?)\s*<", RegexOptions.IgnoreCase);
    if (!declared.Success)
      return null;

    var byAssemblyName = Path.Combine(outputDir, declared.Groups[1].Value.Trim() + ".dll");
    return File.Exists(byAssemblyName) ? byAssemblyName : null;
  }

  static string FirstNonEmpty(params string[] candidates) =>
    Array.Find(candidates, c => !string.IsNullOrWhiteSpace(c)) ?? "";

  /// <summary>
  ///   MSBuild reports paths with Windows separators whatever the host OS: on Linux
  ///   <c>DocumentationFile</c> comes back as <c>obj\Release/net10.0/Thing.xml</c>. A backslash is an
  ///   ordinary filename character there, so <c>Path.Combine</c> builds a path that cannot exist, the
  ///   XML docs are silently not found, and every summary disappears from the generated reference —
  ///   which then reads as drift against a README generated on Windows. Normalizing every
  ///   MSBuild-supplied path on the way in is the only place this has to be remembered.
  ///   <para>
  ///     Both separators have to be rewritten, not just the backslash. The path MSBuild hands back is
  ///     mixed, so on Windows — where the separator already is a backslash — replacing only backslashes
  ///     is a no-op and the forward slashes survive.
  ///   </para>
  /// </summary>
  public static string NormalizeSeparators(string path)
    => path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

  /// <summary>
  ///   PackageReadmeFile names the file inside the package; it does not put it there. Without a
  ///   Pack'd None item `dotnet pack` fails NU5039, so the item is part of the contract.
  /// </summary>
  static bool IsReadmePacked(List<Dictionary<string, string>> noneItems, string readmeRelative) {
    var wanted = Path.GetFileName(readmeRelative);
    foreach (var item in noneItems) {
      var identity = NormalizeSeparators(item.GetValueOrDefault("Identity", ""));
      if (!string.Equals(Path.GetFileName(identity), wanted, StringComparison.OrdinalIgnoreCase))
        continue;

      if (string.Equals(item.GetValueOrDefault("Pack", ""), "true", StringComparison.OrdinalIgnoreCase))
        return true;
    }

    return false;
  }
}

record MsBuildResult(
  Dictionary<string, string> Properties,
  List<Dictionary<string, string>> NoneItems,
  List<Dictionary<string, string>> ProjectReferences);

static class MsBuild {
  /// <summary>
  ///   One evaluation returns both the properties and the None items. Asking MSBuild rather than
  ///   reading the .csproj matters twice over: PackageId is frequently implicit (defaulted from the
  ///   project filename), and the README's Pack item is often contributed by a Directory.Build.props
  ///   that a text scan of the .csproj would never see.
  /// </summary>
  public static MsBuildResult? Evaluate(string projectPath, string configuration, string? targetFramework, params string[] names) {
    var args = new StringBuilder();
    args.Append("msbuild \"").Append(projectPath).Append('"');
    foreach (var n in names)
      args.Append(" -getProperty:").Append(n);

    args.Append(" -getItem:None -getItem:ProjectReference");
    args.Append(" -p:Configuration=").Append(configuration);
    if (!string.IsNullOrEmpty(targetFramework))
      args.Append(" -p:TargetFramework=").Append(targetFramework);

    args.Append(" -nologo");

    var psi = new ProcessStartInfo("dotnet", args.ToString()) {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false
    };

    using var p = Process.Start(psi);
    if (p == null)
      return null;

    // Read both pipes concurrently — a project with a chatty evaluation can otherwise fill the
    // stderr buffer and deadlock against a sequential read of stdout.
    var stdoutTask = p.StandardOutput.ReadToEndAsync();
    var stderrTask = p.StandardError.ReadToEndAsync();
    Task.WaitAll(stdoutTask, stderrTask);
    p.WaitForExit();
    if (p.ExitCode != 0)
      return null;

    try {
      using var doc = JsonDocument.Parse(stdoutTask.Result);
      var properties = new Dictionary<string, string>(StringComparer.Ordinal);
      if (doc.RootElement.TryGetProperty("Properties", out var props))
        foreach (var prop in props.EnumerateObject())
          properties[prop.Name] = prop.Value.GetString() ?? "";

      doc.RootElement.TryGetProperty("Items", out var items);
      return new MsBuildResult(properties, ReadItems(items, "None"), ReadItems(items, "ProjectReference"));
    } catch (JsonException) {
      return null;
    }
  }

  static List<Dictionary<string, string>> ReadItems(JsonElement items, string name) {
    var result = new List<Dictionary<string, string>>();
    if (items.ValueKind != JsonValueKind.Object || !items.TryGetProperty(name, out var array))
      return result;

    foreach (var item in array.EnumerateArray()) {
      var bag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      foreach (var field in item.EnumerateObject())
        if (field.Value.ValueKind == JsonValueKind.String)
          bag[field.Name] = field.Value.GetString() ?? "";

      result.Add(bag);
    }

    return result;
  }
}

// =============================================================================
//  API model
// =============================================================================

record ApiModel(List<NamespaceGroup> Namespaces, List<string> Advisories) {
  /// <summary>How many types the reference documents, which is what the README's pointer promises.</summary>
  public int TypeCount => this.Namespaces.Sum(n => n.Types.Count);
}
record NamespaceGroup(string Name, List<TypeDoc> Types);

record TypeDoc(
  string DisplayName,
  string Kind,
  string Summary,
  string? Example,
  string? BaseType,
  List<string> Interfaces,
  List<MemberDoc> Members,
  bool IsEnum);

record MemberDoc(string Name, string Signature, string Summary, int SortRank);

static class ApiExtractor {
  public static ApiModel Extract(string assemblyPath, string? documentationPath) =>
    Extract([(assemblyPath, documentationPath)]);

  /// <summary>
  ///   Builds one merged model from every assembly the package ships, so a meta-package that bundles
  ///   its references documents what a consumer actually gets.
  /// </summary>
  public static ApiModel Extract(IReadOnlyList<(string Assembly, string? Documentation)> inputs) {
    var advisories = new List<string>();

    var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var dir in SharedFrameworkDirectories()
               .Concat(inputs.Select(i => Path.GetDirectoryName(Path.GetFullPath(i.Assembly))!))
               .Distinct(StringComparer.OrdinalIgnoreCase))
      if (Directory.Exists(dir))
        foreach (var f in Directory.GetFiles(dir, "*.dll"))
          byName[Path.GetFileNameWithoutExtension(f)] = f; // package output wins over the framework

    using var mlc = new MetadataLoadContext(new PathAssemblyResolver(byName.Values));

    var groups = new SortedDictionary<string, List<TypeDoc>>(StringComparer.Ordinal);
    var undocumented = 0;
    var exampleless = new List<string>();
    var unresolvable = new List<string>();
    var attempted = 0;
    var seen = new HashSet<string>(StringComparer.Ordinal);

    foreach (var (assemblyPath, documentationPath) in inputs) {
      var docs = XmlDocs.Load(documentationPath ?? Path.ChangeExtension(assemblyPath, ".xml"));
      var assembly = mlc.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));

      foreach (var type in assembly.GetTypes()) {
        if (!Visibility.IsVisibleApi(type) || Naming.IsCompilerGenerated(type) || Visibility.IsExcludedNamespace(type))
          continue;

        // Two bundled assemblies can legitimately expose the same full type name; document once.
        if (!seen.Add(type.FullName ?? type.Name))
          continue;

        // A type whose base or interface lives in an assembly outside the package and outside the
        // shared frameworks (a NuGet dependency, say) cannot be fully described. Skipping that one
        // type and saying so beats aborting the whole package.
        // Any failure to describe one type is survivable; aborting the run over it is not. A .NET
        // Framework target throws from deep inside signature decoding rather than as a tidy
        // FileNotFoundException, so this deliberately catches broadly and counts what it lost.
        TypeDoc doc;
        try {
          doc = BuildType(type, docs, ref undocumented);
        } catch (Exception e) {
          var reason = e is FileNotFoundException
            ? e.Message.Split(',')[0].Replace("Could not find assembly '", "").Trim('\'')
            : e.GetType().Name;
          unresolvable.Add($"{type.FullName} ({reason})");
          ++attempted;
          continue;
        }

        ++attempted;

        var ns = string.IsNullOrEmpty(type.Namespace) ? "(global namespace)" : type.Namespace;
        if (!groups.TryGetValue(ns, out var list))
          groups[ns] = list = [];

        list.Add(doc);
        if (doc.Example == null && !doc.IsEnum && type.DeclaringType == null)
          exampleless.Add(doc.DisplayName);
      }
    }

    foreach (var list in groups.Values)
      list.Sort((a, b) => string.CompareOrdinal(a.DisplayName, b.DisplayName));

    // Losing a stray type is a warning; losing most or all of them means the assembly could not
    // really be read, and emitting a confidently empty reference would be worse than failing. This
    // is what a .NET Framework target does today: its core library is not among the resolvable
    // shared frameworks, so nearly every signature fails to decode.
    if (unresolvable.Count > 0 && attempted > 0 && unresolvable.Count * 2 >= attempted)
      throw new ApiExtractionException(
        $"{unresolvable.Count} of {attempted} types could not be read — the assembly's framework references " +
        $"are not resolvable here (first: {unresolvable[0]}). A .NET Framework target needs its reference " +
        "assemblies; document a .NET/.NET Standard target framework instead via --target-framework.");

    if (unresolvable.Count > 0)
      advisories.Add(
        $"{unresolvable.Count} type(s) omitted — a referenced assembly could not be resolved: " +
        string.Join(", ", unresolvable.Take(4)) + (unresolvable.Count > 4 ? ", ..." : "") + ".");

    if (undocumented > 0)
      advisories.Add($"{undocumented} public/protected member(s) have no <summary> — their API table cells are blank.");

    if (exampleless.Count > 0) {
      var shown = string.Join(", ", exampleless.Take(6));
      var more = exampleless.Count > 6 ? $" (+{exampleless.Count - 6} more)" : "";
      advisories.Add($"{exampleless.Count} type(s) have no <example> to show off: {shown}{more}.");
    }

    return new ApiModel(
      groups.Select(kv => new NamespaceGroup(kv.Key, kv.Value)).ToList(),
      advisories);
  }

  /// <summary>
  ///   Every installed shared framework, not just the one this tool runs on. A WinForms or WPF
  ///   package references Microsoft.WindowsDesktop.App assemblies (System.Drawing.Common and
  ///   friends), which do not live beside the .NETCore.App runtime.
  /// </summary>
  static IEnumerable<string> SharedFrameworkDirectories() {
    var runtime = RuntimeEnvironment.GetRuntimeDirectory();
    yield return runtime;

    // <dotnet>/shared/Microsoft.NETCore.App/<version>/ -> <dotnet>/shared/
    var shared = Path.GetDirectoryName(Path.GetDirectoryName(runtime.TrimEnd(Path.DirectorySeparatorChar)));
    if (shared == null || !Directory.Exists(shared))
      yield break;

    foreach (var family in Directory.GetDirectories(shared))
      foreach (var version in Directory.GetDirectories(family))
        yield return version;
  }

  static TypeDoc BuildType(Type type, XmlDocs docs, ref int undocumented) {
    var entry = docs.Get(DocId.ForType(type), Inheritance.ForType(type));
    var members = new List<MemberDoc>();
    var isEnum = type.IsEnum;
    var isDelegate = Naming.KindOf(type) == "delegate";

    if (isDelegate) {
      // A delegate's compiler-generated plumbing (.ctor(object, nint), BeginInvoke, EndInvoke) is
      // not API a consumer ever writes. The one thing that matters is the signature it stands for.
      var invoke = type.GetMethod("Invoke");
      if (invoke != null)
        members.Add(new MemberDoc(
          $"`{Naming.BareTypeName(type)}`",
          $"`{Naming.FullDisplayName(invoke.ReturnType)} {Naming.SimpleTypeName(type)}({string.Join(", ", invoke.GetParameters().Select(p => Naming.FullDisplayName(p.ParameterType) + " " + p.Name))})`",
          entry?.Summary ?? "",
          0));
    } else if (isEnum) {
      foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)) {
        var d = docs.Get(DocId.ForField(f), Inheritance.ForMember(f));
        object? raw = null;
        try { raw = f.GetRawConstantValue(); } catch { /* non-constant enum field: skip the value */ }

        members.Add(new MemberDoc(
          $"`{f.Name}`",
          $"`{Convert.ToString(raw, CultureInfo.InvariantCulture)}`",
          d?.Summary ?? "",
          0));
        if (d?.Summary is null or "")
          ++undocumented;
      }

      // Metadata field order is declaration order, which is how a reader expects to meet an enum.
      // It is stable across runs, so the drift check stays reliable without an alphabetical sort.
    } else {
      const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                                 | BindingFlags.Static | BindingFlags.DeclaredOnly;

      foreach (var ctor in type.GetConstructors(flags)) {
        if (!Visibility.IsVisibleMember(ctor) || Naming.IsCompilerGenerated(ctor))
          continue;

        Add(members, docs, ref undocumented, Naming.BareTypeName(type), Signatures.ForConstructor(ctor), DocId.ForMethod(ctor), ctor, 0);
      }

      foreach (var f in type.GetFields(flags)) {
        if (!Visibility.IsVisibleMember(f) || Naming.IsCompilerGenerated(f))
          continue;

        Add(members, docs, ref undocumented, f.Name, Signatures.ForField(f), DocId.ForField(f), f, 1);
      }

      foreach (var p in type.GetProperties(flags)) {
        if (!Visibility.IsVisibleProperty(p) || Naming.IsCompilerGenerated(p))
          continue;

        Add(members, docs, ref undocumented, p.Name, Signatures.ForProperty(p), DocId.ForProperty(p), p, 2);
      }

      foreach (var m in type.GetMethods(flags)) {
        if (!Visibility.IsVisibleMember(m) || Naming.IsCompilerGenerated(m) || Naming.IsAccessor(m))
          continue;

        Add(members, docs, ref undocumented, Naming.MethodDisplayName(m), Signatures.ForMethod(m), DocId.ForMethod(m), m, m.IsSpecialName ? 4 : 3);
      }

      foreach (var e in type.GetEvents(flags)) {
        if (!Visibility.IsVisibleEvent(e) || Naming.IsCompilerGenerated(e))
          continue;

        Add(members, docs, ref undocumented, e.Name, Signatures.ForEvent(e), DocId.ForEvent(e), e, 5);
      }

      // Kind first, then name — sorting by the raw signature would order methods by return type,
      // which scatters overloads of the same member across the table.
      members.Sort((a, b) => a.SortRank != b.SortRank
        ? a.SortRank - b.SortRank
        : string.CompareOrdinal(a.Name, b.Name) is var byName && byName != 0
          ? byName
          : string.CompareOrdinal(a.Signature, b.Signature));
    }

    // Enums and delegates inherit a fixed set of framework interfaces that say nothing about the
    // package's own contract, so their relation line is suppressed entirely.
    var structural = isEnum || isDelegate;

    var interfaces = structural
      ? []
      : type.GetInterfaces()
        .Where(Visibility.IsVisibleApi)
        .Select(Naming.FullDisplayName)
        .OrderBy(x => x, StringComparer.Ordinal)
        .ToList();

    var baseType = !structural
                   && type.BaseType is { } bt
                   && bt.FullName is not ("System.Object" or "System.ValueType" or "System.Enum")
      ? Naming.FullDisplayName(bt)
      : null;

    return new TypeDoc(
      Naming.SimpleTypeName(type),
      Naming.KindOf(type),
      entry?.Summary ?? "",
      entry?.Example,
      baseType,
      interfaces,
      members,
      isEnum);
  }

  static void Add(List<MemberDoc> into, XmlDocs docs, ref int undocumented, string name, string signature, string docId, MemberInfo member, int rank) {
    var d = docs.Get(docId, Inheritance.ForMember(member));
    if (d?.Summary is null or "")
      ++undocumented;

    into.Add(new MemberDoc($"`{name}`", $"`{signature}`", d?.Summary ?? "", rank));
  }
}

static class Visibility {

  // Namespaces excluded with --exclude-namespace, matched on the namespace itself or any child of
  // it. Ordinal comparison: namespaces are case-sensitive identifiers, not user-facing text.
  static readonly List<string> _excludedNamespaces = [];

  public static void ExcludeNamespace(string ns) {
    if (!string.IsNullOrWhiteSpace(ns))
      _excludedNamespaces.Add(ns.Trim());
  }

  /// <summary>Whether a type sits in a namespace the caller asked to leave out.</summary>
  public static bool IsExcludedNamespace(Type t) {
    var ns = t.Namespace;
    if (ns == null || _excludedNamespaces.Count == 0)
      return false;

    foreach (var prefix in _excludedNamespaces)
      if (string.Equals(ns, prefix, StringComparison.Ordinal) || ns.StartsWith(prefix + ".", StringComparison.Ordinal))
        return true;

    return false;
  }

  public static bool IsVisibleApi(Type type) {
    if (type.IsPublic)
      return true;

    if (!type.IsNested)
      return false;

    if (!(type.IsNestedPublic || type.IsNestedFamily || type.IsNestedFamORAssem))
      return false;

    return type.DeclaringType != null && IsVisibleApi(type.DeclaringType);
  }

  public static bool IsVisibleMember(MethodBase m) => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly;
  public static bool IsVisibleMember(FieldInfo f) => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly;

  public static bool IsVisibleProperty(PropertyInfo p) =>
    (p.GetMethod != null && IsVisibleMember(p.GetMethod)) || (p.SetMethod != null && IsVisibleMember(p.SetMethod));

  public static bool IsVisibleEvent(EventInfo e) =>
    (e.AddMethod != null && IsVisibleMember(e.AddMethod)) || (e.RemoveMethod != null && IsVisibleMember(e.RemoveMethod));
}

static class Naming {
  public static bool IsCompilerGenerated(MemberInfo m) =>
    m.GetCustomAttributesData().Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute")
    || m.Name.Contains('<') // <Clone>$, backing fields, local function frames
    || m.Name == "EqualityContract";

  /// <summary>Property and event accessors are represented by their property/event, not twice.</summary>
  public static bool IsAccessor(MethodInfo m) =>
    m.IsSpecialName && (m.Name.StartsWith("get_") || m.Name.StartsWith("set_")
                        || m.Name.StartsWith("add_") || m.Name.StartsWith("remove_"));

  static readonly Dictionary<string, string> OperatorSymbols = new(StringComparer.Ordinal) {
    ["op_Addition"] = "+", ["op_Subtraction"] = "-", ["op_Multiply"] = "*", ["op_Division"] = "/",
    ["op_Modulus"] = "%", ["op_BitwiseAnd"] = "&", ["op_BitwiseOr"] = "|", ["op_ExclusiveOr"] = "^",
    ["op_LeftShift"] = "<<", ["op_RightShift"] = ">>", ["op_UnsignedRightShift"] = ">>>",
    ["op_UnaryNegation"] = "-", ["op_UnaryPlus"] = "+", ["op_LogicalNot"] = "!",
    ["op_OnesComplement"] = "~", ["op_Increment"] = "++", ["op_Decrement"] = "--",
    ["op_Equality"] = "==", ["op_Inequality"] = "!=", ["op_LessThan"] = "<", ["op_GreaterThan"] = ">",
    ["op_LessThanOrEqual"] = "<=", ["op_GreaterThanOrEqual"] = ">=",
    ["op_True"] = "true", ["op_False"] = "false"
  };

  /// <summary>Renders a method the way C# spells it, including operators and conversions.</summary>
  public static string MethodDisplayName(MethodInfo m) {
    if (m.Name is "op_Implicit" or "op_Explicit")
      return (m.Name == "op_Implicit" ? "implicit" : "explicit") + " operator " + FullDisplayName(m.ReturnType);

    return OperatorSymbols.TryGetValue(m.Name, out var symbol) ? "operator " + symbol : m.Name;
  }

  public static bool IsConversionOperator(MethodInfo m) => m.Name is "op_Implicit" or "op_Explicit";

  public static string KindOf(Type t) {
    if (t.IsEnum)
      return "enum";

    if (t.IsInterface)
      return "interface";

    if (t.IsValueType) {
      var ro = t.GetCustomAttributesData().Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute");
      var rec = IsRecord(t);
      return (ro ? "readonly " : "") + (rec ? "record struct" : "struct");
    }

    if (t.BaseType?.FullName is "System.MulticastDelegate" or "System.Delegate")
      return "delegate";

    if (IsRecord(t))
      return t.IsSealed ? "sealed record" : "record";

    if (t.IsAbstract && t.IsSealed)
      return "static class";

    if (t.IsAbstract)
      return "abstract class";

    return t.IsSealed ? "sealed class" : "class";
  }

  static bool IsRecord(Type t) =>
    t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
      .Any(m => m.Name == "<Clone>$")
    || t.GetProperties(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
      .Any(p => p.Name == "EqualityContract");

  static readonly HashSet<string> Keywords = new(StringComparer.Ordinal) {
    "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
    "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
    "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if",
    "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new",
    "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
    "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static",
    "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong",
    "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
  };

  /// <summary>
  ///   Escapes an identifier that collides with a C# keyword. Extension methods in this codebase
  ///   name their first parameter <c>@this</c>, which metadata reports as <c>this</c> — printed bare
  ///   it would render a signature that does not compile.
  /// </summary>
  public static string Identifier(string? name) =>
    name != null && Keywords.Contains(name) ? "@" + name : name ?? "";

  /// <summary>The type's own name with no namespace, no generic list and no declaring prefix.</summary>
  public static string BareTypeName(Type t) {
    var name = t.Name;
    var tick = name.IndexOf('`');
    return tick >= 0 ? name[..tick] : name;
  }

  /// <summary>Name as written in C#: Outer.Inner&lt;T&gt; without the namespace.</summary>
  public static string SimpleTypeName(Type t) {
    // A generic parameter's DeclaringType is the type that declares it, so walking outwards here
    // would expand that type's arguments, which include this parameter again — forever.
    if (t.IsGenericParameter)
      return t.Name;

    var name = t.Name;
    var tick = name.IndexOf('`');
    if (tick >= 0)
      name = name[..tick];

    if (t.IsGenericType || t.IsGenericTypeDefinition) {
      // A nested type inherits its declaring type's parameters. Only the ones this type actually
      // introduces belong on its own name, or Cache<TKey,TValue>.Entry reads as
      // "Cache<TKey,TValue>.Entry<TKey,TValue>".
      var inherited = t.DeclaringType?.GetGenericArguments().Length ?? 0;
      var own = t.GetGenericArguments().Skip(inherited).Select(SimpleTypeName).ToList();
      if (own.Count > 0)
        name += "<" + string.Join(", ", own) + ">";
    }

    return t.DeclaringType != null ? SimpleTypeName(t.DeclaringType) + "." + name : name;
  }

  static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal) {
    ["System.Boolean"] = "bool", ["System.Byte"] = "byte", ["System.SByte"] = "sbyte",
    ["System.Char"] = "char", ["System.Decimal"] = "decimal", ["System.Double"] = "double",
    ["System.Single"] = "float", ["System.Int32"] = "int", ["System.UInt32"] = "uint",
    ["System.Int64"] = "long", ["System.UInt64"] = "ulong", ["System.Int16"] = "short",
    ["System.UInt16"] = "ushort", ["System.Object"] = "object", ["System.String"] = "string",
    ["System.Void"] = "void", ["System.IntPtr"] = "nint", ["System.UIntPtr"] = "nuint"
  };

  /// <summary>Type as it appears in a signature: C# alias where one exists, generics expanded.</summary>
  public static string FullDisplayName(Type t) {
    if (t.IsByRef)
      return FullDisplayName(t.GetElementType()!);

    if (t.IsArray) {
      var rank = t.GetArrayRank();
      return FullDisplayName(t.GetElementType()!) + "[" + new string(',', rank - 1) + "]";
    }

    if (t.IsPointer)
      return FullDisplayName(t.GetElementType()!) + "*";

    if (t.IsGenericParameter)
      return t.Name;

    if (t.FullName != null && Aliases.TryGetValue(t.FullName, out var alias))
      return alias;

    if (t.IsGenericType) {
      var def = t.GetGenericTypeDefinition();
      if (def.FullName == "System.Nullable`1")
        return FullDisplayName(t.GetGenericArguments()[0]) + "?";

      var name = t.Name;
      var tick = name.IndexOf('`');
      if (tick >= 0)
        name = name[..tick];

      return name + "<" + string.Join(", ", t.GetGenericArguments().Select(FullDisplayName)) + ">";
    }

    return t.Name;
  }
}

static class Signatures {
  /// <summary>Constructors are written with the bare type name — no generic parameter list.</summary>
  public static string ForConstructor(ConstructorInfo c) =>
    $"{Modifiers(c)}{Naming.BareTypeName(c.DeclaringType!)}({Parameters(c.GetParameters())})";

  public static string ForMethod(MethodInfo m) {
    // A conversion operator's "name" already carries the target type, so it takes no return type.
    if (Naming.IsConversionOperator(m))
      return $"{Modifiers(m)}{Naming.MethodDisplayName(m)}({Parameters(m.GetParameters())})";

    var generics = m.IsGenericMethodDefinition
      ? "<" + string.Join(", ", m.GetGenericArguments().Select(a => a.Name)) + ">"
      : "";

    return $"{Modifiers(m)}{Naming.FullDisplayName(m.ReturnType)} {Naming.MethodDisplayName(m)}{generics}({Parameters(m.GetParameters(), IsExtensionMethod(m))})";
  }

  public static string ForProperty(PropertyInfo p) {
    var accessors = new StringBuilder(" { ");
    if (p.GetMethod != null && Visibility.IsVisibleMember(p.GetMethod))
      accessors.Append("get; ");

    if (p.SetMethod != null && Visibility.IsVisibleMember(p.SetMethod))
      accessors.Append(IsInitOnly(p.SetMethod) ? "init; " : "set; ");

    accessors.Append('}');

    var index = p.GetIndexParameters();
    var name = index.Length > 0 ? $"this[{Parameters(index)}]" : p.Name;
    var accessor = p.GetMethod ?? p.SetMethod;
    return $"{(accessor != null ? Modifiers(accessor) : "")}{Naming.FullDisplayName(p.PropertyType)} {name}{accessors}";
  }

  static bool IsInitOnly(MethodInfo setter) => setter.ReturnParameter
    .GetRequiredCustomModifiers()
    .Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");

  public static string ForField(FieldInfo f) {
    var mods = new StringBuilder();
    if (f.IsFamily || f.IsFamilyOrAssembly)
      mods.Append("protected ");

    if (f.IsLiteral)
      mods.Append("const ");
    else {
      if (f.IsStatic)
        mods.Append("static ");

      if (f.IsInitOnly)
        mods.Append("readonly ");
    }

    return $"{mods}{Naming.FullDisplayName(f.FieldType)} {f.Name}";
  }

  public static string ForEvent(EventInfo e) {
    var accessor = e.AddMethod ?? e.RemoveMethod;
    return $"{(accessor != null ? Modifiers(accessor) : "")}event {Naming.FullDisplayName(e.EventHandlerType!)} {e.Name}";
  }

  static string Modifiers(MethodBase m) {
    var sb = new StringBuilder();
    if (m.IsFamily || m.IsFamilyOrAssembly)
      sb.Append("protected ");

    if (m.IsStatic)
      sb.Append("static ");

    // GetBaseDefinition() is unavailable under MetadataLoadContext, so the vtable slot attributes
    // are what distinguish virtual from override: a NewSlot method introduces one, a ReuseSlot
    // method overrides an inherited one. NewSlot + Final is how the compiler emits an implicit
    // interface implementation, which carries no C# keyword at all.
    if (m is MethodInfo mi && mi.DeclaringType is { IsInterface: false } && mi.IsVirtual) {
      var newSlot = (mi.Attributes & MethodAttributes.NewSlot) != 0;
      if (mi.IsAbstract)
        sb.Append(newSlot ? "abstract " : "abstract override ");
      else if (!newSlot)
        sb.Append(mi.IsFinal ? "sealed override " : "override ");
      else if (!mi.IsFinal)
        sb.Append("virtual ");
    }

    return sb.ToString();
  }

  /// <summary>
  ///   Renders a default value as the C# literal it is. Control characters must be escaped rather
  ///   than emitted raw: a <c>char</c> parameter defaulting to <c>'\0'</c> otherwise writes a NUL
  ///   byte straight into the README, which turns the file binary.
  /// </summary>
  static string Literal(object? value) => value switch {
    null => "null",
    bool b => b ? "true" : "false",
    char c => "'" + Escape(c) + "'",
    string s => "\"" + string.Concat(s.Select(Escape)) + "\"",
    _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null"
  };

  /// <summary>Exposed for the self-test.</summary>
  public static string LiteralForTest(object? value) => Literal(value);

  static string Escape(char c) => c switch {
    '\0' => "\\0",
    '\n' => "\\n",
    '\r' => "\\r",
    '\t' => "\\t",
    '\\' => "\\\\",
    '\'' => "\\'",
    '"' => "\\\"",
    // A pipe is left alone here; escaping it for the markdown table is the cell renderer's job.
    _ => char.IsControl(c) ? $"\\u{(int)c:X4}" : c.ToString()
  };

  static string Parameters(ParameterInfo[] ps, bool isExtension = false) =>
    string.Join(", ", ps.Select((p, i) => (isExtension && i == 0 ? "this " : "") + Parameter(p)));

  public static bool IsExtensionMethod(MethodInfo m) =>
    m.GetCustomAttributesData().Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.ExtensionAttribute");

  static string Parameter(ParameterInfo p) {
    var prefix = "";
    if (p.ParameterType.IsByRef)
      prefix = p.IsOut ? "out " : p.IsIn ? "in " : "ref ";
    else if (p.GetCustomAttributesData().Any(a => a.AttributeType.FullName == "System.ParamArrayAttribute"))
      prefix = "params ";

    var suffix = p.HasDefaultValue ? " = " + Literal(p.RawDefaultValue) : "";

    return $"{prefix}{Naming.FullDisplayName(p.ParameterType)} {Naming.Identifier(p.Name)}{suffix}";
  }
}

// =============================================================================
//  XML documentation
// =============================================================================

record DocEntry(string Summary, string? Example);

sealed class XmlDocs {
  readonly Dictionary<string, XElement> _members = new(StringComparer.Ordinal);
  readonly Dictionary<string, DocEntry> _resolved = new(StringComparer.Ordinal);

  public static XmlDocs Load(string? path) {
    var docs = new XmlDocs();
    if (path == null || !File.Exists(path))
      return docs;

    XDocument doc;
    try {
      doc = XDocument.Load(path);
    } catch (System.Xml.XmlException) {
      return docs;
    }

    foreach (var member in doc.Descendants("member")) {
      var name = member.Attribute("name")?.Value;
      if (string.IsNullOrEmpty(name))
        continue;

      docs._members[name] = new XElement(member);
    }

    return docs;
  }

  /// <param name="inheritable">
  ///   Where a bare <c>&lt;inheritdoc/&gt;</c> on this member may inherit from, in order. The caller
  ///   supplies it because it holds the reflection metadata this class deliberately does not; see
  ///   <c>Inheritance</c>. Passing nothing leaves a bare <c>&lt;inheritdoc/&gt;</c> unresolved.
  /// </param>
  public DocEntry? Get(string docId, IEnumerable<string>? inheritable = null) => Resolve(docId, [], inheritable);

  DocEntry? Resolve(string docId, HashSet<string> resolving, IEnumerable<string>? inheritable = null) {
    if (_resolved.TryGetValue(docId, out var cached))
      return cached;

    if (!_members.TryGetValue(docId, out var member))
      return null;

    // Guards the cycle a chain of <inheritdoc/> can form — A inheriting from B inheriting from A —
    // and is what makes following one safe at all.
    if (!resolving.Add(docId))
      return new DocEntry("", null);

    var summary = Flatten(member.Element("summary"));
    var example = CodeOf(member.Element("example"));
    var inheritdoc = member.Element("inheritdoc");
    var cref = inheritdoc?.Attribute("cref")?.Value;

    // C# 14 extension-member implementation methods deliberately carry an <inheritdoc cref="..."/>
    // to the metadata extension member instead of duplicating its documentation. Following the cref is
    // required by the language specification and also makes ordinary explicit-cref inheritdoc useful.
    if (!string.IsNullOrEmpty(cref))
      Absorb(Resolve(cref, resolving));
    else if (inheritdoc != null && inheritable != null)
      // A bare <inheritdoc/> takes the documentation of the member this one overrides or implements.
      // The candidates are already flattened most-derived first, so a candidate that is itself an
      // unresolved <inheritdoc/> contributes nothing and the walk simply continues past it.
      foreach (var candidate in inheritable) {
        if (!string.IsNullOrEmpty(summary) && example != null)
          break;

        Absorb(Resolve(candidate, resolving));
      }

    resolving.Remove(docId);
    var result = new DocEntry(summary, example);

    // A member whose bare <inheritdoc/> was resolved WITHOUT its candidates has only been visited on
    // somebody else's behalf, not answered. Caching that emptiness would make its own summary depend
    // on which type the extractor happened to reach first.
    if (inheritdoc == null || !string.IsNullOrEmpty(cref) || inheritable != null)
      _resolved[docId] = result;

    return result;

    // Documentation is inherited to fill gaps, never to replace what the member says itself.
    void Absorb(DocEntry? inherited) {
      if (inherited == null)
        return;

      if (string.IsNullOrEmpty(summary))
        summary = inherited.Summary;

      example ??= inherited.Example;
    }
  }

  /// <summary>Collapses inline doc markup to one table-safe line.</summary>
  public static string Flatten(XElement? element) {
    if (element == null)
      return "";

    var sb = new StringBuilder();
    Walk(element, sb);
    // Pipes are left alone here. Escaping for the markdown table happens once, in the cell
    // renderer; doing it in both places put a literal backslash in every summary containing one.
    return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
  }

  static void Walk(XNode node, StringBuilder sb) {
    switch (node) {
      case XText t:
        sb.Append(t.Value);
        break;
      case XElement e:
        switch (e.Name.LocalName) {
          case "see" or "seealso": {
            var cref = e.Attribute("cref")?.Value ?? e.Attribute("langword")?.Value ?? "";
            var idx = cref.IndexOf(':');
            if (idx >= 0)
              cref = cref[(idx + 1)..];

            // Drop the parameter list before taking the last segment, or a cref like
            // "M:N.C.WriteBits(System.Int32,System.Int32)" would render as "Int32)".
            var paren = cref.IndexOf('(');
            if (paren >= 0)
              cref = cref[..paren];

            var name = cref.Split('.')[^1];

            // A generic cref carries its arity as a backtick ("BitBuffer`1"). Left in place it both
            // reads wrong and terminates the inline code span it sits inside.
            var tick = name.IndexOf('`');
            if (tick >= 0)
              name = name[..tick];

            sb.Append('`').Append(name).Append('`');
            break;
          }
          case "paramref" or "typeparamref":
            sb.Append('`').Append(e.Attribute("name")?.Value ?? "").Append('`');
            break;
          case "c":
            sb.Append('`').Append(e.Value).Append('`');
            break;
          case "code":
            break; // block content never belongs in a table cell
          default:
            foreach (var child in e.Nodes())
              Walk(child, sb);

            break;
        }

        break;
    }
  }

  /// <summary>Pulls the &lt;code&gt; block out of an &lt;example&gt;, undoing the doc-comment indent.</summary>
  static string? CodeOf(XElement? example) {
    if (example == null)
      return null;

    var code = example.Element("code") ?? example;
    var raw = code.Value.Replace("\r\n", "\n").Trim('\n');
    if (string.IsNullOrWhiteSpace(raw))
      return null;

    var lines = raw.Split('\n').Select(l => l.TrimEnd()).ToList();
    while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
      lines.RemoveAt(0);

    while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
      lines.RemoveAt(lines.Count - 1);

    if (lines.Count == 0)
      return null;

    var indent = lines.Where(l => !string.IsNullOrWhiteSpace(l))
      .Select(l => l.Length - l.TrimStart().Length)
      .DefaultIfEmpty(0)
      .Min();

    return string.Join("\n", lines.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart()));
  }
}

/// <summary>
///   Builds the compiler's documentation-comment IDs so metadata members can be matched to their
///   XML entries. Format per ECMA-334 Annex E.
/// </summary>
static class DocId {
  public static string ForType(Type t) => "T:" + TypeId(t);
  public static string ForField(FieldInfo f) => "F:" + TypeId(f.DeclaringType!) + "." + f.Name;
  public static string ForEvent(EventInfo e) => "E:" + TypeId(e.DeclaringType!) + "." + e.Name;

  public static string ForProperty(PropertyInfo p) {
    var id = "P:" + TypeId(p.DeclaringType!) + "." + p.Name;
    var index = p.GetIndexParameters();
    return index.Length == 0 ? id : id + "(" + string.Join(",", index.Select(i => ParamId(i.ParameterType))) + ")";
  }

  public static string ForMethod(MethodBase m) {
    var name = m is ConstructorInfo ? "#ctor" : m.Name;
    var sb = new StringBuilder("M:").Append(TypeId(m.DeclaringType!)).Append('.').Append(name.Replace('.', '#'));

    if (m is MethodInfo { IsGenericMethodDefinition: true } gm)
      sb.Append("``").Append(gm.GetGenericArguments().Length);

    var ps = m.GetParameters();
    if (ps.Length > 0)
      sb.Append('(').Append(string.Join(",", ps.Select(p => ParamId(p.ParameterType)))).Append(')');

    // Conversion operators are disambiguated by return type, not parameters.
    if (m is MethodInfo mi && mi.Name is "op_Implicit" or "op_Explicit")
      sb.Append('~').Append(ParamId(mi.ReturnType));

    return sb.ToString();
  }

  /// <summary>
  ///   Nested types join with '.'; the generic arity tick stays on the segment that declares it,
  ///   which is exactly how <c>Type.Name</c> already spells it (<c>Cache`2</c>).
  /// </summary>
  static string TypeId(Type t) {
    if (t.IsGenericParameter)
      return (t.DeclaringMethod != null ? "``" : "`") + t.GenericParameterPosition;

    return t.DeclaringType != null
      ? TypeId(t.DeclaringType) + "." + t.Name
      : string.IsNullOrEmpty(t.Namespace) ? t.Name : t.Namespace + "." + t.Name;
  }

  public static string ParamId(Type t) {
    if (t.IsByRef)
      return ParamId(t.GetElementType()!) + "@";

    if (t.IsArray) {
      var rank = t.GetArrayRank();
      var suffix = rank == 1 ? "[]" : "[" + string.Join(",", Enumerable.Repeat("0:", rank)) + "]";
      return ParamId(t.GetElementType()!) + suffix;
    }

    if (t.IsPointer)
      return ParamId(t.GetElementType()!) + "*";

    if (t.IsGenericParameter)
      return (t.DeclaringMethod != null ? "``" : "`") + t.GenericParameterPosition;

    if (t.IsGenericType) {
      var def = t.GetGenericTypeDefinition();
      var baseName = def.FullName ?? def.Name;
      var tick = baseName.IndexOf('`');
      if (tick >= 0)
        baseName = baseName[..tick];

      return baseName + "{" + string.Join(",", t.GetGenericArguments().Select(ParamId)) + "}";
    }

    return (t.FullName ?? t.Name).Replace('+', '.');
  }
}

/// <summary>
///   Where a bare <c>&lt;inheritdoc/&gt;</c> inherits from: the documentation IDs of the same member
///   on the base-type chain, then on the implemented interfaces, most-derived first. A type inherits
///   from its base types, then its interfaces.
///
///   <para>
///     The list is flattened rather than followed one hop at a time, which is what makes the chain
///     transitive for free: a base member that itself carries a bare <c>&lt;inheritdoc/&gt;</c>
///     contributes nothing and the next entry is tried.
///   </para>
///   <para>
///     Nothing here can ask the runtime what a method overrides — <c>GetBaseDefinition()</c> is not
///     available under <c>MetadataLoadContext</c> — so members are matched by name and signature.
///   </para>
/// </summary>
static class Inheritance {
  const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                                | BindingFlags.Static | BindingFlags.DeclaredOnly;

  /// <summary>
  ///   Deliberately lazy: the walk costs reflection on every ancestor, and only a member that
  ///   actually carries a bare <c>&lt;inheritdoc/&gt;</c> ever enumerates it.
  /// </summary>
  public static IEnumerable<string> ForType(Type type) {
    foreach (var id in Guarded(() => Ancestors(type).Select(a => DocId.ForType(a.Definition)).ToList()))
      yield return id;
  }

  /// <inheritdoc cref="ForType"/>
  public static IEnumerable<string> ForMember(MemberInfo member) {
    foreach (var id in Guarded(() => MemberCandidates(member)))
      yield return id;
  }

  /// <summary>
  ///   A base type or interface living in an assembly that cannot be resolved makes the walk throw.
  ///   A missing summary is a far better outcome than a failed run, and the extractor already reports
  ///   unresolvable types on their own.
  /// </summary>
  static List<string> Guarded(Func<List<string>> build) {
    try {
      return build();
    } catch {
      return [];
    }
  }

  static List<string> MemberCandidates(MemberInfo member) {
    if (member.DeclaringType is not { } declaring)
      return [];

    var ids = new List<string>();
    foreach (var (definition, arguments) in Ancestors(declaring))
      if (MatchIn(definition, arguments, member) is { } id)
        ids.Add(id);

    return ids;
  }

  /// <summary>
  ///   Base types nearest first, then every implemented interface. <c>GetInterfaces()</c> returns the
  ///   flattened set in an order metadata does not define, so the interfaces are put in a stable
  ///   most-derived-first one: an interface that extends another always reports strictly more
  ///   interfaces of its own, and the type's spelling breaks ties so two runs never disagree.
  /// </summary>
  static List<(Type Definition, Type[] Arguments)> Ancestors(Type type) {
    var result = new List<(Type, Type[])>();

    for (var b = type.BaseType; b != null; b = b.BaseType)
      result.Add(Split(b));

    result.AddRange(type.GetInterfaces()
      .OrderByDescending(i => i.GetInterfaces().Length)
      .ThenBy(i => i.ToString(), StringComparer.Ordinal)
      .Select(Split));

    return result;
  }

  /// <summary>
  ///   Splits an ancestor into the definition that owns the documentation and the arguments it was
  ///   instantiated with. The documentation ID of a member always spells the DECLARING type's generic
  ///   parameters (<c>`0</c>), never whatever the inheriting type substituted for them.
  /// </summary>
  static (Type Definition, Type[] Arguments) Split(Type type) =>
    type.IsGenericType && !type.IsGenericTypeDefinition
      ? (type.GetGenericTypeDefinition(), type.GetGenericArguments())
      : (type, []);

  static string? MatchIn(Type definition, Type[] arguments, MemberInfo member) {
    switch (member) {
      case FieldInfo field: {
        // Fields are never overridden, but one can hide a base field of the same name.
        var found = definition.GetFields(Declared).FirstOrDefault(f => f.Name == field.Name);
        return found == null ? null : DocId.ForField(found);
      }
      case EventInfo evt: {
        var found = definition.GetEvents(Declared).FirstOrDefault(e => e.Name == evt.Name);
        return found == null ? null : DocId.ForEvent(found);
      }
      case PropertyInfo property: {
        var found = definition.GetProperties(Declared).FirstOrDefault(p =>
          NameMatches(p.Name, property.Name, definition)
          && SignatureMatches(p.GetIndexParameters(), property.GetIndexParameters(), arguments));
        return found == null ? null : DocId.ForProperty(found);
      }
      case ConstructorInfo ctor: {
        if (definition.IsInterface)
          return null;

        var found = definition.GetConstructors(Declared).FirstOrDefault(c =>
          SignatureMatches(c.GetParameters(), ctor.GetParameters(), arguments));
        return found == null ? null : DocId.ForMethod(found);
      }
      case MethodInfo method: {
        var found = definition.GetMethods(Declared).FirstOrDefault(m =>
          NameMatches(m.Name, method.Name, definition)
          && Arity(m) == Arity(method)
          && SignatureMatches(m.GetParameters(), method.GetParameters(), arguments)
          // Conversion operators are told apart by their return type and nothing else.
          && (!Naming.IsConversionOperator(method)
              || DocId.ParamId(Substitute(m.ReturnType, arguments)) == DocId.ParamId(method.ReturnType)));
        return found == null ? null : DocId.ForMethod(found);
      }
      default:
        return null;
    }
  }

  static int Arity(MethodInfo m) => m.IsGenericMethodDefinition ? m.GetGenericArguments().Length : 0;

  /// <summary>
  ///   An explicit interface implementation carries the interface in front of the member name
  ///   (<c>Fixture.Package.INamed.Rename</c>), so what the interface itself declares is the last
  ///   segment. The member name never contains a dot of its own, which makes the last one the
  ///   separator.
  /// </summary>
  static bool NameMatches(string candidate, string memberName, Type definition) {
    if (candidate == memberName)
      return true;

    var dot = memberName.LastIndexOf('.');
    if (dot <= 0 || !definition.IsInterface || candidate != memberName[(dot + 1)..])
      return false;

    var simple = definition.Name;
    var tick = simple.IndexOf('`');
    return memberName[..dot].Contains(tick >= 0 ? simple[..tick] : simple, StringComparison.Ordinal);
  }

  static bool SignatureMatches(ParameterInfo[] candidate, ParameterInfo[] member, Type[] arguments) {
    if (candidate.Length != member.Length)
      return false;

    for (var i = 0; i < candidate.Length; ++i)
      if (DocId.ParamId(Substitute(candidate[i].ParameterType, arguments)) != DocId.ParamId(member[i].ParameterType))
        return false;

    return true;
  }

  /// <summary>
  ///   Rewrites a signature written in the ancestor's own generic parameters into the terms the
  ///   inheriting type sees, so the two can be compared. <c>class Ints : IStore&lt;int&gt;</c> has to
  ///   match <c>IStore</c>'s <c>Add(`0)</c> against <c>Add(int)</c>, and
  ///   <c>class Flipped&lt;TA, TB&gt; : IPair&lt;TB, TA&gt;</c> has to survive the reordering. A
  ///   method's own type parameters (<c>``0</c>) belong to the method and are left alone.
  /// </summary>
  static Type Substitute(Type type, Type[] arguments) {
    if (arguments.Length == 0)
      return type;

    if (type.IsGenericParameter)
      return type.DeclaringMethod == null && type.GenericParameterPosition < arguments.Length
        ? arguments[type.GenericParameterPosition]
        : type;

    if (type.IsByRef)
      return Substitute(type.GetElementType()!, arguments).MakeByRefType();

    if (type.IsPointer)
      return Substitute(type.GetElementType()!, arguments).MakePointerType();

    if (type.IsArray) {
      var element = Substitute(type.GetElementType()!, arguments);
      return type.IsSZArray ? element.MakeArrayType() : element.MakeArrayType(type.GetArrayRank());
    }

    if (!type.IsGenericType)
      return type;

    return type.GetGenericTypeDefinition()
      .MakeGenericType(type.GetGenericArguments().Select(a => Substitute(a, arguments)).ToArray());
  }
}

// =============================================================================
//  Rendering
// =============================================================================

static class Renderer {
  public static string Render(ApiModel model) {
    var sb = new StringBuilder();

    if (model.Namespaces.Count == 0)
      throw new ApiExtractionException(
        "the assembly exposes no public or protected types. A shipping package with an empty public " +
        "surface is almost always a build or target-framework problem, not a real result.");

    foreach (var ns in model.Namespaces) {
      sb.AppendLine($"### Namespace `{ns.Name}`");
      sb.AppendLine();

      // A compact linked index rather than a Type/Kind/Summary table: every one of those columns is
      // repeated verbatim in the per-type section immediately below, and on a package the size of
      // Hawkynt.FileFormats.Archives (781 types) that duplication is most of the file. This keeps
      // the navigation and drops the copy.
      sb.AppendLine(string.Join(" · ", ns.Types.Select(t => $"[`{t.DisplayName}`](#{Anchor(t.DisplayName)})")));
      sb.AppendLine();

      foreach (var t in ns.Types)
        RenderType(sb, t);
    }

    return Normalize(sb.ToString());
  }

  /// <summary>
  ///   The rendered body must not depend on the host's line endings. StringBuilder.AppendLine writes
  ///   Environment.NewLine, so an unnormalized body is CRLF on Windows and LF on Linux -- which would
  ///   make the drift check fail purely for running on the other operating system. The splicer puts
  ///   the file's own convention back afterwards.
  /// </summary>
  static string Normalize(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd();

  static void RenderType(StringBuilder sb, TypeDoc t) {
    sb.AppendLine($"#### `{t.DisplayName}`");
    sb.AppendLine();

    if (!string.IsNullOrEmpty(t.Summary)) {
      sb.AppendLine(t.Summary);
      sb.AppendLine();
    }

    var relations = new List<string>();
    if (t.BaseType != null)
      relations.Add($"Inherits `{t.BaseType}`.");

    if (t.Interfaces.Count > 0)
      relations.Add("Implements " + string.Join(", ", t.Interfaces.Select(i => $"`{i}`")) + ".");

    if (relations.Count > 0) {
      sb.AppendLine(string.Join(" ", relations));
      sb.AppendLine();
    }

    if (t.Members.Count == 0)
      sb.AppendLine("_No public or protected members._");
    else {
      sb.AppendLine(t.IsEnum ? "| Value | Numeric | Summary |" : "| Member | Signature | Summary |");
      sb.AppendLine("| --- | --- | --- |");
      foreach (var m in t.Members)
        sb.AppendLine($"| {Cell(m.Name)} | {Cell(m.Signature)} | {Cell(m.Summary)} |");
    }

    sb.AppendLine();

    if (t.Example != null) {
      sb.AppendLine("```csharp");
      sb.AppendLine(t.Example);
      sb.AppendLine("```");
      sb.AppendLine();
    }
  }

  /// <summary>
  ///   Makes a value safe for a markdown table cell. An unescaped pipe splits the row, and a
  ///   signature can legitimately contain one — <c>operator |</c> does.
  /// </summary>
  static string Cell(string s) => s.Replace("\r", "").Replace("\n", " ").Replace("|", "\\|");

  /// <summary>Exposed for the self-test.</summary>
  public static string CellForTest(string s) => Cell(s);

  /// <summary>GitHub's heading-anchor rules, applied to a fenced type heading.</summary>
  public static string Anchor(string display) {
    var lowered = display.ToLowerInvariant();
    var sb = new StringBuilder();
    foreach (var c in lowered)
      if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
        sb.Append(c);
      else if (c == ' ')
        sb.Append('-');

    return sb.ToString();
  }
}

// =============================================================================
//  README splicing
// =============================================================================

sealed class SpliceException(string message) : Exception(message);

sealed class ApiExtractionException(string message) : Exception(message);

/// <summary>
///   The generated reference as a file of its own.
///   <para>
///     It used to be spliced into the README. FrameworkExtensions.Corlib generates about 973 KB of
///     tables across 382 types, and a README that size is not a README any more: nuget.org truncates
///     it, GitHub asks before rendering it, and the handful of hand-written paragraphs a consumer
///     needs first are buried under four hundred types. The README keeps the prose and points here.
///   </para>
///   <para>
///     The pointer is an absolute URL because a package README renders on nuget.org, where a
///     relative link resolves nowhere — the same rule the linter enforces on every other link.
///   </para>
/// </summary>
static class ReferenceDocument {
  public const string FileName = "REFERENCE.md";

  /// <summary>
  ///   Where the README should point. A repository URL may be an scp-style or .git-suffixed clone
  ///   URL; nuget.org renders what it is given, so it is normalized to something a browser opens.
  /// </summary>
  public static string? Url(PackageProject pkg, string relativePath) {
    var repository = pkg.RepositoryUrl.Trim();
    if (repository.Length == 0)
      return null;

    if (repository.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
      repository = "https://" + repository[4..].Replace(':', '/');

    repository = Regex.Replace(repository, @"^git\+", "", RegexOptions.IgnoreCase);
    if (repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
      repository = repository[..^4];

    // RepositoryUrl is often not the repository at all but a browse URL pointing into it —
    // .../AnythingToGif/tree/master/GifFileFormat is what two repos here actually declare. Appending
    // to that produces .../tree/master/GifFileFormat/blob/main/GifFileFormat/REFERENCE.md, which is
    // nonsense that resolves nowhere. Everything from /tree/ or /blob/ onwards is a view of the
    // repository rather than part of its address.
    repository = Regex.Replace(repository, @"/(tree|blob|raw)/.*$", "", RegexOptions.IgnoreCase);

    repository = repository.TrimEnd('/');
    if (!repository.StartsWith("http", StringComparison.OrdinalIgnoreCase))
      return null;

    // Uri.EscapeDataString would escape the separators too. Only a space needs handling in practice,
    // and a repository whose folders contain one is a problem long before this line.
    return $"{repository}/blob/main/{relativePath.Replace(" ", "%20")}";
  }

  /// <summary>
  ///   The whole file, header and all. It is generated end to end — unlike the README there is no
  ///   hand-written prose here to preserve, which is the point of moving it out.
  /// </summary>
  public static string Build(PackageProject pkg, string generated, string existing, string readme, string? readmeUrl) {
    var newline = (existing.Length > 0 ? existing : readme).Contains("\r\n") ? "\r\n" : "\n";

    // Someone who followed the link from nuget.org has no other way back to the prose.
    var back = readmeUrl == null ? "" : $"[← {pkg.PackageId}]({readmeUrl})\n\n";

    var text = $"# {pkg.PackageId} — API reference\n\n"
             + back
             + Banner + "\n\n"
             + "> Every public and protected type and member, read from the built assembly and merged\n"
             + "> with its XML documentation. Generated — edit the XML docs in source, not this file.\n\n"
             + generated.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd() + "\n";

    text = Regex.Replace(text, @"\n{3,}", "\n\n");
    return newline == "\n" ? text : text.Replace("\n", "\r\n");
  }

  public const string Banner =
    "<!-- generated by Hawkynt/RepositoryTemplate/package-readme — edit the XML docs in source, not here -->";
}

static class ReadmeSplicer {
  /// <summary>
  ///   What stands in the README where the reference used to be: one line saying what is there and
  ///   where it went. Counting the types is what makes it worth following rather than boilerplate.
  /// </summary>
  public static string Pointer(string referenceUrl, int typeCount) {
    var types = typeCount == 1 ? "1 type" : $"{typeCount} types";
    return $"Every public and protected member of all {types}, generated from the built assembly and "
         + $"its XML documentation, is in [{ReferenceDocument.FileName}]({referenceUrl}).";
  }

  /// <summary>
  ///   Replaces only the marked region, so every hand-written word outside it survives.
  /// </summary>
  public static string Splice(string readme, string generated) {
    var newline = readme.Contains("\r\n") ? "\r\n" : "\n";
    var normalized = readme.Replace("\r\n", "\n");
    generated = generated.Replace("\r\n", "\n").Replace('\r', '\n');

    var begin = normalized.IndexOf(BeginMarkerConst, StringComparison.Ordinal);
    var end = normalized.IndexOf(EndMarkerConst, StringComparison.Ordinal);

    if (begin < 0 && end >= 0)
      throw new SpliceException($"found {EndMarkerConst} with no matching API:BEGIN marker.");

    if (begin >= 0 && end < 0)
      throw new SpliceException($"found an API:BEGIN marker with no matching {EndMarkerConst}.");

    if (begin >= 0 && end < begin)
      throw new SpliceException("the API:END marker appears before API:BEGIN.");

    if (normalized.IndexOf(BeginMarkerConst, begin + 1, StringComparison.Ordinal) > 0)
      throw new SpliceException("more than one API:BEGIN marker — the generated region must be unique.");

    var body = BannerConst + "\n\n" + generated + "\n\n" + EndMarkerConst;

    string result;
    if (begin >= 0)
      result = normalized[..begin] + body + normalized[(end + EndMarkerConst.Length)..];
    else {
      // No markers yet: insert under the API reference heading, creating it if absent.
      var heading = Regex.Match(normalized, @"^## 📚 API reference[^\n]*$", RegexOptions.Multiline);
      if (!heading.Success)
        throw new SpliceException(
          "no '## 📚 API reference' heading and no API:BEGIN marker. Add the heading; the generator fills the body.");

      var insertAt = heading.Index + heading.Length;
      result = normalized[..insertAt] + "\n\n" + body + normalized[insertAt..];
    }

    result = Regex.Replace(result, @"\n{3,}", "\n\n");
    if (!result.EndsWith('\n'))
      result += "\n";

    return newline == "\n" ? result : result.Replace("\n", "\r\n");
  }

  const string BeginMarkerConst = "<!-- API:BEGIN";
  const string EndMarkerConst = "<!-- API:END -->";
  const string BannerConst =
    "<!-- API:BEGIN generated by Hawkynt/RepositoryTemplate/package-readme — edit the XML docs in source, not here -->";
}

// =============================================================================
//  Structural linter
// =============================================================================

static class Linter {
  public static readonly string[] RequiredHeadings = [
    "## 📦 Installation",
    "## ✨ Features",
    "## 🚀 Quick start",
    "## 📚 API reference",
    "## 🔌 Dependencies",
    "## ⚠️ Limitations",
    "## ❤️ Support",
    "## 📜 License"
  ];

  /// <summary>
  ///   Blanks fenced blocks and inline code spans so the link check does not read code as markdown.
  ///   An array-typed conversion operator renders as
  ///   <c>static explicit operator TItem[](ReadOnlyArraySlice&lt;TItem&gt; this)</c>, whose
  ///   <c>[](</c> is indistinguishable from a link to a regular expression.
  /// </summary>
  static string StripCode(string markdown) {
    var withoutFences = Regex.Replace(markdown, "^```.*?^```", "", RegexOptions.Singleline | RegexOptions.Multiline);
    return Regex.Replace(withoutFences, "`[^`\n]*`", "");
  }

  public static List<Finding> Check(string readme, PackageProject pkg) {
    var findings = new List<Finding>();
    var lines = readme.Replace("\r\n", "\n").Split('\n');
    var where = $"{pkg.ReadmeRelative}";

    // --- H1 must be the package id, so nuget.org's page title matches what you install.
    var h1 = lines.FirstOrDefault(l => l.StartsWith("# "));
    if (h1 == null)
      findings.Add(Finding.Error($"{where}: no H1 title."));
    else if (h1[2..].Trim() != pkg.PackageId)
      findings.Add(Finding.Error($"{where}: H1 is '{h1[2..].Trim()}' but the package id is '{pkg.PackageId}'."));

    // --- Badges, then a one-sentence blockquote, directly under the title.
    var h1Index = Array.FindIndex(lines, l => l.StartsWith("# "));
    if (h1Index >= 0) {
      var after = lines.Skip(h1Index + 1).Take(12).ToList();
      if (!after.Any(l => l.Contains("![") && l.Contains("](")))
        findings.Add(Finding.Error($"{where}: no badge block under the title."));

      if (!after.Any(l => l.TrimStart().StartsWith('>')))
        findings.Add(Finding.Error($"{where}: no '>' blockquote describing the package under the badges."));
    }

    // --- Required headings, in the canonical order.
    var headings = lines.Where(l => l.StartsWith("## ")).Select(l => l.Trim()).ToList();
    var lastFound = -1;
    var outOfOrder = false;
    foreach (var required in RequiredHeadings) {
      var idx = headings.FindIndex(h => h.StartsWith(required, StringComparison.Ordinal));
      if (idx < 0) {
        findings.Add(Finding.Error($"{where}: missing required heading '{required}'."));
        continue;
      }

      if (idx < lastFound)
        outOfOrder = true;

      lastFound = idx;
    }

    if (outOfOrder)
      findings.Add(Finding.Error(
        $"{where}: required headings are out of order. Canonical order: {string.Join(" → ", RequiredHeadings)}."));

    // --- Relative links silently break once the README is rendered on nuget.org.
    foreach (var m in Regex.Matches(StripCode(readme), @"\[[^\]]*\]\(([^)]+)\)").Cast<Match>()) {
      var target = m.Groups[1].Value.Trim();
      if (target.StartsWith('#') || target.StartsWith("http://") || target.StartsWith("https://")
          || target.StartsWith("mailto:"))
        continue;

      findings.Add(Finding.Error(
        $"{where}: relative link '{target}' — package READMEs render on nuget.org, so links must be absolute."));
    }

    // --- Unreplaced template tokens.
    foreach (var m in Regex.Matches(readme, @"\{\{[A-Z_]+\}\}").Cast<Match>().Take(1))
      findings.Add(Finding.Error($"{where}: unreplaced template placeholder '{m.Value}'."));

    return findings;
  }
}

// =============================================================================
//  Self-test
// =============================================================================

static class SelfTest {
  static int _passed;
  static int _failed;

  public static int Run(bool verbose) {
    Console.WriteLine("package-readme self-test");
    Console.WriteLine();

    AnchorTests();
    FlattenTests();
    SpliceTests();
    LiteralTests();
    LinterTests();
    FixtureTest(verbose);

    Console.WriteLine();
    Console.WriteLine($"{_passed} passed, {_failed} failed.");
    return _failed > 0 ? 1 : 0;
  }

  static void Check(string name, string expected, string actual) {
    if (string.Equals(expected, actual, StringComparison.Ordinal)) {
      ++_passed;
      return;
    }

    ++_failed;
    Console.WriteLine($"  FAIL {name}");
    Console.WriteLine($"       expected: {expected}");
    Console.WriteLine($"       actual:   {actual}");
  }

  static void CheckTrue(string name, bool condition) {
    if (condition) {
      ++_passed;
      return;
    }

    ++_failed;
    Console.WriteLine($"  FAIL {name}");
  }

  static void AnchorTests() {
    Check("anchor/simple", "bitwriter", Renderer.Anchor("BitWriter"));
    Check("anchor/generic", "cachetkey-tvalue", Renderer.Anchor("Cache<TKey, TValue>"));
    Check("anchor/nested", "outerinner", Renderer.Anchor("Outer.Inner"));
  }

  static void FlattenTests() {
    Check("flatten/plain", "Hello world.", XmlDocs.Flatten(XElement.Parse("<summary>Hello\n  world.</summary>")));
    Check("flatten/see", "Use `Stream`.", XmlDocs.Flatten(XElement.Parse("<summary>Use <see cref=\"T:System.IO.Stream\"/>.</summary>")));
    Check("flatten/paramref", "The `count` bits.", XmlDocs.Flatten(XElement.Parse("<summary>The <paramref name=\"count\"/> bits.</summary>")));
    Check("flatten/c", "Pass `null`.", XmlDocs.Flatten(XElement.Parse("<summary>Pass <c>null</c>.</summary>")));
    // A pipe would otherwise split the markdown table cell it lands in.
    // Flatten leaves pipes alone; Cell escapes them exactly once.
    Check("flatten/pipe-untouched", "a | b", XmlDocs.Flatten(XElement.Parse("<summary>a | b</summary>")));
    Check("cell/pipe-escaped-once", "a \\| b", Renderer.CellForTest("a | b"));
    Check("flatten/empty", "", XmlDocs.Flatten(null));
  }

  static void SpliceTests() {
    const string readme = "# Pkg\n\n## 📚 API reference\n\n## 🔌 Dependencies\n";
    var spliced = ReadmeSplicer.Splice(readme, "BODY");
    CheckTrue("splice/creates-region", spliced.Contains("API:BEGIN") && spliced.Contains("BODY") && spliced.Contains("API:END"));
    CheckTrue("splice/keeps-following-heading", spliced.Contains("## 🔌 Dependencies"));

    // Idempotence is what makes the drift check trustworthy: generating twice must not churn.
    var twice = ReadmeSplicer.Splice(spliced, "BODY");
    Check("splice/idempotent", spliced, twice);

    var replaced = ReadmeSplicer.Splice(spliced, "OTHER");
    CheckTrue("splice/replaces-body", replaced.Contains("OTHER") && !replaced.Contains("BODY"));

    CheckTrue("splice/preserves-outside-prose",
      ReadmeSplicer.Splice("# Pkg\n\n## 📚 API reference\n\nKeep me.\n\n## 🔌 Dependencies\n", "B").Contains("Keep me."));

    // Line endings must never flap. A CRLF body spliced into an LF README used to leave the file
    // mixed, so the NEXT write converted the whole thing to CRLF -- two consecutive writes produced
    // different bytes, and a check on Linux disagreed with a write on Windows.
    var lfFile = "# Pkg\n\n## 📚 API reference\n\n## 🔌 Dependencies\n";
    var crlfBody = "line one\r\nline two";
    var splicedLf = ReadmeSplicer.Splice(lfFile, crlfBody);
    CheckTrue("splice/lf-file-stays-lf", !splicedLf.Contains('\r'));
    Check("splice/lf-stable-across-writes", splicedLf, ReadmeSplicer.Splice(splicedLf, crlfBody));

    var crlfFile = lfFile.Replace("\n", "\r\n");
    var splicedCrlf = ReadmeSplicer.Splice(crlfFile, "line one\nline two");
    CheckTrue("splice/crlf-file-stays-crlf", !Regex.IsMatch(splicedCrlf, @"(?<!\r)\n"));
    Check("splice/crlf-stable-across-writes", splicedCrlf, ReadmeSplicer.Splice(splicedCrlf, "line one\nline two"));

    // The same body must splice to the same bytes regardless of the host's line endings.
    Check("splice/host-independent", splicedLf, ReadmeSplicer.Splice(lfFile, "line one\nline two"));

    Throws("splice/unbalanced-begin", () => ReadmeSplicer.Splice("# P\n<!-- API:BEGIN -->\nx\n", "B"));
    Throws("splice/unbalanced-end", () => ReadmeSplicer.Splice("# P\n<!-- API:END -->\n", "B"));
    Throws("splice/no-heading", () => ReadmeSplicer.Splice("# P\n\n## Other\n", "B"));
    Throws("splice/duplicate-begin",
      () => ReadmeSplicer.Splice("# P\n<!-- API:BEGIN -->\n<!-- API:BEGIN -->\n<!-- API:END -->\n", "B"));
  }

  static void Throws(string name, Func<string> action) {
    try {
      action();
      ++_failed;
      Console.WriteLine($"  FAIL {name} (expected an error, got none)");
    } catch (SpliceException) {
      ++_passed;
    }
  }

  static void LiteralTests() {
    // A raw control character in a default value turns the README into a binary file.
    Check("literal/nul-char", @"'\0'", Signatures.LiteralForTest('\0'));
    Check("literal/newline-char", @"'\n'", Signatures.LiteralForTest('\n'));
    Check("literal/pipe-char", @"'|'", Signatures.LiteralForTest('|'));
    // "operator |" would otherwise split the markdown table row it sits in.
    Check("cell/pipe-escaped", @"`operator \|`", Renderer.CellForTest("`operator |`"));
    Check("literal/string-escapes", "\"a\\\"b\"", Signatures.LiteralForTest("a\"b"));
    Check("literal/null", "null", Signatures.LiteralForTest(null));
    Check("identifier/keyword-escaped", "@this", Naming.Identifier("this"));
    Check("identifier/ordinary-untouched", "value2", Naming.Identifier("value2"));

    // MSBuild hands back obj\Release/net10.0/X.xml on Linux too. Left alone, the backslash is part
    // of the filename there, the XML docs are never found, and the reference loses every summary.
    var sep = Path.DirectorySeparatorChar;
    Check("msbuild-path/backslashes-normalized",
      $"obj{sep}Release{sep}net10.0{sep}Thing.xml",
      ProjectDiscovery.NormalizeSeparators(@"obj\Release/net10.0/Thing.xml"));
    Check("msbuild-path/forward-slashes-untouched",
      $"obj{sep}Release{sep}Thing.xml",
      ProjectDiscovery.NormalizeSeparators($"obj{sep}Release{sep}Thing.xml"));

    // A package README renders on nuget.org, so the link to the reference has to be absolute and has
    // to be something a browser opens — not whatever clone URL the csproj happened to record.
    static PackageProject WithRepository(string url) =>
      new("p.csproj", "p", "My.Pkg", "README.md", "README.md", true, null, null, [], [], "net10.0", url);

    Check("reference-url/plain",
      "https://github.com/Hawkynt/Example/blob/main/Lib/REFERENCE.md",
      ReferenceDocument.Url(WithRepository("https://github.com/Hawkynt/Example"), "Lib/REFERENCE.md")!);
    Check("reference-url/dot-git-stripped",
      "https://github.com/Hawkynt/Example/blob/main/REFERENCE.md",
      ReferenceDocument.Url(WithRepository("https://github.com/Hawkynt/Example.git"), "REFERENCE.md")!);
    Check("reference-url/trailing-slash-stripped",
      "https://github.com/Hawkynt/Example/blob/main/REFERENCE.md",
      ReferenceDocument.Url(WithRepository("https://github.com/Hawkynt/Example/"), "REFERENCE.md")!);
    Check("reference-url/scp-form-normalized",
      "https://github.com/Hawkynt/Example/blob/main/REFERENCE.md",
      ReferenceDocument.Url(WithRepository("git@github.com:Hawkynt/Example.git"), "REFERENCE.md")!);
    Check("reference-url/space-escaped",
      "https://github.com/Hawkynt/Example/blob/main/My%20Lib/REFERENCE.md",
      ReferenceDocument.Url(WithRepository("https://github.com/Hawkynt/Example"), "My Lib/REFERENCE.md")!);
    Check("reference-url/tree-suffix-stripped",
      "https://github.com/Hawkynt/Example/blob/main/Lib/REFERENCE.md",
      ReferenceDocument.Url(WithRepository("https://github.com/Hawkynt/Example/tree/master/Lib"), "Lib/REFERENCE.md")!);
    Check("reference-url/blob-suffix-stripped",
      "https://github.com/Hawkynt/Example/blob/main/REFERENCE.md",
      ReferenceDocument.Url(WithRepository("https://github.com/Hawkynt/Example/blob/main/README.md"), "REFERENCE.md")!);
    CheckTrue("reference-url/absent-repository-is-null",
      ReferenceDocument.Url(WithRepository(""), "REFERENCE.md") == null);

    // The pointer is what a reader meets in the README, so it says how much is behind the link.
    CheckTrue("pointer/counts-types",
      ReadmeSplicer.Pointer("https://x/REFERENCE.md", 382).Contains("all 382 types"));
    CheckTrue("pointer/singular",
      ReadmeSplicer.Pointer("https://x/REFERENCE.md", 1).Contains("all 1 type,"));
    CheckTrue("pointer/links-absolutely",
      ReadmeSplicer.Pointer("https://x/REFERENCE.md", 3).Contains("(https://x/REFERENCE.md)"));
    CheckTrue("pointer/survives-the-link-linter",
      Linter.Check(GoodReadme().Replace("## 📚 API reference",
          "## 📚 API reference\n\n" + ReadmeSplicer.Pointer("https://x/REFERENCE.md", 3)),
        WithRepository("https://github.com/Hawkynt/Example")).All(f => f.Advisory));
  }

  /// <summary>A README that passes every structural rule, for tests that alter one thing about it.</summary>
  static string GoodReadme() =>
    "# My.Pkg\n\n[![NuGet](https://img.shields.io/x)](https://nuget.org/packages/My.Pkg)\n\n> A thing.\n\n"
    + string.Join("\n\n", Linter.RequiredHeadings) + "\n";

  static void LinterTests() {
    var pkg = new PackageProject("p.csproj", "p", "My.Pkg", "README.md", "README.md", true, null, null, [], [], "net10.0",
      "https://github.com/Hawkynt/Example");

    var good = "# My.Pkg\n\n[![NuGet](https://img.shields.io/x)](https://nuget.org/packages/My.Pkg)\n\n> A thing.\n\n"
               + string.Join("\n\n", Linter.RequiredHeadings) + "\n";
    CheckTrue("lint/clean-readme-passes", Linter.Check(good, pkg).All(f => f.Advisory));

    CheckTrue("lint/wrong-h1", Linter.Check(good.Replace("# My.Pkg", "# Other"), pkg)
      .Any(f => !f.Advisory && f.Message.Contains("H1 is")));

    CheckTrue("lint/missing-heading", Linter.Check(good.Replace("## ⚠️ Limitations\n\n", ""), pkg)
      .Any(f => !f.Advisory && f.Message.Contains("Limitations")));

    CheckTrue("lint/relative-link", Linter.Check(good + "\n[LICENSE](../LICENSE)\n", pkg)
      .Any(f => !f.Advisory && f.Message.Contains("relative link")));

    CheckTrue("lint/anchor-link-allowed", Linter.Check(good + "\n[top](#my-pkg)\n", pkg)
      .All(f => f.Advisory || !f.Message.Contains("relative link")));

    // A generated signature is not a link. An array-typed conversion operator renders as
    // "operator TItem[](Slice<TItem> this)", whose "[](" reads exactly like one.
    CheckTrue("lint/code-span-is-not-a-link", Linter.Check(
        good + "\n| `op` | `static explicit operator TItem[](Slice<TItem> this)` | x |\n", pkg)
      .All(f => f.Advisory || !f.Message.Contains("relative link")));

    CheckTrue("lint/fenced-block-is-not-a-link", Linter.Check(
        good + "\n```csharp\nvar x = arr[0](y);\n```\n", pkg)
      .All(f => f.Advisory || !f.Message.Contains("relative link")));

    CheckTrue("lint/placeholder", Linter.Check(good.Replace("A thing.", "{{DESCRIPTION}}"), pkg)
      .Any(f => !f.Advisory && f.Message.Contains("placeholder")));

    CheckTrue("lint/no-badges", Linter.Check(good.Replace("[![NuGet](https://img.shields.io/x)](https://nuget.org/packages/My.Pkg)", "x"), pkg)
      .Any(f => !f.Advisory && f.Message.Contains("badge")));

    CheckTrue("lint/out-of-order", Linter.Check(SwapTwoHeadings(good), pkg)
      .Any(f => !f.Advisory && f.Message.Contains("out of order")));
  }

  /// <summary>Swaps two required headings, to prove order is enforced and not merely presence.</summary>
  static string SwapTwoHeadings(string readme) => readme
    .Replace("## ✨ Features", "@@SWAP@@")
    .Replace("## 📦 Installation", "## ✨ Features")
    .Replace("@@SWAP@@", "## 📦 Installation");

  /// <summary>
  ///   The compiler bakes in this file's own path, which is the only reliable way for a file-based
  ///   app to find files that sit next to its source.
  /// </summary>
  static string ScriptPath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

  static void FixtureTest(bool verbose) {
    var scriptDir = Path.GetDirectoryName(ScriptPath()) ?? ".";
    var fixture = Path.Combine(scriptDir, "fixtures", "Fixture.Package");
    if (!Directory.Exists(fixture)) {
      Console.WriteLine("  SKIP fixture golden test (fixtures/Fixture.Package not found)");
      return;
    }

    Console.WriteLine("  building fixture...");
    var build = Process.Start(new ProcessStartInfo("dotnet",
      $"build \"{Path.Combine(fixture, "Fixture.Package.csproj")}\" -c Release -nologo -v quiet") {
      RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
    })!;
    build.StandardOutput.ReadToEnd();
    var buildErr = build.StandardError.ReadToEnd();
    build.WaitForExit();
    if (build.ExitCode != 0) {
      ++_failed;
      Console.WriteLine($"  FAIL fixture build: {buildErr}");
      return;
    }

    var info = ProjectDiscovery.Describe(Path.Combine(fixture, "Fixture.Package.csproj"), "Release", null, verbose);
    if (info?.AssemblyPath == null) {
      ++_failed;
      Console.WriteLine("  FAIL fixture: project did not resolve to a packable project with an assembly");
      return;
    }

    var model = ApiExtractor.Extract(info.AssemblyPath, info.DocumentationPath);
    var rendered = Renderer.Render(model);

    var expectedPath = Path.Combine(scriptDir, "fixtures", "EXPECTED.md");
    if (!File.Exists(expectedPath)) {
      File.WriteAllText(expectedPath, rendered);
      Console.WriteLine($"  WROTE golden file {expectedPath} — review it, then re-run.");
      return;
    }

    var expected = File.ReadAllText(expectedPath).Replace("\r\n", "\n").TrimEnd();
    var actual = rendered.Replace("\r\n", "\n").TrimEnd();
    if (string.Equals(expected, actual, StringComparison.Ordinal)) {
      ++_passed;
      Console.WriteLine("  PASS fixture golden output");
    } else {
      ++_failed;
      Console.WriteLine("  FAIL fixture golden output");
      Console.WriteLine("       " + Diff.Describe(expected, actual).Replace("\n", "\n       "));
    }
  }
}

