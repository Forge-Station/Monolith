using System.Linq;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Vehicles.Npc;

/// <summary>
/// A vehicle with someone alive inside stands for them to NPCs: it takes the factions of its living occupants and counts
/// as a live target, so whoever is hostile to the crew attacks the vehicle. An NPC with a grudge against someone inside
/// holds it against the vehicle too. Empty, it belongs to nobody.
/// </summary>
public sealed class VehicleOccupantFactionSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NpcFactionSystem _factions = default!;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private TimeSpan _next;
    private readonly HashSet<ProtoId<NpcFactionPrototype>> _wanted = new();

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _next)
            return;

        _next = _timing.CurTime + Interval;

        var query = EntityQueryEnumerator<VehicleInteriorComponent>();
        while (query.MoveNext(out var vehicle, out var interior))
        {
            _wanted.Clear();
            var occupied = false;
            foreach (var passenger in interior.Passengers)
            {
                if (!TryComp<MobStateComponent>(passenger, out var mobState) || _mobState.IsDead(passenger, mobState))
                    continue;

                occupied = true;
                CarryGrudges(passenger, vehicle);
                if (TryComp<NpcFactionMemberComponent>(passenger, out var member))
                {
                    var factions = member.Factions;
                    _wanted.UnionWith(factions);
                }
            }

            if (occupied)
                EnsureComp<WFOccupiedVehicleComponent>(vehicle);
            else
                RemComp<WFOccupiedVehicleComponent>(vehicle);

            var current = CompOrNull<NpcFactionMemberComponent>(vehicle);
            if (current == null && _wanted.Count == 0)
                continue;

            var held = current?.Factions;
            if (held != null && held.SetEquals(_wanted))
                continue;

            _factions.ClearFactions((vehicle, current), false);
            _factions.AddFactions((vehicle, current), _wanted);
        }
    }

    /// <summary>Whoever is out to get the passenger is out to get the vehicle he's sitting in.</summary>
    private void CarryGrudges(EntityUid passenger, EntityUid vehicle)
    {
        if (!TryComp<FactionExceptionTrackerComponent>(passenger, out var tracker))
            return;
        foreach (var npc in tracker.Entities)
        {
            var hostiles = _factions.GetHostiles(npc).ToList();
            if (hostiles.Contains(passenger) && !hostiles.Contains(vehicle))
                _factions.AggroEntity(npc, vehicle);
        }
    }
}

/// <summary>On a vehicle with someone alive inside: NPCs treat it as a live target (see <see cref="VehicleOccupantFactionSystem"/>).</summary>
[RegisterComponent]
public sealed partial class WFOccupiedVehicleComponent : Component;
