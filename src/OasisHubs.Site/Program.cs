using OasisHubs.Site;


   var builder = WebApplication.CreateBuilder(args);

   builder.AddServiceDefaults();
   builder.ConfigureOpenTelemetry("initializer") ;
   builder.AddDefaultHealthChecks();
   
   builder.ConfigureAppServices();

   var app = builder.Build();
   app.ConfigurePipeline();
   app.Run();
