using Content.Server.Nutrition.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Server.Nutrition.EntitySystems
{
    public sealed partial class TrashOnSolutionEmptySystem : EntitySystem
    {
        [Dependency] private SharedSolutionContainerSystem _solutionContainerSystem = default!;
        [Dependency] private TagSystem _tagSystem = default!;

        /// <summary>
        ///  Передел на массив чтобы все расходники использующие эту систему могли быть использованы гоблинами в качестве компонентов для крафта как газировка, медипены и т.п.
        /// </summary>
        private static readonly ProtoId<TagPrototype>[] TrashTags = { "Trash", "GoblinPreciousTrash" }; /// Forge-Change

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<TrashOnSolutionEmptyComponent, MapInitEvent>(OnMapInit);
            SubscribeLocalEvent<TrashOnSolutionEmptyComponent, SolutionContainerChangedEvent>(OnSolutionChange);
        }

        public void OnMapInit(Entity<TrashOnSolutionEmptyComponent> entity, ref MapInitEvent args)
        {
            CheckSolutions(entity);
        }

        public void OnSolutionChange(Entity<TrashOnSolutionEmptyComponent> entity, ref SolutionContainerChangedEvent args)
        {
            CheckSolutions(entity);
        }

        public void CheckSolutions(Entity<TrashOnSolutionEmptyComponent> entity)
        {
            if (!EntityManager.HasComponent<SolutionContainerManagerComponent>(entity))
                return;

            if (_solutionContainerSystem.TryGetSolution(entity.Owner, entity.Comp.Solution, out _, out var solution))
                UpdateTags(entity, solution);
        }

        public void UpdateTags(Entity<TrashOnSolutionEmptyComponent> entity, Solution solution)
        {
            if (solution.Volume <= 0)
            {
                _tagSystem.AddTags(entity.Owner, TrashTags); /// Forge-Change
                return;
            }

            _tagSystem.RemoveTags(entity.Owner, TrashTags); /// Forge-Change
        }
    }
}
