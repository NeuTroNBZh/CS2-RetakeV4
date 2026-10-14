using RetakeV4.Domain.Common;
using RetakeV4.Domain.Stats;
using static RetakeV4.Domain.Tests.Stats.StatsTestData;

namespace RetakeV4.Domain.Tests.Stats;

// Special kills follow the rules of Kills: only the first death row of a participant, killed by an enemy participant.
public class RoundAnalyzerSpecialKillsTests
{
    private static RoundSummary Analyze(RoundRecord record) => RoundAnalyzer.Analyze(record, Rules);

    private static PlayerKilled Special(ulong? attacker, ulong victim, int timeMs, int penetrated = 0, bool noScope = false, bool smoke = false,
        bool blind = false, bool air = false, bool victimIsBot = false) =>
        new(attacker, victim, victimIsBot, null, false, "awp", false, penetrated, noScope, smoke, blind, air, 10f, null, null, timeMs);

    private static PlayerKilled Every(ulong? attacker, ulong victim, int timeMs, bool victimIsBot = false) =>
        Special(attacker, victim, timeMs, penetrated: 2, noScope: true, smoke: true, blind: true, air: true, victimIsBot: victimIsBot);

    // noscope, wallbang, smoke, blind, air
    private static int[] Specials(PlayerRoundStats p) => new[] { p.NoscopeKills, p.WallbangKills, p.SmokeKills, p.BlindKills, p.AirKills };

    [Fact]
    public void EachFlag_CountsOnTheKillersOwnCounter()
    {
        var context = Context(P(Ct1, TeamSide.CT), P(Ct2, TeamSide.CT), P(Ct3, TeamSide.CT), P(T1, TeamSide.T), P(T2, TeamSide.T), P(T3, TeamSide.T));
        var s = Analyze(Record(context, TeamSide.CT, r =>
        {
            r.Add(Special(T3, Ct3, 500, blind: true, air: true));
            r.Add(Special(Ct1, T1, 1000, noScope: true));
            r.Add(Every(Ct2, T1, 1100));                      // duplicated death event of T1: ignored
            r.Add(Special(Ct1, T2, 2000, penetrated: 3));     // three walls: one wallbang kill
            r.Add(Special(Ct2, T3, 3000, smoke: true));
        }));
        Assert.Equal(2, Of(s, Ct1).Kills);
        Assert.Equal(new[] { 1, 1, 0, 0, 0 }, Specials(Of(s, Ct1)));
        Assert.Equal(1, Of(s, Ct2).Kills);
        Assert.Equal(new[] { 0, 0, 1, 0, 0 }, Specials(Of(s, Ct2)));
        Assert.Equal(new[] { 0, 0, 0, 1, 1 }, Specials(Of(s, T3)));
        Assert.Equal(new int[5], Specials(Of(s, Ct3)));
    }

    // Every flag is set on each row: none of them is a kill, so no counter moves.
    [Fact]
    public void RowsThatAreNotKills_CountNothing()
    {
        var s = Analyze(Record(Context(P(Ct1, TeamSide.CT), P(Ct2, TeamSide.CT), P(T1, TeamSide.T), P(T2, TeamSide.T)), TeamSide.T, r =>
        {
            r.Add(Every(Ct1, 0, 500, victimIsBot: true));   // bot victim
            r.Add(Every(Ct1, Ct2, 1000));                    // team kill
            r.Add(Every(T2, T2, 1500));                      // suicide
            r.Add(Every(null, T1, 2000));                    // world
            r.Add(Every(Ct1, T1, 2100));                     // duplicated death event of T1 (first: the world)
            r.Add(Every(Ct1, 98, 2200));                     // human victim who is not a participant
            r.Add(Every(99, Ct1, 2500));                     // killer who is not a participant
        }));
        foreach (var id in new[] { Ct1, Ct2, T1, T2 })
        {
            Assert.Equal(0, Of(s, id).Kills);
            Assert.Equal(new int[5], Specials(Of(s, id)));
        }
        Assert.Equal(1, Of(s, Ct1).TeamKills);
    }
}
