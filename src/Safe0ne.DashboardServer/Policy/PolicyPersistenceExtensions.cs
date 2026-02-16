using Microsoft.Extensions.DependencyInjection;

namespace Safe0ne.DashboardServer.Policy;

public static class PolicyPersistenceExtensions
{
    /// <summary>
    /// Registers policy persistence services.
    ///
    /// Adds:
    ///  - PolicyCache (Singleton)
    ///  - IPolicyStore => FilePolicyStore (Singleton)
    ///  - PolicyPersistenceService (Singleton)
    ///  - PolicyWarmupHostedService (Hosted)
    /// </summary>
    public static IServiceCollection AddPolicyPersistence(this IServiceCollection services)
    {
        services.AddSingleton<PolicyCache>();
        services.AddSingleton<IPolicyStore>(sp => new FilePolicyStore());
        services.AddSingleton<PolicyPersistenceService>();
        services.AddHostedService<PolicyWarmupHostedService>();
        return services;
    }
}
