using DmxConsole.Web.Components;
using DmxConsole.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// One console, shared by every connected browser/tablet - same idea as a real lighting
// console with multiple control surfaces looking at the same live show.
builder.Services.AddSingleton<MainViewModel>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// No HTTPS redirect: this runs as a local/LAN console tool, and forcing HTTPS would mean
// self-signed-certificate friction on every tablet that connects to it.

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
