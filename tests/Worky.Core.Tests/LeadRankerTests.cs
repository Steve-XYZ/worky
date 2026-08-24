using Worky.Core;

namespace Worky.Core.Tests;

public class LeadRankerTests
{
    static JobLead Lead(string id, double score, DateTimeOffset? at = null)
    {
        var post = new Post(id, "a", "text", at, []);
        var author = new XUser("a", "alice", "Alice", null);
        return new JobLead(post, author, new JobSignal(score, score >= JobSignalClassifier.MatchThreshold, []));
    }

    [Fact]
    public void DeduplicatesByPostIdKeepingHighestScore()
    {
        var ranked = LeadRanker.Rank([Lead("1", 2), Lead("1", 5)]);

        Assert.Single(ranked);
        Assert.Equal(5, ranked[0].Signal.Score);
    }

    [Fact]
    public void OrdersByScoreThenRecency()
    {
        var now = DateTimeOffset.UtcNow;

        var ranked = LeadRanker.Rank([
            Lead("1", 1, now),
            Lead("2", 3, now.AddHours(-5)),
            Lead("3", 3, now),
        ]);

        Assert.Equal(["3", "2", "1"], ranked.Select(l => l.Post.Id).ToArray());
    }
}
