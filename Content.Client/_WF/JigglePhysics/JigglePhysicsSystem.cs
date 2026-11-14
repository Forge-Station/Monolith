using System.Numerics;
using Content.Client.DisplacementMap;
using Content.Shared._WF.JigglePhysics;
using Content.Shared.Camera;
using Content.Shared.DisplacementMap;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Reflection;

namespace Content.Client._WF.JigglePhysics;

/// <summary>
/// Client wobble state for a <see cref="JigglePhysicsComponent"/>, added and removed with it.
/// </summary>
[RegisterComponent, Access(typeof(JigglePhysicsSystem))]
public sealed partial class JigglePhysicsVisualsComponent : Component
{
    [ViewVariables]
    public Vector2 Jiggle;

    [ViewVariables]
    public Vector2 Slap;

    [ViewVariables]
    public Vector2 LastVelocity;

    /// <summary>
    /// The displacement state in use; zero is rest.
    /// </summary>
    [ViewVariables]
    public int DisplacementNumber;

    /// <summary>
    /// Map keys of the displacement layers added.
    /// </summary>
    [ViewVariables]
    public List<string> DisplacementKeys = new();

    /// <summary>
    /// Shaders the wobbling layers had before the displacement shader replaced them, by their jiggle key.
    /// </summary>
    [ViewVariables]
    public Dictionary<string, (ShaderInstance? Shader, string? Prototype, bool Unshaded)> OldShaders = new();
}

/// <summary>
/// Jiggle physics, ported from Trauma Station: a spring driven by the entity's acceleration picks a displacement map for
/// each wobbling layer.
/// </summary>
public sealed partial class JigglePhysicsSystem : EntitySystem
{
    [Dependency] private IReflectionManager _reflection = default!;
    [Dependency] private DisplacementMapSystem _displacement = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private const string KeyPrefix = "jiggle-";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<JigglePhysicsComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<JigglePhysicsComponent, ComponentRemove>(OnRemove);
    }

    private void OnStartup(Entity<JigglePhysicsComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        var data = new DisplacementData();
        data.SizeMaps[32] = new PrototypeLayerData
        {
            RsiPath = ent.Comp.DisplacementsRsi.ToString(),
            State = ent.Comp.DisplacementPrefix + "0",
        };

        var vis = EnsureComp<JigglePhysicsVisualsComponent>(ent);
        var revealed = new HashSet<string>();
        foreach (var keyName in ent.Comp.Layers)
        {
            int index;
            if (_reflection.TryParseEnumReference(keyName, out var keyEnum))
            {
                if (!_sprite.LayerMapTryGet((ent, sprite), keyEnum, out index, false))
                    continue;
            }
            else if (!_sprite.LayerMapTryGet((ent, sprite), keyName, out index, false))
            {
                continue;
            }

            // The displacement shader finds its layer by a string key, so give enum-keyed layers one.
            var key = KeyPrefix + vis.DisplacementKeys.Count;
            _sprite.LayerMapSet((ent, sprite), key, index);
            if (_sprite.TryGetLayer((ent, sprite), index, out var source, false))
                vis.OldShaders[key] = (source.Shader, source.ShaderPrototype?.Id, source.ShaderPrototype == SpriteSystem.UnshadedId);

            if (_displacement.TryAddDisplacement(data, sprite, index, key, revealed))
                vis.DisplacementKeys.Add($"{key}-displacement");
        }
    }

    private void OnRemove(Entity<JigglePhysicsComponent> ent, ref ComponentRemove args)
    {
        if (TerminatingOrDeleted(ent) || !TryComp<JigglePhysicsVisualsComponent>(ent, out var vis))
            return;

        if (TryComp<SpriteComponent>(ent, out var sprite))
        {
            foreach (var key in vis.DisplacementKeys)
            {
                if (_sprite.LayerMapTryGet((ent, sprite), key, out var index, false))
                    _sprite.RemoveLayer((ent, sprite), index);
            }

            foreach (var (key, (shader, prototype, unshaded)) in vis.OldShaders)
            {
                if (!_sprite.LayerMapTryGet((ent, sprite), key, out var index, false))
                    continue;

                if (unshaded)
                    sprite.LayerSetShader(index, SpriteSystem.UnshadedId.Id);
                else
                    sprite.LayerSetShader(index, shader, prototype);
            }
        }

        RemComp<JigglePhysicsVisualsComponent>(ent);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        if (frameTime <= 0f)
            return;

        var query = EntityQueryEnumerator<JigglePhysicsComponent, JigglePhysicsVisualsComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out var comp, out var vis, out var physics))
        {
            Jiggle(uid, comp, vis, physics, frameTime);
        }
    }

    private void Jiggle(EntityUid uid, JigglePhysicsComponent comp, JigglePhysicsVisualsComponent vis,
        PhysicsComponent physics, float dt)
    {
        var velocity = physics.LinearVelocity;
        var acceleration = (velocity - vis.LastVelocity) / dt;
        var force = vis.Jiggle * comp.Springiness + vis.Slap * comp.Damping + acceleration * comp.InertiaScale;

        vis.Slap -= force * dt;
        ClampLength(ref vis.Slap, comp.SlapLimit);

        var delta = vis.Slap;
        if (TryComp<CameraRecoilComponent>(uid, out var recoil))
            delta += recoil.CurrentKick;

        vis.Jiggle += delta * dt;
        ClampLength(ref vis.Jiggle, comp.JiggleLimit);
        vis.LastVelocity = velocity;

        // One axis of displacements: the wobble's length, signed by which way it leans.
        var direction = vis.Jiggle.X >= 0f ? -1f : 1f;
        var number = (int) MathF.Round(vis.Jiggle.Length() * direction * comp.DisplacementCount / comp.JiggleLimit);
        if (number == vis.DisplacementNumber || !TryComp<SpriteComponent>(uid, out var sprite))
            return;

        vis.DisplacementNumber = number;
        var state = comp.DisplacementPrefix + number;
        foreach (var key in vis.DisplacementKeys)
        {
            if (_sprite.TryGetLayer((uid, sprite), key, out var layer, false))
                _sprite.LayerSetRsiState(layer, state);
        }
    }

    private static void ClampLength(ref Vector2 vector, float limit)
    {
        var lengthSquared = vector.LengthSquared();
        if (lengthSquared > limit * limit)
            vector *= limit / MathF.Sqrt(lengthSquared);
    }
}
