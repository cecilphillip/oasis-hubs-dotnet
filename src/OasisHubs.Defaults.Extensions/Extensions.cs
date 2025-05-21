using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Paramore.Brighter;

namespace Microsoft.Extensions.Hosting;

public static class Extensions {
   private const string _healthEndpointPath = "/health";
   private const string _healthLivenessEndpointPath = "/health/live";
   private const string _healthDetailsEndpointPath = "/health/details";

   public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder {
      builder.Services.AddServiceDiscovery();

      builder.Services.ConfigureHttpClientDefaults(http =>
      {
         http.AddStandardResilienceHandler();
         http.AddServiceDiscovery();
      });
      
      

      return builder;
   }
   public static OpenTelemetryBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder, string serviceName)
      where TBuilder : IHostApplicationBuilder {
      builder.Logging.AddOpenTelemetry(logging => {
         logging.IncludeFormattedMessage = true;
         logging.IncludeScopes = true;
      });

      var otelBuilder = builder.Services.AddOpenTelemetry()
         .ConfigureResource(r => r.AddService(serviceName, "oasisHubs", "v2.0.0")
            .AddTelemetrySdk()
            .AddAttributes(new Dictionary<string, object> {
               ["environment.name"] = builder.Environment.EnvironmentName, ["demo.type"] = "oasis.stripe.connect"
            })
            .AddEnvironmentVariableDetector()
         )
         .WithMetrics(metrics => {
            metrics.AddAspNetCoreInstrumentation()
               .AddHttpClientInstrumentation()
               .AddRuntimeInstrumentation();
         })
         .WithTracing(tracing => {
            tracing
               .AddAspNetCoreInstrumentation(tb =>
                  // Exclude health check requests from tracing
                  tb.Filter = context =>
                     !context.Request.Path.StartsWithSegments(_healthEndpointPath)
                     && !context.Request.Path.StartsWithSegments(_healthLivenessEndpointPath)
                     && !context.Request.Path.StartsWithSegments(_healthDetailsEndpointPath)
               )
               .AddHttpClientInstrumentation();
         });
         // .WithLogging(logging => {
         //    logging.AddConsoleExporter();
         // })
      
      var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

      if (useOtlpExporter)
      {
         otelBuilder.UseOtlpExporter();
      }
        
      return otelBuilder;
   }
   
   public static IHealthChecksBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
      where TBuilder : IHostApplicationBuilder {
     var healthChecksBuilder = builder.Services.AddHealthChecks()
         // Add a default liveness check to ensure app is responsive
         .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

      return healthChecksBuilder;
   }

   public static WebApplication MapDefaultEndpoints(this WebApplication app) {
         // All health checks must pass to be considered ready to accept traffic after starting
         app.MapHealthChecks(_healthEndpointPath);

         // Only health checks tagged with "live" tag must pass to be considered alive
         app.MapHealthChecks(_healthLivenessEndpointPath,
            new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") });
         
         // Add a detailed health check endpoint
         app.MapHealthChecks(_healthDetailsEndpointPath, new HealthCheckOptions
         {
            ResponseWriter = async (context, report) =>
            {
               var content = new
               {
                  Status = report.Status.ToString(),
                  Results = report.Entries.ToDictionary(e => e.Key,
                     e => new
                     {
                        Status = e.Value.Status.ToString(),
                        e.Value.Description,
                        e.Value.Duration
                     }),
                  report.TotalDuration
               };

               context.Response.ContentType = "application/json";
               await context.Response.WriteAsync(JsonSerializer.Serialize(content, JsonSerialisationOptions.Options));
            }
         });

      return app;
   }
   
   public static void Deconstruct<T>(this IGrouping<string, T> grouping,
      out string groupKey, out IEnumerable<T> collection) {
      groupKey = grouping.Key;
      collection = grouping;
   }
}
