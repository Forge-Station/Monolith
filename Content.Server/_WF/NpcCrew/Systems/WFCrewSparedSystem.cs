using Content.Server._WF.NpcCrew.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Mono.Company;
using Content.Shared.Cuffs.Components;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Systems;
using Content.Shared.Shuttles.Components;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>
/// Player vessels an NPC crew takes for its own side: one showing the crew's faction on its IFF, or one with a free
/// player of that faction aboard. The crew never warns, hunts or zones them, and answers their fire only once it runs
/// past <see cref="WFCrewAlertSystem.OwnFactionStrayHits"/>.
/// </summary>
public sealed partial class WFCrewSparedSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private readonly Dictionary<EntityUid, HashSet<string>> _aboard = new();
    private GameTick _aboardTick;
    private readonly HashSet<EntityUid> _manned = new();
    private GameTick _mannedTick;

    /// <summary>Whether the crew of <paramref name="grid"/> takes the player vessel <paramref name="target"/> for its own side.</summary>
    public bool Spared(EntityUid grid, EntityUid target)
    {
        // NPC crews settle their own fights; this only shields vessels players fly.
        if (grid == target || HasComp<WFCrewShipProfileComponent>(target))
            return false;

        var own = Company(grid);
        if (own.Length == 0)
            return false;

        // Its own colours on the IFF pass, whoever is really aboard.
        if (!IffMasked(target) && WFCompanyFamily.Same(own, Company(target)))
            return true;

        // Running dark or under another flag, the free crew aboard decide; prisoners don't count.
        foreach (var company in Aboard(target))
        {
            if (WFCompanyFamily.Same(own, company))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the crew of <paramref name="grid"/> holds fire on <paramref name="target"/> because of who is on it or
    /// alongside it: only a <see cref="WFCrewProfilePrototype.SparesCrewedShips"/> crew does.
    /// </summary>
    public bool HoldsFireOn(EntityUid grid, EntityUid target)
    {
        if (grid == target || !TryComp<WFCrewShipProfileComponent>(grid, out var own)
            || !_prototypes.TryIndex(own.Profile, out var profile) || !profile.SparesCrewedShips)
            return false;

        if (Manned(target))
            return true;

        foreach (var dock in _docking.GetDocks(target))
        {
            if (dock.Comp.DockedWith is { } mate && Transform(mate).GridUid is { } alongside && Manned(alongside))
                return true;
        }

        return false;
    }

    /// <summary>Whether a living player stands on a grid, prisoners included; gathered once a tick.</summary>
    private bool Manned(EntityUid grid)
    {
        if (_mannedTick != _timing.CurTick)
        {
            _mannedTick = _timing.CurTick;
            _manned.Clear();
            var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
            while (players.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid is { } on && !HasComp<GhostComponent>(uid) && !_mobs.IsDead(uid))
                    _manned.Add(on);
            }
        }

        return _manned.Contains(grid);
    }

    private string Company(EntityUid uid)
    {
        return CompOrNull<CompanyComponent>(uid)?.CompanyName.Id is { } id && id != "None" ? id : string.Empty;
    }

    private bool IffMasked(EntityUid grid)
    {
        return TryComp<IFFComponent>(grid, out var iff) && (iff.Flags & (IFFFlags.HideLabel | IFFFlags.Hide)) != 0;
    }

    /// <summary>The companies of the living, unrestrained players on a grid, gathered once a tick.</summary>
    private IReadOnlyCollection<string> Aboard(EntityUid grid)
    {
        if (_aboardTick != _timing.CurTick)
        {
            _aboardTick = _timing.CurTick;
            _aboard.Clear();
            var players = EntityQueryEnumerator<ActorComponent, CompanyComponent, TransformComponent>();
            while (players.MoveNext(out var uid, out _, out var company, out var xform))
            {
                if (xform.GridUid is not { } on || company.CompanyName.Id is not { Length: > 0 } id || id == "None"
                    || _mobs.IsDead(uid) || CompOrNull<CuffableComponent>(uid)?.CuffedHandCount > 0)
                    continue;
                if (!_aboard.TryGetValue(on, out var set))
                    _aboard[on] = set = new HashSet<string>();
                set.Add(id);
            }
        }

        return _aboard.TryGetValue(grid, out var found) ? found : Array.Empty<string>();
    }
}
