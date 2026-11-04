#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server.GameTicking;
using Content.Shared.Antag;
using Content.Shared.Antag.Components;
using Content.Shared.GameTicking;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.IntegrationTests.Tests.GameRules;

// Once upon a time, players in the lobby weren't ever considered eligible for antag roles.
// Lets not let that happen again.
[TestFixture]
public sealed class AntagPreferenceTest
{
    [Test]
    [Ignore("Antag role selection isn't used on Frontier.")] // Frontier
    public async Task TestLobbyPlayersValid()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            DummyTicker = false,
            Connected = true,
            InLobby = true
        });

        var server = pair.Server;
        var client = pair.Client;
        var ticker = server.System<ServerGameTicker>();
        var sys = server.System<AntagSelectionSystem>();

        // Initially in the lobby
        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PreRoundLobby));
        Assert.That(client.AttachedEntity, Is.Null);
        Assert.That(ticker.PlayerGameStatuses[client.User!.Value], Is.EqualTo(PlayerGameStatus.NotReadyToPlay));

        EntityUid uid = default;
        await server.WaitPost(() => uid = server.EntMan.Spawn("Traitor"));
        var rule = new Entity<AntagSelectionComponent>(uid, server.EntMan.GetComponent<AntagSelectionComponent>(uid));
        var def = server.ProtoMan.Index(rule.Comp.Antags.Single().Proto);

        // Session and entity checks ignore antag preferences. CanBeAntag is what applies them.
        Assert.That(sys.IsSessionValid(pair.Player!, rule, def), Is.True);
        Assert.That(sys.IsEntityValid(client.AttachedEntity, def), Is.True);
        Assert.That(sys.CanBeAntag(pair.Player!, rule, def), Is.False);

        // Opt into the traitor role.
        await pair.SetAntagPreference("Traitor", true);

        Assert.That(sys.IsSessionValid(pair.Player!, rule, def), Is.True);
        Assert.That(sys.IsEntityValid(client.AttachedEntity, def), Is.True);
        Assert.That(sys.CanBeAntag(pair.Player!, rule, def), Is.True);

        // opt back out
        await pair.SetAntagPreference("Traitor", false);

        Assert.That(sys.IsSessionValid(pair.Player!, rule, def), Is.True);
        Assert.That(sys.IsEntityValid(client.AttachedEntity, def), Is.True);
        Assert.That(sys.CanBeAntag(pair.Player!, rule, def), Is.False);

        await server.WaitPost(() => server.EntMan.DeleteEntity(uid));
        await pair.CleanReturnAsync();
    }
}
