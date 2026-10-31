using Microsoft.Extensions.DependencyInjection;
using Vela.Api.Images;
using Vela.Api.Tests.Support;

namespace Vela.Api.Tests.Images;

/// <summary>Review round 1, F9: the decode gate is one process-wide singleton; a generator without it must not exist.</summary>
public class DecodeGateRegistrationTests
{
    [Fact]
    public void A_generator_cannot_be_resolved_without_the_gate()
    {
        var services = new ServiceCollection().AddLogging().AddSingleton<IBlobStore>(new InMemoryBlobStore()).AddScoped<IVariantGenerator, VariantGenerator>();
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IVariantGenerator>());
    }

    [Fact]
    public void Startup_resolves_the_gate_so_a_missing_registration_fails_fast()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => ImageDecodeGate.EnsureRegistered(services.BuildServiceProvider()));
        services.AddSingleton(new ImageDecodeGate(1, TimeSpan.Zero));
        using var sp = services.BuildServiceProvider();
        Assert.Same(sp.GetRequiredService<ImageDecodeGate>(), ImageDecodeGate.EnsureRegistered(sp));
    }
}
