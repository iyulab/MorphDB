using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MorphDB.Service.Infrastructure;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// <c>AddAuthentication()</c> registers Data Protection, and the framework then writes a key file and warns,
/// on every start of a container, that the keys may not persist. The service protects no data with it (JWT
/// bearer validates and never encrypts; there are no cookies, antiforgery or session affinity), so the key
/// ring is stated to be in memory. A start that reads a key repository from disk again — or a feature that
/// needs a shared one — must change this on purpose.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class DataProtectionKeyRingTests
{
    private readonly ApiIntegrationFixture _fixture;

    public DataProtectionKeyRingTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void The_key_ring_is_in_memory_so_a_start_writes_no_key_file_and_warns_about_none()
    {
        var keys = _fixture.Api.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        Assert.IsType<InMemoryXmlRepository>(keys.XmlRepository);
    }
}
