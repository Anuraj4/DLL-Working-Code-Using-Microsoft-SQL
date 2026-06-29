// Edi.Generator835/Services/IEdiLogger.cs
using System;

namespace Edi.Generator835.Services
{
    public interface IEdiLogger
    {
        void Debug(string message);
        void Information(string message);
        void Warning(string message);
        void Error(string message, Exception ex = null);
    }
}