using Robust.Shared.Audio;

namespace Content.Server._WF.Birds;

/// <summary>A bird that dives at prey with its <c>MeleeWeapon</c>, then climbs away and comes round again.</summary>
[RegisterComponent]
public sealed partial class WFBirdHostileComponent : Component
{
    /// <summary>How far the bird spots prey from.</summary>
    [DataField] public float DiveRange = 9f;

    /// <summary>Only a player with no other player within <see cref="LoneRadius"/> is prey.</summary>
    [DataField] public bool LoneOnly;

    [DataField] public float LoneRadius = 6f;

    /// <summary>Only player-controlled mobs are prey.</summary>
    [DataField] public bool PlayersOnly = true;

    /// <summary>Prey must carry at least this much damage; 0 takes the healthy too. Above 0, critical prey counts.</summary>
    [DataField] public float MinTargetDamage;

    [DataField] public float DiveSpeed = 8f;

    /// <summary>Seconds spent circling over prey before each dive; 0 dives straight away.</summary>
    [DataField] public float CircleTime;

    [DataField] public float ClimbTime = 1.2f;

    /// <summary>Seconds between dives.</summary>
    [DataField] public float Cooldown = 6f;

    /// <summary>A dive that hasn't reached its prey by then is broken off.</summary>
    [DataField] public float MaxDiveTime = 4f;

    [DataField] public SoundSpecifier? DiveSound;

    [ViewVariables] public EntityUid? Target;

    [ViewVariables] public TimeSpan NextDive;
}
