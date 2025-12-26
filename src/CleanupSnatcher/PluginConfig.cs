using System;
using BepInEx.Configuration;

namespace CleanupSnatcher
{
    /// Configuration management for the CleanupSnatcher
    /// Handles all user-configurable settings for projectile grabbing
    public static class PluginConfig
    {
        // Configuration entries
        public static ConfigEntry<bool> EnableProjectileGrabbing { get; private set; }
        public static ConfigEntry<bool> EnableDebugLogs { get; private set; }

        // Initializes all configuration entries
        // cfg: The BepInEx configuration file
        public static void Init(ConfigFile cfg)
        {
            // Projectile grabbing settings
            EnableProjectileGrabbing = cfg.Bind("General", "EnableProjectileGrabbing", true,
                "Enable grabbing of projectiles from Drifter's Cleanup ability");

            // Debug settings
            EnableDebugLogs = cfg.Bind("General", "EnableDebugLogs", false,
                "Enable debug logging for projectile grabbing operations");
        }

        // Removes all event handlers to prevent memory leaks
        // debugLogsHandler: Handler for debug log settings changes
        // projectileGrabbingHandler: Handler for projectile grabbing toggle changes
        public static void RemoveEventHandlers(
            EventHandler debugLogsHandler,
            EventHandler projectileGrabbingHandler)
        {
            EnableDebugLogs.SettingChanged -= debugLogsHandler;
            EnableProjectileGrabbing.SettingChanged -= projectileGrabbingHandler;
        }
    }
}