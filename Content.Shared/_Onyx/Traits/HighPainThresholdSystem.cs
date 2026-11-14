// WOLFGATE(Wolfmed): ported from Onyx for Wolfmed.
// Space Onyx
// Copyright (C) 2026 Space Onyx contributors
//
// This file is licensed under AGPL-3.0-or-later.
// See LICENSES for the full license text.

using Content.Shared._Onyx.Wounds;

namespace Content.Shared._Onyx.Traits;

public sealed class HighPainThresholdSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HighPainThresholdComponent, ModifyPainGainEvent>(OnModifyPainGain);
    }

    private void OnModifyPainGain(Entity<HighPainThresholdComponent> ent, ref ModifyPainGainEvent args)
    {
        args.Multiplier *= ent.Comp.PainMultiplier;
    }
}
