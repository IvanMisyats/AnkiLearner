using AnkiLearner.Core;
using AnkiLearner.Core.Abstractions;
using AnkiLearner.Core.Entities;

namespace AnkiLearner.Tests.Unit;

public class TranslateShaperTests
{
    // The primary author's setup: learning Danish, knowing English + Ukrainian.
    private static readonly string[] Known = ["en", "uk"];

    private static (TranslateResult Result, TranslateCard? WholeInput) Shape(
        TranslateResult raw, string text, string? sourceOverride = null) =>
        TranslateShaper.Shape(raw, text, sourceOverride, "da", Known);

    private static TranslateResult Raw(
        string source, InputKind kind, Dictionary<string, string>? inputTranslations = null,
        params TranslateCard[] cards) =>
        new(source, kind, inputTranslations ?? [], [.. cards]);

    private static TranslateCard Card(
        string term, string transcription = "", string partOfSpeech = "", string gender = "",
        Dictionary<string, List<string>>? meanings = null, string example = "",
        Dictionary<string, string>? exampleTranslations = null, string note = "") =>
        new(term, transcription, partOfSpeech, gender, meanings ?? [], example, exampleTranslations ?? [], note);

    // ---------- CleanText ----------

    [Fact]
    public void CleanText_CollapsesEveryWhitespaceRun() =>
        Assert.Equal("Jeg har ondt i maven", TranslateShaper.CleanText("  Jeg har\n\tondt   i maven \r\n"));

    [Fact]
    public void CleanText_DropsInvisibleFormatCharacters()
    {
        // BOM, zero-width space / non-joiner / joiner.
        Assert.Equal("hund kat", TranslateShaper.CleanText("\uFEFFhu\u200Bnd \u200C kat\u200D"));
        // Soft hyphen.
        Assert.Equal("blomster", TranslateShaper.CleanText("blom\u00ADster"));
        // Left-to-right mark.
        Assert.Equal("hund", TranslateShaper.CleanText("\u200Ehund\u200E"));
        Assert.Equal("", TranslateShaper.CleanText("\u200B\u200C\u200D\uFEFF\u00AD\u200E"));
    }

    [Fact]
    public void CleanText_TurnsControlCharactersIntoSpaces() =>
        Assert.Equal("hund kat", TranslateShaper.CleanText("hund\u0000kat\u0007"));

    // ---------- 1. Source language and input kind ----------

    [Fact]
    public void Override_WinsOverTheDetectedSource()
    {
        var (result, _) = Shape(Raw("da", InputKind.Word, null, Card("gift")), "gift", sourceOverride: "en");
        Assert.Equal("en", result.SourceLanguage);
    }

    [Fact]
    public void Override_WinsOverOther()
    {
        var (result, _) = Shape(Raw("other", InputKind.Word, null, Card("en gave")), "gift", sourceOverride: "en");
        Assert.Equal("en", result.SourceLanguage);
        Assert.Single(result.Cards);
    }

    [Theory]
    [InlineData("da", "da")]
    [InlineData("uk", "uk")]
    [InlineData("de", "da")] // not one of the user's languages → the learning language
    public void NoOverride_KeepsTheDetectedSource(string detected, string expected)
    {
        var (result, _) = Shape(Raw(detected, InputKind.Word, null, Card("hund")), "hund");
        Assert.Equal(expected, result.SourceLanguage);
    }

    [Theory]
    [InlineData(InputKind.Word)]
    [InlineData(InputKind.Phrase)]
    [InlineData(InputKind.Sentence)] // "Tak!" is a sentence without whitespace
    public void TheModelsInputKind_IsKept_EvenForASingleToken(InputKind modelKind)
    {
        var (result, _) = Shape(Raw("da", modelKind, null, Card("tak")), "Tak!");
        Assert.Equal(modelKind, result.InputKind);
    }

    // ---------- 2. Input translations ----------

    [Fact]
    public void InputTranslations_SourceSlotIsTheOriginal_AndEveryLanguageIsPresent()
    {
        var raw = Raw("en", InputKind.Word, new()
        {
            ["da"] = " en hund ",
            ["en"] = "the model's rewrite",
            ["de"] = "Hund", // not one of the user's languages
        }, Card("en hund"));

        var (result, _) = Shape(raw, "dog");

        Assert.Equal(new Dictionary<string, string> { ["da"] = "en hund", ["en"] = "dog", ["uk"] = "" },
            result.InputTranslations);
    }

    // ---------- 3. Cards ----------

