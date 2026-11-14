using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Systems;
using Content.Server.Destructible;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared.Atmos;
using Content.Shared.Damage;
using Content.Shared.Destructible;
using Content.Shared.Examine;

namespace Content.Server._WF.Wolfmed.Autodoc;

/// <summary>The pod's own breathable atmosphere for its occupant while it is sealed.</summary>
// Built like sealed entity storage: a marker on the occupant answers the respirator's inhale and exhale and the
// atmosphere's exposure query with the pod's mix while sealed (occupant under the lid, power, intact hull, not
// emagged); otherwise the events fall through to the tile. The mix is reset to breathable air every time it is
// handed out, which scrubs the occupant's breath and keeps a burning or freezing room off them.
public sealed partial class AutodocSystem
{
    [Dependency] private DestructibleSystem _destructible = default!;

    private void InitializeAtmosphere()
    {
        SubscribeLocalEvent<WolfmedAutodocOccupantComponent, InhaleLocationEvent>(OnOccupantInhale);
        SubscribeLocalEvent<WolfmedAutodocOccupantComponent, ExhaleLocationEvent>(OnOccupantExhale);
        SubscribeLocalEvent<WolfmedAutodocOccupantComponent, AtmosExposedGetAirEvent>(OnOccupantExposed);
        SubscribeLocalEvent<WolfmedAutodocAtmosphereComponent, BreakageEventArgs>(OnHullBreached);
        SubscribeLocalEvent<WolfmedAutodocAtmosphereComponent, DamageChangedEvent>(OnHullDamageChanged);
    }

    /// <summary>Whether the pod keeps its own air around the occupant, and if not the worst reason why.</summary>
    public WolfmedAutodocSeal GetSeal(Entity<AutodocComponent> ent)
    {
        if (!TryComp(ent, out WolfmedAutodocAtmosphereComponent? atmosphere))
            return WolfmedAutodocSeal.Open;

        if (atmosphere.Broken)
            return WolfmedAutodocSeal.Breached;

        if (GetOccupant(ent) == null)
            return WolfmedAutodocSeal.Open;

        if (!IsPowered(ent))
            return WolfmedAutodocSeal.Unpowered;

        // DECISIONS "Autodoc": emag is a threat, so an emagged pod vents its patient to the room on purpose.
        return IsEmagged(ent) ? WolfmedAutodocSeal.Vented : WolfmedAutodocSeal.Sealed;
    }

    /// <summary>The body is lying in a pod that is sealed around it right now.</summary>
    public bool IsSealedIn(EntityUid body) =>
        TryComp(body, out WolfmedAutodocOccupantComponent? marker) && SealedAir((body, marker)) != null;

    /// <summary>A body went into the pod: from now on it asks the pod for its air.</summary>
    private void AtmosphereOccupantEntered(Entity<AutodocComponent> ent, EntityUid body)
    {
        if (TerminatingOrDeleted(body) || !HasComp<WolfmedAutodocAtmosphereComponent>(ent))
            return;

        EnsureComp<WolfmedAutodocOccupantComponent>(body).Pod = ent.Owner;
    }

    /// <summary>The body left the pod: back to the room's air.</summary>
    private void AtmosphereOccupantLeft(Entity<AutodocComponent> ent, EntityUid body)
    {
        if (TerminatingOrDeleted(body) ||
            !TryComp(body, out WolfmedAutodocOccupantComponent? marker) ||
            marker.Pod != ent.Owner)
            return;

        RemComp(body, marker);
    }

    /// <summary>
    /// The pod's air, freshly regenerated, when the pod holding this occupant is sealed around them; null otherwise,
    /// which leaves the caller on the tile.
    /// </summary>
    private GasMixture? SealedAir(Entity<WolfmedAutodocOccupantComponent> occupant)
    {
        var uid = occupant.Comp.Pod;
        if (!TryComp(uid, out AutodocComponent? autodoc) ||
            !TryComp(uid, out WolfmedAutodocAtmosphereComponent? atmosphere))
            return null;

        var pod = new Entity<AutodocComponent>(uid, autodoc);
        if (GetOccupant(pod) != occupant.Owner || GetSeal(pod) != WolfmedAutodocSeal.Sealed)
            return null;

        Regenerate(atmosphere);
        return atmosphere.Air;
    }

    /// <summary>Puts the pod's air back to its fixed mix, which also scrubs whatever was exhaled into it.</summary>
    private static void Regenerate(WolfmedAutodocAtmosphereComponent atmosphere)
    {
        var air = atmosphere.Air;
        air.Clear();
        air.Volume = atmosphere.Volume;
        air.Temperature = atmosphere.Temperature;

        var moles = atmosphere.Pressure * atmosphere.Volume / (Atmospherics.R * atmosphere.Temperature);
        air.SetMoles(Gas.Oxygen, moles * atmosphere.Oxygen);
        air.SetMoles(Gas.Nitrogen, moles * atmosphere.Nitrogen);
    }

    // Internals win: a patient on their own tank keeps breathing it, whichever handler runs first.
    private void OnOccupantInhale(Entity<WolfmedAutodocOccupantComponent> ent, ref InhaleLocationEvent args)
    {
        if (args.Gas == null && SealedAir(ent) is { } air)
            args.Gas = air;
    }

    private void OnOccupantExhale(Entity<WolfmedAutodocOccupantComponent> ent, ref ExhaleLocationEvent args)
    {
        if (args.Gas == null && SealedAir(ent) is { } air)
            args.Gas = air;
    }

    private void OnOccupantExposed(Entity<WolfmedAutodocOccupantComponent> ent, ref AtmosExposedGetAirEvent args)
    {
        if (args.Handled || SealedAir(ent) is not { } air)
            return;

        args.Gas = air;
        args.Handled = true;
    }

    /// <summary>The breakage threshold on the pod's Destructible: the hull is breached and outside air gets in.</summary>
    private void OnHullBreached(Entity<WolfmedAutodocAtmosphereComponent> ent, ref BreakageEventArgs args)
    {
        if (ent.Comp.Broken)
            return;

        ent.Comp.Broken = true;
        _appearance.SetData(ent, WolfmedAutodocAtmosphereVisuals.Breached, true);
        if (!TryComp(ent, out AutodocComponent? autodoc))
            return;

        var pod = new Entity<AutodocComponent>(ent, autodoc);
        if (GetOccupant(pod) != null)
            Speak(pod, AutodocVoiceEvent.HullBreach);

        UpdateUi(pod);
    }

    /// <summary>A repair that takes the damage back under the breakage threshold closes the breach.</summary>
    private void OnHullDamageChanged(Entity<WolfmedAutodocAtmosphereComponent> ent, ref DamageChangedEvent args)
    {
        if (!ent.Comp.Broken || args.DamageIncreased ||
            args.Damageable.TotalDamage >= _destructible.DestroyedAt(ent))
            return;

        ent.Comp.Broken = false;
        _appearance.SetData(ent, WolfmedAutodocAtmosphereVisuals.Breached, false);
        if (TryComp(ent, out AutodocComponent? autodoc))
            UpdateUi((ent, autodoc));
    }

    /// <summary>Examine says whether the atmosphere is protected, and says so for a breached hull even when empty.</summary>
    private void ExamineAtmosphere(Entity<AutodocComponent> ent, ExaminedEvent args)
    {
        if (WolfmedAutodocSealText.Examine(GetSeal(ent)) is { } line)
            args.PushMarkup(Loc.GetString(line));
    }
}
