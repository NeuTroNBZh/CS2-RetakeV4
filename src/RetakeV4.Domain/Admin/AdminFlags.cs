namespace RetakeV4.Domain.Admin;

// Any one flag of a list is enough. @css/root is included because CounterStrikeSharp root only covers the css domain.
public static class AdminFlags
{
    public static IReadOnlyList<string> Admin { get; } = new[] { "@retakev4/admin", "@retakev4/root", "@css/root" };

    public static IReadOnlyList<string> Root { get; } = new[] { "@retakev4/root", "@css/root" };
}
