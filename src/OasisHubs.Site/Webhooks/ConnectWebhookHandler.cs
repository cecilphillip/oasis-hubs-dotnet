using OasisHubs.Defaults.Extensions.Messaging;
using Paramore.Brighter;
using Stripe.Extensions.AspNetCore;

namespace OasisHubs.Site.Webhooks;

public class ConnectWebhookHandler(IAmACommandProcessor commandProcessor, StripeWebhookContext context)
   : StripeWebhookHandler<ConnectWebhookHandler>(context) {

   public override async Task OnAccountUpdatedAsync(Stripe.Event evt) {
      var updatedAccount = (evt.Data.Object as Stripe.Account)!;
      
      if (updatedAccount is { DetailsSubmitted: true, ChargesEnabled: true }) {
         this.Logger.LogDebug("Activating host account ({AccountId})", updatedAccount.Id);
         await commandProcessor.PostAsync(new ActivateHostAccountCommand(updatedAccount));
      }
   }
}
