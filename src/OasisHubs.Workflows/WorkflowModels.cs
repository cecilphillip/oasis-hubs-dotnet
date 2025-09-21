namespace OasisHubs.Workflows;

public record SlimInvoice(string InvoiceId,  DateTime Start, DateTime End, long Total);

public record SlimSubscription(string StripeSubscriptionId, string StripeCustomerId);

public record SlimAccount(string StripeAccountId, string Email, bool DetailsSubmitted, bool ChargesEnabled, string CustomerId);
