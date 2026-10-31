using KubernetesVisualizer.Components;
using KubernetesVisualizer;
using KubernetesVisualizer.Models.Configuration;
using KubernetesVisualizer.Services;
using KubernetesVisualizer.Services.DashboardResourceGraphFilters;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Localization;
using MudBlazor.Services;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMudServices();
builder.Services.AddLocalization();
builder.Services.Configure<GraphGroupingOptions>(builder.Configuration.GetSection(GraphGroupingOptions.SectionName));
builder.Services.AddSingleton<ConnectionProfileStore>();
builder.Services.AddSingleton<ApplicationSettingsStore>();
builder.Services.AddScoped<ApplicationEventManager>();
builder.Services.AddScoped<ClusterService>();
builder.Services.AddScoped<ResourceGraphService>();
builder.Services.AddSingleton<DashboardResourceGraphFilterManager>();
builder.Services.AddScoped<GraphGroupingSettingsService>();

var configuredKeyDirectory = builder.Configuration["DataProtection:KeysDirectory"];
var keyDirectory = string.IsNullOrWhiteSpace(configuredKeyDirectory)
    ? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtection-Keys")
    : Path.GetFullPath(configuredKeyDirectory);
Directory.CreateDirectory(keyDirectory);

var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("KubernetesVisualizer")
    .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));

if (OperatingSystem.IsWindows())
{
    dataProtection.ProtectKeysWithDpapi();
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

var supportedCultures = new[] { new CultureInfo("pt-BR"), new CultureInfo("en") };
const string cultureCookieName = ".KubernetesVisualizer.Culture";
var localizationOptions = new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("pt-BR"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures
};
localizationOptions.RequestCultureProviders.Clear();
localizationOptions.RequestCultureProviders.Add(new CookieRequestCultureProvider { CookieName = cultureCookieName });
app.UseRequestLocalization(localizationOptions);

await using (var storageScope = app.Services.CreateAsyncScope())
{
    await storageScope.ServiceProvider.GetRequiredService<ConnectionProfileStore>().InitializeAsync();
    await storageScope.ServiceProvider.GetRequiredService<ApplicationSettingsStore>().InitializeAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapGet("/culture/set", (HttpContext context, string culture, string? returnUrl) =>
{
    var normalizedCulture = culture.Equals("en", StringComparison.OrdinalIgnoreCase) ? "en" :
        culture.Equals("pt-BR", StringComparison.OrdinalIgnoreCase) ? "pt-BR" : null;
    if (normalizedCulture is null) return Results.BadRequest();

    context.Response.Cookies.Append(cultureCookieName,
        CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(normalizedCulture)),
        new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, HttpOnly = true, SameSite = SameSiteMode.Lax });
    return Results.LocalRedirect(string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl);
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
