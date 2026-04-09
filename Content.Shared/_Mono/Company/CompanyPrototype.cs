using Robust.Shared.Prototypes;
using Robust.Shared.Audio; // Forge-change
using Content.Shared.Roles; // Forge-change
using Content.Shared.StatusIcon; // Forge-change

namespace Content.Shared._Mono.Company;

/// <summary>
/// Prototype for a company that can be assigned to players.
/// </summary>
[Prototype]
public sealed partial class CompanyPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// The form / type of company ("type" is a bad word).
    /// Assign "Neutral"
    /// Assign "Protagonist"
    /// Assign "Antagonist"
    /// </summary>
    [DataField]
    public string Form { get; private set; } = "Neutral";


    /// <summary>
    /// The name of the company.
    /// </summary>
    [DataField(required: true)]
    public string Name { get; private set; } = default!;

    /// <summary>
    /// The description of the company.
    /// </summary>
    [DataField(required: false)]
    public string Description { get; private set; } = string.Empty;

    /// <summary>
    /// The color used to display the company name in examine text.
    /// </summary>
    [DataField]
    public Color Color { get; private set; } = Color.Yellow;

    /// </summary>
    /// Forge-change: delete Logins, Disabled = Whitelisted
    /// Whether this company requires whitelist to join.
    /// Companies with this set to true will be displayed in the selection UI,
    /// but players won't be able to select them unless they are whitelisted.
    /// These companies can still be assigned automatically through the job system.
    /// </summary>
    [DataField]
    public bool Whitelisted { get; private set; } = false;

    /// <summary>
    /// The image to display for this company in the UI.
    /// </summary>
    [DataField]
    public string? Image { get; private set; }

    // Forge-change-start
    [DataField(serverOnly: true)]
    public JobSpecial[] Special { get; private set; } = Array.Empty<JobSpecial>();

    /// <summary>
    /// Whether this company should be completely hidden from the selection UI.
    /// Companies with this set to true will not appear in the company selection menu
    /// and cannot be selected by players under any circumstances.
    /// These companies can still be assigned automatically through the job system.
    /// </summary>
    [DataField]
    public bool Hidden { get; private set; } = false;

    /// <summary>
    /// Note about the company.
    /// </summary>
    [DataField(required: false)]
    public string Note { get; private set; } = string.Empty;

    /// <summary>
    /// The icon prototype to display for this company in ShowJobIcon
    /// </summary>
    [DataField("icon")]
    public ProtoId<CompanyIconPrototype>? Icon { get; private set; }
    // Forge-change-end
}
