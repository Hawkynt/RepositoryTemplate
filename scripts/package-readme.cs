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
//    --configuration <cfg>   build configuration to read (default: Release)
//    --project <path>        restrict to one .csproj (repeatable)
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
        case "--project":
          if (++i >= args.Length)
            return Fail("--project needs a value.");

          projects.Add(args[i]);
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

    return Runner.Execute(root, mode == "--write", configuration, projects, verbose);
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
  public static int Execute(string root, bool write, string configuration, List<string> explicitProjects, bool verbose) {
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
      var info = ProjectDiscovery.Describe(proj, configuration, verbose);
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
      var pkgFindings = Process(pkg, write, ref rewritten);
      foreach (var f in pkgFindings)
        Console.WriteLine($"    {(f.Advisory ? "warning" : "ERROR  ")} {f.Message}");

      findings.AddRange(pkgFindings);
    }

    var errors = findings.Count(f => !f.Advisory);
    var warnings = findings.Count(f => f.Advisory);

    Console.WriteLine();
    if (write)
      Console.WriteLine($"{rewritten} README(s) rewritten, {errors} error(s), {warnings} warning(s).");
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

  static List<Finding> Process(PackageProject pkg, bool write, ref int rewritten) {
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
    } catch (Exception e) {
      findings.Add(Finding.Error($"{pkg.ProjectName}: could not read assembly metadata — {e.Message}"));
      return findings;
    }

    if (pkg.DocumentationPath == null || !File.Exists(pkg.DocumentationPath))
      findings.Add(Finding.Warning(
        $"{pkg.ProjectName}: no XML documentation file. Set <GenerateDocumentationFile>true</GenerateDocumentationFile> " +
        "so summaries and <example> blocks reach the API reference."));

    findings.AddRange(model.Advisories.Select(Finding.Warning));

    var generated = Renderer.Render(model);

    string updated;
    try {
      updated = ReadmeSplicer.Splice(original, generated);
    } catch (SpliceException e) {
      findings.Add(Finding.Error($"{pkg.ProjectName}: {e.Message}"));
      return findings;
    }

    if (write) {
      if (!string.Equals(updated, original, StringComparison.Ordinal)) {
        File.WriteAllText(pkg.ReadmePath, updated);
        ++rewritten;
        Console.WriteLine($"    rewrote {pkg.ReadmeRelative}");
      }
    } else if (!string.Equals(updated, original, StringComparison.Ordinal))
      findings.Add(Finding.Error(
        $"{pkg.ProjectName}: the API reference in '{pkg.ReadmeRelative}' is out of date with the assembly. " +
        Diff.Describe(original, updated)));

    return findings;
  }
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
  List<string> MissingBundles);

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
    foreach (var f in Directory.EnumerateFiles(dir, "*.csproj"))
      into.Add(Path.GetFullPath(f));

    foreach (var sub in Directory.EnumerateDirectories(dir)) {
      var name = Path.GetFileName(sub);
      if (SkipDirectories.Contains(name, StringComparer.OrdinalIgnoreCase))
        continue;

      Walk(sub, into);
    }
  }

  /// <summary>
  ///   Asks MSBuild for the evaluated properties. This matters: some repos declare PackageId
  ///   explicitly, others rely on it defaulting from the .csproj filename. Parsing the XML would
  ///   only see the first kind.
  /// </summary>
  public static PackageProject? Describe(string projectPath, string configuration, bool verbose) {
    var evaluated = MsBuild.Evaluate(projectPath, configuration,
      "PackageId", "IsPackable", "OutputType", "PackageReadmeFile", "TargetPath", "DocumentationFile", "TargetFramework");

    if (evaluated == null) {
      if (verbose)
        Console.WriteLine($"  skipped (MSBuild evaluation failed): {projectPath}");

      return null;
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

    var readmeRelative = props.GetValueOrDefault("PackageReadmeFile", "");
    var projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
    var packageId = props.GetValueOrDefault("PackageId", "");
    if (string.IsNullOrEmpty(packageId))
      packageId = Path.GetFileNameWithoutExtension(projectPath);

    var readmePath = string.IsNullOrEmpty(readmeRelative)
      ? null
      : Path.GetFullPath(Path.Combine(projectDir, readmeRelative));

    var targetPath = props.GetValueOrDefault("TargetPath", "");
    var docFile = props.GetValueOrDefault("DocumentationFile", "");
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
      missing);
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
      var privateAssets = reference.GetValueOrDefault("PrivateAssets", "");
      if (!privateAssets.Split(';').Any(p => p.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)))
        continue;

      var identity = reference.GetValueOrDefault("Identity", "");
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

  /// <summary>
  ///   PackageReadmeFile names the file inside the package; it does not put it there. Without a
  ///   Pack'd None item `dotnet pack` fails NU5039, so the item is part of the contract.
  /// </summary>
  static bool IsReadmePacked(List<Dictionary<string, string>> noneItems, string readmeRelative) {
    var wanted = Path.GetFileName(readmeRelative);
    foreach (var item in noneItems) {
      var identity = item.GetValueOrDefault("Identity", "");
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
  public static MsBuildResult? Evaluate(string projectPath, string configuration, params string[] names) {
    var args = new StringBuilder();
    args.Append("msbuild \"").Append(projectPath).Append('"');
    foreach (var n in names)
      args.Append(" -getProperty:").Append(n);

    args.Append(" -getItem:None -getItem:ProjectReference");
    args.Append(" -p:Configuration=").Append(configuration).Append(" -nologo");

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

record ApiModel(List<NamespaceGroup> Namespaces, List<string> Advisories);
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
    foreach (var dir in inputs
               .Select(i => Path.GetDirectoryName(Path.GetFullPath(i.Assembly))!)
               .Prepend(RuntimeEnvironment.GetRuntimeDirectory())
               .Distinct(StringComparer.OrdinalIgnoreCase))
      if (Directory.Exists(dir))
        foreach (var f in Directory.GetFiles(dir, "*.dll"))
          byName[Path.GetFileNameWithoutExtension(f)] = f;

    using var mlc = new MetadataLoadContext(new PathAssemblyResolver(byName.Values));

    var groups = new SortedDictionary<string, List<TypeDoc>>(StringComparer.Ordinal);
    var undocumented = 0;
    var exampleless = new List<string>();
    var seen = new HashSet<string>(StringComparer.Ordinal);

    foreach (var (assemblyPath, documentationPath) in inputs) {
      var docs = XmlDocs.Load(documentationPath ?? Path.ChangeExtension(assemblyPath, ".xml"));
      var assembly = mlc.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));

      foreach (var type in assembly.GetTypes()) {
        if (!Visibility.IsVisibleApi(type) || Naming.IsCompilerGenerated(type))
          continue;

        // Two bundled assemblies can legitimately expose the same full type name; document once.
        if (!seen.Add(type.FullName ?? type.Name))
          continue;

        var doc = BuildType(type, docs, ref undocumented);
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

  static TypeDoc BuildType(Type type, XmlDocs docs, ref int undocumented) {
    var entry = docs.Get(DocId.ForType(type));
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
        var d = docs.Get(DocId.ForField(f));
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

        Add(members, docs, ref undocumented, Naming.BareTypeName(type), Signatures.ForConstructor(ctor), DocId.ForMethod(ctor), 0);
      }

      foreach (var f in type.GetFields(flags)) {
        if (!Visibility.IsVisibleMember(f) || Naming.IsCompilerGenerated(f))
          continue;

        Add(members, docs, ref undocumented, f.Name, Signatures.ForField(f), DocId.ForField(f), 1);
      }

      foreach (var p in type.GetProperties(flags)) {
        if (!Visibility.IsVisibleProperty(p) || Naming.IsCompilerGenerated(p))
          continue;

        Add(members, docs, ref undocumented, p.Name, Signatures.ForProperty(p), DocId.ForProperty(p), 2);
      }

      foreach (var m in type.GetMethods(flags)) {
        if (!Visibility.IsVisibleMember(m) || Naming.IsCompilerGenerated(m) || Naming.IsAccessor(m))
          continue;

        Add(members, docs, ref undocumented, Naming.MethodDisplayName(m), Signatures.ForMethod(m), DocId.ForMethod(m), m.IsSpecialName ? 4 : 3);
      }

      foreach (var e in type.GetEvents(flags)) {
        if (!Visibility.IsVisibleEvent(e) || Naming.IsCompilerGenerated(e))
          continue;

        Add(members, docs, ref undocumented, e.Name, Signatures.ForEvent(e), DocId.ForEvent(e), 5);
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

  static void Add(List<MemberDoc> into, XmlDocs docs, ref int undocumented, string name, string signature, string docId, int rank) {
    var d = docs.Get(docId);
    if (d?.Summary is null or "")
      ++undocumented;

    into.Add(new MemberDoc($"`{name}`", $"`{signature}`", d?.Summary ?? "", rank));
  }
}

static class Visibility {
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

    var suffix = "";
    if (p.HasDefaultValue) {
      var v = p.RawDefaultValue;
      suffix = " = " + v switch {
        null => "null",
        string s => "\"" + s + "\"",
        bool b => b ? "true" : "false",
        _ => Convert.ToString(v, CultureInfo.InvariantCulture)
      };
    }

    return $"{prefix}{Naming.FullDisplayName(p.ParameterType)} {p.Name}{suffix}";
  }
}

// =============================================================================
//  XML documentation
// =============================================================================

record DocEntry(string Summary, string? Example);

sealed class XmlDocs {
  readonly Dictionary<string, DocEntry> _entries = new(StringComparer.Ordinal);

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

      var summary = Flatten(member.Element("summary"));
      var example = CodeOf(member.Element("example"));
      docs._entries[name] = new DocEntry(summary, example);
    }

    return docs;
  }

  public DocEntry? Get(string docId) => _entries.GetValueOrDefault(docId);

  /// <summary>Collapses inline doc markup to one table-safe line.</summary>
  public static string Flatten(XElement? element) {
    if (element == null)
      return "";

    var sb = new StringBuilder();
    Walk(element, sb);
    var text = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    return text.Replace("|", "\\|");
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

  static string ParamId(Type t) {
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

// =============================================================================
//  Rendering
// =============================================================================

static class Renderer {
  public static string Render(ApiModel model) {
    var sb = new StringBuilder();

    if (model.Namespaces.Count == 0) {
      sb.AppendLine("_This package exposes no public types._");
      return sb.ToString().TrimEnd();
    }

    foreach (var ns in model.Namespaces) {
      sb.AppendLine($"### Namespace `{ns.Name}`");
      sb.AppendLine();
      sb.AppendLine("| Type | Kind | Summary |");
      sb.AppendLine("| --- | --- | --- |");
      foreach (var t in ns.Types)
        sb.AppendLine($"| [`{t.DisplayName}`](#{Anchor(t.DisplayName)}) | {t.Kind} | {Cell(t.Summary)} |");

      sb.AppendLine();

      foreach (var t in ns.Types)
        RenderType(sb, t);
    }

    return sb.ToString().TrimEnd();
  }

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
        sb.AppendLine($"| {m.Name} | {Cell(m.Signature)} | {Cell(m.Summary)} |");
    }

    sb.AppendLine();

    if (t.Example != null) {
      sb.AppendLine("```csharp");
      sb.AppendLine(t.Example);
      sb.AppendLine("```");
      sb.AppendLine();
    }
  }

  static string Cell(string s) => s.Replace("\r", "").Replace("\n", " ");

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

static class ReadmeSplicer {
  /// <summary>
  ///   Replaces only the marked region, so every hand-written word outside it survives.
  /// </summary>
  public static string Splice(string readme, string generated) {
    var newline = readme.Contains("\r\n") ? "\r\n" : "\n";
    var normalized = readme.Replace("\r\n", "\n");

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
    foreach (var m in Regex.Matches(readme, @"\[[^\]]*\]\(([^)]+)\)").Cast<Match>()) {
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
    Check("flatten/pipe-escaped", "a \\| b", XmlDocs.Flatten(XElement.Parse("<summary>a | b</summary>")));
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

  static void LinterTests() {
    var pkg = new PackageProject("p.csproj", "p", "My.Pkg", "README.md", "README.md", true, null, null, [], []);

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

    var info = ProjectDiscovery.Describe(Path.Combine(fixture, "Fixture.Package.csproj"), "Release", verbose);
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

