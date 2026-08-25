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
      worky scan [--query <query>] [--limit <posts>]
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
    const string ScanUsage = "Usage: worky scan [--query <query>] [--limit <posts>]";

    string? query = null;
    int limit = 100;
    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--query" or "-q" when i + 1 < args.Length:
                query = args[++i];
                break;
            case "--limit" or "-l" when i + 1 < args.Length && int.TryParse(args[++i], out limit):
                break;
            default:
                Console.WriteLine($"Unknown argument '{args[i]}'.");
                Console.WriteLine(ScanUsage);
                return 2;
        }
    }

    var token = Environment.GetEnvironmentVariable("WORKY_BEARER_TOKEN");
    if (string.IsNullOrWhiteSpace(token))
    {
        Console.WriteLine("Set WORKY_BEARER_TOKEN to your X API bearer token.");
        return 2;
    }

    try
    {
        using var http = new HttpClient { BaseAddress = new Uri("https://api.x.com/2/") };
        var client = new XApiClient(http, new StaticAuthTokenProvider(token));
        var classifier = new JobSignalClassifier();

        Console.WriteLine($"Scanning recent posts for job signals (limit {limit})...");
        var posts = await client.ScanRecentAsync(query ?? DefaultQuery, limit);
        var leads = LeadRanker.Rank(posts.Select(p => new JobLead(p.Post, p.Author, classifier.Classify(p.Post))))
            .Where(l => l.Signal.IsMatch)
            .ToList();

        Console.WriteLine($"Read {posts.Count} posts, {leads.Count} with job signals.");
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

        return 0;
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

    try
    {
        using var http = new HttpClient { BaseAddress = new Uri("https://api.x.com/2/") };
        var api = new XApiClient(
            http,
            new UserAuthTokenProvider(new AuthFileStore(), new XOAuthClient(http, clientId), SystemClock.Instance));
        var service = new GraphSyncService(api, new GraphStateFileStore(), SystemClock.Instance);

        await service.RunAsync(new GraphSyncOptions { MaxPages = maxPages, Refresh = refresh }, Console.WriteLine);
        return 0;
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
