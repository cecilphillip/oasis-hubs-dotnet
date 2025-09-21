using OasisHubs.Site;
using OasisHubs.Site.Webhooks;
using Stripe.Extensions.AspNetCore;
using Temporalio.Extensions.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddDefaultHealthChecks();

builder.ConfigureOpenTelemetry("initializer")
   .WithTracing(tracing => {
      tracing.AddSource(
         TracingInterceptor.ClientSource.Name,
         TracingInterceptor.WorkflowsSource.Name,
         TracingInterceptor.ActivitiesSource.Name);
   });

builder.ConfigureAppServices();

var app = builder.Build();
app.ConfigurePipeline();

app.MapStripeWebhookHandler<PlatformWebhookHandler>("/webhooks/stripe/platform");
app.MapStripeWebhookHandler<ConnectWebhookHandler>("/webhooks/stripe/connect");

app.Run();
