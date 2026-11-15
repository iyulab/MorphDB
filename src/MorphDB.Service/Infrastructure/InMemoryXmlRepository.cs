using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace MorphDB.Service.Infrastructure;

/// <summary>
/// Holds Data Protection keys in memory for the life of the process. The service protects no data with them
/// (see <c>Program.cs</c>), and the framework builds its key ring at start-up either way — with the default
/// repository that is a key file under the user profile and a warning, on every start of a container, that the
/// keys may not outlive it. This repository writes nothing anywhere.
/// </summary>
internal sealed class InMemoryXmlRepository : IXmlRepository
{
    private readonly object _gate = new();
    private readonly List<XElement> _elements = [];

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        lock (_gate)
        {
            return [.. _elements];
        }
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        lock (_gate)
        {
            _elements.Add(element);
        }
    }
}
