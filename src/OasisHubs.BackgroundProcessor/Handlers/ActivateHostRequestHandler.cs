using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OasisHubs.DbModels;
using OasisHubs.Defaults.Extensions;
using OasisHubs.Defaults.Extensions.Messaging;
using Paramore.Brighter;
using Paramore.Brighter.Inbox;
using Paramore.Brighter.Inbox.Attributes;
using Stripe;

namespace OasisHubs.BackgroundProcessor.Handlers;

public class ActivateHostRequestHandler(
   OasisHubsDbContext dbContext,StripeClient stripeClient,
   ILogger<ActivateHostRequestHandler> logger): RequestHandlerAsync<ActivateHostAccountCommand> {
   
   [UseInboxAsync(step:0, contextKey: typeof(ActivateHostRequestHandler), onceOnly: true)]
   public override async Task<ActivateHostAccountCommand> HandleAsync(
      ActivateHostAccountCommand command,
      CancellationToken cancellationToken = new()) {
      if (command.Account == null) {
         logger.LogError("Account information missing from command");
         throw new ArgumentNullException(nameof(command), "Account information missing.");
      }

      var hostUser = await dbContext.Users
         .Include(u => u.Claims)
         .FirstOrDefaultAsync(u => u.Email == command.Account.Email,
            cancellationToken: cancellationToken);

      if (hostUser != null) {
         if (!string.IsNullOrEmpty(command.Account.CustomerId)) {
            
            var stripeCustomer = await stripeClient.V1.Customers.GetAsync(command.Account.CustomerId,
               cancellationToken: cancellationToken);

            await stripeClient.V1.Customers.UpdateAsync(stripeCustomer.Id,
               new CustomerUpdateOptions {
                  Metadata = new Dictionary<string, string> { { AppConstants.OASIS_USER_TYPE, "host" } }
               }, cancellationToken: cancellationToken);

            logger.LogInformation("Stripe customer updated with host metadata");
            hostUser.IsHost = true;

            var filterClaim = hostUser.Claims.Single(c => c.ClaimType == AppConstants.OASIS_USER_TYPE);
            filterClaim.ClaimValue = "host";

            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("User ({UserId}) found and host enabled", hostUser.Id);
         }
      }
      else {
         logger.LogError("HostUser for Stripe account ({StripeAccountId}) not found",
            command.Account.StripeAccountId);
      }

      return await base.HandleAsync(command, cancellationToken);
   }
}
