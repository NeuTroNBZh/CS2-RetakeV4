namespace RetakeV4.Domain.MapVote;

public enum MapVoteRequestRefusal
{
    AlreadyOpen,
    AlreadyDecided,
    NotEnoughMaps,
}

public static class MapVoteRequestCheck
{
    public static MapVoteRequestRefusal? Check(bool voteOpen, bool decided, int maps) =>
        voteOpen ? MapVoteRequestRefusal.AlreadyOpen
        : decided ? MapVoteRequestRefusal.AlreadyDecided
        : maps < MapPool.MinimumMaps ? MapVoteRequestRefusal.NotEnoughMaps
        : null;
}
