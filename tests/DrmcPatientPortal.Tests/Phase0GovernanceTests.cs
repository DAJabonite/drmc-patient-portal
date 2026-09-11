using System.Security.Claims;
using DrmcPatientPortal.Infrastructure.EvidenceGates;
using DrmcPatientPortal.Infrastructure.SourceStates;
using DrmcPatientPortal.Middleware;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace DrmcPatientPortal.Tests;

public class Phase0GovernanceTests
{
    [Fact]
    public void EvidenceGate_FailsClosed_WhenFeatureIsNotConfigured()
    {
        var service = CreateService(new FeatureApprovalOptions(), Environments.Production);

        var decision = service.GetDecision("UnknownFeature");

        Assert.False(decision.IsEnabled);
        Assert.Contains("No approval record", decision.Reason);
    }

    [Fact]
    public void EvidenceGate_RequiresCompleteEvidence_InProduction()
    {
        var options = new FeatureApprovalOptions
        {
            Features = new Dictionary<string, FeatureApprovalRecord>
            {
                [FeatureKeys.DigitalId] = new() { Enabled = true }
            }
        };
        var service = CreateService(options, Environments.Production);

        var decision = service.GetDecision(FeatureKeys.DigitalId);

        Assert.False(decision.IsEnabled);
        Assert.Contains("requires complete approval evidence", decision.Reason);
    }

    [Fact]
    public void EvidenceGate_AllowsApprovedMalasakitCalculator()
    {
        var options = new FeatureApprovalOptions
        {
            Features = new Dictionary<string, FeatureApprovalRecord>
            {
                [FeatureKeys.MalasakitCoverageCalculator] = new()
                {
                    Enabled = true,
                    EvidenceReference = "DEC-2026-09-11-MALASAKIT-001",
                    ApprovedBy = "Repository owner",
                    ApprovedAt = DateTimeOffset.Parse("2026-09-11T10:32:50+08:00")
                }
            }
        };
        var service = CreateService(options, Environments.Production);

        Assert.True(service.IsEnabled(FeatureKeys.MalasakitCoverageCalculator));
    }

    [Fact]
    public void SourceState_DistinguishesEmptyFromUnavailable()
    {
        var updatedAt = DateTimeOffset.UtcNow;
        var empty = SourceDataResult<IReadOnlyList<string>>.ConfirmedEmpty(updatedAt);
        var unavailable = SourceDataResult<IReadOnlyList<string>>.Unavailable("Source could not be reached.");

        Assert.Equal(SourceDataState.ConfirmedEmpty, empty.State);
        Assert.Equal(SourceDataState.Unavailable, unavailable.State);
        Assert.NotEqual(empty.State, unavailable.State);
    }

    [Fact]
    public void ProtectedCacheClassifier_CoversAuthenticatedAndPatientRoutes()
    {
        var authenticated = new ClaimsPrincipal(
            new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "patient-1") }, "test"));
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.True(ProtectedResponseCacheMiddleware.IsProtectedRequest("/Patient/Home", anonymous));
        Assert.True(ProtectedResponseCacheMiddleware.IsProtectedRequest("/anything", authenticated));
        Assert.False(ProtectedResponseCacheMiddleware.IsProtectedRequest("/OpdGuide", anonymous));
    }

    private static FeatureApprovalService CreateService(FeatureApprovalOptions options, string environmentName)
        => new(Options.Create(options), new TestWebHostEnvironment { EnvironmentName = environmentName });

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "DrmcPatientPortal.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
