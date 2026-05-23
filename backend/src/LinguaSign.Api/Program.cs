using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using LinguaSign.Documents;
using LinguaSign.Translation;
using LinguaSign.Signing;
using LinguaSign.Analysis;
using LinguaSign.Audit;
using LinguaSign.Export;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// --- Supabase JWT authentication ---
// Supabase issues access tokens under {SUPABASE_URL}/auth/v1 and publishes signing
// keys at {SUPABASE_URL}/auth/v1/.well-known/jwks.json. Set Supabase:Url in config
// (see appsettings / .env). Legacy projects using the HS256 JWT secret can instead set
// TokenValidationParameters.IssuerSigningKey from Supabase:JwtSecret.
var supabaseUrl = builder.Configuration["Supabase:Url"]?.TrimEnd('/');

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        if (!string.IsNullOrWhiteSpace(supabaseUrl))
        {
            options.Authority = $"{supabaseUrl}/auth/v1";
            options.MetadataAddress = $"{supabaseUrl}/auth/v1/.well-known/openid-configuration";
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = !string.IsNullOrWhiteSpace(supabaseUrl),
            ValidIssuer = $"{supabaseUrl}/auth/v1",
            ValidateAudience = true,
            ValidAudience = "authenticated",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true
        };
    });

builder.Services.AddAuthorization();

// --- Modular monolith: each module owns its own service registration ---
builder.Services
    .AddDocumentsModule()
    .AddTranslationModule()
    .AddSigningModule()
    .AddAnalysisModule()
    .AddAuditModule()
    .AddExportModule();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

// Liveness probe — anonymous.
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "LinguaSign.Api" }))
   .WithName("HealthCheck");

// Example protected endpoint — confirms the JWT pipeline is wired.
app.MapGet("/me", (HttpContext ctx) => Results.Ok(new
{
    userId = ctx.User.FindFirst("sub")?.Value,
    email = ctx.User.FindFirst("email")?.Value
}))
.RequireAuthorization()
.WithName("CurrentUser");

app.Run();
