using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using EdiFabric.Core.Model.Edi;
using EdiFabric.Core.Model.Edi.ErrorContexts;
using EdiFabric.Templates.Hipaa5010;

namespace System.Runtime.CompilerServices
{
    // Required to use records with net48 target framework
    internal static class IsExternalInit { }
}

namespace Xalta.Edi.BalancingValidation.Core
{
    // Standardized records for solution-wide use
    public record EdiElementError(
        string FieldKey,          // e.g. "CLP07"
        int ElementPosition,      // e.g. 7
        string ElementCode,       // e.g. "127"
        string BusinessName,      // e.g. "Payer Claim Control Number"
        string ErrorType,         // e.g. "RequiredDataElementMissing"
        string Message,           // full message
        string? Value,            // value that caused error, null if missing
        string? ErrorDescription = null // detailed description
    );

    public record EdiSegmentError(
        string SegmentName,    // e.g. "CLP"
        int SegmentPosition,   // Relative 0-based position from ST
        string Loop,           // e.g. "2100"
        List<EdiElementError> ElementErrors
    );

    public record EdiValidationResult(
        bool IsValid,
        List<EdiSegmentError> Errors,
        string TransactionVersion = "5010_835",
        DateTime? ValidatedAt = null
    )
    {
        public int TotalErrorCount => Errors.Sum(s => s.ElementErrors.Count);
        public DateTime ValidatedAtTime => ValidatedAt ?? DateTime.UtcNow;

        /// <summary>Flattened list of errors for UI display and high-level reporting.</summary>
        public List<ValidationError> FlattenedErrors
        {
            get
            {
                var list = new List<ValidationError>();
                foreach (var segErr in Errors)
                {
                    foreach (var elemErr in segErr.ElementErrors)
                    {
                        list.Add(new ValidationError
                        {
                            ErrorCode = elemErr.ErrorType,
                            SegmentName = segErr.SegmentName,
                            SegmentPosition = segErr.SegmentPosition,
                            ElementReference = elemErr.FieldKey,
                            Message = elemErr.Message,
                            ElementPosition = elemErr.ElementPosition,
                            ElementBusinessName = elemErr.BusinessName,
                            ElementCode = elemErr.ElementCode,
                            ElementValue = elemErr.Value,
                            ErrorDescription = string.IsNullOrEmpty(elemErr.ErrorDescription)
                                ? $"Context: {elemErr.BusinessName} = {elemErr.Value ?? "missing"}"
                                : $"{elemErr.ErrorDescription} | Context: {elemErr.BusinessName} = {elemErr.Value ?? "missing"}",
                            ContextInfo = segErr.Loop == "N/A" ? string.Empty : segErr.Loop
                        });
                    }
                }
                return list;
            }
        }
    }

    public static class Edi835Validator
    {
        // Built once, reused on every call
        private static readonly Lazy<Dictionary<(string Seg, int Pos), (string Code, string PropName)>> _map =
            new(() => BuildMap(typeof(TS835), new HashSet<Type>()));

        public static EdiValidationResult Validate(TS835 transaction, ValidationSettings? settings = null)
        {
            if (transaction.IsValid(out MessageErrorContext ctx, settings))
                return new EdiValidationResult(true, new(), ValidatedAt: DateTime.UtcNow);

            var segErrors = ctx.Errors.Select(seg => new EdiSegmentError(
                SegmentName: seg.Name,
                SegmentPosition: seg.Position,
                Loop: seg.LoopId ?? "N/A",
                ElementErrors: seg.Errors.Select(elem =>
                {
                    var (propName, code) = Resolve(seg.Name, elem.Position, elem.Name);
                    string businessName = ToBusinessName(seg.Name, code, propName);
                    string displayValue = EnrichValue(code, elem.Value);

                    // Replace raw code e.g. "127" → "Payer Claim Control Number (127)"
                    string message = Regex.Replace(
                        elem.Message,
                        $@"\b{Regex.Escape(elem.Name)}\b",
                        $"{businessName} ({elem.Name})"
                    );

                    return new EdiElementError(
                        FieldKey: $"{seg.Name}{elem.Position:D2}",
                        ElementPosition: elem.Position,
                        ElementCode: code,
                        BusinessName: businessName,
                        ErrorType: elem.Code.ToString(),
                        Message: message,
                        Value: string.IsNullOrEmpty(elem.Value) ? null : displayValue,
                        ErrorDescription: elem.Message
                    );
                }).ToList()
            )).ToList();

            return new EdiValidationResult(false, segErrors, ValidatedAt: DateTime.UtcNow);
        }

        // ── Internals ─────────────────────────────────────────────────────────

        private static (string PropName, string Code) Resolve(string seg, int pos, string rawCode)
        {
            var map = _map.Value;

            if (map.TryGetValue((seg, pos), out var hit))
                return (hit.PropName, hit.Code);

            var byCode = map.FirstOrDefault(kv => kv.Key.Seg == seg && kv.Value.Code == rawCode);
            if (byCode.Value != default) return (byCode.Value.PropName, byCode.Value.Code);

            var global = map.FirstOrDefault(kv => kv.Value.Code == rawCode);
            return global.Value != default
                ? (global.Value.PropName, global.Value.Code)
                : ($"Unknown_{rawCode}", rawCode);
        }

