using RetakeV4.Domain.Hud;

namespace RetakeV4.Domain.Tests.Hud;

public class KeyMuteTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MutedSlot_IgnoresKeysUntilTheDelayIsOver()
    {
        var mute = KeyMute.Empty.Mute(3, Now, TimeSpan.FromSeconds(1));
        Assert.True(mute.IsMuted(3, Now.AddMilliseconds(999)));
        Assert.False(mute.IsMuted(3, Now.AddSeconds(1)));
        Assert.False(mute.IsMuted(4, Now));
    }

    [Fact]
    public void Forget_ClearsASlot()
    {
        var mute = KeyMute.Empty.Mute(3, Now, TimeSpan.FromSeconds(1)).Forget(3);
        Assert.False(mute.IsMuted(3, Now));
    }
}
