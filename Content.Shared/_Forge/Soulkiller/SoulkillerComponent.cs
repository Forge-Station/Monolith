// Author: @lenta313. Все права не защищены / No rights reserved.
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Soulkiller;

/// <summary>
/// Marks a Station-AI core ("Душегуб") that a player can inhabit through a
/// <see cref="SoulkillerConnectorComponent"/>. On connect, a brain is spawned into the core's
/// mind slot (granting full station-AI functionality) and the player's mind <b>visits</b> it —
/// their real body stays alive and soulless, and they can return at any time.
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedSoulkillerSystem))]
public sealed partial class SoulkillerComponent : Component
{
    /// <summary>
    /// Brain prototype spawned into the core when a player connects. Default = the normal
    /// station-AI brain, so the inhabitant gets the full AI toolset (eye, cameras, laws, jump-to-core).
    /// </summary>
    [DataField]
    public EntProtoId BrainProto = "StationAiBrain";

    /// <summary>
    /// Container on the core that the brain is inserted into (the AI mind slot).
    /// </summary>
    [DataField]
    public string MindSlotContainerId = "station_ai_mind_slot";

    /// <summary>
    /// Action granted to the inhabiting player to return to their real body.
    /// </summary>
    [DataField]
    public EntProtoId ReturnAction = "ActionSoulkillerReturnToBody";

    /// <summary>
    /// Tracks the granted action entity so it can be removed on unvisit.
    /// </summary>
    [DataField]
    public EntityUid? ReturnActionEntity;

    /// <summary>
    /// The brain currently spawned inside this core (null = empty core).
    /// </summary>
    [DataField]
    public EntityUid? SpawnedBrain;

    /// <summary>
    /// The mind currently inhabiting this core (null = empty / unattended).
    /// </summary>
    [DataField]
    public EntityUid? InhabitingMind;

    /// <summary>
    /// The operator's real body, sealed inside the connector capsule while connected.
    /// </summary>
    [DataField]
    public EntityUid? TetheredBody;

    /// <summary>
    /// The connector capsule the operator's body is sealed inside while connected. Opening it
    /// forcibly breaks the connection and ejects the body.
    /// </summary>
    [DataField]
    public EntityUid? Connector;

    /// <summary>
    /// Species allowed to connect. Only humanoids of this species may use the connector.
    /// КПБ = IPC.
    /// </summary>
    [DataField]
    public ProtoId<SpeciesPrototype> RequiredSpecies = "IPC";
}
