using Microsoft.AspNetCore.DataProtection;
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

var builder = WebApplication.CreateBuilder(args);

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
    // Support JWT from httpOnly cookie
    o.Events = new JwtBearerEvents
    {
        OnMessageReceived = ctx =>
        {
            if (ctx.Request.Cookies.TryGetValue("dbtula_token", out var token))
                ctx.Token = token;
            // Also support Authorization header for Swagger/tools
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization();

// ── Data Protection — keys persisted in DB so they survive redeploys ──────────
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>();
builder.Services.AddCredentialService();

// ── Services ─────────────────────────────────────────────────────────────────
builder.Services.AddScoped<AuthService>();

// ── Comparison worker queue ───────────────────────────────────────────────────
var channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });
builder.Services.AddSingleton(channel);
builder.Services.AddHostedService<ComparisonWorker>();

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

var app = builder.Build();

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

app.Run();
