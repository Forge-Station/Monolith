using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Research.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ResearchServerComponent : Component
{
    /// <summary>
    /// The name of the server
    /// </summary>
    [AutoNetworkedField]
    [DataField("serverName"), ViewVariables(VVAccess.ReadWrite)]
    public string ServerName = "RDSERVER";

    /// <summary>
    /// The amount of points on the server.
    /// </summary>
    [AutoNetworkedField]
    [DataField("points"), ViewVariables(VVAccess.ReadWrite)]
    public int Points;

    /// <summary>
    /// A unique numeric id representing the server
    /// </summary>
    [AutoNetworkedField]
    [ViewVariables(VVAccess.ReadOnly)]
    public int Id;

    /// <summary>
    /// Entities connected to the server
    /// </summary>
    /// <remarks>
    /// This is not safe to read clientside
    /// </remarks>
    [ViewVariables(VVAccess.ReadOnly)]
    public List<EntityUid> Clients = new();

    [DataField("nextUpdateTime", customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextUpdateTime = TimeSpan.Zero;

    [DataField("researchConsoleUpdateTime"), ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan ResearchConsoleUpdateTime = TimeSpan.FromSeconds(1);

    // Forge-change
    [AutoNetworkedField]
    [DataField("insertedDisks")]
    [ViewVariables(VVAccess.ReadOnly)]
    public List<EntityUid> InsertedDisks = new();

    /// <summary>
    /// When true, only clients in <see cref="AuthorizedClients"/> may connect to this server.
    /// Equipment is authorized via the research server link console UI.
    /// </summary>
    [DataField]
    public bool RestrictClientConnections = true;

    /// <summary>
    /// Research clients explicitly allowed to link to this server.
    /// Server-side only; sent to clients through the link UI state.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public HashSet<EntityUid> AuthorizedClients = new();
    // Forge-change end
}

/// <summary>
/// Event raised on a server's clients when the point value of the server is changed.
/// </summary>
/// <param name="Server"></param>
/// <param name="Total"></param>
/// <param name="Delta"></param>
[ByRefEvent]
public readonly record struct ResearchServerPointsChangedEvent(EntityUid Server, int Total, int Delta);

/// <summary>
/// Event raised every second to calculate the amount of points added to the server.
/// </summary>
/// <param name="Server"></param>
/// <param name="Points"></param>
[ByRefEvent]
public record struct ResearchServerGetPointsPerSecondEvent(EntityUid Server, int Points);

