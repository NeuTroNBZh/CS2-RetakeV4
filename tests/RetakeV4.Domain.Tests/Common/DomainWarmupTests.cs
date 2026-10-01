using RetakeV4.Domain.Common;

namespace RetakeV4.Domain.Tests.Common;

public class DomainWarmupTests
{
    [Fact]
    public void Run_ExercisesTheRoundPathWithoutFailing() => DomainWarmup.Run();

    [Fact]
    public void Run_CanBeRepeated()
    {
        DomainWarmup.Run();
        DomainWarmup.Run();
    }
}
