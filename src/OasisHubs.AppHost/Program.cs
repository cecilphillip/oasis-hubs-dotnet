var builder = DistributedApplication.CreateBuilder(args);

var sqlServerPassword = builder.AddParameter("sqlServerPassword", true);
var sqlServer = builder.AddSqlServer("sqlServer", sqlServerPassword, 1433)
   .WithDataBindMount(".temp/mssql/data")
   .WithLifetime(ContainerLifetime.Persistent);

var database = sqlServer.AddDatabase("OasisHubsDb");

var rabbitPwd = builder.AddParameter("rabbitmqPassword", true);
var rabbitUsr = builder.AddParameter("rabbitmqUsername", true);
var rmq = builder.AddRabbitMQ("rmq", rabbitUsr, rabbitPwd, 5672)
   //.WithEnvironment("RABBITMQ_DEFAULT_VHOST", "oasis")
   .WithBindMount(".config/rabbitmq/rabbitmq_enabled_plugins", "/etc/rabbitmq/enabled_plugins")
   .WithManagementPlugin(15672)
   .WithLifetime(ContainerLifetime.Persistent);

var redisPwd = builder.AddParameter("redisPassword", true);
var redisCache = builder.AddRedis("basketCache", 6379, redisPwd)
   .WithDataBindMount(".temp/redis/data")
   .WithBindMount("./.config/redis", "/usr/local/etc/redis")
   .WithRedisInsight()
   .WithLifetime(ContainerLifetime.Persistent);


var stripeDefaultApiKey = builder.AddParameter("stripeSecretKey", true);

// Projects
builder.AddProject<Projects.OasisHubs_DataInitializer>("dataInitializer")
   .WithEnvironment("Stripe__Default__ApiKey", stripeDefaultApiKey)
   .WithReference(database)
   .WaitFor(database);

// builder.AddProject<Projects.OasisHubs_Site>("mainsite")
//    // Add references
//    .WithReference(rmq)
//    .WithReference(redisCache)
//    .WithReference(database)
//    
//    // Wait for references
//    .WaitFor(rmq)
//    .WaitFor(redisCache)
//    .WaitFor(sqlServer);

builder.Build().Run();
