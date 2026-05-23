using Microsoft.Extensions.DependencyInjection;

namespace LinguaSign.Audit;

/// <summary>
/// Audit module — append-only audit trail (hashes, timestamps, signer identity, model versions).
/// Phase 3 will add immutable audit logging across document lifecycle events.
/// </summary>
public static class AuditModule
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services)
    {
        // TODO Phase 3: register append-only audit logging services.
        return services;
    }
}
