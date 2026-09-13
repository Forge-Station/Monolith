using Content.Shared._WF.CCVar;
using Content.Shared._WF.Genitals.Prototypes;
using Content.Shared.Humanoid;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Genitals.Systems;

/// <summary>Anatomy settings, eligibility and owner requests.</summary>
/// <remarks>Abstract because EntitySystemManager runs a concrete base beside its subclass; each side has one subclass.</remarks>
public abstract partial class SharedGenitalsSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] protected IGameTiming Timing = default!;

    private bool _enabled = true;

    /// <summary>The Default settings prototype.</summary>
    public GenitalSettingsPrototype Settings => GenitalProfileValidator.GetSettings(_proto);

    /// <summary>The replicated kill switch wf.anatomy_enabled.</summary>
    public bool AnatomyEnabled => _enabled;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_cfg, WolfgateCVars.AnatomyEnabled, value => _enabled = value, true);
        InitializeRequests();
    }

    /// <summary>Server override rate-limits owner requests per session, request type and slot (-1 for none).</summary>
    protected virtual bool AllowRequest(ICommonSession session, Type request, int slot)
    {
        return true;
    }

    public bool IsAdult(int age)
    {
        return age >= Settings.AdultAge;
    }

    /// <summary>A humanoid at least AdultAge. Anything that is not humanoid is not adult.</summary>
    public bool IsAdult(EntityUid uid)
    {
        return TryComp<HumanoidAppearanceComponent>(uid, out var humanoid) && IsAdult(humanoid.Age);
    }

    /// <summary>Adult and not an excluded species.</summary>
    public bool IsEligible(int age, string species)
    {
        return GenitalProfileValidator.IsEligible(age, species, Settings, out _);
    }

    /// <summary>A humanoid whose age and species are eligible.</summary>
    public bool IsEligible(EntityUid uid)
    {
        return TryComp<HumanoidAppearanceComponent>(uid, out var humanoid) && IsEligible(humanoid.Age, humanoid.Species.Id);
    }
}
