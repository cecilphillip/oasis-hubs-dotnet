using System.Security.Claims;
using Bogus;
using Microsoft.AspNetCore.Identity;
using OasisHubs.DbModels;
using Stripe;
using Stripe.TestHelpers;

namespace OasisHubs.DataInitializer;

public class Initializer(
//   UserManager<OasisHubsUser> userManager,
   IServiceProvider serviceProvider,
  // StripeClient stripeClient,
   ILogger<Initializer> logger)
   : BackgroundService {
   private readonly Faker _faker = new("en_US");
   private const string _actMetaKey = "host.account.id";
   private const string _custMetaKey = "oasis.customer.id";

   private const string _placeHolderDescription =
      "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat.";

   protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
      logger.LogInformation("Seeding database ...");

      using var scope = serviceProvider.CreateScope();
      var dbContext = scope.ServiceProvider.GetRequiredService<OasisHubsDbContext>();
      await dbContext.Database.EnsureCreatedAsync(stoppingToken);
      
      var stripeClient = scope.ServiceProvider.GetRequiredService<StripeClient>();
      var userManager = scope.ServiceProvider.GetRequiredService<UserManager<OasisHubsUser>>();
      
      await CreateUsers(stripeClient, userManager);
      await CreateHubTierProductsAsync(stripeClient);

      logger.LogInformation("Seeding completed!");
   }

   //TODO: Change the names of thee users
   private async Task CreateUsers(StripeClient stripeClient, UserManager<OasisHubsUser> userManager) {
      logger.LogInformation("Creating users ...");

      //TODO: add connect account to hosts
      
      // Host User 1
      await CreateUser("Cecil Phillip", "cecil@test.com", stripeClient, userManager); 

      // Host User 2
      await CreateUser("James Moriarty", "james@test.com", stripeClient, userManager);

      // Customer 1
      await CreateUser("Dorian Gray", "dorian@test.com", stripeClient, userManager, addTestClock: true);

      // Customer 2
      await CreateUser("H. G. Wells", "george@test.com",stripeClient, userManager, addTestClock: true);
   }

   private async Task CreateUser(string name, string email, StripeClient stripeClient, UserManager<OasisHubsUser> userManager, bool addExpressAccount = false, bool addTestClock = false) {
      
      var ccOptions = new CustomerCreateOptions {
         Name = name,
         Email = email,
         Description = "Faker User Account",
         PaymentMethod = "pm_card_visa",
         Address = new AddressOptions {
            Line1 = this._faker.Address.StreetAddress(),
            City = this._faker.Address.City(),
            State = this._faker.Address.StateAbbr(),
            Country = "US"
         },
         InvoiceSettings =
            new CustomerInvoiceSettingsOptions { DefaultPaymentMethod = "pm_card_visa" }
      };

      if (addTestClock) {
         logger.LogInformation("Creating test clock ...");
         var tcCreateOptions = new TestClockCreateOptions {
            Name = $"Subscription Clock ({name})", FrozenTime = DateTimeOffset.UtcNow.DateTime
         };

         var newTestClock = await stripeClient.V1.TestHelpers.TestClocks.CreateAsync(tcCreateOptions);
         ccOptions.TestClock = newTestClock.Id;
         logger.LogDebug(
            "Created test clock ({TestClock}) attached to user ({Username})", newTestClock.Id,
            ccOptions.Name);
      }

      var newCustomer = await stripeClient.V1.Customers.CreateAsync(ccOptions);
      var newUser = new OasisHubsUser {
         UserName = ccOptions.Email, Email = ccOptions.Email, EmailConfirmed = true, StripeCustomerId = newCustomer.Id
      };

      await userManager.CreateAsync(newUser, "test");

      // add claim
      await userManager.AddClaimAsync(newUser, new Claim(ClaimsConstants.OASIS_USER_TYPE, "customer"));
      logger.LogDebug("Created user {CustomerName}", ccOptions.Name);

      // update Stripe customer with oasis customer Id
      var cuOptions = new CustomerUpdateOptions {
         Metadata = new Dictionary<string, string> { [_custMetaKey] = newUser.Id }
      };

      // Extract into its own method
      if (addExpressAccount) {
         logger.LogInformation("Created express account ...");

         // create an express account
         var companyName = this._faker.Company.CompanyName(0);
         var acOptions = new AccountCreateOptions {
            Country = "US",
            Email = newUser.Email,
            Type = "express",
            Company = new AccountCompanyOptions {
               Name = companyName,
               Structure =
                  "single_member_llc", //https://stripe.com/docs/connect/identity-verification#business-structure
               Address = new AddressOptions {
                  Line1 = "address_full_match",
                  City = "Miami",
                  State = "FL",
                  PostalCode = "33109",
                  Country = "US"
               }
            },
            BusinessProfile = new AccountBusinessProfileOptions {
               Name = companyName,
               Mcc = "6513", //https://stripe.com/docs/connect/setting-mcc#list
               ProductDescription = "Remote work rental space",
               SupportEmail = newUser.Email
            },
            BusinessType = "company",
            Capabilities = new AccountCapabilitiesOptions {
               UsBankAccountAchPayments =
                  new AccountCapabilitiesUsBankAccountAchPaymentsOptions { Requested = true },
               LinkPayments = new AccountCapabilitiesLinkPaymentsOptions { Requested = true },
               CardPayments = new AccountCapabilitiesCardPaymentsOptions { Requested = true },
               Transfers = new AccountCapabilitiesTransfersOptions { Requested = true }
            },
            TosAcceptance = new AccountTosAcceptanceOptions { ServiceAgreement = "full" },
            Metadata = new Dictionary<string, string> { ["owner.customer.id"] = newCustomer.Id }
         };

         var newExpressAccount = await stripeClient.V1.Accounts.CreateAsync(acOptions);
         logger.LogDebug(
            "Created express account for {ExpressBusinessName}", acOptions.BusinessProfile.Name);

         // update user with express account Id
         newUser.StripeAccountId = newExpressAccount.Id;
         await userManager.UpdateAsync(newUser);

         // update Stripe customer with express
         cuOptions.Metadata[_actMetaKey] = newExpressAccount.Id;
         await CreateRentalHubsAsync(newUser.StripeAccountId, companyName);
      }

      await stripeClient.V1.Customers.UpdateAsync(newCustomer.Id, cuOptions);
   }

   private async Task CreateRentalHubsAsync(string expressAccountId, string companyName) {
      if (string.IsNullOrEmpty(expressAccountId)) {
         logger.LogWarning("Cannot create hubs. Express account user not found!");
         return;
      }

      logger.LogInformation("Creating Hubs ...");
      using var scope = serviceProvider.CreateScope();
      var context = scope.ServiceProvider.GetRequiredService<OasisHubsDbContext>();
      
      // Hub Rental #1
       CreateHubAsync(context,
         new HubRental {
            Title = $"Two Room Condo by {companyName}",
            Description = _placeHolderDescription,
            Capacity = this._faker.Random.Number(1, 10),
            HubType = this._faker.PickRandom<RentalType>(),
            HubTier = this._faker.PickRandom<RentalTier>(),
            Location = this._faker.Address.State(),
            ImageUrl = "https://images.unsplash.com/photo-1522708323590-d24dbb6b0267",
            StripeAccountId = expressAccountId,
            ReferenceCode = ReferenceCodeGenerator.GetUniqueKey()
         });

      // Hub Rental #2
       CreateHubAsync(context,
         new HubRental {
            Title = $"Cute Ranch with huge yard by {companyName}",
            Description = _placeHolderDescription,
            Capacity = this._faker.Random.Number(1, 10),
            HubType = this._faker.PickRandom<RentalType>(),
            HubTier = this._faker.PickRandom<RentalTier>(),
            Location = this._faker.Address.State(),
            ImageUrl = "https://images.unsplash.com/photo-1502672023488-70e25813eb80",
            StripeAccountId = expressAccountId,
            ReferenceCode = ReferenceCodeGenerator.GetUniqueKey()
         });

      // Hub Rental #3
       CreateHubAsync(context,
         new HubRental {
            Title = $"The Canopy House by {companyName}",
            Description = _placeHolderDescription,
            Capacity = this._faker.Random.Number(1, 10),
            HubType = this._faker.PickRandom<RentalType>(),
            HubTier = this._faker.PickRandom<RentalTier>(),
            Location = this._faker.Address.State(),
            ImageUrl = "https://images.unsplash.com/photo-1534595038511-9f219fe0c979",
            StripeAccountId = expressAccountId,
            ReferenceCode = ReferenceCodeGenerator.GetUniqueKey()
         });

      // Hub Rental #4
       CreateHubAsync(context,
         new HubRental {
            Title = $"Co-Working Desk by {companyName}",
            Description = _placeHolderDescription,
            Capacity = this._faker.Random.Number(1, 10),
            HubType = this._faker.PickRandom<RentalType>(),
            HubTier = this._faker.PickRandom<RentalTier>(),
            Location = this._faker.Address.State(),
            ImageUrl = "https://images.unsplash.com/photo-1512917774080-9991f1c4c750",
            StripeAccountId = expressAccountId,
            ReferenceCode = ReferenceCodeGenerator.GetUniqueKey()
         });

      // Hub Rental #5
       CreateHubAsync(context,
         new HubRental {
            Title = $"Single Bedroom Apartment by {companyName}",
            Description = _placeHolderDescription,
            Capacity = this._faker.Random.Number(1, 10),
            HubType = RentalType.Room,
            HubTier = RentalTier.Standard,
            Location = this._faker.Address.State(),
            ImageUrl = "https://images.unsplash.com/photo-1554995207-c18c203602cb",
            StripeAccountId = expressAccountId,
            ReferenceCode = ReferenceCodeGenerator.GetUniqueKey()
         });
      
      await context.SaveChangesAsync();
   }

   private void CreateHubAsync(OasisHubsDbContext context, HubRental rental) {
      
      ArgumentNullException.ThrowIfNull(rental);
      // Add a product in the database
      context.HubRentals.Add(rental);
      logger.LogDebug("Add Hub rental record {RentalName} to database", rental.Title);
   }

   private async Task CreateHubTierProductsAsync(StripeClient stripeClient) {
      logger.LogInformation("Creating product tiers ...");

      await CreateHubTierAsync("Oasis Basic", "Oasis Basic Tier", 3500,
         new[] { "Cable Internet", "Shared Workspace", "Coffee and Tea" }, "basic_tier",
         "oasis_basic_tier.png", stripeClient);

      await CreateHubTierAsync("Oasis Standard", "Oasis Standard Tier", 6000,
         new[] { "Standing Desk", "Private Office", "Snacks and Drinks" }, "standard_tier",
         "oasis_standard_tier.png", stripeClient);

      await CreateHubTierAsync("Oasis Premium", "Oasis Premium Tier", 12000,
         new[] { "High Speed Fiber Optic Internet", "Whiteboards", "Private Team Workspace", "Catering" },
         "premium_tier", "oasis_premium_tier.png", stripeClient);
   }

   private async Task CreateHubTierAsync(string title, string description, long hourlyUnitPrice,
      IEnumerable<string> features, string priceLookupPrefix, string imageFileName, StripeClient stripeClient) {
      
      // locate and upload image
      var imagePath = Path.Combine(Directory.GetCurrentDirectory(), "images", imageFileName);
      await using var stream = System.IO.File.OpenRead(imagePath);
      var fileCreateOptions =
         new FileCreateOptions { File = stream, Purpose = FilePurpose.BusinessLogo };

      var createdFile = await stripeClient.V1.Files.CreateAsync(fileCreateOptions);
      logger.LogDebug("Uploading image file ({ImageFileName}) to stripe", imageFileName);

      // Create a file link
      var fileLinkOptions = new FileLinkCreateOptions { File = createdFile.Id };
      var fileLink = await stripeClient.V1.FileLinks.CreateAsync(fileLinkOptions);

      // Create the subscription tier in Stripe
      logger.LogDebug("Creating product {ProductName} and attaching image file", title);
      var prodCreateOptions = new ProductCreateOptions {
         Name = title,
         Description = description,
         Images = [fileLink.Url],
         MarketingFeatures = features.Select(f => new ProductMarketingFeatureOptions { Name = f }).ToList(),
         UnitLabel = "hour",
         Metadata =
            new Dictionary<string, string> { ["hub.tier"] = "true", ["tier.image"] = imageFileName },
         
         //TODO: investigate setting the default price here
      };

      var newHubProduct = await stripeClient.V1.Products.CreateAsync(prodCreateOptions);

      // Create flat price in product
      var priceCreateOptions = new PriceCreateOptions {
         Product = newHubProduct.Id,
         Nickname = newHubProduct.Name,
         Currency = "usd",
         UnitAmount = hourlyUnitPrice,
         LookupKey = $"{priceLookupPrefix}_usd",
         BillingScheme = "per_unit",
         Recurring = new PriceRecurringOptions { Interval = "month", UsageType = "licensed" }
      };

      var newProductPrice = await stripeClient.V1.Prices.CreateAsync(priceCreateOptions);

      // Update default price
      await stripeClient.V1.Products.UpdateAsync(newHubProduct.Id,
         new ProductUpdateOptions { DefaultPrice = newProductPrice.Id });
      logger.LogDebug("Price ({PriceId}) created and set as default", newProductPrice.Id);

      // Create tiered pricing in a product
      priceCreateOptions = new PriceCreateOptions {
         Product = newHubProduct.Id,
         Nickname = newHubProduct.Name,
         LookupKey = $"{priceLookupPrefix}_usd_tiered",
         Currency = "usd",
         Tiers = new List<PriceTierOptions> {
            new() { UnitAmount = 0, UpTo = 10 }, new() { UnitAmount = hourlyUnitPrice / 10, UpTo = PriceTierUpTo.Inf }
         },
         Recurring = new PriceRecurringOptions { Interval = "month", UsageType = "metered" },
         TiersMode = "graduated",
         BillingScheme = "tiered"
      };

      newProductPrice = await stripeClient.V1.Prices.CreateAsync(priceCreateOptions);
      logger.LogDebug("Metered price ({PriceId}) created ", newProductPrice.Id);
   }
}
