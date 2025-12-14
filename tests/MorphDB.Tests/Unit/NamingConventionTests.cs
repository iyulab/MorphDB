using MorphDB.Core.Infrastructure;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Unit tests for NamingConvention converter.
/// </summary>
public class NamingConventionTests
{
    #region ToPascalCase Tests

    [Theory]
    [InlineData("user_account", "UserAccount")]
    [InlineData("user_id", "UserId")]
    [InlineData("created_at", "CreatedAt")]
    [InlineData("first_name", "FirstName")]
    public void ToPascalCase_FromSnakeCase_ShouldConvert(string input, string expected)
    {
        var result = NamingConvention.ToPascalCase(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("userAccount", "UserAccount")]
    [InlineData("userId", "UserId")]
    [InlineData("firstName", "FirstName")]
    [InlineData("isActive", "IsActive")]
    public void ToPascalCase_FromCamelCase_ShouldConvert(string input, string expected)
    {
        var result = NamingConvention.ToPascalCase(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("user-account", "UserAccount")]
    [InlineData("user-id", "UserId")]
    [InlineData("created-at", "CreatedAt")]
    public void ToPascalCase_FromKebabCase_ShouldConvert(string input, string expected)
    {
        var result = NamingConvention.ToPascalCase(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("USER_ACCOUNT", "UserAccount")]
    [InlineData("API_KEY", "ApiKey")]
    [InlineData("DATABASE_URL", "DatabaseUrl")]
    public void ToPascalCase_FromScreamingSnakeCase_ShouldConvert(string input, string expected)
    {
        var result = NamingConvention.ToPascalCase(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("UserAccount", "UserAccount")]
    [InlineData("UserId", "UserId")]
    public void ToPascalCase_AlreadyPascalCase_ShouldRemainSame(string input, string expected)
    {
        var result = NamingConvention.ToPascalCase(input);
        result.Should().Be(expected);
    }

    #endregion

    #region ToCamelCase Tests

    [Theory]
    [InlineData("user_account", "userAccount")]
    [InlineData("user_id", "userId")]
    [InlineData("created_at", "createdAt")]
    [InlineData("first_name", "firstName")]
    public void ToCamelCase_FromSnakeCase_ShouldConvert(string input, string expected)
    {
        var result = NamingConvention.ToCamelCase(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("UserAccount", "userAccount")]
    [InlineData("UserId", "userId")]
    [InlineData("FirstName", "firstName")]
    [InlineData("IsActive", "isActive")]
    public void ToCamelCase_FromPascalCase_ShouldConvert(string input, string expected)
    {
        var result = NamingConvention.ToCamelCase(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("user-account", "userAccount")]
    [InlineData("user-id", "userId")]
    public void ToCamelCase_FromKebabCase_ShouldConvert(string input, string expected)
    {
        var result = NamingConvention.ToCamelCase(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("userAccount", "userAccount")]
    [InlineData("userId", "userId")]
    public void ToCamelCase_AlreadyCamelCase_ShouldRemainSame(string input, string expected)
    {
        var result = NamingConvention.ToCamelCase(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("XMLParser", "xmlParser")]
    [InlineData("HTTPClient", "httpClient")]
    [InlineData("ID", "id")]
    [InlineData("UserID", "userId")]
    public void ToCamelCase_WithAcronyms_ShouldConvertCorrectly(string input, string expected)
    {
        var result = NamingConvention.ToCamelCase(input);
        result.Should().Be(expected);
    }

    #endregion

    #region ToSnakeCase Tests

    [Theory]
    [InlineData("UserAccount", "user_account")]
    [InlineData("UserId", "user_id")]
    [InlineData("CreatedAt", "created_at")]
    [InlineData("FirstName", "first_name")]
    public void ToSnakeCase_FromPascalCase_ShouldConvert(string input, string expected)
    {
        var result = NamingConvention.ToSnakeCase(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("userAccount", "user_account")]
    [InlineData("userId", "user_id")]
    [InlineData("firstName", "first_name")]
    [InlineData("isActive", "is_active")]
    public void ToSnakeCase_FromCamelCase_ShouldConvert(string input, string expected)
    {
        var result = NamingConvention.ToSnakeCase(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("user-account", "user_account")]
    [InlineData("user-id", "user_id")]
    public void ToSnakeCase_FromKebabCase_ShouldConvert(string input, string expected)
    {
        var result = NamingConvention.ToSnakeCase(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("user_account", "user_account")]
    [InlineData("user_id", "user_id")]
    public void ToSnakeCase_AlreadySnakeCase_ShouldRemainSame(string input, string expected)
    {
        var result = NamingConvention.ToSnakeCase(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("XMLParser", "xml_parser")]
    [InlineData("HTTPClient", "http_client")]
    [InlineData("UserID", "user_id")]
    public void ToSnakeCase_WithAcronyms_ShouldConvertCorrectly(string input, string expected)
    {
        var result = NamingConvention.ToSnakeCase(input);
        result.Should().Be(expected);
    }

    #endregion

    #region ToKebabCase Tests

    [Theory]
    [InlineData("UserAccount", "user-account")]
    [InlineData("UserId", "user-id")]
    [InlineData("CreatedAt", "created-at")]
    public void ToKebabCase_FromPascalCase_ShouldConvert(string input, string expected)
    {
        var result = NamingConvention.ToKebabCase(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("userAccount", "user-account")]
    [InlineData("userId", "user-id")]
    public void ToKebabCase_FromCamelCase_ShouldConvert(string input, string expected)
    {
        var result = NamingConvention.ToKebabCase(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("user_account", "user-account")]
    [InlineData("user_id", "user-id")]
    public void ToKebabCase_FromSnakeCase_ShouldConvert(string input, string expected)
    {
        var result = NamingConvention.ToKebabCase(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("user-account", "user-account")]
    [InlineData("user-id", "user-id")]
    public void ToKebabCase_AlreadyKebabCase_ShouldRemainSame(string input, string expected)
    {
        var result = NamingConvention.ToKebabCase(input);
        result.Should().Be(expected);
    }

    #endregion

    #region ToScreamingSnakeCase Tests

    [Theory]
    [InlineData("UserAccount", "USER_ACCOUNT")]
    [InlineData("apiKey", "API_KEY")]
    [InlineData("databaseUrl", "DATABASE_URL")]
    public void ToScreamingSnakeCase_FromMixedCase_ShouldConvert(string input, string expected)
    {
        var result = NamingConvention.ToScreamingSnakeCase(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("user_account", "USER_ACCOUNT")]
    [InlineData("api_key", "API_KEY")]
    public void ToScreamingSnakeCase_FromSnakeCase_ShouldConvert(string input, string expected)
    {
        var result = NamingConvention.ToScreamingSnakeCase(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("USER_ACCOUNT", "USER_ACCOUNT")]
    [InlineData("API_KEY", "API_KEY")]
    public void ToScreamingSnakeCase_AlreadyScreamingSnakeCase_ShouldRemainSame(string input, string expected)
    {
        var result = NamingConvention.ToScreamingSnakeCase(input);
        result.Should().Be(expected);
    }

    #endregion

    #region Edge Cases

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AllConverters_WithNullOrEmpty_ShouldReturnInput(string? input)
    {
        NamingConvention.ToPascalCase(input!).Should().Be(input);
        NamingConvention.ToCamelCase(input!).Should().Be(input);
        NamingConvention.ToSnakeCase(input!).Should().Be(input);
        NamingConvention.ToKebabCase(input!).Should().Be(input);
        NamingConvention.ToScreamingSnakeCase(input!).Should().Be(input);
    }

    [Theory]
    [InlineData("a", "A")]
    [InlineData("A", "A")]
    public void ToPascalCase_SingleChar_ShouldHandleCorrectly(string input, string expected)
    {
        var result = NamingConvention.ToPascalCase(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("a", "a")]
    [InlineData("A", "a")]
    public void ToCamelCase_SingleChar_ShouldHandleCorrectly(string input, string expected)
    {
        var result = NamingConvention.ToCamelCase(input);
        result.Should().Be(expected);
    }

    [Fact]
    public void Convert_WithNamingStyle_ShouldDelegateCorrectly()
    {
        const string input = "user_account";

        NamingConvention.Convert(input, NamingStyle.PascalCase).Should().Be("UserAccount");
        NamingConvention.Convert(input, NamingStyle.CamelCase).Should().Be("userAccount");
        NamingConvention.Convert(input, NamingStyle.SnakeCase).Should().Be("user_account");
        NamingConvention.Convert(input, NamingStyle.KebabCase).Should().Be("user-account");
        NamingConvention.Convert(input, NamingStyle.ScreamingSnakeCase).Should().Be("USER_ACCOUNT");
    }

    #endregion

    #region Extension Methods Tests

    [Fact]
    public void ExtensionMethods_ShouldWorkCorrectly()
    {
        const string input = "user_account";

        input.ToPascalCase().Should().Be("UserAccount");
        input.ToCamelCase().Should().Be("userAccount");
        input.ToSnakeCase().Should().Be("user_account");
        input.ToKebabCase().Should().Be("user-account");
        input.ToScreamingSnakeCase().Should().Be("USER_ACCOUNT");
        input.ToNamingStyle(NamingStyle.PascalCase).Should().Be("UserAccount");
    }

    #endregion

    #region Real-World Scenarios

    [Theory]
    [InlineData("created_at", "createdAt")]      // DB → JSON
    [InlineData("updated_at", "updatedAt")]      // DB → JSON
    [InlineData("tenant_id", "tenantId")]        // DB → JSON
    [InlineData("is_active", "isActive")]        // DB → JSON
    [InlineData("first_name", "firstName")]      // DB → JSON
    [InlineData("last_login_at", "lastLoginAt")] // DB → JSON
    public void DatabaseToJsonApi_ShouldConvertCorrectly(string dbColumn, string jsonKey)
    {
        var result = NamingConvention.ToCamelCase(dbColumn);
        result.Should().Be(jsonKey);
    }

    [Theory]
    [InlineData("CreatedAt", "created_at")]      // C# → DB
    [InlineData("UpdatedAt", "updated_at")]      // C# → DB
    [InlineData("TenantId", "tenant_id")]        // C# → DB
    [InlineData("IsActive", "is_active")]        // C# → DB
    [InlineData("FirstName", "first_name")]      // C# → DB
    public void CSharpToDatabase_ShouldConvertCorrectly(string csharpProperty, string dbColumn)
    {
        var result = NamingConvention.ToSnakeCase(csharpProperty);
        result.Should().Be(dbColumn);
    }

    [Theory]
    [InlineData("UserAccounts", "user-accounts")]  // C# → REST path
    [InlineData("OrderItems", "order-items")]      // C# → REST path
    [InlineData("ApiKeys", "api-keys")]            // C# → REST path
    public void CSharpToRestPath_ShouldConvertCorrectly(string csharpName, string restPath)
    {
        var result = NamingConvention.ToKebabCase(csharpName);
        result.Should().Be(restPath);
    }

    #endregion
}
