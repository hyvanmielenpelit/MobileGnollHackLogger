namespace Overseer.Tests.UnitTests;

using System.Linq;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The BOARD FACTS quote check: every double-quoted literal in a rubric's BOARD FACTS bullets looked
/// up verbatim in the board text. Only missing literals are issues; an unquoted bullet is a count.
/// </summary>
public class BenchmarkBoardFactsCheckerTests
{
    private const string Grail = "T - the uncursed Holy Grail (0 charges, 0 rechargings)";
    private const string Dwarf = "Creature <13,9> 'h' [red] a level 3 peaceful dwarf (northwest)";

    private const string Board =
        "Dlvl:3 $:10 HP:14(14) Pw:5(5) AC:4\n"
        + "Inventory:\n"
        + Grail + "\n"
        + "d - 2 uncursed scrolls labeled FOOBIE BLETCH\n"
        + "Nearby:\n"
        + Dwarf + "\n"
        + "Something is written here in the dust. You read: \"Elbereth\".\n";

    private static BoardFactsCheckDto CheckOne(string expectedPoints, string board = Board, long id = 101, int orderIndex = 1)
        => BenchmarkBoardFactsChecker.Check(board, new[] { (id, orderIndex, (string?)expectedPoints) });

    [Fact]
    public void Run54HolyGrail_TheRubricDroppedABucWord_IsMissing()
    {
        var check = CheckOne("**BOARD FACTS**\n- The inventory lists \"T - the Holy Grail (0 charges, 0 rechargings)\".", id: 7, orderIndex: 6);

        Assert.Equal(1, check.BulletCount);
        Assert.Equal(1, check.CheckedLiteralCount);
        var missing = Assert.Single(check.MissingLiterals);
        Assert.Equal(7, missing.QuestionId);
        Assert.Equal(6, missing.OrderIndex);
        Assert.Equal("T - the Holy Grail (0 charges, 0 rechargings)", missing.Literal);
        Assert.Contains("The inventory lists", missing.LineExcerpt);
    }

    [Fact]
    public void Run54HolyGrail_TheBoardsOwnLine_IsFound()
    {
        var check = CheckOne($"**BOARD FACTS**\n- The inventory lists \"{Grail}\".");

        Assert.Equal(1, check.CheckedLiteralCount);
        Assert.Empty(check.MissingLiterals);
    }

    [Fact]
    public void SeveralLiteralsOnOneBullet_AreEachChecked()
    {
        var check = CheckOne("**BOARD FACTS**\n- The status line shows \"HP:14(14)\", \"Pw:5(5)\" and \"AC:3\".");

        Assert.Equal(1, check.BulletCount);
        Assert.Equal(3, check.CheckedLiteralCount);
        Assert.Equal("AC:3", Assert.Single(check.MissingLiterals).Literal);
    }

    [Fact]
    public void ALiteralWithApostrophesAndBrackets_IsReadLiterally()
    {
        var check = CheckOne($"**BOARD FACTS**\n- The dwarf is listed as \"{Dwarf}\".");

        Assert.Equal(1, check.CheckedLiteralCount);
        Assert.Empty(check.MissingLiterals);
        Assert.Equal(new[] { Dwarf }, BenchmarkBoardFactsChecker.ExtractLiterals($"The dwarf is listed as \"{Dwarf}\"."));
    }

    [Fact]
    public void AQuotedBoardLineContainingQuoteCharacters_SplitsIntoFragmentsThatAreStillOnTheBoard()
    {
        const string bullet = "- The engraving reads \"Something is written here in the dust. You read: \"Elbereth\".\"";

        var literals = BenchmarkBoardFactsChecker.ExtractLiterals(bullet.Substring(2));
        Assert.Equal(new[] { "Something is written here in the dust. You read: ", "." }, literals);

        var check = CheckOne("**BOARD FACTS**\n" + bullet);
        Assert.Equal(2, check.CheckedLiteralCount);
        Assert.Empty(check.MissingLiterals);
    }

    [Fact]
    public void AnUnpairedLastQuote_IsIgnored()
    {
        var literals = BenchmarkBoardFactsChecker.ExtractLiterals("Reads \"HP:14(14)\" then a stray \" here");

        Assert.Equal(new[] { "HP:14(14)" }, literals);
    }

    [Fact]
    public void TypographicQuotes_AreReadTheSameWay()
    {
        var check = CheckOne("**BOARD FACTS**\n- The scrolls are “d - 2 uncursed scrolls labeled FOOBIE BLETCH”; the status line has “HP:15(15)”.");

        Assert.Equal(2, check.CheckedLiteralCount);
        Assert.Equal("HP:15(15)", Assert.Single(check.MissingLiterals).Literal);
    }

    [Fact]
    public void ACrlfBoard_MatchesAnLfRubric_AndTheReverse()
    {
        string crlfBoard = Board.Replace("\n", "\r\n");
        string lfRubric = $"**BOARD FACTS**\n- The inventory lists \"{Grail}\".\n- Status \"HP:14(14)\".";
        string crlfRubric = lfRubric.Replace("\n", "\r\n");

        var crlfBoardCheck = CheckOne(lfRubric, crlfBoard);
        Assert.Equal(2, crlfBoardCheck.BulletCount);
        Assert.Equal(2, crlfBoardCheck.CheckedLiteralCount);
        Assert.Empty(crlfBoardCheck.MissingLiterals);

        var crlfRubricCheck = CheckOne(crlfRubric);
        Assert.Equal(2, crlfRubricCheck.BulletCount);
        Assert.Equal(2, crlfRubricCheck.CheckedLiteralCount);
        Assert.Empty(crlfRubricCheck.MissingLiterals);
    }

