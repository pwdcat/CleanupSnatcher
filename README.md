# CleanupSnatcher

Allows Drifter to snatch projectiles from the Cleanup ability.

## Credit
Idea by [swuff-star](https://thunderstore.io/package/swuff-star/)

## Configuration

Configurable options that can be adjusted in the config file (`BepInEx/config/pwdcat.CleanupSnatcher.cfg`) or via Risk of Options.

### Projectile Grabbing
- **EnableProjectileGrabbing** (true/false): Enable the Repossess interception system.

### Debug
- **EnableDebugLogs** (true/false): Enable debug logging for interception operations.

## How It Works

1. Drifter uses Cleanup ability - projectile display appears above head
2. While the display is active, press Repossess
3. Cleanup's current projectile is spawned in and cycles to the next (won't use the cleanup ability)
4. This "snatches" the projectile that was displayed, allowing you to grab it

## Compatibility

- Compatible with [Risk of Options](https://thunderstore.io/c/riskofrain2/p/Rune580/Risk_Of_Options/) for configuration
- Compatible with [DrifterRandomizer](https://thunderstore.io/package/Spig/DrifterRandomizer/) by [Spig](https://thunderstore.io/package/Spig/)