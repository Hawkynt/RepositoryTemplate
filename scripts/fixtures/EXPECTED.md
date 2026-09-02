### Namespace `Fixture.Package`

[`AliasCodec`](#aliascodec) · [`BitOrder`](#bitorder) · [`BitWriter`](#bitwriter) · [`BytewiseProbe`](#bytewiseprobe) · [`Cache<TKey, TValue>`](#cachetkey-tvalue) · [`Cache<TKey, TValue>.Entry`](#cachetkey-tvalueentry) · [`Codec`](#codec) · [`Empty`](#empty) · [`Flipped<TA, TB>`](#flippedta-tb) · [`Fraction`](#fraction) · [`Helpers`](#helpers) · [`INamed`](#inamed) · [`IPair<TFirst, TSecond>`](#ipairtfirst-tsecond) · [`IProbe`](#iprobe) · [`IStore<TItem>`](#istoretitem) · [`IntStore`](#intstore) · [`Location`](#location) · [`MostlyHidden`](#mostlyhidden) · [`NamedThing`](#namedthing) · [`NullCodec`](#nullcodec) · [`Predicate<T>`](#predicatet) · [`ProbeOutcome`](#probeoutcome) · [`StaticExtensionFixture`](#staticextensionfixture) · [`StringCache`](#stringcache) · [`Undocumented`](#undocumented)

#### `AliasCodec`

Covers documentation inherited from an abstract base class.

Inherits `NullCodec`.

| Member | Signature | Summary |
| --- | --- | --- |
| `AliasCodec` | `AliasCodec()` |  |
| `Name` | `override string Name { get; }` | The name this codec is registered under. |

#### `BitOrder`

A bit-order selector, to cover the enum rendering path.

| Value | Numeric | Summary |
| --- | --- | --- |
| `MsbFirst` | `0` | Most significant bit first. |
| `LsbFirst` | `1` | Least significant bit first. |

#### `BitWriter`

Writes individual bits to a buffer.

| Member | Signature | Summary |
| --- | --- | --- |
| `BitWriter` | `BitWriter(BitOrder order)` | Creates a writer using the given bit order. |
| `MaxBitsPerCall` | `const int MaxBitsPerCall` | The largest number of bits `WriteBits` accepts at once. |
| `Order` | `BitOrder Order { get; }` | The configured bit order. |
| `Written` | `long Written { get; init; }` | How many bits have been written so far. Covers the init-only accessor path. |
| `Flush` | `void Flush(bool padWithOnes = false)` | Pads to a byte boundary and flushes. Covers the optional-parameter path. |
| `Render` | `byte[] Render(int[,] matrix)` | Covers an array-returning method and a multidimensional parameter. |
| `TryPack` | `bool TryPack(ref int state, out byte packed, in long seed)` | Covers ref, out and in parameters in one signature. |
| `WriteAll` | `void WriteAll(params int[] values)` | Covers the params array path. |
| `WriteBits` | `void WriteBits(int value, int count)` | Writes the `count` low bits of `value`. |
| `Flushed` | `event EventHandler Flushed` | Raised once the internal buffer is flushed. |

```csharp
var writer = new BitWriter(BitOrder.MsbFirst);
writer.WriteBits(0b1011_0010, count: 8);
writer.Flush();
```

#### `BytewiseProbe`

Covers documentation inherited from an interface.

Implements `IProbe`.

| Member | Signature | Summary |
| --- | --- | --- |
| `BytewiseProbe` | `BytewiseProbe()` |  |
| `Matched` | `bool Matched { get; }` | Whether the last probe matched. |
| `Probe` | `int Probe(byte[] data)` | Probes `data` and reports how many bytes were consumed. |

#### `Cache<TKey, TValue>`

A generic cache, covering generic type parameters and nested types.

| Member | Signature | Summary |
| --- | --- | --- |
| `Cache` | `Cache()` |  |
| `MaxEntries` | `protected readonly int MaxEntries` | Covers a protected field. |
| `Item` | `TValue this[TKey key] { get; set; }` | Indexer, which the doc-comment grammar spells as an `Item` property. |
| `Map` | `TResult Map<TResult>(Func<TValue, TResult> projection)` | Covers a generic method on a generic type. |
| `OnEvicted` | `protected virtual void OnEvicted(TKey key)` | Covers the protected-member path — protected members are part of the public contract. |
| `Set` | `void Set(TKey key, TValue value)` | Stores a value. |

```csharp
var cache = new Cache<string, int>();
cache.Set("answer", 42);
```

#### `Cache<TKey, TValue>.Entry`

One cached entry. Covers the nested-public-type path.

Implements `IEquatable<Entry<TKey, TValue>>`.

| Member | Signature | Summary |
| --- | --- | --- |
| `Entry` | `Entry(TKey Key, TValue Value)` | One cached entry. Covers the nested-public-type path. |
| `Key` | `TKey Key { get; init; }` |  |
| `Value` | `TValue Value { get; init; }` |  |

#### `Codec`

Covers documentation inherited from an abstract base class.

| Member | Signature | Summary |
| --- | --- | --- |
| `Codec` | `protected Codec()` |  |
| `Name` | `abstract string Name { get; }` | The name this codec is registered under. |
| `Encode` | `abstract int Encode(byte[] input, int offset)` | Encodes `input` and reports how many bytes were written. |
| `OnFinished` | `protected virtual void OnFinished()` | Called once the codec has finished, whatever the outcome. |

#### `Empty`

Boundary case: a visible type with no public or protected members at all.

| Member | Signature | Summary |
| --- | --- | --- |
| `Empty` | `Empty()` |  |

#### `Flipped<TA, TB>`

Covers a generic interface whose parameters arrive in the other order.

Implements `IPair<TB, TA>`.

| Member | Signature | Summary |
| --- | --- | --- |
| `Flipped` | `Flipped()` |  |
| `Swap` | `IPair<TA, TB> Swap(TB first, TA second)` | Returns the pair the other way round. |

#### `Fraction`

Covers operator overloads and conversions.

| Member | Signature | Summary |
| --- | --- | --- |
| `Fraction` | `Fraction(int numerator, int denominator)` | Creates a fraction. |
| `Denominator` | `int Denominator { get; }` | The denominator. |
| `Numerator` | `int Numerator { get; }` | The numerator. |
| `explicit operator Fraction` | `static explicit operator Fraction(int value)` | Converts from an int explicitly. |
| `implicit operator double` | `static implicit operator double(Fraction value)` | Converts to a double implicitly. |
| `operator +` | `static Fraction operator +(Fraction left, Fraction right)` | Adds two fractions. |

#### `Helpers`

Covers the static-class path.

| Member | Signature | Summary |
| --- | --- | --- |
| `Doubled` | `static int Doubled(this int value)` | Covers an extension method. |

#### `INamed`

Covers the interface rendering path.

| Member | Signature | Summary |
| --- | --- | --- |
| `Name` | `string Name { get; }` | The display name. |
| `Rename` | `void Rename(string name)` | Renames the instance. |

#### `IPair<TFirst, TSecond>`

Covers a generic interface whose parameters arrive in the other order.

| Member | Signature | Summary |
| --- | --- | --- |
| `Swap` | `IPair<TSecond, TFirst> Swap(TFirst first, TSecond second)` | Returns the pair the other way round. |

#### `IProbe`

Covers documentation inherited from an interface.

| Member | Signature | Summary |
| --- | --- | --- |
| `Matched` | `bool Matched { get; }` | Whether the last probe matched. |
| `Probe` | `int Probe(byte[] data)` | Probes `data` and reports how many bytes were consumed. |

#### `IStore<TItem>`

Covers documentation inherited across a generic interface.

| Member | Signature | Summary |
| --- | --- | --- |
| `Items` | `IReadOnlyList<TItem> Items { get; }` | Everything stored so far. |
| `AddRange` | `void AddRange(TItem[] items)` | Stores many items at once. |
| `Add` | `void Add(TItem item)` | Stores `item`. |
| `Fold` | `TResult Fold<TResult>(Func<TItem, TResult, TResult> folder, TResult seed)` | Folds every stored item into one value. |
| `TryTake` | `bool TryTake(out TItem item)` | Takes one item out, if there is one. |

#### `IntStore`

Covers documentation inherited across a generic interface.

Implements `IStore<int>`.

| Member | Signature | Summary |
| --- | --- | --- |
| `IntStore` | `IntStore()` |  |
| `Items` | `IReadOnlyList<int> Items { get; }` | Everything stored so far. |
| `AddRange` | `void AddRange(int[] items)` | Stores many items at once. |
| `Add` | `void Add(int item)` | Stores `item`. |
| `Fold` | `TResult Fold<TResult>(Func<int, TResult, TResult> folder, TResult seed)` | Folds every stored item into one value. |
| `TryTake` | `bool TryTake(out int item)` | Takes one item out, if there is one. |

#### `Location`

Covers the record path.

Implements `IEquatable<Location>`.

| Member | Signature | Summary |
| --- | --- | --- |
| `Location` | `Location(string Path)` | Covers the record path. |
| `Path` | `string Path { get; init; }` | Where the thing lives. |

#### `MostlyHidden`

Covers a type whose members should be excluded entirely.

| Member | Signature | Summary |
| --- | --- | --- |
| `MostlyHidden` | `MostlyHidden()` |  |
| `Visible` | `int Visible { get; set; }` | The only member that belongs in the reference. |

#### `NamedThing`

Covers explicit interface implementation and base-type reporting.

Implements `INamed`.

| Member | Signature | Summary |
| --- | --- | --- |
| `NamedThing` | `NamedThing()` |  |
| `Name` | `string Name { get; }` | The display name. |

#### `NullCodec`

Covers documentation inherited from an abstract base class.

Inherits `Codec`.

| Member | Signature | Summary |
| --- | --- | --- |
| `NullCodec` | `NullCodec()` |  |
| `Name` | `override string Name { get; }` | The name this codec is registered under. |
| `Encode` | `override int Encode(byte[] input, int offset)` | Encodes `input` and reports how many bytes were written. |
| `OnFinished` | `protected override void OnFinished()` | Does nothing at all, and says so in its own words. |

#### `Predicate<T>`

Covers the delegate path.

| Member | Signature | Summary |
| --- | --- | --- |
| `Predicate` | `bool Predicate<T>(T candidate)` | Covers the delegate path. |

#### `ProbeOutcome`

Covers bare inheritdoc on the one member kind that has nothing to inherit from.

| Value | Numeric | Summary |
| --- | --- | --- |
| `Hit` | `0` | The probe found what it was looking for. |
| `Miss` | `1` | The probe found nothing. Stated here, so the inheritdoc below must not overwrite it. |
| `Inconclusive` | `2` |  |

#### `StaticExtensionFixture`

Covers C# 14 extension-member metadata.

| Member | Signature | Summary |
| --- | --- | --- |
| `get_SupportedBits` | `static int get_SupportedBits()` | Gets the bit width of the extended integer type. |

#### `StringCache`

A generic cache, covering generic type parameters and nested types.

Inherits `Cache<string, int>`.

| Member | Signature | Summary |
| --- | --- | --- |
| `StringCache` | `StringCache()` |  |
| `OnEvicted` | `protected override void OnEvicted(string key)` | Covers the protected-member path — protected members are part of the public contract. |

```csharp
var cache = new Cache<string, int>();
cache.Set("answer", 42);
```

#### `Undocumented`

| Member | Signature | Summary |
| --- | --- | --- |
| `Undocumented` | `Undocumented()` |  |
| `Value` | `int Value { get; set; }` |  |
| `DoSomething` | `void DoSomething()` |  |