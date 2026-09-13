using Content.Shared._Common.Consent;
using Content.Shared._WF.Genitals.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Genitals.Systems;

/// <summary>Arousal writes: the owner's panel, consent-gated external changes and the resets. Every change goes through Apply.</summary>
public sealed partial class SharedArousalSystem
{
    [Dependency] private GenitalConsentSystem _consent = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    /// <summary>Highest arousal value.</summary>
    public const byte MaxArousal = 100;

    private void InitializeWrites()
    {
        SubscribeLocalEvent<GenitalsComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<GenitalsComponent, GenitalsChangedEvent>(OnGenitalsChanged);
        SubscribeLocalEvent<GenitalConsentChangedEvent>(OnConsentChanged);
    }

    /// <summary>Owner path (panel). Needs the kill switch, strict master consent on the body, a living body and an arousable organ. Clamps to 0..100.</summary>
    public bool SetArousal(Entity<GenitalsComponent> ent, byte value)
    {
        if (!_consent.AnatomyEnabled
            || !_consent.HasMaster(ent.Owner)
            || _mobState.IsDead(ent.Owner)
            || !CanBeAroused((ent.Owner, ent.Comp)))
            return false;

        Apply(ent, Math.Min(value, MaxArousal), null);
        return true;
    }

    /// <summary>
    /// Any path other than the owner's panel (future reagents or interactions). Needs the target's consent to adult content
    /// and to <paramref name="consent"/>, the target actively present and alive, and, when the source is a humanoid, an adult
    /// source with strict master consent. Clamps to 0..100.
    /// </summary>
    public bool TryAdjustArousal(Entity<GenitalsComponent?> ent, int delta, ProtoId<ConsentTogglePrototype> consent,
        EntityUid? source = null)
    {
        if (!Resolve(ent, ref ent.Comp, false)
            || !CanBeAroused(ent)
            || _mobState.IsDead(ent.Owner)
            || !_consent.CanAdjustExternally(ent.Owner, consent, source))
            return false;

        var value = (byte) Math.Clamp(ent.Comp.Arousal + delta, 0, MaxArousal);
        Apply(new Entity<GenitalsComponent>(ent.Owner, ent.Comp), value, source);
        return true;
    }

    /// <summary>Admin repair: arousal to 0 through Apply, without the consent and life gates.</summary>
    public void ResetArousal(Entity<GenitalsComponent> ent)
    {
        Apply(ent, 0, null);
    }

    /// <summary>The one write path: sets and dirties the field, then raises the visuals and arousal events. Nothing happens when unchanged.</summary>
    private void Apply(Entity<GenitalsComponent> ent, byte value, EntityUid? source)
    {
        var old = ent.Comp.Arousal;
        if (old == value)
            return;

        var settings = _genitals.Settings;
        ent.Comp.Arousal = value;
        DirtyField(ent.Owner, ent.Comp, nameof(GenitalsComponent.Arousal));

        var visuals = new GenitalsVisualsChangedEvent();
        RaiseLocalEvent(ent.Owner, ref visuals);

        var changed = new ArousalChangedEvent(old, value, ToState(old, settings), ToState(value, settings), source);
        RaiseLocalEvent(ent.Owner, ref changed);
    }

    /// <summary>Death resets arousal; owner writes are refused while dead.</summary>
    private void OnMobStateChanged(Entity<GenitalsComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            Apply(ent, 0, null);
    }

    /// <summary>Nothing arousable remains, e.g. the last penis or vagina was removed or the mirror emptied: arousal resets.</summary>
    private void OnGenitalsChanged(Entity<GenitalsComponent> ent, ref GenitalsChangedEvent args)
    {
        if (!CanBeAroused((ent.Owner, ent.Comp)))
            Apply(ent, 0, null);
    }

    /// <summary>The master switch went off, or the mind left and cleared the toggles: arousal resets.</summary>
    private void OnConsentChanged(ref GenitalConsentChangedEvent ev)
    {
        if (TryComp<GenitalsComponent>(ev.Body, out var genitals) && !_consent.HasMaster(ev.Body))
            Apply((ev.Body, genitals), 0, null);
    }
}
