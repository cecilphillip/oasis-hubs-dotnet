using Microsoft.AspNetCore.Identity;
using OasisHubs.DataInitializer;
using OasisHubs.DbModels;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.ConfigureOpenTelemetry("initializer");
builder.AddDefaultHealthChecks();

builder.AddSqlServerDbContext<OasisHubsDbContext>("OasisHubsDb");

builder.Services.AddIdentity<OasisHubsUser, IdentityRole>(options => {
      options.User.RequireUniqueEmail = true;
   })
   .AddDefaultTokenProviders()
   .AddEntityFrameworkStores<OasisHubsDbContext>();

builder.Services.Configure<IdentityOptions>(options => {
   // Relax default password settings.
   options.Password.RequireDigit = false;
   options.Password.RequireLowercase = false;
   options.Password.RequireNonAlphanumeric = false;
   options.Password.RequireUppercase = false;
   options.Password.RequiredLength = 3;
   options.Password.RequiredUniqueChars = 0;
});

builder.Services.AddStripe();
builder.Services.AddSingleton<Initializer>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Initializer>());

var app = builder.Build();

app.UseHttpsRedirection();
app.MapDefaultEndpoints();

app.Run();
