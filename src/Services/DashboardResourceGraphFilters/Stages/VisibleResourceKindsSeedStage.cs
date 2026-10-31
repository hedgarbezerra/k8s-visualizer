using KubernetesVisualizer.Services.DashboardResourceGraphFilters.Pipelines;
namespace KubernetesVisualizer.Services.DashboardResourceGraphFilters.Stages;

internal sealed class VisibleResourceKindsSeedStage : IDashboardResourceGraphFilterStage
{
    public void Apply(DashboardResourceGraphFilterContext context)
    {
        if (context.VisibleKinds.Count == 0) return;
        context.ActiveIds.UnionWith(context.CandidateItems
            .Where(item => context.VisibleKinds.Contains(item.Kind))
            .Select(item => item.Uid));
    }
}
