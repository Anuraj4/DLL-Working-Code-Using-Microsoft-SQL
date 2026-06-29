using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Edi.Generator835.Configuration;

namespace Edi.Generator835.Services
{
    /// <summary>
    /// Logic for splitting mixed codes (CARC and RARC) from a single input string.
    /// </summary>
    public class CarcRarcSplitter
    {
        private readonly MappingConfiguration _mappings;

        public CarcRarcSplitter(MappingConfiguration mappings)
        {
            _mappings = mappings;
        }

        /// <summary>
        /// Splits a comma/semicolon separated string into its CARC and RARC components.
        /// Returns (FirstCarc, AllRarcsJoined).
        /// </summary>
        public (string Carc, string Rarc) Split(string mixedCodes)
        {
            if (string.IsNullOrWhiteSpace(mixedCodes))
                return (string.Empty, string.Empty);

            var codes = mixedCodes.Split(new[] { ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Select(c => Regex.Replace(c.Trim(), @"^(0>|Q>)+", "", RegexOptions.IgnoreCase))
                                  .Where(c => !string.IsNullOrEmpty(c))
                                  .ToList();

            var carcList = new List<string>();
            var rarcList = new List<string>();

            foreach (var code in codes)
            {
                if (_mappings.IsCarc(code))
                {
                    carcList.Add(code);
                }
                else
                {
                    // If it's not a CARC, it's a RARC (Remark Code)
                    rarcList.Add(code);
                    if (_mappings.IsRarc(code))
                    {
                        Serilog.Log.Information("100% correct remark: {RemarkCode}", code);
                    }
                    else
                    {
                        Serilog.Log.Warning("Remark code does not match payer specific remark: {RemarkCode}", code);
                    }
                }
            }

            return (string.Join(", ", carcList), string.Join(", ", rarcList));
        }

        /// <summary>
        /// Splits and returns all identified CARCs and RARCs separately.
        /// </summary>
        public (List<string> Carcs, List<string> Rarcs) SplitAll(string mixedCodes)
        {
            if (string.IsNullOrWhiteSpace(mixedCodes))
                return (new List<string>(), new List<string>());

            var codes = mixedCodes.Split(new[] { ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Select(c => Regex.Replace(c.Trim(), @"^(0>|Q>)+", "", RegexOptions.IgnoreCase))
                                  .Where(c => !string.IsNullOrEmpty(c))
                                  .ToList();

            var carcs = codes.Where(c => _mappings.IsCarc(c)).ToList();
            var rarcs = codes.Where(c => !_mappings.IsCarc(c)).ToList();

            return (carcs, rarcs);
        }
    }
}
