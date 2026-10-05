#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Content.Client.Shuttles.UI;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Planets;
using Content.Shared._WF.Planets;
using Content.Shared._CE.ZLevels.Core.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

[TestFixture]
public sealed class PlanetRadarDrawingTest
{
    [Test]
    public async Task FervidusTerrainReachesTheDrawingHandle()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        await EnableFeature(pair);
        var layers = new List<EntityUid>();
        var netOrbit = default(NetEntity);
        var netAir = default(NetEntity);
        var netLayers = new List<NetEntity>();

        await pair.Server.WaitAssertion(() =>
        {
            var server = pair.Server;
            Assert.That(server.System<WFPlanetRegistrySystem>().TryGetSurface("PlanetFervidus", out var surface), Is.True);
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface!, Vector2.Zero, "Fervidus", null);
            layers.AddRange(server.EntMan.GetComponent<WFPlanetNetworkComponent>(network!.Value).Layers);
            netOrbit = server.EntMan.GetNetEntity(layers[^1]);
            netAir = server.EntMan.GetNetEntity(layers[^2]);
            foreach (var layer in layers)
            {
                netLayers.Add(server.EntMan.GetNetEntity(layer));
                server.System<Robust.Server.GameStates.PvsOverrideSystem>().AddGlobalOverride(layer);
            }

        });
        await AttachViewer(pair, layers[^1], Vector2.Zero);
        await pair.RunSeconds(2);
        await pair.Client.WaitAssertion(() =>
        {
            var orbit = pair.Client.EntMan.GetEntity(netOrbit);
            Assert.That(pair.Client.EntMan.GetComponent<WFOrbitLayerComponent>(orbit).RadarLayers, Is.Not.Empty);
            using var radar = new ShuttleNavControl();
            radar.Measure(new Vector2(800));
            radar.Arrange(new UIBox2(Vector2.Zero, new Vector2(800)));
            using var handle = new TerrainHandle();
            var matrix = Matrix3x2.CreateScale(0.3f, -0.3f) * Matrix3x2.CreateTranslation(400, 400);
            typeof(ShuttleNavControl).GetMethod("DrawWfTerrain", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(radar, new object?[] { handle, matrix, orbit });
            Assert.That(handle.Vertices, Is.GreaterThan(0), "Terrain returned without submitting geometry.");
            Assert.That(handle.VisibleVertices, Is.GreaterThan(0), "Terrain geometry is outside the viewport.");
            Assert.That(handle.Colours.Count, Is.GreaterThan(1), "Basalt alone hides the planet: lava and formations must be sampled too.");
            Assert.That(handle.Brightness, Is.GreaterThan(0.02f), "Terrain is effectively black after colour conversion.");
            // A fresh radar must draw from every altitude, without cached orbit geometry.
            foreach (var netMap in netLayers)
            {
                using var descendedRadar = new ShuttleNavControl();
                descendedRadar.Measure(new Vector2(800));
                descendedRadar.Arrange(new UIBox2(Vector2.Zero, new Vector2(800)));
                using var descendedHandle = new TerrainHandle();
                typeof(ShuttleNavControl).GetMethod("DrawWfTerrain", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(descendedRadar, new object?[] { descendedHandle, matrix, pair.Client.EntMan.GetEntity(netMap) });
                Assert.That(descendedHandle.VisibleVertices, Is.GreaterThan(0), $"No terrain on layer {netMap}.");
                Assert.That(descendedHandle.Colours.Count, Is.GreaterThan(1));
            }
            // A client-side transit map, so the server cannot delete the empty gap before it renders.
            var gap = pair.Client.EntMan.GetEntity(netAir);
            var transit = pair.Client.EntMan.AddComponent<CEZTransitMapComponent>(gap);
            transit.LowerMap = pair.Client.EntMan.GetEntity(netLayers[0]);
            transit.UpperMap = pair.Client.EntMan.GetEntity(netLayers[1]);
            pair.Client.EntMan.RemoveComponent<WFPlanetLayerComponent>(gap);
            using var gapHandle = new TerrainHandle();
            typeof(ShuttleNavControl).GetMethod("DrawWfTerrain", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(radar, new object?[] { gapHandle, matrix, gap });
            Assert.That(gapHandle.VisibleVertices, Is.GreaterThan(0), "Transit gap lost its planet recipe.");
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var air = pair.Client.EntMan.GetEntity(netAir);
            radar.SetMatrix(new Robust.Shared.Map.EntityCoordinates(air, Vector2.Zero), Angle.Zero);
            typeof(ShuttleNavControl).GetField("_wasPanned", flags)!.SetValue(radar, true);
            radar.Offset = new Vector2(100);
            var arrival = new Robust.Shared.Map.EntityCoordinates(orbit, Vector2.Zero);
            radar.SetMatrix(arrival, Angle.Zero);
            Assert.That(typeof(ShuttleNavControl).GetField("_coordinates", flags)!.GetValue(radar), Is.EqualTo(arrival), "Panning must not leave radar on a previous map after travel.");
            Assert.That(radar.Offset, Is.EqualTo(Vector2.Zero));
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    private sealed class TerrainHandle() : DrawingHandleScreen(null!)
    {
        public readonly HashSet<Color> Colours = new();
        public int Vertices;
        public int VisibleVertices;
        public float Brightness;
        public override void DrawPrimitives(DrawPrimitiveTopology topology, Texture texture, ReadOnlySpan<DrawVertexUV2DColor> vertices)
        {
            Vertices += vertices.Length;
            foreach (var vertex in vertices)
            {
                if (vertex.Position.X is >= 0 and <= 800 && vertex.Position.Y is >= 0 and <= 800)
                    VisibleVertices++;
                Brightness = MathF.Max(Brightness, MathF.Max(vertex.Color.R, MathF.Max(vertex.Color.G, vertex.Color.B)));
            }
        }
        public override void DrawPrimitives(DrawPrimitiveTopology topology, Texture texture, ReadOnlySpan<ushort> indices, ReadOnlySpan<DrawVertexUV2DColor> vertices) => DrawPrimitives(topology, texture, vertices);
        private Matrix3x2 _transform = Matrix3x2.Identity;
        public override void SetTransform(in Matrix3x2 matrix) => _transform = matrix;
        public override Matrix3x2 GetTransform() => _transform;
        public override void UseShader(ShaderInstance? shader) { }
        public override ShaderInstance? GetShader() => null;
        public override void DrawLine(Vector2 from, Vector2 to, Color color) { }
        public override void RenderInRenderTarget(IRenderTarget target, Action action, Color? clearColor) => action();
        public override void DrawRect(UIBox2 rect, Color color, bool filled = true)
        {
            Colours.Add(color);
            var converted = Color.FromSrgb(color);
            var quad = new[]
            {
                new DrawVertexUV2DColor(Vector2.Transform(rect.TopLeft, _transform), converted),
                new DrawVertexUV2DColor(Vector2.Transform(rect.BottomRight, _transform), converted),
            };
            DrawPrimitives(DrawPrimitiveTopology.TriangleList, null!, quad);
        }
        public override void DrawTextureRectRegion(Texture texture, UIBox2 rect, UIBox2? subRegion = null, Color? modulate = null) { }
        public override void DrawEntity(EntityUid entity, Vector2 position, Vector2 scale, Angle? worldRot, Angle eyeRotation = default, Direction? overrideDirection = null, SpriteComponent? sprite = null, TransformComponent? xform = null, SharedTransformSystem? xformSystem = null) { }
    }
}
