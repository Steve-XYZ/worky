namespace Worky.Core;

public sealed record JobSignal(double Score, bool IsMatch, IReadOnlyList<string> Reasons);

public sealed record JobLead(Post Post, XUser Author, JobSignal Signal)
{
    public string Permalink => $"https://x.com/{Author.UserName}/status/{Post.Id}";
}
