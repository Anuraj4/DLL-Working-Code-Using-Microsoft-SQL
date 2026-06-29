using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Edi.Generator835.Configuration;
using Edi.Generator835.Pipeline;
using Edi.Generator835.Services;
using Xalta.Edi.AddressParser.Parsers;
using Xalta.Edi.AddressParser.Services;
using Xalta.Edi.AddressParser.Providers;
using Serilog;
using Newtonsoft.Json;

namespace Edi.Generator835
{
    public class EnterpriseGenerator
    {
        private readonly string _configPath;
        private readonly string _templatePath;
        private readonly string _outputPath;
        private readonly string? _sqlServerName;
        private readonly string? _sqlDatabaseName;

        private const string EdiFabricSerialKey = "c417cb9dd9d54297a55c032a74c87996";

        static EnterpriseGenerator()
        {
            // EdiFabric requires its license token to be set before any X12Writer operations
            Xalta.Edi.LicenseProvider.EdiLicense.Initialize();
        }

        public EnterpriseGenerator(string configPath, string templatePath, string outputPath)
        {
            _configPath = configPath;
            _templatePath = templatePath;
            _outputPath = outputPath;
        }

        /// <summary>
        /// Constructor that loads configuration from SQL Server instead of Excel file.
        /// </summary>
        public EnterpriseGenerator(string serverName, string databaseName, string templatePath, string outputPath)
        {
            _sqlServerName = serverName;
            _sqlDatabaseName = databaseName;
            _configPath = string.Empty;
            _templatePath = templatePath;
            _outputPath = outputPath;
        }

        public async Task<List<PipelineResult>> RunAsync(string inputCsvPath, bool enableParallelProcessing = true, bool enableAppsmith = false)
        {
            Log.Information("Starting Enterprise Generator workflow.");
            var results = new List<PipelineResult>();

            // 1. Load Configuration
            MappingConfiguration config;
            if (_sqlServerName != null && _sqlDatabaseName != null)
            {
                // Load from SQL Server
                var sqlProvider = new SqlMappingProvider(_sqlServerName, _sqlDatabaseName);
                config = sqlProvider.LoadMappingsFromSql(BuildSqlConnectionString());
                Log.Information("Configuration loaded from SQL Server: {Server}, Database: {Database}", _sqlServerName, _sqlDatabaseName);
            }
            else
            {
                // Load from Excel file (existing behavior)
                var configProvider = new ExcelMappingProvider();
                config = configProvider.LoadMappings(_configPath);
            }

            // 2. Initialize Address Parser
            using (var httpClient = new System.Net.Http.HttpClient())
            {
                var apiProvider = new CountriesNowApiProvider(httpClient);
                var excelAddrProvider = new ExcelAddressDataProvider(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AddressData.xlsx"));
                var addrDataService = new AddressDataService(apiProvider, excelAddrProvider);
                await addrDataService.InitializeAsync();
                var addressParser = new UsAddressParser(addrDataService);

                // 3. Initialize Services
                var mapper = new CsvToExcelMapper(config, addressParser);
                var templateService = new ExcelTemplateService(_templatePath, _outputPath);

                // Construct pipeline
                IMappingProvider pipelineProvider;
                if (_sqlServerName != null && _sqlDatabaseName != null)
                {
                    pipelineProvider = new SqlMappingProvider(_sqlServerName, _sqlDatabaseName);
                }
                else
                {
                    pipelineProvider = new ExcelMappingProvider();
                }
                var pipeline = new Edi835Pipeline(pipelineProvider);

                var orchestrator = new ParallelProcessingOrchestrator(mapper, templateService, pipeline, config);

                // 4. Run Parallel Orchestration
                results = await orchestrator.ProcessFolderAsync(inputCsvPath, enableParallelProcessing, enableAppsmith: enableAppsmith);
            }

            Log.Information("Enterprise Generator workflow completed.");
            return results;
        }

        private string BuildSqlConnectionString()
        {
            return $@"Server={_sqlServerName};
                        Database={_sqlDatabaseName};
                        Trusted_Connection=True;
                        TrustServerCertificate=True;";
        }
    }
}
