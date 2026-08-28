### Namespace `Fixture.Package`

[`BitOrder`](#bitorder) · [`BitWriter`](#bitwriter) · [`Cache<TKey, TValue>`](#cachetkey-tvalue) · [`Cache<TKey, TValue>.Entry`](#cachetkey-tvalueentry) · [`Empty`](#empty) · [`Fraction`](#fraction) · [`Helpers`](#helpers) · [`INamed`](#inamed) · [`Location`](#location) · [`MostlyHidden`](#mostlyhidden) · [`NamedThing`](#namedthing) · [`Predicate<T>`](#predicatet) · [`Undocumented`](#undocumented)

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

#### `Empty`

Boundary case: a visible type with no public or protected members at all.

| Member | Signature | Summary |
| --- | --- | --- |
| `Empty` | `Empty()` |  |

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

#### `Predicate<T>`

Covers the delegate path.

| Member | Signature | Summary |
| --- | --- | --- |
| `Predicate` | `bool Predicate<T>(T candidate)` | Covers the delegate path. |

#### `Undocumented`

| Member | Signature | Summary |
| --- | --- | --- |
| `Undocumented` | `Undocumented()` |  |
| `Value` | `int Value { get; set; }` |  |
| `DoSomething` | `void DoSomething()` |  |