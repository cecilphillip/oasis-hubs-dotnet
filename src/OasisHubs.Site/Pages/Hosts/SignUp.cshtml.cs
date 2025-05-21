using Bogus;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OasisHubs.DbModels;

using Stripe;

namespace OasisHubs.Site.Pages.Hosts;

public class HostSignUpModel : PageModel {
   private readonly UserManager<OasisHubsUser> _userManager;
   private readonly SignInManager<OasisHubsUser> _signInManager;
   private readonly StripeClient _stripeClient;
   private readonly LinkGenerator _linkGenerator;
   private readonly ILogger<SignUpModel> _logger;

   public OasisHubsUser? OasisUser { get; set; }

   public HostSignUpModel(UserManager<OasisHubsUser> userManager, SignInManager<OasisHubsUser> signInManager, StripeClient stripeClient,
      LinkGenerator linkGenerator, ILogger<SignUpModel> logger) {
      this._userManager = userManager;
      this._signInManager = signInManager;
      this._stripeClient = stripeClient;
      this._linkGenerator = linkGenerator;
      this._logger = logger;
   }

   public async Task<IActionResult> OnGetAsync() {
      var currentUser = await this._userManager.GetUserAsync(User);

      if (currentUser is null) {
         this._logger.LogDebug("User not found!");
         return Redirect("/Index");
      }

      if (currentUser.IsHost) {
         await _signInManager.RefreshSignInAsync(currentUser);
         return Redirect("/Index");
      }

      if (string.IsNullOrEmpty(currentUser.StripeAccountId)) {
         return Page();
      }

      // Generate new account link for onboarding
      var basePageUri = _linkGenerator.GetUriByPage(this.HttpContext, "/Index");
      var alcOptions = new AccountLinkCreateOptions {
         Account = currentUser.StripeAccountId,
         RefreshUrl = $"{basePageUri}hosts/refresh",
         ReturnUrl = $"{basePageUri}hosts/complete",
         Type = "account_onboarding",
         CollectionOptions = new() {
            Fields = "eventually_due",
            FutureRequirements = "include"
         }
      };
      
      var acLink = await _stripeClient.V1.AccountLinks.CreateAsync(alcOptions);
      return Redirect(acLink.Url);
   }

   public async Task<IActionResult> OnPostAsync() {
      var currentUser = await this._userManager.GetUserAsync(User);

      if (currentUser is null) {
         this._logger.LogDebug("User not found!");
         return Page();
      }

      // create express account
      var faker = new Faker("en_US");
      var companyName = faker.Company.CompanyName(0);
      var acOptions = new AccountCreateOptions {
         BusinessType = "individual",
         Country = "US",
         DefaultCurrency = "usd",
         Email = currentUser.Email,
         
         BusinessProfile = new AccountBusinessProfileOptions {
            Name = companyName,
            //https://stripe.com/docs/connect/setting-mcc#list
            Mcc = "6513", 
            ProductDescription = "Remote work rental space",
            SupportEmail = currentUser.Email
         },
         
         Individual = new() {
            Email = currentUser.Email,
            Phone = "0000000000",
            IdNumber = "000000000",
            Dob = new(){ Day = 1, Month = 1, Year = 1902 },
            Verification =  new() {
               Document = new() {
                  Front = "file_identity_document_success"
               }
            }
         },
         
         Company = new AccountCompanyOptions {
            Name = companyName,
            Phone = "0000000000",
            Address = new AddressOptions {
               Line1 = "address_full_match",
               City = "Miami",
               State = "FL",
               PostalCode = "33109",
               Country = "US"
            },
         },
       
         Controller = new() {
            StripeDashboard = new() { Type = "express"},
            RequirementCollection = "stripe",
            Losses = new AccountControllerLossesOptions() {Payments = "application"},
            Fees = new AccountControllerFeesOptions { Payer = "application" },
         },
            
         Capabilities = new AccountCapabilitiesOptions {
            UsBankAccountAchPayments = new() { Requested = true },
            BankTransferPayments = new() { Requested = true },
            LinkPayments = new() { Requested = true },
            CardPayments = new() { Requested = true },
            KlarnaPayments = new() { Requested = true },
            Transfers = new() { Requested = true }
         },
         
         TosAcceptance = new AccountTosAcceptanceOptions { ServiceAgreement = "full" },
         Metadata =
            new Dictionary<string, string> { ["owner.customer.id"] = currentUser.StripeCustomerId }
      };
      
      var newConnectAccount = await _stripeClient.V1.Accounts.CreateAsync(acOptions);

      // update user with express account Id
      currentUser.StripeAccountId = newConnectAccount.Id;
      await this._userManager.UpdateAsync(currentUser);
      
      // update Stripe customer with express account Id
      var cuOptions = new CustomerUpdateOptions {
         Metadata = new Dictionary<string, string> { ["host.account.id"] = newConnectAccount.Id }
      };
      
      await this._stripeClient.V1.Customers.UpdateAsync(currentUser.StripeCustomerId, cuOptions);

      // Link account to platform
      var basePageUri = _linkGenerator.GetUriByPage(this.HttpContext, "/Index");
      var alcOptions = new AccountLinkCreateOptions {
         Account = newConnectAccount.Id,
         RefreshUrl = $"{basePageUri}/hosts/refresh",
         ReturnUrl = $"{basePageUri}/hosts/complete",
         Type = "account_onboarding",
         CollectionOptions = new() {
            Fields = "eventually_due",
            FutureRequirements = "include"
         }
      };
      
      var acLink = await this._stripeClient.V1.AccountLinks.CreateAsync(alcOptions);
      return Redirect(acLink.Url);
   }
}
