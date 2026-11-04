using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace RouteGo.Reporting.Api.Tests.Support;

/// <summary>
/// The real host over the test database, trusting the test signing key so slice tests can call it
/// with tokens from <see cref="TestAccessTokens"/>. The queue workers are left out: an HTTP test
/// reads the figures the database holds, and a worker polling underneath it would race the
/// assertion. Their own tests drive them directly.
/// </summary>
internal static class ReportingTestHost
{
    public static ApiFactory For(DatabaseFixture database, params Action<IServiceCollection>[] overrides)
    {
        var all = new List<Action<IServiceCollection>> { TestAccessTokens.TrustTestSigningKey, RemoveBackgroundWork };
        all.AddRange(overrides);
        return TestHost.For(database, all.ToArray());
    }

    private static void RemoveBackgroundWork(IServiceCollection services)
    {
        var backgroundWork = services.Where(IsBackgroundWork).ToList();
        foreach (ServiceDescriptor descriptor in backgroundWork)
        {
            services.Remove(descriptor);
        }
    }

    // Program.cs registers every worker through a factory; the framework's own hosted services are
    // registered by type and stay.
    private static bool IsBackgroundWork(ServiceDescriptor descriptor)
    {
        if (descriptor.ServiceType != typeof(IHostedService))
        {
            return false;
        }

        return descriptor.ImplementationFactory != null;
    }
}
