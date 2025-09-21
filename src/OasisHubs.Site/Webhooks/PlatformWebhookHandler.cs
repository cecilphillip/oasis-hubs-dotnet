using OasisHubs.Defaults.Extensions;
using OasisHubs.Workflows;
using Stripe;
using Stripe.Extensions.AspNetCore;
using Temporalio.Client;

namespace OasisHubs.Site.Webhooks;

public class PlatformWebhookHandler( ITemporalClient temporalClient, StripeWebhookContext context)
   : StripeWebhookHandler<PlatformWebhookHandler>(context) {

   public override async Task OnInvoicePaidAsync(Event evt) {
      var invoice = (evt.Data.Object as Invoice)!;
      
      if (invoice is { Status: "paid" }) {
         this.Logger.LogDebug("Initiating funds transfer for paid invoice ({InvoiceId})",invoice.Id);
        
         var invoiceData = new SlimInvoice(invoice.Id, invoice.PeriodStart, invoice.PeriodEnd, invoice.Total);
         await temporalClient.StartWorkflowAsync<FundsTransferWorkflow>(w => w.RunAsync(invoiceData),
            new() {
               Id = $"oasis-funds-transfer-{Guid.NewGuid():N}", 
               TaskQueue = AppConstants.TemporalTaskQueue
            });
      }
   }

   public override async Task OnCheckoutSessionCompletedAsync(Event evt) {
      var checkoutSession = (evt.Data.Object as Stripe.Checkout.Session)!;
          
      if (checkoutSession is { Status: "complete", PaymentStatus: "paid" }) {
         
         this.Logger.LogDebug( "A new stripe subscription has been activated ({SubscriptionId})",
            checkoutSession.Id);
        
         await Task.CompletedTask;
      }
   }
   
   public override async Task OnCustomerSubscriptionCreatedAsync(Event evt) {
      var newSubscription = (evt.Data.Object as Subscription)!;
      
      if (newSubscription is { Status: "active" }) {
         
         this.Logger.LogDebug( "A new stripe subscription is active ({SubscriptionId})",
            newSubscription.Id);
         
         var subscriptionData = new SlimSubscription(newSubscription.Id, newSubscription.CustomerId);
         await temporalClient.StartWorkflowAsync<ActivateCustomerSubscriptionWorkflow>(w => w.RunAsync(subscriptionData),
            new() {
               Id = $"oasis-activate-customer-subscription-{Guid.NewGuid():N}", 
               TaskQueue = AppConstants.TemporalTaskQueue
            });
      }
   }

   public override async Task OnCustomerSubscriptionUpdatedAsync(Event evt) {
      
      var updatedSubscription = (evt.Data.Object as Subscription)!;
      if (updatedSubscription is { Status: "active" }) {
         
         this.Logger.LogDebug( "A stripe subscription has been updated to active ({SubscriptionId})",
            updatedSubscription.Id);
         
         var subscriptionData = new SlimSubscription(updatedSubscription.Id, updatedSubscription.CustomerId);
         await temporalClient.StartWorkflowAsync<ActivateCustomerSubscriptionWorkflow>(w => w.RunAsync(subscriptionData),
            new() {
               Id = $"oasis-update-customer-subscription-{Guid.NewGuid():N}", 
               TaskQueue = AppConstants.TemporalTaskQueue
            });
      }
   }
}
