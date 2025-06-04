using System.Collections.Concurrent;
using System.Threading.Channels;
using OasisHubs.Defaults.Extensions;
using Stripe;
using Stripe.Billing;

namespace OasisHubs.Site.Workers;

public class HubUsageWorker(Channel<HubUsageReport> hubUsageChannel, IServiceProvider provider) : BackgroundService {
   private const int _batchSize = 1;
   private readonly ConcurrentBag<HubUsageReport> _reportedUsage = new();

   protected override async Task ExecuteAsync(CancellationToken stoppingToken) {

      await using var scope = provider.CreateAsyncScope();
      var stripeClient = scope.ServiceProvider.GetRequiredService<StripeClient>();
      
      while (await hubUsageChannel.Reader.WaitToReadAsync(stoppingToken)) {
         var report = await hubUsageChannel.Reader.ReadAsync(stoppingToken);

         _reportedUsage.Add(report);

         if (_reportedUsage.Count >= _batchSize) {
            await ReportUsageBatch(stripeClient);
         }
      }
   }

   private async Task ReportUsageBatch(StripeClient stripeClient) {
      var groupedUsage = _reportedUsage.Take(_reportedUsage.Count)
         .GroupBy(r => r.CustomerId)
         .Select(g => new HubUsageReport(g.Key, g.Sum(r => r.Usage)));

      foreach (var usageReport in groupedUsage) {
         var options = new MeterEventCreateOptions {
            EventName = AppConstants.ReportUsageEventName,
            Payload = new Dictionary<string, string> {
               { AppConstants.ReportUsageEventCustomer, usageReport.CustomerId },
               { AppConstants.ReportUsageEventValue, usageReport.Usage.ToString() },
            },
         };

         await stripeClient.V1.Billing.MeterEvents.CreateAsync(options);
      }

      _reportedUsage.Clear();
   }
}
