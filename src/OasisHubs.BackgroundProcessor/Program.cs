using OasisHubs.BackgroundProcessor;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.ConfigureOpenTelemetry("processor") ;
builder.AddDefaultHealthChecks();

builder.ConfigureAppServices();

var app = builder.Build();

app.UseHttpsRedirection();
app.MapDefaultEndpoints();

app.Run();
