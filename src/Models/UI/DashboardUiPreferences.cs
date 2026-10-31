namespace KubernetesVisualizer.Models.UI;

public sealed record DashboardUiPreferences(
    string FunctionalGroup,
    string[] VisibleKinds,
    bool HideKubernetesSystemResources,
    bool HideControllerManagedResources,
    bool HideStandaloneResources,
    string[] CollapsedNodeIds,
    double Zoom,
    bool? HideAutomaticallyGeneratedResources = null);
