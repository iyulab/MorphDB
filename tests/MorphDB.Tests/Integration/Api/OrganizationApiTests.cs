using System.Net;
using System.Net.Http.Json;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Service.Controllers;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// Integration tests for Organization API endpoints.
/// Tests organization CRUD, member management, and invitation flows.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class OrganizationApiTests : IAsyncLifetime
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;
    private Guid _testOrgId;

    public OrganizationApiTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        // Use authenticated client for [Authorize] endpoints
        _client = fixture.Api.CreateAuthenticatedClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    #region Organization CRUD Tests

    [Fact]
    public async Task CreateOrganization_WithValidData_ShouldReturnCreated()
    {
        // Arrange
        var request = new
        {
            Name = $"Test Org {Guid.NewGuid():N}"[..30],
            Slug = $"test-org-{Guid.NewGuid():N}"[..25],
            Description = "Test organization for integration tests"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/organizations", request);

        // Debug: Read response content if not successful
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"API returned {response.StatusCode}: {errorContent}");
        }

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var org = await response.Content.ReadFromJsonAsync<OrganizationApiResponse>();
        org.Should().NotBeNull();
        org!.OrganizationId.Should().NotBeEmpty();
        org.Name.Should().Be(request.Name);
        org.Slug.Should().Be(request.Slug);
        org.Description.Should().Be(request.Description);
        org.Status.Should().Be("Active");

        // Store for other tests
        _testOrgId = org.OrganizationId;
    }

    [Fact]
    public async Task CreateOrganization_WithAutoSlug_ShouldGenerateSlug()
    {
        // Arrange
        var request = new
        {
            Name = $"Auto Slug Org {Guid.NewGuid():N}"[..25],
            Description = "Organization with auto-generated slug"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/organizations", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var org = await response.Content.ReadFromJsonAsync<OrganizationApiResponse>();
        org.Should().NotBeNull();
        org!.Slug.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetOrganization_WithValidId_ShouldReturnOrganization()
    {
        // Arrange - Create an org first
        var createRequest = new
        {
            Name = $"Get Test Org {Guid.NewGuid():N}"[..25],
            Slug = $"get-test-{Guid.NewGuid():N}"[..20]
        };
        var createResponse = await _client.PostAsJsonAsync("/api/organizations", createRequest);
        var createdOrg = await createResponse.Content.ReadFromJsonAsync<OrganizationApiResponse>();

        // Act
        var response = await _client.GetAsync($"/api/organizations/{createdOrg!.OrganizationId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var org = await response.Content.ReadFromJsonAsync<OrganizationApiResponse>();
        org.Should().NotBeNull();
        org!.OrganizationId.Should().Be(createdOrg.OrganizationId);
        org.Name.Should().Be(createRequest.Name);
    }

    [Fact]
    public async Task GetOrganization_WithInvalidId_ShouldReturnNotFound()
    {
        // Act
        var response = await _client.GetAsync($"/api/organizations/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListOrganizations_ShouldReturnUserOrganizations()
    {
        // Arrange - Create a couple of orgs
        await _client.PostAsJsonAsync("/api/organizations", new
        {
            Name = $"List Test Org 1 {Guid.NewGuid():N}"[..25],
            Slug = $"list-1-{Guid.NewGuid():N}"[..18]
        });
        await _client.PostAsJsonAsync("/api/organizations", new
        {
            Name = $"List Test Org 2 {Guid.NewGuid():N}"[..25],
            Slug = $"list-2-{Guid.NewGuid():N}"[..18]
        });

        // Act
        var response = await _client.GetAsync("/api/organizations");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var orgs = await response.Content.ReadFromJsonAsync<List<OrganizationApiResponse>>();
        orgs.Should().NotBeNull();
        orgs!.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task UpdateOrganization_WithValidData_ShouldReturnUpdated()
    {
        // Arrange - Create an org first
        var createRequest = new
        {
            Name = $"Update Test Org {Guid.NewGuid():N}"[..25],
            Slug = $"update-{Guid.NewGuid():N}"[..18]
        };
        var createResponse = await _client.PostAsJsonAsync("/api/organizations", createRequest);
        var createdOrg = await createResponse.Content.ReadFromJsonAsync<OrganizationApiResponse>();

        var updateRequest = new
        {
            Name = $"Updated Org Name {Guid.NewGuid():N}"[..25],
            Description = "Updated description"
        };

        // Act
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/organizations/{createdOrg!.OrganizationId}")
        {
            Content = JsonContent.Create(updateRequest)
        };
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedOrg = await response.Content.ReadFromJsonAsync<OrganizationApiResponse>();
        updatedOrg.Should().NotBeNull();
        updatedOrg!.Name.Should().Be(updateRequest.Name);
        updatedOrg.Description.Should().Be(updateRequest.Description);
    }

    [Fact]
    public async Task DeleteOrganization_WithValidId_ShouldReturnNoContent()
    {
        // Arrange - Create an org first
        var createRequest = new
        {
            Name = $"Delete Test Org {Guid.NewGuid():N}"[..25],
            Slug = $"delete-{Guid.NewGuid():N}"[..18]
        };
        var createResponse = await _client.PostAsJsonAsync("/api/organizations", createRequest);
        var createdOrg = await createResponse.Content.ReadFromJsonAsync<OrganizationApiResponse>();

        // Act
        var response = await _client.DeleteAsync($"/api/organizations/{createdOrg!.OrganizationId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify deletion
        var getResponse = await _client.GetAsync($"/api/organizations/{createdOrg.OrganizationId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Organization Statistics Tests

    [Fact]
    public async Task GetOrganizationStats_WithValidOrg_ShouldReturnStats()
    {
        // Arrange - Create an org
        var createRequest = new
        {
            Name = $"Stats Test Org {Guid.NewGuid():N}"[..25],
            Slug = $"stats-{Guid.NewGuid():N}"[..18]
        };
        var createResponse = await _client.PostAsJsonAsync("/api/organizations", createRequest);
        var createdOrg = await createResponse.Content.ReadFromJsonAsync<OrganizationApiResponse>();

        // Act
        var response = await _client.GetAsync($"/api/organizations/{createdOrg!.OrganizationId}/stats");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var stats = await response.Content.ReadFromJsonAsync<OrganizationStats>();
        stats.Should().NotBeNull();
        stats!.OrganizationId.Should().Be(createdOrg.OrganizationId);
        stats.TotalMembers.Should().BeGreaterThanOrEqualTo(1); // Creator is a member
    }

    #endregion

    #region Member Management Tests

    [Fact]
    public async Task ListMembers_WithValidOrg_ShouldReturnMembers()
    {
        // Arrange - Create an org
        var createRequest = new
        {
            Name = $"Members Test Org {Guid.NewGuid():N}"[..25],
            Slug = $"members-{Guid.NewGuid():N}"[..18]
        };
        var createResponse = await _client.PostAsJsonAsync("/api/organizations", createRequest);
        var createdOrg = await createResponse.Content.ReadFromJsonAsync<OrganizationApiResponse>();

        // Act
        var response = await _client.GetAsync($"/api/organizations/{createdOrg!.OrganizationId}/members");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var members = await response.Content.ReadFromJsonAsync<List<OrganizationMemberApiResponse>>();
        members.Should().NotBeNull();
        members!.Count.Should().BeGreaterThanOrEqualTo(1); // Creator should be owner
    }

    [Fact]
    public async Task AddMember_WithValidData_ShouldReturnCreated()
    {
        // Arrange - Create an org
        var createRequest = new
        {
            Name = $"Add Member Org {Guid.NewGuid():N}"[..25],
            Slug = $"addmem-{Guid.NewGuid():N}"[..18]
        };
        var createResponse = await _client.PostAsJsonAsync("/api/organizations", createRequest);
        var createdOrg = await createResponse.Content.ReadFromJsonAsync<OrganizationApiResponse>();

        var addMemberRequest = new
        {
            UserId = $"user-{Guid.NewGuid():N}",
            Email = $"test-{Guid.NewGuid():N}@example.com"[..30],
            DisplayName = "Test User",
            Role = OrganizationRole.Member
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/organizations/{createdOrg!.OrganizationId}/members",
            addMemberRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var member = await response.Content.ReadFromJsonAsync<OrganizationMemberApiResponse>();
        member.Should().NotBeNull();
        member!.Email.Should().Be(addMemberRequest.Email);
        member.Role.Should().Be("Member");
    }

    [Fact]
    public async Task UpdateMember_WithValidData_ShouldReturnUpdated()
    {
        // Arrange - Create an org and add a member
        var createRequest = new
        {
            Name = $"Update Member Org {Guid.NewGuid():N}"[..25],
            Slug = $"updmem-{Guid.NewGuid():N}"[..18]
        };
        var createResponse = await _client.PostAsJsonAsync("/api/organizations", createRequest);
        var createdOrg = await createResponse.Content.ReadFromJsonAsync<OrganizationApiResponse>();

        var addMemberRequest = new
        {
            UserId = $"user-{Guid.NewGuid():N}",
            Email = $"update-{Guid.NewGuid():N}@example.com"[..32],
            DisplayName = "Original Name",
            Role = OrganizationRole.Member
        };
        var addResponse = await _client.PostAsJsonAsync(
            $"/api/organizations/{createdOrg!.OrganizationId}/members",
            addMemberRequest);
        var addedMember = await addResponse.Content.ReadFromJsonAsync<OrganizationMemberApiResponse>();

        var updateRequest = new
        {
            Role = OrganizationRole.Admin,
            DisplayName = "Updated Name"
        };

        // Act
        var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"/api/organizations/{createdOrg.OrganizationId}/members/{addedMember!.MemberId}")
        {
            Content = JsonContent.Create(updateRequest)
        };
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedMember = await response.Content.ReadFromJsonAsync<OrganizationMemberApiResponse>();
        updatedMember.Should().NotBeNull();
        updatedMember!.Role.Should().Be("Admin");
        updatedMember.DisplayName.Should().Be("Updated Name");
    }

    [Fact]
    public async Task RemoveMember_WithValidId_ShouldReturnNoContent()
    {
        // Arrange - Create an org and add a member
        var createRequest = new
        {
            Name = $"Remove Member Org {Guid.NewGuid():N}"[..25],
            Slug = $"remmem-{Guid.NewGuid():N}"[..18]
        };
        var createResponse = await _client.PostAsJsonAsync("/api/organizations", createRequest);
        var createdOrg = await createResponse.Content.ReadFromJsonAsync<OrganizationApiResponse>();

        var addMemberRequest = new
        {
            UserId = $"user-{Guid.NewGuid():N}",
            Email = $"remove-{Guid.NewGuid():N}@example.com"[..32],
            DisplayName = "To Be Removed",
            Role = OrganizationRole.Member
        };
        var addResponse = await _client.PostAsJsonAsync(
            $"/api/organizations/{createdOrg!.OrganizationId}/members",
            addMemberRequest);
        var addedMember = await addResponse.Content.ReadFromJsonAsync<OrganizationMemberApiResponse>();

        // Act
        var response = await _client.DeleteAsync(
            $"/api/organizations/{createdOrg.OrganizationId}/members/{addedMember!.MemberId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    #endregion

    #region Invitation Tests

    [Fact]
    public async Task CreateInvitation_WithValidData_ShouldReturnCreated()
    {
        // Arrange - Create an org
        var createRequest = new
        {
            Name = $"Invite Test Org {Guid.NewGuid():N}"[..25],
            Slug = $"invite-{Guid.NewGuid():N}"[..18]
        };
        var createResponse = await _client.PostAsJsonAsync("/api/organizations", createRequest);
        var createdOrg = await createResponse.Content.ReadFromJsonAsync<OrganizationApiResponse>();

        var inviteRequest = new
        {
            Email = $"invited-{Guid.NewGuid():N}@example.com"[..35],
            Role = OrganizationRole.Member
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/organizations/{createdOrg!.OrganizationId}/invitations",
            inviteRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var invitation = await response.Content.ReadFromJsonAsync<InvitationApiResponse>();
        invitation.Should().NotBeNull();
        invitation!.Email.Should().Be(inviteRequest.Email);
        invitation.Status.Should().Be("Pending");
    }

    [Fact]
    public async Task ListInvitations_WithValidOrg_ShouldReturnInvitations()
    {
        // Arrange - Create an org and invitation
        var createRequest = new
        {
            Name = $"List Invite Org {Guid.NewGuid():N}"[..25],
            Slug = $"lstinv-{Guid.NewGuid():N}"[..18]
        };
        var createResponse = await _client.PostAsJsonAsync("/api/organizations", createRequest);
        var createdOrg = await createResponse.Content.ReadFromJsonAsync<OrganizationApiResponse>();

        await _client.PostAsJsonAsync(
            $"/api/organizations/{createdOrg!.OrganizationId}/invitations",
            new
            {
                Email = $"list-inv-{Guid.NewGuid():N}@example.com"[..35],
                Role = OrganizationRole.Member
            });

        // Act
        var response = await _client.GetAsync($"/api/organizations/{createdOrg.OrganizationId}/invitations");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var invitations = await response.Content.ReadFromJsonAsync<List<InvitationApiResponse>>();
        invitations.Should().NotBeNull();
        invitations!.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task RevokeInvitation_WithValidId_ShouldReturnNoContent()
    {
        // Arrange - Create an org and invitation
        var createRequest = new
        {
            Name = $"Revoke Invite Org {Guid.NewGuid():N}"[..25],
            Slug = $"revinv-{Guid.NewGuid():N}"[..18]
        };
        var createResponse = await _client.PostAsJsonAsync("/api/organizations", createRequest);
        var createdOrg = await createResponse.Content.ReadFromJsonAsync<OrganizationApiResponse>();

        var inviteResponse = await _client.PostAsJsonAsync(
            $"/api/organizations/{createdOrg!.OrganizationId}/invitations",
            new
            {
                Email = $"revoke-{Guid.NewGuid():N}@example.com"[..35],
                Role = OrganizationRole.Member
            });
        var invitation = await inviteResponse.Content.ReadFromJsonAsync<InvitationApiResponse>();

        // Act
        var response = await _client.DeleteAsync(
            $"/api/organizations/{createdOrg.OrganizationId}/invitations/{invitation!.InvitationId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    #endregion

    #region Settings and Configuration Tests

    [Fact]
    public async Task UpdateOrganization_WithSettings_ShouldPersistSettings()
    {
        // Arrange - Create an org
        var createRequest = new
        {
            Name = $"Settings Test Org {Guid.NewGuid():N}"[..25],
            Slug = $"settings-{Guid.NewGuid():N}"[..18]
        };
        var createResponse = await _client.PostAsJsonAsync("/api/organizations", createRequest);
        var createdOrg = await createResponse.Content.ReadFromJsonAsync<OrganizationApiResponse>();

        var updateRequest = new
        {
            Settings = new
            {
                DefaultLocale = "ko-KR",
                Timezone = "Asia/Seoul",
                MaxProjects = 10,
                MaxMembers = 50
            }
        };

        // Act
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/organizations/{createdOrg!.OrganizationId}")
        {
            Content = JsonContent.Create(updateRequest)
        };
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedOrg = await response.Content.ReadFromJsonAsync<OrganizationApiResponse>();
        updatedOrg.Should().NotBeNull();
        updatedOrg!.Settings.Should().NotBeNull();
        updatedOrg.Settings!.DefaultLocale.Should().Be("ko-KR");
        updatedOrg.Settings.Timezone.Should().Be("Asia/Seoul");
        updatedOrg.Settings.MaxProjects.Should().Be(10);
        updatedOrg.Settings.MaxMembers.Should().Be(50);
    }

    #endregion
}
