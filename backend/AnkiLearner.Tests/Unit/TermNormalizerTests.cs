using AnkiLearner.Core;

namespace AnkiLearner.Tests.Unit;

public class TermNormalizerTests
{
    [Theory]
    [InlineData("da", true)]
    [InlineData("DA", true)]
    [InlineData("de", false)]
    [InlineData("en", false)]
    public void HasParticles_OnlyForLanguagesWithATable(string language, bool expected) =>
        Assert.Equal(expected, TermNormalizer.HasParticles(language));

    [Theory]
    [InlineData("en aftale", "aftale")]
    [InlineData("et minde", "minde")]
    [InlineData("<b>en</b> aftale", "aftale")]
    [InlineData("At Spise", "spise")]
    [InlineData("  at   have ondt  ", "have ondt")]
    [InlineData("aftale", "aftale")]
    public void MatchKey_StripsOneLeadingDanishParticle(string term, string expected) =>
        Assert.Equal(expected, TermNormalizer.MatchKey(term, "da"));

    [Theory]
    [InlineData("en")]
    [InlineData("et")]
    [InlineData("at")]
    [InlineData("enkel")]
    [InlineData("ethvert")]
    [InlineData("atom")]
    [InlineData("atten")]
    public void MatchKey_LeavesBareParticlesAndLookalikeWordsAlone(string term) =>
        Assert.Equal(term, TermNormalizer.MatchKey(term, "da"));

    [Fact]
    public void MatchKey_StripsOnlyOneParticle() =>
        Assert.Equal("en", TermNormalizer.MatchKey("et en", "da"));

    [Fact]
    public void MatchKey_LanguageWithoutTable_IsPlainNormalizedForm()
    {
        Assert.Equal("der hund", TermNormalizer.MatchKey("Der Hund", "de"));
        Assert.Equal("en aftale", TermNormalizer.MatchKey("en aftale", "en"));
    }

    [Fact]
    public void MatchCandidates_Danish_CoverBareAndEveryParticle()
    {
        var expected = new[] { "aftale", "en aftale", "et aftale", "at aftale" };
        Assert.Equal(expected, TermNormalizer.MatchCandidates("aftale", "da"));
        Assert.Equal(expected, TermNormalizer.MatchCandidates("<i>En</i> aftale", "da"));
    }

    [Fact]
    public void MatchCandidates_BareParticle_KeepsItAsTheKey() =>
        Assert.Equal(["en", "en en", "et en", "at en"], TermNormalizer.MatchCandidates("en", "da"));

    [Fact]
    public void MatchCandidates_LanguageWithoutTable_IsJustTheNormalizedForm() =>
        Assert.Equal(["der hund"], TermNormalizer.MatchCandidates(" <b>Der</b>  Hund ", "de"));

    [Theory]
    [InlineData("", "da")]
    [InlineData("  <br> ", "da")]
    [InlineData("", "de")]
    public void MatchCandidates_EmptyInput_IsEmpty(string term, string language) =>
        Assert.Empty(TermNormalizer.MatchCandidates(term, language));
}
