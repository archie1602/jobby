using Jobby.Dashboard;
using Jobby.Dashboard.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Jobby.Tests.Dashboard;

public class JobbyDashboardBuilderTests
{
    private static JobbyDashboardBuilder NewBuilder(out IServiceCollection services)
    {
        services = new ServiceCollection();
        return new JobbyDashboardBuilder(services, new JobbyDashboardAuthState());
    }

    [Fact]
    public void AllowAnonymous_SetsAnonymousMode()
    {
        var b = NewBuilder(out _);
        b.AllowAnonymous();
        Assert.Equal(JobbyDashboardAuthMode.Anonymous, b.AuthState.Mode);
    }

    [Fact]
    public void RequireHostPolicy_SetsPolicyNames()
    {
        var b = NewBuilder(out _);
        b.RequireHostPolicy("ReadP", "ManageP");
        Assert.Equal(JobbyDashboardAuthMode.Policy, b.AuthState.Mode);
        Assert.Equal("ReadP", b.AuthState.ReadPolicy);
        Assert.Equal("ManageP", b.AuthState.ManagePolicy);
    }

    [Fact]
    public async Task RequireAuthorization_RegistersReadPolicy_AndSetsPolicyMode()
    {
        var b = NewBuilder(out var services);
        b.RequireAuthorization(p => p.RequireAssertion(_ => true));
        Assert.Equal(JobbyDashboardAuthMode.Policy, b.AuthState.Mode);
        var provider = services.BuildServiceProvider().GetRequiredService<IAuthorizationPolicyProvider>();
        Assert.NotNull(await provider.GetPolicyAsync(JobbyDashboardPolicies.Read));
    }

    [Fact]
    public void SecondConflictingDecision_Throws()
    {
        var b = NewBuilder(out _);
        b.AllowAnonymous();
        Assert.Throws<InvalidOperationException>(() => b.RequireHostPolicy("X"));
    }

    [Fact]
    public void RequireAuthorization_ConflictingSecondDecision_DoesNotRegisterPolicyBeforeThrow()
    {
        var b = NewBuilder(out _);
        b.AllowAnonymous();
        Assert.Throws<InvalidOperationException>(() => b.RequireAuthorization(p => p.RequireAssertion(_ => true)));
    }
}