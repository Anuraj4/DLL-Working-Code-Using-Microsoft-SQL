using System;

namespace Xalta.Edi.LicenseProvider
{
    public static class EdiLicense
    {
        private static bool _isInitialized = false;
        private static readonly object _lock = new object();
        private const string SerialKey = "c417cb9dd9d54297a55c032a74c87996";

        public static void Initialize()
        {
            if (_isInitialized) return;

            lock (_lock)
            {
                if (_isInitialized) return;

                // Console.WriteLine("EdiLicense: Initializing...");
                try
                {
                    // Attempt token-based initialization first
                    // This is often more reliable in headless or test environments
                    var token = EdiFabric.SerialKey.GetToken(SerialKey);
                    Console.WriteLine($"EdiLicense: Retrieved token: {token}");
                    EdiFabric.SerialKey.SetToken(token);
                    Console.WriteLine("EdiLicense: SetToken success.");
                    Console.WriteLine("Days to Expire: " + EdiFabric.SerialKey.DaysToExpiration);
                    _isInitialized = true;
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"EdiLicense: Token-based failed: {ex.Message}");
                    Console.WriteLine("Days to Expire: " + EdiFabric.SerialKey.DaysToExpiration);
                    // Fallback to direct serial key set
                    try
                    {
                        EdiFabric.SerialKey.Set(SerialKey, true);
                        Console.WriteLine("EdiLicense: SerialKey.Set success.");
                        Console.WriteLine("Days to Expire: " + EdiFabric.SerialKey.DaysToExpiration);
                        _isInitialized = true;
                    }
                    catch (Exception ex2)
                    {
                        Console.WriteLine("Days to Expire: " + EdiFabric.SerialKey.DaysToExpiration);
                        Console.WriteLine($"EdiLicense: SerialKey.Set failed: {ex2.Message}");
                        // If both fail, we don't throw here to avoid crashing the static constructor
                        // but subsequent EDI operations will naturally fail with license errors.
                    }
                }
            }
        }
    }
}
