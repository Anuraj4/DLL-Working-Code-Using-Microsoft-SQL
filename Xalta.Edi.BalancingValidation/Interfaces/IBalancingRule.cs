using System.Collections.Generic;
using Xalta.Edi.BalancingValidation.Core;

namespace Xalta.Edi.BalancingValidation.Interfaces
{
    public interface IBalancingRule<T>
    {
        string RuleName { get; }
        EdiValidationResult Validate(T context);
    }
}
