using System.Globalization;
using Worky.Core;
using Worky.Core.Auth;
using Worky.Core.Graph;
using Worky.Cli;

const string DefaultQuery =
    "(hiring OR \"we're hiring\" OR \"job opening\" OR \"open role\" OR \"join our team\") -is:retweet -is:reply lang:en";
const string Usage = """
    Usage:
      worky login
      worky scan [--query <query>] [--limit <posts>] [--targeted] [--interests <terms>] [--max-authors <count>]
      worky sync-graph [--max-pages <pages>] [--refresh-graph]
    """;

if (args.Length == 0)
{
    Console.WriteLine(Usage);
    return 2;
}

return args[0] switch
{
    "login" when args.Length > 1 => UnknownArgument(args[1]),
    "login" => await LoginCommand.RunAsync(),
    "scan" => await RunScanAsync(args[1..]),
    "sync-graph" => await RunSyncGraphAsync(args[1..]),
    _ => UnknownCommand(args[0]),
};

static int UnknownCommand(string name)
{
    Console.WriteLine($"Unknown command '{name}'.");
    Console.WriteLine(Usage);
    return 2;
}

static int UnknownArgument(string name)
{
    Console.WriteLine($"Unknown argument '{name}'.");
    Console.WriteLine(Usage);
    return 2;
}

