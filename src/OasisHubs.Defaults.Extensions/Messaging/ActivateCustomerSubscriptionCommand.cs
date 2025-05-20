using Paramore.Brighter;

namespace OasisHubs.Defaults.Extensions.Messaging;

public class ActivateCustomerSubscriptionCommand : Command {
   public SlimSubscription CustomerSubscription { get; init; }

   public ActivateCustomerSubscriptionCommand(Stripe.Subscription newSubscription) :base(Guid.NewGuid()) {
      this.CustomerSubscription =
         new SlimSubscription(newSubscription.Id, newSubscription.CustomerId);
   }

   public ActivateCustomerSubscriptionCommand() : base(Guid.NewGuid()) {
      this.CustomerSubscription = default!;
   }

   public record SlimSubscription(string StripeSubscriptionId, string StripeCustomerId);
}
