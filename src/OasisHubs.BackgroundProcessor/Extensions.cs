using Paramore.Brighter;
using Paramore.Brighter.MessagingGateway.RMQ;
using Paramore.Brighter.ServiceActivator.Extensions.DependencyInjection;
using Paramore.Brighter.ServiceActivator.Extensions.Hosting;

using OasisHubs.DbModels;
using OasisHubs.Defaults.Extensions;
using OasisHubs.Defaults.Extensions.Messaging;
using Paramore.Brighter.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace OasisHubs.BackgroundProcessor;

internal static class Extensions {
   public static WebApplicationBuilder ConfigureAppServices(this WebApplicationBuilder builder) {

      builder.AddSqlServerDbContext<OasisHubsDbContext>("OasisHubsDb");
      builder.AddMessagingServices();
      builder.Services.AddStripe();
      
      return builder;
   }

   private static void AddMessagingServices(this WebApplicationBuilder builder) {
      var rabbitConnectionString = builder.Configuration.GetConnectionString("rabbitServer");
      
      if (string.IsNullOrWhiteSpace(rabbitConnectionString)) {
         throw new ArgumentException("RabbitMQ connection string is not set.");
      }
      
      var rmqMessagingConnection = new RmqMessagingGatewayConnection {
         Name = "OasisHubsRMQConnection",
         AmpqUri = new AmqpUriSpecification(new Uri(rabbitConnectionString)),
         //https://www.rabbitmq.com/tutorials/amqp-concepts.html#exchange-direct
         Exchange = new Exchange(MessagingConstants.DEFAULT_EXCHANGE, ExchangeType.Direct, true),
         DeadLetterExchange =
            new Exchange(MessagingConstants.DEFAULT_DLQ_EXCHANGE, ExchangeType.Direct, true),
         PersistMessages = true
      };
      
      Subscription[] subscriptions = [
         new RmqSubscription<ActivateHostAccountCommand>(
            new SubscriptionName(MessagingConstants.HOST_UPDATE_SUBSCRIPTION), // subscription name only for diagnostics
            new ChannelName(MessagingConstants.HOST_UPDATE_CHANNEL), // queue name, also used for diagnostics
            new RoutingKey(MessagingConstants.HOST_UPDATED_TOPIC),
            deadLetterChannelName: new ChannelName(MessagingConstants.DEFAULT_DLQ_CHANNEL),
            deadLetterRoutingKey: MessagingConstants.DEFAULT_DLQ_ROUTING_KEY,
            requeueCount: 2, runAsync: true, isDurable: true,
            makeChannels: OnMissingChannel.Create),
         new RmqSubscription<ActivateCustomerSubscriptionCommand>(
            new SubscriptionName(MessagingConstants.SUBSCRIPTION_ACTIVATED_SUBSCRIPTION),
            new ChannelName(MessagingConstants.SUBSCRIPTION_ACTIVATED_CHANNEL),
            new RoutingKey(MessagingConstants.SUBSCRIPTION_ACTIVATED_TOPIC),
            deadLetterChannelName: new ChannelName(MessagingConstants.DEFAULT_DLQ_CHANNEL),
            deadLetterRoutingKey: MessagingConstants.DEFAULT_DLQ_ROUTING_KEY,
            requeueCount: 2, runAsync: true, isDurable: true,
            makeChannels: OnMissingChannel.Create),
         new RmqSubscription<InitiateFundsTransferCommand>(
            new SubscriptionName(MessagingConstants.FUNDS_TRANSFER_SUBSCRIPTION),
            new ChannelName(MessagingConstants.FUNDS_TRANSFER_CHANNEL),
            new RoutingKey(MessagingConstants.FUNDS_TRANSFER_TOPIC),
            deadLetterChannelName: new ChannelName(MessagingConstants.DEFAULT_DLQ_CHANNEL),
            deadLetterRoutingKey: MessagingConstants.DEFAULT_DLQ_ROUTING_KEY,
            requeueCount: 2, runAsync: true, isDurable: true,
            makeChannels: OnMissingChannel.Create)];
      
      builder.Services.AddServiceActivator(options => {
         var rmqMessageConsumerFactory =
            new RmqMessageConsumerFactory(rmqMessagingConnection);
         
         options.UseScoped = true;
         options.HandlerLifetime = ServiceLifetime.Scoped;
         options.MapperLifetime = ServiceLifetime.Singleton;
         options.CommandProcessorLifetime = ServiceLifetime.Scoped;
         options.ChannelFactory = new ChannelFactory(rmqMessageConsumerFactory);
         options.Subscriptions = subscriptions;
      })
      .UseInMemoryInbox()
      .AutoFromAssemblies();
      
      builder.Services.AddHostedService<ServiceActivatorHostedService>();
   }
}
