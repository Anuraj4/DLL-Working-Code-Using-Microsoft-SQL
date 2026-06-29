using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Edi.Generator835.Configuration;
using Edi.Generator835.Context;
using Xalta.Edi.BalancingValidation.Core;

namespace Edi.Generator835.Pipeline
{
    public class PipelineResult
    {
        public bool Success { get; set; }
        public string InputFilePath { get; set; } = string.Empty;
        public string OutputFilePath { get; set; } = string.Empty;
        public EdiValidationResult? ValidationResult { get; set; }
        public List<ValidationError> ValidationErrors => ValidationResult?.FlattenedErrors ?? new List<ValidationError>();
        public TimeSpan ExecutionTime { get; set; }
        public string Statistics { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
    }

    public interface IEdi835Pipeline
    {
        Task<PipelineResult> Execute(string inputExcelPath, string outputDirectory, MappingConfiguration mappings, GenerationContext context, bool enableAppsmith = false);
    }
}
