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
    /// earlier request or an earlier group; a request of another group may be named only beside its group.
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

                if (_groupOf.TryGetValue(dependency, out var group) && group is not null && group != request.AtomicityGroup
                    && !(request.DependsOn ?? []).Contains(group, StringComparer.Ordinal))
                {
                    return Refuse(request, StatusCodes.Status400BadRequest, "INVALID_ARGUMENT",
                        $"Request '{request.Id}' depends on '{dependency}', a request of atomicity group '{group}'; name the group in dependsOn as well.");
                }
            }

            return (request.DependsOn ?? []).FirstOrDefault(_failed.Contains) is { } failed
                ? Refuse(request, StatusCodes.Status424FailedDependency, "FAILED_DEPENDENCY", $"Request '{request.Id}' was not run: '{failed}', which it depends on, failed.")
                : null;
        }

        private static ODataBatchResponseItem Refuse(ODataBatchRequestItem request, int status, string code, string message) =>
            new() { Id = request.Id, Status = status, Body = ODataError.Of(code, message) };
    }

    /// <summary>
    /// The entities earlier requests created or read, by request id, so a later request's URL can name one
    /// as <c>$&lt;id&gt;</c> — the request that made it must be among its <c>dependsOn</c>.
    /// </summary>
    public sealed class References
    {
        private readonly Dictionary<string, string> _entityUrls = new(StringComparer.Ordinal);

        /// <summary>Records the entity a request created (its <c>location</c>) or read by key.</summary>
        public void Record(ODataBatchRequestItem request, ODataBatchResponseItem response)
        {
            if (response.Status >= StatusCodes.Status400BadRequest)
            {
                return;
            }

            if (response.Headers is { } headers && headers.TryGetValue("location", out var location))
            {
                _entityUrls[request.Id] = location;
            }
            else if (string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase)
                && ODataBatchUrl.TryParse(request.Url, out var target, out _) && target.Key is { } key)
            {
                _entityUrls[request.Id] = $"/odata/{target.EntitySet}({key})";
            }
        }

        /// <summary>
        /// The request's URL with a leading <c>$&lt;id&gt;</c> replaced by that request's entity, or the
        /// reason it cannot be: the id is not a dependency of the request, or its request named no entity.
        /// </summary>
        public string? Resolve(ODataBatchRequestItem request, out string? problem)
        {
            problem = null;
            var url = request.Url?.TrimStart('/') ?? "";
            if (!url.StartsWith('$'))
            {
                return request.Url;
            }

            var end = url.IndexOfAny(['/', '?']);
            var name = end < 0 ? url[1..] : url[1..end];
            var rest = end < 0 ? "" : url[end..];
            if (!(request.DependsOn ?? []).Contains(name, StringComparer.Ordinal))
            {
                problem = $"Request '{request.Id}' names '${name}' in its url; a request it refers to must be among its dependsOn.";
                return null;
            }

            if (!_entityUrls.TryGetValue(name, out var entity))
            {
                problem = $"Request '{request.Id}' names '${name}' in its url, but request '{name}' created or read no single entity.";
                return null;
            }

            if (rest.StartsWith('/'))
            {
                problem = $"Request '{request.Id}' follows '${name}' with a path; only query options may follow a reference.";
                return null;
            }

            return entity + rest;
        }
    }
}
