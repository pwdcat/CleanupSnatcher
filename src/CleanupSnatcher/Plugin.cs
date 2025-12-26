using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;
using UnityEngine.Networking;
using HarmonyLib;
using RiskOfOptions;
using RiskOfOptions.Options;
using RoR2;
using RoR2.Projectile;

namespace CleanupSnatcher
{
    [BepInPlugin(Constants.PluginGuid, Constants.PluginName, Constants.PluginVersion)]
    public class CleanupSnatcherPlugin : BaseUnityPlugin
    {
        // Plugin instance
        public static CleanupSnatcherPlugin Instance { get; private set; }

        // Gets whether Risk of Options is installed
        public static bool RooInstalled => Chainloader.PluginInfos.ContainsKey("com.rune580.riskofoptions");

        // Gets the directory name where the plugin is located
        public string DirectoryName => System.IO.Path.GetDirectoryName(((BaseUnityPlugin)this).Info.Location);

        // Event handler references for cleanup
        private EventHandler debugLogsHandler;
        private EventHandler projectileGrabbingHandler;

        public void Awake()
        {
            Instance = this;
            Log.Init(Logger);

            Log.Info($"{Constants.LogPrefix} CleanupSnatcherPlugin.Awake() called");

            // Initialize configuration
            PluginConfig.Init(Config);

            Log.EnableDebugLogs = PluginConfig.EnableDebugLogs.Value;
            Log.Info($"{Constants.LogPrefix} Debug logs enabled: {Log.EnableDebugLogs}");

            // Setup configuration event handlers
            SetupConfigurationEventHandlers();

            // Apply all Harmony patches
            ApplyHarmonyPatches();

            // Register for game events
            RegisterGameEvents();

            Log.Info($"{Constants.LogPrefix} Plugin initialization complete");
        }

        public void OnDestroy()
        {
            // Remove configuration event handlers to prevent memory leaks
            PluginConfig.RemoveEventHandlers(debugLogsHandler, projectileGrabbingHandler);
        }

        public void Start()
        {
            SetupRiskOfOptions();
        }

        #region Configuration Management

        private void SetupConfigurationEventHandlers()
        {
            debugLogsHandler = (sender, args) =>
            {
                Log.EnableDebugLogs = PluginConfig.EnableDebugLogs.Value;
            };
            PluginConfig.EnableDebugLogs.SettingChanged += debugLogsHandler;

            projectileGrabbingHandler = (sender, args) =>
            {
                // Configuration updated - patches handle the new value
            };
            PluginConfig.EnableProjectileGrabbing.SettingChanged += projectileGrabbingHandler;
        }

        #endregion

        #region Harmony Patching

        private void ApplyHarmonyPatches()
        {
            Harmony harmony = new Harmony("pwdcat.CleanupSnatcher");
            harmony.PatchAll();
        }

        #endregion

        #region Game Event Management

        private void RegisterGameEvents()
        {
            // Scene changes to handle projectile cleanup
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnSceneChanged;
        }

        private static void OnSceneChanged(UnityEngine.SceneManagement.Scene oldScene, UnityEngine.SceneManagement.Scene newScene)
        {
            // Clean up any projectile references when changing scenes
            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Scene changed from {oldScene.name} to {newScene.name}");
            }
        }

        #endregion

        #region Risk of Options Integration

        private void SetupRiskOfOptions()
        {
            if (!RooInstalled) return;

            ModSettingsManager.SetModDescription("Allows Drifter to grab projectiles from their Cleanup ability.", Constants.PluginGuid, Constants.PluginName);

            try
            {
                byte[] array = System.IO.File.ReadAllBytes(System.IO.Path.Combine(DirectoryName, "icon.png"));
                UnityEngine.Texture2D val = new UnityEngine.Texture2D(256, 256);
                UnityEngine.ImageConversion.LoadImage(val, array);
                ModSettingsManager.SetModIcon(UnityEngine.Sprite.Create(val, new UnityEngine.Rect(0f, 0f, 256f, 256f), new UnityEngine.Vector2(0.5f, 0.5f)));
            }
            catch (Exception)
            {
                // Icon loading failed - continue without icon
            }

            // Add configuration options to the Risk of Options interface
            AddConfigurationOptions();
        }

        private void AddConfigurationOptions()
        {
            if (!RooInstalled) return;

            // Projectile Grabbing Settings
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableProjectileGrabbing));

            // Debug Settings
            ModSettingsManager.AddOption(new CheckBoxOption(PluginConfig.EnableDebugLogs));
        }

        #endregion
    }
}