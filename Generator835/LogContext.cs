// Edi.Generator835/LogContext.cs
using System;

namespace Edi.Generator835
{
    public static class LogContext
    {
        public static IDisposable PushProperty(string name, object value, bool destructureObjects = false)
        {
            return new DummyDisposable();
        }

        private class DummyDisposable : IDisposable
        {
            public void Dispose()
            {
                // No-op
            }
        }
    }
}
