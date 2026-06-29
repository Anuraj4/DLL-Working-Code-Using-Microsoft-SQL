namespace Edi.Generator835.Configuration
{
    /// <summary>
    /// Abstraction for loading mapping configurations from any source.
    /// </summary>
    public interface IMappingProvider
    {
        /// <summary>
        /// Load all mappings from the given configuration source (file or directory).
        /// </summary>
        MappingConfiguration LoadMappings(string configPath);
    }
}
