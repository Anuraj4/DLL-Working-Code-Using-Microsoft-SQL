using Edi.Generator835.Configuration;
using Edi.Generator835.Models;

namespace Edi.Generator835.Services.Interfaces
{
    /// <summary>
    /// Service for normalizing raw EOB data before EDI generation.
    /// Handles cleaning of dates, addresses, names, and general text.
    /// </summary>
    public interface IDataNormalizer
    {
        void Normalize(Edi835DataModel model, MappingConfiguration mappings);
        void SynchronizeContext(Edi835DataModel model);
    }
}
