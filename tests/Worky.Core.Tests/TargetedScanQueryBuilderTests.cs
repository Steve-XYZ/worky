using System.Text.RegularExpressions;
using Worky.Core;

namespace Worky.Core.Tests;

public class TargetedScanQueryBuilderTests
{
    static readonly string[] Terms = ["hiring", "we're hiring", "job opening", "open role", "join our team"];

    [Fact]
    public void BuildsExactQueryShapeForSmallAuthorSet()
    {
        var query = TargetedScanQueryBuilder.BuildQuery(["alice", "bob_42"], ["rust"]);

        Assert.Equal("(rust) (from:alice OR from:bob_42) -is:retweet lang:en", query);
    }

    [Fact]
    public void QuotesMultiWordTermsVerbatim()
    {
        var query = TargetedScanQueryBuilder.BuildQuery(["a"], ["Gamedev Jobs", "hiring"]);

        Assert.Equal("(\"Gamedev Jobs\" OR hiring) (from:a) -is:retweet lang:en", query);
    }

    [Fact]
    public void SplitsLargeAuthorSetsWithoutDroppingOrDuplicatingAuthors()
    {
        var random = new Random(20260825);
        var authors = Enumerable.Range(0, 250)
            .Select(_ => new string('x', random.Next(4, 16)) + random.Next(100, 999))
            .Distinct()
            .ToList();

        var queries = TargetedScanQueryBuilder.BuildQueries(authors, Terms);

        Assert.True(queries.Count > 1);
        Assert.All(queries, q => Assert.True(q.Length <= TargetedScanQueryBuilder.MaxQueryChars, q));
        Assert.All(queries, q => Assert.True(
            TargetedScanQueryBuilder.CountOperators(q) <= TargetedScanQueryBuilder.MaxQueryOperators, q));

        var batched = queries
            .SelectMany(q => Regex.Matches(q, @"from:([A-Za-z0-9_]+)").Select(m => m.Groups[1].Value))
            .ToList();
        Assert.Equal(authors.Count, batched.Count);
        Assert.Equal(authors.OrderBy(a => a), batched.OrderBy(a => a));
    }

    [Fact]
    public void OperatorBudgetSplitsBeforeCharBudgetWithShortUsernames()
    {
        var authors = Enumerable.Range(0, 40).Select(i => $"u{i}").ToList();

        var queries = TargetedScanQueryBuilder.BuildQueries(authors, Terms);

        Assert.True(queries.Count > 1);
        Assert.All(queries, q => Assert.True(
            TargetedScanQueryBuilder.CountOperators(q) <= TargetedScanQueryBuilder.MaxQueryOperators, q));

        var batched = queries
            .SelectMany(q => Regex.Matches(q, @"from:([A-Za-z0-9_]+)").Select(m => m.Groups[1].Value))
            .ToList();
        Assert.Equal(authors, batched);
    }

    [Fact]
    public void FifteenCharUsernamesNeverTruncated()
    {
        var authors = Enumerable.Range(0, 30)
            .Select(i => new string('n', 15 - $"{i}".Length) + i)
            .ToList();
        Assert.All(authors, a => Assert.Equal(15, a.Length));

        var queries = TargetedScanQueryBuilder.BuildQueries(authors, Terms);

        var batched = queries
            .SelectMany(q => Regex.Matches(q, @"from:([A-Za-z0-9_]+)").Select(m => m.Groups[1].Value))
            .ToList();
        Assert.Equal(authors.OrderBy(a => a, StringComparer.Ordinal), batched.OrderBy(a => a, StringComparer.Ordinal));
        Assert.All(queries, q =>
        {
            Assert.True(q.Length <= TargetedScanQueryBuilder.MaxQueryChars, q);
            Assert.DoesNotContain("...", q);
        });
    }

    [Fact]
    public void BatchingIsDeterministicAcrossRuns()
    {
        var authors = Enumerable.Range(0, 60).Select(i => $"author_{i}").ToList();

        var first = TargetedScanQueryBuilder.BuildQueries(authors, Terms);
        var second = TargetedScanQueryBuilder.BuildQueries(authors, Terms);

        Assert.Equal(first, second);
    }

    [Fact]
    public void EmptyAuthorSetYieldsNoQueries() =>
        Assert.Empty(TargetedScanQueryBuilder.BuildQueries([], Terms));

    [Theory]
    [InlineData("\"we're hiring\"")]
    [InlineData("rust:lang")]
    public void TryValidateTermsRejectsTermsWithQuotesOrColons(string term)
    {
        Assert.False(TargetedScanQueryBuilder.TryValidateTerms([term, "rust"], out var error));
        Assert.Equal($"Search term '{term}' must not contain quotes or ':' operators.", error);
    }

    [Fact]
    public void TryValidateTermsRejectsOversizedTerm()
    {
        var oversized = new string('x', 446);

        Assert.False(TargetedScanQueryBuilder.TryValidateTerms([oversized], out var error));

        Assert.Contains("480-character", error);
        Assert.Contains("20-operator", error);
    }

    [Fact]
    public void TryValidateTermsRejectsWhitespaceOnlyTermSets()
    {
        Assert.False(TargetedScanQueryBuilder.TryValidateTerms(["", "   "], out var error));
        Assert.Equal("At least one search term is required.", error);
    }

    [Fact]
    public void TryValidateTermsAcceptsDefaultTerms()
    {
        Assert.True(TargetedScanQueryBuilder.TryValidateTerms(TargetedScanQueryBuilder.DefaultTerms, out var error));
        Assert.Null(error);
    }
}
