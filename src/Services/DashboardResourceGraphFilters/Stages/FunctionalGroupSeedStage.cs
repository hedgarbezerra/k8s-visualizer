using KubernetesVisualizer.Services.DashboardResourceGraphFilters.Pipelines;
namespace KubernetesVisualizer.Services.DashboardResourceGraphFilters.Stages;

internal sealed class FunctionalGroupSeedStage : IDashboardResourceGraphFilterStage
{
    public void Apply(DashboardResourceGraphFilterContext context)
    {
        var options = context.Options;
        if (options.FunctionalGroup == "All" && context.VisibleKinds.Count == 0)
        {
            context.ActiveIds.UnionWith(context.CandidateItems.Select(item => item.Uid));
            return;
        }

        context.ActiveIds.UnionWith(context.CandidateItems
            .Where(item => (options.FunctionalGroup == "All" || ResourceFunctionalGroupClassifier.GetGroup(item) == options.FunctionalGroup)
                           && (context.VisibleKinds.Count == 0 || context.VisibleKinds.Contains(item.Kind)))
            .Select(item => item.Uid));
    }
}
