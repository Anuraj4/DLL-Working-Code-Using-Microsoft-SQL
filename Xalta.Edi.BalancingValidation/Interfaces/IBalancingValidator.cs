using System.Collections.Generic;
using Xalta.Edi.BalancingValidation.Core;

namespace Xalta.Edi.BalancingValidation.Interfaces
{
    public interface IBalancingValidator<T>
    {
        void AddRule(IBalancingRule<T> rule);
        EdiValidationResult Validate(T context);
    }
}
