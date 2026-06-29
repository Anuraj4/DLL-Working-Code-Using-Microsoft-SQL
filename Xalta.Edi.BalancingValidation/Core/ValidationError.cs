using System.Collections.Generic;

namespace Xalta.Edi.BalancingValidation.Core
{
    public class ValidationError
    {
        public string ErrorCode { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string SegmentName { get; set; } = string.Empty;
        public int? SegmentPosition { get; set; }
        public string ElementReference { get; set; } = string.Empty;
        public string ContextInfo { get; set; } = string.Empty;

        public int? ElementPosition { get; set; }

        public string? ElementBusinessName { get; set; }

        public string? ElementCode { get; set; }

        public string? ElementValue { get; set; }

        public string? ErrorDescription { get; set; }

        public override string ToString()
        {
            var parts = new List<string>();

            if (!string.IsNullOrEmpty(SegmentName))
            {
                var segInfo = SegmentName;
                if (SegmentPosition.HasValue) segInfo += $" (Position {SegmentPosition})";
                if (!string.IsNullOrEmpty(ContextInfo)) segInfo += $" in Loop {ContextInfo}";
                parts.Add($"Segment: {segInfo}");
            }

            if (!string.IsNullOrEmpty(ElementReference))
            {
                var elemInfo = ElementReference;
                if (!string.IsNullOrEmpty(ElementBusinessName)) elemInfo += $" — {ElementBusinessName}";
                if (!string.IsNullOrEmpty(ElementCode)) elemInfo += $" [Code: {ElementCode}]";
                if (ElementPosition.HasValue) elemInfo += $" at position {ElementPosition}";
                parts.Add($"Field: {elemInfo}");
            }

            if (!string.IsNullOrEmpty(ElementValue))
                parts.Add($"Value: {ElementValue}");

            if (!string.IsNullOrEmpty(ErrorCode))
                parts.Add($"Error: {ErrorCode}");

            if (!string.IsNullOrEmpty(ErrorDescription))
                parts.Add($"Details: {ErrorDescription}");

            if (!string.IsNullOrEmpty(Message))
                parts.Add($"Message: {Message}");

            return string.Join(" | ", parts);
        }
    }
}
