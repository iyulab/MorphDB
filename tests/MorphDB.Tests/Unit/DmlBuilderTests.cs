using MorphDB.Npgsql.Dml;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Unit tests for DmlBuilder.
/// </summary>
public class DmlBuilderTests
{
    #region BuildInsert Tests

    [Fact]
    public void BuildInsert_ShouldGenerateValidSql()
    {
        // Arrange
        var columns = new[] { "id", "email", "name" };
        var parameters = new[] { "@p0", "@p1", "@p2" };

        // Act
        var sql = DmlBuilder.BuildInsert("t_users", columns, parameters);

        // Assert
        sql.Should().Contain("INSERT INTO \"t_users\"");
        sql.Should().Contain("\"id\", \"email\", \"name\"");
        sql.Should().Contain("VALUES (@p0, @p1, @p2)");
        sql.Should().Contain("RETURNING *");
    }

    [Fact]
    public void BuildInsert_WithEmptyColumns_ShouldThrow()
    {
        // Arrange
        var columns = Array.Empty<string>();
        var parameters = Array.Empty<string>();

        // Act & Assert
        var act = () => DmlBuilder.BuildInsert("t_users", columns, parameters);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildInsert_WithMismatchedCounts_ShouldThrow()
    {
        // Arrange
        var columns = new[] { "id", "email" };
        var parameters = new[] { "@p0" }; // Mismatched count

        // Act & Assert
        var act = () => DmlBuilder.BuildInsert("t_users", columns, parameters);
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region BuildBulkInsert Tests

    [Fact]
    public void BuildBulkInsert_ShouldGenerateValidSql()
    {
        // Arrange
        var columns = new[] { "id", "email" };
        var arrayParams = new[] { "@arr0", "@arr1" };

        // Act
        var sql = DmlBuilder.BuildBulkInsert("t_users", columns, arrayParams);

        // Assert
        sql.Should().Contain("INSERT INTO \"t_users\"");
        sql.Should().Contain("\"id\", \"email\"");
        sql.Should().Contain("SELECT * FROM UNNEST(@arr0, @arr1)");
        sql.Should().Contain("RETURNING *");
    }

    #endregion

    #region BuildUpdate Tests

    [Fact]
    public void BuildUpdate_ShouldGenerateValidSql()
    {
        // Arrange
        var setColumns = new[]
        {
            ("email", "@p0"),
            ("name", "@p1")
        };
        var whereClause = "\"id\" = @id";

        // Act
        var sql = DmlBuilder.BuildUpdate("t_users", setColumns, whereClause);

        // Assert
        sql.Should().Contain("UPDATE \"t_users\"");
        sql.Should().Contain("SET \"email\" = @p0, \"name\" = @p1");
        sql.Should().Contain("WHERE \"id\" = @id");
        sql.Should().Contain("RETURNING *");
    }

    [Fact]
    public void BuildUpdate_WithEmptySetColumns_ShouldThrow()
    {
        // Arrange
        var setColumns = Array.Empty<(string, string)>();

        // Act & Assert
        var act = () => DmlBuilder.BuildUpdate("t_users", setColumns, "id = @id");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildBatchUpdate_ShouldNotIncludeReturning()
    {
        // Arrange
        var setColumns = new[] { ("status", "@p0") };
        var whereClause = "\"active\" = true";

        // Act
        var sql = DmlBuilder.BuildBatchUpdate("t_users", setColumns, whereClause);

        // Assert
        sql.Should().Contain("UPDATE \"t_users\"");
        sql.Should().Contain("SET \"status\" = @p0");
        sql.Should().Contain("WHERE \"active\" = true");
        sql.Should().NotContain("RETURNING");
    }

    #endregion

    #region BuildDelete Tests

    [Fact]
    public void BuildDelete_ShouldGenerateValidSql()
    {
        // Arrange
        var whereClause = "\"id\" = @id";

        // Act
        var sql = DmlBuilder.BuildDelete("t_users", whereClause);

        // Assert
        sql.Should().Be("DELETE FROM \"t_users\" WHERE \"id\" = @id");
    }

    #endregion

    #region BuildSelectById Tests

    [Fact]
    public void BuildSelectById_ShouldGenerateSelectAll()
    {
        // Act
        var sql = DmlBuilder.BuildSelectById("t_users", "id");

        // Assert
        sql.Should().Contain("SELECT *");
        sql.Should().Contain("FROM \"t_users\"");
        sql.Should().Contain("WHERE \"id\" = @id");
    }

    [Fact]
    public void BuildSelectById_WithColumns_ShouldGenerateSelectSpecific()
    {
        // Arrange
        var columns = new[] { "id", "email", "name" };

        // Act
        var sql = DmlBuilder.BuildSelectById("t_users", "id", columns);

        // Assert
        sql.Should().Contain("SELECT \"id\", \"email\", \"name\"");
        sql.Should().Contain("FROM \"t_users\"");
        sql.Should().Contain("WHERE \"id\" = @id");
    }

    #endregion

    #region BuildUpsert Tests

    [Fact]
    public void BuildUpsert_ShouldGenerateValidSql()
    {
        // Arrange
        var columns = new[] { "id", "email", "name" };
        var parameters = new[] { "@p0", "@p1", "@p2" };
        var keyColumns = new[] { "id" };

        // Act
        var sql = DmlBuilder.BuildUpsert("t_users", columns, parameters, keyColumns);

        // Assert
        sql.Should().Contain("INSERT INTO \"t_users\"");
        sql.Should().Contain("(\"id\", \"email\", \"name\")");
        sql.Should().Contain("VALUES (@p0, @p1, @p2)");
        sql.Should().Contain("ON CONFLICT (\"id\")");
        sql.Should().Contain("DO UPDATE SET");
        sql.Should().Contain("\"email\" = EXCLUDED.\"email\"");
        sql.Should().Contain("\"name\" = EXCLUDED.\"name\"");
        sql.Should().Contain("RETURNING *");
    }

    [Fact]
    public void BuildUpsert_WithCompositeKey_ShouldGenerateValidSql()
    {
        // Arrange
        var columns = new[] { "tenant_id", "user_id", "role" };
        var parameters = new[] { "@p0", "@p1", "@p2" };
        var keyColumns = new[] { "tenant_id", "user_id" };

        // Act
        var sql = DmlBuilder.BuildUpsert("t_tenant_users", columns, parameters, keyColumns);

        // Assert
        sql.Should().Contain("ON CONFLICT (\"tenant_id\", \"user_id\")");
        sql.Should().Contain("\"role\" = EXCLUDED.\"role\"");
    }

    [Fact]
    public void BuildUpsert_WithAllColumnsAsKeys_ShouldGenerateNoOpUpdate()
    {
        // Arrange
        var columns = new[] { "id" };
        var parameters = new[] { "@p0" };
        var keyColumns = new[] { "id" };

        // Act
        var sql = DmlBuilder.BuildUpsert("t_users", columns, parameters, keyColumns);

        // Assert
        sql.Should().Contain("ON CONFLICT (\"id\")");
        sql.Should().Contain("DO UPDATE SET \"id\" = EXCLUDED.\"id\"");
    }

    [Fact]
    public void BuildUpsert_WithEmptyKeyColumns_ShouldThrow()
    {
        // Arrange
        var columns = new[] { "id", "email" };
        var parameters = new[] { "@p0", "@p1" };
        var keyColumns = Array.Empty<string>();

        // Act & Assert
        var act = () => DmlBuilder.BuildUpsert("t_users", columns, parameters, keyColumns);
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region BuildIdWhereClause Tests

    [Fact]
    public void BuildIdWhereClause_ShouldGenerateValidClause()
    {
        // Act
        var clause = DmlBuilder.BuildIdWhereClause("id");

        // Assert
        clause.Should().Be("\"id\" = @id");
    }

    [Fact]
    public void BuildIdWhereClause_WithCustomParameter_ShouldGenerateValidClause()
    {
        // Act
        var clause = DmlBuilder.BuildIdWhereClause("user_id", "@userId");

        // Assert
        clause.Should().Be("\"user_id\" = @userId");
    }

    #endregion

    #region QuoteIdentifier Tests

    [Fact]
    public void QuoteIdentifier_ShouldWrapInDoubleQuotes()
    {
        // Act
        var result = DmlBuilder.QuoteIdentifier("column_name");

        // Assert
        result.Should().Be("\"column_name\"");
    }

    [Fact]
    public void QuoteIdentifier_WithDoubleQuotes_ShouldEscape()
    {
        // Act
        var result = DmlBuilder.QuoteIdentifier("column\"name");

        // Assert
        result.Should().Be("\"column\"\"name\"");
    }

    #endregion
}
