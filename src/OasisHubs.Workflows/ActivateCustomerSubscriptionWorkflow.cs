using DotNext;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OasisHubs.DbModels;
using OasisHubs.Defaults.Extensions;
using Temporalio.Activities;
using Temporalio.Workflows;

namespace OasisHubs.Workflows;

[Workflow]
public class ActivateCustomerSubscriptionWorkflow {

   [WorkflowQuery]
   public string WorkflowStatus { get; set; } = "Workflow not started";
   
   [WorkflowRun]
   public async Task RunAsync(SlimSubscription customerSubscription) {
      WorkflowStatus = "Workflow started";
      
      var activityResult = await Workflow.ExecuteActivityAsync<CustomerSubscriptionActivities, Result<bool>>(
         x => x.ActivateCustomerSubscriptionAsync(customerSubscription),
         new() { StartToCloseTimeout = TimeSpan.FromSeconds(10) });

      if (!activityResult) {
         const string msg = "Unable to activate customer subscription";
         Workflow.Logger.LogWarning(msg);
         WorkflowStatus = msg;
         return;
      }

      //Handle errors
      WorkflowStatus = "Workflow completed";
   }
}

public class CustomerSubscriptionActivities(OasisHubsDbContext dbContext, ILogger<CustomerSubscriptionActivities> logger) {
   
   [Activity]    
   public async Task<Result<bool>> ActivateCustomerSubscriptionAsync(SlimSubscription customerSubscription) {
      var userRecord = await dbContext.Users
         .Include(u => u.Claims)
         .FirstOrDefaultAsync(u => u.StripeCustomerId == customerSubscription.StripeCustomerId);

      if (userRecord is { IsEnabled: true }) {
         userRecord.HasSubscriptionActive = true;

         var subClaim = userRecord.Claims.FirstOrDefault(c => c.ClaimType == AppConstants.OASIS_SUBSCRIPTION_ACTIVE);
         userRecord.ActiveSubscriptionId = customerSubscription.StripeSubscriptionId;

         if (subClaim is not null) {
            subClaim.ClaimValue = "true";
         }
         else {
            userRecord.Claims.Add(new IdentityUserClaim<string> {
               ClaimType = AppConstants.OASIS_SUBSCRIPTION_ACTIVE, ClaimValue = "true", UserId = userRecord.Id
            });
         }

         await dbContext.SaveChangesAsync();
      }
      else {
         logger.LogWarning("Active customer not found {StripeCustomerId}",
           customerSubscription.StripeCustomerId);
      }

      return Result.FromValue(true);
   }
}
