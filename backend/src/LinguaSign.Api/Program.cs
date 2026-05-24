using System.Security.Claims;
using System.Text;
using Hangfire;
using Hangfire.PostgreSql;
using LinguaSign.Analysis;
using LinguaSign.Audit;
using LinguaSign.Documents;
using LinguaSign.Documents.Persistence;
using LinguaSign.Documents.Services;
using LinguaSign.Export;
using LinguaSign.Signing;
using LinguaSign.Translation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// Allow reasonably large PDF uploads (default Kestrel limit is ~28 MB).
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 52_428_800); // 50 MB

var connectionString = builder.Configuration.GetConnectionString("Postgres");

// --- Supabase JWT authentication ---
// Supabase user access tokens carry audience "authenticated" and issuer
// "{SUPABASE_URL}/auth/v1". Legacy projects sign with the HS256 JWT secret
// (set Supabase:JwtSecret); newer projects use asymmetric keys exposed via JWKS.
var supabaseUrl = builder.Configuration["Supabase:Url"]?.TrimEnd('/');
var supabaseJwtSecret = builder.Configuration["Supabase:JwtSecret"];

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var tvp = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudience = "authenticated",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidateIssuer = !string.IsNullOrWhiteSpace(supabaseUrl),
            ValidIssuers = string.IsNullOrWhiteSpace(supabaseUrl)
                ? null
                : [$"{supabaseUrl}/auth/v1", supabaseUrl],
        };

        if (!string.IsNullOrWhiteSpace(supabaseJwtSecret))
        {
            // Legacy HS256: validate the signature with the shared JWT secret.
            tvp.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(supabaseJwtSecret));
        }
        else if (!string.IsNullOrWhiteSpace(supabaseUrl))
        {
            // Asymmetric keys: resolve signing keys from Supabase's JWKS via discovery.
            options.Authority = $"{supabaseUrl}/auth/v1";
            options.MetadataAddress = $"{supabaseUrl}/auth/v1/.well-known/openid-configuration";
        }

        options.TokenValidationParameters = tvp;
    });

builder.Services.AddAuthorization();

// --- CORS (allow the Next.js frontend to call the API cross-origin) ---
var corsOrigins = builder.Configuration["Cors:Origins"]?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    ?? ["http://localhost:3000"];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(corsOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

// --- Background jobs (OCR → translate → analyze pipeline) ---
builder.Services.AddHangfire(cfg => cfg
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(connectionString)));
builder.Services.AddHangfireServer();

// --- Modular monolith: each module owns its own service registration ---
builder.Services
    .AddDocumentsModule(builder.Configuration)
    .AddTranslationModule()
    .AddSigningModule()
    .AddAnalysisModule()
    .AddAuditModule()
    .AddExportModule();

var app = builder.Build();

// Apply EF Core migrations on startup in development for convenience.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    try
    {
        await scope.ServiceProvider.GetRequiredService<LinguaSignDbContext>()
            .Database.MigrateAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Database migration on startup failed — is Postgres running?");
    }

    app.MapOpenApi();
    app.UseHangfireDashboard("/hangfire");
}

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Liveness probe — anonymous.
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "LinguaSign.Api" }))
   .WithName("HealthCheck");

// Current user claims — confirms the JWT pipeline.
app.MapGet("/me", (HttpContext ctx) => Results.Ok(new
{
    userId = GetUserId(ctx),
    email = ctx.User.FindFirst("email")?.Value,
}))
.RequireAuthorization()
.WithName("CurrentUser");

// --- Documents API ---
var documents = app.MapGroup("/api/documents").RequireAuthorization();

documents.MapPost("/", async (HttpRequest req, IDocumentService svc, IBackgroundJobClient jobs, HttpContext ctx) =>
{
    var userId = GetUserId(ctx);
    if (userId is null) return Results.Unauthorized();

    if (!req.HasFormContentType)
        return Results.BadRequest("Expected multipart/form-data with a 'file' field.");

    var form = await req.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0)
        return Results.BadRequest("No file uploaded.");
    if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest("Only PDF files are supported.");

    await using var stream = file.OpenReadStream();
    var summary = await svc.CreateAsync(userId, file.FileName, stream);

    // Kick off OCR extraction in the background.
    jobs.Enqueue<IOcrProcessingService>(s => s.ProcessAsync(summary.Id, CancellationToken.None));

    return Results.Created($"/api/documents/{summary.Id}", summary);
})
.DisableAntiforgery()
.WithName("UploadDocument");

documents.MapGet("/", async (IDocumentService svc, HttpContext ctx) =>
{
    var userId = GetUserId(ctx);
    return userId is null ? Results.Unauthorized() : Results.Ok(await svc.ListAsync(userId));
})
.WithName("ListDocuments");

documents.MapGet("/{id:guid}", async (Guid id, IDocumentService svc, HttpContext ctx) =>
{
    var userId = GetUserId(ctx);
    if (userId is null) return Results.Unauthorized();
    var detail = await svc.GetAsync(userId, id);
    return detail is null ? Results.NotFound() : Results.Ok(detail);
})
.WithName("GetDocument");

documents.MapGet("/{id:guid}/file", async (Guid id, IDocumentService svc, HttpContext ctx) =>
{
    var userId = GetUserId(ctx);
    if (userId is null) return Results.Unauthorized();
    var file = await svc.OpenFileAsync(userId, id);
    return file is null
        ? Results.NotFound()
        : Results.File(file.Stream, "application/pdf", file.FileName);
})
.WithName("GetDocumentFile");

app.Run();

static string? GetUserId(HttpContext ctx)
    => ctx.User.FindFirst("sub")?.Value
       ?? ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
