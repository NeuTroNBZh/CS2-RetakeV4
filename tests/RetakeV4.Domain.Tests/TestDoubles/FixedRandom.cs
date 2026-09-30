using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Tests.TestDoubles;

public sealed class FixedRandom(params int[] values) : IRandom
{
    private int _index;

    public int Next(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxExclusive, 1);
        var value = values.Length == 0 ? 0 : values[_index++ % values.Length];
        return Math.Clamp(value, 0, maxExclusive - 1);
    }

    public double NextDouble() => 0.0;
}
