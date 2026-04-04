using System.Text;
using Inkhound.Web.Auth;
using Inkhound.Web.Hubs;
using Inkhound.Web.Jobs;
using Inkhound.Web.Middleware;
using Inkhound.Web.State;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ── Port ──────────────────────────────────────────────────────────────────────
var port = Environment.GetEnvironmentVariable("APP_PORT") ?? "5000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// ── Controllers + SignalR ──────────────────────────────────────────────────────
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();

// ── Auth — order matters: initializer before JwtService ───────────────────────
builder.Services.AddHostedService<JwtKeyInitializer>();
builder.Services.AddSingleton<IUserStore, FileUserStore>();
builder.Services.AddSingleton<JwtService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = "Inkhound",
            ValidAudience            = "Inkhound",
            IssuerSigningKeyResolver = (_, _, _, _) =>
            {
                var secret = builder.Configuration["Auth:JwtSecret"] ?? string.Empty;
                return [new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret))];
            },
            ClockSkew = TimeSpan.Zero
        };

        // JWT passed via query string for SignalR connections
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
    });

builder.Services.AddAuthorization();

// ── State + Jobs ───────────────────────────────────────────────────────────────
builder.Services.AddSingleton<PlatformStateService>();
builder.Services.AddSingleton<JobRunner>();

// ── CORS (dev: allows Angular dev server on 4200) ──────────────────────────────
builder.Services.AddCors(opts => opts.AddDefaultPolicy(p =>
    p.WithOrigins("http://localhost:4200")
     .AllowAnyHeader()
     .AllowAnyMethod()
     .AllowCredentials()));

// ─────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();   // must be first in the pipeline

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<AppHub>("/hub/app");
app.MapFallbackToFile("index.html");

app.Run();
