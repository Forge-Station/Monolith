#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Server._WF.SectorControl;
using Content.Shared._WF.SectorControl;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.SectorControl;

/// <summary>Records the sector events a test cares about and vetoes the claims it is told to.</summary>
public sealed class WFSectorTestSystem : EntitySystem
{
    public readonly List<WFSectorCellChangedEvent> Changed = new();
    public readonly List<WFSectorCellContestedEvent> Contested = new();
    public readonly List<WFSectorContestResolvedEvent> Resolved = new();
    public readonly List<WFSectorClaimAttemptEvent> Attempts = new();
    public readonly List<WFSectorCellDisputedEvent> Disputed = new();

    public readonly List<WFSectorRetakeAttemptEvent> RetakeAttempts = new();
    public readonly List<WFSectorCellRetakingEvent> Retaking = new();
    public readonly List<WFSectorCellRetakenEvent> Retaken = new();

    public readonly List<WFSectorCellClearingEvent> Clearing = new();
    public readonly List<WFSectorCellClearedEvent> Cleared = new();

    /// <summary>Cells a retake is vetoed for.</summary>
    public readonly HashSet<WFSectorCell> RetakeVetoed = new();

    /// <summary>Cells every claim and contest on is vetoed for.</summary>
    public readonly HashSet<WFSectorCell> Vetoed = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFSectorClaimAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<WFSectorCellChangedEvent>(OnChanged);
        SubscribeLocalEvent<WFSectorCellContestedEvent>(OnContested);
        SubscribeLocalEvent<WFSectorContestResolvedEvent>(OnResolved);
        SubscribeLocalEvent<WFSectorCellDisputedEvent>(OnDisputed);
        SubscribeLocalEvent<WFSectorRetakeAttemptEvent>(OnRetakeAttempt);
        SubscribeLocalEvent<WFSectorCellRetakingEvent>(OnRetaking);
        SubscribeLocalEvent<WFSectorCellRetakenEvent>(OnRetaken);
        SubscribeLocalEvent<WFSectorCellClearingEvent>(OnClearing);
        SubscribeLocalEvent<WFSectorCellClearedEvent>(OnCleared);
    }

    private void OnClearing(ref WFSectorCellClearingEvent args)
    {
        Clearing.Add(args);
    }

    private void OnCleared(ref WFSectorCellClearedEvent args)
    {
        Cleared.Add(args);
    }

    /// <summary>Forgets what was recorded and lifts every veto.</summary>
    public void Reset()
    {
        Changed.Clear();
        Contested.Clear();
        Resolved.Clear();
        Attempts.Clear();
        Disputed.Clear();
        Vetoed.Clear();
        RetakeAttempts.Clear();
        Retaking.Clear();
        Retaken.Clear();
        RetakeVetoed.Clear();
        Clearing.Clear();
        Cleared.Clear();
    }

    private void OnRetakeAttempt(ref WFSectorRetakeAttemptEvent args)
    {
        RetakeAttempts.Add(args);
        if (RetakeVetoed.Contains(args.Cell))
            args.Cancelled = true;
    }

    private void OnRetaking(ref WFSectorCellRetakingEvent args)
    {
        Retaking.Add(args);
    }

    private void OnRetaken(ref WFSectorCellRetakenEvent args)
    {
        Retaken.Add(args);
    }

    private void OnAttempt(ref WFSectorClaimAttemptEvent args)
    {
        Attempts.Add(args);
        if (Vetoed.Contains(args.Cell))
            args.Cancelled = true;
    }

    private void OnChanged(ref WFSectorCellChangedEvent args)
    {
        Changed.Add(args);
    }

    private void OnContested(ref WFSectorCellContestedEvent args)
    {
        Contested.Add(args);
    }

    private void OnDisputed(ref WFSectorCellDisputedEvent args)
    {
        Disputed.Add(args);
    }

    private void OnResolved(ref WFSectorContestResolvedEvent args)
    {
        Resolved.Add(args);
    }
}
