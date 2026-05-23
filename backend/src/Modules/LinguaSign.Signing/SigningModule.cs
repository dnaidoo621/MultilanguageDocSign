using Microsoft.Extensions.DependencyInjection;

namespace LinguaSign.Signing;

/// <summary>
/// Signing module — e-signature workflow integration.
/// Phase 3 will integrate Documenso (self-hosted, AES-level) and signature metadata capture.
/// </summary>
public static class SigningModule
{
    public static IServiceCollection AddSigningModule(this IServiceCollection services)
    {
        // TODO Phase 3: register Documenso integration and signature workflow services.
        return services;
    }
}
