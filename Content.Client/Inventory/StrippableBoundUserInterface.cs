using System.Linq;
using System.Numerics;
using Content.Client.Examine;
using Content.Client.Strip;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Hands.Controls;
using Content.Client.Verbs.UI;
using Content.Shared.Cuffs;
using Content.Shared.Cuffs.Components;
using Content.Shared.Ensnaring.Components;
using Content.Shared.Hands.Components;
using Content.Shared.IdentityManagement;
using Content.Shared.Input;
using Content.Shared.Inventory;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Strip.Components;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Map;
using static Content.Client.Inventory.ClientInventorySystem;
using static Robust.Client.UserInterface.Control;
using Content.Shared._EE.Strip.Components; // EE

namespace Content.Client.Inventory
{
    [UsedImplicitly]
    public sealed partial class StrippableBoundUserInterface : BoundUserInterface
    {
        [Dependency] private IPlayerManager _player = default!;
        [Dependency] private IUserInterfaceManager _ui = default!;

        private readonly ExamineSystem _examine;
        private readonly InventorySystem _inv;
        private readonly SharedCuffableSystem _cuffable;
        private readonly StrippableSystem _strippable;

        [ViewVariables]
        private const int ButtonSeparation = 4;

        [ViewVariables]
        public const string HiddenPocketEntityId = "StrippingHiddenEntity";

        [ViewVariables]
        private StrippingMenu? _strippingMenu;

        [ViewVariables]
        private readonly EntityUid _virtualHiddenEntity;

        public StrippableBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
            _examine = EntMan.System<ExamineSystem>();
            _inv = EntMan.System<InventorySystem>();
            _cuffable = EntMan.System<SharedCuffableSystem>();
            _strippable = EntMan.System<StrippableSystem>();

            _virtualHiddenEntity = EntMan.SpawnEntity(HiddenPocketEntityId, MapCoordinates.Nullspace);
        }

        protected override void Open()
        {
            base.Open();

            _strippingMenu = this.CreateWindowCenteredLeft<StrippingMenu>();
            _strippingMenu.OnDirty += UpdateMenu;
            _strippingMenu.Title = Loc.GetString("strippable-bound-user-interface-stripping-menu-title", ("ownerName", Identity.Name(Owner, EntMan)));
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
                return;

            if (_strippingMenu != null)
                _strippingMenu.OnDirty -= UpdateMenu;

            EntMan.DeleteEntity(_virtualHiddenEntity);
            base.Dispose(disposing);
        }

        public void DirtyMenu()
        {
            if (_strippingMenu != null)
                _strippingMenu.Dirty = true;
        }

        public void UpdateMenu()
        {
            if (_strippingMenu == null)
                return;

            _strippingMenu.ClearButtons();

            // Forge-Change: hoist so window sizing below can reuse the same components.
            InventoryComponent? inv = null;
            HandsComponent? handsComp = null;
            EnsnareableComponent? snare = null;

            if (EntMan.TryGetComponent(Owner, out inv))
            {
                foreach (var slot in inv.Slots)
                {
                    AddInventoryButton(Owner, slot.Name, inv);
                }
            }

            if (EntMan.TryGetComponent(Owner, out handsComp) && handsComp.CanBeStripped)
            {
                // good ol hands shit code. there is a GuiHands comparer that does the same thing... but these are hands
                // and not gui hands... which are different...
                foreach (var hand in handsComp.Hands.Values)
                {
                    if (hand.Location != HandLocation.Right)
                        continue;

                    AddHandButton(hand);
                }

                foreach (var hand in handsComp.Hands.Values)
                {
                    if (hand.Location != HandLocation.Middle)
                        continue;

                    AddHandButton(hand);
                }

                foreach (var hand in handsComp.Hands.Values)
                {
                    if (hand.Location != HandLocation.Left)
                        continue;

                    AddHandButton(hand);
                }
            }

            // snare-removal button. This is just the old button before the change to item slots. It is pretty out of place.
            if (EntMan.TryGetComponent(Owner, out snare) && snare.IsEnsnared)
            {
                var button = new Button()
                {
                    Text = Loc.GetString("strippable-bound-user-interface-stripping-menu-ensnare-button"),
                    StyleClasses = { StyleBase.ButtonOpenRight }
                };

                button.OnPressed += (_) => SendPredictedMessage(new StrippingEnsnareButtonPressed());

                _strippingMenu.SnareContainer.AddChild(button);
            }

            // Forge-Change-Start: size the window from the visible slot grid so the back/belt row is never clipped.
            // NPC templates park unstrippable slots at 20+ so they sit outside the vanilla window; do not grow to fit those.
            const int hiddenSlotPos = 10;
            var cell = SlotControl.DefaultButtonSize + ButtonSeparation;
            var maxPos = Vector2i.Zero;
            if (inv != null)
            {
                foreach (var slot in inv.Slots)
                {
                    var pos = slot.StrippingWindowPos;
                    if (pos.X >= hiddenSlotPos || pos.Y >= hiddenSlotPos)
                        continue;

                    maxPos = Vector2i.ComponentMax(maxPos, pos);
                }
            }

            var hands = handsComp is { CanBeStripped: true } ? handsComp.Hands.Count : 0;
            var width = Math.Max(220f, (maxPos.X + 1) * cell + 40f);
            var height = (maxPos.Y + 1) * cell + 88f + hands * cell;
            if (snare?.IsEnsnared == true)
                height += 40f;

            var viewport = _ui.WindowRoot.Size;
            if (viewport.X > 1 && viewport.Y > 1)
            {
                width = Math.Min(width, MathF.Max(280f, viewport.X * 0.5f));
                height = Math.Min(height, MathF.Max(360f, viewport.Y * 0.85f));
            }

            _strippingMenu.SetSize = new Vector2(width, height);
            // Forge-Change-End
        }

