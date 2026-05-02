using Microsoft.EntityFrameworkCore;
using TennisIntelligence.Data;
using TennisIntelligence.Filters;
using TennisIntelligence.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages(options =>
{
    options.Conventions.ConfigureFilter(new Microsoft.AspNetCore.Mvc.ServiceFilterAttribute(typeof(InteractionLoggingFilter)));
});

builder.Services.AddDbContext<TennisDbContext>(options =>
{
    // Render.com provides DATABASE_URL; fall back to appsettings for local dev
    var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
    if (!string.IsNullOrEmpty(databaseUrl))
    {
        // Render format: postgres://user:password@host/dbname or postgres://user:password@host:port/dbname
        var uri = new Uri(databaseUrl);
        var userInfo = uri.UserInfo.Split(':');
        var port = uri.Port > 0 ? uri.Port : 5432;
        var connectionString = $"Host={uri.Host};Port={port};Database={uri.AbsolutePath.TrimStart('/')};Username={userInfo[0]};Password={userInfo[1]};SSL Mode=Require;Trust Server Certificate=true";
        options.UseNpgsql(connectionString);
    }
    else
    {
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
    }
});

// AI Coach services
builder.Services.AddHttpClient<OllamaCoachProvider>();
builder.Services.AddSingleton<RuleBasedCoachProvider>();
builder.Services.AddScoped<CoachService>();

// Interaction tracking
builder.Services.AddScoped<InteractionService>();
builder.Services.AddScoped<InteractionLoggingFilter>();

var app = builder.Build();

// Auto-migrate database on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TennisDbContext>();

    // If the database doesn't exist yet, Migrate() will create it with the full schema.
    // If it exists from EnsureCreated(), apply pending migrations.
    // On first run after switching to migrations, you may need to run:
    //   dotnet ef database update -- to apply the migration to an existing database
    // Or delete the database and let Migrate() recreate it.
    db.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseRouting();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
