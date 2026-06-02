using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Omnisense
{
    [InitializeOnLoad]
    public static class OmnisenseLogger
    {
        private static string LogPath => Path.Combine(Directory.GetCurrentDirectory(), "Logs", "Omnisense.log");

        static OmnisenseLogger()
        {
            // Subscribe to Unity log event, preventing duplicate registrations
            Application.logMessageReceived -= HandleUnityLog;
            Application.logMessageReceived += HandleUnityLog;
        }

        /// <summary>
        /// Intercepts all Unity Console logs and writes them to the external file
        /// if they are related to Omnisense or represent errors/exceptions.
        /// </summary>
        private static void HandleUnityLog(string logString, string stackTrace, LogType type)
        {
            // Ignore logs generated directly by OmnisenseLogger methods to prevent recursion
            if (logString.StartsWith("[Omnisense][") || logString.StartsWith("[Omnisense-Logger]"))
            {
                return;
            }

            bool isOmnisenseLog = logString.Contains("Omnisense");
            bool isExceptionOrError = type == LogType.Error || type == LogType.Exception;

            // Save the log if it's from Omnisense or if it's a native system error/exception
            if (isOmnisenseLog || isExceptionOrError)
            {
                string category = type.ToString().ToUpper();
                if (isOmnisenseLog && !isExceptionOrError)
                {
                    // Map generic info logs to INFO category
                    category = "INFO";
                }

                string message = logString;
                if (isExceptionOrError && !string.IsNullOrEmpty(stackTrace))
                {
                    message += "\nStack Trace:\n" + stackTrace;
                }

                // Write directly to the log file (does not call Debug.Log to avoid recursion)
                WriteToFile(message, category);
            }
        }

        /// <summary>
        /// Logs an informational message to the persistent log file and the Unity Console.
        /// </summary>
        public static void Log(string message, string category = "INFO")
        {
            WriteToFile(message, category);
            Debug.Log($"[Omnisense][{category}] {message}");
        }

        /// <summary>
        /// Logs a warning message to the persistent log file and the Unity Console.
        /// </summary>
        public static void LogWarning(string message, string category = "WARNING")
        {
            WriteToFile(message, category);
            Debug.LogWarning($"[Omnisense][{category}] {message}");
        }

        /// <summary>
        /// Logs an error message to the persistent log file and the Unity Console.
        /// </summary>
        public static void LogError(string message, string category = "ERROR")
        {
            WriteToFile(message, category);
            Debug.LogError($"[Omnisense][{category}] {message}");
        }

        /// <summary>
        /// Records the start of a new turn or prompt run with separator and context info.
        /// </summary>
        public static void StartNewTurn(string turnId, string prompt, string model)
        {
            string separator = new string('=', 70);
            string entry = $"\n{separator}\n" +
                           $"NEW TURN STARTED: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\n" +
                           $"Turn ID: {turnId}\n" +
                           $"Model: {model}\n" +
                           $"User Prompt: {prompt}\n" +
                           $"{separator}\n";

            // Write only to file to prevent duplicate spam in console
            try
            {
                string logDir = Path.GetDirectoryName(LogPath);
                if (!Directory.Exists(logDir))
                    Directory.CreateDirectory(logDir);
                File.AppendAllText(LogPath, entry + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Omnisense-Logger] Failed to write new turn boundary: {ex.Message}");
            }

            Debug.Log($"[Omnisense][SYSTEM] --- NEW TURN STARTED: {turnId} --- Prompt length: {prompt?.Length ?? 0} chars");
        }

        private static void WriteToFile(string message, string category)
        {
            try
            {
                string logDir = Path.GetDirectoryName(LogPath);
                if (!Directory.Exists(logDir))
                    Directory.CreateDirectory(logDir);

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string formatted = $"[{timestamp}] [{category}] {message}";

                File.AppendAllText(LogPath, formatted + Environment.NewLine);
            }
            catch (Exception ex)
            {
                // Fallback to console if file writing fails
                Debug.LogWarning($"[Omnisense-Logger] Failed to write log entry to file: {ex.Message}");
            }
        }
    }
}
