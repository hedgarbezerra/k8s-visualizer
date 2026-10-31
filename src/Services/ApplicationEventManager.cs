using KubernetesVisualizer.Models.UI;

namespace KubernetesVisualizer.Services;

/// <summary>Coordinates application operations and publishes their UI events to the current Blazor circuit.</summary>
public sealed class ApplicationEventManager(
    ClusterService clusterService,
    GraphGroupingSettingsService graphGroupingSettings,
    ApplicationSettingsStore applicationSettings)
{
    private long _selectionRevision;
    private long _cultureRevision;
    private bool _restoreAttempted;

    public event Action? Changed;
    public event Action<KubernetesSelectionChangedEventArgs>? SelectionChanged;
    public event Action? RelatedLabelSettingsChanged;
    public event Action<ApplicationCultureChangedEventArgs>? CultureChanged;

    public IReadOnlyList<string> DefaultRelatedLabelKeys => graphGroupingSettings.DefaultRelatedLabelKeys;

    public async Task EnsureProfilesLoadedAsync(CancellationToken cancellationToken = default)
    {
        await clusterService.EnsureProfilesLoadedAsync(cancellationToken);
        Changed?.Invoke();
    }

    public async Task RestoreLastSelectionAsync(CancellationToken cancellationToken = default)
    {
        await EnsureProfilesLoadedAsync(cancellationToken);
        if (_restoreAttempted) return;
        _restoreAttempted = true;

        var selection = await applicationSettings.LoadLastSelectionAsync(cancellationToken);
        if (selection.ConnectionId is null
            || clusterService.Profiles.All(profile => profile.Id != selection.ConnectionId)) return;

        await ConnectAsync(selection.ConnectionId, cancellationToken);
        if (!clusterService.IsProfileConnected(selection.ConnectionId)) return;

        var restoredNamespace = selection.Namespace;
        if (restoredNamespace is not null && clusterService.Namespaces.Count > 0
            && !clusterService.Namespaces.Contains(restoredNamespace, StringComparer.Ordinal))
            restoredNamespace = null;

        await clusterService.SelectNamespaceAsync(restoredNamespace, cancellationToken);
        Changed?.Invoke();
        PublishSelectionChanged(KubernetesSelectionChangeKind.Namespace);
    }

    public async Task LoadContextsFromPathAsync(string? path, CancellationToken cancellationToken = default)
    {
        var operation = clusterService.LoadContextsFromPathAsync(path, cancellationToken);
        Changed?.Invoke();
        await operation;
        Changed?.Invoke();
    }

    public async Task AddProfileAsync(string name, string kubeconfigPath, string contextName,
        CancellationToken cancellationToken = default)
    {
        await clusterService.AddProfileAsync(name, kubeconfigPath, contextName, cancellationToken);
        Changed?.Invoke();
    }

    public async Task RemoveProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var before = SnapshotSelection();
        await clusterService.RemoveProfileAsync(profileId, cancellationToken);
        Changed?.Invoke();
        if (before != SnapshotSelection()) PublishSelectionChanged(KubernetesSelectionChangeKind.Cluster);
        Changed?.Invoke();
    }

    public async Task ConnectAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var needsConnection = !clusterService.IsProfileConnected(profileId);
        var operation = clusterService.ConnectAsync(profileId, cancellationToken);
        if (needsConnection) Changed?.Invoke();
        await operation;
        Changed?.Invoke();
        if (clusterService.IsProfileConnected(profileId))
            PublishSelectionChanged(KubernetesSelectionChangeKind.Connection);
    }

    public async Task DisconnectAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var before = SnapshotSelection();
        await clusterService.DisconnectAsync(profileId, cancellationToken);
        PublishClusterChanges(before, KubernetesSelectionChangeKind.Cluster);
    }

    public async Task SelectConnectionAsync(string? profileId, CancellationToken cancellationToken = default)
    {
        var before = SnapshotSelection();
        await clusterService.SelectConnectionAsync(profileId, cancellationToken);
        PublishSelectionIfChanged(before, KubernetesSelectionChangeKind.Cluster);
    }

    public async Task SelectNamespaceAsync(string? namespaceName, CancellationToken cancellationToken = default)
    {
        var before = SnapshotSelection();
        await clusterService.SelectNamespaceAsync(namespaceName, cancellationToken);
        PublishSelectionIfChanged(before, KubernetesSelectionChangeKind.Namespace);
    }

    public Task<IReadOnlyList<string>> GetRelatedLabelKeysAsync(CancellationToken cancellationToken = default) =>
        graphGroupingSettings.GetRelatedLabelKeysAsync(cancellationToken);

    public Task<string?> GetApplicationSettingAsync(string key, CancellationToken cancellationToken = default) =>
        applicationSettings.GetApplicationSettingAsync(key, cancellationToken);

    public Task SetApplicationSettingAsync(string key, string? value, CancellationToken cancellationToken = default) =>
        applicationSettings.SetApplicationSettingAsync(key, value, cancellationToken);

    public async Task SaveRelatedLabelKeysAsync(IEnumerable<string> labelKeys, CancellationToken cancellationToken = default)
    {
        await graphGroupingSettings.SaveRelatedLabelKeysAsync(labelKeys, cancellationToken);
        RelatedLabelSettingsChanged?.Invoke();
    }

    public Task<string> ApplyManifestYamlAsync(string yaml, CancellationToken cancellationToken = default) =>
        CoordinateAsync(() => clusterService.ApplyManifestYamlAsync(yaml, cancellationToken));

    public Task DeleteResourceAsync(KubernetesResourceItem item, CancellationToken cancellationToken = default) =>
        CoordinateAsync(() => clusterService.DeleteResourceAsync(item, cancellationToken));

    public Task StopWorkloadAsync(KubernetesResourceItem item, CancellationToken cancellationToken = default) =>
        CoordinateAsync(() => clusterService.StopWorkloadAsync(item, cancellationToken));

    public Task ResumeWorkloadAsync(KubernetesResourceItem item, CancellationToken cancellationToken = default) =>
        CoordinateAsync(() => clusterService.ResumeWorkloadAsync(item, cancellationToken));

    public Task<IReadOnlyList<KubernetesResourceActionResult>> DeleteResourcesAsync(
        IReadOnlyList<KubernetesResourceItem> items, CancellationToken cancellationToken = default) =>
        CoordinateAsync(() => clusterService.DeleteResourcesAsync(items, cancellationToken));

    public Task<IReadOnlyList<KubernetesResourceActionResult>> StopResourcesAsync(
        IReadOnlyList<KubernetesResourceItem> items, CancellationToken cancellationToken = default) =>
        CoordinateAsync(() => clusterService.StopResourcesAsync(items, cancellationToken));

    public Task<IReadOnlyList<KubernetesResourceActionResult>> ResumeResourcesAsync(
        IReadOnlyList<KubernetesResourceItem> items, CancellationToken cancellationToken = default) =>
        CoordinateAsync(() => clusterService.ResumeResourcesAsync(items, cancellationToken));

    public void NotifyCultureChanged(string culture) =>
        CultureChanged?.Invoke(new ApplicationCultureChangedEventArgs(++_cultureRevision, culture));

    private (string? ConnectionId, string? Namespace) SnapshotSelection() =>
        (clusterService.ActiveConnectionId, clusterService.CurrentNamespace);

    private async Task CoordinateAsync(Func<Task> operation)
    {
        try { await operation(); }
        finally { Changed?.Invoke(); }
    }

    private async Task<T> CoordinateAsync<T>(Func<Task<T>> operation)
    {
        try { return await operation(); }
        finally { Changed?.Invoke(); }
    }

    private void PublishClusterChanges((string? ConnectionId, string? Namespace) before,
        KubernetesSelectionChangeKind selectionKind)
    {
        Changed?.Invoke();
        if (before != SnapshotSelection()) PublishSelectionChanged(selectionKind);
    }

    private void PublishSelectionIfChanged((string? ConnectionId, string? Namespace) before,
        KubernetesSelectionChangeKind selectionKind)
    {
        if (before == SnapshotSelection()) return;
        Changed?.Invoke();
        PublishSelectionChanged(selectionKind);
    }

    private void PublishSelectionChanged(KubernetesSelectionChangeKind selectionKind)
    {
        var after = SnapshotSelection();
        SelectionChanged?.Invoke(new KubernetesSelectionChangedEventArgs(
            ++_selectionRevision, selectionKind, after.ConnectionId, after.Namespace));
    }
}
