using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.AddressableAssets;
using System;
using System.Linq;
using EntityStates.Drifter;

namespace CleanupSnatcher.Patches
{
    // Harmony patches for enabling projectile grabbing functionality
    public static class ProjectilePatches
    {
        // Flag to indicate that the next projectiles should be made grabbable
        // Set when Repossess is intercepted during ToggleVisuals display
        private static bool _shouldMakeNextProjectilesGrabbable = false;

        // Track when ToggleVisuals display is currently active
        private static bool _isToggleVisualsDisplayActive = false;

        // Patch DrifterCleanupController.ToggleVisuals to track display state
        [HarmonyPatch(typeof(DrifterCleanupController), "ToggleVisuals")]
        public class DrifterCleanupController_ToggleVisuals_Patch
        {
            [HarmonyPrefix]
            public static void Prefix(DrifterCleanupController __instance, GameObject target, bool enabled, float duration)
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} ToggleVisuals called - target: {target?.name}, enabled: {enabled}, duration: {duration}");
                }

                // Track display state for Repossess interception
                _isToggleVisualsDisplayActive = enabled;

                // Clear flag when display turns off
                if (!enabled)
                {
                    _shouldMakeNextProjectilesGrabbable = false;
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Display disabled - clearing grab flag");
                    }
                }
            }
        }

        // Patch ProjectileManager.FireProjectileServer to tag projectiles when the flag is set
        // This makes projectiles grabbable when spawned by Cleanup ability after Repossess interception
        [HarmonyPatch(typeof(ProjectileManager), "FireProjectileServer", typeof(FireProjectileInfo), typeof(NetworkConnection), typeof(ushort), typeof(double))]
        public class ProjectileManager_FireProjectileServer_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(FireProjectileInfo fireProjectileInfo)
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} ProjectileManager.FireProjectileServer - prefab: {fireProjectileInfo.projectilePrefab?.name}, position: {fireProjectileInfo.position}, owner: {fireProjectileInfo.owner?.name}");
                }

                if (!PluginConfig.EnableProjectileGrabbing.Value || !_shouldMakeNextProjectilesGrabbable)
                    return;

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} ProjectileManager.FireProjectileServer - flag set for projectile: {fireProjectileInfo.projectilePrefab?.name}");
                }

                // Clear the flag after processing
                _shouldMakeNextProjectilesGrabbable = false;
            }
        }

        // Patch ThrownObjectProjectileController
        // This fixes the NullReferenceException that occurs when grabbed projectiles are thrown
        [HarmonyPatch(typeof(RoR2.Projectile.ThrownObjectProjectileController), "CalculatePassengerFinalPosition")]
        public class ThrownObjectProjectileController_CalculatePassengerFinalPosition_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(RoR2.Projectile.ThrownObjectProjectileController __instance, ref Vector3 position, ref Quaternion rotation)
            {
                if (PluginConfig.EnableDebugLogs.Value && __instance != null && __instance.gameObject != null)
                {
                    Log.Info($"{Constants.LogPrefix} CalculatePassengerFinalPosition called on {__instance.gameObject.name}");
                }

                // Check if the passenger (the thrown object) was made grabbable
                // The SpecialObjectAttributes is on the passenger, not the controller
                GameObject passenger = null;
                try
                {
                    passenger = Traverse.Create(__instance).Field("passenger").GetValue<GameObject>();
                }
                catch (Exception ex)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Failed to get passenger: {ex.Message}");
                    }
                }

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Passenger: {(passenger != null && passenger.gameObject != null ? passenger.name : "null")}");
                }

                if (passenger != null && passenger.gameObject != null)
                {
                    var soa = passenger.GetComponent<SpecialObjectAttributes>();
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} SOA: {soa != null}, grabbable: {soa?.grabbable ?? false}");
                    }

                    if (soa != null && soa.grabbable)
                    {
                        // This passenger was a projectile that was made grabbable - it may not have proper passenger setup
                        // Provide fallback position/rotation
                        position = passenger.transform.position;
                        rotation = passenger.transform.rotation;

                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Provided fallback position/rotation for grabbable projectile passenger {passenger.name}");
                        }
                        return false; // Skip the original method
                    }
                }
                else
                {
                    // Passenger is null or destroyed
                    if (__instance != null && __instance.transform != null)
                    {
                        position = __instance.transform.position;
                        rotation = __instance.transform.rotation;
                    }
                    else
                    {
                        position = Vector3.zero;
                        rotation = Quaternion.identity;
                    }

                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Passenger is null or destroyed, providing fallback position/rotation");
                    }
                    return false; // Skip the original method
                }

                // Let normal thrown objects work as usual
                return true;
            }
        }

        // Patch NetworkServer.Spawn to add SpecialObjectAttributes to projectiles when the flag is set
        // This catches the actual instantiated projectile object
        [HarmonyPatch(typeof(NetworkServer), "Spawn", typeof(GameObject))]
        public class NetworkServer_Spawn_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(GameObject obj)
            {
                if (!PluginConfig.EnableProjectileGrabbing.Value || !_shouldMakeNextProjectilesGrabbable || obj == null)
                    return;

                // Check if this is a projectile (has ProjectileController component)
                if (obj.GetComponent<ProjectileController>() != null)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} NetworkServer.Spawn - making projectile grabbable: {obj.name}");
                    }

                    // Add SpecialObjectAttributes to make the projectile grabbable
                    AddSpecialObjectAttributesToProjectile(obj);
                }
            }
        }

        // Patch GenericSkill.ExecuteIfReady to intercept Repossess when ToggleVisuals display is active
        // When Repossess is pressed while display is ON, force Cleanup to spawn projectiles immediately
        [HarmonyPatch(typeof(GenericSkill), "ExecuteIfReady")]
        public class GenericSkill_ExecuteIfReady_Patch
        {
            [HarmonyPrefix]
            public static bool Prefix(GenericSkill __instance, ref bool __result)
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} ExecuteIfReady called for skill: {__instance.skillDef?.skillName}");
                }

                // Only intercept if projectile grabbing is enabled
                if (!PluginConfig.EnableProjectileGrabbing.Value)
                    return true;

                // Check if this is a Repossess skill
                if (__instance.skillDef?.skillName == "Repossess")
                {
                    // Check if ToggleVisuals display is currently active
                        if (_isToggleVisualsDisplayActive)
                        {
                            // This prevents spamming projectiles when Repossess is on cooldown
                            if (!__instance.CanExecute())
                            {
                                if (PluginConfig.EnableDebugLogs.Value)
                                {
                                    Log.Info($"{Constants.LogPrefix} Repossess pressed but skill is not ready (cooldown) - skipping projectile spawn");
                                }
                                return true; // Let normal execution handle the cooldown case
                            }

                            if (PluginConfig.EnableDebugLogs.Value)
                            {
                                Log.Info($"{Constants.LogPrefix} Repossess pressed while ToggleVisuals display is ACTIVE - spawning projectile directly");
                            }

                            // Execute Cleanup skill to cycle projectile types, then spawn manually
                            SkillLocator skillLocator = __instance.GetComponent<SkillLocator>();
                            if (skillLocator != null && skillLocator.secondary != null)
                            {
                                GenericSkill cleanupSkill = skillLocator.secondary;

                                // Check if bag is already full
                                var bagStateMachine = EntityStateMachine.FindByCustomName(skillLocator.gameObject, "Bag");
                                if (bagStateMachine != null && bagStateMachine.state is EntityStates.Drifter.Bag.BaggedObject)
                                {
                                    if (PluginConfig.EnableDebugLogs.Value)
                                    {
                                        Log.Info($"{Constants.LogPrefix} Bag is already full, skipping projectile spawn");
                                    }
                                    return true; // Bag is full, don't spawn
                                }

                                // Manually cycle the projectile index in DrifterCleanupController
                                var cleanupControllerForCycling = skillLocator.gameObject.GetComponent<DrifterCleanupController>();
                                if (cleanupControllerForCycling != null)
                                {
                                    if (PluginConfig.EnableDebugLogs.Value)
                                    {
                                        Log.Info($"{Constants.LogPrefix} Manually cycling projectile index in DrifterCleanupController");
                                    }

                                    // Manually cycle the prefabIndex to the next projectile type
                                    var indexField = typeof(DrifterCleanupController).GetField("prefabIndex", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                                    if (indexField != null)
                                    {
                                        try
                                        {
                                            // Get the projectile candidate selection to know how many options there are
                                            var selectionField = typeof(DrifterCleanupController).GetField("projectileCandidateSelection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                            if (selectionField != null)
                                            {
                                                var selection = selectionField.GetValue(cleanupControllerForCycling) as WeightedSelection<ProjectileFamily.Candidate>;
                                                if (selection != null)
                                                {
                                                    // Cycle to next index (0 to Count-1)
                                                    int currentIndex = (int)indexField.GetValue(cleanupControllerForCycling);
                                                    int absIndex = Math.Abs(currentIndex); // Handle negative values (not shown)
                                                    int nextIndex = (absIndex + 1) % selection.Count;
                                                    // Keep the sign (negative means not shown)
                                                    if (currentIndex < 0)
                                                    {
                                                        nextIndex = -nextIndex;
                                                    }

                                                    indexField.SetValue(cleanupControllerForCycling, nextIndex);

                                                    if (PluginConfig.EnableDebugLogs.Value)
                                                    {
                                                        Log.Info($"{Constants.LogPrefix} Cycled prefabIndex from {currentIndex} to {nextIndex} (total candidates: {selection.Count})");
                                                    }
                                                }
                                                else
                                                {
                                                    if (PluginConfig.EnableDebugLogs.Value)
                                                    {
                                                        Log.Info($"{Constants.LogPrefix} Could not get projectile candidate selection");
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                // Fallback
                                                int currentIndex = (int)indexField.GetValue(cleanupControllerForCycling);
                                                int absIndex = Math.Abs(currentIndex);
                                                int nextIndex = (absIndex + 1) % 3;
                                                if (currentIndex < 0)
                                                {
                                                    nextIndex = -nextIndex;
                                                }
                                                indexField.SetValue(cleanupControllerForCycling, nextIndex);

                                                if (PluginConfig.EnableDebugLogs.Value)
                                                {
                                                    Log.Info($"{Constants.LogPrefix} Fallback cycling prefabIndex from {currentIndex} to {nextIndex}");
                                                }
                                            }
                                        }
                                        catch (System.Exception ex)
                                        {
                                            if (PluginConfig.EnableDebugLogs.Value)
                                            {
                                                Log.Info($"{Constants.LogPrefix} Failed to cycle prefabIndex: {ex.Message}");
                                            }
                                        }
                                    }
                                    else
                                    {
                                        if (PluginConfig.EnableDebugLogs.Value)
                                        {
                                            Log.Info($"{Constants.LogPrefix} Could not find prefabIndex field in DrifterCleanupController");
                                        }
                                    }
                                }
                                else
                                {
                                    if (PluginConfig.EnableDebugLogs.Value)
                                    {
                                        Log.Info($"{Constants.LogPrefix} Could not find DrifterCleanupController for manual cycling");
                                    }
                                }

                                // Spawn the projectile
                                if (cleanupSkill.stateMachine != null && cleanupSkill.stateMachine.state is EntityStates.Drifter.AimMagicBag aimMagicBagState)
                                {
                                    var traverse = Traverse.Create(aimMagicBagState);

                                    // Get the projectile prefab from the AimMagicBag state
                                    GameObject projectilePrefab = traverse.Field("projectilePrefab").GetValue<GameObject>();

                                    if (projectilePrefab != null)
                                    {
                                        if (PluginConfig.EnableDebugLogs.Value)
                                        {
                                            Log.Info($"{Constants.LogPrefix} Spawning cycled projectile: {projectilePrefab.name}");
                                        }

                                        // Set flag so the spawned projectile will be made grabbable
                                        _shouldMakeNextProjectilesGrabbable = true;

                                        // Get other required values from the AimMagicBag state using reflection
                                        float damageCoefficient = traverse.Field("damageCoefficient").GetValue<float>();
                                        float force = traverse.Field("force").GetValue<float>();
                                        float damageStat = traverse.Field("damageStat").GetValue<float>();

                                        // Get the gameObject from the skill locator
                                        GameObject gameObject = skillLocator.gameObject;

                                        // Debug: Log various position options
                                        if (PluginConfig.EnableDebugLogs.Value)
                                        {
                                            Log.Info($"{Constants.LogPrefix} Drifter position: {gameObject.transform.position}");
                                            Log.Info($"{Constants.LogPrefix} Drifter forward: {gameObject.transform.forward}");
                                            Log.Info($"{Constants.LogPrefix} Camera position: {Camera.main?.transform.position}");
                                            Log.Info($"{Constants.LogPrefix} Camera forward: {Camera.main?.transform.forward}");
                                        }

                                        // Calculate spawn position and direction using the same trajectory logic as normal Cleanup
                                        Vector3 spawnPosition = gameObject.transform.position + gameObject.transform.forward * 2f + Vector3.up * 1f; // fallback
                                        Vector3 projectileDirection = gameObject.transform.forward; // fallback

                                        if (cleanupSkill.stateMachine != null && cleanupSkill.stateMachine.state is EntityStates.Drifter.AimMagicBag trajectoryAimMagicBagState)
                                        {
                                            var trajectoryTraverse = Traverse.Create(trajectoryAimMagicBagState);
                                            var trajectoryInfo = trajectoryTraverse.Field("currentTrajectoryInfo").GetValue();

                                            if (trajectoryInfo != null)
                                            {
                                                try
                                                {
                                                    // Get the final ray from trajectory info (same as normal Cleanup)
                                                    var finalRay = (Ray)trajectoryInfo.GetType().GetField("finalRay").GetValue(trajectoryInfo);
                                                    spawnPosition = finalRay.origin;
                                                    projectileDirection = finalRay.direction;

                                                    if (PluginConfig.EnableDebugLogs.Value)
                                                    {
                                                        Log.Info($"{Constants.LogPrefix} Using AimMagicBag trajectory - position: {spawnPosition}, direction: {projectileDirection}");
                                                    }
                                                }
                                                catch (System.Exception ex)
                                                {
                                                    if (PluginConfig.EnableDebugLogs.Value)
                                                    {
                                                        Log.Info($"{Constants.LogPrefix} Failed to get trajectory data: {ex.Message}, using fallback");
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                if (PluginConfig.EnableDebugLogs.Value)
                                                {
                                                    Log.Info($"{Constants.LogPrefix} No trajectory info available, using fallback values");
                                                }
                                            }
                                        }
                                        else
                                        {
                                            if (PluginConfig.EnableDebugLogs.Value)
                                            {
                                                Log.Info($"{Constants.LogPrefix} Could not find AimMagicBag state, using fallback values");
                                            }
                                        }

                                        if (PluginConfig.EnableDebugLogs.Value)
                                        {
                                            Log.Info($"{Constants.LogPrefix} Final spawn position: {spawnPosition}, direction: {projectileDirection}");
                                        }

                                        FireProjectileInfo fireProjectileInfo = new FireProjectileInfo
                                        {
                                            projectilePrefab = projectilePrefab,
                                            position = spawnPosition,
                                            rotation = Util.QuaternionSafeLookRotation(projectileDirection, Vector3.up),
                                            speedOverride = 10f, // Counteract movement toward drifter to keep stationary
                                            damage = damageCoefficient * damageStat,
                                            owner = gameObject,
                                            crit = false,
                                            damageColorIndex = DamageColorIndex.Default,
                                            force = force,
                                            target = null
                                        };

                                        // Fire the projectile
                                        ProjectileManager.instance.FireProjectile(fireProjectileInfo);

                                        if (PluginConfig.EnableDebugLogs.Value)
                                        {
                                            Log.Info($"{Constants.LogPrefix} Cycled projectile spawned for Repossess grab");
                                        }
                                    }
                                    else
                                    {
                                        if (PluginConfig.EnableDebugLogs.Value)
                                        {
                                            Log.Info($"{Constants.LogPrefix} No projectile prefab found after Cleanup execution");
                                        }
                                    }
                                }
                                else
                                {
                                    if (PluginConfig.EnableDebugLogs.Value)
                                    {
                                        Log.Info($"{Constants.LogPrefix} Could not find AimMagicBag state after Cleanup execution");
                                    }
                                }
                            }
                            else
                            {
                                if (PluginConfig.EnableDebugLogs.Value)
                                {
                                    Log.Info($"{Constants.LogPrefix} Could not find SkillLocator or Cleanup skill");
                                }
                            }

                            // Let Repossess execute normally - it will grab the spawned projectile
                            return true;
                        }
                }

                // Continue with normal execution
                return true;
            }
        }

        // Gets the appropriate icon for a projectile based on its name
        private static UnityEngine.Texture GetProjectileIcon(string projectileName)
        {
            // Try Addressables first (more modern approach)
            string addressableKey = projectileName switch
            {
                "DrifterGrenade" => "RoR2/Base/StickyBomb/texStickyBombIcon.png",
                "DrifterJunkBall" => "RoR2/DLC3/Items/texJunkIcon.png",
                "DrifterToolbotCrate" => "RoR2/DLC3/Items/PowerCube/texPowerCubeIcon.png",
                "DrifterHotSauce" => "RoR2/DLC1/Molotov/texMolotovIcon.png",
                "DrifterKnife" => "RoR2/Base/Dagger/texDaggerIcon.png",
                "DrifterBubbleShield" => "RoR2/Junk/Engi/texEngiShieldSpriteCrosshair.png",
                "DrifterGeode" => "RoR2/DLC2/Elites/EliteAurelionite/texAffixAurelioniteIcon.png",
                "DrifterBarrel" => "RoR2/Base/Common/MiscIcons/texBarrelIcon.png",
                "DrifterBrokenDrone" => "RoR2/Base/Drones/texDrone2Icon.png",
                "DrifterBrokenHAND" => "RoR2/Base/Toolbot/texToolbotIcon.png",
                "DrifterEvilSkull" => "RoR2/Base/AltarSkeleton/texAltarSkeletonBody.png",
                _ => "RoR2/Base/Common/MiscIcons/texMysteryIcon.png"
            };

            if (!string.IsNullOrEmpty(addressableKey))
            {
                try
                {
                    var handle = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<UnityEngine.Texture2D>(addressableKey);
                    var texture = handle.WaitForCompletion();
                    if (texture != null)
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Loaded icon via Addressables: {addressableKey}");
                        }
                        return texture;
                    }
                }
                catch (Exception ex)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Failed to load via Addressables {addressableKey}: {ex.Message}, falling back to LegacyResourcesAPI");
                    }
                }
            }

            return null;
        }

        // Adds SpecialObjectAttributes to a projectile to make it grabbable
        // Uses the same approach as DrifterBossGrab mod
        private static void AddSpecialObjectAttributesToProjectile(GameObject obj)
        {
            if (obj == null)
                return;

            // Cache the object name to avoid repeated property access
            string objName = obj.name;

            // Pre-cache lowercase name for multiple string operations
            string lowerObjName = objName.ToLowerInvariant();

            // Ensure the object has a name for identification and blacklisting
            if (string.IsNullOrEmpty(objName))
            {
                objName = obj.name = "Projectile_" + obj.GetInstanceID();
                lowerObjName = objName.ToLowerInvariant(); // Update cached lowercase
            }

            // For projectiles, use the object itself as the target
            var targetObj = obj;

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Using projectile object {targetObj.name} for SpecialObjectAttributes (original: {objName})");
            }

            // Ensure the target object has NetworkIdentity for networking synchronization
            var networkIdentity = targetObj.GetComponent<NetworkIdentity>();
            if (networkIdentity != null)
            {
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Target {targetObj.name} already has NetworkIdentity: netId = {networkIdentity.netId}");
                }
            }
            else
            {
                // Object doesn't have NetworkIdentity - add it and try to spawn it on the network
                networkIdentity = targetObj.AddComponent<NetworkIdentity>();
                networkIdentity.serverOnly = false;
                networkIdentity.localPlayerAuthority = false;

                // Try to spawn the object on the network
                try
                {
                    if (NetworkServer.active)
                    {
                        NetworkServer.Spawn(targetObj);
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} Successfully spawned projectile {targetObj.name} on network with netId = {networkIdentity.netId}");
                        }
                    }
                    else
                    {
                        if (PluginConfig.EnableDebugLogs.Value)
                        {
                            Log.Info($"{Constants.LogPrefix} NetworkServer not active, cannot spawn projectile {targetObj.name}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Failed to spawn projectile {targetObj.name} on network: {ex.Message}");
                    }
                }

                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Added NetworkIdentity to projectile {targetObj.name}: netId = {networkIdentity.netId}");
                }
            }

            // Check if already has SpecialObjectAttributes
            var existingSoa = targetObj.GetComponent<SpecialObjectAttributes>();
            if (existingSoa != null)
            {
                // Ensure it's configured for grabbing
                if (!existingSoa.grabbable || string.IsNullOrEmpty(existingSoa.breakoutStateMachineName))
                {
                    existingSoa.grabbable = true;
                    existingSoa.breakoutStateMachineName = ""; // Required for BaggedObject to attach the object
                    existingSoa.orientToFloor = true; // Like chests

                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Updated existing SpecialObjectAttributes on projectile {targetObj.name}");
                    }
                }
                return;
            }

            // Add SpecialObjectAttributes to make the projectile grabbable
            var soa = targetObj.AddComponent<SpecialObjectAttributes>();
            soa.renderersToDisable = new System.Collections.Generic.List<Renderer>();
            soa.behavioursToDisable = new System.Collections.Generic.List<MonoBehaviour>();
            soa.collisionToDisable = new System.Collections.Generic.List<GameObject>();
            soa.childObjectsToDisable = new System.Collections.Generic.List<GameObject>();
            soa.pickupDisplaysToDisable = new System.Collections.Generic.List<PickupDisplay>();
            soa.lightsToDisable = new System.Collections.Generic.List<Light>();
            soa.objectsToDetach = new System.Collections.Generic.List<GameObject>();
            soa.childSpecialObjectAttributes = new System.Collections.Generic.List<SpecialObjectAttributes>();
            soa.skillHighlightRenderers = new System.Collections.Generic.List<Renderer>();
            soa.soundEventsToStop = new System.Collections.Generic.List<AkEvent>();
            soa.soundEventsToPlay = new System.Collections.Generic.List<AkEvent>();

            // Calculate scaled attributes based on object size
            var (scaledMass, scaledDurability) = CalculateScaledAttributes(obj, objName);

            // Configure for grabbing (similar to chests) - set ALL properties
            soa.grabbable = true;
            soa.massOverride = scaledMass;
            soa.maxDurability = scaledDurability;
            soa.durability = scaledDurability;
            soa.hullClassification = HullClassification.Human;
            soa.breakoutStateMachineName = "";
            soa.orientToFloor = true;

            // Set name and void properties
            string displayName = objName.Replace("(Clone)", "");
            var numericSuffixPattern = new System.Text.RegularExpressions.Regex(@"\s*\(\d+\)$");
            displayName = numericSuffixPattern.Replace(displayName, "");
            soa.bestName = displayName;
            soa.isVoid = lowerObjName.Contains("void");

            if (PluginConfig.EnableDebugLogs.Value && soa.isVoid)
            {
                Log.Info($"{Constants.LogPrefix} Marked projectile {objName} as void object");
            }

            // Set the icon for the projectile
            soa.portraitIcon = GetProjectileIcon(displayName);
            if (PluginConfig.EnableDebugLogs.Value && soa.portraitIcon != null)
            {
                Log.Info($"{Constants.LogPrefix} Set icon for projectile {objName}");
            }

            // Populate collections with actual components from the projectile
            var renderers = obj.GetComponentsInChildren<Renderer>(false);
            foreach (var renderer in renderers)
            {
                soa.renderersToDisable.Add(renderer);
                // Keep projectile visible - no invisible spawning
            }

            var colliders = obj.GetComponentsInChildren<Collider>(false);
            foreach (var collider in colliders)
            {
                soa.collisionToDisable.Add(collider.gameObject);
            }

            // Find and disable lights
            var lights = obj.GetComponentsInChildren<Light>(false);
            foreach (var light in lights)
            {
                soa.lightsToDisable.Add(light);
            }

            // Find and disable PickupDisplays
            var pickupDisplays = obj.GetComponentsInChildren<PickupDisplay>(false);
            foreach (var pickupDisplay in pickupDisplays)
            {
                soa.pickupDisplaysToDisable.Add(pickupDisplay);
            }

            // Find and disable all enabled projectile behaviors that could cause issues when grabbed
            // Use a scalable approach to find all projectile-related MonoBehaviours
            var allBehaviours = obj.GetComponentsInChildren<MonoBehaviour>(true);

            foreach (var behaviour in allBehaviours)
            {
                if (behaviour == null || !behaviour.enabled) continue;

                // Skip core components that should remain active
                var componentType = behaviour.GetType();
                if (componentType == typeof(NetworkIdentity) ||
                    componentType == typeof(SpecialObjectAttributes) ||
                    componentType == typeof(CharacterBody)) // Don't disable CharacterBody
                {
                    continue;
                }

                // Disable all behaviors that could cause issues when grabbed
                soa.behavioursToDisable.Add(behaviour);
                if (PluginConfig.EnableDebugLogs.Value)
                {
                    Log.Info($"{Constants.LogPrefix} Disabled behavior: {componentType.Name} on {objName}");
                }
            }

            // Set ungrabbable = false on CharacterBody to allow grabbing
            var characterBody = obj.GetComponent<CharacterBody>();
            if (characterBody != null)
            {
                try
                {
                    Traverse.Create(characterBody).Property("ungrabbable").SetValue(false);
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Set ungrabbable=false on CharacterBody for projectile {objName}");
                    }
                }
                catch (System.Exception ex)
                {
                    if (PluginConfig.EnableDebugLogs.Value)
                    {
                        Log.Info($"{Constants.LogPrefix} Failed to set ungrabbable on CharacterBody for projectile {objName}: {ex.Message}");
                    }
                }
            }
        }

        // Calculates scaled mass and durability based on object size
        // Probably should be removed, too much of a bum
        private static (float massOverride, int maxDurability) CalculateScaledAttributes(GameObject obj, string objName)
        {
            float sizeMetric = CalculateObjectSizeMetric(obj);

            const float referenceSize = 10f;
            const float baseMass = 100f;
            const int baseDurability = 8;

            // Calculate scale factor (clamp to reasonable range)
            float scaleFactor = Mathf.Clamp(sizeMetric / referenceSize, 0.5f, 5f);

            // Scale mass and durability
            float scaledMass = baseMass * scaleFactor;
            int scaledDurability = Mathf.RoundToInt(baseDurability * scaleFactor);

            // Ensure minimum values
            scaledMass = Mathf.Max(scaledMass, 25f);
            scaledDurability = Mathf.Max(scaledDurability, 3);

            if (PluginConfig.EnableDebugLogs.Value)
            {
                Log.Info($"{Constants.LogPrefix} Size scaling for {objName}: sizeMetric={sizeMetric:F2}, scaleFactor={scaleFactor:F2}, mass={scaledMass:F0}, durability={scaledDurability}");
            }

            return (scaledMass, scaledDurability);
        }

        // Calculates a size metric for an object based on its colliders
        private static float CalculateObjectSizeMetric(GameObject obj)
        {
            if (obj == null) return 1f;

            float totalSize = 0f;
            var colliders = obj.GetComponentsInChildren<Collider>(false);

            foreach (var collider in colliders)
            {
                if (collider == null || !collider.enabled) continue;

                if (collider is BoxCollider box)
                {
                    var size = box.size;
                    totalSize += size.x * size.y * size.z; // Volume
                }
                else if (collider is SphereCollider sphere)
                {
                    float radius = sphere.radius;
                    totalSize += (4f/3f) * Mathf.PI * radius * radius * radius; // Volume
                }
                else if (collider is CapsuleCollider capsule)
                {
                    float radius = capsule.radius;
                    float height = capsule.height;
                    // Approximate volume for capsule
                    totalSize += Mathf.PI * radius * radius * height;
                }
                else if (collider is MeshCollider mesh)
                {
                    // For mesh colliders, use bounds volume as approximation
                    var bounds = mesh.bounds;
                    totalSize += bounds.size.x * bounds.size.y * bounds.size.z;
                }
            }

            // Ensure minimum size
            totalSize = Mathf.Max(totalSize, 0.1f);

            return totalSize;
        }
    }
}