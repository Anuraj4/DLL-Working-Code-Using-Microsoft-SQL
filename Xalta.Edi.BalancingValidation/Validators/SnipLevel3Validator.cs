using System;
using System.Collections.Generic;
using EdiFabric.Templates.Hipaa5010;
using Xalta.Edi.BalancingValidation.Core;
using Xalta.Edi.BalancingValidation.Interfaces;
using Xalta.Edi.BalancingValidation.Rules.Edi835;
using Xalta.Edi.BalancingValidation.Rules.Generic;

namespace Xalta.Edi.BalancingValidation.Validators
{
    /// <summary>
    /// Validator specifically for HIPAA SNIP Level 3 (Balancing) compliance.
    /// Implementation based on: https://support.edifabric.com/hc/en-us/articles/360000361352-How-to-validate-HIPAA-SNIP-levels
    /// </summary>
    public class SnipLevel3Validator : BalancingValidator<TS835>
    {
        public SnipLevel3Validator()
        {
            // Structural Validation (SNIP 1 & 2)
            // AddRule(new StructureValidationRule<TS835>());

            // 835 Balancing Rules

            // Transaction Balancing:
            // Sum 2100 CLP04 - Sum PLB (PLB04 + PLB06 + PLB08 + PLB10 + PLB12 + PLB14) = BPR02
            AddRule(new TransactionBalanceRule());

            // Claim Payment Balancing:
            // CLP03 - (Sum 2100 CAS (CAS03 + CAS06 + CAS09 + CAS12 + CAS15 + CAS18) + Sum 2110 CAS (CAS03 + CAS06 + CAS09 + CAS12 + CAS15 + CAS18)) = CLP04
            AddRule(new ClpSegmentBalanceRule());

            // Service Payment Balancing:
            // SVC02 - Sum 2110 CAS (CAS03 + CAS06 + CAS09 + CAS12 + CAS15 + CAS18) = SVC03
            AddRule(new SvcSegmentBalanceRule());
        }
    }
}
