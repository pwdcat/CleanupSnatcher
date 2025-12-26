using BepInEx.Logging;
using System.Runtime.CompilerServices;

namespace CleanupSnatcher
{
    internal static class Log
    {
        private static ManualLogSource? _logSource;
        internal static bool EnableDebugLogs { get; set; }

        // Pre-allocated constant strings to reduce allocations
        private const string ERROR_PREFIX = "[ERROR] ";
        private const string FATAL_PREFIX = "[FATAL] ";
        private const string INFO_PREFIX = "[INFO] ";
        private const string WARNING_PREFIX = "[WARNING] ";

        internal static void Init(ManualLogSource logSource)
        {
            _logSource = logSource;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Error(object data) => _logSource?.LogMessage(ERROR_PREFIX + data);
    
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Fatal(object data) => _logSource?.LogMessage(FATAL_PREFIX + data);
    
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Info(object data) { if (EnableDebugLogs) _logSource?.LogMessage(INFO_PREFIX + data); }
    
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Message(object data) => _logSource?.LogMessage(data);
    
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Warning(object data) => _logSource?.LogMessage(WARNING_PREFIX + data);
    }
}