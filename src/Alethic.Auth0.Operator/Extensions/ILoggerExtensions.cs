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
        private const string TimestampKey = "timestamp";
        private const string MessageKey = "message";
        private const string ExceptionKey = "exception";
        private const string JsonLogTemplate = "{JsonLog}";
        private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ssZ";
        
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
                [TimestampKey] = DateTimeOffset.UtcNow.ToString(TimestampFormat),
                [MessageKey] = message
            };

            // Add additional structured data if provided
            if (additionalData != null)
            {
                // If additionalData is a dictionary or anonymous object, add its properties
                if (additionalData is IDictionary<string, object?> dict)
                {
                    foreach (var kvp in dict)
                    {
                        if (kvp.Key != TimestampKey && kvp.Key != MessageKey) // Avoid overwriting core fields
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
                        if (key != TimestampKey && key != MessageKey) // Avoid overwriting core fields
                        {
                            logEntry[key] = prop.GetValue(additionalData);
                        }
                    }
                }
            }

            // Add exception details if present
            if (exception != null)
            {
                logEntry[ExceptionKey] = new
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
                    logger.LogInformation(JsonLogTemplate, json);
                    break;
                case LogLevel.Warning:
                    logger.LogWarning(exception, JsonLogTemplate, json);
                    break;
                case LogLevel.Error:
                    logger.LogError(exception, JsonLogTemplate, json);
                    break;
                case LogLevel.Debug:
                    logger.LogDebug(JsonLogTemplate, json);
                    break;
                case LogLevel.Critical:
                    logger.LogCritical(exception, JsonLogTemplate, json);
                    break;
            }
        }
    }
}