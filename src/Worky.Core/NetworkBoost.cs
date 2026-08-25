namespace Worky.Core;

public static class NetworkBoost
{
    public const double ScoreBonus = 1.0;
    public const string Reason = "network match";

    public static IReadOnlyList<JobLead> Apply(IReadOnlyList<JobLead> leads, Graph.GraphState? state)
    {
        if (state is null) return leads;

        var followedIds = state.FollowedUsers.Select(u => u.Id).ToHashSet();
        return leads
            .Select(lead => followedIds.Contains(lead.Post.AuthorId) ? Boosted(lead) : lead)
            .ToList();
    }

    static JobLead Boosted(JobLead lead)
    {
        var score = lead.Signal.Score + ScoreBonus;
        return lead with
        {
            Signal = new JobSignal(
                score,
                score >= JobSignalClassifier.MatchThreshold,
                [.. lead.Signal.Reasons, Reason]),
        };
    }
}
