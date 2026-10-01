using RetakeV4.Domain.Common;
using RetakeV4.Domain.Tests.TestDoubles;

namespace RetakeV4.Domain.Tests.Common;

public class RandomExtensionsTests
{
    [Fact]
    public void Shuffle_ReturnsPermutation_WithoutMutatingInput()
    {
        var input = new List<int> { 1, 2, 3, 4, 5 };
        var shuffled = new SystemRandom(new Random(7)).Shuffle(input);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, input);
        Assert.Equal(input.Order(), shuffled.Order());
    }

    [Fact]
    public void Shuffle_IsDeterministicForAGivenSeed()
    {
        var first = new SystemRandom(new Random(42)).Shuffle(Enumerable.Range(0, 20));
        var second = new SystemRandom(new Random(42)).Shuffle(Enumerable.Range(0, 20));
        Assert.Equal(first, second);
    }

    [Fact]
    public void Shuffle_WithFixedZeros_RotatesDeterministically()
    {
        Assert.Equal(new[] { 2, 3, 1 }, new FixedRandom(0).Shuffle(new[] { 1, 2, 3 }));
    }

    [Fact]
    public void Shuffle_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(new FixedRandom().Shuffle(Array.Empty<int>()));
    }

    [Fact]
    public void Pick_UsesRandomIndex()
    {
        Assert.Equal("b", new FixedRandom(1).Pick(new[] { "a", "b", "c" }));
    }

    [Fact]
    public void Pick_EmptyList_Throws()
    {
        Assert.Throws<ArgumentException>(() => new FixedRandom().Pick(Array.Empty<string>()));
    }

    [Fact]
    public void SystemRandom_Next_RejectsNonPositiveBound()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SystemRandom.Shared.Next(0));
    }

    [Fact]
    public void SystemRandom_StaysInBounds()
    {
        var random = new SystemRandom(new Random(1));
        Assert.All(Enumerable.Range(0, 200), _ => Assert.InRange(random.Next(3), 0, 2));
        Assert.InRange(random.NextDouble(), 0.0, 1.0);
    }
}
