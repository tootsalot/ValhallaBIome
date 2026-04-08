# ValhallaBiome

ValhallaBiome is a biome vegetation overhaul for Valheim built with BepInEx and Jotunn.

It expands world generation with custom vegetation across the game's biomes, including trees, bushes, rocks, crystals, mushrooms, corals, jungle clutter, ice formations, and other decorative flora. It also includes optional vanilla clutter tuning so biome ground cover can be reduced for a cleaner look and better performance.

## Features

* Adds a large set of custom flora across Meadows, Black Forest, Swamp, Mountain, Plains, Mistlands, Ashlands, Deep North, and Ocean
* Includes decorative bushes, clutter, rocks, crystals, mushrooms, corals, desert plants, jungle foliage, and more
* Optional vanilla clutter reduction with per biome config values
* Optional vanilla vegetation tuning to better fit the custom flora mix
* Uses an external `biomeflora` asset bundle packaged alongside the DLL
* Replaces the original emissive dependency with a custom lava emissive effect

## Requirements

* BepInExPack Valheim
* Jotunn

## Installation

1. Install BepInExPack Valheim
2. Install Jotunn
3. Extract this mod into your Valheim folder or install it with a mod manager
4. Make sure both of these files are present in the same plugin folder:
   * `ValhallaBiome.dll`
   * `biomeflora`

## Configuration

The config file includes options for:

* enabling plugin logging
* reducing vanilla clutter density
* setting grass density values for Meadows, Black Forest, Swamp, and Plains
* adjusting some vanilla vegetation placement values

## Notes

* This mod is focused on decorative biome vegetation
* Some flora uses custom name cleanup and pickable sanitizing to avoid ugly internal prefab names in game
* The asset bundle is loaded externally at runtime, so the bundle file must stay next to the DLL

## Known Issues

* Some support prefabs are still broader than ideal
* Some names and hover text may still need cleanup on edge cases
* Some destructible flora may still need follow up cleanup work
* Some billboard based clutter may need additional polish depending on game version and asset state

## Credits

* Original Biome Flora assets by Horem
* Built with BepInEx and Jotunn
