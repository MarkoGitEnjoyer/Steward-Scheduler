using Scheduler.UI.Components;
using Scheduler.Core;
using Scheduler.Data;
using Scheduler.UI.Controllers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDataLayer(builder.Configuration);
builder.Services.AddCoreServices();

builder.Services.AddScoped<DashboardController>();
builder.Services.AddScoped<ScheduleController>();
builder.Services.AddScoped<StewardController>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();