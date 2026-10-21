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
}