        private void AddHandButton(Hand hand)
        {
            var button = new HandButton(hand.Name, hand.Location);

            button.Pressed += SlotPressed;

            if (EntMan.TryGetComponent<VirtualItemComponent>(hand.HeldEntity, out var virt))
            {
                button.Blocked = true;
                if (EntMan.TryGetComponent<CuffableComponent>(Owner, out var cuff) && _cuffable.GetAllCuffs(cuff).Contains(virt.BlockingEntity))
                    button.BlockedRect.MouseFilter = MouseFilterMode.Ignore;
            }

            // Goobstation: use virtual entity if hidden
            UpdateEntityIcon(button, EntMan.HasComponent<StripMenuHiddenComponent>(hand.HeldEntity) ? _virtualHiddenEntity : hand.HeldEntity);
            // End Goobstation
            _strippingMenu!.HandsContainer.AddChild(button);
        }

        private void SlotPressed(GUIBoundKeyEventArgs ev, SlotControl slot)
        {
            // TODO: allow other interactions? Verbs? But they should then generate a pop-up and/or have a delay so the
            // user that is being stripped can prevent the verbs from being exectuted.
            // So for now: only stripping & examining
            if (ev.Function == EngineKeyFunctions.Use)
            {
                SendPredictedMessage(new StrippingSlotButtonPressed(slot.SlotName, slot is HandButton));
                return;
            }

            if (slot.Entity == null)
                return;

            if (ev.Function == ContentKeyFunctions.ExamineEntity)
            {
                _examine.DoExamine(slot.Entity.Value);
                ev.Handle();
            }
            else if (ev.Function == EngineKeyFunctions.UseSecondary)
            {
                _ui.GetUIController<VerbMenuUIController>().OpenVerbMenu(slot.Entity.Value);
                ev.Handle();
            }
        }

        private void AddInventoryButton(EntityUid invUid, string slotId, InventoryComponent inv)
        {
            if (!_inv.TryGetSlotContainer(invUid, slotId, out var container, out var slotDef, inv))
                return;

            var entity = container.ContainedEntity;

            // If this is a full pocket, obscure the real entity
            // this does not work for modified clients because they are still sent the real entity
            if (entity != null && _strippable.IsStripHidden(slotDef, _player.LocalEntity))
                entity = _virtualHiddenEntity;

            // Goobstation/EE: hide strip menu items
            if (entity != null && EntMan.HasComponent<StripMenuHiddenComponent>(entity))
                entity = _virtualHiddenEntity;
            // End Goobstation/EE

            var button = new SlotButton(new SlotData(slotDef, container));
            button.Pressed += SlotPressed;

            _strippingMenu!.InventoryContainer.AddChild(button);

            UpdateEntityIcon(button, entity);

            LayoutContainer.SetPosition(button, slotDef.StrippingWindowPos * (SlotControl.DefaultButtonSize + ButtonSeparation));
        }

        private void UpdateEntityIcon(SlotControl button, EntityUid? entity)
        {
            // Hovering, highlighting & storage are features of general hands & inv GUIs. This UI just re-uses these because I'm lazy.
            button.ClearHover();
            button.StorageButton.Visible = false;

            if (entity == null)
            {
                button.SetEntity(null);
                return;
            }

            EntityUid? viewEnt;
            if (EntMan.TryGetComponent<VirtualItemComponent>(entity, out var virt))
                viewEnt = EntMan.HasComponent<SpriteComponent>(virt.BlockingEntity) ? virt.BlockingEntity : null;
            else if (EntMan.HasComponent<SpriteComponent>(entity))
                viewEnt = entity;
            else
                return;

            button.SetEntity(viewEnt);
        }
    }
}
