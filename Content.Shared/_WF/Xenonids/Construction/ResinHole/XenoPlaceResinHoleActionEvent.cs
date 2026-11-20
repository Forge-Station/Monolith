using Content.Shared.Actions;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Content.Shared._WF.Xenonids.Construction.Events;

public sealed partial class XenoPlaceResinHoleActionEvent : InstantActionEvent
{
    [DataField]
    public EntProtoId Prototype = "XenoResinHole";

    [DataField]
    public float DestroyWeedSourceDelay = 1.0f;

    [DataField]
    public int PlasmaCost = 200;
}
