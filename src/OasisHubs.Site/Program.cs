using OasisHubs.Site;
using OasisHubs.Site.Webhooks;
using Stripe.Extensions.AspNetCore;


var builder = WebApplication.CreateBuilder(args);

   builder.AddServiceDefaults();
   builder.ConfigureOpenTelemetry("initializer") ;
   builder.AddDefaultHealthChecks();
   
   builder.ConfigureAppServices();

   var app = builder.Build();
   app.ConfigurePipeline();
   
   app.MapStripeWebhookHandler<PlatformWebhookHandler>("/webhooks/stripe/platform");
   app.MapStripeWebhookHandler<ConnectWebhookHandler>("/webhooks/stripe/connect");
      
   app.Run();
