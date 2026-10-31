using KubernetesVisualizer.Services.DashboardResourceGraphFilters.Pipelines;
namespace KubernetesVisualizer.Services.DashboardResourceGraphFilters.Stages;

internal sealed class AvailableResourceKindsStage : IDashboardResourceGraphFilterStage
{
    public void Apply(DashboardResourceGraphFilterContext context)
    {
        context.AvailableKinds = context.CandidateItems
            .Select(item => item.Kind)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        context.VisibleKinds.IntersectWith(context.AvailableKinds);
    }
}
