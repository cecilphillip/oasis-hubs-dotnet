using Microsoft.AspNetCore.Authorization;
using OasisHubs.Defaults.Extensions;

namespace OasisHubs.Site.Policies;


public class HostAuthPolicyRequirement : AuthorizationHandler<HostAuthPolicyRequirement>, IAuthorizationRequirement {

   protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, HostAuthPolicyRequirement requirement) {
        var isHost = context.User.HasClaim(c => c is { Type: AppConstants.OASIS_USER_TYPE, Value: "host" });
        if (isHost) {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}
