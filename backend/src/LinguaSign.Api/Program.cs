using System.Security.Claims;
using System.Text;
using Hangfire;
using Hangfire.PostgreSql;
using LinguaSign.Analysis;
using LinguaSign.Analysis.Persistence;
using LinguaSign.Analysis.Services;
using LinguaSign.Audit;
using LinguaSign.Audit.Persistence;
using LinguaSign.Audit.Services;
using LinguaSign.Documents;
using LinguaSign.Documents.Persistence;
using LinguaSign.Documents.Services;
using LinguaSign.Export;
using LinguaSign.Signing;
using LinguaSign.Signing.Contracts;
using LinguaSign.Signing.Persistence;
using LinguaSign.Signing.Services;
using LinguaSign.Translation;
using LinguaSign.Translation.Persistence;
using LinguaSign.Translation.Services;
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
    .AddTranslationModule(builder.Configuration)
    .AddSigningModule(builder.Configuration)
    .AddAnalysisModule(builder.Configuration)
    .AddAuditModule(builder.Configuration)
    .AddExportModule();

var app = builder.Build();

// Apply EF Core migrations on startup (all environments). For a single API replica this is
// the simplest path; scale-out deployments should run migrations as a separate job instead.
using (var scope = app.Services.CreateScope())
{
    try
    {
        await scope.ServiceProvider.GetRequiredService<LinguaSignDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<TranslationDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<SigningDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AuditDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AnalysisDbContext>().Database.MigrateAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Database migration on startup failed — is Postgres reachable?");
    }
}

// Dev-only surfaces: OpenAPI document + Hangfire dashboard.
if (app.Environment.IsDevelopment())
{
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

// --- Translation API ---
documents.MapPost("/{id:guid}/translate", async (Guid id, TranslateRequest? req, ITranslationService svc, IBackgroundJobClient jobs, HttpContext ctx) =>
{
    var userId = GetUserId(ctx);
    if (userId is null) return Results.Unauthorized();

    var target = string.IsNullOrWhiteSpace(req?.TargetLanguage) ? "en" : req!.TargetLanguage!;
    try
    {
        var summary = await svc.StartAsync(userId, id, target);
        jobs.Enqueue<ITranslationProcessingService>(s => s.ProcessAsync(summary.Id, CancellationToken.None));
        return Results.Accepted($"/api/documents/{id}/translation?target={target}", summary);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(ex.Message);
    }
})
.WithName("StartTranslation");

documents.MapGet("/{id:guid}/translation", async (Guid id, string? target, ITranslationService svc, HttpContext ctx) =>
{
    var userId = GetUserId(ctx);
    if (userId is null) return Results.Unauthorized();
    var detail = await svc.GetAsync(userId, id, string.IsNullOrWhiteSpace(target) ? "en" : target);
    return detail is null ? Results.NotFound() : Results.Ok(detail);
})
.WithName("GetTranslation");

// --- Signing API ---
documents.MapPost("/{id:guid}/sign", async (Guid id, SignRequest req, ISigningService svc, HttpContext ctx) =>
{
    var userId = GetUserId(ctx);
    if (userId is null) return Results.Unauthorized();
    if (req is null || string.IsNullOrWhiteSpace(req.SignerName))
        return Results.BadRequest("signerName is required.");

    var ip = ctx.Connection.RemoteIpAddress?.ToString();
    var ua = ctx.Request.Headers.UserAgent.ToString();
    try
    {
        return Results.Ok(await svc.SignAsync(userId, id, req, ip, ua));
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(ex.Message);
    }
})
.WithName("SignDocument");

documents.MapGet("/{id:guid}/signature", async (Guid id, ISigningService svc, HttpContext ctx) =>
{
    var userId = GetUserId(ctx);
    if (userId is null) return Results.Unauthorized();
    var dto = await svc.GetAsync(userId, id);
    return dto is null ? Results.NotFound() : Results.Ok(dto);
})
.WithName("GetSignature");

documents.MapGet("/{id:guid}/signed-pdf", async (Guid id, ISigningService svc, HttpContext ctx) =>
{
    var userId = GetUserId(ctx);
    if (userId is null) return Results.Unauthorized();
    var file = await svc.OpenSignedPdfAsync(userId, id);
    return file is null
        ? Results.NotFound()
        : Results.File(file.Stream, "application/pdf", file.FileName);
})
.WithName("GetSignedPdf");

// --- Audit API ---
documents.MapGet("/{id:guid}/audit", async (Guid id, IAuditService svc, HttpContext ctx) =>
{
    var userId = GetUserId(ctx);
    return userId is null ? Results.Unauthorized() : Results.Ok(await svc.GetTrailAsync(userId, id));
})
.WithName("GetAuditTrail");

// --- Export API ---
documents.MapGet("/{id:guid}/export", async (Guid id, IExportService svc, HttpContext ctx) =>
{
    var userId = GetUserId(ctx);
    if (userId is null) return Results.Unauthorized();
    var pkg = await svc.BuildAuditPackageAsync(userId, id);
    return pkg is null ? Results.NotFound() : Results.File(pkg.Content, pkg.ContentType, pkg.FileName);
})
.WithName("ExportAuditPackage");

// --- Analysis API (risk detection + explanations) ---
documents.MapPost("/{id:guid}/analyze", async (Guid id, IAnalysisService svc, IBackgroundJobClient jobs, HttpContext ctx) =>
{
    var userId = GetUserId(ctx);
    if (userId is null) return Results.Unauthorized();
    try
    {
        var summary = await svc.StartAsync(userId, id);
        jobs.Enqueue<IAnalysisProcessingService>(s => s.ProcessAsync(summary.Id, CancellationToken.None));
        return Results.Accepted($"/api/documents/{id}/analysis", summary);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(ex.Message);
    }
})
.WithName("StartAnalysis");

documents.MapGet("/{id:guid}/analysis", async (Guid id, IAnalysisService svc, HttpContext ctx) =>
{
    var userId = GetUserId(ctx);
    if (userId is null) return Results.Unauthorized();
    var detail = await svc.GetAsync(userId, id);
    return detail is null ? Results.NotFound() : Results.Ok(detail);
})
.WithName("GetAnalysis");

app.Run();

static string? GetUserId(HttpContext ctx)
    => ctx.User.FindFirst("sub")?.Value
       ?? ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

record TranslateRequest(string? TargetLanguage);
