using System.Threading.Channels;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using OasisHubs.DbModels;
using OasisHubs.Defaults.Extensions;
using OasisHubs.Site.Policies;
using OasisHubs.Site.Workers;
using Paramore.Brighter;
using Paramore.Brighter.Extensions.DependencyInjection;
using Paramore.Brighter.MessagingGateway.RMQ;
using RabbitMQ.Client;
using ZiggyCreatures.Caching.Fusion;
using Channel = System.Threading.Channels.Channel;

namespace OasisHubs.Site;

internal static class Extensions {
   public static void ConfigureAppServices(this WebApplicationBuilder builder) {
      
      builder.AddSqlServerDbContext<OasisHubsDbContext>("OasisHubsDb");
      builder.Services.AddCoreServices(builder.Configuration)
         .AddMessagingServices(builder.Configuration)
         .AddAuthServices()
         .AddRazorAppServices();
   }

   public static WebApplication ConfigurePipeline(this WebApplication app) {
     
      app.UseStaticFiles();

      app.UseRouting();

      app.UseAuthentication();
      app.UseAuthorization();

      app.MapControllers();
      app.MapRazorPages();

      return app;
   }
   

   private static IServiceCollection AddCoreServices(this IServiceCollection services,
      IConfiguration configuration) {
      
      services.AddIdentity<OasisHubsUser, IdentityRole>(options => {
            options.User.RequireUniqueEmail = true;
         })
         .AddDefaultTokenProviders()
         .AddEntityFrameworkStores<OasisHubsDbContext>();

      services.Configure<IdentityOptions>(options => {
         // Relax default password settings.
         options.Password.RequireDigit = false;
         options.Password.RequireLowercase = false;
         options.Password.RequireNonAlphanumeric = false;
         options.Password.RequireUppercase = false;
         options.Password.RequiredLength = 3;
         options.Password.RequiredUniqueChars = 0;
      });

      services.AddStripe();
      services.AddHostedService<HubUsageWorker>();
      
      var redisConnection = configuration.GetConnectionString("redisCache");
      services.AddFusionCache()
         .WithDefaultEntryOptions(new FusionCacheEntryOptions
         {
            Duration = TimeSpan.FromMinutes(4),
            IsFailSafeEnabled = true,
            FailSafeMaxDuration = TimeSpan.FromMinutes(2),
         })
         .WithCacheKeyPrefix("oasisHubs:main:")
         .WithSystemTextJsonSerializer()
         .WithDistributedCache(new RedisCache(new RedisCacheOptions { Configuration = redisConnection }));
      
      services.AddSingleton<Channel<HubUsageReport>>( _ => Channel.CreateUnbounded<HubUsageReport>(new()
      {
         SingleReader = true,
         AllowSynchronousContinuations = false
      }));

      return services;
   }

   private static IServiceCollection AddMessagingServices(this IServiceCollection services,
      IConfiguration configuration) {
      
      var rabbitConnectionString = configuration.GetConnectionString("rabbitServer");
      if (rabbitConnectionString is null)
         throw new Exception("RabbitMQ Connection information missing");

      var rmqMessagingGatewayConnection = new RmqMessagingGatewayConnection {
         Name = "OasisHubsRMQConnection",
         AmpqUri = new AmqpUriSpecification(new Uri(rabbitConnectionString)),
         //https://www.rabbitmq.com/tutorials/amqp-concepts.html#exchange-direct
         Exchange = new Exchange(MessagingConstants.DEFAULT_EXCHANGE, ExchangeType.Direct, true),
         DeadLetterExchange =
            new Exchange(MessagingConstants.DEFAULT_DLQ_EXCHANGE, ExchangeType.Direct, true),
         PersistMessages = true
      };

      // Configure command processor
      services.AddBrighter()
         .UseInMemoryOutbox()
         .UseExternalBus(new RmqProducerRegistryFactory(
            rmqMessagingGatewayConnection,
            [
               new RmqPublication {
                  Topic = new RoutingKey(MessagingConstants.HOST_UPDATED_TOPIC),
                  MakeChannels = OnMissingChannel.Create
               },
               new RmqPublication {
                  Topic = new RoutingKey(MessagingConstants.SUBSCRIPTION_ACTIVATED_TOPIC),
                  MakeChannels = OnMissingChannel.Create
               },
               new RmqPublication {
                  Topic = new RoutingKey(MessagingConstants.FUNDS_TRANSFER_TOPIC),
                  MakeChannels = OnMissingChannel.Create
               }
            ]).Create())
         .AutoFromAssemblies();

      return services;
   }

   private static IServiceCollection AddAuthServices(this IServiceCollection services) {
      services.AddAuthorizationBuilder()
         .AddPolicy("is_host_policy",
            policy => policy.AddRequirements(new HostAuthPolicyRequirement()))
         .AddPolicy("can_view_listings",
            policy => policy.AddRequirements(new CanViewListingsPolicyRequirement()));

      services.ConfigureApplicationCookie(options => {
         options.Cookie.HttpOnly = true;
         options.ExpireTimeSpan = TimeSpan.FromMinutes(20);
         options.SlidingExpiration = true;
         options.LoginPath = "/Signin";
         options.AccessDeniedPath = "/Pricing"; //TODO: Need a better option for this
      });
      
      return services;
   }

   private static IServiceCollection AddRazorAppServices(this IServiceCollection services) {
      services.Configure<RouteOptions>(options => {
         options.LowercaseQueryStrings = true;
         options.LowercaseUrls = true;
      });

      services.AddRazorPages(options => {
         options.Conventions.AuthorizePage("/Hosts/SignUp");
         options.Conventions.AuthorizeFolder("/Dashboard", "is_host_policy");
      });

      services.AddControllers();
      
      return services;
   }
}
