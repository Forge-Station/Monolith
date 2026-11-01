using Content.Shared._WF.RoundSetup;
using NUnit.Framework;

namespace Content.Tests._WF.RoundSetup;

/// <summary>Exercises every access mode against invitations, codes, capacity and round state.</summary>
[TestFixture]
public sealed class WFRoundSetupPolicyTest
{
    /// <summary>All access modes obey capacity and late-join restrictions.</summary>
    [TestCase(WFLobbyAccess.Public, false, false, WFLobbyJoinDecision.Join)]
    [TestCase(WFLobbyAccess.Public, false, true, WFLobbyJoinDecision.Join)]
    [TestCase(WFLobbyAccess.Public, true, false, WFLobbyJoinDecision.Join)]
    [TestCase(WFLobbyAccess.Public, true, true, WFLobbyJoinDecision.Join)]
    [TestCase(WFLobbyAccess.Requests, false, false, WFLobbyJoinDecision.Request)]
    [TestCase(WFLobbyAccess.Requests, false, true, WFLobbyJoinDecision.Request)]
    [TestCase(WFLobbyAccess.Requests, true, false, WFLobbyJoinDecision.Join)]
    [TestCase(WFLobbyAccess.Requests, true, true, WFLobbyJoinDecision.Join)]
    [TestCase(WFLobbyAccess.Closed, false, false, WFLobbyJoinDecision.Denied)]
    [TestCase(WFLobbyAccess.Closed, false, true, WFLobbyJoinDecision.Denied)]
    [TestCase(WFLobbyAccess.Closed, true, false, WFLobbyJoinDecision.Join)]
    [TestCase(WFLobbyAccess.Closed, true, true, WFLobbyJoinDecision.Join)]
    [TestCase(WFLobbyAccess.Code, false, false, WFLobbyJoinDecision.Denied)]
    [TestCase(WFLobbyAccess.Code, false, true, WFLobbyJoinDecision.Join)]
    [TestCase(WFLobbyAccess.Code, true, false, WFLobbyJoinDecision.Join)]
    [TestCase(WFLobbyAccess.Code, true, true, WFLobbyJoinDecision.Join)]
    [TestCase(WFLobbyAccess.Invite, false, false, WFLobbyJoinDecision.Denied)]
    [TestCase(WFLobbyAccess.Invite, false, true, WFLobbyJoinDecision.Denied)]
    [TestCase(WFLobbyAccess.Invite, true, false, WFLobbyJoinDecision.Join)]
    [TestCase(WFLobbyAccess.Invite, true, true, WFLobbyJoinDecision.Join)]
    public void AccessMatrix(
        WFLobbyAccess access,
        bool invited,
        bool validCode,
        WFLobbyJoinDecision expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Evaluate(false, false, false), Is.EqualTo(expected), "Pregame does not require late joining.");
            Assert.That(Evaluate(false, true, false), Is.EqualTo(expected), "Pregame with late joining enabled.");
            Assert.That(Evaluate(true, true, false), Is.EqualTo(expected), "A started lobby permits late joining.");
            Assert.That(Evaluate(true, false, false), Is.EqualTo(WFLobbyJoinDecision.Denied), "Late joining disabled.");
            Assert.That(Evaluate(false, false, true), Is.EqualTo(WFLobbyJoinDecision.Denied), "Full pregame lobby.");
            Assert.That(Evaluate(false, true, true), Is.EqualTo(WFLobbyJoinDecision.Denied), "Full pregame lobby with late joining enabled.");
            Assert.That(Evaluate(true, false, true), Is.EqualTo(WFLobbyJoinDecision.Denied), "Full started lobby with late joining disabled.");
            Assert.That(Evaluate(true, true, true), Is.EqualTo(WFLobbyJoinDecision.Denied), "Full started lobby.");
        });

        WFLobbyJoinDecision Evaluate(bool started, bool allowLateJoin, bool full)
        {
            return WFRoundSetupPolicy.EvaluateJoin(access, started, allowLateJoin, full, invited, validCode);
        }
    }

    /// <summary>Company settings let members walk on; outsiders ask or are turned away.</summary>
    [TestCase(WFLobbyAccess.CompanyOnly, true, false, WFLobbyJoinDecision.Join)]
    [TestCase(WFLobbyAccess.CompanyOnly, false, false, WFLobbyJoinDecision.Denied)]
    [TestCase(WFLobbyAccess.CompanyOnly, false, true, WFLobbyJoinDecision.Join)]
    [TestCase(WFLobbyAccess.CompanyFirst, true, false, WFLobbyJoinDecision.Join)]
    [TestCase(WFLobbyAccess.CompanyFirst, false, false, WFLobbyJoinDecision.Request)]
    [TestCase(WFLobbyAccess.CompanyFirst, false, true, WFLobbyJoinDecision.Join)]
    public void CompanyAccess(WFLobbyAccess access, bool companyMember, bool invited, WFLobbyJoinDecision expected)
    {
        Assert.That(WFRoundSetupPolicy.EvaluateJoin(access, false, false, false, invited, false, companyMember), Is.EqualTo(expected));
    }

    /// <summary>Malformed access values fail closed even with both credentials.</summary>
    [Test]
    public void UnknownAccessNeverGrantsEntry([Range(7, 255)] int value)
    {
        Assert.That(
            WFRoundSetupPolicy.EvaluateJoin((WFLobbyAccess) value, false, true, false, true, true),
            Is.EqualTo(WFLobbyJoinDecision.Denied));
    }

    /// <summary>Code entry accepts lowercase letters and surrounding whitespace.</summary>
    [TestCase("ABC234", "ABC234")]
    [TestCase("ABCD234", "ABCD234")]
    [TestCase("ABCD2345", "ABCD2345")]
    [TestCase("abcd2345", "ABCD2345")]
    [TestCase("  aBcD2345  ", "ABCD2345")]
    [TestCase("\tABCD2345\r\n", "ABCD2345")]
    [TestCase("Zyxw9876", "ZYXW9876")]
    public void CodesAcceptAsciiCaseAndOuterWhitespace(string input, string expected)
    {
        Assert.That(WFRoundSetupPolicy.TryNormalizeCode(input, out var normalized), Is.True);
        Assert.That(normalized, Is.EqualTo(expected));
    }

    /// <summary>Malformed codes and Unicode lookalikes never become valid credentials.</summary>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("     ")]
    [TestCase("ABC23")]
    [TestCase("ABCDE2345")]
    [TestCase("ABCD2340")]
    [TestCase("ABCD2341")]
    [TestCase("ABCD234I")]
    [TestCase("ABCD234O")]
    [TestCase("ABCD234i")]
    [TestCase("ABCD234o")]
    [TestCase("AB CD234")]
    [TestCase("AB-CD234")]
    [TestCase("AB\nCD234")]
    [TestCase("AB\0CD234")]
    [TestCase("ABCD234\u017f")]
    [TestCase("ABCD234\u212a")]
    [TestCase("ABCD234\uFF21")]
    [TestCase("ABCD234\u0391")]
    [TestCase("ABCD234\u0410")]
    [TestCase("ABCD234\u200B")]
    public void InvalidCodesReturnNoNormalizedValue(string input)
    {
        Assert.That(WFRoundSetupPolicy.TryNormalizeCode(input, out var normalized), Is.False);
        Assert.That(normalized, Is.Empty);
    }

    [TestCase("Rescue\u202Ecrew")]
    [TestCase("Rescue\u2066crew")]
    [TestCase("Rescue\u200Bcrew")]
    [TestCase("Rescue\u200Dcrew")]
    [TestCase("Rescue\uFEFFcrew")]
    [TestCase("Rescue\U000E0001crew")]
    [TestCase("Rescue\tcrew")]
    public void DescriptionsRejectInvisibleControls(string input)
    {
        Assert.That(WFRoundSetupPolicy.TryNormalizeDescription(input, out var description), Is.False);
        Assert.That(description, Is.Empty);
    }

    [Test]
    public void DescriptionsPreservePlainUnicodeAndParagraphs()
    {
        Assert.That(WFRoundSetupPolicy.TryNormalizeDescription("  Café rescue\r\nMedical crew welcome.  ", out var description), Is.True);
        Assert.That(description, Is.EqualTo("Café rescue\nMedical crew welcome."));
    }

    /// <summary>Every configured generation character is accepted by validation.</summary>
    [Test]
    public void CodeAlphabetCanGenerateAcceptedCodes()
    {
        foreach (var character in WFRoundSetupPolicy.CodeAlphabet)
        {
            var code = new string(character, WFRoundSetupPolicy.MaximumCodeLength);
            Assert.That(WFRoundSetupPolicy.TryNormalizeCode(code, out var normalized), Is.True);
            Assert.That(normalized, Is.EqualTo(code));
        }
    }
}
