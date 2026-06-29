using System;
using EdiFabric.Core.Model.Edi;
using Edi.Generator835.Configuration;
using Edi.Generator835.Context;
using Edi.Generator835.Pipeline;
using Xalta.Edi.BalancingValidation.Core;
using Newtonsoft.Json;

namespace Edi.Generator835.Integration
{
    /// <summary>
    /// Entry point for Automation Anywhere (A360) integration.
    /// Exposes a simple API that returns a JSON string result.
    /// </summary>
    public class A360Generator
    {
        /// <summary>
        /// Sets the EdiFabric license key. Must be called before generation.
        /// </summary>
        public string SetLicense(string key)
        {
            try
            {
                EdiFabric.SerialKey.Set(key, true);
                return JsonConvert.SerializeObject(new { success = true, message = "License set successfully" });
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Generates an EDI 835 file from the given Excel input.
        /// Orchestrates the full pipeline: Config -> Read -> Rules -> Generate -> Validate.
        /// </summary>
        /// <param name="inputExcelPath">Full path to Eob_Data.xlsx</param>
        /// <param name="outputDirectory">Directory to write the .edi file</param>
        /// <param name="configPath">Full path to the Excel configuration file</param>
        /// <returns>JSON string with PipelineResult (Success, OutputFilePath, ValidationResult)</returns>
        public string GenerateEdi835(string inputExcelPath, string outputDirectory, string configPath)
        {
            try
            {
                var mappingProvider = new ExcelMappingProvider();
                var mappings = mappingProvider.LoadMappings(configPath);

                int startControl = int.Parse(mappings.GetSetting("Interchange_StartControlNumber", "1"));
                var controlProvider = new SequentialControlNumberProvider(startControl);
                var context = new GenerationContext(controlProvider);

                var pipeline = new Edi835Pipeline(mappingProvider);
                var result = pipeline.Execute(inputExcelPath, outputDirectory, mappings, context).GetAwaiter().GetResult();

                return JsonConvert.SerializeObject(result, Formatting.Indented);
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new PipelineResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ValidationResult = new EdiValidationResult(false, new System.Collections.Generic.List<EdiSegmentError>
                    {
                        new EdiSegmentError("SYS", -1, "N/A", new System.Collections.Generic.List<EdiElementError> {
                            new EdiElementError("SYS", -1, "ERR", "System Error", "Exception", $"Critical Failure: {ex.Message}", null, ex.StackTrace)
                        })
                    }, ValidatedAt: DateTime.UtcNow)
                }, Formatting.Indented);
            }
        }
    }
}
