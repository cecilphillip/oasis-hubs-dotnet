using OasisHubs.BackgroundProcessor;
using Temporalio.Extensions.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddDefaultHealthChecks();

builder.ConfigureOpenTelemetry("processor")
   .WithTracing(tracing => {
      tracing.AddSource(
         TracingInterceptor.ClientSource.Name,
         TracingInterceptor.WorkflowsSource.Name,
         TracingInterceptor.ActivitiesSource.Name);
   });

builder.ConfigureAppServices();

var app = builder.Build();
app.MapDefaultEndpoints();
app.Run();
