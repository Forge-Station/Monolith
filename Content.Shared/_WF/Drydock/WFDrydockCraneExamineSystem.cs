using Content.Shared.Examine;

namespace Content.Shared._WF.Drydock;

/// <summary>
/// Shows a drydock crane's linked console, working power draw and share of power received on examine.
/// </summary>
public sealed class WFDrydockCraneExamineSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFDrydockCraneComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(EntityUid uid, WFDrydockCraneComponent comp, ExaminedEvent args)
    {
        // The console can be outside the examiner's PVS.
        args.PushMarkup(comp.LinkedConsole is { } console && TryComp(console, out MetaDataComponent? meta)
            ? Loc.GetString("drydock-crane-examine-linked", ("console", meta.EntityName))
            : Loc.GetString("drydock-crane-examine-unlinked"));
        if (comp.Blocker is { } blocker)
            args.PushMarkup(Loc.GetString("drydock-crane-examine-blocked", ("blocker", blocker)));

        args.PushMarkup(Loc.GetString("drydock-crane-examine-load", ("kw", MathF.Round(comp.WorkingLoad / 1000f, 1))));
        args.PushMarkup(Loc.GetString("drydock-crane-examine-power", ("percent", (int) MathF.Round(comp.PowerRatio * 100f))));
    }
}
