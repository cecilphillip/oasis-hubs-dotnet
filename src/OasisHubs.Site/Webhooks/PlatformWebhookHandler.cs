using OasisHubs.Defaults.Extensions.Messaging;
using Paramore.Brighter;
using Stripe;
using Stripe.Extensions.AspNetCore;

namespace OasisHubs.Site.Webhooks;

public class PlatformWebhookHandler(IAmACommandProcessor commandProcessor, StripeWebhookContext context)
   : StripeWebhookHandler<PlatformWebhookHandler>(context) {

   public override async Task OnInvoicePaidAsync(Stripe.Event evt) {
      var invoice = (evt.Data.Object as Invoice)!;
      
      if (invoice is { Status: "paid" }) {
         this.Logger.LogDebug("Initiating funds transfer for paid invoice ({InvoiceId})",invoice.Id);
         await commandProcessor.PostAsync(new InitiateFundsTransferCommand(invoice));
      }
   }

   public override async Task OnCheckoutSessionCompletedAsync(Stripe.Event evt) {
      var checkoutSession = (evt.Data.Object as Stripe.Checkout.Session)!;
          
      if (checkoutSession is { Status: "complete", PaymentStatus: "paid" }) {
         
         this.Logger.LogDebug( "A new stripe subscription has been activated ({SubscriptionId})",
            checkoutSession.Id);
         await Task.CompletedTask;
         //await commandProcessor.PostAsync(new ActivateCustomerSubscriptionCommand(checkoutSession));
      }
   }
   
   public override async Task OnCustomerSubscriptionCreatedAsync(Stripe.Event evt) {
      var newSubscription = (evt.Data.Object as Stripe.Subscription)!;
      
      if (newSubscription is { Status: "active" }) {
         
         this.Logger.LogDebug( "A new stripe subscription is active ({SubscriptionId})",
            newSubscription.Id);
         await commandProcessor.PostAsync(new ActivateCustomerSubscriptionCommand(newSubscription));
      }
      
   }

   public override async Task OnCustomerSubscriptionUpdatedAsync(Stripe.Event evt) {
      
      var updatedSubscription = (evt.Data.Object as Stripe.Subscription)!;
      if (updatedSubscription is { Status: "active" }) {
         
         this.Logger.LogDebug( "A stripe subscription has been updated to active ({SubscriptionId})",
            updatedSubscription.Id);
         
         await commandProcessor.PostAsync(new ActivateCustomerSubscriptionCommand(updatedSubscription));
      }
   }
}
