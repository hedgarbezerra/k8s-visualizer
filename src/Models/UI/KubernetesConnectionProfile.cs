namespace KubernetesVisualizer.Models.UI;

public sealed record KubernetesConnectionProfile(
    string Id,
    string Name,
    string KubeconfigPath,
    string ContextName);

public sealed record KubernetesLastSelection(string? ConnectionId, string? Namespace);

public sealed record KubernetesResourceType(
    string Name,
    string Kind,
    string GroupVersion,
    bool Namespaced,
    string ApiPath);

public sealed record KubernetesResourceItem(
    string Kind,
    string Name,
    string Namespace,
    string Status,
    string GroupVersion,
    string ResourceName,
    bool IsKubernetesSystemManaged = false,
    bool IsControllerManaged = false,
    IReadOnlyDictionary<string, string>? Labels = null,
    IReadOnlyDictionary<string, string>? Annotations = null);

public sealed record DashboardUiPreferences(
    string FunctionalGroup,
    string[] VisibleKinds,
    bool HideKubernetesSystemResources,
    bool HideControllerManagedResources,
    bool HideStandaloneResources,
    string[] CollapsedNodeIds,
    double Zoom);

public sealed record ResourceListUiPreferences(
    string? SelectedResourceKey,
    string Search,
    bool HideKubernetesSystemResources,
    bool HideControllerManagedResources,
    bool HideStandaloneResources);

public enum KubernetesSelectionChangeKind
{
    Connection,
    Cluster,
    Namespace
}

public sealed record KubernetesSelectionChangedEventArgs(
    long Revision,
    KubernetesSelectionChangeKind Kind,
    string? ConnectionId,
    string? Namespace);
