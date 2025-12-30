using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Service.Controllers;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// Integration tests for SSO API endpoints.
/// Tests SSO configuration CRUD, login flow initiation, and configuration management.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class SsoApiTests : IAsyncLifetime
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;
    private Guid _testOrgId;
    private string _testOrgSlug = string.Empty;

    public SsoApiTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        // Use authenticated client for [Authorize] endpoints
        _client = fixture.Api.CreateAuthenticatedClient();
    }

    public async Task InitializeAsync()
    {
        // Create a test organization for SSO tests
        var createOrgRequest = new
        {
            Name = $"SSO Test Org {Guid.NewGuid():N}"[..25],
            Slug = $"sso-org-{Guid.NewGuid():N}"[..18]
        };

        var response = await _client.PostAsJsonAsync("/api/organizations", createOrgRequest);
        if (response.StatusCode == HttpStatusCode.Created)
        {
            var org = await response.Content.ReadFromJsonAsync<OrganizationApiResponse>();
            _testOrgId = org!.OrganizationId;
            _testOrgSlug = org.Slug;
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    #region SSO Configuration CRUD Tests

    [Fact]
    public async Task CreateSsoConfig_WithValidOidcData_ShouldReturnCreated()
    {
        // Arrange
        var request = new
        {
            Name = "Test OIDC Provider",
            ProviderType = SsoProviderType.Oidc,
            Authority = "https://login.example.com",
            ClientId = "test-client-id",
            ClientSecret = "test-secret",
            Scopes = new[] { "openid", "profile", "email" },
            AllowedDomains = new[] { "example.com" },
            AutoProvisionUsers = true,
            DefaultRole = OrganizationRole.Member
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/sso/organizations/{_testOrgId}/configs",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var config = await response.Content.ReadFromJsonAsync<SsoConfigResponse>();
        config.Should().NotBeNull();
        config!.SsoConfigId.Should().NotBeEmpty();
        config.OrganizationId.Should().Be(_testOrgId);
        config.Name.Should().Be(request.Name);
        config.ProviderType.Should().Be(SsoProviderType.Oidc);
        config.Authority.Should().Be(request.Authority);
        config.ClientId.Should().Be(request.ClientId);
        config.HasClientSecret.Should().BeTrue();
        config.AutoProvisionUsers.Should().BeTrue();
    }

    [Fact]
    public async Task CreateSsoConfig_WithEntraId_ShouldReturnCreated()
    {
        // Arrange
        var request = new
        {
            Name = "Azure AD SSO",
            ProviderType = SsoProviderType.EntraId,
            Authority = "https://login.microsoftonline.com/tenant-id/v2.0",
            ClientId = "azure-client-id",
            ClientSecret = "azure-secret",
            Scopes = new[] { "openid", "profile", "email" },
            AutoProvisionUsers = true
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/sso/organizations/{_testOrgId}/configs",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var config = await response.Content.ReadFromJsonAsync<SsoConfigResponse>();
        config.Should().NotBeNull();
        config!.ProviderType.Should().Be(SsoProviderType.EntraId);
    }

    [Fact]
    public async Task ListSsoConfigs_WithValidOrg_ShouldReturnConfigs()
    {
        // Arrange - Create a config first
        await _client.PostAsJsonAsync(
            $"/api/sso/organizations/{_testOrgId}/configs",
            new
            {
                Name = "List Test Config",
                ProviderType = SsoProviderType.Oidc,
                Authority = "https://list.example.com",
                ClientId = "list-client"
            });

        // Act
        var response = await _client.GetAsync($"/api/sso/organizations/{_testOrgId}/configs");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var configs = await response.Content.ReadFromJsonAsync<List<SsoConfigResponse>>();
        configs.Should().NotBeNull();
        configs!.Count.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task GetSsoConfig_WithValidId_ShouldReturnConfig()
    {
        // Arrange - Create a config
        var createResponse = await _client.PostAsJsonAsync(
            $"/api/sso/organizations/{_testOrgId}/configs",
            new
            {
                Name = "Get Test Config",
                ProviderType = SsoProviderType.Oidc,
                Authority = "https://get.example.com",
                ClientId = "get-client"
            });
        var created = await createResponse.Content.ReadFromJsonAsync<SsoConfigResponse>();

        // Act
        var response = await _client.GetAsync($"/api/sso/configs/{created!.SsoConfigId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var config = await response.Content.ReadFromJsonAsync<SsoConfigResponse>();
        config.Should().NotBeNull();
        config!.SsoConfigId.Should().Be(created.SsoConfigId);
        config.Name.Should().Be("Get Test Config");
    }

    [Fact]
    public async Task UpdateSsoConfig_WithValidData_ShouldReturnUpdated()
    {
        // Arrange - Create a config
        var createResponse = await _client.PostAsJsonAsync(
            $"/api/sso/organizations/{_testOrgId}/configs",
            new
            {
                Name = "Update Test Config",
                ProviderType = SsoProviderType.Oidc,
                Authority = "https://update.example.com",
                ClientId = "update-client"
            });
        var created = await createResponse.Content.ReadFromJsonAsync<SsoConfigResponse>();

        var updateRequest = new
        {
            Name = "Updated Config Name",
            Authority = "https://updated.example.com",
            AutoProvisionUsers = false
        };

        // Act
        var response = await _client.PutAsJsonAsync(
            $"/api/sso/configs/{created!.SsoConfigId}",
            updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<SsoConfigResponse>();
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Updated Config Name");
        updated.Authority.Should().Be("https://updated.example.com");
        updated.AutoProvisionUsers.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteSsoConfig_WithValidId_ShouldReturnNoContent()
    {
        // Arrange - Create a config
        var createResponse = await _client.PostAsJsonAsync(
            $"/api/sso/organizations/{_testOrgId}/configs",
            new
            {
                Name = "Delete Test Config",
                ProviderType = SsoProviderType.Oidc,
                Authority = "https://delete.example.com",
                ClientId = "delete-client"
            });
        var created = await createResponse.Content.ReadFromJsonAsync<SsoConfigResponse>();

        // Act
        var response = await _client.DeleteAsync($"/api/sso/configs/{created!.SsoConfigId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify deletion
        var getResponse = await _client.GetAsync($"/api/sso/configs/{created.SsoConfigId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region SSO Activation Tests

    [Fact]
    public async Task ActivateSsoConfig_WithInvalidAuthority_ShouldReturnBadRequest()
    {
        // Arrange - Create a config with an invalid/unreachable authority
        var createResponse = await _client.PostAsJsonAsync(
            $"/api/sso/organizations/{_testOrgId}/configs",
            new
            {
                Name = "Activate Test Config",
                ProviderType = SsoProviderType.Oidc,
                Authority = "https://activate.example.com",
                ClientId = "activate-client"
            });
        var created = await createResponse.Content.ReadFromJsonAsync<SsoConfigResponse>();

        // Act - Activation should fail because the authority cannot be validated
        var response = await _client.PostAsync($"/api/sso/configs/{created!.SsoConfigId}/activate", null);

        // Assert - Should return BadRequest because OIDC discovery fails
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Verify config status is Error (set when validation fails)
        var getResponse = await _client.GetAsync($"/api/sso/configs/{created.SsoConfigId}");
        var config = await getResponse.Content.ReadFromJsonAsync<SsoConfigResponse>();
        config!.Status.Should().Be(SsoConfigStatus.Error);
    }

    [Fact]
    public async Task DeactivateSsoConfig_WithDisabledConfig_ShouldReturnNoContent()
    {
        // Arrange - Create a config (starts as Disabled)
        var createResponse = await _client.PostAsJsonAsync(
            $"/api/sso/organizations/{_testOrgId}/configs",
            new
            {
                Name = "Deactivate Test Config",
                ProviderType = SsoProviderType.Oidc,
                Authority = "https://deactivate.example.com",
                ClientId = "deactivate-client"
            });
        var created = await createResponse.Content.ReadFromJsonAsync<SsoConfigResponse>();

        // Act - Deactivate the config (should work even if already disabled)
        var response = await _client.PostAsync($"/api/sso/configs/{created!.SsoConfigId}/deactivate", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify config is disabled
        var getResponse = await _client.GetAsync($"/api/sso/configs/{created.SsoConfigId}");
        var config = await getResponse.Content.ReadFromJsonAsync<SsoConfigResponse>();
        config!.Status.Should().Be(SsoConfigStatus.Disabled);
    }

    #endregion

    #region SSO Test Configuration Tests

    [Fact]
    public async Task TestSsoConfig_WithInvalidAuthority_ShouldReturnError()
    {
        // Arrange - Create a config with invalid authority
        var createResponse = await _client.PostAsJsonAsync(
            $"/api/sso/organizations/{_testOrgId}/configs",
            new
            {
                Name = "Invalid Test Config",
                ProviderType = SsoProviderType.Oidc,
                Authority = "https://invalid.nonexistent.example.com",
                ClientId = "invalid-client"
            });
        var created = await createResponse.Content.ReadFromJsonAsync<SsoConfigResponse>();

        // Act
        var response = await _client.PostAsync($"/api/sso/configs/{created!.SsoConfigId}/test", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<SsoTestResult>();
        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region SSO Login Flow Tests

    [Fact]
    public async Task InitiateLogin_WithNoActiveSso_ShouldReturnNotFound()
    {
        // Arrange - Use an org with no active SSO
        var newOrgResponse = await _client.PostAsJsonAsync("/api/organizations", new
        {
            Name = $"No SSO Org {Guid.NewGuid():N}"[..20],
            Slug = $"nosso-{Guid.NewGuid():N}"[..15]
        });
        var newOrg = await newOrgResponse.Content.ReadFromJsonAsync<OrganizationApiResponse>();

        // Act
        var response = await _client.GetAsync(
            $"/api/sso/login/{newOrg!.Slug}?redirectUri=https://app.example.com/callback");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task InitiateLogin_WithMissingRedirectUri_ShouldReturnBadRequest()
    {
        // Act - No redirectUri parameter
        var response = await _client.GetAsync($"/api/sso/login/{_testOrgSlug}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Claim Mapping Tests

    [Fact]
    public async Task CreateSsoConfig_WithCustomClaimMappings_ShouldPersistMappings()
    {
        // Arrange
        var request = new
        {
            Name = "Custom Claims Config",
            ProviderType = SsoProviderType.Oidc,
            Authority = "https://claims.example.com",
            ClientId = "claims-client",
            ClaimMappings = new
            {
                SubjectClaim = "user_id",
                EmailClaim = "mail",
                NameClaim = "display_name",
                FirstNameClaim = "first",
                LastNameClaim = "last",
                GroupsClaim = "roles"
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/sso/organizations/{_testOrgId}/configs",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var config = await response.Content.ReadFromJsonAsync<SsoConfigResponse>();
        config.Should().NotBeNull();
        config!.ClaimMappings.Should().NotBeNull();
        config.ClaimMappings!.SubjectClaim.Should().Be("user_id");
        config.ClaimMappings.EmailClaim.Should().Be("mail");
        config.ClaimMappings.NameClaim.Should().Be("display_name");
    }

    #endregion

    #region Domain Restriction Tests

    [Fact]
    public async Task CreateSsoConfig_WithDomainRestrictions_ShouldPersistDomains()
    {
        // Arrange
        var request = new
        {
            Name = "Domain Restricted Config",
            ProviderType = SsoProviderType.Oidc,
            Authority = "https://domain.example.com",
            ClientId = "domain-client",
            AllowedDomains = new[] { "company.com", "partner.com" }
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/sso/organizations/{_testOrgId}/configs",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var config = await response.Content.ReadFromJsonAsync<SsoConfigResponse>();
        config.Should().NotBeNull();
        config!.AllowedDomains.Should().NotBeNull();
        config.AllowedDomains!.Should().Contain("company.com");
        config.AllowedDomains.Should().Contain("partner.com");
    }

    #endregion

    #region Provider Type Tests

    [Theory]
    [InlineData(SsoProviderType.Oidc)]
    [InlineData(SsoProviderType.EntraId)]
    [InlineData(SsoProviderType.Google)]
    [InlineData(SsoProviderType.Okta)]
    [InlineData(SsoProviderType.Auth0)]
    [InlineData(SsoProviderType.Keycloak)]
    public async Task CreateSsoConfig_WithDifferentProviders_ShouldSucceed(SsoProviderType providerType)
    {
        // Arrange
        var request = new
        {
            Name = $"Provider Test {providerType}",
            ProviderType = providerType,
            Authority = $"https://{providerType.ToString().ToLower(CultureInfo.InvariantCulture)}.example.com",
            ClientId = $"{providerType.ToString().ToLower(CultureInfo.InvariantCulture)}-client"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/sso/organizations/{_testOrgId}/configs",
            request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var config = await response.Content.ReadFromJsonAsync<SsoConfigResponse>();
        config.Should().NotBeNull();
        config!.ProviderType.Should().Be(providerType);
    }

    #endregion
}
