var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
   .WithPgAdmin()
   //.WithLifetime(ContainerLifetime.Persistent)
   .WithDataBindMount(".temp/postgres/data");

var database = postgres.AddDatabase("OasisHubsDb");

var redisPwd = builder.AddParameter("redisPassword", true);
var redisCache = builder.AddRedis("redisCache", 6379, redisPwd)
   .WithDataBindMount(".temp/redis/data")
   .WithBindMount("./.config/redis", "/usr/local/etc/redis")
   .WithRedisInsight()
   .WithLifetime(ContainerLifetime.Persistent);

var temporal = await builder.AddTemporalServerContainer("temporal", x => x
   .WithNamespace("OasisHubs"));

temporal.WithLifetime(ContainerLifetime.Persistent);

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
   .WithReference(temporal)
   .WaitFor(temporal)
   .WaitFor(redisCache)
   .WithReference(database)
   .WaitFor(database);

builder.AddProject<Projects.OasisHubs_Site>("mainSite")
   .WithEnvironment("Stripe__Default__ApiKey", stripeDefaultApiKey)
   .WithEnvironment("Stripe__Default__PublicKey", stripeDefaultPublicKey)
   .WithEnvironment("Stripe__Default__WebhookSecret", stripeDefaultWebhookSecret)
   .WithReference(redisCache)
   .WithReference(database)
   
   .WaitFor(database)
   .WaitFor(redisCache);

builder.Build().Run();
