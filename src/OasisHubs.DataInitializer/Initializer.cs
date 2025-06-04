using System.Security.Claims;
using Bogus;
using Microsoft.AspNetCore.Identity;
using OasisHubs.DbModels;
using OasisHubs.Defaults.Extensions;
using Stripe;
using Stripe.Billing;
using Stripe.TestHelpers;

namespace OasisHubs.DataInitializer;

public class Initializer(
   IServiceProvider serviceProvider,
   IHostApplicationLifetime appLifetime,
   ILogger<Initializer> logger) : BackgroundService {
   
   private readonly Faker _faker = new("en_US");
   private const string _actMetaKey = "host.account.id";
   private const string _customerMetaKey = "oasis.customer.id";

   private const string _placeHolderDescription =
      "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat.";


   protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
      logger.LogInformation("Seeding database ...");

      using var scope = serviceProvider.CreateScope();
      var dbContext = scope.ServiceProvider.GetRequiredService<OasisHubsDbContext>();
      await dbContext.Database.EnsureCreatedAsync(stoppingToken);

      var stripeClient = scope.ServiceProvider.GetRequiredService<StripeClient>();
      var userManager = scope.ServiceProvider.GetRequiredService<UserManager<OasisHubsUser>>();

      // Create test users with Stripe customers and connected accounts for hosts
      await CreateUsers(stripeClient, userManager);

      // Create Stripe usage meter
      await CreateUsageMeter(stripeClient);

      // Initialize subscription tiers as Stripe products with pricing configurations
      await CreateHubTierProductsAsync(stripeClient);
      
      //  Create transactions to initialize balance
      await CreateTransactionsAsync(stripeClient);

      logger.LogInformation("Seeding completed!");
      appLifetime.StopApplication();
   }

   
