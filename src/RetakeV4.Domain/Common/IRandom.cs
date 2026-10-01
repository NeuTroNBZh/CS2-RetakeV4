namespace RetakeV4.Domain.Common;

public interface IRandom
{
    int Next(int maxExclusive);

    double NextDouble();
}

public sealed class SystemRandom : IRandom
{
    private readonly Random _random;

    public SystemRandom(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        _random = random;
    }

    public static SystemRandom Shared { get; } = new(Random.Shared);

    public int Next(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxExclusive, 1);
        return _random.Next(maxExclusive);
    }

    public double NextDouble() => _random.NextDouble();
}
