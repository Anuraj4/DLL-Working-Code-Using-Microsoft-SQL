using System;
using EdiFabric.Core.Model.Edi;
using EdiFabric.Templates.Hipaa5010;
using Xalta.Edi.BalancingValidation.Core;
using Xalta.Edi.BalancingValidation.Interfaces;
using Xalta.Edi.BalancingValidation.Rules.Generic;
using Xalta.Edi.BalancingValidation.Validators;

namespace Xalta.Edi.BalancingValidation
{
    public static class EdiValidatorFactory
    {
        /// <summary>
        /// Creates a validator for SNIP Level 1 (Integrity).
        /// Validates basic EDI syntax and structure.
        /// </summary>
        public static IBalancingValidator<TS835> CreateSnip1()
        {
            var validator = new BalancingValidator<TS835>();
            var settings = new ValidationSettings { ValidationLevel = ValidationLevel.SyntaxOnly_SNIP1 };
            validator.AddRule(new StructureValidationRule<TS835>(settings));
            return validator;
        }

        /// <summary>
        /// Creates a validator for SNIP Level 2 (Requirement).
        /// Validates requirements, data types, and limits.
        /// </summary>
        public static IBalancingValidator<TS835> CreateSnip2()
        {
            var validator = new BalancingValidator<TS835>();
            var settings = new ValidationSettings { ValidationLevel = ValidationLevel.LimitsAndCodes_SNIP2 };
            validator.AddRule(new StructureValidationRule<TS835>(settings));
            return validator;
        }

        /// <summary>
        /// Creates a validator for SNIP Level 3 (Balancing).
        /// Includes SNIP 1 and 2 structural checks AND custom mathematical balancing rules.
        /// </summary>
        public static IBalancingValidator<TS835> CreateSnip3()
        {
            var validator = new SnipLevel3Validator();
            // SnipLevel3Validator constructor already adds StructureValidationRule (defaulting to SNIP 2) 
            // and the custom balancing rules.
            // If stricter structure check is needed inside, SnipLevel3Validator can be updated to accept settings.
            return validator;
        }


        public static IBalancingValidator<TS835> CreateSnip4()
        {
            var validator = new BalancingValidator<TS835>();
            var settings = new ValidationSettings { ValidationLevel = ValidationLevel.InterSegment_SNIP4 };
            validator.AddRule(new StructureValidationRule<TS835>(settings));
            return validator;
        }

        /// <summary>
        /// Creates a custom validator with specified settings.
        /// </summary>
        public static IBalancingValidator<TS835> CreateCustom(ValidationSettings settings)
        {
            var validator = new BalancingValidator<TS835>();
            validator.AddRule(new StructureValidationRule<TS835>(settings));
            return validator;
        }
    }
}
