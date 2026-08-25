using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Worky.Core.Auth;

namespace Worky.Core;

public sealed record PostWithAuthor(Post Post, XUser Author);

public sealed record SearchPage(IReadOnlyList<PostWithAuthor> Items, string? NextToken);

public sealed class XApiException(int statusCode, string body)
    : Exception($"X API returned {statusCode}: {body}")
{
    public int StatusCode { get; } = statusCode;
}

public sealed class XApiClient(HttpClient http, IAuthTokenProvider authToken)
{
    const string TweetFields = "created_at,author_id,entities";
    const string UserFields = "username,name,description";

    public async Task<XUser> GetMeAsync(CancellationToken ct = default)
    {
        using var response = await GetAsync("users/me", ct);
        await EnsureSuccessAsync(response, ct);

        var payload = await response.Content.ReadFromJsonAsync<MeResponseDto>(ApiJson.Options, ct);
        var user = payload?.Data ?? throw new XApiException((int)response.StatusCode, "empty users/me response");
        return new XUser(user.Id, user.Username, user.Name, user.Description);
    }

    public async Task<IReadOnlyList<PostWithAuthor>> ScanRecentAsync(
        string query, int maxPosts, CancellationToken ct = default)
    {
        var results = new List<PostWithAuthor>();
        string? next = null;
        do
        {
            var remaining = maxPosts - results.Count;
            var page = await SearchRecentAsync(query, Math.Clamp(remaining, 10, 100), next, ct);
            results.AddRange(page.Items);
            next = page.NextToken;
        }
        while (next is not null && results.Count < maxPosts);

        return results.Take(maxPosts).ToList();
    }

    public async Task<SearchPage> SearchRecentAsync(
        string query, int maxResults = 25, string? nextToken = null, CancellationToken ct = default)
    {
        var qs = new List<string>
        {
            $"query={Uri.EscapeDataString(query)}",
            $"max_results={Math.Clamp(maxResults, 10, 100)}",
            $"tweet.fields={TweetFields}",
            "expansions=author_id",
            $"user.fields={UserFields}",
        };
        if (nextToken is not null) qs.Add($"next_token={Uri.EscapeDataString(nextToken)}");

        using var response = await GetAsync($"tweets/search/recent?{string.Join("&", qs)}", ct);
        await EnsureSuccessAsync(response, ct);

        var payload = await response.Content.ReadFromJsonAsync<SearchResponseDto>(ApiJson.Options, ct);
        var users = (payload?.Includes?.Users ?? [])
            .ToDictionary(u => u.Id, u => new XUser(u.Id, u.Username, u.Name, u.Description));
        var items = (payload?.Data ?? [])
            .Where(t => t.AuthorId is not null && users.ContainsKey(t.AuthorId))
            .Select(t => new PostWithAuthor(
                new Post(t.Id, t.AuthorId!, t.Text, t.CreatedAt, ExtractUrls(t.Entities)),
                users[t.AuthorId!]))
            .ToList();

        return new SearchPage(items, payload?.Meta?.NextToken);
    }

    public async Task<IReadOnlyList<XUser>> GetFollowingAsync(string userId, CancellationToken ct = default)
    {
        var results = new List<XUser>();
        string? cursor = null;
        do
        {
            var qs = $"max_results=100&user.fields={UserFields}";
            if (cursor is not null) qs += $"&pagination_token={Uri.EscapeDataString(cursor)}";

            using var response = await GetAsync($"users/{userId}/following?{qs}", ct);
            await EnsureSuccessAsync(response, ct);

            var payload = await response.Content.ReadFromJsonAsync<UsersResponseDto>(ApiJson.Options, ct);
            results.AddRange((payload?.Data ?? [])
                .Select(u => new XUser(u.Id, u.Username, u.Name, u.Description)));
            cursor = payload?.Meta?.NextToken;
        }
        while (cursor is not null);

        return results;
    }

    async Task<HttpResponseMessage> GetAsync(string pathAndQuery, CancellationToken ct)
    {
        var token = await authToken.GetTokenAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, pathAndQuery);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request, ct);
    }

    static IReadOnlyList<string> ExtractUrls(EntitiesDto? entities) =>
        (entities?.Urls ?? [])
            .Select(u => u.UnwoundUrl ?? u.ExpandedUrl)
            .OfType<string>()
            .ToList();

    static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct);
        throw new XApiException((int)response.StatusCode, body);
    }
}

internal sealed record SearchResponseDto(
    [property: JsonPropertyName("data")] List<TweetDto>? Data,
    [property: JsonPropertyName("includes")] IncludesDto? Includes,
    [property: JsonPropertyName("meta")] MetaDto? Meta);

internal sealed record UsersResponseDto(
    [property: JsonPropertyName("data")] List<UserDto>? Data,
    [property: JsonPropertyName("meta")] MetaDto? Meta);

internal sealed record TweetDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("author_id")] string? AuthorId,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt,
    [property: JsonPropertyName("entities")] EntitiesDto? Entities);

internal sealed record EntitiesDto(
    [property: JsonPropertyName("urls")] List<UrlEntityDto>? Urls);

internal sealed record UrlEntityDto(
    [property: JsonPropertyName("expanded_url")] string? ExpandedUrl,
    [property: JsonPropertyName("unwound_url")] string? UnwoundUrl);

internal sealed record IncludesDto(
    [property: JsonPropertyName("users")] List<UserDto>? Users);

internal sealed record UserDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description);

internal sealed record MetaDto(
    [property: JsonPropertyName("next_token")] string? NextToken,
    [property: JsonPropertyName("result_count")] int ResultCount);

internal sealed record MeResponseDto(
    [property: JsonPropertyName("data")] UserDto? Data);
