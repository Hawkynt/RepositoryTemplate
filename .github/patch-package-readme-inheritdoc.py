from pathlib import Path

script = Path("scripts/package-readme.cs")
text = script.read_text(encoding="utf-8")
text = text.replace(
    'sealed class XmlDocs {\n  readonly Dictionary<string, DocEntry> _entries = new(StringComparer.Ordinal);',
    'sealed class XmlDocs {\n  readonly Dictionary<string, XElement> _members = new(StringComparer.Ordinal);\n  readonly Dictionary<string, DocEntry> _resolved = new(StringComparer.Ordinal);'
)
text = text.replace(
    '      var summary = Flatten(member.Element("summary"));\n      var example = CodeOf(member.Element("example"));\n      docs._entries[name] = new DocEntry(summary, example);',
    '      docs._members[name] = new XElement(member);'
)
text = text.replace(
    '  public DocEntry? Get(string docId) => _entries.GetValueOrDefault(docId);',
    '''  public DocEntry? Get(string docId) => Resolve(docId, []);\n\n  DocEntry? Resolve(string docId, HashSet<string> resolving) {\n    if (_resolved.TryGetValue(docId, out var cached))\n      return cached;\n\n    if (!_members.TryGetValue(docId, out var member))\n      return null;\n\n    // C# 14 extension-member implementation methods deliberately carry an <inheritdoc cref=\"...\"/>\n    // to the metadata extension member instead of duplicating its documentation. Following the cref is\n    // required by the language specification and also makes ordinary explicit-cref inheritdoc useful.\n    if (!resolving.Add(docId))\n      return new DocEntry("", null);\n\n    var summary = Flatten(member.Element("summary"));\n    var example = CodeOf(member.Element("example"));\n    var inheritdoc = member.Element("inheritdoc");\n    var cref = inheritdoc?.Attribute("cref")?.Value;\n    if (!string.IsNullOrEmpty(cref)) {\n      var inherited = Resolve(cref, resolving);\n      if (inherited != null) {\n        if (string.IsNullOrEmpty(summary))\n          summary = inherited.Summary;\n        example ??= inherited.Example;\n      }\n    }\n\n    resolving.Remove(docId);\n    var result = new DocEntry(summary, example);\n    _resolved[docId] = result;\n    return result;\n  }'''
)
script.write_text(text, encoding="utf-8")

api = Path("scripts/fixtures/Fixture.Package/Api.cs")
text = api.read_text(encoding="utf-8")
needle = '''public static class Helpers {\n\n  /// <summary>Covers an extension method.</summary>\n  public static int Doubled(this int value) => value * 2;\n}\n'''
replacement = needle + '''\n/// <summary>Covers C# 14 extension-member metadata.</summary>\npublic static class StaticExtensionFixture {\n  extension(int) {\n    /// <summary>Gets the bit width of the extended integer type.</summary>\n    public static int SupportedBits => 32;\n  }\n}\n'''
if needle not in text:
    raise SystemExit("fixture insertion point not found")
api.write_text(text.replace(needle, replacement), encoding="utf-8")

expected = Path("scripts/fixtures/EXPECTED.md")
text = expected.read_text(encoding="utf-8")
text = text.replace(
    '[`BitOrder`](#bitorder) · [`BitWriter`](#bitwriter) · [`Cache<TKey, TValue>`](#cachetkey-tvalue) · [`Cache<TKey, TValue>.Entry`](#cachetkey-tvalueentry) · [`Empty`](#empty) · [`Fraction`](#fraction) · [`Helpers`](#helpers) · [`INamed`](#inamed) · [`Location`](#location) · [`MostlyHidden`](#mostlyhidden) · [`NamedThing`](#namedthing) · [`Predicate<T>`](#predicatet) · [`Undocumented`](#undocumented)',
    '[`BitOrder`](#bitorder) · [`BitWriter`](#bitwriter) · [`Cache<TKey, TValue>`](#cachetkey-tvalue) · [`Cache<TKey, TValue>.Entry`](#cachetkey-tvalueentry) · [`Empty`](#empty) · [`Fraction`](#fraction) · [`Helpers`](#helpers) · [`INamed`](#inamed) · [`Location`](#location) · [`MostlyHidden`](#mostlyhidden) · [`NamedThing`](#namedthing) · [`Predicate<T>`](#predicatet) · [`StaticExtensionFixture`](#staticextensionfixture) · [`Undocumented`](#undocumented)'
)
needle = '''#### `Undocumented`\n'''
section = '''#### `StaticExtensionFixture`\n\nCovers C# 14 extension-member metadata.\n\n| Member | Signature | Summary |\n| --- | --- | --- |\n| `get_SupportedBits` | `static int get_SupportedBits()` | Gets the bit width of the extended integer type. |\n\n'''
if needle not in text:
    raise SystemExit("expected insertion point not found")
expected.write_text(text.replace(needle, section + needle), encoding="utf-8")
