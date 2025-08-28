using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Alethic.Auth0.Operator.Extensions
{
    /// <summary>
    /// Extension methods for ILogger that provide JSON formatted logging with timestamp and message fields.
    /// </summary>
    public static class ILoggerExtensions
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Logs an information message in JSON format with timestamp.
        /// </summary>
        public static void LogInformationJson(this ILogger logger, string message, object? additionalData = null)
        {
            LogJson(logger, LogLevel.Information, message, additionalData, null);
        }

        /// <summary>
        /// Logs a warning message in JSON format with timestamp.
        /// </summary>
        public static void LogWarningJson(this ILogger logger, string message, object? additionalData = null)
        {
            LogJson(logger, LogLevel.Warning, message, additionalData, null);
        }

        /// <summary>
        /// Logs an error message in JSON format with timestamp.
        /// </summary>
        public static void LogErrorJson(this ILogger logger, string message, object? additionalData = null, Exception? exception = null)
        {
            LogJson(logger, LogLevel.Error, message, additionalData, exception);
        }

        /// <summary>
        /// Logs a debug message in JSON format with timestamp.
        /// </summary>
        public static void LogDebugJson(this ILogger logger, string message, object? additionalData = null)
        {
            LogJson(logger, LogLevel.Debug, message, additionalData, null);
        }

        /// <summary>
        /// Logs a critical message in JSON format with timestamp.
        /// </summary>
        public static void LogCriticalJson(this ILogger logger, string message, object? additionalData = null, Exception? exception = null)
        {
            LogJson(logger, LogLevel.Critical, message, additionalData, exception);
        }

        /// <summary>
        /// Core JSON logging method that formats all log entries consistently.
        /// </summary>
        private static void LogJson(ILogger logger, LogLevel logLevel, string message, object? additionalData, Exception? exception)
        {
            var logEntry = new Dictionary<string, object?>
            {
                ["timestamp"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["message"] = message
            };

            // Add additional structured data if provided
            if (additionalData != null)
            {
                // If additionalData is a dictionary or anonymous object, add its properties
                if (additionalData is IDictionary<string, object?> dict)
                {
                    foreach (var kvp in dict)
                    {
                        if (kvp.Key != "timestamp" && kvp.Key != "message") // Avoid overwriting core fields
                        {
                            logEntry[kvp.Key] = kvp.Value;
                        }
                    }
                }
                else
                {
                    // For anonymous objects or other types, serialize and add as structured data
                    var properties = additionalData.GetType().GetProperties();
                    foreach (var prop in properties)
                    {
                        var key = JsonNamingPolicy.CamelCase.ConvertName(prop.Name);
                        if (key != "timestamp" && key != "message") // Avoid overwriting core fields
                        {
                            logEntry[key] = prop.GetValue(additionalData);
                        }
                    }
                }
            }

            // Add exception details if present
            if (exception != null)
            {
                logEntry["exception"] = new
                {
                    type = exception.GetType().Name,
                    message = exception.Message,
                    stackTrace = exception.StackTrace
                };
            }

            var json = JsonSerializer.Serialize(logEntry, JsonOptions);

            // Use the appropriate log level
            switch (logLevel)
            {
                case LogLevel.Information:
                    logger.LogInformation("{JsonLog}", json);
                    break;
                case LogLevel.Warning:
                    logger.LogWarning(exception, "{JsonLog}", json);
                    break;
                case LogLevel.Error:
                    logger.LogError(exception, "{JsonLog}", json);
                    break;
                case LogLevel.Debug:
                    logger.LogDebug("{JsonLog}", json);
                    break;
                case LogLevel.Critical:
                    logger.LogCritical(exception, "{JsonLog}", json);
                    break;
            }
        }
    }
}