/// <summary>
/// Creates sample transactions to initialize a balance for testing purposes.
/// </summary>
/// <param name="stripeClient">The Stripe client used to create the payment intent.</param>
/// <returns>A task representing the asynchronous operation.</returns>
private async Task CreateTransactionsAsync(StripeClient stripeClient) {

   var balance = await stripeClient.V1.Balance.GetAsync();
   
   if (balance.Available.Sum(s => s.Amount) > 0) {
      logger.LogDebug("Skipping transaction creation. Balance is already initialized");
      return;
   }
   
   var opts = new PaymentIntentCreateOptions {
      Amount = 3500000, 
      Currency = "usd",
      Description = "Filling up the balance for testing",
      PaymentMethodTypes = ["card"],
      PaymentMethod = "pm_card_bypassPending",
      Confirm = true,
   };
   
   for (var i = 0; i < 5; i++) {
      opts.Metadata = new() { ["test.transaction"] = $"transaction-{i}" };
      logger.LogDebug("Creating test transaction {TransactionId}", opts.Metadata["test.transaction"]);
      await stripeClient.V1.PaymentIntents.CreateAsync(opts);
   }
}

   /// <summary>
   /// Creates multiple users, integrates them with Stripe, and configures additional user properties.
   /// </summary>
   /// <param name="stripeClient">The client used to communicate with the Stripe API.</param>
   /// <param name="userManager">The user manager used to create and manage application users.</param>
   /// <returns>A task that represents the asynchronous operation of creating users and configuring their accounts.</returns>
   private async Task CreateUsers(StripeClient stripeClient, UserManager<OasisHubsUser> userManager) {
      logger.LogInformation("Creating users ...");

      if (userManager.Users.Any()) {
         logger.LogInformation("Skipping user creation. Existing users found");
         return;
      }

      // Host User 1
      await CreateUser("Cecil Phillip", "cecil@test.com", stripeClient, userManager, true);

      // Host User 2
      await CreateUser("James Moriarty", "james@test.com", stripeClient, userManager, true);

      // Customer 1
      await CreateUser("Dorian Gray", "dorian@test.com", stripeClient, userManager, addTestClock: true);

      // Customer 2
      await CreateUser("H. G. Wells", "george@test.com", stripeClient, userManager, addTestClock: true);
   }


   /// <summary>
   /// Creates a new user with the specified details integrates them with Stripe and ASP.NET Core Identity user store.
   /// </summary>
   /// <param name="name">The name of the user to be created.</param>
   /// <param name="email">The email address of the user to be created.</param>
   /// <param name="stripeClient">The client used for interacting with the Stripe API.</param>
   /// <param name="userManager">The user manager used for creating and managing application users.</param>
   /// <param name="addConnectAccount">Optional. Indicates whether a Connect account should be added for the user. Defaults to false.</param>
   /// <param name="addTestClock">Optional. Indicates whether a test clock should be created and attached to the user. Defaults to false.</param>
   /// <returns>A task that represents the asynchronous operation of creating the user with all necessary configurations.</returns>
   private async Task CreateUser(string name, string email, StripeClient stripeClient,
      UserManager<OasisHubsUser> userManager, bool addConnectAccount = false, bool addTestClock = false) {
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

      // Create a new user and associate it with a customer in stripe 
      var newCustomer = await stripeClient.V1.Customers.CreateAsync(ccOptions);
      var newUser = new OasisHubsUser {
         UserName = ccOptions.Email, Email = ccOptions.Email, EmailConfirmed = true, StripeCustomerId = newCustomer.Id
      };

      await userManager.CreateAsync(newUser, "test");

      // add claim
      await userManager.AddClaimAsync(newUser, new Claim(AppConstants.OASIS_USER_TYPE, "customer"));
      await userManager.AddClaimAsync(newUser, new Claim(AppConstants.StripeCustomerIdClaimType, newCustomer.Id));
      logger.LogDebug("Created user {CustomerName}", ccOptions.Name);

      // update Stripe customer with oasis customer id
      var cuOptions = new CustomerUpdateOptions {
         Metadata = new Dictionary<string, string> { [_customerMetaKey] = newUser.Id }
      };


      // Create and attach new stripe connected account.
      if (addConnectAccount) {
         await CreateConnectedAccountAsync(stripeClient, userManager, newUser, newCustomer, cuOptions);
      }

      await stripeClient.V1.Customers.UpdateAsync(newCustomer.Id, cuOptions);
   }


   /// <summary>
   /// Creates a Stripe connected account for a user, updates the user and customer metadata, 
   /// and associates the account with rental hubs.
   /// </summary>
   /// <param name="stripeClient">The Stripe client used for API interactions.</param>
   /// <param name="userManager">The user manager for updating user information.</param>
   /// <param name="newUser">The newly created user to associate with the connected account.</param>
   /// <param name="newCustomer">The Stripe customer associated with the user.</param>
   /// <param name="cuOptions">The customer update options for adding metadata.</param>
   /// <returns>A task representing the asynchronous operation.</returns>
   private async Task CreateConnectedAccountAsync(StripeClient stripeClient, UserManager<OasisHubsUser> userManager,
      OasisHubsUser newUser, Customer newCustomer, CustomerUpdateOptions cuOptions) {
      logger.LogInformation("Creating connect account");

      // create a connected account
      var companyName = this._faker.Company.CompanyName(0);
      var acOptions = new AccountCreateOptions {
         BusinessType = "individual",
         Country = "US",
         DefaultCurrency = "usd",
         Email = newUser.Email,
         BusinessProfile = new AccountBusinessProfileOptions {
            Name = companyName,
            //https://stripe.com/docs/connect/setting-mcc#list
            Mcc = "6513",
            ProductDescription = "Remote work rental space",
            SupportEmail = newUser.Email
         },
         Individual = new() {
            Email = newUser.Email,
            Phone = "0000000000",
            IdNumber = "000000000",
            Dob = new() { Day = 1, Month = 1, Year = 1902 },
            Verification = new() { Document = new() { Front = "file_identity_document_success" } }
         },
         Company = new AccountCompanyOptions {
            Name = companyName,
            Phone = "0000000000",
            //https://stripe.com/docs/connect/identity-verification#business-structure
            Address = new AddressOptions {
               Line1 = "address_full_match",
               City = "Miami",
               State = "FL",
               PostalCode = "33109",
               Country = "US"
            },
         },
         Controller = new() {
            StripeDashboard = new() { Type = "express" },
            RequirementCollection = "stripe",
            Losses = new AccountControllerLossesOptions() { Payments = "application" },
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
         TosAcceptance = new() { ServiceAgreement = "full" },
         Metadata = new Dictionary<string, string> { ["owner.customer.id"] = newCustomer.Id }
      };

      var newConnectAccount = await stripeClient.V1.Accounts.CreateAsync(acOptions);
      logger.LogDebug(
         "Created a connect account for {ConnectBusinessName}", acOptions.BusinessProfile.Name);

      // update the user with a connected account id
      newUser.StripeAccountId = newConnectAccount.Id;
      await userManager.UpdateAsync(newUser);

      // update Stripe customer with a connected account id 
      cuOptions.Metadata[_actMetaKey] = newConnectAccount.Id;
      await CreateRentalHubsAsync(newUser.StripeAccountId, companyName);
   }

   /// <summary>
   /// Creates and initializes multiple rental hubs for the specified company and associates them with the given Stripe connected account.
   /// </summary>
   /// <param name="connectAccountId">The Stripe connect account identifier to associate the hubs with.</param>
   /// <param name="companyName">The name of the company owning the hubs.</param>
   /// <returns>A task that represents the asynchronous operation of creating and saving the rental hubs in the database.</returns>
   private async Task CreateRentalHubsAsync(string connectAccountId, string companyName) {
      if (string.IsNullOrEmpty(connectAccountId)) {
         logger.LogWarning("Cannot create hubs. Connect account id not provided!");
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
            StripeAccountId = connectAccountId,
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
            StripeAccountId = connectAccountId,
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
            StripeAccountId = connectAccountId,
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
            StripeAccountId = connectAccountId,
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
            StripeAccountId = connectAccountId,
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

      var options = new ProductListOptions { Limit = 1 };
      var existingProducts = await stripeClient.V1.Products.ListAsync(options);

      if (existingProducts.Any(p => p.Metadata.ContainsKey("hub.tier"))) {
         logger.LogInformation("Skipping product creation. Existing hub tier products found");
         return;
      }

      await CreateHubTierAsync("Oasis Basic", "Oasis Basic Tier", 3500,
         ["Cable Internet", "Shared Workspace", "Coffee and Tea"], "basic_tier",
         "oasis_basic_tier.png", stripeClient);

      await CreateHubTierAsync("Oasis Standard", "Oasis Standard Tier", 6000,
         ["Standing Desk", "Private Office", "Snacks and Drinks"], "standard_tier",
         "oasis_standard_tier.png", stripeClient);

      await CreateHubTierAsync("Oasis Premium", "Oasis Premium Tier", 12000,
         ["High Speed Fiber Optic Internet", "Whiteboards", "Private Team Workspace", "Catering"],
         "premium_tier", "oasis_premium_tier.png", stripeClient);
   }

   /// <summary>
   /// Creates a hub tier product in Stripe with associated pricing and features.
   /// </summary>
   /// <param name="title">The name of the product.</param>
   /// <param name="description">The description of the product.</param>
   /// <param name="hourlyUnitPrice">The price per hour in cents.</param>
   /// <param name="features">The list of features included in this tier.</param>
   /// <param name="priceLookupPrefix">The prefix used for price lookup keys.</param>
   /// <param name="imageFileName">The name of the image file to be used for the product.</param>
   /// <param name="stripeClient">The Stripe client instance.</param>
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
            new Dictionary<string, string> { ["hub.tier"] = priceLookupPrefix, ["tier.image"] = imageFileName },
      };

      var newHubProduct = await stripeClient.V1.Products.CreateAsync(prodCreateOptions);
      var meter = await GetExistingMeterAsync(stripeClient);
      if (meter is null) {
         logger.LogCritical("No meter found for pricing tiers");
         throw new Exception("Required meter not found");
      }

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
         Tiers = [
            new() { UnitAmount = 0, UpTo = 10 },
            new() { UnitAmount = hourlyUnitPrice / 10, UpTo = PriceTierUpTo.Inf }
         ],
         Recurring = new PriceRecurringOptions { Interval = "month", UsageType = "metered", Meter = meter.Id },
         TiersMode = "graduated",
         BillingScheme = "tiered"
      };

      newProductPrice = await stripeClient.V1.Prices.CreateAsync(priceCreateOptions);
      logger.LogDebug("Metered price ({PriceId}) created ", newProductPrice.Id);
   }

   private async Task CreateUsageMeter(StripeClient stripeClient) {
      logger.LogInformation("Attempting meter creation");

      var meter = await GetExistingMeterAsync(stripeClient);
      if (meter is null) {
         var meterCreateOptions = new MeterCreateOptions {
            DisplayName = "Hub Usage Meter",
            EventName = AppConstants.ReportUsageEventName,
            DefaultAggregation = new MeterDefaultAggregationOptions { Formula = "sum", },
            ValueSettings = new MeterValueSettingsOptions { EventPayloadKey = AppConstants.ReportUsageEventValue },
            CustomerMapping =
               new MeterCustomerMappingOptions { Type = "by_id", EventPayloadKey = "stripe_customer_id", },
         };

         meter = await stripeClient.V1.Billing.Meters.CreateAsync(meterCreateOptions);
         logger.LogInformation("New meter created ({MeterId})", meter.Id);
      }
      else {
         logger.LogInformation("Existing meter found for event ({MeterEvent}. Skipping meter creation)",
            meter.EventName);
      }
   }

   private async Task<Meter?> GetExistingMeterAsync(StripeClient stripeClient) {
      var existingMeters = await stripeClient.V1.Billing.Meters.ListAsync(new() { Limit = 3 });

      foreach (var meter in existingMeters) {
         if (meter.EventName == AppConstants.ReportUsageEventName) {
            return meter;
         }
      }

      return null;
   }
}
