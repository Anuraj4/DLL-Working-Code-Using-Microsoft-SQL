// Edi.Generator835/Log.cs
using System;
using System.Text.RegularExpressions;
using Edi.Generator835.Services;

namespace Edi.Generator835
{
    public static class Log
    {
        private static string FormatMessage(string messageTemplate, object[] propertyValues)
        {
            if (propertyValues == null || propertyValues.Length == 0)
                return messageTemplate;

            try
            {
                int index = 0;
                string formattedTemplate = Regex.Replace(
                    messageTemplate,
                    @"\{([a-zA-Z0-9_]+)(:[^}]+)?\}",
                    match => "{" + (index++) + match.Groups[2].Value + "}"
                );
                return string.Format(formattedTemplate, propertyValues);
            }
            catch
            {
                return messageTemplate + " | Args: " + string.Join(", ", propertyValues);
            }
        }

        public static void Verbose(string messageTemplate, params object[] propertyValues)
        {
            var logger = LoggingProvider.Logger;
            if (logger != null)
            {
                logger.Debug(FormatMessage(messageTemplate, propertyValues));
            }
        }

        public static void Verbose(Exception exception, string messageTemplate, params object[] propertyValues)
        {
            var logger = LoggingProvider.Logger;
            if (logger != null)
            {
                logger.Debug(FormatMessage(messageTemplate, propertyValues) + $"\nException: {exception}");
            }
        }

        public static void Debug(string messageTemplate, params object[] propertyValues)
        {
            var logger = LoggingProvider.Logger;
            if (logger != null)
            {
                logger.Debug(FormatMessage(messageTemplate, propertyValues));
            }
        }

        public static void Debug(Exception exception, string messageTemplate, params object[] propertyValues)
        {
            var logger = LoggingProvider.Logger;
            if (logger != null)
            {
                logger.Debug(FormatMessage(messageTemplate, propertyValues) + $"\nException: {exception}");
            }
        }

        public static void Information(string messageTemplate, params object[] propertyValues)
        {
            var logger = LoggingProvider.Logger;
            if (logger != null)
            {
                logger.Information(FormatMessage(messageTemplate, propertyValues));
            }
        }

        public static void Information(Exception exception, string messageTemplate, params object[] propertyValues)
        {
            var logger = LoggingProvider.Logger;
            if (logger != null)
            {
                logger.Information(FormatMessage(messageTemplate, propertyValues) + $"\nException: {exception}");
            }
        }

        public static void Warning(string messageTemplate, params object[] propertyValues)
        {
            var logger = LoggingProvider.Logger;
            if (logger != null)
            {
                logger.Warning(FormatMessage(messageTemplate, propertyValues));
            }
        }

        public static void Warning(Exception exception, string messageTemplate, params object[] propertyValues)
        {
            var logger = LoggingProvider.Logger;
            if (logger != null)
            {
                logger.Warning(FormatMessage(messageTemplate, propertyValues) + $"\nException: {exception}");
            }
        }

        public static void Error(string messageTemplate, params object[] propertyValues)
        {
            var logger = LoggingProvider.Logger;
            if (logger != null)
            {
                logger.Error(FormatMessage(messageTemplate, propertyValues));
            }
        }

        public static void Error(Exception exception, string messageTemplate, params object[] propertyValues)
        {
            var logger = LoggingProvider.Logger;
            if (logger != null)
            {
                logger.Error(FormatMessage(messageTemplate, propertyValues), exception);
            }
        }
    }
}