    [Fact]
    public void Cards_WithAnEmptyOrHtmlOnlyTerm_AreDropped()
    {
        var (result, _) = Shape(Raw("da", InputKind.Word, null,
            Card(""), Card("   "), Card("<br>"), Card("<b> </b>"), Card(" hund ")), "hund");
        Assert.Equal("hund", Assert.Single(result.Cards).Term);
    }

    [Theory]
    [InlineData("<b>en hund</b>", "en hund")]
    [InlineData("  en <i>hund</i>  ", "en hund")]
    [InlineData("rock &amp; roll", "rock & roll")]
    [InlineData("En Hund", "En Hund")] // case kept
    public void Cards_TermIsPlainText(string modelTerm, string expected)
    {
        var (result, _) = Shape(Raw("da", InputKind.Word, null, Card(modelTerm)), "hund");
        Assert.Equal(expected, Assert.Single(result.Cards).Term);
    }

    [Fact]
    public void Cards_AreDedupedByTheExactNormalizedTerm_KeepingTheFirst()
    {
        var (result, _) = Shape(Raw("da", InputKind.Word, null,
            Card("Hund", transcription: "first"),
            Card("<b>hund</b>", transcription: "second"),
            Card("hund ", transcription: "third")), "hund");

        var card = Assert.Single(result.Cards);
        Assert.Equal("first", card.Transcription);
    }

    [Fact]
    public void Cards_ARepeatedTerm_AddsItsSensesToTheFirstCard()
    {
        // "en kost": a broom and a diet. Same citation form, so one card with both senses.
        var (result, _) = Shape(Raw("da", InputKind.Word, null,
            Card("en kost", note: "first", meanings: new() { ["en"] = ["broom"], ["uk"] = ["мітла"] }),
            Card("en kost", note: "second", meanings: new() { ["en"] = ["diet", "Broom", "food", "fare", "nutrition"] })),
            "kost");

        var card = Assert.Single(result.Cards);
        Assert.Equal("first", card.Note);
        // Distinct (case-insensitive), in order, at most 4 per language.
        Assert.Equal(["broom", "diet", "food", "fare"], card.Meanings["en"]);
        Assert.Equal(["мітла"], card.Meanings["uk"]);
    }

    [Fact]
    public void Cards_HomographsThatDifferOnlyByTheArticle_AreBothKept()
    {
        var (result, _) = Shape(Raw("da", InputKind.Word, null,
            Card("gift", partOfSpeech: "adjective"),
            Card("en gift", partOfSpeech: "noun")), "gift");

        Assert.Equal(["gift", "en gift"], result.Cards.Select(c => c.Term));
    }

    [Fact]
    public void Cards_AreCappedAtFive_KeepingTheFirst()
    {
        var terms = new[] { "en", "to", "tre", "fire", "fem", "seks", "syv" };
        var (result, _) = Shape(Raw("da", InputKind.Word, null, [.. terms.Select(t => Card(t))]), "tal");

        Assert.Equal(TranslateShaper.MaxCards, result.Cards.Count);
        Assert.Equal(terms.Take(5), result.Cards.Select(c => c.Term));
    }

    [Fact]
    public void Cards_ARepeatedTermAfterTheCap_StillMergesItsSenses()
    {
        var cards = new[] { "en", "to", "tre", "fire", "fem", "seks" }
            .Select(t => Card(t, meanings: new() { ["en"] = [t + "-sense"] }))
            .Append(Card("en", meanings: new() { ["en"] = ["one", "a"] }))
            .ToArray();

        var (result, _) = Shape(Raw("da", InputKind.Word, null, cards), "tal");

        Assert.Equal(TranslateShaper.MaxCards, result.Cards.Count);
        Assert.Equal(["en-sense", "one", "a"], result.Cards[0].Meanings["en"]);
    }

    [Fact]
    public void Cards_FieldsAreClampedToTheWordColumnLimits()
    {
        var (result, _) = Shape(Raw("da", InputKind.Word, null, Card("hund",
            transcription: new string('a', 250),
            partOfSpeech: new string('b', 60),
            gender: new string('c', 40))), "hund");

        var card = Assert.Single(result.Cards);
        Assert.Equal(Word.TranscriptionMaxLength, card.Transcription.Length);
        Assert.Equal(Word.PartOfSpeechMaxLength, card.PartOfSpeech.Length);
        Assert.Equal(Word.GenderMaxLength, card.Gender.Length);
    }

