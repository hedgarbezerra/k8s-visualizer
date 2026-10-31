using KubernetesVisualizer.Models.UI;

namespace KubernetesVisualizer.Services;

/// <summary>Centralizes the curated visibility rules used by the graph dashboard.</summary>
public static class DashboardResourceVisibilityPolicy
{
    private static readonly HashSet<string> KubernetesSystemKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "APIService", "ClusterRole", "ClusterRoleBinding", "FlowSchema", "PriorityLevelConfiguration",
        "MutatingWebhookConfiguration", "ValidatingWebhookConfiguration", "CustomResourceDefinition"
    };

    private static readonly HashSet<string> AutomaticallyGeneratedSupportKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "ComponentStatus", "ControllerRevision", "EndpointSlice", "Endpoints", "Event", "IPAddress", "Lease",
        "NodeMetrics", "PodMetrics", "VolumeAttachment"
    };

    public static bool IsAutomaticallyGeneratedSupportResource(KubernetesResourceGraphItem item) =>
        AutomaticallyGeneratedSupportKinds.Contains(item.Kind)
        || item.Kind.Equals("Service", StringComparison.OrdinalIgnoreCase)
           && item.Namespace.Equals("default", StringComparison.OrdinalIgnoreCase)
           && item.Name.Equals("kubernetes", StringComparison.OrdinalIgnoreCase);

    public static bool IsKubernetesSystemResource(KubernetesResourceGraphItem item) =>
        item.IsKubernetesSystemManaged || KubernetesSystemKinds.Contains(item.Kind);

    public static bool IsVisible(KubernetesResourceGraphItem item, bool hideSystemResources,
        bool hideControllerManagedResources, bool hideStandaloneResources, bool hideAutomaticallyGeneratedResources) =>
        (!hideSystemResources || !IsKubernetesSystemResource(item))
        && (!hideControllerManagedResources || !item.IsControllerManaged)
        && (!hideStandaloneResources || item.IsControllerManaged)
        && (!hideAutomaticallyGeneratedResources || !IsAutomaticallyGeneratedSupportResource(item));
}
