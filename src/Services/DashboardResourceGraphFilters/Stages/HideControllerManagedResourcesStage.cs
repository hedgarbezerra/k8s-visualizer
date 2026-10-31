using KubernetesVisualizer.Services.DashboardResourceGraphFilters.Pipelines;
namespace KubernetesVisualizer.Services.DashboardResourceGraphFilters.Stages;

internal sealed class HideControllerManagedResourcesStage : IDashboardResourceGraphFilterStage
{
    public void Apply(DashboardResourceGraphFilterContext context)
    {
        if (context.Options.HideControllerManagedResources)
            context.CandidateItems = context.CandidateItems.Where(item => !item.IsControllerManaged).ToArray();
    }
}
