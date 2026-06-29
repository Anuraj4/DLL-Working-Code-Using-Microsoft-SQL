using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;

namespace Edi.Generator835.Rules
{
    /// <summary>
    /// Maps verbose ClaimType text from EOB data to CLP-06 ClaimFilingIndicator codes
    /// using fuzzy word-matching against the 'claim_filling_indicator' config sheet.
    /// 
    /// Matching strategy:
    /// 1. Tokenize both ClaimType and each EobExtractedText into words
    /// 2. For each config row, check if ALL words in EobExtractedText appear in ClaimType
    /// 3. Score matches by (matching words count / keyword length) — longest keyword match wins
    /// 4. If no match → returns null, letting DetermineValue's fallback chain (fallback_codes → "MC") kick in
    /// </summary>
    public class ClaimFillingIndicatorRule : IRuleDefinition
    {
        private const string TableName = "claim_filling_indicator";

        public string Name => "ClaimFillingIndicator";
        public int Priority => 8; // Run before generic CsvLookup (priority 10)
        public string TargetField => "CLP06";

        public bool CanExecute(RuleExecutionContext context)
        {
            return !string.IsNullOrWhiteSpace(context.RawValue) &&
                   context.Mappings.RawMappingTables.ContainsKey(TableName);
        }

        public string? Execute(RuleExecutionContext context)
        {
            if (!context.Mappings.RawMappingTables.TryGetValue(TableName, out var records) || records.Count == 0)
                return null;

            string claimType = context.RawValue;
            var claimTypeWords = Tokenize(claimType);

            if (claimTypeWords.Count == 0)
                return null;

            // Also check against the full text (lowered) for substring matching of multi-word phrases
            string claimTypeLower = claimType.ToLowerInvariant();

            string? bestCode = null;
            int bestScore = 0;

            foreach (var record in records)
            {
                if (!record.TryGetValue("EobExtractedText", out var keyword) || string.IsNullOrWhiteSpace(keyword))
                    continue;

                if (!record.TryGetValue("ClaimFillingIndicator", out var code) || string.IsNullOrWhiteSpace(code))
                    continue;

                // Strategy 1: Check if keyword phrase appears as substring (handles "Medicare Part A")
                if (claimTypeLower.Contains(keyword.ToLowerInvariant()))
                {
                    int score = keyword.Length; // Longer exact substring = better match
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestCode = code.Trim();
                    }
                    continue;
                }

                // Strategy 2: Fuzzy word matching — all words in keyword must appear in claim type
                var keywordWords = Tokenize(keyword);
                if (keywordWords.Count == 0) continue;

                int matchedCount = keywordWords.Count(w => claimTypeWords.Contains(w));

                if (matchedCount == keywordWords.Count)
                {
                    // All keyword words found in claim type — score by keyword length
                    int score = keyword.Length;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestCode = code.Trim();
                    }
                }
                else if (matchedCount > 0)
                {
                    // Partial fuzzy match — only use if nothing better found
                    // Score is partial: e.g., 1 out of 3 words matched = lower priority
                    int partialScore = matchedCount; // Much lower than full-phrase match lengths
                    if (bestScore == 0 && partialScore > 0)
                    {
                        bestScore = 0; // Keep bestScore=0 so full matches always win
                        bestCode = code.Trim();
                    }
                }
            }

            if (bestCode != null)
            {
                Log.Information("[ClaimFillingIndicator] Matched ClaimType '{ClaimType}' → Code: {Code}", claimType, bestCode);
            }

            return bestCode;
        }

        /// <summary>
        /// Tokenize text into lowercase words, stripping parentheses and common delimiters.
        /// </summary>
        private static HashSet<string> Tokenize(string text)
        {
            return text
                .ToLowerInvariant()
                .Replace("(", " ").Replace(")", " ")
                .Replace(",", " ").Replace(".", " ")
                .Replace("/", " ").Replace("-", " ")
                .Replace("&", " ")
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet();
        }
    }
}
