using Content.Shared.Examine;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Humanoid;
using Content.Shared.Station;
using Content.Shared._EE.Contractors.Components;  // Forge-Change
using Robust.Shared.Prototypes;
using Content.Shared.Traits.Assorted.Components;
using Content.Shared.Preferences;

namespace Content.Shared.Traits.Assorted.Systems;

public sealed class ExtendDescriptionSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedStationSpawningSystem _stationSpawning = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ExtendDescriptionComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(EntityUid uid, ExtendDescriptionComponent component, ExaminedEvent args)
    {
        // Passport should only reveal its "inside" text while it is open.
        // Without this check, ExtendDescription would show even for closed passports.
        if (TryComp<PassportComponent>(uid, out var passport) && passport.IsClosed)  // Forge-Change
            return;

        if (component.DescriptionList.Count <= 0)
            return;

        HumanoidCharacterProfile? profile = null;
        _stationSpawning.GetProfile(args.Examiner, out profile);

        foreach (var desc in component.DescriptionList)
        {
            if (!args.IsInDetailsRange && desc.RequireDetailRange
                || !TryComp(args.Examiner, out MetaDataComponent? comp) || comp.EntityPrototype == null)
                continue;

            bool meetsRequirements = true;

            if (desc.Requirements != null)
            {
                foreach (var req in desc.Requirements)
                {
                    if (!req.Check(EntityManager, _proto, profile, new Dictionary<string, TimeSpan>(), out _))
                    {
                        meetsRequirements = false;
                        break;
                    }
                }
            }

            var description = meetsRequirements ? desc.Description : desc.RequirementsNotMetDescription;

            if (description != string.Empty)
                args.PushMarkup($"[font size ={desc.FontSize}][color={desc.Color}]{Loc.GetString(description, ("entity", uid))}[/color][/font]");
        }
    }
}
