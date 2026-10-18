using System.Text.RegularExpressions;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Unit;

/// <summary>
/// <c>ValidationException(field, message)</c> names the request field that failed and always answers
/// <c>VALIDATION_ERROR</c>. Three call sites passed an error code in the field's place
/// (<c>"SYSTEM_COLUMN"</c>, <c>"UNSAFE_TYPE_CAST"</c>, <c>"INVALID_OPERATION"</c>): the code the API
/// reference documents never reached the wire, and the message read "Validation error for
/// 'SYSTEM_COLUMN'". A code is a <c>SchemaException</c>'s first argument. The docs parity gate could not
/// see this — it compares two written lists, and both named the code.
/// </summary>
public partial class ValidationExceptionFieldTests
{
    [Fact]
    public void No_validation_exception_is_given_an_error_code_as_its_field()
    {
        // src/MorphDB.Core/Exceptions/MorphDbException.cs -> src
        var anchor = ConstraintBoundaryDoc.RepoFilePath("src/MorphDB.Core/Exceptions/MorphDbException.cs");
        var src = Directory.GetParent(anchor)!.Parent!.Parent!.FullName;

        var misuses = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(src, path)
                .Split(Path.DirectorySeparatorChar)
                .Any(segment => segment is "bin" or "obj"))
            .SelectMany(path => CodeAsField().Matches(File.ReadAllText(path))
                .Select(match => $"{Path.GetRelativePath(src, path)}: {match.Value}"))
            .ToList();

        misuses.Should().BeEmpty("an error code goes to SchemaException(code, message); ValidationException's first argument is a field name");
    }

    // new ValidationException("UPPER_SNAKE", ... — a field is a request member name, never shouted.
    [GeneratedRegex("""new\s+ValidationException\(\s*"[A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+"\s*,""")]
    private static partial Regex CodeAsField();
}
