namespace Worky.Core;

public sealed record Post(
    string Id,
    string AuthorId,
    string Text,
    DateTimeOffset? CreatedAt,
    IReadOnlyList<string> Urls);
