#nullable enable
using System;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.IntegrationTests.Tests._WF.Wolfmed;
using Content.Server._WF.Newbie;
using Content.Server.Players.PlayTimeTracking;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.Newbie;
using Content.Shared.Examine;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Shuttles.Systems;
using Content.Shared.StatusIcon;
using NUnit.Framework;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Newbie;

/// <summary>
/// A new player's body and owned ship carry the newbie tag until they turn it off or pass the playtime threshold.
/// </summary>
[TestFixture]
[TestOf(typeof(WFNewbieTagSystem))]
public sealed class WFNewbieTagTest : GameTest
{
    private EntityUid _body;
    private EntityUid _ship;

    /// <summary>Refreshes the tags at once and reports what the body and ship carry.</summary>
    private async Task<(bool Body, bool Ship)> Refresh()
    {
        var result = (false, false);
        await Server.WaitPost(() =>
        {
            SEntMan.System<WFNewbieTagSystem>().RefreshAll();
            result = (SEntMan.HasComponent<WFNewbieComponent>(_body), SEntMan.HasComponent<WFNewbieShipComponent>(_ship));
        });
        await RunTicksSync(5);
        return result;
    }

    private async Task SetShowTag(bool value)
    {
        await OverrideCVar(Side.Client, WFNewbieCVars.ShowTag, value);
        await RunTicksSync(5);
        var seen = !value;
        await Server.WaitPost(() => seen = Server.ResolveDependency<INetConfigurationManager>()
            .GetClientCVar(ServerSession!.Channel, WFNewbieCVars.ShowTag));
        Assert.That(seen, Is.EqualTo(value), "the server never got the client's setting, so the test proves nothing.");
    }

    [Test]
    public async Task TagFollowsPlaytimeAndOptOutTest()
    {
        Assert.That(ServerSession, Is.Not.Null, "This test needs a connected pair.");
        var map = await WolfmedGameTest.CreateTestMap(Pair);
        var minds = SEntMan.System<SharedMindSystem>();
        _ship = map.Grid;

        await Server.WaitPost(() =>
        {
            var session = ServerSession!;
            minds.WipeMind(session.ContentData()?.Mind);
            _body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            minds.TransferTo(minds.CreateMind(session.UserId).Owner, _body);
            SEntMan.EnsureComponent<ShipOwnershipComponent>(_ship).OwnerUserId = session.UserId;
        });
        await RunTicksSync(30);
        Assert.That(ServerSession!.AttachedEntity, Is.EqualTo(_body), "the player did not attach to its body.");

        // A fresh player is tagged on foot and on their ship, and the client sees the mark.
        var fresh = await Refresh();
        string examine = string.Empty, label = string.Empty;
        await Server.WaitPost(() =>
        {
            examine = SEntMan.System<ExamineSystemShared>().GetExamineText(_body, _body).ToMarkup();
            label = SEntMan.System<SharedShuttleSystem>().GetIFFLabel(_ship) ?? string.Empty;
        });
        var clientSees = false;
        await Client.WaitPost(() => clientSees = CEntMan.HasComponent<WFNewbieComponent>(ToClientUid(_body)));

        Assert.Multiple(() =>
        {
            Assert.That(fresh.Body, Is.True, "a fresh player's body was not tagged.");
            Assert.That(fresh.Ship, Is.True, "a fresh player's ship was not tagged.");
            Assert.That(examine, Does.Contain("new around here"), "the examine line is missing.");
            Assert.That(label, Does.Contain("(L)"), "the ship's radar label has no newbie tag.");
            Assert.That(clientSees, Is.True, "the client never got the newbie component.");
        });

        // Turning the tag off removes it from both, and turning it back on restores it.
        await SetShowTag(false);
        var hidden = await Refresh();
        await SetShowTag(true);
        var shown = await Refresh();

        // A body the player leaves loses the tag.
        var left = false;
        await Server.WaitPost(() =>
        {
            minds.TransferTo(ServerSession!.ContentData()!.Mind!.Value, null);
            left = SEntMan.HasComponent<WFNewbieComponent>(_body);
        });
        await Server.WaitPost(() => minds.TransferTo(ServerSession!.ContentData()!.Mind!.Value, _body));
        await RunTicksSync(5);

        // Passing the threshold clears it.
        await Server.WaitPost(() => Server.ResolveDependency<PlayTimeTrackingManager>()
            .AddTimeToOverallPlaytime(ServerSession!, TimeSpan.FromHours(9)));
        var veteran = await Refresh();

        Assert.Multiple(() =>
        {
            Assert.That(hidden, Is.EqualTo((false, false)), "the tag stayed after the player turned it off.");
            Assert.That(shown, Is.EqualTo((true, true)), "the tag did not come back when turned on again.");
            Assert.That(left, Is.False, "a body kept the tag after its player left it.");
            Assert.That(veteran, Is.EqualTo((false, false)), "a player past the threshold is still tagged.");
        });
    }

    /// <summary>
    /// The mark is a Mod icon drawn before every other right-hand icon, so it sits above that column instead of
    /// taking a HUD slot and pushing the last job, security or health icon off the sprite.
    /// </summary>
    [Test]
    public async Task MarkTakesNoHudSlotTest()
    {
        await Server.WaitAssertion(() =>
        {
            var protos = Server.ProtoMan;
            var mark = protos.Index<SsdIconPrototype>("WFNewbieIcon");
            Assert.That(mark.Layer, Is.EqualTo(StatusIconLayer.Mod), "the mark takes a slot in the icon column.");

            foreach (var kind in protos.EnumeratePrototypeKinds())
            {
                if (!typeof(StatusIconPrototype).IsAssignableFrom(kind))
                    continue;

                foreach (var proto in protos.EnumeratePrototypes(kind))
                {
                    if (proto is not StatusIconPrototype icon || icon == mark)
                        continue;

                    if (icon.Layer != StatusIconLayer.Base ||
                        icon.LocationPreference == StatusIconLocationPreference.Left)
                    {
                        continue;
                    }

                    Assert.That(icon.Priority, Is.GreaterThan(mark.Priority),
                        $"{icon.ID} draws before the mark, so the mark lands on top of it.");
                }
            }
        });
    }
}