        private static string ToBusinessName(string seg, string code, string propName)
        {
            if (KnownNames.TryGetValue($"{seg}_{code}", out var known)) return known;
            var clean = Regex.Replace(propName, @"_\d+$", "");
            return Regex.Replace(clean, @"(?<=[a-z])(?=[A-Z])", " ");
        }

        private static string EnrichValue(string code, string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            // Re-check value explicitly to satisfy the compiler's nullability analysis for dictionary keys
            string nonNullValue = value ?? "";
            if (string.IsNullOrEmpty(nonNullValue)) return "";

            string? desc = null;
            switch (code)
            {
                case "1029":
                    if (ClaimStatus.ContainsKey(nonNullValue)) desc = ClaimStatus[nonNullValue];
                    break;
                case "1032":
                    if (FilingCodes.ContainsKey(nonNullValue)) desc = FilingCodes[nonNullValue];
                    break;
                case "305":
                    if (HandlingCodes.ContainsKey(nonNullValue)) desc = HandlingCodes[nonNullValue];
                    break;
                case "591":
                    if (PaymentCodes.ContainsKey(nonNullValue)) desc = PaymentCodes[nonNullValue];
                    break;
            }
            return desc != null ? $"{nonNullValue} ({desc})" : nonNullValue;
        }

        private static Dictionary<(string Seg, int Pos), (string Code, string PropName)> BuildMap(Type type, HashSet<Type> visited)
        {
            var map = new Dictionary<(string, int), (string, string)>();
            Traverse(type, visited, map);
            return map;
        }

        private static void Traverse(Type type, HashSet<Type> visited, Dictionary<(string, int), (string, string)> map)
        {
            if (type == null || !visited.Add(type)) return;
            if (type.Namespace?.StartsWith("System") == true || type.Namespace?.StartsWith("Microsoft") == true) return;

            string? seg = type.GetCustomAttributesData()
                .FirstOrDefault(a => a.AttributeType.Name == "SegmentAttribute")
                ?.ConstructorArguments.FirstOrDefault().Value?.ToString();

            if (seg != null)
            {
                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    string? code = null; int pos = 0;
                    try
                    {
                        foreach (var a in prop.GetCustomAttributesData())
                        {
                            if (a.AttributeType.Name == "DataElementAttribute" && a.ConstructorArguments.Count > 0)
                                code = a.ConstructorArguments[0].Value?.ToString();
                            if (a.AttributeType.Name == "PosAttribute" && a.ConstructorArguments.Count > 0)
                                pos = Convert.ToInt32(a.ConstructorArguments[0].Value);
                        }
                    }
                    catch { }
                    if (code != null && pos > 0) map[(seg, pos)] = (code, prop.Name);
                }
            }

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var pt = prop.PropertyType;
                if (pt.IsGenericType && pt.GetGenericTypeDefinition() == typeof(List<>)) pt = pt.GetGenericArguments()[0];
                if (!pt.IsPrimitive && pt != typeof(string)) Traverse(pt, visited, map);
            }
        }

        private static readonly Dictionary<string, string> KnownNames = new()
        {
            { "BPR_305",  "Transaction Handling Code" },
            { "BPR_782",  "Total Premium Payment Amount" },
            { "BPR_591",  "Payment Method Code" },
            { "CLP_1028", "Patient Control Number" },
            { "CLP_1029", "Claim Status Code" },
            { "CLP_782",  "Claim Charge/Payment Amount" },
            { "CLP_1032", "Claim Filing Indicator Code" },
            { "CLP_127",  "Payer Claim Control Number" },
            { "TRN_127",  "Current Transaction Trace Number" },
        };

        private static readonly Dictionary<string, string> ClaimStatus = new()
        {
            {"1","Processed as Primary"},{"2","Processed as Secondary"},{"3","Processed as Tertiary"},
            {"4","Denied"},{"19","Processed as Primary, Forwarded to Additional Payer(s)"},
            {"20","Processed as Secondary, Forwarded to Additional Payer(s)"},
            {"22","Reversal of Previous Payment"},{"25","Predetermination Pricing Only - No Payment"},
        };

        private static readonly Dictionary<string, string> FilingCodes = new()
        {
            {"12","PPO"},{"13","Point of Service"},{"14","EPO"},{"15","Indemnity Insurance"},
            {"MA","Medicare Part A"},{"MB","Medicare Part B"},{"MC","Medicaid"},
            {"HM","HMO"},{"WC","Workers Compensation"},{"VA","Veterans Affairs"},{"ZZ","Mutually Defined"},
        };

        private static readonly Dictionary<string, string> HandlingCodes = new()
        {
            {"C","Payment Accompanies Remittance"},{"D","Make Payment Only"},
            {"H","Notification Only"},{"I","Remittance Information Only"},
        };

        private static readonly Dictionary<string, string> PaymentCodes = new()
        {
            {"ACH","Automated Clearing House"},{"CHK","Check"},
            {"FWT","Wire Transfer"},{"NON","Non-Payment Data"},
        };
    }
}