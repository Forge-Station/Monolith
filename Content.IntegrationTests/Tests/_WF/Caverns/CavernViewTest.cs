#nullable enable
using System.Linq;
using System.Numerics;
using Content.Client._WF.Caverns;
using Content.Client.Clickable;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.Caverns;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Caverns;

/// <summary>
/// What a client standing by a mouth gets to see the cavern through it: the cavern map under the ground, no sky drawn
/// through the hole or in the cavern, and shades that click anywhere on their tile though their middle is clear.
/// </summary>
[TestFixture]
[TestOf(typeof(WFCavernViewSystem))]
public sealed class CavernViewTest
{
    private const string Surface = "WFSurfaceAsclepiu";

    /// <summary>
    /// A client on a mouth's lip finds the cavern under the ground for the z-level renderer, and neither the cavern nor
    /// the ground around the hole draws the sky, while ground far from any hole still does.
    /// </summary>
    [Test]
    public async Task ClientSeesCavernUnderMouth()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;

        await EnableCaverns(pair);
        await DisableClaims(pair);
        var world = await BuildWorld(pair, Surface);
        var gate = await Gate(pair, world);

        await PlanetFixture.AttachViewer(pair, world.Ground, TileCentre(gate.ClimbTile));
        await pair.RunTicksSync(30);

        var groundNet = server.EntMan.GetNetEntity(world.Ground);
        var cavernNet = server.EntMan.GetNetEntity(world.Cavern);

        await client.WaitAssertion(() =>
        {
            var view = client.System<WFCavernViewSystem>();
            var ground = client.EntMan.GetEntity(groundNet);
            var cavern = client.EntMan.GetEntity(cavernNet);
            var aroundHole = new Box2(gate.Min - new Vector2i(2, 2), gate.Max + new Vector2i(3, 3));
            var farAway = aroundHole.Translated(new Vector2(200, 0));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(view.TryGetCavernBelow(ground, out var below), Is.True,
                    "The client beside a mouth has no cavern under the ground to draw.");
                Assert.That(below, Is.EqualTo(cavern), "The client found another map under the ground.");
                Assert.That(view.HidesSky(cavern, aroundHole), Is.True, "The cavern draws the sky.");
                Assert.That(view.HidesSky(ground, aroundHole), Is.True, "The ground draws the sky through a mouth.");
                Assert.That(view.HidesSky(ground, farAway), Is.False, "The ground hides the sky with no mouth in view.");
            }
        });

        await Teardown(pair, world);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The shade of a hole tile with hole all round, whose art is clear from edge to edge, takes a click anywhere on its
    /// tile, and not a tile away.
    /// </summary>
    [Test]
    public async Task ShadeClicksAcrossItsTile()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;

        await EnableCaverns(pair);
        await DisableClaims(pair);
        var world = await BuildWorld(pair, Surface);
        var gate = await Gate(pair, world);

        await PlanetFixture.AttachViewer(pair, world.Ground, TileCentre(gate.ClimbTile));
        await pair.RunTicksSync(30);

        // Every corner of this tile has hole all round, so none of its pieces draws anything over it.
        var interiors = gate.Hole.Where(tile =>
            Enumerable.Range(-1, 3).All(dx => Enumerable.Range(-1, 3).All(dy => gate.Hole.Contains(tile + new Vector2i(dx, dy))))).ToList();
        Assert.That(interiors, Is.Not.Empty, $"Precondition: the {Surface} gate has no hole tile with hole all round.");
        var interior = interiors.OrderBy(tile => tile.X).ThenBy(tile => tile.Y).First();

        var shadeNet = NetEntity.Invalid;
        await server.WaitPost(() =>
        {
            var ground = server.EntMan.GetComponent<WFCavernGroundComponent>(world.Ground);
            shadeNet = server.EntMan.GetNetEntity(ground.Shades[interior]);
        });

        await client.WaitAssertion(() =>
        {
            var shade = client.EntMan.GetEntity(shadeNet);
            var sprite = client.EntMan.GetComponent<SpriteComponent>(shade);
            var clicks = client.System<ClickableSystem>();
            var eye = client.ResolveDependency<IEyeManager>().CurrentEye;
            var centre = client.System<SharedTransformSystem>().GetWorldPosition(shade);

            eye.Rotation = 0;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(clicks.CheckClick((shade, null, sprite, null), centre, eye, out _, out _, out _), Is.True,
                    "A click in the clear middle of a hole tile misses its shade.");
                Assert.That(clicks.CheckClick((shade, null, sprite, null), centre + new Vector2(0.45f, -0.45f), eye, out _, out _, out _),
                    Is.True, "A click near the corner of a clear hole tile misses its shade.");
                Assert.That(clicks.CheckClick((shade, null, sprite, null), centre + new Vector2(0f, 1.6f), eye, out _, out _, out _),
                    Is.False, "A click a tile away hits the shade.");
            }
        });

        await Teardown(pair, world);
        await pair.CleanReturnAsync();
    }
}
