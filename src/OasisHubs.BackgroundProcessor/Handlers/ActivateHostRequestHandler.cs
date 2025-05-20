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

public class ActivateHostRequestHandler : RequestHandlerAsync<ActivateHostAccountCommand> {
   private readonly OasisHubsDbContext _dbContext;
   private readonly StripeClient _stripeClient;
   private readonly ILogger<ActivateHostRequestHandler> _logger;

   public ActivateHostRequestHandler(OasisHubsDbContext dbContext, StripeClient stripeClient,
      ILogger<ActivateHostRequestHandler> logger) {
      this._dbContext = dbContext;
      this._stripeClient = stripeClient;
      this._logger = logger;
   }

   [UseInboxAsync(step:0, contextKey: typeof(ActivateHostRequestHandler), onceOnly: true, onceOnlyAction: OnceOnlyAction.Throw)]
   public override async Task<ActivateHostAccountCommand> HandleAsync(
      ActivateHostAccountCommand command,
      CancellationToken cancellationToken = new()) {
      if (command.Account == null) {
         this._logger.LogError("Account information missing from command");
         throw new ArgumentNullException(nameof(command), "Account information missing.");
      }

      var hostUser = await _dbContext.Users
         .Include(u => u.Claims)
         .FirstOrDefaultAsync(u => u.Email == command.Account.Email,
            cancellationToken: cancellationToken);

      if (hostUser != null) {
         if (!string.IsNullOrEmpty(command.Account.CustomerId)) {
            
            var stripeCustomer = await _stripeClient.V1.Customers.GetAsync(command.Account.CustomerId,
               cancellationToken: cancellationToken);

            await _stripeClient.V1.Customers.UpdateAsync(stripeCustomer.Id,
               new CustomerUpdateOptions {
                  Metadata = new Dictionary<string, string> { { AppConstants.OASIS_USER_TYPE, "host" } }
               }, cancellationToken: cancellationToken);

            this._logger.LogInformation("Stripe customer updated with host metadata");
            hostUser.IsHost = true;

            var filterClaim = hostUser.Claims.Single(c => c.ClaimType == AppConstants.OASIS_USER_TYPE);
            filterClaim.ClaimValue = "host";

            await _dbContext.SaveChangesAsync(cancellationToken);
            this._logger.LogInformation("User ({UserId}) found and host enabled", hostUser.Id);
         }
      }
      else {
         this._logger.LogError("HostUser for Stripe account ({StripeAccountId}) not found",
            command.Account.StripeAccountId);
      }

      return await base.HandleAsync(command, cancellationToken);
   }
}
