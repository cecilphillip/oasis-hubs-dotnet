using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OasisHubs.DbModels;
using OasisHubs.Defaults.Extensions;
using OasisHubs.Defaults.Extensions.Messaging;
using Paramore.Brighter;
using Paramore.Brighter.Inbox;
using Paramore.Brighter.Inbox.Attributes;

namespace OasisHubs.BackgroundProcessor.Handlers;

public class ActivateCustomerSubscriptionRequestHandler : RequestHandlerAsync<
   ActivateCustomerSubscriptionCommand> {
   private readonly OasisHubsDbContext _dbContext;
   private readonly ILogger<ActivateCustomerSubscriptionRequestHandler> _logger;

   public ActivateCustomerSubscriptionRequestHandler(
      OasisHubsDbContext dbContext, ILogger<ActivateCustomerSubscriptionRequestHandler> logger) {
      this._dbContext = dbContext;
      this._logger = logger;
   }

   [UseInboxAsync(step: 0, contextKey: typeof(ActivateCustomerSubscriptionRequestHandler), onceOnly: true,
      onceOnlyAction: OnceOnlyAction.Throw)]
   public override async Task<ActivateCustomerSubscriptionCommand> HandleAsync(
      ActivateCustomerSubscriptionCommand command,
      CancellationToken cancellationToken = new()) {
      if (command.CustomerSubscription == null) {
         this._logger.LogError("Subscription information missing from command");
         throw new ArgumentNullException(nameof(command), "Subscription information missing.");
      }

      var userRecord = await _dbContext.Users
         .Include(u => u.Claims)
         .FirstOrDefaultAsync(u => u.StripeCustomerId == command.CustomerSubscription.StripeCustomerId,
            cancellationToken: cancellationToken);

      if (userRecord is { IsEnabled: true }) {
         userRecord.HasSubscriptionActive = true;

         var subClaim = userRecord.Claims.FirstOrDefault(c => c.ClaimType == AppConstants.OASIS_SUBSCRIPTION_ACTIVE);
         userRecord.ActiveSubscriptionId = command.CustomerSubscription.StripeSubscriptionId;

         if (subClaim is not null) {
            subClaim.ClaimValue = "true";
         }
         else {
            userRecord.Claims.Add(new IdentityUserClaim<string> {
               ClaimType = AppConstants.OASIS_SUBSCRIPTION_ACTIVE, ClaimValue = "true", UserId = userRecord.Id
            });
         }

         await _dbContext.SaveChangesAsync(cancellationToken);
      }
      else {
         this._logger.LogWarning("Active customer not found {StripeCustomerId}",
            command.CustomerSubscription.StripeCustomerId);
      }

      return await base.HandleAsync(command, cancellationToken);
   }
}
