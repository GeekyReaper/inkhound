using System.Security.Cryptography;
using System.Text;
using Inkhound.Core;
using Inkhound.Web.Auth;
using Inkhound.Web.Hubs;
using Inkhound.Web.Middleware;
using Inkhound.Web.Startup;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ── JWT key — load from JSON file, generate on first run ──────────────────────
const string JwtKeyFile = "data/system/jwt-key.json";
builder.Configuration.AddJsonFile(JwtKeyFile, optional: true, reloadOnChange: false);
if (string.IsNullOrEmpty(builder.Configuration["Auth:JwtSecret"]))
{
    var newKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    Directory.CreateDirectory(Path.GetDirectoryName(JwtKeyFile)!);
    File.WriteAllText(JwtKeyFile,
        System.Text.Json.JsonSerializer.Serialize(new { Auth = new { JwtSecret = newKey } }));
    builder.Configuration["Auth:JwtSecret"] = newKey;
}

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Auth:JwtSecret"]!));

// ── Port ──────────────────────────────────────────────────────────────────────
var port = Environment.GetEnvironmentVariable("APP_PORT") ?? "5000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// ── Controllers + SignalR ──────────────────────────────────────────────────────
var enumConverter = new System.Text.Json.Serialization.JsonStringEnumConverter();

builder.Services.AddControllersWithViews()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(enumConverter));

builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(enumConverter));

// ── Swagger ───────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Inkhound API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name         = "Authorization",
        Type         = SecuritySchemeType.Http,
        Scheme       = "bearer",
        BearerFormat = "JWT",
        In           = ParameterLocation.Header,
        Description  = "Paste your JWT token (without 'Bearer ' prefix)"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});


// ── Auth ──────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton(signingKey);
builder.Services.AddSingleton<IUserStore, FileUserStore>();
builder.Services.AddSingleton<JwtService>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = "Smart";
        options.DefaultAuthenticateScheme = "Smart";
        options.DefaultChallengeScheme = "Smart";
    })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = "Inkhound",
            ValidAudience            = "Inkhound",
            IssuerSigningKey         = signingKey,
            ClockSkew                = TimeSpan.Zero
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) &&
                    ctx.HttpContext.Request.Path.StartsWithSegments("/hub"))
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    })
    .AddScheme<ApiKeyAuthenticationSchemeOptions, ApiKeyAuthenticationHandler>("ApiKey", _ => { })
    .AddPolicyScheme("Smart", "JWT or API Key", options =>
    {
        options.ForwardDefaultSelector = context =>
            context.Request.Headers.ContainsKey("X-Api-Key") ? "ApiKey" : JwtBearerDefaults.AuthenticationScheme;
    });

builder.Services.AddAuthorization();

// ── Inkhound Manager ───────────────────────────────────────────────────────────
builder.Services.AddSingleton<InkhoundManager>(_ => new InkhoundManager("data/inkhound.db"));
builder.Services.AddHostedService<InkhoundManagerInitializer>();

// ── CORS ──────────────────────────────────────────────────────────────────────
builder.Services.AddCors(opts => opts.AddDefaultPolicy(p =>
    p.WithOrigins("http://localhost:4200")
     .AllowAnyHeader()
     .AllowAnyMethod()
     .AllowCredentials()));

// ─────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();

app.UseSwagger();
app.UseSwaggerUI();

app.UseDefaultFiles();
app.UseStaticFiles();

var imagesDir = Path.Combine(app.Environment.ContentRootPath, "data", "images");
Directory.CreateDirectory(imagesDir);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(imagesDir),
    RequestPath = "/images"
});

app.UseCors();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<AppHub>("/hub/app");
app.MapFallbackToFile("index.html");

app.Run();
