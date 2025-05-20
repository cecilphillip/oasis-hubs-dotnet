using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OasisHubs.DbModels;
using OasisHubs.Defaults.Extensions.Messaging;
using Paramore.Brighter;
using Paramore.Brighter.Inbox;
using Paramore.Brighter.Inbox.Attributes;
using Stripe;

namespace OasisHubs.BackgroundProcessor.Handlers;

public class
   InitiateFundsTransferRequestHandler : RequestHandlerAsync<InitiateFundsTransferCommand> {
   private readonly OasisHubsDbContext _dbContext;
   private readonly StripeClient _stripeClient;
   private readonly ILogger<InitiateFundsTransferRequestHandler> _logger;

   private const decimal DISTRIBUTABLE_PERCENTAGE = 0.75m;

   public InitiateFundsTransferRequestHandler(
      OasisHubsDbContext dbContext, StripeClient stripeClient,
      ILogger<InitiateFundsTransferRequestHandler> logger) {
      this._dbContext = dbContext;
      this._stripeClient = stripeClient;
      this._logger = logger;
   }

  [UseInboxAsync(step:0, contextKey: typeof(InitiateFundsTransferRequestHandler), onceOnly: true, onceOnlyAction: OnceOnlyAction.Warn)]
   public override async Task<InitiateFundsTransferCommand> HandleAsync(
      InitiateFundsTransferCommand command,
      CancellationToken cancellationToken = new()) {
      if (command.Invoice == null) {
         this._logger.LogError("Invoice information missing from command");
         throw new ArgumentNullException(nameof(command), "Account information missing.");
      }
      
      var invoiceBookings = await this._dbContext.Bookings
         .Include(booking => booking.Renter)
         .Include(booking => booking.Rental)
         .Where(b =>
            b.ReservedDateUtc > command.Invoice.Start &&
            b.ReservedDateUtc < command.Invoice.End)
         .ToListAsync(cancellationToken: cancellationToken);

      var distributableTotal = command.Invoice.Total * DISTRIBUTABLE_PERCENTAGE;
      var totalReportedHours = invoiceBookings.Sum(i => i.Hours);

      this._logger.LogInformation("Processing invoice {InvoiceId} with total hours {TotalHours}",
         command.Invoice.InvoiceId, totalReportedHours);

      var transferService = new TransferService(this._stripeClient);
      if (invoiceBookings.Any()) {
         var groupedBookings = invoiceBookings.GroupBy(b => b.Rental.StripeAccountId);

         foreach (var (accountId, bookings) in groupedBookings) {
            decimal reportedAccountHours = bookings.Sum(b => b.Hours);
            var accountHoursPercentage = reportedAccountHours / totalReportedHours;
            var transferAmount = Convert.ToInt64(distributableTotal * accountHoursPercentage);

            var transOptions = new TransferCreateOptions {
               Amount = transferAmount,
               Currency = "usd",
               Destination = accountId,
               //SourceTransaction = command.Invoice.,
               Metadata = new Dictionary<string, string> {
                  ["invoice.id"] = command.Invoice.InvoiceId,
                  ["invoice.hours.total"] = totalReportedHours.ToString(CultureInfo.InvariantCulture),
                  ["invoice.hours.account_reported"] = reportedAccountHours.ToString(CultureInfo.InvariantCulture),
                  ["invoice.hours.percentage"] = accountHoursPercentage.ToString(CultureInfo.InvariantCulture)
               }
            };

            //TODO: Catch Stripe exception
            await transferService.CreateAsync(transOptions,cancellationToken: cancellationToken);

            this._logger.LogInformation(
               "Transfer initiated for ${TransferAmount} to account ({ConnectAccountId}) from invoice ({InvoiceId})",
               (transferAmount / 100m), accountId, command.Invoice.InvoiceId);
         }
      }
      else {
         this._logger.LogWarning("No bookings found for invoice billing period {InvoiceId}",
            command.Invoice.InvoiceId);
      }

      return await base.HandleAsync(command, cancellationToken);
   }
}
