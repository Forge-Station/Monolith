namespace Content.Shared._WF.Vehicles.Compat;

/// <summary>Queues refreshes of open <see cref="IRefreshableBui"/>s and runs them on the next update.</summary>
public sealed partial class VehicleUserInterfaceSystem : EntitySystem
{
    private readonly List<(Entity<UserInterfaceComponent?> Ent, Action<Entity<UserInterfaceComponent?>, VehicleUserInterfaceSystem> Act)> _toRefresh = new();

    /// <summary>Refreshes every open interface of type <typeparamref name="T"/> on the entity once the current event batch ends.</summary>
    public void RefreshUIs<T>(Entity<UserInterfaceComponent?> uiEnt) where T : BoundUserInterface, IRefreshableBui
    {
        _toRefresh.Add((uiEnt, static (uiEnt, system) =>
        {
            try
            {
                if (system.TerminatingOrDeleted(uiEnt))
                    return;

                if (!system.Resolve(uiEnt, ref uiEnt.Comp, false))
                    return;

                foreach (var bui in uiEnt.Comp.ClientOpenInterfaces.Values)
                {
                    if (bui is T ui)
                        ui.Refresh();
                }
            }
            catch (Exception e)
            {
                system.Log.Error($"Error refreshing {nameof(T)}\n{e}");
            }
        }));
    }

    public override void Update(float frameTime)
    {
        try
        {
            foreach (var refresh in _toRefresh)
            {
                refresh.Act(refresh.Ent, this);
            }
        }
        finally
        {
            _toRefresh.Clear();
        }
    }
}
