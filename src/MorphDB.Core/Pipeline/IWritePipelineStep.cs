namespace MorphDB.Core.Pipeline;

/// <summary>
/// Base interface for all pipeline steps.
/// </summary>
public interface IWritePipelineStep
{
    /// <summary>
    /// Order in which this step should execute (lower = earlier).
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Whether this step should execute for the given context.
    /// </summary>
    bool ShouldExecute(IWriteContext context);

    /// <summary>
    /// Executes the pipeline step.
    /// </summary>
    Task ExecuteAsync(IWriteContext context);
}

/// <summary>
/// A validation step in the write pipeline.
/// Validates data before writing to the database.
/// </summary>
public interface IValidator : IWritePipelineStep
{
}

/// <summary>
/// A transformation step in the write pipeline.
/// Transforms data before writing to the database.
/// </summary>
public interface ITransformer : IWritePipelineStep
{
}

/// <summary>
/// Well-known pipeline step orders.
/// </summary>
public static class PipelineOrder
{
    // Transformers (run first to prepare data)
    public const int IdApplier = 50;           // UUID v7 generation (first)
    public const int DefaultValueApplier = 100;
    public const int TimestampApplier = 200;
    public const int VersionApplier = 300;
    public const int AuditFieldApplier = 400;
    public const int OwnerApplier = 450;       // _owner_id from SecurityContext
    public const int SortOrderApplier = 475;   // _sort_order auto-generation
    public const int ComputedFieldApplier = 500;

    // Validators (run after transformers)
    public const int TypeValidator = 1000;
    public const int RequiredValidator = 1100;
    public const int UniqueValidator = 1200;
    public const int ForeignKeyValidator = 1300;
    public const int CheckValidator = 1400;
    public const int CustomValidator = 1500;
}