async Task<int> RunScanAsync(string[] args)
{
    const string ScanUsage =
        "Usage: worky scan [--query <query>] [--limit <posts>] [--targeted] [--interests <terms>] [--max-authors <count>]";

    string? query = null;
    string? interestsRaw = null;
    int limit = 100;
    int maxAuthors = TargetedScanRequest.DefaultMaxAuthors;
    var targeted = false;
    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--query" or "-q" when i + 1 < args.Length:
                query = args[++i];
                break;
            case "--limit" or "-l" when i + 1 < args.Length && int.TryParse(args[++i], out limit):
                break;
            case "--targeted":
                targeted = true;
                break;
            case "--interests" or "-i" when i + 1 < args.Length:
                interestsRaw = args[++i];
                break;
            case "--max-authors" or "-m" when i + 1 < args.Length && int.TryParse(args[++i], out maxAuthors):
                break;
            default:
                Console.WriteLine($"Unknown argument '{args[i]}'.");
                Console.WriteLine(ScanUsage);
                return 2;
        }
    }

    if (targeted && query is not null)
    {
        Console.WriteLine("--query cannot be combined with --targeted.");
        Console.WriteLine(ScanUsage);
        return 2;
    }

    List<string>? interests = null;
    if (interestsRaw is not null)
    {
        interests = interestsRaw.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
        if (interests.Count == 0)
        {
            Console.WriteLine("--interests must list at least one comma-separated term.");
            Console.WriteLine(ScanUsage);
            return 2;
        }
    }

    if (limit < 1)
    {
        Console.WriteLine("--limit must be at least 1.");
        Console.WriteLine(ScanUsage);
        return 2;
    }

    if (maxAuthors < 1)
    {
        Console.WriteLine("--max-authors must be at least 1.");
        Console.WriteLine(ScanUsage);
        return 2;
    }

    if (targeted && interests is not null && !TargetedScanQueryBuilder.TryValidateTerms(interests, out var termsError))
    {
        Console.WriteLine(termsError);
        Console.WriteLine(ScanUsage);
        return 2;
    }

    var token = Environment.GetEnvironmentVariable("WORKY_BEARER_TOKEN");
    if (string.IsNullOrWhiteSpace(token))
    {
        Console.WriteLine("Set WORKY_BEARER_TOKEN to your X API bearer token.");
        return 2;
    }

    var estimate = targeted
        ? CostEstimator.ForTargetedScan(maxAuthors, limit)
        : CostEstimator.ForScan(limit);
    Console.WriteLine($"estimated cost: {FormatUsd(estimate.FloorUsd)}–{FormatUsd(estimate.CeilingUsd)}");

    var classifier = new JobSignalClassifier();
    var snapshot = new GraphStateFileStore().Load();
    var partialPosts = new List<PostWithAuthor>();
    var reads = new ApiReadTracker();

    try
    {
        using var http = new HttpClient { BaseAddress = new Uri("https://api.x.com/2/") };
        var client = new XApiClient(http, new StaticAuthTokenProvider(token), reads);

        IReadOnlyList<PostWithAuthor> posts;
        IReadOnlyList<JobLead> leads;
        if (targeted)
        {
            Console.WriteLine(
                $"Scanning your network for job signals (top {maxAuthors} authors by interest match, limit {limit})...");
            var service = new TargetedScanService(client, new GraphStateFileStore(), SystemClock.Instance);
            var result = await service.RunAsync(new TargetedScanRequest
            {
                MaxAuthors = maxAuthors,
                Terms = interests,
                Limit = limit,
            }, onPartial: page =>
            {
                partialPosts.Clear();
                partialPosts.AddRange(page);
            });

            switch (result)
            {
                case TargetedScanResult.MissingSnapshot:
                    Console.Error.WriteLine(
                        "No follow-graph snapshot found. Run 'worky sync-graph' first to scan your network.");
                    return 2;
                case TargetedScanResult.StaleSnapshot stale:
                    Console.Error.WriteLine(
                        $"Your follow-graph snapshot is {stale.Age.TotalDays:0} days old "
                        + $"(fresh for {GraphState.FreshnessTtl.TotalDays:0}). Run 'worky sync-graph' to refresh it.");
                    return 2;
                case TargetedScanResult.Completed completed:
                    posts = completed.Posts;
                    leads = completed.Leads;
                    break;
                default:
                    return 2;
            }
        }
        else
        {
            Console.WriteLine($"Scanning recent posts for job signals (limit {limit})...");
            posts = await client.ScanRecentAsync(query ?? DefaultQuery, limit, onPage: page =>
            {
                partialPosts.Clear();
                partialPosts.AddRange(page);
            });
            leads = RankLeads(posts, classifier, snapshot);
        }

        Console.WriteLine($"Read {posts.Count} posts, {leads.Count} with job signals.");
        PrintLeads(leads);
        Console.WriteLine(ActualsLine(reads));
        return 0;
    }
    catch (XRateLimitException ex)
    {
        Console.Error.WriteLine(RateLimitMessage(ex));
        if (partialPosts.Count > 0)
        {
            var leadsSoFar = RankLeads(partialPosts, classifier, snapshot);
            Console.WriteLine("Rate limited by the X API; showing partial results.");
            Console.WriteLine($"Read {partialPosts.Count} posts so far, {leadsSoFar.Count} with job signals.");
            PrintLeads(leadsSoFar);
        }
        Console.WriteLine(ActualsLine(reads));
        return 1;
    }
    catch (XApiException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
    catch (RefreshFailedException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
    catch (ArgumentException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 2;
    }
}

async Task<int> RunSyncGraphAsync(string[] args)
{
    const string SyncUsage = "Usage: worky sync-graph [--max-pages <pages>] [--refresh-graph]";

    int maxPages = GraphSyncOptions.DefaultMaxPages;
    var refresh = false;
    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--max-pages" when i + 1 < args.Length && int.TryParse(args[++i], out maxPages):
                break;
            case "--refresh-graph":
                refresh = true;
                break;
            default:
                Console.WriteLine($"Unknown argument '{args[i]}'.");
                Console.WriteLine(SyncUsage);
                return 2;
        }
    }

    if (maxPages < 1)
    {
        Console.WriteLine("--max-pages must be at least 1.");
        Console.WriteLine(SyncUsage);
        return 2;
    }

    var clientId = Environment.GetEnvironmentVariable("WORKY_CLIENT_ID");
    if (string.IsNullOrWhiteSpace(clientId))
    {
        Console.Error.WriteLine(
            "Set WORKY_CLIENT_ID to your X app's OAuth 2.0 public client id (User authentication settings).");
        return 2;
    }

    var estimate = CostEstimator.ForSyncGraph(maxPages);
    Console.WriteLine($"estimated cost: {FormatUsd(estimate.FloorUsd)}–{FormatUsd(estimate.CeilingUsd)}");

    try
    {
        using var http = new HttpClient { BaseAddress = new Uri("https://api.x.com/2/") };
        var reads = new ApiReadTracker();
        var api = new XApiClient(
            http,
            new UserAuthTokenProvider(new AuthFileStore(), new XOAuthClient(http, clientId), SystemClock.Instance),
            reads);
        var service = new GraphSyncService(api, new GraphStateFileStore(), SystemClock.Instance);

        await service.RunAsync(new GraphSyncOptions { MaxPages = maxPages, Refresh = refresh }, Console.WriteLine);
        Console.WriteLine(ActualsLine(reads));
        return 0;
    }
    catch (XRateLimitException ex)
    {
        Console.Error.WriteLine(RateLimitMessage(ex));
        return 1;
    }
    catch (XApiException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
    catch (RefreshFailedException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static IReadOnlyList<JobLead> RankLeads(
    IReadOnlyList<PostWithAuthor> posts, JobSignalClassifier classifier, GraphState? snapshot) =>
    LeadRanker.Rank(NetworkBoost.Apply(
            posts.Select(p => new JobLead(p.Post, p.Author, classifier.Classify(p.Post))).ToList(),
            snapshot))
        .Where(l => l.Signal.IsMatch)
        .ToList();

static void PrintLeads(IReadOnlyList<JobLead> leads)
{
    foreach (var lead in leads)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"@{lead.Author.UserName}  {lead.Signal.Score.ToString("0.0", CultureInfo.InvariantCulture)}  {lead.Permalink}");
        if (lead.Post.CreatedAt is { } at)
            Console.WriteLine(at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        var text = lead.Post.Text.Length > 200 ? lead.Post.Text[..200] + "..." : lead.Post.Text;
        Console.WriteLine("  " + text.ReplaceLineEndings(" "));
        Console.WriteLine("  signals: " + string.Join(", ", lead.Signal.Reasons));
    }
}

static string FormatUsd(decimal value) => "$" + value.ToString("0.00", CultureInfo.InvariantCulture);

static string ActualsLine(ApiReadTracker reads)
{
    var parts = new List<string>();
    if (reads.Posts > 0) parts.Add($"{reads.Posts} posts");
    if (reads.Users > 0) parts.Add($"{reads.Users} users");
    if (parts.Count == 0) parts.Add("0 posts");
    return "actual reads: " + string.Join(", ", parts)
        + $" (~{FormatUsd(reads.EstimatedPostCostUsd + reads.EstimatedUserCostUsd)})";
}

static string RateLimitMessage(XRateLimitException ex) =>
    "Rate limited by the X API on " + ex.Endpoint
    + (ex.ResetAt is { } reset
        ? $"; the window resets at {reset.ToLocalTime():yyyy-MM-dd HH:mm} local time."
        : "; the reset time is unknown.");
