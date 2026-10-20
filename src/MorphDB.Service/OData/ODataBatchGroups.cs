namespace MorphDB.Service.OData;

/// <summary>
/// The shape rules of a JSON batch's atomicity groups and request dependencies (OData JSON Format 4.01
/// §19): what makes a batch malformed as a whole, how its requests fall into runs, and when a dependent
/// request may not run.
/// </summary>
internal static class ODataBatchGroups
{
    /// <summary>
    /// Why the batch is malformed as a whole, or null: a request without an id, an id given twice, a group
    /// named like a request, or a group whose requests are not adjacent.
    /// </summary>
    public static string? Problem(IReadOnlyList<ODataBatchRequestItem> requests)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var request in requests)
        {
            if (string.IsNullOrEmpty(request.Id))
            {
                return "Every request of a batch needs an id.";
            }

            if (!ids.Add(request.Id))
            {
                return $"The id '{request.Id}' is given to more than one request of the batch.";
            }
        }

        var closed = new HashSet<string>(StringComparer.Ordinal);
        string? open = null;
        foreach (var request in requests)
        {
            var group = request.AtomicityGroup;
            if (group is not null && ids.Contains(group))
            {
                return $"The atomicity group '{group}' has the id of a request; a group id must differ from every request id.";
            }

            if (group != open)
            {
                if (open is not null)
                {
                    closed.Add(open);
                }

                if (group is not null && closed.Contains(group))
                {
                    return $"The requests of atomicity group '{group}' are not adjacent; a group's requests must follow one another.";
                }

                open = group;
            }
        }

        return null;
    }

    /// <summary>The batch as runs: each request outside a group on its own, each group as one run.</summary>
    public static IEnumerable<(string? Group, IReadOnlyList<ODataBatchRequestItem> Requests)> Segments(IReadOnlyList<ODataBatchRequestItem> requests)
    {
        var i = 0;
        while (i < requests.Count)
        {
            var group = requests[i].AtomicityGroup;
            if (group is null)
            {
                yield return (null, [requests[i]]);
                i++;
                continue;
            }

            var run = new List<ODataBatchRequestItem>();
            while (i < requests.Count && requests[i].AtomicityGroup == group)
            {
                run.Add(requests[i]);
                i++;
            }

            yield return (group, run);
        }
    }

    /// <summary>
    /// What earlier requests and groups came to, for the requests that depend on them. A dependency names an
    /// earlier request or an earlier group; a request of another group is reached only through its group.
    /// </summary>
    public sealed class Dependencies(IReadOnlyList<ODataBatchRequestItem> requests)
    {
        private readonly Dictionary<string, string?> _groupOf = requests.ToDictionary(r => r.Id, r => r.AtomicityGroup, StringComparer.Ordinal);
        private readonly HashSet<string> _done = new(StringComparer.Ordinal);
        private readonly HashSet<string> _failed = new(StringComparer.Ordinal);

        /// <summary>Records how a request or a group ended.</summary>
        public void Record(string id, int status)
        {
            _done.Add(id);
            if (status >= StatusCodes.Status400BadRequest)
            {
                _failed.Add(id);
            }
        }

        /// <summary>The answer a request gets instead of running, or null when it may run.</summary>
        public ODataBatchResponseItem? Refusal(ODataBatchRequestItem request)
        {
            foreach (var dependency in request.DependsOn ?? [])
            {
                if (!_done.Contains(dependency))
                {
                    return Refuse(request, StatusCodes.Status400BadRequest, "INVALID_ARGUMENT",
                        $"Request '{request.Id}' depends on '{dependency}', which is not an earlier request or atomicity group of this batch.");
                }

                if (_groupOf.TryGetValue(dependency, out var group) && group is not null && group != request.AtomicityGroup)
                {
                    return Refuse(request, StatusCodes.Status400BadRequest, "INVALID_ARGUMENT",
                        $"Request '{request.Id}' depends on '{dependency}', a request of atomicity group '{group}'; depend on the group instead.");
                }
            }

            return (request.DependsOn ?? []).FirstOrDefault(_failed.Contains) is { } failed
                ? Refuse(request, StatusCodes.Status424FailedDependency, "FAILED_DEPENDENCY", $"Request '{request.Id}' was not run: '{failed}', which it depends on, failed.")
                : null;
        }

        private static ODataBatchResponseItem Refuse(ODataBatchRequestItem request, int status, string code, string message) =>
            new() { Id = request.Id, Status = status, Body = ODataError.Of(code, message) };
    }
}
