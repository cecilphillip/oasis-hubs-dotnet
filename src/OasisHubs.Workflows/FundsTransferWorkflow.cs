using System.Globalization;
using DotNext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OasisHubs.DbModels;
using Stripe;
using Temporalio.Activities;
using Temporalio.Workflows;

namespace OasisHubs.Workflows;

[Workflow]
public class FundsTransferWorkflow {
   
   [WorkflowQuery]
   public string WorkflowStatus { get; set; } = "Workflow not started";

   [WorkflowRun]
   public async Task RunAsync(SlimInvoice customerInvoice) {
      WorkflowStatus = "Workflow started";
      
      var activityResult = await Workflow.ExecuteActivityAsync<FundsTransferActivities, Result<bool>>(
         x => x.TransferHostFundsAsync(customerInvoice),
         new() { StartToCloseTimeout = TimeSpan.FromSeconds(30) });
      
      if (!activityResult) {
         const string msg = "Unable to transfer host funds";
         Workflow.Logger.LogWarning(msg);
         WorkflowStatus = msg;
         return;
      }
      
      WorkflowStatus = "Workflow completed";
   }
}

public class FundsTransferActivities(  OasisHubsDbContext dbContext, StripeClient stripeClient,
   ILogger<FundsTransferActivities> logger) {
   
   private const decimal DISTRIBUTABLE_PERCENTAGE = 0.75m;
   
   [Activity]
   public async Task<Result<bool>> TransferHostFundsAsync(SlimInvoice customerInvoice) {
      
       var invoiceBookings = await dbContext.Bookings
         .Include(booking => booking.Renter)
         .Include(booking => booking.Rental)
         .Where(b =>
            b.ReservedDateUtc > customerInvoice.Start &&
            b.ReservedDateUtc < customerInvoice.End)
         .ToListAsync();

      if (invoiceBookings.Count == 0) {
         logger.LogWarning("No bookings found for invoice billing period {InvoiceId}",
            customerInvoice.InvoiceId);

         return Result.FromValue(false);
      }

      var distributableTotal = customerInvoice.Total * DISTRIBUTABLE_PERCENTAGE;
      var totalReportedHours = invoiceBookings.Sum(i => i.Hours);

      logger.LogInformation("Processing invoice {InvoiceId} with total hours {TotalHours}",
         customerInvoice.InvoiceId, totalReportedHours);
      
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
               ["invoice.id"] = customerInvoice.InvoiceId,
               ["invoice.hours.total"] = totalReportedHours.ToString(CultureInfo.InvariantCulture),
               ["invoice.hours.account_reported"] = reportedAccountHours.ToString(CultureInfo.InvariantCulture),
               ["invoice.hours.percentage"] = accountHoursPercentage.ToString(CultureInfo.InvariantCulture)
            }
         };
         
         await stripeClient.V1.Transfers.CreateAsync(transOptions);

         logger.LogInformation(
            "Transfer initiated for ${TransferAmount} to account ({ConnectAccountId}) from invoice ({InvoiceId})",
            (transferAmount / 100m), accountId, customerInvoice.InvoiceId);
      }
      
      return Result.FromValue(true);
   }
}
