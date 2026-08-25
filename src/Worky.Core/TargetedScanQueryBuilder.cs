namespace Worky.Core;

public static class TargetedScanQueryBuilder
{
    public const int MaxQueryChars = 480;
    public const int MaxQueryOperators = 20;
    public const int MaxAuthorHandleLength = 15;

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

    public static bool TryValidateTerms(IEnumerable<string> terms, out string? error)
    {
        var prepared = terms
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (prepared.Count == 0)
        {
            error = "At least one search term is required.";
            return false;
        }

        var offender = prepared.FirstOrDefault(t => t.Contains('"') || t.Contains(':'));
        if (offender is not null)
        {
            error = $"Search term '{offender}' must not contain quotes or ':' operators.";
            return false;
        }

        if (!FitsWithinBudgets([new string('a', MaxAuthorHandleLength)], prepared))
        {
            error = $"Combined search terms exceed the {MaxQueryChars}-character "
                + $"or {MaxQueryOperators}-operator query budget.";
            return false;
        }

        error = null;
        return true;
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
        var offender = prepared.FirstOrDefault(t => t.Contains('"') || t.Contains(':'));
        if (offender is not null)
            throw new ArgumentException(
                $"Search term '{offender}' must not contain quotes or ':' operators.", nameof(terms));

        return prepared;
    }
}
