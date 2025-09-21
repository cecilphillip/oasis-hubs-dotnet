using OasisHubs.Defaults.Extensions;
using OasisHubs.Workflows;
using Stripe;
using Stripe.Extensions.AspNetCore;
using Temporalio.Client;

namespace OasisHubs.Site.Webhooks;

public class ConnectWebhookHandler(ITemporalClient temporalClient, StripeWebhookContext context)
   : StripeWebhookHandler<ConnectWebhookHandler>(context) {
   public override async Task OnAccountUpdatedAsync(Event evt) {
      var updatedAccount = (evt.Data.Object as Account)!;

      if (updatedAccount is { DetailsSubmitted: true, ChargesEnabled: true }) {
         this.Logger.LogDebug("Activating host account ({AccountId})", updatedAccount.Id);

         var hostAccountData = new SlimAccount(updatedAccount.Id, updatedAccount.Email,
            updatedAccount.DetailsSubmitted, updatedAccount.ChargesEnabled,
            updatedAccount.Metadata.GetValueOrDefault("owner.customer.id", string.Empty));

         await temporalClient.StartWorkflowAsync<ActivateHostWorkflow>(w => w.RunAsync(hostAccountData),
            new() {
               Id = $"oasis-activate-host-{Guid.NewGuid():N}", 
               TaskQueue = AppConstants.TemporalTaskQueue
            });
      }
   }
}
