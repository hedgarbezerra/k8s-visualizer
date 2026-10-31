using KubernetesVisualizer.Services.DashboardResourceGraphFilters.Pipelines;
namespace KubernetesVisualizer.Services.DashboardResourceGraphFilters.Stages;

internal sealed class HideStandaloneResourcesStage : IDashboardResourceGraphFilterStage
{
    public void Apply(DashboardResourceGraphFilterContext context)
    {
        if (context.Options.HideStandaloneResources)
            context.CandidateItems = context.CandidateItems.Where(item => item.IsControllerManaged).ToArray();
    }
}
