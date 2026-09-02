using System.Diagnostics.CodeAnalysis;

namespace Fixture.Package;

/// <summary>A bit-order selector, to cover the enum rendering path.</summary>
public enum BitOrder {
  /// <summary>Most significant bit first.</summary>
  MsbFirst = 0,

  /// <summary>Least significant bit first.</summary>
  LsbFirst = 1
}

/// <summary>Covers bare inheritdoc on the one member kind that has nothing to inherit from.</summary>
public enum ProbeOutcome {

  /// <summary>The probe found what it was looking for.</summary>
  Hit = 0,

  // An enum member overrides nothing and implements nothing, so a bare inheritdoc has no source and
  // the cell stays empty. What must NOT happen is the resolution throwing, or blanking the summary
  // the member states itself.
  /// <summary>The probe found nothing. Stated here, so the inheritdoc below must not overwrite it.</summary>
  /// <inheritdoc/>
  Miss = 1,

  /// <inheritdoc/>
  Inconclusive = 2
}

/// <summary>Writes individual bits to a buffer.</summary>
/// <example>
/// <code>
/// var writer = new BitWriter(BitOrder.MsbFirst);
/// writer.WriteBits(0b1011_0010, count: 8);
/// writer.Flush();
/// </code>
/// </example>
public sealed class BitWriter {

  /// <summary>The largest number of bits <see cref="WriteBits"/> accepts at once.</summary>
  public const int MaxBitsPerCall = 32;

  /// <summary>Creates a writer using the given bit order.</summary>
  /// <param name="order">Whether to emit the most or least significant bit first.</param>
  public BitWriter(BitOrder order) => this.Order = order;

  /// <summary>The configured bit order.</summary>
  public BitOrder Order { get; }

  /// <summary>How many bits have been written so far. Covers the init-only accessor path.</summary>
  public long Written { get; init; }

  /// <summary>Raised once the internal buffer is flushed.</summary>
  public event EventHandler? Flushed;

  /// <summary>Writes the <paramref name="count"/> low bits of <paramref name="value"/>.</summary>
  /// <param name="value">The source value.</param>
  /// <param name="count">How many low bits to take. Must not exceed <c>32</c>.</param>
  public void WriteBits(int value, int count) { }

  /// <summary>Pads to a byte boundary and flushes. Covers the optional-parameter path.</summary>
  public void Flush(bool padWithOnes = false) => this.Flushed?.Invoke(this, EventArgs.Empty);

  /// <summary>Covers ref, out and in parameters in one signature.</summary>
  public bool TryPack(ref int state, out byte packed, in long seed) {
    packed = 0;
    return false;
  }

  /// <summary>Covers the params array path.</summary>
  public void WriteAll(params int[] values) { }

  /// <summary>Covers an array-returning method and a multidimensional parameter.</summary>
  public byte[] Render(int[,] matrix) => [];
}

/// <summary>A generic cache, covering generic type parameters and nested types.</summary>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TValue">The stored type.</typeparam>
/// <example>
/// <code>
/// var cache = new Cache&lt;string, int&gt;();
/// cache.Set("answer", 42);
/// </code>
/// </example>
public class Cache<TKey, TValue> where TKey : notnull {

  /// <summary>One cached entry. Covers the nested-public-type path.</summary>
  public readonly record struct Entry(TKey Key, TValue Value);

  /// <summary>Stores a value.</summary>
  public void Set(TKey key, TValue value) { }

  /// <summary>Covers a generic method on a generic type.</summary>
  public TResult Map<TResult>(Func<TValue, TResult> projection) => default!;

  /// <summary>Indexer, which the doc-comment grammar spells as an <c>Item</c> property.</summary>
  public TValue this[TKey key] {
    get => default!;
    set { }
  }

  /// <summary>Covers the protected-member path — protected members are part of the public contract.</summary>
  protected virtual void OnEvicted(TKey key) { }

  /// <summary>Covers a protected field.</summary>
  protected readonly int MaxEntries = 128;
}

/// <summary>Covers operator overloads and conversions.</summary>
public readonly struct Fraction {

  /// <summary>Creates a fraction.</summary>
  public Fraction(int numerator, int denominator) {
    this.Numerator = numerator;
    this.Denominator = denominator;
  }

  /// <summary>The numerator.</summary>
  public int Numerator { get; }

  /// <summary>The denominator.</summary>
  public int Denominator { get; }

  /// <summary>Adds two fractions.</summary>
  public static Fraction operator +(Fraction left, Fraction right) => left;

  /// <summary>Converts to a double implicitly.</summary>
  public static implicit operator double(Fraction value) => 0d;

  /// <summary>Converts from an int explicitly.</summary>
  public static explicit operator Fraction(int value) => new(value, 1);
}

/// <summary>Covers the interface rendering path.</summary>
public interface INamed {

  /// <summary>The display name.</summary>
  string Name { get; }

  /// <summary>Renames the instance.</summary>
  void Rename(string name);
}

/// <summary>Covers explicit interface implementation and base-type reporting.</summary>
public sealed class NamedThing : INamed {

  /// <summary>The display name.</summary>
  public string Name => "thing";

  void INamed.Rename(string name) { }
}

// =============================================================================
//  Bare <inheritdoc/>
// =============================================================================

/// <summary>Covers documentation inherited from an interface.</summary>
public interface IProbe {

