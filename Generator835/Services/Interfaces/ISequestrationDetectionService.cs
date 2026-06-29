using Edi.Generator835.Configuration;
using Edi.Generator835.Models;

namespace Edi.Generator835.Services.Interfaces
{
    public interface ISequestrationDetectionService
    {
        void ProcessSequestration(Edi835DataModel model, MappingConfiguration mappings);
        decimal? CalculateSequestration(decimal paid);
    }
}
