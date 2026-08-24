using System.Globalization;
using System.Net.Http.Headers;
using Worky.Core;

const string DefaultQuery =
    "(hiring OR \"we're hiring\" OR \"job opening\" OR \"open role\" OR \"join our team\") -is:retweet -is:reply lang:en";
const string Usage = "Usage: worky scan [--query <query>] [--limit <posts>]";

if (args.Length == 0 || args[0] != "scan")
{
    if (args.Length > 0) Console.WriteLine($"Unknown command '{args[0]}'.");
    Console.WriteLine(Usage);
    return 2;
}

string? query = null;
int limit = 100;
for (var i = 1; i < args.Length; i++)
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
            Console.WriteLine(Usage);
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
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    var client = new XApiClient(http);
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
