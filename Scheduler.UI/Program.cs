using Scheduler.UI.Components;
using Scheduler.Core;
using Scheduler.Data;
using Scheduler.UI.Controllers;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add Scheduler.Data and Scheduler.Core services
builder.Services.AddDataLayer(builder.Configuration);
builder.Services.AddCoreServices();

// Register MVC controllers
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