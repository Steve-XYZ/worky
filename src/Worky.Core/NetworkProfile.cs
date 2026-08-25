using System.Text.RegularExpressions;
using Worky.Core.Graph;

namespace Worky.Core;

public sealed record ScoredNetworkAuthor(FollowedUser User, int Score);

public static class NetworkProfile
{
    public static IReadOnlyList<ScoredNetworkAuthor> Build(
        Graph.GraphState state, IEnumerable<string>? seedKeywords = null)
    {
        var keywords = (seedKeywords ?? [])
            .Select(k => k.Trim().ToLowerInvariant())
            .Where(k => k.Length > 0)
            .Distinct()
            .ToList();

        return state.FollowedUsers
            .Select(u => new ScoredNetworkAuthor(u, CountKeywordHits(u, keywords)))
            .OrderByDescending(a => a.Score)
            .ThenBy(a => a.User.UserName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static int CountKeywordHits(Graph.FollowedUser user, IReadOnlyList<string> keywords)
    {
        if (keywords.Count == 0) return 0;
        var text = $"{user.Name} {user.Description}".ToLowerInvariant();
        return keywords.Count(keyword =>
            Regex.IsMatch(text, $@"\b{Regex.Escape(keyword)}\b", RegexOptions.CultureInvariant));
    }
}
