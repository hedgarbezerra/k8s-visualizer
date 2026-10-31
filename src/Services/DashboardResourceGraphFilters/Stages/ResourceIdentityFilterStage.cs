using KubernetesVisualizer.Services.DashboardResourceGraphFilters.Pipelines;
namespace KubernetesVisualizer.Services.DashboardResourceGraphFilters.Stages;

internal sealed class ResourceIdentityFilterStage : IDashboardResourceGraphFilterStage
{
    public void Apply(DashboardResourceGraphFilterContext context) => context.CandidateItems = context.CandidateItems
        .Where(item => !string.IsNullOrWhiteSpace(item.Uid))
        .GroupBy(item => item.Uid, StringComparer.Ordinal)
        .Select(group => group.First())
        .ToArray();
}
