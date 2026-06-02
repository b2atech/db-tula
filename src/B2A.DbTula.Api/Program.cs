using Microsoft.AspNetCore.DataProtection;
using Serilog;
using System.Text;
using System.Threading.Channels;
using B2A.DbTula.Api.Data;
using B2A.DbTula.Api.Hubs;
using B2A.DbTula.Api.Services;
using B2A.DbTula.Api.Workers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

// Serilog — compact JSON to stdout (systemd journal), same as other Dhanman services
// Grafana/Loki picks this up via promtail reading the journal
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("service", "dbtula-api")
    .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter())
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

// ── Database ──────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("AppDb")));

// ── Auth ─────────────────────────────────────────────────────────────────────
var jwtKey = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is required");

builder.Services.AddAuthentication(o =>
{
    o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidateAudience = true,
        ValidAudience = builder.Configuration["Jwt:Audience"],
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ValidateLifetime = true
    };
    // Support JWT from Authorization header, cookie, or SignalR query string
    o.Events = new JwtBearerEvents
    {
        OnMessageReceived = ctx =>
        {
            // SignalR passes token via ?access_token= query string (WebSocket can't send headers)
            var qs = ctx.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(qs) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                ctx.Token = qs;
            // Also accept from cookie
            else if (ctx.Request.Cookies.TryGetValue("dbtula_token", out var cookie))
                ctx.Token = cookie;
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization();

// ── Credential encryption — fixed AES-256 key from config ────────────────────
builder.Services.AddCredentialService();

// ── Services ─────────────────────────────────────────────────────────────────
builder.Services.AddScoped<AuthService>();

// ── Comparison worker queue ───────────────────────────────────────────────────
var channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });
builder.Services.AddSingleton(channel);
builder.Services.AddHostedService<ComparisonWorker>();
builder.Services.AddHostedService<StuckRunCleanupService>();

// ── SignalR ───────────────────────────────────────────────────────────────────
builder.Services.AddSignalR();

// ── CORS (allow React dev server) ────────────────────────────────────────────
builder.Services.AddCors(o => o.AddPolicy("frontend", p =>
    p.WithOrigins(builder.Configuration["Cors:AllowedOrigins"]?.Split(',') ?? ["http://localhost:5173"])
     .AllowAnyHeader()
     .AllowAnyMethod()
     .AllowCredentials()));

// ── Controllers + Swagger ─────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(o =>
        o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "db-tula API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            []
        }
    });
});

// ─────────────────────────────────────────────────────────────────────────────

// Behind nginx — trust forwarded headers so SignalR gets correct scheme (wss://)
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                       | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

// Apply migrations on startup (non-fatal — tables already created via SQL script if DB is firewalled)
using (var scope = app.Services.CreateScope())
{
    try
    {
        var dbCtx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbCtx.Database.MigrateAsync();
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogWarning("Migration check skipped: {Message}", ex.Message);
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<ComparisonHub>("/hubs/comparison");
app.MapGet("/health", () => Results.Ok(new { status = "healthy", time = DateTime.UtcNow }));

app.Run();