    [Fact]
    public void TheSection_EndsAtTheNextBoldHeading()
    {
        var check = CheckOne(
            "**BOARD FACTS**\n"
            + "- Status \"HP:14(14)\".\n"
            + "**REQUIRED**\n"
            + "- The answer says \"this text is not on the board\".\n");

        Assert.Equal(1, check.BulletCount);
        Assert.Equal(1, check.CheckedLiteralCount);
        Assert.Empty(check.MissingLiterals);
    }

    [Fact]
    public void ARubricWithNoSection_ChecksNothing()
    {
        var check = CheckOne("**REQUIRED**\n- The answer quotes \"not on the board\".");

        Assert.Equal(0, check.BulletCount);
        Assert.Equal(0, check.CheckedLiteralCount);
        Assert.Equal(0, check.UnquotedBulletCount);
        Assert.Empty(check.MissingLiterals);
        Assert.Empty(check.UnquotedBullets);
    }

    [Fact]
    public void ANullRubric_ChecksNothing()
    {
        var check = BenchmarkBoardFactsChecker.Check(Board, new[] { (1L, 1, (string?)null) });

        Assert.Equal(0, check.BulletCount);
        Assert.Empty(check.MissingLiterals);
    }

    [Fact]
    public void AnUnquotedBullet_IsCounted_AndNotReportedAsMissing()
    {
        var check = CheckOne(
            "**BOARD FACTS**\n"
            + "- The status line shows no hunger state.\n"
            + "- Status \"HP:14(14)\".",
            id: 5,
            orderIndex: 5);

        Assert.Equal(2, check.BulletCount);
        Assert.Equal(1, check.CheckedLiteralCount);
        Assert.Equal(1, check.UnquotedBulletCount);
        Assert.Empty(check.MissingLiterals);
        var unquoted = Assert.Single(check.UnquotedBullets);
        Assert.Equal(5, unquoted.OrderIndex);
        Assert.Null(unquoted.Literal);
        Assert.Equal("The status line shows no hunger state.", unquoted.LineExcerpt);
    }

    [Fact]
    public void AWrappedBullet_IsOneBullet_AndALiteralSplitAcrossTheWrap_IsJoinedBySingleSpace()
    {
        var check = CheckOne(
            "**BOARD FACTS**\n"
            + "- The dwarf is listed as \"Creature <13,9> 'h' [red] a level 3\n"
            + "  peaceful dwarf (northwest)\".\n"
            + "- The inventory lists\n"
            + $"  \"{Grail}\".\n");

        Assert.Equal(2, check.BulletCount);
        Assert.Equal(2, check.CheckedLiteralCount);
        Assert.Equal(0, check.UnquotedBulletCount);
        Assert.Empty(check.MissingLiterals);
    }

    [Fact]
    public void Questions_AreReportedInOrderIndexOrder()
    {
        var check = BenchmarkBoardFactsChecker.Check(Board, new[]
        {
            (30L, 17, (string?)"**BOARD FACTS**\n- \"missing seventeen\""),
            (10L, 1, (string?)"**BOARD FACTS**\n- \"missing one\""),
        });

        Assert.Equal(new[] { 1, 17 }, check.MissingLiterals.Select(m => m.OrderIndex).ToArray());
    }

    [Fact]
    public void TheLineExcerpt_IsCappedAt160Characters()
    {
        string longText = new string('x', 300);
        var check = CheckOne($"**BOARD FACTS**\n- {longText} \"not on the board\"");

        var missing = Assert.Single(check.MissingLiterals);
        Assert.Equal(BenchmarkBoardFactsChecker.MaxExcerptLength, missing.LineExcerpt.Length);
    }

    [Fact]
    public void SerializeAndDeserialize_RoundTrip_AndUnreadableJsonIsNull()
    {
        var check = CheckOne("**BOARD FACTS**\n- \"HP:14(14)\" and \"AC:3\".\n- No quote.");

        var copy = BenchmarkBoardFactsChecker.Deserialize(BenchmarkBoardFactsChecker.Serialize(check));

        Assert.NotNull(copy);
        Assert.Equal(check.BulletCount, copy!.BulletCount);
        Assert.Equal(check.CheckedLiteralCount, copy.CheckedLiteralCount);
        Assert.Equal(check.UnquotedBulletCount, copy.UnquotedBulletCount);
        Assert.Equal("AC:3", Assert.Single(copy.MissingLiterals).Literal);
        Assert.Single(copy.UnquotedBullets);

        Assert.Null(BenchmarkBoardFactsChecker.Deserialize(null));
        Assert.Null(BenchmarkBoardFactsChecker.Deserialize("   "));
        Assert.Null(BenchmarkBoardFactsChecker.Deserialize("{not json"));
    }
}
