namespace RetakeV4.Domain.Common;

public static class RandomExtensions
{
    public static IReadOnlyList<T> Shuffle<T>(this IRandom random, IEnumerable<T> items)
    {
        var copy = items.ToArray();
        for (var i = copy.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }
        return copy;
    }

    public static T Pick<T>(this IRandom random, IReadOnlyList<T> items)
    {
        if (items.Count == 0)
        {
            throw new ArgumentException("Cannot pick from an empty list", nameof(items));
        }
        return items[random.Next(items.Count)];
    }
}
