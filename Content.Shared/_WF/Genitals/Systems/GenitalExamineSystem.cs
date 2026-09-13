using Content.Shared._WF.Genitals.Components;
using Content.Shared._WF.Genitals.Prototypes;
using Content.Shared.Body.Organ;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Genitals.Systems;

/// <summary>Consent-gated clinical examine lines for exposed anatomy and for loose genital organs.</summary>
/// <remarks>
/// Shared, so the client predicts the body lines. Organ data exists only on the server, so loose-organ lines arrive in
/// the server's examine response. Every argument is a localised word or a number, never player text.
/// </remarks>
public sealed partial class GenitalExamineSystem : EntitySystem
{
    [Dependency] private GenitalConsentSystem _consent = default!;
    [Dependency] private GenitalCoverageSystem _coverage = default!;
    [Dependency] private SharedGenitalsSystem _genitals = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    /// <summary>Group priority, below the humanoid line (100).</summary>
    private const int ExaminePriority = 50;

    private const string BodyPrefix = "wf-genitals-examine-";
    private const string SelfPrefix = "wf-genitals-examine-self-";
    private const string OrganPrefix = "wf-genitals-organ-examine-";

    // Fluent selector values.
    private const string NoShape = "none";
    private const string Yes = "yes";
    private const string No = "no";

