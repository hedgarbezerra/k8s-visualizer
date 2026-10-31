using KubernetesVisualizer.Services.DashboardResourceGraphFilters.Pipelines;
namespace KubernetesVisualizer.Services.DashboardResourceGraphFilters.Stages;

internal sealed class VisibleResourceKindsOutputStage : IDashboardResourceGraphFilterStage
{
    public void Apply(DashboardResourceGraphFilterContext context)
    {
        context.Items = context.CandidateItems
            .Where(item => context.ActiveIds.Contains(item.Uid)
                           && (context.VisibleKinds.Count == 0 || context.VisibleKinds.Contains(item.Kind)))
            .ToArray();
        context.Edges = context.Relations!.Edges
            .Where(edge => context.ActiveIds.Contains(edge.SourceId) && context.ActiveIds.Contains(edge.TargetId))
            .ToArray();
    }
}
