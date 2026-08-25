namespace Worky.Core;

public static class TargetedScanQueryBuilder
{
    public const int MaxQueryChars = 480;
    public const int MaxQueryOperators = 20;

    public static readonly IReadOnlyList<string> DefaultTerms =
    [
        "hiring",
        "we're hiring",
        "job opening",
        "open role",
        "join our team",
    ];

    public static int CountOperators(string query) =>
        query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim('(', ')'))
            .Count(token => token == "OR" || token.Contains(':'));

    public static string BuildQuery(IReadOnlyList<string> userNames, IReadOnlyList<string> terms)
    {
        var authors = string.Join(" OR ", userNames.Select(n => $"from:{n}"));
        return $"{RenderTerms(terms)} ({authors}) -is:retweet lang:en";
    }

    public static IReadOnlyList<string> BuildQueries(IReadOnlyList<string> userNames, IReadOnlyList<string>? terms = null)
    {
        var normalizedTerms = NormalizeTerms(terms ?? DefaultTerms);

        var batches = new List<List<string>>();
        var current = new List<string>();
        foreach (var name in userNames)
        {
            current.Add(name);
            if (FitsWithinBudgets(current, normalizedTerms)) continue;

            current.RemoveAt(current.Count - 1);
            if (current.Count == 0)
                throw new ArgumentException($"Author '{name}' cannot fit in a query within budget.", nameof(userNames));

            batches.Add(current);
            current = [name];
            if (!FitsWithinBudgets(current, normalizedTerms))
                throw new ArgumentException($"Author '{name}' cannot fit in a query within budget.", nameof(userNames));
        }

        if (current.Count > 0) batches.Add(current);
        return batches.Select(b => BuildQuery(b, normalizedTerms)).ToList();
    }

    static bool FitsWithinBudgets(List<string> batch, IReadOnlyList<string> terms)
    {
        var query = BuildQuery(batch, terms);
        return query.Length <= MaxQueryChars && CountOperators(query) <= MaxQueryOperators;
    }

    static string RenderTerms(IReadOnlyList<string> terms) =>
        "(" + string.Join(" OR ", terms.Select(RenderTerm)) + ")";

    static string RenderTerm(string term) =>
        term.Contains(' ') ? $"\"{term}\"" : term;

    static IReadOnlyList<string> NormalizeTerms(IEnumerable<string> terms)
    {
        var prepared = terms
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (prepared.Count == 0)
            throw new ArgumentException("At least one search term is required.", nameof(terms));
        if (prepared.Any(t => t.Contains('"') || t.Contains(':')))
            throw new ArgumentException("Search terms must not contain quotes or ':' operators.", nameof(terms));

        return prepared;
    }
}
