using Microsoft.Extensions.Options;
using Moq;
using Orleans.TestKit;
using PartyTown.Configuration;
using PartyTown.Grains.Generation;

namespace BackendTest;

/// <summary>Provider edits reach live endpoint grains instead of waiting for deactivation.</summary>
public class LlmProviderConfigGrainTest : TestKitBase
{
    [Fact]
    public async Task UpdateAndRemove_PushConfigToTheEndpoint()
    {
        Silo.AddService<IOptions<LlmOptions>>(Options.Create(new LlmOptions()));
        Silo.AddProbe<ILlmRouterGrain>(0);
        var entry = new LlmProviderEntry { Type = "ollama", BaseUrl = "http://a", ModelName = "m1" };
        var config = await Silo.CreateGrainAsync<LlmProviderConfigGrain>(0);
        var added = await config.AddProviderAsync(entry);
        var endpoint = Silo.AddProbe<IOllamaEndpointGrain>(added.Id);

        await config.UpdateProviderAsync(added with { ModelName = "m2" });
        await config.RemoveProviderAsync(added.Id);

        endpoint.Verify(e => e.ApplyConfigAsync(It.Is<LlmProviderEntry?>(c => c != null && c.ModelName == "m2")), Times.Once);
        endpoint.Verify(e => e.ApplyConfigAsync(null), Times.Once);
    }

    private async Task<(LlmProviderConfigGrain Config, LlmProviderEntry Provider)> ConfigWithProviderAsync()
    {
        Silo.AddService<IOptions<LlmOptions>>(Options.Create(new LlmOptions()));
        Silo.AddProbe<ILlmRouterGrain>(0);
        var config = await Silo.CreateGrainAsync<LlmProviderConfigGrain>(0);
        var provider = await config.AddProviderAsync(new LlmProviderEntry { Type = "ollama", BaseUrl = "http://a", ModelName = "m1" });
        Silo.AddProbe<IOllamaEndpointGrain>(provider.Id);
        return (config, provider);
    }

    [Fact]
    public async Task SetPersonaRoutes_ReplacesThatPersonaOnly_AndStampsTheId()
    {
        var (config, provider) = await ConfigWithProviderAsync();
        var (vlad, denise) = (Guid.NewGuid(), Guid.NewGuid());
        var voice = new PersonaRoute { Tier = JobComplexity.CharacterVoice, ProviderId = provider.Id };
        var general = new PersonaRoute { Tier = JobComplexity.General, ProviderId = provider.Id };

        await config.SetPersonaRoutesAsync(denise, new List<PersonaRoute> { voice });
        await config.SetPersonaRoutesAsync(vlad, new List<PersonaRoute> { voice, general });
        await config.SetPersonaRoutesAsync(vlad, new List<PersonaRoute> { general });

        var routes = await config.GetPersonaRoutesAsync();
        Assert.Equal(2, routes.Count);
        Assert.Contains(routes, r => r.PersonaId == denise && r.Tier == JobComplexity.CharacterVoice);
        Assert.Contains(routes, r => r.PersonaId == vlad && r.Tier == JobComplexity.General);
    }

    [Fact]
    public async Task SetPersonaRoutes_RefusesUnknownProvidersAndCombinedTiers()
    {
        var (config, provider) = await ConfigWithProviderAsync();
        var persona = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentException>(() => config.SetPersonaRoutesAsync(persona, new List<PersonaRoute>
        {
            new() { Tier = JobComplexity.General, ProviderId = Guid.NewGuid() },
        }));
        await Assert.ThrowsAsync<ArgumentException>(() => config.SetPersonaRoutesAsync(persona, new List<PersonaRoute>
        {
            new() { Tier = JobComplexity.General | JobComplexity.CharacterVoice, ProviderId = provider.Id },
        }));
        Assert.Empty(await config.GetPersonaRoutesAsync());
    }

    [Fact]
    public async Task RemoveProvider_DropsItsPersonaRoutes()
    {
        var (config, provider) = await ConfigWithProviderAsync();
        await config.SetPersonaRoutesAsync(Guid.NewGuid(), new List<PersonaRoute>
        {
            new() { Tier = JobComplexity.General, ProviderId = provider.Id },
        });

        await config.RemoveProviderAsync(provider.Id);

        Assert.Empty(await config.GetPersonaRoutesAsync());
    }
}
