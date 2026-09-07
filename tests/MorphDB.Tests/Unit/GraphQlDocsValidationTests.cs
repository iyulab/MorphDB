using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Language;
using HotChocolate.Validation;
using Microsoft.Extensions.DependencyInjection;
using MorphDB.Service.GraphQL;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Unit;

/// <summary>
/// The execution half of GraphQL docs parity. Its neighbour compares descriptions — the field and
/// argument names an example uses against the ones the schema declares — and a name-level comparison
/// answers a narrower question than the one a reader has, which is whether the operation they copied
/// is one this server accepts.
/// <para>
/// The two come apart wherever acceptance depends on something other than a name: a required
/// argument left out, a value of the wrong kind, a leaf field given a selection set, a subscription
/// with two root fields, a fragment on a type that cannot appear there. Every one of those is a
/// document whose names all exist. The realtime example that was refused for two months while the
/// name-level gate stayed green was this shape, on the other transport.
/// </para>
/// <para>
/// So this runs the server's own validator — the same phase a request goes through before any
/// resolver is called — over every fenced example. Validation rather than execution is deliberate
/// and is the same judgment its HTTP counterpart makes: an example names tables this database does
/// not have, so requiring it to succeed would test the fixture rather than the document. It also
/// keeps mutations and subscriptions in scope, which executing them could not — a documented
/// mutation sent to a live server writes, and a subscription does not answer over this transport
/// at all.
/// </para>
/// </summary>
public class GraphQlDocsValidationTests
{
    [Fact]
    public async Task Every_documented_operation_is_accepted_by_the_servers_own_validator()
    {
        var (schema, validator) = await ValidatorAsync();

        foreach (var (label, document) in GraphQlDocs.Blocks())
        {
            var result = validator.Validate(schema, document);

            result.HasErrors.Should().BeFalse(
                $"{label} is an operation the reference teaches, and a reader who sends it gets it "
                + "refused before a resolver runs: "
                + string.Join(" | ", result.Errors.Select(e => e.Message)));
        }
    }

    /// <summary>
    /// Proves the validator above is wired with rules, rather than accepting everything. A gate
    /// built on a validator that validates nothing passes for the wrong reason, and passing for the
    /// wrong reason is indistinguishable from working until the day it matters.
    /// </summary>
    [Fact]
    public async Task The_validator_refuses_an_operation_the_schema_does_not_offer()
    {
        var (schema, validator) = await ValidatorAsync();

        var result = validator.Validate(
            schema,
            Utf8GraphQLParser.Parse("query { thisFieldDoesNotExistOnTheQueryType }"));

        result.HasErrors.Should().BeTrue(
            "a field no type declares must be reported — if it is not, the validator carries no "
            + "rules and the gate above is vacuous");
    }

    private static async Task<(ISchemaDefinition Schema, DocumentValidator Validator)> ValidatorAsync()
    {
        var services = new ServiceCollection()
            .AddGraphQLServer()
            .AddMorphDbTypes()
            .Services
            .BuildServiceProvider();

        var executor = await services.GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync();

        // The executor's own validator, not one built here: a validator assembled in a test carries
        // whatever rules the test remembered, and this gate is only worth anything if the rules are
        // the ones a request actually goes through. The falsification test below is what holds that.
        return (executor.Schema, executor.Schema.Services.GetRequiredService<DocumentValidator>());
    }
}
