using Worky.Core;

namespace Worky.Core.Tests;

public class JobSignalClassifierTests
{
    readonly JobSignalClassifier classifier = new();

    static Post PostOf(string text) => new("1", "100", text, DateTimeOffset.UtcNow, []);

    [Fact]
    public void StrongPhraseWithAtsLinkMatches()
    {
        var post = PostOf(
            "We're hiring a Senior Backend Engineer in Lisbon! Apply here: https://jobs.lever.co/acme/backend");

        var signal = classifier.Classify(post);

        Assert.True(signal.IsMatch);
        Assert.Contains(signal.Reasons, r => r.StartsWith("phrase", StringComparison.Ordinal));
        Assert.Contains(signal.Reasons, r => r.StartsWith("ats link", StringComparison.Ordinal));
    }

    [Fact]
    public void CasualHiringMentionStaysBelowThreshold()
    {
        var post = PostOf("Companies should stop hiring juniors and start mentoring seniors instead.");

        Assert.False(classifier.Classify(post).IsMatch);
    }

    [Fact]
    public void NeutralPostDoesNotMatch()
    {
        var post = PostOf("Beautiful morning run along the river.");

        Assert.False(classifier.Classify(post).IsMatch);
    }

    [Fact]
    public void AtsLinkAloneMatches()
    {
        var post = PostOf("New opportunity just went live https://boards.greenhouse.io/acme/jobs/4021183004");

        Assert.True(classifier.Classify(post).IsMatch);
    }

    [Fact]
    public void DistinctPhrasesAccumulate()
    {
        var post = PostOf("We are hiring! Join our team, open roles across the stack.");

        var signal = classifier.Classify(post);

        Assert.True(signal.IsMatch);
        Assert.True(signal.Score >= 3 * JobSignalClassifier.StrongPhraseWeight);
    }
}
