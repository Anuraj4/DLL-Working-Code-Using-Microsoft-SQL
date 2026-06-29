using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EdiFabric.Core.Model.Edi;
using EdiFabric.Core.Model.Edi.X12;
using EdiFabric.Framework.Readers;
using EdiFabric.Templates.Hipaa5010;
using Xalta.Edi.BalancingValidation;
using Xalta.Edi.BalancingValidation.Core;
using Xalta.Edi.BalancingValidation.Interfaces;
using Xalta.Edi.BalancingValidation.Rules.Edi835;
using Xalta.Edi.BalancingValidation.Rules.Generic;
using Xalta.Edi.BalancingValidation.Validators;

namespace System.Runtime.CompilerServices
{
    // Required to use records with net48 target framework in this assembly
    internal static class IsExternalInit { }
}

namespace Edi.Generator835.Services
{
    public class EdiValidationService
    {
        public EdiValidationResult ValidateEdiFile(string filePath, int snipLevel = 4)
        {
            if (!File.Exists(filePath))
                return new EdiValidationResult(false, new List<EdiSegmentError> { new EdiSegmentError("SYSTEM", 0, "N/A", new List<EdiElementError> { new EdiElementError("FILE", -1, "ERR", "System", "FileNotFound", $"File not found: {filePath}", null) }) });

            using var stream = File.OpenRead(filePath);
            return ValidateEdiStream(stream, snipLevel);
        }

        public EdiValidationResult ValidateEdiString(string ediContent, int snipLevel = 4)
        {
            if (string.IsNullOrWhiteSpace(ediContent))
                return new EdiValidationResult(true, new List<EdiSegmentError>());

            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(ediContent));
            return ValidateEdiStream(stream, snipLevel);
        }

        public EdiValidationResult ValidateEdiStream(Stream stream, int snipLevel = 4)
        {
            var errors = new List<EdiSegmentError>();
            try
            {
                var reader = new X12Reader(stream, "EdiFabric.Templates.Hipaa");
                while (reader.Read())
                {
                    if (reader.Item is TS835 ts)
                    {
                        var res = ValidateTransaction(ts, snipLevel);
                        errors.AddRange(res.Errors);
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add(new EdiSegmentError("SYSTEM", 0, "N/A", new List<EdiElementError> { new EdiElementError("SYS", -1, "ERR", "System", "PARSER_001", $"Critical Parsing Error: {ex.Message}", null) }));
            }
            return new EdiValidationResult(errors.Count == 0, errors);
        }

        public EdiValidationResult ValidateTransaction(TS835 transaction, int snipLevel = 4)
        {
            // 1. Determine the highest required structural validation level
            var structuralLevel = ValidationLevel.SyntaxOnly_SNIP1;
            if (snipLevel >= 4) structuralLevel = ValidationLevel.InterSegment_SNIP4;
            else if (snipLevel >= 2) structuralLevel = ValidationLevel.LimitsAndCodes_SNIP2;

            // 2. Create a single validator and add the highest structural rule
            var validator = new BalancingValidator<TS835>();
            var settings = new ValidationSettings { ValidationLevel = structuralLevel };
            validator.AddRule(new StructureValidationRule<TS835>(settings));

            // 3. Add custom balancing rules if SNIP 3 or higher is requested
            if (snipLevel >= 3)
            {
                validator.AddRule(new TransactionBalanceRule());
                validator.AddRule(new ClpSegmentBalanceRule());
                validator.AddRule(new SvcSegmentBalanceRule());
            }

            // 4. Perform validation in a single pass
            return validator.Validate(transaction);
        }
    }
}
