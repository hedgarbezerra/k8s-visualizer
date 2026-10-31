using KubernetesVisualizer.Services;
using KubernetesVisualizer.Services.DashboardResourceGraphFilters.Pipelines;
namespace KubernetesVisualizer.Services.DashboardResourceGraphFilters.Stages;

internal sealed class HideKubernetesSystemResourcesStage : IDashboardResourceGraphFilterStage
{
    public void Apply(DashboardResourceGraphFilterContext context)
    {
        if (context.Options.HideKubernetesSystemResources)
            context.CandidateItems = context.CandidateItems
                .Where(item => !DashboardResourceVisibilityPolicy.IsKubernetesSystemResource(item)).ToArray();
    }
}
