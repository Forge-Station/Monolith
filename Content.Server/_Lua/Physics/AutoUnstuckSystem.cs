// LuaWorld - This file is licensed under AGPLv3
// Copyright (c) 2025 LuaWorld
// See AGPLv3.txt for details.

using System.Numerics;
using JetBrains.Annotations;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Content.Shared.Humanoid;

namespace Content.Server._Lua.Physics;

[UsedImplicitly]
public sealed class AutoUnstuckSystem : EntitySystem
{
    private static readonly Vector2[] StuckOffsets =
    {
        new(2f, 0f),
        new(-2f, 0f),
        new(0f, 2f),
        new(0f, -2f),
    };

    // Stuck threshold is 15s, so polling at ~2 Hz is plenty.
    private const float CheckInterval = 0.5f;
    private const float StuckThreshold = 15f;

    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    private readonly Dictionary<EntityUid, float> _stuckTime = new();
    private EntityQuery<TransformComponent> _xformQuery;
    private readonly List<EntityUid> _toClear = new();
    private float _checkAccum;

    public override void Initialize()
    {
        base.Initialize();
        _xformQuery = GetEntityQuery<TransformComponent>();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _checkAccum += frameTime;
        if (_checkAccum < CheckInterval)
            return;

        var elapsed = _checkAccum;
        _checkAccum = 0f;

        _toClear.Clear();

        var query = EntityQueryEnumerator<HumanoidAppearanceComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out _, out var body))
        {
            if (!body.Awake) continue;
            if (body.BodyType == BodyType.Static || !body.CanCollide) continue;
            if (IsPaused(uid)) continue;

            var hasStaticHardContact = false;
            var contacts = _physics.GetContacts(uid);
            while (contacts.MoveNext(out var contact))
            {
                if (!contact.IsTouching || !contact.Hard) continue;
                var otherBody = contact.OtherBody(uid);
                if (otherBody.BodyType != BodyType.Static) continue;
                hasStaticHardContact = true;
                break;
            }
            if (!hasStaticHardContact)
            {
                _toClear.Add(uid);
                continue;
            }
            if (_stuckTime.TryGetValue(uid, out var t)) _stuckTime[uid] = t + elapsed;
            else _stuckTime[uid] = elapsed;
            if (_stuckTime[uid] < StuckThreshold) continue;
            if (_xformQuery.TryGetComponent(uid, out var xform))
            {
                var offset = _random.Pick(StuckOffsets);
                _physics.SetCanCollide(uid, false, body: body);
                _xform.SetCoordinates(uid, xform, xform.Coordinates.Offset(offset));
                _physics.SetCanCollide(uid, true, body: body);
                _physics.SetLinearVelocity(uid, Vector2.Zero, body: body);
                _physics.WakeBody(uid, body: body);
            }
            _toClear.Add(uid);
        }
        foreach (var uid in _toClear)
        {
            _stuckTime.Remove(uid);
        }
    }
}
