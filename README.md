# SaveItemTweaks

A [NeosModLoader](https://github.com/zkxs/NeosModLoader) mod for [Neos VR](https://neos.com/) that tweaks for item save/spawn.

## Current features
- Ignore user scale when saving items
- Do not change scale according to the user scale when the item spawn
- Call InventoryItem.Unpack when importing the object (Slots named Holder will no longer be garbage when cloud spawning)

All features are toggleable in the config.

## Installation
1. Install [NeosModLoader](https://github.com/zkxs/NeosModLoader).
2. Place [SaveItemTweaks.dll](https://github.com/hantabaru1014/SaveItemTweaks/releases/latest/download/SaveItemTweaks.dll) into your `nml_mods` folder. This folder should be at `C:\Program Files (x86)\Steam\steamapps\common\NeosVR\nml_mods` for a default install. You can create it if it's missing, or if you launch the game once with NeosModLoader installed it will create the folder for you.
3. Start the game. If you want to verify that the mod is working you can check your Neos logs.