  /// <summary>Whether the last probe matched.</summary>
  bool Matched { get; }

  /// <summary>Probes <paramref name="data"/> and reports how many bytes were consumed.</summary>
  int Probe(byte[] data);
}

/// <inheritdoc/>
public sealed class BytewiseProbe : IProbe {

  /// <inheritdoc/>
  public bool Matched => false;

  /// <inheritdoc/>
  public int Probe(byte[] data) => 0;
}

/// <summary>Covers documentation inherited from an abstract base class.</summary>
public abstract class Codec {

  /// <summary>The name this codec is registered under.</summary>
  public abstract string Name { get; }

  /// <summary>Encodes <paramref name="input"/> and reports how many bytes were written.</summary>
  public abstract int Encode(byte[] input, int offset);

  /// <summary>Called once the codec has finished, whatever the outcome.</summary>
  protected virtual void OnFinished() { }
}

// Declared BEFORE the NullCodec it derives from, so it is documented first and reaches Codec through
// a NullCodec whose own bare inheritdoc has not been answered yet. Caching that non-answer would
// blank NullCodec's own rows further down the page.
/// <inheritdoc/>
public sealed class AliasCodec : NullCodec {

  /// <inheritdoc/>
  public override string Name => "alias";
}

// The middle link of a two-level chain: every member here inherits, so anything reaching NullCodec
// from below has to keep walking up to Codec.
/// <inheritdoc/>
public class NullCodec : Codec {

  /// <inheritdoc/>
  public override string Name => "null";

  /// <inheritdoc/>
  public override int Encode(byte[] input, int offset) => 0;

  /// <summary>Does nothing at all, and says so in its own words.</summary>
  /// <inheritdoc/>
  protected override void OnFinished() { }
}

/// <summary>Covers documentation inherited across a generic interface.</summary>
/// <typeparam name="TItem">The stored item type.</typeparam>
public interface IStore<TItem> {

  /// <summary>Everything stored so far.</summary>
  IReadOnlyList<TItem> Items { get; }

  /// <summary>Stores <paramref name="item"/>.</summary>
  void Add(TItem item);

  /// <summary>Stores many items at once.</summary>
  void AddRange(TItem[] items);

  /// <summary>Takes one item out, if there is one.</summary>
  bool TryTake(out TItem item);

  /// <summary>Folds every stored item into one value.</summary>
  /// <typeparam name="TResult">What the fold produces.</typeparam>
  TResult Fold<TResult>(Func<TItem, TResult, TResult> folder, TResult seed);
}

// Closes the interface over a concrete type: every inherited signature is spelled `0 on the
// interface and int here.
/// <inheritdoc/>
public sealed class IntStore : IStore<int> {

  /// <inheritdoc/>
  public IReadOnlyList<int> Items => [];

  /// <inheritdoc/>
  public void Add(int item) { }

  /// <inheritdoc/>
  public void AddRange(int[] items) { }

  /// <inheritdoc/>
  public bool TryTake(out int item) {
    item = 0;
    return false;
  }

  /// <inheritdoc/>
  public TResult Fold<TResult>(Func<int, TResult, TResult> folder, TResult seed) => seed;
}

/// <summary>Covers a generic interface whose parameters arrive in the other order.</summary>
/// <typeparam name="TFirst">The first half.</typeparam>
/// <typeparam name="TSecond">The second half.</typeparam>
public interface IPair<TFirst, TSecond> {

  /// <summary>Returns the pair the other way round.</summary>
  IPair<TSecond, TFirst> Swap(TFirst first, TSecond second);
}

// Matching by position alone would look for Swap(TA, TB) and silently find nothing.
/// <inheritdoc/>
public sealed class Flipped<TA, TB> : IPair<TB, TA> {

  /// <inheritdoc/>
  public IPair<TA, TB> Swap(TB first, TA second) => null!;
}

/// <inheritdoc/>
public sealed class StringCache : Cache<string, int> {

  /// <inheritdoc/>
  protected override void OnEvicted(string key) { }
}

/// <summary>Covers the static-class path.</summary>
public static class Helpers {

  /// <summary>Covers an extension method.</summary>
  public static int Doubled(this int value) => value * 2;
}

/// <summary>Covers C# 14 extension-member metadata.</summary>
public static class StaticExtensionFixture {
  extension(int) {
    /// <summary>Gets the bit width of the extended integer type.</summary>
    public static int SupportedBits => 32;
  }
}

/// <summary>Covers the record path.</summary>
/// <param name="Path">Where the thing lives.</param>
public sealed record Location(string Path);

/// <summary>Covers the delegate path.</summary>
public delegate bool Predicate<in T>(T candidate);

// Boundary case: a public type carrying no XML documentation whatsoever. Its summary cells must
// render empty rather than throwing or being omitted.
public sealed class Undocumented {
  public int Value { get; set; }

  public void DoSomething() { }
}

/// <summary>Boundary case: a visible type with no public or protected members at all.</summary>
public sealed class Empty {
  internal int Hidden { get; set; }
}

/// <summary>Covers a type whose members should be excluded entirely.</summary>
public sealed class MostlyHidden {

  /// <summary>The only member that belongs in the reference.</summary>
  public int Visible { get; set; }

  internal int Internal { get; set; }

  private int Private { get; set; }

  [SuppressMessage("Style", "IDE0051", Justification = "fixture")]
  private void Unused() { }
}
