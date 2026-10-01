using RetakeV4.Contracts;
using RetakeV4.Domain.Events;
using RetakeV4.Modules.Api;

namespace RetakeV4.Integration.Tests.Modules.Api;

public class RetakeApiHostTests
{
    private static RetakeApiService Service() => new(new EventBus(_ => { }), new ListLogger(), _ => null, _ => 0UL);

    [Fact]
    public void Capability_FollowsThePublishedInstance_AcrossReloads()
    {
        RetakeApiHost.Publish(null);
        Assert.Null(RetakeApi.Capability.Get());
        var first = Service();
        RetakeApiHost.Publish(first);
        Assert.Same(first, RetakeApi.Capability.Get());
        RetakeApiHost.Publish(null);
        var reloaded = Service();
        RetakeApiHost.Publish(reloaded);
        Assert.Same(reloaded, RetakeApi.Capability.Get());
        RetakeApiHost.Publish(null);
        Assert.Null(RetakeApi.Capability.Get());
    }
}
