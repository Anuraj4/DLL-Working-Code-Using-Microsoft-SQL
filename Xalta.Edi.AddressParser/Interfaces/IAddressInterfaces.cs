using System.Collections.Generic;
using System.Threading.Tasks;
using Xalta.Edi.AddressParser.Models;

namespace Xalta.Edi.AddressParser.Interfaces
{
    public interface IAddressParser
    {
        ParsedAddress Parse(string rawAddress);
    }

    public interface IAddressDataProvider
    {
        Task<IEnumerable<StateData>> GetStatesAsync();
        Task<IEnumerable<string>> GetCitiesAsync(string state);
    }
}
