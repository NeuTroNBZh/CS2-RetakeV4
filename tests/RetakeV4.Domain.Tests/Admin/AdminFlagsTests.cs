using RetakeV4.Domain.Admin;

namespace RetakeV4.Domain.Tests.Admin;

public class AdminFlagsTests
{
    // Server owners usually only have @css/root; it must open the Retake admin tools without extra flags.
    [Theory]
    [InlineData("@retakev4/admin")]
    [InlineData("@retakev4/root")]
    [InlineData("@css/root")]
    public void AdminAccess_AcceptsTheseFlags(string flag) => Assert.Contains(flag, AdminFlags.Admin);

    [Theory]
    [InlineData("@retakev4/root")]
    [InlineData("@css/root")]
    public void RootAccess_AcceptsTheseFlags(string flag) => Assert.Contains(flag, AdminFlags.Root);

    [Fact]
    public void RootAccess_DoesNotAcceptPlainAdmin() => Assert.DoesNotContain("@retakev4/admin", AdminFlags.Root);
}