    [Fact]
    public void Cards_HaveEveryKnownLanguage_AndNoOthers()
    {
        var (result, _) = Shape(Raw("da", InputKind.Word, null, Card("hund",
            meanings: new() { ["en"] = ["dog", " ", "hound"], ["de"] = ["Hund"] },
            exampleTranslations: new() { ["en"] = "The dog." })), "hund");

        var card = Assert.Single(result.Cards);
        Assert.Equal(["en", "uk"], card.Meanings.Keys);
        Assert.Equal(["dog", "hound"], card.Meanings["en"]);
        Assert.Empty(card.Meanings["uk"]);
        Assert.Equal(new Dictionary<string, string> { ["en"] = "The dog.", ["uk"] = "" }, card.ExampleTranslations);
    }

    // ---------- 4. Sentence examples ----------

    [Fact]
    public void Sentence_InTheLearningLanguage_IsEveryCardsExample()
    {
        const string text = "Jeg har ondt i maven";
        var raw = Raw("da", InputKind.Sentence, new() { ["en"] = "I have a stomach ache", ["uk"] = "У мене болить живіт" },
            Card("ondt i maven", example: "Model example.", exampleTranslations: new() { ["en"] = "Model translation." }),
            Card("en mave"));

        var (result, _) = Shape(raw, text);

        Assert.Equal(2, result.Cards.Count);
        Assert.All(result.Cards, card =>
        {
            Assert.Equal(text, card.Example);
            Assert.Equal("I have a stomach ache", card.ExampleTranslations["en"]);
            Assert.Equal("У мене болить живіт", card.ExampleTranslations["uk"]);
        });
    }

    [Fact]
    public void Sentence_WithoutWhitespace_StillGetsTheInputAsExample()
    {
        var raw = Raw("da", InputKind.Sentence, new() { ["en"] = "Thanks!", ["uk"] = "Дякую!" }, Card("tak"));

        var (result, _) = Shape(raw, "Tak!");

        var card = Assert.Single(result.Cards);
        Assert.Equal("Tak!", card.Example);
        Assert.Equal("Thanks!", card.ExampleTranslations["en"]);
        Assert.Equal("Дякую!", card.ExampleTranslations["uk"]);
    }

    [Fact]
    public void Sentence_InAKnownLanguage_UsesItsTranslation_WithTheOriginalAsTranslation()
    {
        const string text = "I have a stomach ache";
        var raw = Raw("en", InputKind.Sentence, new() { ["da"] = "Jeg har ondt i maven", ["uk"] = "У мене болить живіт" },
            Card("ondt i maven"), Card("en mave"));

        var (result, _) = Shape(raw, text);

        Assert.All(result.Cards, card =>
        {
            Assert.Equal("Jeg har ondt i maven", card.Example);
            Assert.Equal(text, card.ExampleTranslations["en"]);
            Assert.Equal("У мене болить живіт", card.ExampleTranslations["uk"]);
        });
    }

    [Fact]
    public void Sentence_InAKnownLanguage_WithoutALearningLanguageRendering_KeepsTheModelsExamples()
    {
        var raw = Raw("en", InputKind.Sentence, new() { ["da"] = "", ["uk"] = "У мене болить живіт" },
            Card("en mave", example: "Maven gør ondt.", exampleTranslations: new() { ["en"] = "The stomach hurts." }));

        var (result, wholeInput) = Shape(raw, "I have a stomach ache");

        var card = Assert.Single(result.Cards);
        Assert.Equal("Maven gør ondt.", card.Example);
        Assert.Equal("The stomach hurts.", card.ExampleTranslations["en"]);
        Assert.Null(wholeInput);
    }

    [Fact]
    public void Phrase_KeepsTheModelsExamples()
    {
        var raw = Raw("da", InputKind.Phrase, new() { ["en"] = "stomach ache" },
            Card("ondt i maven", example: "Hun har ondt i maven.", exampleTranslations: new() { ["en"] = "She has a stomach ache." }));

        var (result, _) = Shape(raw, "ondt i maven");

        var card = Assert.Single(result.Cards);
        Assert.Equal("Hun har ondt i maven.", card.Example);
        Assert.Equal("She has a stomach ache.", card.ExampleTranslations["en"]);
    }

    // ---------- 5. Whole input ----------

