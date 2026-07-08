using Luma.App.Services;

namespace Luma.Tests;

public sealed class ProviderAvailabilityTests
{
    private static ProviderDiagnostic Ready(string name) => new(true, name);
    private static ProviderDiagnostic Missing(string name) => new(false, name);

    [Fact]
    public void KeepsPreferredProviderWhenAvailable()
    {
        var selected = ProviderAvailability.Select(1, [Ready("Claude"), Ready("Codex"), Ready("Grok")]);

        Assert.Equal(1, selected);
    }

    [Fact]
    public void FallsBackToFirstAvailableProvider()
    {
        var selected = ProviderAvailability.Select(0, [Missing("Claude"), Missing("Codex"), Ready("Grok")]);

        Assert.Equal(2, selected);
    }

    [Fact]
    public void PreservesPreferenceWhenNoProviderIsAvailable()
    {
        var selected = ProviderAvailability.Select(1, [Missing("Claude"), Missing("Codex"), Missing("Grok")]);

        Assert.Equal(1, selected);
    }
}
