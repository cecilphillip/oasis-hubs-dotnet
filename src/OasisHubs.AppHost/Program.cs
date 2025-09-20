var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
   .WithPgAdmin()
   //.WithLifetime(ContainerLifetime.Persistent)
   .WithDataBindMount(".temp/postgres/data");

var database = postgres.AddDatabase("OasisHubsDb");

var rabbitPwd = builder.AddParameter("rabbitmqPassword", true);
var rabbitUsr = builder.AddParameter("rabbitmqUsername", true);
var rabbitServer = builder.AddRabbitMQ("rabbitServer", rabbitUsr, rabbitPwd, 5672)
   .WithBindMount(".config/rabbitmq/rabbitmq_enabled_plugins", "/etc/rabbitmq/enabled_plugins")
   .WithManagementPlugin(15672)
   .WithLifetime(ContainerLifetime.Persistent);

var redisPwd = builder.AddParameter("redisPassword", true);
var redisCache = builder.AddRedis("redisCache", 6379, redisPwd)
   .WithDataBindMount(".temp/redis/data")
   .WithBindMount("./.config/redis", "/usr/local/etc/redis")
   .WithRedisInsight()
   .WithLifetime(ContainerLifetime.Persistent);

var stripeDefaultApiKey = builder.AddParameter("stripeSecretKey", true);
var stripeDefaultPublicKey = builder.AddParameter("stripePublicKey", true);
var stripeDefaultWebhookSecret = builder.AddParameter("stripeWebhookSecret", true);

// Projects
builder.AddProject<Projects.OasisHubs_DataInitializer>("dataInitializer")
   .WithEnvironment("Stripe__Default__ApiKey", stripeDefaultApiKey)
   .WithReference(database)
   .WaitFor(database);

builder.AddProject<Projects.OasisHubs_BackgroundProcessor>("processor")
   .WithEnvironment("Stripe__Default__ApiKey", stripeDefaultApiKey)
   .WithEnvironment("Stripe__Default__PublicKey", stripeDefaultPublicKey)
   .WithEnvironment("Stripe__Default__WebhookSecret", stripeDefaultWebhookSecret)
   .WithReference(redisCache)
   .WithReference(rabbitServer)
   .WaitFor(rabbitServer)
   .WaitFor(redisCache)
   .WithReference(database)
   .WaitFor(database);

builder.AddProject<Projects.OasisHubs_Site>("mainSite")
   .WithEnvironment("Stripe__Default__ApiKey", stripeDefaultApiKey)
   .WithEnvironment("Stripe__Default__PublicKey", stripeDefaultPublicKey)
   .WithEnvironment("Stripe__Default__WebhookSecret", stripeDefaultWebhookSecret)
   .WithReference(rabbitServer)
   .WithReference(redisCache)
   .WithReference(database)
   
   .WaitFor(database)
   .WaitFor(rabbitServer)
   .WaitFor(redisCache);

builder.Build().Run();
