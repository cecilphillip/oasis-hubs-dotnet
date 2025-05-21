using Microsoft.Extensions.Diagnostics.HealthChecks;
using OasisHubs.BackgroundProcessor;
using Paramore.Brighter.ServiceActivator.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.ConfigureOpenTelemetry("processor") ;
builder.AddDefaultHealthChecks()
   .AddCheck<BrighterServiceActivatorHealthCheck>("Brighter", HealthStatus.Unhealthy);

builder.ConfigureAppServices();

var app = builder.Build();

app.UseHttpsRedirection();
app.MapDefaultEndpoints();

app.Run();
