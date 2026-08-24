namespace Worky.Core;

public static class LeadRanker
{
    public static IReadOnlyList<JobLead> Rank(IEnumerable<JobLead> leads) =>
    [
        .. leads
            .GroupBy(l => l.Post.Id)
            .Select(g => g.OrderByDescending(l => l.Signal.Score).First())
            .OrderByDescending(l => l.Signal.Score)
            .ThenByDescending(l => l.Post.CreatedAt ?? DateTimeOffset.MinValue),
    ];
}
