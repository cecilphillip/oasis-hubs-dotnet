using DotNext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OasisHubs.DbModels;
using OasisHubs.Defaults.Extensions;
using Stripe;
using Temporalio.Activities;
using Temporalio.Workflows;

namespace OasisHubs.Workflows;

[Workflow]
public class ActivateHostWorkflow {
   [WorkflowQuery]
   public string WorkflowStatus { get; set; } = "Workflow not started";
   
   [WorkflowRun]
   public async Task RunAsync(SlimAccount hostAccount) {
      WorkflowStatus = $"Workflow started for host {hostAccount.CustomerId}"; 
      
      var activityResult = await Workflow.ExecuteActivityAsync<ActivateHostActivities, Result<bool>>(
         x => x.ActivateHostAsync(hostAccount),
         new() { StartToCloseTimeout = TimeSpan.FromSeconds(10) });
      
      if (!activityResult) {
         const string msg = "Unable to activate host profile";
         Workflow.Logger.LogWarning(msg);
         WorkflowStatus = msg;
         return;
      }
      
      WorkflowStatus = "Workflow completed";
   }
}

public class ActivateHostActivities(OasisHubsDbContext dbContext,StripeClient stripeClient, ILogger<ActivateHostActivities> logger) {
   
   [Activity]
   public async Task<Result<bool>> ActivateHostAsync(SlimAccount hostAccount) {
      var hostUser = await dbContext.Users
         .Include(u => u.Claims)
         .FirstOrDefaultAsync(u => u.Email == hostAccount.Email);

      if (hostUser != null) {
         if (!string.IsNullOrEmpty(hostAccount.CustomerId)) {
            
            var stripeCustomer = await stripeClient.V1.Customers.GetAsync(hostAccount.CustomerId);

            await stripeClient.V1.Customers.UpdateAsync(stripeCustomer.Id,
               new CustomerUpdateOptions {
                  Metadata = new Dictionary<string, string> { { AppConstants.OASIS_USER_TYPE, "host" } }});

            logger.LogInformation("Stripe customer updated with host metadata");
            hostUser.IsHost = true;

            var filterClaim = hostUser.Claims.Single(c => c.ClaimType == AppConstants.OASIS_USER_TYPE);
            filterClaim.ClaimValue = "host";

            await dbContext.SaveChangesAsync();
            logger.LogInformation("User ({UserId}) found and host enabled", hostUser.Id);
         }
      }
      else {
         logger.LogError("HostUser for Stripe account ({StripeAccountId}) not found",
            hostAccount.StripeAccountId);
      }
      
      return Result.FromValue(true);
   }
}
