using KubernetesVisualizer.Services.DashboardResourceGraphFilters.Pipelines;
namespace KubernetesVisualizer.Services.DashboardResourceGraphFilters.Stages;

internal interface IDashboardResourceGraphFilterStage
{
    void Apply(DashboardResourceGraphFilterContext context);
}
