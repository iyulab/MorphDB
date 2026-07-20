namespace MorphDB.Core.Abstractions;

/// <summary>
/// Manages PostgreSQL notification triggers for real-time change notifications.
/// </summary>
public interface ITableNotificationTriggerManager
{
    /// <summary>
    /// Creates a notification trigger for a table in a project.
    /// </summary>
    /// <param name="projectId">The project (project) ID.</param>
    /// <param name="physicalTableName">The physical table name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CreateTriggerAsync(Guid projectId, string physicalTableName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the notification trigger from a table in a project.
    /// </summary>
    /// <param name="projectId">The project (project) ID.</param>
    /// <param name="physicalTableName">The physical table name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveTriggerAsync(Guid projectId, string physicalTableName, CancellationToken cancellationToken = default);
}
