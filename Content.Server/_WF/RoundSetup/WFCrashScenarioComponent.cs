using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._WF.RoundSetup;

/// <summary>A lobby's original hull or one of its surviving wreck sections.</summary>
[RegisterComponent, UnsavedComponent, AutoGenerateComponentPause]
public sealed partial class WFCrashScenarioComponent : Component
{
    /// <summary>The lobby that owns this hull.</summary>
    [DataField]
    public int LobbyId;

    /// <summary>Fixed end of the crew's preparation window.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan DescendAt;

    /// <summary>The first survivor has boarded and armed the preparation countdown.</summary>
    [DataField]
    public bool Started;

    /// <summary>The hull has entered the normal atmospheric flight simulation.</summary>
    [DataField]
    public bool Descending;

    /// <summary>A standard crash impact has completed, allowing repairs and piloting.</summary>
    [DataField]
    public bool Crashed;
}

/// <summary>Routes station admission through its owning Round Setup lobby.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFCrashScenarioStationComponent : Component
{
    /// <summary>The lobby that controls admission to this station.</summary>
    [DataField]
    public int LobbyId;
}

/// <summary>A helm whose scenario hull cannot respond until its crash is complete.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFCrashScenarioConsoleComponent : Component;
