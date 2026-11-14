using System.Linq;
using System.Numerics;
using Content.Server.Chat.Systems;
using Content.Server.Radio.EntitySystems;
using Content.Shared._Crescent.Dispenser;
using Content.Shared._Mono.Company;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.TradeGoods.Systems;

/// <summary>Sector Exchange announcements on the common channel, and private messages to some players.</summary>
public sealed partial class WFTradeMarketSystem
{
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private MetaDataSystem _meta = default!;

    private static readonly ProtoId<RadioChannelPrototype> ExchangeChannel = "Common";
    private static readonly Color ExchangeColor = Color.FromHex("#d9a441");

    private EntityUid? _exchange;

    /// <summary>Says a market line as the Sector Exchange, on the common channel unless another is given.</summary>
    public void Radio(string message, ProtoId<RadioChannelPrototype>? channel = null)
    {
        if (Exchange() is { } exchange)
            _radio.SendRadioMessage(exchange, message, channel ?? ExchangeChannel, exchange);
    }

    /// <summary>
    /// Says a market line about some stations: on the common channel, or only to the underground companies when any of
    /// them is an off-the-books market.
    /// </summary>
    public void Announce(string message, params EntityUid?[] stations)
    {
        if (stations.Any(station => station is { } grid && IsHiddenMarket(grid)))
            TellCompanies(_proto.Index(DefaultProfile).UndergroundCompanies, message);
        else
            Radio(message);
    }

    /// <summary>Whether a grid holds an off-the-books market, listed only under the app's Underground tab.</summary>
    public bool IsHiddenMarket(EntityUid grid)
    {
        var query = EntityQueryEnumerator<DispenserComponent, TransformComponent>();
        while (query.MoveNext(out var chute, out _, out var xform))
        {
            if (xform.GridUid == grid && IsHiddenChute(chute))
                return true;
        }

        return false;
    }

    /// <summary>Whether a chute is an off-the-books market's, such as PDV Helios or the Underground fence.</summary>
    public bool IsHiddenChute(EntityUid chute)
    {
        return MetaData(chute).EntityPrototype is { } proto && _proto.Index(DefaultProfile).HiddenChutes.Contains(proto.ID);
    }

    /// <summary>Tells the players aboard some grids, as the Sector Exchange.</summary>
    public void TellAboard(IReadOnlyCollection<EntityUid> grids, string message)
    {
        var filter = Filter.Empty();
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out _, out var actor, out var xform))
        {
            if (xform.GridUid is { } grid && grids.Contains(grid))
                filter.AddPlayer(actor.PlayerSession);
        }

        Tell(filter, message);
    }

    /// <summary>Tells the players of some companies, as the Sector Exchange.</summary>
    public void TellCompanies(IReadOnlyCollection<ProtoId<CompanyPrototype>> companies, string message)
    {
        var filter = Filter.Empty();
        var players = EntityQueryEnumerator<ActorComponent, CompanyComponent>();
        while (players.MoveNext(out _, out var actor, out var company))
        {
            if (companies.Contains(company.CompanyName))
                filter.AddPlayer(actor.PlayerSession);
        }

        Tell(filter, message);
    }

    private void Tell(Filter filter, string message)
    {
        if (filter.Count > 0)
            _chat.DispatchFilteredAnnouncement(filter, message, sender: Loc.GetString("wf-trade-exchange"), playSound: false, colorOverride: ExchangeColor);
    }

    /// <summary>The entity on the sector map that speaks for the Exchange, heard without a telecom server.</summary>
    private EntityUid? Exchange()
    {
        if (_exchange is { } existing && !TerminatingOrDeleted(existing))
            return existing;

        if (!_map.TryGetMap(_ticker.DefaultMap, out var mapUid) || mapUid is not { } uid)
            return null;

        var exchange = Spawn(null, new EntityCoordinates(uid, Vector2.Zero));
        Transform(exchange).GridTraversal = false;
        _meta.SetEntityName(exchange, Loc.GetString("wf-trade-exchange"));
        EnsureComp<TelecomExemptComponent>(exchange);
        _exchange = exchange;
        return exchange;
    }
}
