using RetakeV4.Domain;

namespace RetakeV4.Domain.Tests;

public class DomainInfoTests
{
    [Fact]
    public void AssemblyName_MatchesDeclaredName()
    {
        Assert.Equal(DomainInfo.AssemblyName, typeof(DomainInfo).Assembly.GetName().Name);
    }
}