    /// <summary>Slots described on a body, top to bottom. The womb is internal and never described.</summary>
    private static readonly GenitalSlot[] DescribedSlots =
    {
        GenitalSlot.Breasts,
        GenitalSlot.Penis,
        GenitalSlot.Testicles,
        GenitalSlot.Vagina,
    };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GenitalsComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<GenitalOrganComponent, ExaminedEvent>(OnOrganExamined);
    }

    /// <summary>One line per exposed organ when both sides consent and the examiner is in details range. Self-examine also lists organs others cannot see.</summary>
    private void OnExamined(Entity<GenitalsComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || !_consent.CanExamine(args.Examiner, ent.Owner))
            return;

        var self = args.Examiner == ent.Owner;
        var settings = _genitals.Settings;
        var arousal = SharedArousalSystem.ToState(ent.Comp.Arousal, settings);
        var target = Identity.Entity(ent.Owner, EntityManager);
        var coverage = _coverage.GetCoverage(ent);

        using (args.PushGroup(nameof(GenitalExamineSystem), ExaminePriority))
        {
            foreach (var slot in DescribedSlots)
            {
                var exposure = _coverage.GetExposure(ent, slot, coverage);
                if (!exposure.Present)
                    continue;

                string? line;
                if (exposure.Exposed)
                    line = ExposedLine(ent.Comp, slot, arousal, settings, target, self);
                else if (self)
                    line = UnexposedSelfLine(ent.Comp, slot);
                else
                    line = null;

                if (line != null)
                    args.PushMarkup(line);
            }
        }
    }

    /// <summary>One line for a loose organ. Server only: the client's copy of an organ carries default data.</summary>
    private void OnOrganExamined(Entity<GenitalOrganComponent> ent, ref ExaminedEvent args)
    {
        if (_net.IsClient || !args.IsInDetailsRange || !_consent.ExaminerMayRead(args.Examiner))
            return;

        // An organ still inside a body is described only through that body, under its owner's consent.
        if (TryComp<OrganComponent>(ent.Owner, out var organ) && organ.Body != null)
            return;

        if (OrganLine(ent.Comp) is not { } line)
            return;

        using (args.PushGroup(nameof(GenitalExamineSystem), ExaminePriority))
        {
            args.PushMarkup(line);
        }
    }

    /// <summary>Line for an exposed organ, or null when the slot holds nothing to describe.</summary>
    private string? ExposedLine(GenitalsComponent genitals, GenitalSlot slot, ArousalState arousal,
        GenitalSettingsPrototype settings, EntityUid target, bool self)
    {
        var prefix = self ? SelfPrefix : BodyPrefix;
        switch (slot)
        {
            case GenitalSlot.Penis when genitals.Penis is { } penis:
                return PenisLine(prefix, penis, arousal, target);

            case GenitalSlot.Testicles when genitals.Testicles is { } testicles:
                return Loc.GetString(prefix + "testicles", ("target", target), ("size", GenitalWords.TesticleSize(testicles.Step)));

            case GenitalSlot.Vagina when genitals.Vagina is { } vagina:
            {
                // Same threshold as the aroused art.
                var aroused = arousal != ArousalState.None && arousal >= settings.VaginaArousedFrom;
                return Loc.GetString(prefix + "vagina",
                    ("target", target),
                    ("shape", ExamineWord(vagina.Shape) ?? NoShape),
                    ("aroused", aroused ? Yes : No));
            }

            case GenitalSlot.Breasts when genitals.Breasts is { } breasts:
                return BreastsLine(prefix, breasts, target);

            default:
                return null;
        }
    }

    /// <summary>Sheath or slit with its state when sheathed; otherwise shape, erect length and state.</summary>
    private string PenisLine(string prefix, GenitalOrganState penis, ArousalState arousal, EntityUid target)
    {
        if (penis.Sheath != SheathType.None)
        {
            var sheath = penis.Sheath == SheathType.Slit ? "wf-genitals-sheath-word-slit" : "wf-genitals-sheath-word-sheath";
            return Loc.GetString(prefix + "penis-sheathed",
                ("target", target),
                ("sheath", Loc.GetString(sheath)),
                ("state", Loc.GetString(SheathStateKey(arousal))));
        }

        var length = (int) penis.LengthCm;
        var state = Loc.GetString(ErectionStateKey(arousal));
        if (ExamineWord(penis.Shape) is { } shape)
            return Loc.GetString(prefix + "penis", ("target", target), ("shape", shape), ("length", length), ("state", state));

        return Loc.GetString(prefix + "penis-noshape", ("target", target), ("length", length), ("state", state));
    }

    private string BreastsLine(string prefix, GenitalOrganState breasts, EntityUid target)
    {
        var cup = GenitalWords.CupText(breasts.Step);
        var lactating = breasts.Lactation ? Yes : No;
        if (ExamineWord(breasts.Shape) is { } shape)
            return Loc.GetString(prefix + "breasts", ("target", target), ("shape", shape), ("cup", cup), ("lactating", lactating));

        return Loc.GetString(prefix + "breasts-noshape", ("target", target), ("cup", cup), ("lactating", lactating));
    }

    /// <summary>Self-examine line for a present organ others cannot see: hidden by the owner's setting, or covered.</summary>
    private string? UnexposedSelfLine(GenitalsComponent genitals, GenitalSlot slot)
    {
        if (OrganWordKey(slot) is not { } organ)
            return null;

        var key = genitals.Visibility.Get(slot) == GenitalVisibility.AlwaysHidden ? "hidden" : "covered";
        var plural = slot is GenitalSlot.Testicles or GenitalSlot.Breasts;
        return Loc.GetString(SelfPrefix + key, ("organ", Loc.GetString(organ)), ("plural", plural ? Yes : No));
    }

    /// <summary>Line for a loose organ, from the data it was built with.</summary>
    private string? OrganLine(GenitalOrganComponent organ)
    {
        var state = organ.State;
        switch (organ.Slot)
        {
            case GenitalSlot.Penis:
            {
                // An organ spawned without a configuration has no length.
                if (state.LengthCm == 0)
                    return Loc.GetString(OrganPrefix + "penis-plain");

                var length = (int) state.LengthCm;
                if (ExamineWord(state.Shape) is { } shape)
                    return Loc.GetString(OrganPrefix + "penis", ("shape", shape), ("length", length));

                return Loc.GetString(OrganPrefix + "penis-noshape", ("length", length));
            }

            case GenitalSlot.Testicles:
                return state.Testicles == TesticleType.External
                    ? Loc.GetString(OrganPrefix + "testicles", ("size", GenitalWords.TesticleSize(state.Step)))
                    : Loc.GetString(OrganPrefix + "testicles-plain");

            case GenitalSlot.Vagina:
                return Loc.GetString(OrganPrefix + "vagina", ("shape", ExamineWord(state.Shape) ?? NoShape));

            case GenitalSlot.Womb:
                return Loc.GetString(OrganPrefix + "womb");

            case GenitalSlot.Breasts:
            {
                var cup = GenitalWords.CupText(state.Step);
                if (ExamineWord(state.Shape) is { } shape)
                    return Loc.GetString(OrganPrefix + "breasts", ("shape", shape), ("cup", cup));

                return Loc.GetString(OrganPrefix + "breasts-noshape", ("cup", cup));
            }

            default:
                return null;
        }
    }

    /// <summary>Localised examine word of a shape; null when the shape has none (nondescript) or is unknown.</summary>
    private string? ExamineWord(ProtoId<GenitalShapePrototype>? shape)
    {
        if (shape is not { } id || !_proto.TryIndex(id, out var proto) || proto.ExamineName is not { } name)
            return null;

        return Loc.GetString(name);
    }

    private static string ErectionStateKey(ArousalState arousal)
    {
        return arousal switch
        {
            ArousalState.Full => "wf-genitals-state-erect",
            ArousalState.Partial => "wf-genitals-state-partial",
            _ => "wf-genitals-state-flaccid",
        };
    }

    private static string SheathStateKey(ArousalState arousal)
    {
        return arousal switch
        {
            ArousalState.Full => "wf-genitals-sheath-state-full",
            ArousalState.Partial => "wf-genitals-sheath-state-partial",
            _ => "wf-genitals-sheath-state-retracted",
        };
    }

    /// <summary>Organ word for the self-examine covered and hidden lines; null for the womb.</summary>
    private static string? OrganWordKey(GenitalSlot slot)
    {
        return slot switch
        {
            GenitalSlot.Penis => "wf-genitals-examine-organ-penis",
            GenitalSlot.Testicles => "wf-genitals-examine-organ-testicles",
            GenitalSlot.Vagina => "wf-genitals-examine-organ-vagina",
            GenitalSlot.Breasts => "wf-genitals-examine-organ-breasts",
            _ => null,
        };
    }
}
