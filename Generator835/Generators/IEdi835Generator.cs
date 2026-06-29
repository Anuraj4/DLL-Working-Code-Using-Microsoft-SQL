using EdiFabric.Core.Model.Edi.X12;
using EdiFabric.Templates.Hipaa5010;
using Edi.Generator835.Models;

namespace Edi.Generator835.Generators
{
    public interface IEdi835Generator
    {
        TS835 Generate(Edi835DataModel data);
    }
}
