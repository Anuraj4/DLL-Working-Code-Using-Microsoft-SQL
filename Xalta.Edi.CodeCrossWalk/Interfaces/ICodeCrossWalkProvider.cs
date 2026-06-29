using System.Collections.Generic;

namespace Xalta.Edi.CodeCrossWalk.Interfaces
{
    public interface ICodeCrossWalkProvider
    {
        /// <summary>
        /// Loads all mapping data from the source.
        /// Returns a dictionary where key is TableName, and value is another dictionary of Input -> Output.
        /// </summary>
        Dictionary<string, Dictionary<string, string>> LoadMappings();
    }
}
