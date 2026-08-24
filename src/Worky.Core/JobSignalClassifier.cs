namespace Worky.Core;

public sealed class JobSignalClassifier
{
    public const double MatchThreshold = 1.5;

    public const double StrongPhraseWeight = 2.0;
    const double WeakPhraseWeight = 0.75;
    const double AtsLinkWeight = 3.0;

    static readonly string[] StrongPhrases =
    [
        "we're hiring", "we are hiring", "i'm hiring", "i am hiring",
        "we're looking for", "we are looking for", "now hiring",
        "job opening", "open role", "open roles", "roles open",
        "join our team", "join my team", "come work with us",
        "we're growing the team", "position open", "positions open",
    ];

    static readonly string[] WeakPhrases =
    [
        "hiring", "recruiting", "looking for a", "apply now", "dm me",
    ];

    static readonly string[] AtsHosts =
    [
        "boards.greenhouse.io", "job-boards.greenhouse.io", "jobs.lever.co",
        "jobs.eu.lever.co", "jobs.ashbyhq.com", "apply.workable.com",
        "jobs.smartrecruiters.com", "jobs.teamtailor.com", "app.recruitee.com",
        "myworkdayjobs.com",
    ];

    public JobSignal Classify(Post post)
    {
        var reasons = new List<string>();
        double score = 0;

        foreach (var phrase in StrongPhrases)
        {
            if (!post.Text.Contains(phrase, StringComparison.OrdinalIgnoreCase)) continue;
            score += StrongPhraseWeight;
            reasons.Add($"phrase \"{phrase}\"");
        }

        foreach (var phrase in WeakPhrases)
        {
            if (!post.Text.Contains(phrase, StringComparison.OrdinalIgnoreCase)) continue;
            score += WeakPhraseWeight;
            reasons.Add($"mention \"{phrase}\"");
        }

        foreach (var host in AtsHosts)
        {
            if (!post.Text.Contains(host, StringComparison.OrdinalIgnoreCase)) continue;
            score += AtsLinkWeight;
            reasons.Add($"ats link {host}");
        }

        return new JobSignal(score, score >= MatchThreshold, reasons);
    }
}
