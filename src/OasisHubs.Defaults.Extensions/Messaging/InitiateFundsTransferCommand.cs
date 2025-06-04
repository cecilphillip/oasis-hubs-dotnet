using Paramore.Brighter;

namespace OasisHubs.Defaults.Extensions.Messaging;

public class InitiateFundsTransferCommand : Command {
   public SlimInvoice Invoice { get; init; }
   public InitiateFundsTransferCommand(Stripe.Invoice invoice) : base(Guid.NewGuid()) {
      this.Invoice = new SlimInvoice(invoice.Id, invoice.PeriodStart, invoice.PeriodEnd, invoice.Total);
   }
   public InitiateFundsTransferCommand() : base(Guid.NewGuid()) {
      this.Invoice = default!;
   }

   public record SlimInvoice(string InvoiceId,  DateTime Start, DateTime End, long Total);
}
