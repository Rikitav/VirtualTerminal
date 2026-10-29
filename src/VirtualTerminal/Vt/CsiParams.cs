namespace VirtualTerminal.Vt;

/// <summary>
/// A sub-parameter-aware view over the parameters of a CSI/DCS sequence. Values are stored
/// flat; <see cref="IsSubParam"/> reports whether the value at an index was introduced by a
/// <c>':'</c> separator (a sub-parameter of the preceding <c>';'</c>-group) rather than by
/// <c>';'</c>. This is what distinguishes e.g. <c>4:3</c> (curly underline) from <c>4;3</c>
/// (single underline + italic), and <c>38:2:...</c> from <c>38;2;...</c>.
/// </summary>
public readonly struct CsiParams
{
    private readonly int[] _values;
    private readonly bool[] _sub;
    private readonly int _count;

    internal CsiParams(int[] values, bool[] sub, int count)
    {
        _values = values;
        _sub = sub;
        _count = count;
    }

    /// <summary>Number of stored values (top-level + sub).</summary>
    public int Length => _count;

    /// <summary>Raw value at <paramref name="index"/> (no bounds check beyond count).</summary>
    public int this[int index] => index >= 0 && index < _count ? _values[index] : 0;

    /// <summary>Value at <paramref name="index"/>, or <paramref name="defaultValue"/> when absent.</summary>
    public int At(int index, int defaultValue) =>
        index >= 0 && index < _count ? _values[index] : defaultValue;

    /// <summary>Whether the value at <paramref name="index"/> is a <c>':'</c> sub-parameter.</summary>
    public bool IsSubParam(int index) => index >= 0 && index < _count && _sub[index];

    /// <summary>True when no parameters were supplied (e.g. bare <c>CSI m</c>).</summary>
    public bool IsEmpty => _count == 0;

    /// <summary>Enumerates (index, value, isSubParam).</summary>
    public Enumerator GetEnumerator() => new(this);

    /// <summary>Supports iterating over the parameters of a <see cref="CsiParams"/> value.</summary>
    public ref struct Enumerator
    {
        private readonly CsiParams _p;
        private int _i;

        internal Enumerator(CsiParams p) { _p = p; _i = -1; }

        /// <summary>Advances the enumerator to the next parameter.</summary>
        /// <returns>true if another parameter is available; otherwise false.</returns>
        public bool MoveNext() { _i++; return _i < _p._count; }

        /// <summary>Gets the current parameter index, value, and sub-parameter flag.</summary>
        public readonly (int Index, int Value, bool IsSubParam) Current => (_i, _p._values[_i], _p._sub[_i]);
    }
}
