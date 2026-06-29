using System;
using System.Collections.Generic;
using System.Linq;
using EdiFabric.Core.Model.Edi;
using EdiFabric.Templates.Hipaa5010;
using Xalta.Edi.BalancingValidation.Core;
using Xalta.Edi.BalancingValidation.Interfaces;

namespace Xalta.Edi.BalancingValidation.Rules.Generic
{
    public class StructureValidationRule<T> : IBalancingRule<T> where T : EdiMessage
    {
        private readonly ValidationSettings _validationSettings;
        public string RuleName => $"Structural Validation (Level: {_validationSettings?.ValidationLevel.ToString() ?? "Default"})";

        public StructureValidationRule()
        {
            _validationSettings = new ValidationSettings { ValidationLevel = ValidationLevel.LimitsAndCodes_SNIP2 };
        }

        public StructureValidationRule(ValidationSettings validationSettings)
        {
            _validationSettings = validationSettings ?? new ValidationSettings { ValidationLevel = ValidationLevel.LimitsAndCodes_SNIP2 };
        }

        public EdiValidationResult Validate(T transaction)
        {
            if (transaction is TS835 ts835)
            {
                return Edi835Validator.Validate(ts835, _validationSettings);
            }

            return new EdiValidationResult(true, new List<EdiSegmentError>());
        }
    }
}
