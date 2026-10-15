using System.IO.Compression;
using Aurigo.RowManagement.Api.HealthChecks;
using Aurigo.RowManagement.Api.Logging;
using Aurigo.RowManagement.Api.Middleware;
using Aurigo.RowManagement.Api.RateLimiting;
using Aurigo.RowManagement.Application;
using Aurigo.RowManagement.Application.Documents;
using Aurigo.RowManagement.Infrastructure;
using Aurigo.RowManagement.Infrastructure.Auth;
using Aurigo.RowManagement.Infrastructure.Persistence;
using Aurigo.RowManagement.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Npgsql;
using Serilog;
using System.Text;

// A plain (non-reloadable) logger for startup failures. CreateBootstrapLogger() can only be frozen once
// per process, which breaks hosting several instances of the app in one process (integration tests).
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Logging: Serilog to console, enriched with tenant, user and request id ──
    builder.Host.UseSerilog((ctx, sp, lc) =>
    {
        lc.ReadFrom.Configuration(ctx.Configuration)
          .Enrich.FromLogContext();
        var http = sp.GetService<IHttpContextAccessor>();
        if (http is not null) lc.Enrich.With(new TenantEnricher(http));
        lc.WriteTo.Console();
    });

    // ── Response compression (Brotli, then Gzip) ──
    builder.Services.AddResponseCompression(opts =>
    {
        opts.EnableForHttps = true;
        opts.Providers.Add<BrotliCompressionProvider>();
        opts.Providers.Add<GzipCompressionProvider>();
        opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Append("application/problem+json");
    });
    builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
    builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure();

    // ── Authentication: this app issues and validates its own JWTs ──
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer();
    builder.Services
        .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
        .Configure<JwtOptions, IHostEnvironment>((o, jwt, env) =>
        {
            o.MapInboundClaims = false; // keep claim names exactly as issued ("sub", "role", "tenant_id")
            o.RequireHttpsMetadata = !env.IsDevelopment();
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwt.Issuer,
                ValidAudience = jwt.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                NameClaimType = "email",
                RoleClaimType = JwtOptions.RoleClaim,
                ClockSkew = TimeSpan.FromMinutes(1),
            };
            // A correctly signed token is still refused unless it identifies a real tenant and user.
            // Every query filter and audit stamp depends on these; an absent or malformed value must be a
            // 401 here, not a request that limps on with an empty tenant.
            o.Events = new JwtBearerEvents
            {
                OnTokenValidated = ctx =>
                {
                    var tenant = ctx.Principal?.FindFirst(JwtOptions.TenantClaim)?.Value;
                    var subject = ctx.Principal?.FindFirst("sub")?.Value;
                    if (!Guid.TryParse(tenant, out var t) || t == Guid.Empty || !Guid.TryParse(subject, out var u) || u == Guid.Empty)
                        ctx.Fail("The token does not identify a tenant and a user.");
                    return Task.CompletedTask;
                },
            };
        });
    builder.Services.AddAuthorization();

    builder.Services.AddRowRateLimiting();

    builder.Services.AddControllers()
        .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

    // ── Document attachments: size limit from configuration (Documents:MaxBytes, default 25 MB) ──
    // Kestrel's own request-body cap (about 28 MB) still applies; raise it too if MaxBytes goes above that.
    builder.Services.AddOptions<DocumentOptions>()
        .Configure<IConfiguration>((o, cfg) => cfg.GetSection(DocumentOptions.SectionName).Bind(o));
    builder.Services.AddOptions<FormOptions>()
        .Configure<Microsoft.Extensions.Options.IOptions<DocumentOptions>>(
            (form, docs) => form.MultipartBodyLengthLimit = docs.Value.MaxBytes + 1024 * 1024); // + form fields

    // ── Swagger: versioned routes (/api/v1/...) ──
    // Behind the Aurigo gateway these are exposed under a /api/row-management/v1/... prefix that the
    // gateway adds; the app itself always serves /api/v1/....
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Aurigo.RowManagement.Api",
            Version = "v1",
            Description = "Right-of-Way acquisition management API (MVP: project to closing).",
        });
        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "Bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "JWT from /api/v1/auth/login, or from /api/v1/auth/dev-token in Development.",
        });
        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme { Reference = new OpenApiReference { Id = "Bearer", Type = ReferenceType.SecurityScheme } },
                Array.Empty<string>()
            }
        });

        var xml = Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml");
        if (File.Exists(xml)) c.IncludeXmlComments(xml);
    });

    // ── CORS (the Vite dev server and preview) ──
    builder.Services.AddCors();
    builder.Services.AddOptions<CorsOptions>().Configure<IConfiguration>((o, cfg) =>
    {
        var origins = (cfg["Cors:AllowedOrigins"] ?? "http://localhost:5173,http://localhost:4173")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod());
    });

    builder.Services.AddHealthChecks().AddCheck<DbHealthCheck>("database");

    var app = builder.Build();

    // Fail fast: a missing or too-short JWT signing key must stop startup, not the first login.
    _ = app.Services.GetRequiredService<JwtOptions>();
    // Same for the tax ID encryption key: a missing key stops startup, not the first tax ID saved.
    _ = app.Services.GetRequiredService<Aurigo.RowManagement.Infrastructure.Security.TaxIdOptions>();

    // ── Database: migrate (fail fast on a bad schema), then dev-only seed ──
    if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RowDbContext>();
        if (db.Database.IsRelational())
        {
            var cs = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
            Log.Information("Database: {Host}:{Port}/{Database}", cs.Host, cs.Port, cs.Database);
            await db.Database.MigrateAsync();
        }
        else
        {
            await db.Database.EnsureCreatedAsync(); // non-relational provider (tests)
        }
    }

    // Seeding runs after migration. Platform steps (default roles and permissions) run everywhere; demo
    // steps gate themselves to Development inside SeedRunner.
    if (app.Configuration.GetValue("Database:SeedOnStartup", true))
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SeedRunner>().RunAsync();
    }

    app.UseResponseCompression();
    app.UseSerilogRequestLogging();
    app.UseMiddleware<ExceptionHandlingMiddleware>();
    app.UseRouting();
    app.UseCors();
    app.UseAuthentication();
    app.UseRateLimiter();   // after authentication so the per-user write limit can see the caller
    app.UseAuthorization();

    if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
    {
        app.UseSwagger();
        app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Aurigo.RowManagement v1"));
    }

    app.MapControllers();
    app.MapHealthChecks("/health");

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "API failed to start");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Entry-point marker so WebApplicationFactory can host the API in integration tests.</summary>
public partial class Program { }
