namespace Content.Server._WF.Shipyard;

/// <summary>
/// Put on a station whose ship sales feed the used ship lot. Every ship sold at any console or
/// trader on this station is copied before it is scrapped and relisted a few minutes later.
/// </summary>
[RegisterComponent]
public sealed partial class UsedShipMarketComponent : Component
{
}
