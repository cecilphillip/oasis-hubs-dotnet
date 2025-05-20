namespace OasisHubs.Defaults.Extensions;

public static class AppConstants {
      public const string OASIS_SUBSCRIPTION_ACTIVE = "oasis.subscription.active";
      public const string OASIS_USER_TYPE = "oasis.user.type";
      
      public const string StripeCustomerIdClaimType = "stripe.customer.id";
   
      public const string ReportUsageEventName = "hub_usage";
      public const string ReportUsageEventValue = "hours";
      public const string ReportUsageEventCustomer = "stripe_customer_id";
}

public record HubUsageReport(string CustomerId, int Usage);
