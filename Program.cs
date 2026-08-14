using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TennisIntelligence.Connectors;
using TennisIntelligence.Data;
using TennisIntelligence.Filters;
using TennisIntelligence.Security;
using TennisIntelligence.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages(options =>
{
    options.Conventions.ConfigureFilter(
        new Microsoft.AspNetCore.Mvc.ServiceFilterAttribute(typeof(InteractionLoggingFilter)));
    if (!builder.Environment.IsDevelopment())
    {
        options.Conventions.AuthorizeFolder("/");
        options.Conventions.AllowAnonymousToPage("/Error");
        options.Conventions.AllowAnonymousToPage("/Login");
    }
});
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.Configure<AppAuthOptions>(
    builder.Configuration.GetSection(AppAuthOptions.SectionName));

builder.Services.AddDbContext<TennisDbContext>(options =>
{
    var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
    if (!string.IsNullOrEmpty(databaseUrl))
    {
        options.UseNpgsql(BuildPostgresConnectionString(databaseUrl));
    }
    else
    {
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
    }
});

builder.Services.AddHttpClient<OllamaCoachProvider>();
builder.Services.AddScoped<RuleBasedCoachProvider>();
builder.Services.AddScoped<CoachService>();
builder.Services.AddScoped<WearableImportService>();
builder.Services.Configure<ConnectorOptions>(
    builder.Configuration.GetSection(ConnectorOptions.SectionName));
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedHost |
        ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});

builder.Services.AddScoped<InteractionService>();
builder.Services.AddScoped<InteractionLoggingFilter>();
builder.Services.AddHostedService<KeepAliveService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment()
    && string.IsNullOrWhiteSpace(app.Configuration[$"{AppAuthOptions.SectionName}:Password"]))
{
    throw new InvalidOperationException("AppAuth:Password is required outside development.");
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TennisDbContext>();
    db.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseForwardedHeaders();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

// Render injects RENDER_GIT_COMMIT, so the running build is identifiable without a dashboard login.
var startedAt = DateTimeOffset.UtcNow;
app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    commit = Environment.GetEnvironmentVariable("RENDER_GIT_COMMIT") ?? "local",
    startedAt
}));
app.MapConnectorEndpoints();

app.Run();

static string BuildPostgresConnectionString(string databaseUrl)
{
    if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out var uri)
        || (uri.Scheme != "postgres" && uri.Scheme != "postgresql"))
    {
        throw new InvalidOperationException("DATABASE_URL must be a valid PostgreSQL URL.");
    }

    var separator = uri.UserInfo.IndexOf(':');
    if (separator <= 0 || separator == uri.UserInfo.Length - 1)
    {
        throw new InvalidOperationException("DATABASE_URL must include a username and password.");
    }

    var database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));
    if (string.IsNullOrWhiteSpace(database))
    {
        throw new InvalidOperationException("DATABASE_URL must include a database name.");
    }

    return new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.IsDefaultPort ? 5432 : uri.Port,
        Database = database,
        Username = Uri.UnescapeDataString(uri.UserInfo[..separator]),
        Password = Uri.UnescapeDataString(uri.UserInfo[(separator + 1)..]),
        SslMode = SslMode.Require,
        Timeout = 15,
        CommandTimeout = 30
    }.ConnectionString;
}