    [Fact]
    public void WholeInput_ForALearningLanguagePhrase_IsTheInputItself()
    {
        var raw = Raw("da", InputKind.Phrase, new() { ["en"] = "break up with", ["uk"] = "" }, Card("at slå op"));

        var (_, wholeInput) = Shape(raw, "slå op med");

        Assert.NotNull(wholeInput);
        Assert.Equal("slå op med", wholeInput.Term);
        Assert.Equal(["break up with"], wholeInput.Meanings["en"]);
        Assert.Empty(wholeInput.Meanings["uk"]); // empty rendering → no meaning
        Assert.Equal(new Dictionary<string, string> { ["en"] = "", ["uk"] = "" }, wholeInput.ExampleTranslations);
        Assert.Equal("", wholeInput.Example);
        Assert.Equal("", wholeInput.Note);
        Assert.Equal("", wholeInput.Transcription);
        Assert.Equal("", wholeInput.PartOfSpeech);
        Assert.Equal("", wholeInput.Gender);
    }

    [Fact]
    public void WholeInput_ForAKnownLanguageSentence_IsItsLearningLanguageTranslation()
    {
        const string text = "I have a stomach ache";
        var raw = Raw("en", InputKind.Sentence, new() { ["da"] = "Jeg har ondt i maven", ["uk"] = "У мене болить живіт" },
            Card("ondt i maven"));

        var (_, wholeInput) = Shape(raw, text);

        Assert.NotNull(wholeInput);
        Assert.Equal("Jeg har ondt i maven", wholeInput.Term);
        Assert.Equal([text], wholeInput.Meanings["en"]);
        Assert.Equal(["У мене болить живіт"], wholeInput.Meanings["uk"]);
    }

    [Fact]
    public void WholeInput_IsOmitted_ForAWord()
    {
        var raw = Raw("da", InputKind.Word, new() { ["en"] = "dog" }, Card("en hund"));
        Assert.Null(Shape(raw, "hund").WholeInput);
    }

    [Theory]
    [InlineData(InputKind.Phrase)]
    [InlineData(InputKind.Sentence)]
    public void WholeInput_IsOmitted_ForASingleToken_WhateverTheKind(InputKind kind)
    {
        var raw = Raw("da", kind, new() { ["en"] = "Thanks!" }, Card("tak"));
        Assert.Null(Shape(raw, "Tak!").WholeInput);
    }

    [Fact]
    public void WholeInput_ForAPhrase_DropsTrailingPunctuation()
    {
        var raw = Raw("da", InputKind.Phrase, new() { ["en"] = "Thanks for the meal!" }, Card("en mad"));

        var (_, wholeInput) = Shape(raw, "Tak for mad!");

        Assert.Equal("Tak for mad", wholeInput?.Term);
    }

    [Fact]
    public void WholeInput_ForASentence_KeepsItsPunctuation()
    {
        var raw = Raw("da", InputKind.Sentence, new() { ["en"] = "I have a stomach ache." }, Card("en mave"));

        var (_, wholeInput) = Shape(raw, "Jeg har ondt i maven.");

        Assert.Equal("Jeg har ondt i maven.", wholeInput?.Term);
    }

    [Theory]
    [InlineData("Tak for mad!", InputKind.Phrase)]
    [InlineData("Tak for mad!»", InputKind.Phrase)]
    [InlineData("Tak for mad…", InputKind.Sentence)]
    public void WholeInput_IsOmitted_WhenACardIsTheSameEntryButForTrailingPunctuation(string text, InputKind kind)
    {
        var raw = Raw("da", kind, new() { ["en"] = "Thanks for the meal!" }, Card("tak for mad"));
        Assert.Null(Shape(raw, text).WholeInput);
    }

    [Fact]
    public void WholeInput_IsOmitted_WhenTheLearningLanguageRenderingIsEmpty()
    {
        var raw = Raw("en", InputKind.Phrase, new() { ["da"] = "" }, Card("at forelske sig"));
        Assert.Null(Shape(raw, "fall in love").WholeInput);
    }

    [Theory]
    [InlineData("forelske sig")]    // only the infinitive marker differs
    [InlineData("At forelske sig")] // case differs
    public void WholeInput_IsOmitted_WhenACardIsTheSameEntry(string cardTerm)
    {
        var raw = Raw("en", InputKind.Phrase, new() { ["da"] = "at forelske sig" }, Card(cardTerm));
        Assert.Null(Shape(raw, "fall in love").WholeInput);
    }

    // ---------- 6. Other languages ----------

    [Fact]
    public void Other_HasNoCardsAndNoWholeInput()
    {
        var raw = Raw("other", InputKind.Phrase, new() { ["da"] = "god dag" }, Card("god dag"));

        var (result, wholeInput) = Shape(raw, "guten Tag");

        Assert.Equal(TranslateShaper.OtherLanguage, result.SourceLanguage);
        Assert.Empty(result.Cards);
        Assert.Null(wholeInput);
    }
}
