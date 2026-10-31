using KubernetesVisualizer.Services;
using KubernetesVisualizer.Services.DashboardResourceGraphFilters.Pipelines;
namespace KubernetesVisualizer.Services.DashboardResourceGraphFilters.Stages;

internal sealed class HideAutomaticallyGeneratedResourcesStage : IDashboardResourceGraphFilterStage
{
    public void Apply(DashboardResourceGraphFilterContext context)
    {
        if (context.Options.HideAutomaticallyGeneratedResources)
            context.CandidateItems = context.CandidateItems
                .Where(item => !DashboardResourceVisibilityPolicy.IsAutomaticallyGeneratedSupportResource(item)).ToArray();
    }
}
