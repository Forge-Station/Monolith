using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WF.Xenobiology.Bounties;

/// <summary>
/// A xenobiology bounty: items to hand in at the bounty console for research points.
/// </summary>
[Prototype]
public sealed partial class XenobiologyBountyPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public float PointsAwarded;

    [DataField(required: true)]
    public List<XenobiologyBountyItemEntry> Entries = new();

    /// <summary>
    /// Prefix of the bounty's displayed ID.
    /// </summary>
    [DataField]
    public string IdPrefix = "NT";
}

[DataDefinition]
public partial record struct XenobiologyBountyItemEntry()
{
    [DataField(required: true)]
    public EntityWhitelist Whitelist = default!;

    [DataField]
    public EntityWhitelist? Blacklist;

    [DataField]
    public int Amount = 1;

    [DataField]
    public LocId Name;
}

/// <summary>
/// An open bounty on a console.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class XenobiologyBountyData
{
    [DataField]
    public string Id { get; private set; } = string.Empty;

    [DataField(required: true)]
    public ProtoId<XenobiologyBountyPrototype> Bounty { get; private set; } = string.Empty;

    public XenobiologyBountyData(XenobiologyBountyPrototype bounty, int uniqueIdentifier)
    {
        Bounty = bounty.ID;
        Id = $"{bounty.IdPrefix}{uniqueIdentifier:D3}";
    }
}

[Serializable, NetSerializable]
public enum XenobiologyBountyResult : byte
{
    Completed,
    Skipped,
}

/// <summary>
/// A bounty that was completed or skipped.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public readonly partial record struct XenobiologyBountyHistoryData
{
    [DataField]
    public string Id { get; init; } = string.Empty;

    [DataField]
    public XenobiologyBountyResult Result { get; init; } = XenobiologyBountyResult.Completed;

    [DataField]
    public string? ActorName { get; init; }

    [DataField]
    public TimeSpan Timestamp { get; init; } = TimeSpan.MinValue;

    [DataField(required: true)]
    public ProtoId<XenobiologyBountyPrototype> Bounty { get; init; } = string.Empty;

    public XenobiologyBountyHistoryData(XenobiologyBountyData bounty, XenobiologyBountyResult result, TimeSpan timestamp, string? actorName)
    {
        Bounty = bounty.Bounty;
        Result = result;
        Id = bounty.Id;
        ActorName = actorName;
        Timestamp = timestamp;
    }
}

/// <summary>
/// A console that lists xenobiology bounties and pays research points to its research server.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
public sealed partial class XenobiologyBountyConsoleComponent : Component
{
    [DataField]
    public List<XenobiologyBountyData> Bounties = new();

    [DataField]
    public List<XenobiologyBountyHistoryData> History = new();

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextSkipTime = TimeSpan.Zero;

    [DataField]
    public TimeSpan SkipDelay = TimeSpan.FromMinutes(10);

    [DataField]
    public TimeSpan RefreshDelay = TimeSpan.FromMinutes(12);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextRefresh = TimeSpan.Zero;

    [DataField]
    public SoundSpecifier FulfillSound = new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");

    [DataField]
    public SoundSpecifier DenySound = new SoundPathSpecifier("/Audio/Effects/Cargo/buzz_two.ogg");

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextDenySoundTime = TimeSpan.Zero;

    [DataField]
    public TimeSpan DenySoundDelay = TimeSpan.FromSeconds(2);
}

[Serializable, NetSerializable]
public enum XenobiologyBountyUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class XenobiologyBountyConsoleState(
    List<XenobiologyBountyData> bounties,
    List<XenobiologyBountyHistoryData> history,
    TimeSpan untilNextSkip,
    TimeSpan untilNextRefresh)
    : BoundUserInterfaceState
{
    public readonly List<XenobiologyBountyData> Bounties = bounties;
    public readonly List<XenobiologyBountyHistoryData> History = history;
    public readonly TimeSpan UntilNextSkip = untilNextSkip;
    public readonly TimeSpan UntilNextRefresh = untilNextRefresh;
}

[Serializable, NetSerializable]
public sealed class XenobiologyBountyFulfillMessage(string bountyId) : BoundUserInterfaceMessage
{
    public readonly string BountyId = bountyId;
}

[Serializable, NetSerializable]
public sealed class XenobiologyBountySkipMessage(string bountyId) : BoundUserInterfaceMessage
{
    public readonly string BountyId = bountyId;
}
