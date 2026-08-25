namespace Worky.Core;

public sealed record TargetedScanRequest
{
    public const int DefaultMaxAuthors = 100;
    public const int DefaultLimit = 100;

    public int MaxAuthors { get; init; } = DefaultMaxAuthors;
    public IReadOnlyList<string>? Terms { get; init; }
    public int Limit { get; init; } = DefaultLimit;
}

public abstract record TargetedScanResult
{
    public sealed record MissingSnapshot : TargetedScanResult;

    public sealed record StaleSnapshot(TimeSpan Age) : TargetedScanResult;

    public sealed record Completed(
        int Batches,
        IReadOnlyList<PostWithAuthor> Posts,
        IReadOnlyList<JobLead> Leads) : TargetedScanResult;
}

public sealed class TargetedScanService(
    XApiClient api,
    Graph.GraphStateFileStore store,
    IClock clock)
{
    public async Task<TargetedScanResult> RunAsync(TargetedScanRequest request, CancellationToken ct = default)
    {
        var state = store.Load();
        if (state is null) return new TargetedScanResult.MissingSnapshot();

        var age = clock.UtcNow - state.IngestedAt;
        if (state.IsStale(clock.UtcNow)) return new TargetedScanResult.StaleSnapshot(age);

        var terms = request.Terms ?? TargetedScanQueryBuilder.DefaultTerms;
        var authors = NetworkProfile.Build(state, terms)
            .Take(Math.Max(request.MaxAuthors, 0))
            .Select(a => a.User.UserName)
            .ToList();

        if (authors.Count == 0)
            return new TargetedScanResult.Completed(0, [], []);

        var queries = TargetedScanQueryBuilder.BuildQueries(authors, terms);
        var collected = new List<PostWithAuthor>();
        foreach (var query in queries)
        {
            if (collected.Count >= request.Limit) break;
            var page = await api.SearchRecentAsync(query, Math.Clamp(request.Limit - collected.Count, 10, 100), ct: ct);
            collected.AddRange(page.Items);
        }
        var posts = collected.Take(request.Limit).ToList();

        var classifier = new JobSignalClassifier();
        var leads = LeadRanker.Rank(NetworkBoost.Apply(
                posts.Select(p => new JobLead(p.Post, p.Author, classifier.Classify(p.Post))).ToList(),
                state))
            .Where(l => l.Signal.IsMatch)
            .ToList();

        return new TargetedScanResult.Completed(queries.Count, posts, leads);
    }
}
