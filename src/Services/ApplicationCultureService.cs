namespace KubernetesVisualizer.Services;

public sealed class ApplicationCultureService
{
    private long _revision;

    public event Action<ApplicationCultureChangedEventArgs>? CultureChanged;

    public void NotifyCultureChanged(string culture) =>
        CultureChanged?.Invoke(new ApplicationCultureChangedEventArgs(++_revision, culture));
}

public sealed record ApplicationCultureChangedEventArgs(long Revision, string Culture);
