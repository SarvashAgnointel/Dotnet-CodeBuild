using DBAccess; // Ensure this is the correct namespace for IDbHandler and DbHandler
using Serilog;
using System.Net;
using Microsoft.Extensions.Logging;
using log4net;
using log4net.Config;
using Serilog.Filters;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using TravelAd_Api.DataLogic;
using Amazon.S3;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using static TravelAd_Api.Controllers.SmsController;
using TravelAd_Api.Controllers;
using TravelAd_Api.BackgroundServices;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddScoped<IDbHandler, DbHandler>();
builder.Services.AddMemoryCache();
builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<Dialler>();
builder.Services.AddScoped<Dialler>();
// Configure CORS


var allowedOrigins = builder.Configuration.GetSection("CorsSettings:AllowedOrigins").Get<List<string>>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigin",
        builder => builder.WithOrigins(allowedOrigins.ToArray())
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials());
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]))
        };


    });


//builder.WebHost.UseUrls("http://0.0.0.0:5000");

builder.Services.AddScoped<JWT>();
builder.Services.Configure<Stripesettings>(builder.Configuration.GetSection("Stripe"));

//Configure Serilog


// Base directory for all logs
string baseLogDirectory = @"C:\TravelAD_Api\Logs";
string[] controllers = { "AdvertiserAccount", "Admin", "Authentication", "Whatsapp" };

// Ensure base directories exist for all controllers
foreach (var controller in controllers)
{
    string controllerLogDirectory = Path.Combine(baseLogDirectory, controller);
    if (!Directory.Exists(controllerLogDirectory))
    {
        Directory.CreateDirectory(controllerLogDirectory);
    }
}

// Configure Serilog with controller-specific filters
var loggerConfig = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("ApplicationName", "TravelAd_Api")
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss}] [{Level:u3}] {Message:lj}{NewLine}{Exception}");

foreach (var controller in controllers)
{
    string controllerLogPath = Path.Combine(baseLogDirectory, controller, $"{controller}-.log");
    loggerConfig.WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(Matching.WithProperty<string>("SourceContext", sc => sc.Contains(controller + "Controller")))
        .WriteTo.File(controllerLogPath,
                      rollingInterval: RollingInterval.Day,
                      retainedFileCountLimit: 90,            // Keep last 90 days of logs
                      outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    );
}


Log.Logger = loggerConfig.CreateLogger();
builder.Host.UseSerilog();

builder.Services.AddAWSService<IAmazonS3>();
builder.Services.AddTransient<S3Service>(); ;
builder.Services.AddHostedService<FileDownloadAndBackupService>();
builder.Services.AddSingleton<IDbHandler, DbHandler>();
builder.Services.AddSingleton<SmsController>();

builder.Services.AddHostedService<MemoryCleanupService>(); // ✅ Memory Cleanup
builder.Services.AddHostedService<CampaignProcessingService>(); // ✅ Campaign Processing

builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.KeepAliveTimeout = TimeSpan.FromDays(1);
});

var app = builder.Build();

app.UseHttpsRedirection();

app.UseRouting();

// Ensure CORS is used before Routing
app.UseCors("AllowSpecificOrigin");
app.UseCookiePolicy(new CookiePolicyOptions
{
    MinimumSameSitePolicy = SameSiteMode.None, // Allows cross-site cookies
    HttpOnly = Microsoft.AspNetCore.CookiePolicy.HttpOnlyPolicy.Always
});
app.UseAuthentication();
app.UseAuthorization();



app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
    endpoints.MapFallback(context =>
    {
        Console.WriteLine($"Unhandled request: {context.Request.Method} {context.Request.Path}");
        return Task.CompletedTask;
    });
});

app.MapGet("/health", () => Results.Ok("Healthy"));
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();
