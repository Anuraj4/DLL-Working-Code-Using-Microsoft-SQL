using System;
using System.Collections.Generic;
using System.Linq;
using Xalta.Edi.BalancingValidation.Interfaces;

namespace Xalta.Edi.BalancingValidation.Core
{
    public class BalancingValidator<T> : IBalancingValidator<T>
    {
        private readonly List<IBalancingRule<T>> _rules;

        public BalancingValidator()
        {
            _rules = new List<IBalancingRule<T>>();
        }

        public void AddRule(IBalancingRule<T> rule)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            _rules.Add(rule);
        }

        public EdiValidationResult Validate(T context)
        {
            var allErrors = new List<EdiSegmentError>();

            foreach (var rule in _rules)
            {
                var ruleResult = rule.Validate(context);
                if (!ruleResult.IsValid && ruleResult.Errors != null)
                {
                    allErrors.AddRange(ruleResult.Errors);
                }
            }

            return new EdiValidationResult(!allErrors.Any(), allErrors);
        }
    }
}
