using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;

namespace Xalta.Edi.BalancingValidation.Core
{
    /// <summary>
    /// Formats EdiValidationResult into multiple output formats.
    /// </summary>
    public static class EdiValidationFormatter
    {
        // ── Console / Terminal Output ─────────────────────────────────────────

        /// <summary>
        /// Rich console-formatted output with box-drawing characters.
        /// </summary>
        public static string ToConsole(EdiValidationResult result)
        {
            var sb = new StringBuilder();

            sb.AppendLine("╔══════════════════════════════════════════════════════════════╗");
            sb.AppendLine("║           EDI 835 Validation Report                         ║");
            sb.AppendLine("╚══════════════════════════════════════════════════════════════╝");
            sb.AppendLine($"  Version   : {result.TransactionVersion}");
            sb.AppendLine($"  Validated : {result.ValidatedAtTime:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"  Status    : {(result.IsValid ? "✅ VALID" : "❌ INVALID")}");

            if (result.IsValid)
            {
                sb.AppendLine("  Result    : Transaction passed all validation checks.");
                return sb.ToString();
            }

            sb.AppendLine($"  Errors    : {result.TotalErrorCount} element error(s) across {result.Errors.Count} segment(s)");
            sb.AppendLine();

            foreach (var segErr in result.Errors)
            {
                sb.AppendLine($"  ┌─────────────────────────────────────────────────────────");
                sb.AppendLine($"  │ Segment  : {segErr.SegmentName}");
                sb.AppendLine($"  │ Position : {segErr.SegmentPosition}");
                sb.AppendLine($"  │ Loop     : {segErr.Loop ?? "N/A (Header/Trailer)"}");
                sb.AppendLine($"  │ Errors   : {segErr.ElementErrors.Count}");

                foreach (var elemErr in segErr.ElementErrors)
                {
                    sb.AppendLine($"  │");
                    sb.AppendLine($"  │   ┌── [{elemErr.FieldKey}] {elemErr.BusinessName}");
                    sb.AppendLine($"  │   ├── Element Code  : {elemErr.ElementCode}");
                    sb.AppendLine($"  │   ├── Error Type    : {elemErr.ErrorType}");
                    sb.AppendLine($"  │   ├── Description   : {elemErr.ErrorDescription ?? "N/A"}");
                    sb.AppendLine($"  │   ├── Element Position   : {elemErr.ElementPosition}");

                    if (elemErr.Value != null)
                        sb.AppendLine($"  │   ├── Value         : {elemErr.Value}");

                    sb.AppendLine($"  │   └── Message       : {elemErr.Message}");
                }

                sb.AppendLine($"  └─────────────────────────────────────────────────────────");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        // ── Plain Text (for logging) ───────────────────────────────────────────

        /// <summary>
        /// Single-line per error — suitable for application logs.
        /// </summary>
        public static string ToLog(EdiValidationResult result)
        {
            if (result.IsValid)
                return $"[EDI835][{result.TransactionVersion}] VALID";

            var sb = new StringBuilder();
            sb.AppendLine($"[EDI835][{result.TransactionVersion}] INVALID — {result.TotalErrorCount} error(s)");

            foreach (var segErr in result.Errors)
            {
                foreach (var elemErr in segErr.ElementErrors)
                {
                    sb.AppendLine(
                        $"  [{segErr.SegmentName}][Loop:{segErr.Loop ?? "N/A"}]" +
                        $"[{elemErr.FieldKey}] {elemErr.BusinessName}" +
                        $" | {elemErr.ErrorType}: {elemErr.ErrorDescription ?? "N/A"}" +
                        (elemErr.Value != null ? $" | Value: '{elemErr.Value}'" : "") +
                        $" | {elemErr.Message}"
                    );
                }
            }

            return sb.ToString();
        }

        // ── Summary Only ──────────────────────────────────────────────────────

        /// <summary>
        /// Brief one-liner summary for status checks.
        /// </summary>
        public static string ToSummary(EdiValidationResult result)
        {
            if (result.IsValid)
                return $"EDI 835 [{result.TransactionVersion}]: Valid ✅";

            var segments = result.Errors.Select(s => s.SegmentName).Distinct();
            return $"EDI 835 [{result.TransactionVersion}]: Invalid ❌ — " +
                   $"{result.TotalErrorCount} error(s) in segments: {string.Join(", ", segments)}";
        }

        // ── JSON Output ───────────────────────────────────────────────────────

        /// <summary>
        /// Full structured JSON output — for APIs or serialization.
        /// </summary>
        public static string ToJson(EdiValidationResult result, bool indented = true)
        {
            return JsonConvert.SerializeObject(result, indented ? Formatting.Indented : Formatting.None);
        }

        // ── HTML Report ───────────────────────────────────────────────────────

        /// <summary>
        /// Styled HTML report — for web display or email attachments.
        /// </summary>
        public static string ToHtml(EdiValidationResult result)
        {
            var sb = new StringBuilder();

            var statusColor = result.IsValid ? "#28a745" : "#dc3545";
            var statusBadge = result.IsValid ? "✔ VALID" : $"✘ INVALID ({result.TotalErrorCount} errors)";

            sb.Append($@"
<!DOCTYPE html>
<html lang='en'>
<head>
  <meta charset='UTF-8'>
  <title>EDI 835 Validation Report</title>
  <style>
    body       {{ font-family: 'Segoe UI', Tahoma, sans-serif; margin: 24px; background: #f5f5f5; color: #333; }}
    h1         {{ color: #444; }}
    .meta      {{ background: #fff; padding: 16px 20px; border-radius: 6px; margin-bottom: 20px;
                  border-left: 5px solid {statusColor}; box-shadow: 0 1px 3px rgba(0,0,0,.1); }}
    .badge     {{ display:inline-block; padding: 4px 12px; border-radius: 12px;
                  background: {statusColor}; color: #fff; font-weight: bold; font-size: 14px; }}
    .segment   {{ background: #fff; border-radius: 6px; margin-bottom: 16px;
                  box-shadow: 0 1px 3px rgba(0,0,0,.1); overflow: hidden; }}
    .seg-header{{ background: #495057; color: #fff; padding: 10px 16px; font-weight: bold; font-size: 14px; }}
    .seg-meta  {{ display:flex; gap: 24px; padding: 8px 16px; background: #f8f9fa;
                  font-size: 13px; border-bottom: 1px solid #dee2e6; }}
    table      {{ width: 100%; border-collapse: collapse; font-size: 13px; }}
    th         {{ background: #e9ecef; text-align: left; padding: 8px 12px;
                  border-bottom: 2px solid #dee2e6; color: #495057; }}
    td         {{ padding: 8px 12px; border-bottom: 1px solid #f0f0f0; vertical-align: top; }}
    tr:hover td{{ background: #f8f9fa; }}
    .key       {{ font-family: monospace; background: #e9ecef; padding: 2px 6px;
                  border-radius: 3px; font-weight: bold; color: #e83e8c; }}
    .code      {{ font-family: monospace; font-size: 12px; color: #6c757d; }}
    .err-missing{{ color: #dc3545; font-weight: 600; }}
    .err-invalid{{ color: #fd7e14; font-weight: 600; }}
    .err-length {{ color: #6f42c1; font-weight: 600; }}
    .err-other  {{ color: #6c757d; }}
    .value     {{ font-family: monospace; background: #fff3cd; padding: 2px 6px;
                  border-radius: 3px; font-size: 12px; }}
  </style>
</head>
<body>
  <h1>EDI 835 Validation Report</h1>
  <div class='meta'>
    <div><strong>Version:</strong> {result.TransactionVersion}</div>
    <div><strong>Validated:</strong> {result.ValidatedAtTime:yyyy-MM-dd HH:mm:ss} UTC</div>
    <div style='margin-top:8px'><span class='badge'>{statusBadge}</span></div>
  </div>
");

            if (result.IsValid)
            {
                sb.Append("<p style='color:#28a745;font-size:16px;'>✔ Transaction passed all validation checks.</p>");
            }
            else
            {
                foreach (var segErr in result.Errors)
                {
                    sb.Append($@"
  <div class='segment'>
    <div class='seg-header'>Segment: {segErr.SegmentName}</div>
    <div class='seg-meta'>
      <span><strong>Position:</strong> {segErr.SegmentPosition}</span>
      <span><strong>Loop:</strong> {segErr.Loop ?? "N/A"}</span>
      <span><strong>Errors:</strong> {segErr.ElementErrors.Count}</span>
    </div>
    <table>
      <thead>
        <tr>
          <th>Segment Field</th>
          <th>Business Name</th>
          <th>Element Code</th>
          <th>Error Type</th>
          <th>Description</th>
          <th>Value</th>
          <th>Message</th>
        </tr>
      </thead>
      <tbody>
");
                    foreach (var elemErr in segErr.ElementErrors)
                    {
                        string errClass = elemErr.ErrorType switch
                        {
                            var s when s.Contains("Missing") => "err-missing",
                            var s when s.Contains("Invalid") => "err-invalid",
                            var s when s.Contains("Too") => "err-length",
                            _ => "err-other"
                        };

                        string valueCell = elemErr.Value != null
                            ? $"<span class='value'>{WebUtility.HtmlEncode(elemErr.Value)}</span>"
                            : "<span style='color:#aaa'>—</span>";

                        sb.Append($@"
        <tr>
          <td><span class='key'>{elemErr.FieldKey}</span></td>
          <td>{elemErr.BusinessName}</td>
          <td><span class='code'>{elemErr.ElementCode}</span></td>
          <td class='{errClass}'>{elemErr.ErrorType}</td>
          <td>{elemErr.ErrorDescription ?? "N/A"}</td>
          <td>{valueCell}</td>
          <td>{WebUtility.HtmlEncode(elemErr.Message)}</td>
        </tr>
");
                    }

                    sb.Append("      </tbody>\n    </table>\n  </div>\n");
                }
            }

            sb.Append("</body>\n</html>");
            return sb.ToString();
        }

        // ── Write to file helpers ─────────────────────────────────────────────

        public static void WriteHtmlReport(EdiValidationResult result, string outputPath)
        {
            File.WriteAllText(outputPath, ToHtml(result), Encoding.UTF8);
        }

        public static void WriteJsonReport(EdiValidationResult result, string outputPath)
        {
            File.WriteAllText(outputPath, ToJson(result), Encoding.UTF8);
        }
    }
}
