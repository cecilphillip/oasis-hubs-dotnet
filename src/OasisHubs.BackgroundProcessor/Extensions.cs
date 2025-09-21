using OasisHubs.DbModels;
using OasisHubs.Defaults.Extensions;
using OasisHubs.Workflows;
using Temporalio.Extensions.Hosting;
using Temporalio.Extensions.OpenTelemetry;

namespace OasisHubs.BackgroundProcessor;

internal static class Extensions {
   public static WebApplicationBuilder ConfigureAppServices(this WebApplicationBuilder builder) {

      builder.AddNpgsqlDbContext<OasisHubsDbContext>("OasisHubsDb");
      builder.Services.AddStripe();
      
      var temporalConnectionString = builder.Configuration.GetConnectionString("temporal") ?? "localhost:7233";
      builder.Services
         .AddTemporalClient(opts => {
            opts.TargetHost = temporalConnectionString;
            opts.Namespace = AppConstants.TemporalNamespace;
            opts.Interceptors = [new TracingInterceptor()];
         })
         .AddHostedTemporalWorker(AppConstants.TemporalTaskQueue)
         .AddWorkflow<ActivateCustomerSubscriptionWorkflow>()
         .AddWorkflow<ActivateHostWorkflow>()
         .AddWorkflow<FundsTransferWorkflow>()
         .AddScopedActivities<FundsTransferActivities>()
         .AddScopedActivities<ActivateHostActivities>()
         .AddScopedActivities<CustomerSubscriptionActivities>();
      
      return builder;
   }
}
