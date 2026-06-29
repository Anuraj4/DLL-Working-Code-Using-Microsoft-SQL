namespace Xalta.Edi.CodeCrossWalk.Interfaces
{
    public interface ICodeCrossWalkService
    {
        /// <summary>
        /// Looks up a code from a specific table.
        /// </summary>
        /// <param name="tableName">Name of the lookup table.</param>
        /// <param name="input">The input value to map.</param>
        /// <returns>The mapped code, or the input itself (or null/empty) if not found, based on implementation.</returns>
        string Lookup(string tableName, string input);

        /// <summary>
        /// Attempts to lookup a code, returns false if not found.
        /// </summary>
        bool TryLookup(string tableName, string input, out string output);
    }
}
