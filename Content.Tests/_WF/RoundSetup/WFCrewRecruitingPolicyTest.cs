using Content.Shared._WF.RoundSetup;
using NUnit.Framework;

namespace Content.Tests._WF.RoundSetup;

/// <summary>Covers when a crew shows as recruiting, posting text rules and which crews take live members.</summary>
[TestFixture]
public sealed class WFCrewRecruitingPolicyTest
{
    [TestCase(true, WFLobbyAccess.Public, false, false, 1, 1, true)]
    [TestCase(true, WFLobbyAccess.Requests, true, true, 3, 2, true)]
    [TestCase(false, WFLobbyAccess.Public, false, false, 1, 1, false)]
    [TestCase(true, WFLobbyAccess.Code, false, false, 1, 1, false)]
    [TestCase(true, WFLobbyAccess.Invite, false, false, 1, 1, false)]
    [TestCase(true, WFLobbyAccess.Closed, false, false, 1, 1, false)]
    [TestCase(true, WFLobbyAccess.Public, true, false, 1, 1, false)]
    [TestCase(true, WFLobbyAccess.Public, false, false, 1, 0, false)]
    [TestCase(true, WFLobbyAccess.Public, false, false, WFRoundSetupPolicy.MaximumMembers, 1, false)]
    public void RecruitingNeedsAnOpenSlot(bool flag, WFLobbyAccess access, bool started, bool lateJoin, int members, int openings, bool expected)
    {
        Assert.That(WFRoundSetupPolicy.ShowsRecruiting(flag, access, started, lateJoin, members, openings), Is.EqualTo(expected));
    }

    [Test]
    public void PostingsAreOneBoundedLine()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WFRoundSetupPolicy.TryNormalizePosting("  Need   two\nhands  ", out var posting), Is.True);
            Assert.That(posting, Is.EqualTo("Need two hands"));
            Assert.That(WFRoundSetupPolicy.TryNormalizePosting(new string('a', WFRoundSetupPolicy.MaximumPostingLength), out _), Is.True);
            Assert.That(WFRoundSetupPolicy.TryNormalizePosting(new string('a', WFRoundSetupPolicy.MaximumPostingLength + 1), out _), Is.False);
            Assert.That(WFRoundSetupPolicy.TryNormalizePosting("   ", out _), Is.False);
            Assert.That(WFRoundSetupPolicy.TryNormalizePosting(null, out _), Is.False);
            Assert.That(WFRoundSetupPolicy.TryNormalizePosting("bad​text", out _), Is.False);
        });
    }

    [TestCase(WFRoundScenario.Formed, false, true)]
    [TestCase(WFRoundScenario.Formed, true, true)]
    [TestCase(WFRoundScenario.Ship, true, true)]
    [TestCase(WFRoundScenario.Ship, false, false)]
    [TestCase(WFRoundScenario.FactionShip, true, false)]
    [TestCase(WFRoundScenario.Crashlanded, true, false)]
    [TestCase(WFRoundScenario.Doomed, true, false)]
    public void LiveMembersJoinFormedAndLaunchedShipCrews(WFRoundScenario scenario, bool started, bool expected)
    {
        Assert.That(WFRoundSetupPolicy.AllowsLiveMembers(scenario, started), Is.EqualTo(expected));
    }
}
