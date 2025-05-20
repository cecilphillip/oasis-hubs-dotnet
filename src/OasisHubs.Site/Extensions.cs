using System.Threading.Channels;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using OasisHubs.DbModels;
using OasisHubs.Defaults.Extensions;
using OasisHubs.Defaults.Extensions.Messaging;
using OasisHubs.Site.Policies;
using Paramore.Brighter;
using Paramore.Brighter.Extensions.DependencyInjection;
using Paramore.Brighter.MessagingGateway.RMQ;
using Paramore.Brighter.ServiceActivator.Extensions.DependencyInjection;
using Paramore.Brighter.ServiceActivator.Extensions.Hosting;
using RabbitMQ.Client;
using ZiggyCreatures.Caching.Fusion;
using Channel = System.Threading.Channels.Channel;

namespace OasisHubs.Site;

internal static class Extensions {
   public static WebApplication ConfigureAppServices(this WebApplicationBuilder builder) {
      
      builder.AddSqlServerDbContext<OasisHubsDbContext>("OasisHubsDb");
      builder.Services.AddCoreServices(builder.Configuration)
         .AddMessagingServices(builder.Configuration)
         .AddAuthServices()
         .AddRazorAppServices();
      
      return builder.Build();
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
      var rabbitConnectionString = configuration.GetConnectionString("redisCache");
      if (rabbitConnectionString is null)
         throw new Exception("RabbitMQ Connection information missing");

      var rmqMessagingGatewayConnection = new RmqMessagingGatewayConnection {
         Name = "OasisHubsRMQConnection",
         AmpqUri = new AmqpUriSpecification(new Uri(rabbitConnectionString),
            connectionRetryCount: 5, retryWaitInMilliseconds: 250),
         //https://www.rabbitmq.com/tutorials/amqp-concepts.html#exchange-direct
         Exchange = new Exchange(MessagingConstants.DEFAULT_EXCHANGE, ExchangeType.Direct, true),
         DeadLetterExchange =
            new Exchange(MessagingConstants.DEFAULT_DLQ_EXCHANGE, ExchangeType.Fanout, true),
         Heartbeat = 15,
         PersistMessages = true
      };

      // Configure command processor
      services.AddBrighter(options => {
            options.HandlerLifetime = ServiceLifetime.Scoped;
            options.CommandProcessorLifetime = ServiceLifetime.Scoped;
            options.MapperLifetime = ServiceLifetime.Singleton;
         })
         .UseExternalBus(new RmqProducerRegistryFactory(
            rmqMessagingGatewayConnection,
            new[] {
               new RmqPublication {
                  Topic = new RoutingKey(MessagingConstants.HOST_UPDATED_TOPIC),
                  MaxOutStandingMessages = 20,
                  WaitForConfirmsTimeOutInMilliseconds = 10000,
                  MakeChannels = OnMissingChannel.Create
               },
               new RmqPublication {
                  Topic = new RoutingKey(MessagingConstants.SUBSCRIPTION_ACTIVATED_TOPIC),
                  MaxOutStandingMessages = 20,
                  WaitForConfirmsTimeOutInMilliseconds = 10000,
                  MakeChannels = OnMissingChannel.Create
               },
               new RmqPublication {
                  Topic = new RoutingKey(MessagingConstants.FUNDS_TRANSFER_TOPIC),
                  MaxOutStandingMessages = 20,
                  WaitForConfirmsTimeOutInMilliseconds = 10000,
                  MakeChannels = OnMissingChannel.Create
               }
            }).Create())
         .AutoFromAssemblies();

      // Configure activator
      var subscriptions = new Paramore.Brighter.Subscription[] {
         new RmqSubscription<ActivateHostAccountCommand>(
            new SubscriptionName(MessagingConstants.HOST_UPDATE_SUBSCRIPTION),
            new ChannelName(MessagingConstants.HOST_UPDATE_CHANNEL),
            new RoutingKey(MessagingConstants.HOST_UPDATED_TOPIC),
            deadLetterChannelName: new ChannelName(MessagingConstants.DEFAULT_DLQ_CHANNEL),
            deadLetterRoutingKey: MessagingConstants.DEFAULT_DLQ_ROUTING_KEY,
            requeueCount: 5,
            runAsync: true, isDurable: true,
            makeChannels: OnMissingChannel.Create),
         new RmqSubscription<ActivateCustomerSubscriptionCommand>(
            new SubscriptionName(MessagingConstants.SUBSCRIPTION_ACTIVATED_SUBSCRIPTION),
            new ChannelName(MessagingConstants.SUBSCRIPTION_ACTIVATED_CHANNEL),
            new RoutingKey(MessagingConstants.SUBSCRIPTION_ACTIVATED_TOPIC),
            deadLetterChannelName: new ChannelName(MessagingConstants.DEFAULT_DLQ_CHANNEL),
            deadLetterRoutingKey: MessagingConstants.DEFAULT_DLQ_ROUTING_KEY,
            requeueCount: 5,
            runAsync: true, isDurable: true,
            makeChannels: OnMissingChannel.Create),
         new RmqSubscription<InitiateFundsTransferCommand>(
            new SubscriptionName(MessagingConstants.FUNDS_TRANSFER_SUBSCRIPTION),
            new ChannelName(MessagingConstants.FUNDS_TRANSFER_CHANNEL),
            new RoutingKey(MessagingConstants.FUNDS_TRANSFER_TOPIC),
            deadLetterChannelName: new ChannelName(MessagingConstants.DEFAULT_DLQ_CHANNEL),
            deadLetterRoutingKey: MessagingConstants.DEFAULT_DLQ_ROUTING_KEY,
            requeueCount: 5,
            runAsync: true, isDurable: true,
            makeChannels: OnMissingChannel.Create)
      };
      services.AddServiceActivator(options => {
            var rmqMessageConsumerFactory =
               new RmqMessageConsumerFactory(rmqMessagingGatewayConnection);

            options.UseScoped = true;
            options.HandlerLifetime = ServiceLifetime.Scoped;
            options.MapperLifetime = ServiceLifetime.Singleton;
            options.CommandProcessorLifetime = ServiceLifetime.Scoped;
            options.ChannelFactory = new ChannelFactory(rmqMessageConsumerFactory);
            options.Subscriptions = subscriptions;
         })
         .UseInMemoryInbox()
         .AutoFromAssemblies();

      services.AddHostedService<ServiceActivatorHostedService>();

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
