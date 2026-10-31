using KubernetesVisualizer.Models.UI;

namespace KubernetesVisualizer.Services.DashboardResourceGraphFilters.Stages;

public static class ResourceFunctionalGroupClassifier
{
    private static readonly HashSet<string> BuiltInApiGroups = new(StringComparer.Ordinal)
    {
        "apps", "batch", "autoscaling", "networking.k8s.io", "discovery.k8s.io", "storage.k8s.io",
        "rbac.authorization.k8s.io", "policy", "scheduling.k8s.io", "coordination.k8s.io", "events.k8s.io",
        "certificates.k8s.io", "admissionregistration.k8s.io", "apiregistration.k8s.io", "node.k8s.io",
        "metrics.k8s.io", "resource.k8s.io", "flowcontrol.apiserver.k8s.io", "authentication.k8s.io",
        "authorization.k8s.io"
    };

    public static string GetGroup(KubernetesResourceGraphItem item)
    {
        var mapped = item.Kind switch
        {
            "Node" or "RuntimeClass" => "Infrastructure",
            "Namespace" => "Organization",
            "Deployment" or "ReplicaSet" or "StatefulSet" or "DaemonSet" or "ReplicationController" or "Pod" => "Workloads",
            "Job" or "CronJob" => "Processing",
            "HorizontalPodAutoscaler" or "VerticalPodAutoscaler" => "Scaling",
            "Service" or "Endpoints" or "EndpointSlice" or "Ingress" or "IngressClass" or "Gateway" or "GatewayClass"
                or "HTTPRoute" or "GRPCRoute" or "TCPRoute" or "TLSRoute" or "UDPRoute" or "ReferenceGrant" or "BackendTLSPolicy" => "Networking",
            "PersistentVolume" or "PersistentVolumeClaim" or "StorageClass" or "VolumeAttachment" or "CSIDriver" or "CSINode"
                or "CSIStorageCapacity" or "VolumeSnapshot" or "VolumeSnapshotClass" or "VolumeSnapshotContent" => "Storage",
            "ConfigMap" or "Secret" => "Configuration",
            "ServiceAccount" or "Role" or "ClusterRole" or "RoleBinding" or "ClusterRoleBinding" or "CertificateSigningRequest" => "Security",
            "NetworkPolicy" or "ResourceQuota" or "LimitRange" or "PriorityClass" or "PodDisruptionBudget" or "ValidatingAdmissionPolicy"
                or "ValidatingAdmissionPolicyBinding" => "Policies",
            "CustomResourceDefinition" or "MutatingWebhookConfiguration" or "ValidatingWebhookConfiguration" or "APIService" => "Extensibility",
            "Event" or "Lease" or "ComponentStatus" or "PodMetrics" or "NodeMetrics" => "Operations",
            _ => string.Empty
        };
        if (mapped.Length > 0) return mapped;

        var apiGroup = item.GroupVersion.Contains('/') ? item.GroupVersion[..item.GroupVersion.IndexOf('/')] : string.Empty;
        return apiGroup.Length == 0 || BuiltInApiGroups.Contains(apiGroup) ? "Operations" : "Extensibility";
    }
}
