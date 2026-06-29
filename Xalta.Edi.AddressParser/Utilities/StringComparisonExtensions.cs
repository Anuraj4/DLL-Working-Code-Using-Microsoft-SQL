using System;

namespace Xalta.Edi.AddressParser.Utilities
{
    public static class StringComparisonExtensions
    {
        public static double JaroWinklerSimilarity(this string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
                return 0;

            if (s1.Equals(s2, StringComparison.OrdinalIgnoreCase))
                return 1.0;

            double jaro = JaroSimilarity(s1, s2);
            int prefixLength = GetPrefixLength(s1, s2);

            return jaro + (prefixLength * 0.1 * (1.0 - jaro));
        }

        private static double JaroSimilarity(string s1, string s2)
        {
            int len1 = s1.Length;
            int len2 = s2.Length;

            int matchDistance = Math.Max(len1, len2) / 2 - 1;

            bool[] s1Matches = new bool[len1];
            bool[] s2Matches = new bool[len2];

            int matches = 0;
            for (int i = 0; i < len1; i++)
            {
                int start = Math.Max(0, i - matchDistance);
                int end = Math.Min(i + matchDistance + 1, len2);

                for (int j = start; j < end; j++)
                {
                    if (s2Matches[j]) continue;
                    if (char.ToUpperInvariant(s1[i]) != char.ToUpperInvariant(s2[j])) continue;

                    s1Matches[i] = true;
                    s2Matches[j] = true;
                    matches++;
                    break;
                }
            }

            if (matches == 0) return 0;

            double transpositions = 0;
            int k = 0;
            for (int i = 0; i < len1; i++)
            {
                if (!s1Matches[i]) continue;
                while (!s2Matches[k]) k++;
                if (char.ToUpperInvariant(s1[i]) != char.ToUpperInvariant(s2[k])) transpositions++;
                k++;
            }

            return (matches / (double)len1 + matches / (double)len2 + (matches - transpositions / 2.0) / matches) / 3.0;
        }

        private static int GetPrefixLength(string s1, string s2)
        {
            int n = Math.Min(4, Math.Min(s1.Length, s2.Length));
            for (int i = 0; i < n; i++)
            {
                if (char.ToUpperInvariant(s1[i]) != char.ToUpperInvariant(s2[i])) return i;
            }
            return n;
        }
    }
}
