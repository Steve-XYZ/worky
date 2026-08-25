using System.Text.Json.Serialization;

namespace Worky.Core.Graph;

public sealed record GraphState
{
    public static readonly TimeSpan FreshnessTtl = TimeSpan.FromDays(7);

    [JsonPropertyName("user_id")]
    public required string UserId { get; init; }

    [JsonPropertyName("username")]
    public required string UserName { get; init; }

    [JsonPropertyName("followed_users")]
    public required IReadOnlyList<FollowedUser> FollowedUsers { get; init; }

    [JsonPropertyName("ingested_at")]
    public required DateTimeOffset IngestedAt { get; init; }

    public bool IsStale(DateTimeOffset now) => now - IngestedAt >= FreshnessTtl;
}

public sealed record FollowedUser(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("username")] string UserName,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description);
