using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using Jotunn;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace ValhallaBiome
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "valheim.ValhallaBiome";
        public const string PluginName = "ValhallaBiome";
        public const string PluginVersion = "2.0.2";

        private AssetBundle _bundle;

        // -- Config: General --
        private ConfigEntry<bool> _enableLogging;

        // -- Config: Vanilla clutter reduction --
        private ConfigEntry<bool> _reduceVanillaClutter;
        private ConfigEntry<int> _meadowsGrassAmount;
        private ConfigEntry<int> _forestGrassAmount;
        private ConfigEntry<int> _swampGrassAmount;
        private ConfigEntry<int> _plainsGrassAmount;

        // -- Config: Vanilla vegetation tweaks --
        private ConfigEntry<bool> _adjustVanillaVegetation;

        // -- Runtime counters for logging --
        private int _zNetViewsAdded;

        private void Awake()
        {
            CreateConfigs();

            PrefabManager.OnPrefabsRegistered += OnPrefabsRegistered;
            ZoneManager.OnVanillaLocationsAvailable += OnVanillaLocationsAvailable;

            if (_reduceVanillaClutter.Value)
                ZoneManager.OnVanillaClutterAvailable += OnVanillaClutterAvailable;
        }

        private void OnPrefabsRegistered()
        {
            PrefabManager.OnPrefabsRegistered -= OnPrefabsRegistered;

            _zNetViewsAdded = 0; // Reset counter

            LoadBundle();
            if (_bundle == null) return;

            SanitizeBundlePrefabs();
            FixBrokenBillboards();
            FixLavaRockEmissive();
            RegisterSupportPrefabs();
            RegisterAllVegetation();

            _bundle.Unload(false);
            Logger.LogInfo($"{PluginName} loaded: vegetation registered across all biomes.");
        }

        // =====================================================================
        //  CONFIGURATION
        // =====================================================================

        private void CreateConfigs()
        {
            Config.SaveOnConfigSet = true;

            _enableLogging = Config.Bind("1. General", "Enable Logging", false,
                new ConfigDescription("Log vegetation registration to BepInEx console",
                    null, new ConfigurationManagerAttributes { IsAdminOnly = false }));

            _reduceVanillaClutter = Config.Bind("2. Clutter", "Reduce Vanilla Clutter", true,
                new ConfigDescription("Reduce vanilla grass/groundcover density. Client-side only.",
                    null, new ConfigurationManagerAttributes { IsAdminOnly = false }));

            _meadowsGrassAmount = Config.Bind("2. Clutter", "Meadows Grass Amount", 25,
                new ConfigDescription("Grass instances for Meadows (vanilla ~80). Client-side only.",
                    new AcceptableValueRange<int>(5, 80),
                    new ConfigurationManagerAttributes { IsAdminOnly = false }));

            _forestGrassAmount = Config.Bind("2. Clutter", "Forest Grass Amount", 30,
                new ConfigDescription("Grass instances for Black Forest (vanilla ~80). Client-side only.",
                    new AcceptableValueRange<int>(5, 80),
                    new ConfigurationManagerAttributes { IsAdminOnly = false }));

            _swampGrassAmount = Config.Bind("2. Clutter", "Swamp Grass Amount", 35,
                new ConfigDescription("Grass instances for Swamp (vanilla ~80). Client-side only.",
                    new AcceptableValueRange<int>(5, 80),
                    new ConfigurationManagerAttributes { IsAdminOnly = false }));

            _plainsGrassAmount = Config.Bind("2. Clutter", "Plains Grass Amount", 20,
                new ConfigDescription("Grass instances for Plains (vanilla ~80). Client-side only.",
                    new AcceptableValueRange<int>(5, 80),
                    new ConfigurationManagerAttributes { IsAdminOnly = false }));

            _adjustVanillaVegetation = Config.Bind("3. Vanilla Tweaks", "Adjust Vanilla Vegetation", true,
                new ConfigDescription("Tweak vanilla tree/bush/shrub spawn rates to complement custom flora.",
                    null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
        }

        // =====================================================================
        //  VANILLA VEGETATION TWEAKS (from ValhallaBiomes)
        // =====================================================================

        private void OnVanillaLocationsAvailable()
        {
            try
            {
                if (_adjustVanillaVegetation.Value)
                    AdjustVanillaVegetation();
            }
            catch (Exception e)
            {
                Logger.LogWarning($"Exception during vanilla vegetation adjustment: {e}");
            }
            finally
            {
                ZoneManager.OnVanillaLocationsAvailable -= OnVanillaLocationsAvailable;
            }
        }

        private void AdjustVanillaVegetation()
        {
            // Reduce vanilla bush cluster sizes in Plains for more open feel
            ModifyVegetation("Bush01_heath", veg =>
            {
                veg.m_groupSizeMin = 1;
                veg.m_groupSizeMax = 1;
                veg.m_min = 2f;
                veg.m_max = 4f;
            });

            // Thin out Black Forest shrubs for less visual noise
            ModifyVegetation("shrub_2", veg =>
            {
                veg.m_min = 3f;
                veg.m_max = 5f;
            });

            // Spread Ygga shoots to DeepNorth
            foreach (var shoot in new[] { "YggaShoot1", "YggaShoot2", "YggaShoot3" })
            {
                ModifyVegetation(shoot, veg =>
                {
                    veg.m_biome = Heightmap.Biome.Mistlands | Heightmap.Biome.DeepNorth;
                    veg.m_max = 3f;
                });
            }

            // Denser swamp trees
            ModifyVegetation("SwampTree1", veg =>
            {
                veg.m_min = 3f;
                veg.m_max = 5f;
                veg.m_groupSizeMax = 3;
            });

            // More beech in Meadows
            ModifyVegetation("Beech1", veg =>
            {
                veg.m_min = 2f;
                veg.m_max = 4f;
            });

            LogInfo("Vanilla vegetation adjusted");
        }

        // =====================================================================
        //  VANILLA CLUTTER REDUCTION (from ValhallaBiomes)
        // =====================================================================

        private void OnVanillaClutterAvailable()
        {
            try
            {
                SetClutterAmount("grass green", _meadowsGrassAmount.Value);
                SetClutterAmount("grass green short", _meadowsGrassAmount.Value);
                SetClutterAmount("forest groundcover", _forestGrassAmount.Value);
                SetClutterAmount("forest groundcover short brown", _forestGrassAmount.Value);
                SetClutterAmount("swampgrass", _swampGrassAmount.Value);
                SetClutterAmount("heath grass", _plainsGrassAmount.Value);
                SetClutterAmount("heath grass green", _plainsGrassAmount.Value);
                LogInfo("Vanilla clutter adjusted");
            }
            catch (Exception e)
            {
                Logger.LogWarning($"Exception during clutter adjustment: {e}");
            }
            finally
            {
                ZoneManager.OnVanillaClutterAvailable -= OnVanillaClutterAvailable;
            }
        }

        private void ModifyVegetation(string prefabName, Action<ZoneSystem.ZoneVegetation> modifier)
        {
            try
            {
                var veg = ZoneManager.Instance.GetZoneVegetation(prefabName);
                if (veg != null)
                {
                    modifier(veg);
                    LogInfo($"Modified vegetation: {prefabName}");
                }
                else
                {
                    Logger.LogWarning($"Vegetation not found for modification: {prefabName}");
                }
            }
            catch (Exception e)
            {
                Logger.LogWarning($"Exception modifying {prefabName}: {e.Message}");
            }
        }

        private void SetClutterAmount(string clutterName, int amount)
        {
            try
            {
                var clutter = ZoneManager.Instance.GetClutter(clutterName);
                if (clutter != null)
                {
                    clutter.m_enabled = true;
                    clutter.m_amount = amount;
                    LogInfo($"Clutter '{clutterName}' set to {amount}");
                }
            }
            catch (Exception e)
            {
                Logger.LogWarning($"Exception adjusting clutter {clutterName}: {e.Message}");
            }
        }

        private void LogInfo(string message)
        {
            if (_enableLogging.Value)
                Logger.LogMessage(message);
        }

        // =====================================================================
        //  BUNDLE LOADING
        // =====================================================================

        private void LoadBundle()
        {
            string bundlePath = Path.Combine(Path.GetDirectoryName(Info.Location), "biomeflora");
            _bundle = AssetBundle.LoadFromFile(bundlePath);

            if (_bundle == null)
                Logger.LogError($"Failed to load asset bundle at: {bundlePath}");
            else
                Logger.LogInfo($"Asset bundle loaded: {bundlePath}");
        }


        // =====================================================================
        //  PREFAB SANITIZING
        //  1) Replaces raw prefab/file names with readable hover names
        //  2) Forces custom pickables to use vanilla drops or disables them
        // =====================================================================

        private static readonly Dictionary<string, string> CustomNameOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BouncingBet"] = "Bouncing Bet",
            ["BrownrayKnapweed"] = "Brownray Knapweed",
            ["SaintJohnsWort"] = "Saint John's Wort",
            ["LillyValley"] = "Lily of the Valley",
            ["BlackCherryBush"] = "Black Cherry Bush",
            ["DwarfWildRose"] = "Dwarf Wild Rose",
            ["GerberaDaisy"] = "Gerbera Daisy",
            ["ShaggySoldier"] = "Shaggy Soldier",
            ["TangledRibwort"] = "Tangled Ribwort",
            ["DesertThistle"] = "Desert Thistle",
            ["FieldGrass"] = "Field Grass",
            ["SeaGrass"] = "Sea Grass",
            ["RedSeaFern"] = "Red Sea Fern",
            ["SmallPine"] = "Pine"
        };

        private static readonly string[] DecorativePickableTokens =
        {
            "Crystal", "Ore", "BloodIron", "GoldOre", "Mithril", "MoonIron", "Orichalcum", "Vibranium", "ZincOre"
        };

        private static readonly string[] FlowerPickableTokens =
        {
            "Chamomile", "Cornflower", "Poppy", "BouncingBet", "BrownrayKnapweed", "Goldenrod", "Sunroot"
        };

        private void SanitizeBundlePrefabs()
        {
            int renamed = 0;
            int pickablesRemapped = 0;
            int pickablesDisabled = 0;
            int missingScriptsRemoved = 0;

            foreach (var path in _bundle.GetAllAssetNames().Where(p => p.EndsWith(".prefab")))
            {
                var prefab = _bundle.LoadAsset<GameObject>(path);
                if (prefab == null)
                    continue;

                // Remove missing script components (e.g., old HoremvoreAssembly.EmissiveMesh_HS references)
                foreach (var component in prefab.GetComponentsInChildren<Component>(true))
                {
                    if (component is MonoBehaviour mb && mb == null)
                    {
                        DestroyImmediate(component);
                        missingScriptsRemoved++;
                    }
                }

                if (ApplyReadableNames(prefab))
                    renamed++;

                SanitizeCustomItemDrops(prefab);
                SanitizePickables(prefab, ref pickablesRemapped, ref pickablesDisabled);
            }

            Logger.LogInfo($"Sanitized bundle prefabs. Renamed: {renamed}, remapped pickables: {pickablesRemapped}, disabled pickables: {pickablesDisabled}, missing scripts removed: {missingScriptsRemoved}");
        }


        private static readonly string[] DisableCustomItemTokens =
        {
            "plantfibers", "bloodiron", "mithril", "mooniron", "orichalcum", "vibranium", "zincore"
        };

        private void SanitizeCustomItemDrops(GameObject prefab)
        {
            foreach (var itemDrop in prefab.GetComponentsInChildren<ItemDrop>(true))
            {
                SanitizeItemDrop(itemDrop);
            }

            foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null)
                    continue;

                SanitizeStringFields(component, component.GetType(), new HashSet<object>(), prefab.name);
                RemapObjectReferences(component, component.GetType(), new HashSet<object>());
            }
        }

        private void SanitizeItemDrop(ItemDrop itemDrop)
        {
            if (itemDrop?.m_itemData?.m_shared == null)
                return;

            string readableName = GetReadablePrefabName(itemDrop.gameObject.name);
            itemDrop.m_itemData.m_shared.m_name = readableName;
            if (string.IsNullOrWhiteSpace(itemDrop.m_itemData.m_shared.m_description) || itemDrop.m_itemData.m_shared.m_description.StartsWith("$"))
                itemDrop.m_itemData.m_shared.m_description = readableName;

            GameObject vanillaPrefab = RemapCustomItemPrefab(itemDrop.gameObject);
            if (vanillaPrefab != null)
            {
                var vanillaItemDrop = vanillaPrefab.GetComponent<ItemDrop>();
                if (vanillaItemDrop?.m_itemData?.m_shared != null)
                {
                    itemDrop.m_itemData.m_shared = vanillaItemDrop.m_itemData.m_shared;
                    LogInfo($"Remapped item drop to vanilla prefab: {itemDrop.gameObject.name} => {vanillaPrefab.name}");
                }
            }
        }

        private void SanitizeStringFields(object instance, Type instanceType, HashSet<object> visited, string prefabName)
        {
            if (instance == null || instanceType == null || !visited.Add(instance))
                return;

            foreach (var field in instanceType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object value;
                try
                {
                    value = field.GetValue(instance);
                }
                catch
                {
                    continue;
                }

                if (value == null)
                    continue;

                if (field.FieldType == typeof(string))
                {
                    string current = value as string;
                    if (ShouldReplaceName(current, prefabName))
                    {
                        string readableName = GetReadablePrefabName(prefabName);
                        if (!string.Equals(current, readableName, StringComparison.Ordinal))
                            field.SetValue(instance, readableName);
                    }
                    continue;
                }

                if (typeof(System.Collections.IList).IsAssignableFrom(field.FieldType))
                {
                    var list = value as System.Collections.IList;
                    if (list == null) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var entry = list[i];
                        if (entry == null) continue;
                        if (entry is string text)
                        {
                            if (ShouldReplaceName(text, prefabName))
                                list[i] = GetReadablePrefabName(prefabName);
                        }
                        else if (!entry.GetType().IsPrimitive && entry.GetType() != typeof(string) && !typeof(UnityEngine.Object).IsAssignableFrom(entry.GetType()))
                        {
                            SanitizeStringFields(entry, entry.GetType(), visited, prefabName);
                        }
                    }
                    continue;
                }

                if (!field.FieldType.IsPrimitive && field.FieldType != typeof(string) && !typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType))
                {
                    SanitizeStringFields(value, field.FieldType, visited, prefabName);
                }
            }
        }

        private void RemapObjectReferences(object instance, Type instanceType, HashSet<object> visited)
        {
            if (instance == null || instanceType == null || !visited.Add(instance))
                return;

            foreach (var field in instanceType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object value;
                try
                {
                    value = field.GetValue(instance);
                }
                catch
                {
                    continue;
                }

                if (value == null)
                    continue;

                if (field.FieldType == typeof(GameObject))
                {
                    var go = value as GameObject;
                    var remapped = RemapCustomItemPrefab(go);
                    if (!ReferenceEquals(go, remapped))
                        field.SetValue(instance, remapped);
                    continue;
                }

                if (typeof(System.Collections.IList).IsAssignableFrom(field.FieldType))
                {
                    var list = value as System.Collections.IList;
                    if (list == null) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var entry = list[i];
                        if (entry == null) continue;

                        if (entry is GameObject listGo)
                        {
                            var remapped = RemapCustomItemPrefab(listGo);
                            if (!ReferenceEquals(listGo, remapped))
                                list[i] = remapped;
                            continue;
                        }

                        PatchNestedObject(entry, list, i, visited);
                    }
                    continue;
                }

                if (!field.FieldType.IsPrimitive && field.FieldType != typeof(string) && !typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType))
                {
                    RemapObjectReferences(value, field.FieldType, visited);
                }
            }
        }

        private void PatchNestedObject(object entry, System.Collections.IList list, int index, HashSet<object> visited)
        {
            if (entry is GameObject entryGo)
            {
                var remappedGo = RemapCustomItemPrefab(entryGo);
                if (!ReferenceEquals(entryGo, remappedGo))
                    list[index] = remappedGo;
                return;
            }

            var type = entry.GetType();
            foreach (var nestedField in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object nestedValue;
                try
                {
                    nestedValue = nestedField.GetValue(entry);
                }
                catch
                {
                    continue;
                }

                if (nestedValue is GameObject nestedGo)
                {
                    var remapped = RemapCustomItemPrefab(nestedGo);
                    if (!ReferenceEquals(nestedGo, remapped))
                        nestedField.SetValue(entry, remapped);
                }
                else if (nestedValue != null && !nestedField.FieldType.IsPrimitive && nestedField.FieldType != typeof(string) && !typeof(UnityEngine.Object).IsAssignableFrom(nestedField.FieldType))
                {
                    RemapObjectReferences(nestedValue, nestedField.FieldType, visited);
                }
            }
            list[index] = entry;
        }

        private GameObject RemapCustomItemPrefab(GameObject prefab)
        {
            if (prefab == null)
                return null;

            string name = prefab.name;
            if (!name.StartsWith("item_", StringComparison.OrdinalIgnoreCase) || !name.EndsWith("_bf", StringComparison.OrdinalIgnoreCase))
                return prefab;

            string clean = StripBundleSuffix(name).Substring("item_".Length);
            string fallbackName = null;

            if (clean.IndexOf("plantfibers", StringComparison.OrdinalIgnoreCase) >= 0)
                fallbackName = "Wood";
            else if (clean.IndexOf("flower", StringComparison.OrdinalIgnoreCase) >= 0 || clean.IndexOf("petal", StringComparison.OrdinalIgnoreCase) >= 0)
                fallbackName = "Dandelion";
            else if (clean.IndexOf("moss", StringComparison.OrdinalIgnoreCase) >= 0 || clean.IndexOf("wort", StringComparison.OrdinalIgnoreCase) >= 0)
                fallbackName = "Thistle";
            else if (DisableCustomItemTokens.Any(t => clean.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0))
                fallbackName = null;

            return string.IsNullOrEmpty(fallbackName) ? null : FindVanillaPrefab(fallbackName);
        }

        private bool ApplyReadableNames(GameObject prefab)
        {
            string readableName = GetReadablePrefabName(prefab.name);
            bool changed = false;

            foreach (var behaviour in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || behaviour is ItemDrop)
                    continue;

                changed |= TrySetReadableName(behaviour, "m_name", readableName, prefab.name);
                changed |= TrySetReadableName(behaviour, "m_overrideName", readableName, prefab.name, force: true);
            }

            return changed;
        }

        private bool TrySetReadableName(object target, string fieldName, string readableName, string prefabName, bool force = false)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(string))
                return false;

            string current = field.GetValue(target) as string;
            if (!force && !ShouldReplaceName(current, prefabName))
                return false;

            if (string.Equals(current, readableName, StringComparison.Ordinal))
                return false;

            field.SetValue(target, readableName);
            return true;
        }

        private static bool ShouldReplaceName(string current, string prefabName)
        {
            if (string.IsNullOrWhiteSpace(current))
                return true;

            if (current.StartsWith("$", StringComparison.Ordinal))
                return false;

            return string.Equals(current, prefabName, StringComparison.OrdinalIgnoreCase)
                || current.IndexOf("_BF", StringComparison.OrdinalIgnoreCase) >= 0
                || current.StartsWith("Pickable_", StringComparison.OrdinalIgnoreCase)
                || current.StartsWith("Prop_", StringComparison.OrdinalIgnoreCase)
                || current.StartsWith("Clutter_", StringComparison.OrdinalIgnoreCase);
        }

        private void SanitizePickables(GameObject prefab, ref int remapped, ref int disabled)
        {
            string readableName = GetReadablePrefabName(prefab.name);
            string fallbackItemName = GetVanillaPickableFallback(prefab.name);
            GameObject fallbackItemPrefab = string.IsNullOrEmpty(fallbackItemName) ? null : FindVanillaPrefab(fallbackItemName);

            foreach (var pickable in prefab.GetComponentsInChildren<Pickable>(true))
            {
                pickable.m_overrideName = readableName;

                if (fallbackItemPrefab != null)
                {
                    pickable.m_itemPrefab = fallbackItemPrefab;
                    remapped++;
                }
                else
                {
                    DestroyImmediate(pickable);
                    disabled++;
                }
            }

            foreach (var pickableItem in prefab.GetComponentsInChildren<PickableItem>(true))
            {
                TrySetReadableName(pickableItem, "m_name", readableName, prefab.name, force: true);

                // Newer Valheim builds no longer expose the RandomItem type in a way this project
                // can construct directly at compile time. Since the goal is to prevent modded
                // pickables from yielding non-vanilla loot, disable PickableItem components entirely.
                // Standard Pickable components above are still remapped to vanilla items when possible.
                DestroyImmediate(pickableItem);
                disabled++;
            }
        }

        private GameObject FindVanillaPrefab(string prefabName)
        {
            if (string.IsNullOrWhiteSpace(prefabName))
                return null;

            var prefab = PrefabManager.Instance.GetPrefab(prefabName);
            if (prefab != null)
                return prefab;

            if (ZNetScene.instance != null)
            {
                prefab = ZNetScene.instance.GetPrefab(prefabName);
                if (prefab != null)
                    return prefab;
            }

            if (ObjectDB.instance != null)
                return ObjectDB.instance.GetItemPrefab(prefabName);

            return null;
        }

        private static string GetVanillaPickableFallback(string prefabName)
        {
            string clean = StripBundleSuffix(prefabName);

            if (clean.StartsWith("Pickable_", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring("Pickable_".Length);

            if (clean.IndexOf("SaintJohnsWort", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Thistle";

            if (FlowerPickableTokens.Any(token => clean.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
                return "Dandelion";

            if (DecorativePickableTokens.Any(token => clean.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
                return null;

            return null;
        }

        private static string GetReadablePrefabName(string prefabName)
        {
            string clean = StripBundleSuffix(prefabName);

            if (clean.StartsWith("Pickable_", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring("Pickable_".Length);

            if (clean.StartsWith("Prop_", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring("Prop_".Length);

            if (clean.StartsWith("Clutter", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring("Clutter".Length).TrimStart('_');

            string normalizedKey = Regex.Replace(clean, @"(_\d+)+$", string.Empty);
            if (CustomNameOverrides.TryGetValue(normalizedKey, out var overridden))
                return overridden;

            var tokens = Regex.Split(clean, "_+")
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .SelectMany(SplitCamelCase)
                .Where(t => !IsNoiseToken(t))
                .ToList();

            if (tokens.Count == 0)
                return prefabName.Replace("_BF", string.Empty).Replace('_', ' ').Trim();

            MoveColorToFront(tokens, "Crystal");
            MoveColorToFront(tokens, "Mushroom");

            return string.Join(" ", tokens);
        }

        private static string StripBundleSuffix(string value)
        {
            return value.EndsWith("_BF", StringComparison.OrdinalIgnoreCase)
                ? value.Substring(0, value.Length - 3)
                : value;
        }

        private static IEnumerable<string> SplitCamelCase(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                yield break;

            foreach (Match match in Regex.Matches(value, @"[A-Z]?[a-z]+|[A-Z]+(?![a-z])|\d+"))
            {
                yield return match.Value;
            }
        }

        private static bool IsNoiseToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return true;

            if (int.TryParse(token, out _))
                return true;

            switch (token)
            {
                case "BF":
                case "Young":
                case "Mature":
                case "Old":
                case "Aged":
                case "Ancient":
                case "Single":
                case "Group":
                case "Detailed":
                case "Cross":
                case "Small":
                case "Tiny":
                    return true;
                default:
                    return false;
            }
        }

        private static void MoveColorToFront(List<string> tokens, string anchor)
        {
            int anchorIndex = tokens.FindIndex(t => string.Equals(t, anchor, StringComparison.OrdinalIgnoreCase));
            if (anchorIndex <= 0 || anchorIndex >= tokens.Count - 1)
                return;

            string next = tokens[anchorIndex + 1];
            if (!IsColorToken(next))
                return;

            tokens.RemoveAt(anchorIndex + 1);
            tokens.Insert(0, next);
        }

        private static bool IsColorToken(string token)
        {
            switch (token)
            {
                case "Blue":
                case "Teal":
                case "Purple":
                case "Green":
                case "Orange":
                case "Red":
                case "Pink":
                case "White":
                case "Yellow":
                case "Chartreuse":
                    return true;
                default:
                    return false;
            }
        }

        private void FixBrokenBillboards()
        {
            int lodGroupsPatched = 0;
            int billboardsDisabled = 0;

            foreach (var path in _bundle.GetAllAssetNames().Where(p => p.EndsWith(".prefab")))
            {
                var prefab = _bundle.LoadAsset<GameObject>(path);
                if (prefab == null)
                    continue;

                foreach (var lodGroup in prefab.GetComponentsInChildren<LODGroup>(true))
                {
                    var lods = lodGroup.GetLODs();
                    bool changed = false;
                    var rebuilt = new List<LOD>();

                    foreach (var lod in lods)
                    {
                        var filtered = lod.renderers
                            .Where(r => r != null && !(r is BillboardRenderer))
                            .ToArray();

                        if (filtered.Length != lod.renderers.Length)
                            changed = true;

                        if (filtered.Length > 0)
                            rebuilt.Add(new LOD(lod.screenRelativeTransitionHeight, filtered));
                    }

                    if (changed && rebuilt.Count > 0)
                    {
                        lodGroup.SetLODs(rebuilt.ToArray());
                        lodGroup.RecalculateBounds();
                        lodGroupsPatched++;
                    }
                }

                foreach (var billboard in prefab.GetComponentsInChildren<BillboardRenderer>(true))
                {
                    billboard.enabled = false;
                    billboardsDisabled++;
                }
            }

            Logger.LogInfo($"Patched broken billboards. LODGroups updated: {lodGroupsPatched}, BillboardRenderers disabled: {billboardsDisabled}");
        }

        // =====================================================================
        //  EMISSIVE MESH FIX (lava rocks)
        // =====================================================================

        private static readonly string[] LavaRockPrefabs =
        {
            "LavaBlock_1_BF", "LavaBlock_2_BF", "LavaBlock_3_BF", "LavaBlock_4_BF", "LavaBlock_5_BF",
            "LavaPillar_1_BF", "LavaPillar_2_BF", "LavaPillar_3_BF",
            "LavaPillar_4_BF", "LavaPillar_5_BF", "LavaPillar_6_BF",
            "LavaPlate_1_BF", "LavaPlate_2_BF", "LavaPlate_3_BF", "LavaPlate_4_BF", "LavaPlate_5_BF",
            "LavaRock_1_BF", "LavaRock_2_BF", "LavaRock_3_BF", "LavaRock_4_BF"
        };

        private void FixLavaRockEmissive()
        {
            foreach (var name in LavaRockPrefabs)
            {
                var prefab = _bundle.LoadAsset<GameObject>(name);
                if (prefab == null) continue;

                // Add replacement emissive effect to renderers with emission
                foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.sharedMaterial != null &&
                        renderer.sharedMaterial.HasProperty("_EmissionColor") &&
                        renderer.gameObject.GetComponent<EmissiveMeshEffect>() == null)
                    {
                        renderer.gameObject.AddComponent<EmissiveMeshEffect>();
                    }
                }
            }

            Logger.LogInfo($"Patched EmissiveMesh on {LavaRockPrefabs.Length} lava rock prefabs.");
        }

        // =====================================================================
        //  SUPPORT PREFABS (not spawned as vegetation - referenced by other prefabs)
        // =====================================================================

        private void RegisterSupportPrefabs()
        {
            int count = 0;

            // Register all non-vegetation prefabs the bundle contains.
            // This ensures tree logs/stumps, item drops, SFX, mocks, etc. are
            // present in ZNetScene so vegetation prefabs can reference them.
            string[] allPaths = _bundle.GetAllAssetNames()
                .Where(p => p.EndsWith(".prefab"))
                .ToArray();

            // Vegetation prefab paths are registered via AddCustomVegetation below,
            // so we only need PrefabManager for everything else.
            var vegNames = GetAllVegetationPrefabNames();

            foreach (var path in allPaths)
            {
                var prefab = _bundle.LoadAsset<GameObject>(path);
                if (prefab == null) continue;
                if (vegNames.Contains(prefab.name)) continue; // handled by vegetation registration

                try
                {
                    PrefabManager.Instance.AddPrefab(new CustomPrefab(prefab, true));
                    count++;
                }
                catch (System.Exception ex)
                {
                    Logger.LogWarning($"Failed to register support prefab {prefab.name}: {ex.Message}");
                }
            }

            Logger.LogInfo($"Registered {count} support prefabs (logs, stumps, items, mocks, sfx).");
        }

        // =====================================================================
        //  VEGETATION REGISTRATION
        // =====================================================================

        private void RegisterAllVegetation()
        {
            int count = 0;
            count += RegisterMeadows();
            count += RegisterBlackForest();
            count += RegisterSwamp();
            count += RegisterMountain();
            count += RegisterPlains();
            count += RegisterMistlands();
            count += RegisterAshlands();
            count += RegisterDeepNorth();
            count += RegisterOcean();
            Logger.LogInfo($"Registered {count} vegetation prefabs across all biomes. ZNetViews added: {_zNetViewsAdded}");
        }

        // ----- HELPERS -----

       private int AddVeg(string name, Heightmap.Biome biome, float min, float max,
            int grpMin = 1, int grpMax = 1, float grpRad = 0f,
            float sclMin = 1f, float sclMax = 1f,
            float altMin = 1f, float altMax = 1000f,
            float tiltMax = 35f,
            float oceanMin = 0f, float oceanMax = 0f,
            bool blockCheck = true,
            bool inForest = false, float forestMin = 0f, float forestMax = 0f)
        {
            var prefab = _bundle.LoadAsset<GameObject>(name);
            if (prefab == null)
            {
                Logger.LogWarning($"Vegetation prefab not found: {name}");
                return 0;
            }

            // Ensure ZNetView is present for networked vegetation
            if (prefab.GetComponent<ZNetView>() == null)
            {
                prefab.AddComponent<ZNetView>();
                _zNetViewsAdded++;
                LogInfo($"Added ZNetView to vegetation prefab: {name}");
            }

            var config = new VegetationConfig
            {
                Biome = biome,
                BlockCheck = blockCheck,
                Min = min,
                Max = max,
                GroupSizeMin = grpMin,
                GroupSizeMax = grpMax,
                GroupRadius = grpRad,
                ScaleMin = sclMin,
                ScaleMax = sclMax,
                MinAltitude = altMin,
                MaxAltitude = altMax,
                MaxTilt = tiltMax,
                MinOceanDepth = oceanMin,
                MaxOceanDepth = oceanMax,
                InForest = inForest
            };

            try
            {
                ZoneManager.Instance.AddCustomVegetation(new CustomVegetation(prefab, true, config));
                return 1;
            }
            catch (System.Exception ex)
            {
                Logger.LogWarning($"Failed to register vegetation {name}: {ex.Message}");
                return 0;
            }
        }
        private int AddGroup(string[] names, Heightmap.Biome biome, float min, float max,
            int grpMin = 1, int grpMax = 1, float grpRad = 0f,
            float sclMin = 1f, float sclMax = 1f,
            float altMin = 1f, float altMax = 1000f,
            float tiltMax = 35f,
            float oceanMin = 0f, float oceanMax = 0f,
            bool blockCheck = true,
            bool inForest = false, float forestMin = 0f, float forestMax = 0f)
        {
            int count = 0;
            foreach (var name in names)
                count += AddVeg(name, biome, min, max, grpMin, grpMax, grpRad,
                    sclMin, sclMax, altMin, altMax, tiltMax, oceanMin, oceanMax,
                    blockCheck, inForest, forestMin, forestMax);
            return count;
        }

        // =====================================================================
        //  MEADOWS
        // =====================================================================

        private int RegisterMeadows()
        {
            const Heightmap.Biome B = Heightmap.Biome.Meadows;
            int c = 0;

            // -- Trees (common) --
            c += AddGroup(new[] {
                "AspenTree_BF", "AspenYoung_BF", "AspenMature_BF", "AspenSmall_BF",
                "LindenTree_BF", "LindenYoung_BF", "LindenSmall_BF",
                "MapleTree_BF", "MapleYoung_BF", "MapleSmall_BF",
                "RedMapleTree_BF", "RedMapleYoung_BF", "RedMapleSmall_BF"
            }, B, min: 1, max: 3, grpMin: 1, grpMax: 3, grpRad: 10f,
               sclMin: 0.8f, sclMax: 1.2f);

            // -- Trees (large, rare) --
            c += AddGroup(new[] {
                "AspenOld_BF", "AspenAged_BF", "AspenAncient_BF",
                "LindenMature_BF", "LindenOld_BF", "LindenAged_BF", "LindenAncient_BF",
            }, B, min: 0, max: 1, grpMin: 1, grpMax: 1,
               sclMin: 0.9f, sclMax: 1.3f);

            // -- Bushes --
            c += AddGroup(new[] {
                "BushGreen_1_BF", "BushGreen_2_BF", "BushGreen_3_BF", "BushGreen_4_BF", "BushGreen_5_BF",
                "BlackCherryBush_1_BF", "BlackCherryBush_2_BF", "BlackCherryBush_3_BF",
                "BlackCherryBush_4_BF", "BlackCherryBush_5_BF",
                "DwarfWildRose_1_BF", "DwarfWildRose_2_BF", "DwarfWildRose_3_BF",
                "MapleBush_1_BF", "MapleBush_2_BF", "MapleBush_3_BF"
            }, B, min: 1, max: 3, grpMin: 1, grpMax: 3, grpRad: 5f,
               sclMin: 0.7f, sclMax: 1.3f);

            // -- Flowers & small plants --
            c += AddGroup(new[] {
                "Chrysanthemum_Group_BF", "Chrysanthemum_Single_BF",
                "Dahlia_Group_BF", "Dahlia_Single_BF",
                "Daisy_Group_BF", "Daisy_Single_BF",
                "GerberaDaisy_Group_BF", "GerberaDaisy_Single_BF",
                "LillyValley_01_BF", "LillyValley_02_BF",
                "Verbena_BF",
                "ShaggySoldier_01_BF", "ShaggySoldier_02_BF", "ShaggySoldier_03_BF"
            }, B, min: 1, max: 3, grpMin: 2, grpMax: 5, grpRad: 4f,
               sclMin: 0.8f, sclMax: 1.2f);

            // -- Meadow grasses --
            c += AddGroup(new[] {
                "Grass_Meadow_1_01_BF", "Grass_Meadow_1_02_BF", "Grass_Meadow_1_03_BF",
                "Grass_Meadow_1_04_BF", "Grass_Meadow_1_05_BF", "Grass_Meadow_1_06_BF",
                "Grass_Meadow_1_01_Cross_BF", "Grass_Meadow_1_02_Cross_BF", "Grass_Meadow_1_03_Cross_BF",
                "Grass_Meadow_1_01_Detailed_BF", "Grass_Meadow_1_02_Detailed_BF",
                "Grass_Meadow_2_01_BF", "Grass_Meadow_2_02_BF", "Grass_Meadow_2_03_BF",
                "Grass_Meadow_2_04_BF", "Grass_Meadow_2_05_BF", "Grass_Meadow_2_06_BF",
                "Grass_Meadow_2_01_Cross_BF", "Grass_Meadow_2_02_Cross_BF",
                "Grass_Meadow_2_01_Detailed_BF", "Grass_Meadow_2_02_Detailed_BF",
                "Grass_Meadow_3_01_BF", "Grass_Meadow_3_02_BF",
                "Grass_Meadow_3_03_BF", "Grass_Meadow_3_04_BF",
                "FieldGrass_1_BF", "FieldGrass_2_BF", "FieldGrass_3_BF", "FieldGrass_4_BF"
            }, B, min: 2, max: 5, grpMin: 3, grpMax: 8, grpRad: 5f,
               sclMin: 0.8f, sclMax: 1.2f, blockCheck: false);

            // -- Forest floor plants --
            c += AddGroup(new[] {
                "Plant_Forest_1_01_BF", "Plant_Forest_1_02_BF",
                "Plant_Forest_1_03_BF", "Plant_Forest_1_04_BF"
            }, B, min: 1, max: 3, grpMin: 2, grpMax: 4, grpRad: 4f,
               sclMin: 0.8f, sclMax: 1.2f);

            // -- Pickable flowers --
            c += AddGroup(new[] {
                "Pickable_Chamomile_BF", "Pickable_Chamomile_Small_BF",
                "Pickable_Cornflower_BF", "Pickable_Cornflower_Small_BF",
                "Pickable_Poppy_BF", "Pickable_Poppy_Small_BF",
                "Pickable_BouncingBet_BF",
                "Pickable_BrownrayKnapweed_BF", "Pickable_BrownrayKnapweed_Small_BF",
                "Pickable_Goldenrod_BF", "Pickable_Goldenrod_Small_BF"
            }, B, min: 1, max: 2, grpMin: 1, grpMax: 3, grpRad: 4f,
               sclMin: 0.8f, sclMax: 1.1f);

            return c;
        }

        // =====================================================================
        //  BLACK FOREST
        // =====================================================================

        private int RegisterBlackForest()
        {
            const Heightmap.Biome B = Heightmap.Biome.BlackForest;
            int c = 0;

            // -- Conifer trees --
            c += AddGroup(new[] {
                "EasternPine_BF", "EasternYoung_BF", "EasternMature_BF",
                "PonderossaPine_BF", "PonderossaYoung_BF", "PonderossaMature_BF"
            }, B, min: 1, max: 3, grpMin: 1, grpMax: 3, grpRad: 10f,
               sclMin: 0.8f, sclMax: 1.2f, inForest: true, forestMin: 1.0f, forestMax: 1.15f);

            // -- Large conifers (rare) --
            c += AddGroup(new[] {
                "EasternAged_BF", "EasternOld_BF", "EasternAncient_BF",
                "PonderossaAged_BF", "PonderossaOld_BF", "PonderossaAncient_BF"
            }, B, min: 0, max: 1, grpMin: 1, grpMax: 1,
               sclMin: 0.9f, sclMax: 1.3f, inForest: true, forestMin: 1.0f, forestMax: 1.15f);

            // -- Oaks --
            c += AddGroup(new[] {
                "OakAged_BF", "OakOld_BF", "OakAncient_BF"
            }, B, min: 0, max: 1, grpMin: 1, grpMax: 1,
               sclMin: 0.9f, sclMax: 1.3f);

            // -- Understory bushes --
            c += AddGroup(new[] {
                "SmallPine_1_BF",
                "Ekage_1_BF", "Ekage_2_BF", "Ekage_3_BF",
                "Phriscus_1_BF", "Phriscus_2_BF",
                "Shosie_1_BF", "Shosie_2_BF", "Shosie_3_BF"
            }, B, min: 1, max: 3, grpMin: 1, grpMax: 3, grpRad: 5f,
               sclMin: 0.7f, sclMax: 1.3f);

            // -- Ferns, ivy, forest plants --
            c += AddGroup(new[] {
                "Fern_01_BF", "Fern_02_BF", "Fern_03_BF",
                "Ivy_01_BF", "Ivy_02_BF", "Ivy_03_BF",
                "Plant_Forest_2_01_BF", "Plant_Forest_2_02_BF", "Plant_Forest_3_01_BF"
            }, B, min: 1, max: 3, grpMin: 2, grpMax: 5, grpRad: 4f,
               sclMin: 0.8f, sclMax: 1.2f);

            // -- Forest grasses --
            c += AddGroup(new[] {
                "Grass_Forest_01_BF", "Grass_Forest_02_BF", "Grass_Forest_03_BF"
            }, B, min: 2, max: 5, grpMin: 3, grpMax: 8, grpRad: 5f,
               sclMin: 0.8f, sclMax: 1.2f, blockCheck: false);

            // -- Glowing mushrooms (ambient) --
            c += AddGroup(new[] {
                "GlowingMushroom_Green_1_BF", "GlowingMushroom_Green_2_BF",
                "GlowingMushroom_Green_3_BF", "GlowingMushroom_Green_4_BF",
                "GlowingMushroom_Blue_1_BF", "GlowingMushroom_Blue_2_BF",
                "GlowingMushroom_Blue_3_BF", "GlowingMushroom_Blue_4_BF"
            }, B, min: 1, max: 2, grpMin: 2, grpMax: 4, grpRad: 3f,
               sclMin: 0.5f, sclMax: 1.2f, inForest: true, forestMin: 1.0f, forestMax: 1.15f);

            // -- Pickable --
            c += AddVeg("Pickable_SaintJohnsWort_BF", B, min: 1, max: 2,
               grpMin: 1, grpMax: 2, grpRad: 3f, sclMin: 0.8f, sclMax: 1.1f);

            return c;
        }

        // =====================================================================
        //  SWAMP
        // =====================================================================

        private int RegisterSwamp()
        {
            const Heightmap.Biome B = Heightmap.Biome.Swamp;
            int c = 0;

            // -- Willow trees --
            c += AddGroup(new[] {
                "WillowTree_BF", "WillowYoung_BF", "WillowMature_BF"
            }, B, min: 1, max: 3, grpMin: 1, grpMax: 2, grpRad: 10f,
               sclMin: 0.8f, sclMax: 1.2f, altMax: 50f);

            // -- Large willows (rare) --
            c += AddGroup(new[] {
                "WillowAged_BF", "WillowOld_BF", "WillowAncient_BF"
            }, B, min: 0, max: 1, grpMin: 1, grpMax: 1,
               sclMin: 0.9f, sclMax: 1.3f, altMax: 50f);

            // -- Bushes --
            c += AddGroup(new[] {
                "WillowBush_1_BF", "WillowBush_2_BF", "WillowBush_3_BF", "WillowBush_4_BF",
                "DeadBush_1_BF", "DeadBush_2_BF", "DeadBush_3_BF",
                "TangledRibwort_1_BF", "TangledRibwort_2_BF",
                "MercysMoss_BF",
                "GraveGilliflower_1_BF", "GraveGilliflower_2_BF"
            }, B, min: 1, max: 3, grpMin: 1, grpMax: 3, grpRad: 5f,
               sclMin: 0.7f, sclMax: 1.3f, altMax: 50f);

            // -- Giant skulls --
            c += AddGroup(new[] {
                "GiantSkull_1_BF", "GiantSkull_2_BF", "GiantSkull_3_BF"
            }, B, min: 0, max: 1, grpMin: 1, grpMax: 1,
               sclMin: 0.8f, sclMax: 1.5f, altMax: 50f);

            // -- Swamp mushrooms --
            c += AddGroup(new[] {
                "GlowingMushroom_Chartreuse_1_BF", "GlowingMushroom_Chartreuse_2_BF",
                "GlowingMushroom_Chartreuse_3_BF", "GlowingMushroom_Chartreuse_4_BF",
                "GiantMushroom_Green_1_BF", "GiantMushroom_Green_2_BF", "GiantMushroom_Green_3_BF"
            }, B, min: 1, max: 2, grpMin: 1, grpMax: 3, grpRad: 4f,
               sclMin: 0.5f, sclMax: 1.3f, altMax: 50f);

            return c;
        }

        // =====================================================================
        //  MOUNTAIN
        // =====================================================================

        private int RegisterMountain()
        {
            const Heightmap.Biome B = Heightmap.Biome.Mountain;
            int c = 0;

            // -- Crimean pines --
            c += AddGroup(new[] {
                "CrimeanPine_BF", "CrimeanYoung_BF", "CrimeanSmall_BF", "CrimeanMature_BF",
                "CrimeanAged_BF", "CrimeanOld_BF", "CrimeanAncient_BF"
            }, B, min: 1, max: 2, grpMin: 1, grpMax: 2, grpRad: 10f,
               sclMin: 0.8f, sclMax: 1.2f, altMin: 100f, tiltMax: 30f);

            // -- Claw rocks --
            c += AddGroup(new[] {
                "ClawRock_1_BF", "ClawRock_2_BF", "ClawRock_3_BF", "ClawRock_4_BF",
                "ClawRock_5_BF", "ClawRock_6_BF", "ClawRock_7_BF", "ClawRock_8_BF",
                "ClawRock_9_BF", "ClawRock_10_BF", "ClawRock_11_BF", "ClawRock_12_BF", "ClawRock_13_BF",
                "ClawCrackedRock_1_BF", "ClawCrackedRock_2_BF",
                "ClawRockBase_1_BF", "ClawRockBase_2_BF", "ClawRockBase_3_BF",
                "ClawRockBase_4_BF", "ClawRockBase_5_BF",
                "ClawRockS_1_BF", "ClawRockS_2_BF", "ClawRockS_3_BF",
                "ClawRockSmall_1_BF", "ClawRockSmall_2_BF", "ClawRockSmall_3_BF", "ClawRockSmall_4_BF"
            }, B, min: 0, max: 2, grpMin: 1, grpMax: 2, grpRad: 6f,
               sclMin: 0.8f, sclMax: 1.5f, altMin: 100f, tiltMax: 40f);

            // -- Ice rocks --
            c += AddGroup(new[] {
                "IceRock_1_BF", "IceRock_2_BF", "IceRock_3_BF", "IceRock_4_BF", "IceRock_5_BF",
                "IceRock_6_BF", "IceRock_7_BF", "IceRock_8_BF", "IceRock_9_BF", "IceRock_10_BF"
            }, B, min: 0, max: 2, grpMin: 1, grpMax: 1,
               sclMin: 0.8f, sclMax: 1.5f, altMin: 150f, tiltMax: 40f);

            // -- Crystals --
            c += AddGroup(new[] {
                "Crystal_Blue_1_BF", "Crystal_Blue_2_BF", "Crystal_Blue_3_BF",
                "Crystal_Blue_4_BF", "Crystal_Blue_5_BF", "Crystal_Blue_6_BF",
                "Crystal_Teal_1_BF", "Crystal_Teal_2_BF", "Crystal_Teal_3_BF",
                "Crystal_Teal_4_BF", "Crystal_Teal_5_BF", "Crystal_Teal_6_BF",
                "Crystal_Purple_1_BF", "Crystal_Purple_2_BF", "Crystal_Purple_3_BF",
                "Crystal_Purple_4_BF", "Crystal_Purple_5_BF", "Crystal_Purple_6_BF",
                "CrystalRock_1_BF", "CrystalRock_2_BF"
            }, B, min: 0, max: 1, grpMin: 1, grpMax: 3, grpRad: 3f,
               sclMin: 0.5f, sclMax: 1.5f, altMin: 100f, tiltMax: 30f);

            return c;
        }

        // =====================================================================
        //  PLAINS
        // =====================================================================

        private int RegisterPlains()
        {
            const Heightmap.Biome B = Heightmap.Biome.Plains;
            int c = 0;

            // -- Deciduous trees --
            c += AddGroup(new[] {
                "PoplarTree_BF", "PoplarYoung_BF", "PoplarMature_BF",
                "MapleAged_BF", "MapleOld_BF", "MapleMature_BF",
                "RedMapleMature_BF", "RedMapleAged_BF"
            }, B, min: 1, max: 2, grpMin: 1, grpMax: 2, grpRad: 10f,
               sclMin: 0.8f, sclMax: 1.2f);

            // -- Large deciduous (rare) --
            c += AddGroup(new[] {
                "PoplarAged_BF", "PoplarOld_BF", "PoplarAncient_BF",
                "MapleAncient_BF",
                "RedMapleOld_BF", "RedMapleAncient_BF",
                "RedOakAged_BF", "RedOakOld_BF", "RedOakAncient_BF"
            }, B, min: 0, max: 1, grpMin: 1, grpMax: 1,
               sclMin: 0.9f, sclMax: 1.3f);

            // -- Dry vegetation --
            c += AddGroup(new[] {
                "BushYellow_1_BF",
                "Aloe_1_BF", "Aloe_2_BF", "Aloe_3_BF",
                "Yucca_1_BF", "Yucca_2_BF",
                "DeadPlant_Desert_BF",
                "DesertThistle_1_BF", "DesertThistle_2_BF", "DesertThistle_3_BF"
            }, B, min: 1, max: 3, grpMin: 1, grpMax: 3, grpRad: 5f,
               sclMin: 0.7f, sclMax: 1.3f);

            // -- Cacti --
            c += AddGroup(new[] {
                "Cactus_1_BF", "Cactus_2_BF", "Cactus_3_BF", "Cactus_4_BF",
                "Cactus_5_BF", "Cactus_6_BF", "Cactus_7_BF", "Cactus_8_BF",
                "SmallCactus_1_BF", "SmallCactus_2_BF"
            }, B, min: 0, max: 2, grpMin: 1, grpMax: 2, grpRad: 4f,
               sclMin: 0.7f, sclMax: 1.3f);

            // -- Plains grasses --
            c += AddGroup(new[] {
                "Grass_Plains_1_BF", "Grass_Plains_2_BF",
                "Grass_Plains_3_BF", "Grass_Plains_4_BF",
                "DesertSage_BF"
            }, B, min: 2, max: 5, grpMin: 3, grpMax: 8, grpRad: 5f,
               sclMin: 0.8f, sclMax: 1.2f, blockCheck: false);

            // -- Pickable --
            c += AddGroup(new[] {
                "Pickable_Sunroot_BF", "Pickable_Sunroot_Small_BF"
            }, B, min: 1, max: 2, grpMin: 1, grpMax: 2, grpRad: 3f,
               sclMin: 0.8f, sclMax: 1.1f);

            return c;
        }

        // =====================================================================
        //  MISTLANDS
        // =====================================================================

        private int RegisterMistlands()
        {
            const Heightmap.Biome B = Heightmap.Biome.Mistlands;
            int c = 0;

            // -- Giant mushrooms --
            c += AddGroup(new[] {
                "GiantMushroom_Blue_1_BF", "GiantMushroom_Blue_2_BF", "GiantMushroom_Blue_3_BF",
                "GiantMushroom_Purple_1_BF", "GiantMushroom_Purple_2_BF", "GiantMushroom_Purple_3_BF",
                "GiantMushroom_Red_1_BF", "GiantMushroom_Red_2_BF", "GiantMushroom_Red_3_BF",
                "GiantMushroom_Yellow_1_BF", "GiantMushroom_Yellow_2_BF", "GiantMushroom_Yellow_3_BF"
            }, B, min: 0, max: 2, grpMin: 1, grpMax: 2, grpRad: 8f,
               sclMin: 0.6f, sclMax: 1.4f);

            // -- Giant tube mushrooms --
            c += AddGroup(new[] {
                "GiantTubeMushroom_Blue_1_BF", "GiantTubeMushroom_Blue_2_BF",
                "GiantTubeMushroom_Blue_3_BF", "GiantTubeMushroom_Blue_4_BF",
                "GiantTubeMushroom_Green_1_BF", "GiantTubeMushroom_Green_2_BF",
                "GiantTubeMushroom_Green_3_BF", "GiantTubeMushroom_Green_4_BF",
                "GiantTubeMushroom_Purple_1_BF", "GiantTubeMushroom_Purple_2_BF",
                "GiantTubeMushroom_Purple_3_BF", "GiantTubeMushroom_Purple_4_BF",
                "GiantTubeMushroom_Red_1_BF", "GiantTubeMushroom_Red_2_BF",
                "GiantTubeMushroom_Red_3_BF", "GiantTubeMushroom_Red_4_BF",
                "GiantTubeMushroom_Yellow_1_BF", "GiantTubeMushroom_Yellow_2_BF",
                "GiantTubeMushroom_Yellow_3_BF", "GiantTubeMushroom_Yellow_4_BF"
            }, B, min: 0, max: 2, grpMin: 1, grpMax: 3, grpRad: 6f,
               sclMin: 0.5f, sclMax: 1.3f);

            // -- Glowing mushrooms --
            c += AddGroup(new[] {
                "GlowingMushroom_Purple_1_BF", "GlowingMushroom_Purple_2_BF",
                "GlowingMushroom_Purple_3_BF", "GlowingMushroom_Purple_4_BF",
                "GlowingMushroom_Red_1_BF", "GlowingMushroom_Red_2_BF",
                "GlowingMushroom_Red_3_BF", "GlowingMushroom_Red_4_BF",
                "GlowingMushroom_Orange_1_BF", "GlowingMushroom_Orange_2_BF",
                "GlowingMushroom_Orange_3_BF", "GlowingMushroom_Orange_4_BF",
                "GlowingMushroom_Pink_1_BF", "GlowingMushroom_Pink_2_BF",
                "GlowingMushroom_Pink_3_BF", "GlowingMushroom_Pink_4_BF",
                "GlowingMushroom_White_1_BF", "GlowingMushroom_White_2_BF",
                "GlowingMushroom_White_3_BF", "GlowingMushroom_White_4_BF",
                "GlowingMushroom_Yellow_1_BF", "GlowingMushroom_Yellow_2_BF",
                "GlowingMushroom_Yellow_3_BF", "GlowingMushroom_Yellow_4_BF",
                "GlowingMushroom_Teal_1_BF", "GlowingMushroom_Teal_2_BF",
                "GlowingMushroom_Teal_3_BF", "GlowingMushroom_Teal_4_BF"
            }, B, min: 1, max: 3, grpMin: 2, grpMax: 5, grpRad: 4f,
               sclMin: 0.4f, sclMax: 1.4f);

            // -- Crystals --
            c += AddGroup(new[] {
                "Crystal_Green_1_BF", "Crystal_Green_2_BF", "Crystal_Green_3_BF",
                "Crystal_Green_4_BF", "Crystal_Green_5_BF", "Crystal_Green_6_BF",
                "Crystal_Orange_1_BF", "Crystal_Orange_2_BF", "Crystal_Orange_3_BF",
                "Crystal_Orange_4_BF", "Crystal_Orange_5_BF", "Crystal_Orange_6_BF",
                "Crystal_Red_1_BF", "Crystal_Red_2_BF", "Crystal_Red_3_BF",
                "Crystal_Red_4_BF", "Crystal_Red_5_BF", "Crystal_Red_6_BF"
            }, B, min: 0, max: 1, grpMin: 1, grpMax: 3, grpRad: 3f,
               sclMin: 0.5f, sclMax: 1.5f);

            // -- Obsidian rocks --
            c += AddGroup(new[] {
                "ObsidianBlock_1_BF", "ObsidianBlock_2_BF", "ObsidianBlock_3_BF",
                "ObsidianPillar_1_BF",
                "ObsidianPillarSmall_1_BF", "ObsidianPillarSmall_2_BF", "ObsidianPillarSmall_3_BF",
                "ObsidianRockSmall_1_BF", "ObsidianRockSmall_2_BF", "ObsidianRockSmall_3_BF",
                "ObsidianRockTiny_1_BF", "ObsidianRockTiny_2_BF",
                "ObsidianSmallBlock_1_BF", "ObsidianSmallBlock_2_BF"
            }, B, min: 0, max: 2, grpMin: 1, grpMax: 2, grpRad: 5f,
               sclMin: 0.8f, sclMax: 1.5f);

            // -- Mistlands bushes --
            c += AddGroup(new[] {
                "Astrubrac_1_BF", "Astrubrac_2_BF", "Astrubrac_3_BF",
                "Astrubrac_1_Large_BF", "Astrubrac_2_Large_BF",
                "Astrubrac_3_Large_BF", "Astrubrac_4_Large_BF",
                "TwistedCollard_BF", "DevilsDuscle_BF", "GrimPoke_BF",
                "TauntingPoppy_BF",
                "WhompingLotus_1_BF", "WhompingLotus_2_BF",
                "Vaisy_1_BF", "Vaisy_2_BF", "Vaisy_3_BF", "Vaisy_4_BF", "Vaisy_5_BF"
            }, B, min: 1, max: 3, grpMin: 1, grpMax: 3, grpRad: 5f,
               sclMin: 0.6f, sclMax: 1.3f);

            // -- Pickable crystals --
            c += AddGroup(new[] {
                "Pickable_Crystal_Blue_BF", "Pickable_Crystal_Green_BF",
                "Pickable_Crystal_Orange_BF", "Pickable_Crystal_Purple_BF",
                "Pickable_Crystal_Red_BF", "Pickable_Crystal_Teal_BF"
            }, B, min: 0, max: 1, grpMin: 1, grpMax: 2, grpRad: 3f,
               sclMin: 0.8f, sclMax: 1.2f);

            return c;
        }

        // =====================================================================
        //  ASHLANDS
        // =====================================================================

        private int RegisterAshlands()
        {
            const Heightmap.Biome B = Heightmap.Biome.AshLands;
            int c = 0;

            // -- Lava rocks (with EmissiveMesh fix) --
            c += AddGroup(LavaRockPrefabs, B, min: 0, max: 2, grpMin: 1, grpMax: 2, grpRad: 6f,
               sclMin: 0.8f, sclMax: 1.5f);

            // -- Sand blocks --
            c += AddGroup(new[] {
                "Prop_SandBlock_1_BF", "Prop_SandBlock_2_BF", "Prop_SandBlock_3_BF", "Prop_SandBlock_4_BF",
                "Prop_SandBlock_5_BF", "Prop_SandBlock_6_BF", "Prop_SandBlock_7_BF", "Prop_SandBlock_8_BF"
            }, B, min: 0, max: 2, grpMin: 1, grpMax: 1,
               sclMin: 0.8f, sclMax: 1.5f);

            // -- Sand fins --
            c += AddGroup(new[] {
                "Prop_SandFin_1_BF", "Prop_SandFin_2_BF", "Prop_SandFin_3_BF", "Prop_SandFin_4_BF"
            }, B, min: 0, max: 2, grpMin: 1, grpMax: 2, grpRad: 5f,
               sclMin: 0.8f, sclMax: 1.5f);

            // -- Sand plates --
            c += AddGroup(Enumerable.Range(1, 15).Select(i => $"Prop_SandPlate_{i}_BF").ToArray(),
                B, min: 0, max: 2, grpMin: 1, grpMax: 1,
                sclMin: 0.8f, sclMax: 1.5f);

            // -- Sand rocks --
            c += AddGroup(Enumerable.Range(1, 15).Select(i => $"Prop_SandRock_{i}_BF").ToArray(),
                B, min: 0, max: 2, grpMin: 1, grpMax: 2, grpRad: 5f,
                sclMin: 0.8f, sclMax: 1.5f);

            // -- Sand sediment --
            c += AddGroup(Enumerable.Range(1, 14).Select(i => $"Prop_SandSediment_{i}_BF").ToArray(),
                B, min: 0, max: 2, grpMin: 1, grpMax: 1,
                sclMin: 0.8f, sclMax: 1.5f);

            // -- Sand spires --
            c += AddGroup(Enumerable.Range(1, 19).Select(i => $"Prop_SandSpire_{i}_BF").ToArray(),
                B, min: 0, max: 1, grpMin: 1, grpMax: 2, grpRad: 8f,
                sclMin: 0.8f, sclMax: 1.5f);

            // -- Desert / palm trees --
            c += AddGroup(new[] {
                "DesertTree_BF", "DesertTree_Dead_BF",
                "PalmTree_BF", "PalmYoung_BF", "PalmMature_BF",
                "PalmAged_BF", "PalmOld_BF", "PalmAncient_BF"
            }, B, min: 0, max: 2, grpMin: 1, grpMax: 2, grpRad: 10f,
               sclMin: 0.8f, sclMax: 1.2f);

            // -- Jungle bushes --
            c += AddGroup(new[] {
                "BushJungle_1_BF", "BushJungle_2_BF", "BushJungle_3_BF", "BushJungle_4_BF",
                "BushJungle_5_BF", "BushJungle_6_BF", "BushJungle_7_BF",
                "LargeBushJungle_1_BF", "LargeBushJungle_1S_BF", "LargeBushJungle_2_BF",
                "LargeBushJungle_3_BF", "LargeBushJungle_4_BF"
            }, B, min: 1, max: 3, grpMin: 1, grpMax: 3, grpRad: 5f,
               sclMin: 0.7f, sclMax: 1.3f);

            // -- Jungle clutter & grass --
            c += AddGroup(new[] {
                "ClutterJungle_1_BF", "ClutterJungle_1L_BF", "ClutterJungle_2_BF",
                "ClutterJungle_3_BF", "ClutterJungle_4_BF", "ClutterJungle_5_BF",
                "ClutterJungle_6_BF", "ClutterJungle_6F_BF",
                "ClutterJungle_7_BF", "ClutterJungle_7F_BF",
                "GrassJungle_1_BF", "GrassJungle_2_BF", "GrassJungle_3_BF"
            }, B, min: 2, max: 5, grpMin: 3, grpMax: 8, grpRad: 5f,
               sclMin: 0.8f, sclMax: 1.2f, blockCheck: false);

            return c;
        }

        // =====================================================================
        //  DEEP NORTH
        // =====================================================================

        private int RegisterDeepNorth()
        {
            const Heightmap.Biome B = Heightmap.Biome.DeepNorth;
            int c = 0;

            // -- Giant ice cliffs --
            c += AddGroup(new[] {
                "GiantIceCliff_1_BF", "GiantIceCliff_2_BF", "GiantIceCliff_3_BF", "GiantIceCliff_4_BF",
                "GiantIceCliff_5_BF", "GiantIceCliff_6_BF", "GiantIceCliff_7_BF", "GiantIceCliff_8_BF"
            }, B, min: 0, max: 1, grpMin: 1, grpMax: 1,
               sclMin: 0.8f, sclMax: 1.5f, tiltMax: 40f);

            // -- Ice formations --
            c += AddGroup(new[] {
                "Ice_1_BF", "Ice_2_BF", "Ice_3_BF", "Ice_4_BF",
                "Ice_Large_1_BF", "Ice_Large_2_BF", "Ice_Large_3_BF", "Ice_Large_4_BF"
            }, B, min: 0, max: 2, grpMin: 1, grpMax: 2, grpRad: 8f,
               sclMin: 0.8f, sclMax: 1.5f, tiltMax: 40f);

            // -- Ice crystals --
            c += AddGroup(new[] {
                "IceCrystal_1_BF", "IceCrystal_2_BF", "IceCrystal_3_BF",
                "IceCrystal_Group_1_BF", "IceCrystal_Group_2_BF", "IceCrystal_Group_3_BF"
            }, B, min: 0, max: 2, grpMin: 1, grpMax: 3, grpRad: 4f,
               sclMin: 0.5f, sclMax: 1.5f);

            // -- Ice floes (floating, ocean edges) --
            c += AddGroup(new[] {
                "IceFloating_1_BF", "IceFloating_2_BF", "IceFloating_3_BF", "IceFloating_4_BF",
                "IceFloating_Large_1_BF", "IceFloating_Large_2_BF",
                "IceFloating_Large_3_BF", "IceFloating_Large_4_BF"
            }, B, min: 0, max: 2, grpMin: 1, grpMax: 2, grpRad: 10f,
               sclMin: 0.8f, sclMax: 1.5f, altMin: -1000f, altMax: 0f,
               oceanMin: 0f, oceanMax: 30f);

            return c;
        }

        // =====================================================================
        //  OCEAN
        // =====================================================================

        private int RegisterOcean()
        {
            const Heightmap.Biome B = Heightmap.Biome.Ocean;
            int c = 0;

            // -- Corals --
            c += AddGroup(new[] {
                "Coral_1_1_BF", "Coral_1_2_BF",
                "Coral_2_1_BF",
                "Coral_3_1_BF", "Coral_3_2_BF",
                "Coral_4_1_BF", "Coral_4_2_BF",
                "Coral_5_1_BF", "Coral_5_2_BF",
                "Coral_6_1_BF",
                "Coral_7_1_BF", "Coral_7_2_BF",
                "Coral_9_1_BF", "Coral_9_2_BF", "Coral_9_3_BF",
                "Coral_13_1_BF", "Coral_13_2_BF", "Coral_13_3_BF",
                "Coral_14_1_BF", "Coral_14_2_BF", "Coral_14_3_BF", "Coral_14_4_BF",
                "Coral_15_1_BF", "Coral_15_2_BF", "Coral_15_3_BF",
                "Coral_16_1_BF", "Coral_16_2_BF"
            }, B, min: 1, max: 3, grpMin: 2, grpMax: 5, grpRad: 6f,
               sclMin: 0.5f, sclMax: 1.5f, altMin: -1000f, altMax: -1f,
               oceanMin: 5f, oceanMax: 50f);

            // -- Sea clutter --
            c += AddGroup(new[] {
                "RedSeaFern_1_BF", "RedSeaFern_2_BF",
                "SeaGrass_1_BF", "SeaGrass_2_BF", "SeaGrass_3_BF",
                "Seaweed_1_BF", "Seaweed_2_BF"
            }, B, min: 2, max: 5, grpMin: 3, grpMax: 8, grpRad: 5f,
               sclMin: 0.7f, sclMax: 1.3f, altMin: -1000f, altMax: -1f,
               oceanMin: 3f, oceanMax: 30f, blockCheck: false);

            // -- Pickable ore (ocean floor) --
            c += AddGroup(new[] {
                "Pickable_BloodIronOre_BF", "Pickable_GoldOre_BF",
                "Pickable_MithrilOre_BF", "Pickable_MoonIronOre_BF",
                "Pickable_OrichalcumOre_BF", "Pickable_VibraniumOre_BF", "Pickable_ZincOre_BF"
            }, B, min: 0, max: 1, grpMin: 1, grpMax: 1,
               sclMin: 0.8f, sclMax: 1.2f, altMin: -1000f, altMax: -1f,
               oceanMin: 10f, oceanMax: 50f);

            return c;
        }

        // =====================================================================
        //  VEGETATION PREFAB NAME COLLECTOR
        //  Used by RegisterSupportPrefabs to exclude vegetation from PrefabManager
        // =====================================================================

        private System.Collections.Generic.HashSet<string> GetAllVegetationPrefabNames()
        {
            var names = new System.Collections.Generic.HashSet<string>();

            // --- MEADOWS ---
            // Trees
            names.UnionWith(new[] {
                "AspenTree_BF", "AspenYoung_BF", "AspenMature_BF", "AspenSmall_BF",
                "AspenOld_BF", "AspenAged_BF", "AspenAncient_BF",
                "LindenTree_BF", "LindenYoung_BF", "LindenSmall_BF",
                "LindenMature_BF", "LindenOld_BF", "LindenAged_BF", "LindenAncient_BF",
                "MapleTree_BF", "MapleYoung_BF", "MapleSmall_BF",
                "RedMapleTree_BF", "RedMapleYoung_BF", "RedMapleSmall_BF"
            });
            // Bushes
            names.UnionWith(new[] {
                "BushGreen_1_BF", "BushGreen_2_BF", "BushGreen_3_BF", "BushGreen_4_BF", "BushGreen_5_BF",
                "BlackCherryBush_1_BF", "BlackCherryBush_2_BF", "BlackCherryBush_3_BF",
                "BlackCherryBush_4_BF", "BlackCherryBush_5_BF",
                "DwarfWildRose_1_BF", "DwarfWildRose_2_BF", "DwarfWildRose_3_BF",
                "MapleBush_1_BF", "MapleBush_2_BF", "MapleBush_3_BF"
            });
            // Flowers, plants, grasses, pickables
            names.UnionWith(new[] {
                "Chrysanthemum_Group_BF", "Chrysanthemum_Single_BF",
                "Dahlia_Group_BF", "Dahlia_Single_BF",
                "Daisy_Group_BF", "Daisy_Single_BF",
                "GerberaDaisy_Group_BF", "GerberaDaisy_Single_BF",
                "LillyValley_01_BF", "LillyValley_02_BF", "Verbena_BF",
                "ShaggySoldier_01_BF", "ShaggySoldier_02_BF", "ShaggySoldier_03_BF",
                "Plant_Forest_1_01_BF", "Plant_Forest_1_02_BF",
                "Plant_Forest_1_03_BF", "Plant_Forest_1_04_BF"
            });
            names.UnionWith(new[] {
                "Grass_Meadow_1_01_BF", "Grass_Meadow_1_02_BF", "Grass_Meadow_1_03_BF",
                "Grass_Meadow_1_04_BF", "Grass_Meadow_1_05_BF", "Grass_Meadow_1_06_BF",
                "Grass_Meadow_1_01_Cross_BF", "Grass_Meadow_1_02_Cross_BF", "Grass_Meadow_1_03_Cross_BF",
                "Grass_Meadow_1_01_Detailed_BF", "Grass_Meadow_1_02_Detailed_BF",
                "Grass_Meadow_2_01_BF", "Grass_Meadow_2_02_BF", "Grass_Meadow_2_03_BF",
                "Grass_Meadow_2_04_BF", "Grass_Meadow_2_05_BF", "Grass_Meadow_2_06_BF",
                "Grass_Meadow_2_01_Cross_BF", "Grass_Meadow_2_02_Cross_BF",
                "Grass_Meadow_2_01_Detailed_BF", "Grass_Meadow_2_02_Detailed_BF",
                "Grass_Meadow_3_01_BF", "Grass_Meadow_3_02_BF",
                "Grass_Meadow_3_03_BF", "Grass_Meadow_3_04_BF",
                "FieldGrass_1_BF", "FieldGrass_2_BF", "FieldGrass_3_BF", "FieldGrass_4_BF"
            });
            names.UnionWith(new[] {
                "Pickable_Chamomile_BF", "Pickable_Chamomile_Small_BF",
                "Pickable_Cornflower_BF", "Pickable_Cornflower_Small_BF",
                "Pickable_Poppy_BF", "Pickable_Poppy_Small_BF",
                "Pickable_BouncingBet_BF",
                "Pickable_BrownrayKnapweed_BF", "Pickable_BrownrayKnapweed_Small_BF",
                "Pickable_Goldenrod_BF", "Pickable_Goldenrod_Small_BF"
            });

            // --- BLACK FOREST ---
            names.UnionWith(new[] {
                "EasternPine_BF", "EasternYoung_BF", "EasternMature_BF",
                "EasternAged_BF", "EasternOld_BF", "EasternAncient_BF",
                "PonderossaPine_BF", "PonderossaYoung_BF", "PonderossaMature_BF",
                "PonderossaAged_BF", "PonderossaOld_BF", "PonderossaAncient_BF",
                "OakAged_BF", "OakOld_BF", "OakAncient_BF",
                "SmallPine_1_BF",
                "Ekage_1_BF", "Ekage_2_BF", "Ekage_3_BF",
                "Phriscus_1_BF", "Phriscus_2_BF",
                "Shosie_1_BF", "Shosie_2_BF", "Shosie_3_BF",
                "Fern_01_BF", "Fern_02_BF", "Fern_03_BF",
                "Ivy_01_BF", "Ivy_02_BF", "Ivy_03_BF",
                "Plant_Forest_2_01_BF", "Plant_Forest_2_02_BF", "Plant_Forest_3_01_BF",
                "Grass_Forest_01_BF", "Grass_Forest_02_BF", "Grass_Forest_03_BF",
                "GlowingMushroom_Green_1_BF", "GlowingMushroom_Green_2_BF",
                "GlowingMushroom_Green_3_BF", "GlowingMushroom_Green_4_BF",
                "GlowingMushroom_Blue_1_BF", "GlowingMushroom_Blue_2_BF",
                "GlowingMushroom_Blue_3_BF", "GlowingMushroom_Blue_4_BF",
                "Pickable_SaintJohnsWort_BF"
            });

            // --- SWAMP ---
            names.UnionWith(new[] {
                "WillowTree_BF", "WillowYoung_BF", "WillowMature_BF",
                "WillowAged_BF", "WillowOld_BF", "WillowAncient_BF",
                "WillowBush_1_BF", "WillowBush_2_BF", "WillowBush_3_BF", "WillowBush_4_BF",
                "DeadBush_1_BF", "DeadBush_2_BF", "DeadBush_3_BF",
                "TangledRibwort_1_BF", "TangledRibwort_2_BF", "MercysMoss_BF",
                "GraveGilliflower_1_BF", "GraveGilliflower_2_BF",
                "GiantSkull_1_BF", "GiantSkull_2_BF", "GiantSkull_3_BF",
                "GlowingMushroom_Chartreuse_1_BF", "GlowingMushroom_Chartreuse_2_BF",
                "GlowingMushroom_Chartreuse_3_BF", "GlowingMushroom_Chartreuse_4_BF",
                "GiantMushroom_Green_1_BF", "GiantMushroom_Green_2_BF", "GiantMushroom_Green_3_BF"
            });

            // --- MOUNTAIN ---
            names.UnionWith(new[] {
                "CrimeanPine_BF", "CrimeanYoung_BF", "CrimeanSmall_BF", "CrimeanMature_BF",
                "CrimeanAged_BF", "CrimeanOld_BF", "CrimeanAncient_BF"
            });
            names.UnionWith(new[] {
                "ClawRock_1_BF", "ClawRock_2_BF", "ClawRock_3_BF", "ClawRock_4_BF",
                "ClawRock_5_BF", "ClawRock_6_BF", "ClawRock_7_BF", "ClawRock_8_BF",
                "ClawRock_9_BF", "ClawRock_10_BF", "ClawRock_11_BF", "ClawRock_12_BF", "ClawRock_13_BF",
                "ClawCrackedRock_1_BF", "ClawCrackedRock_2_BF",
                "ClawRockBase_1_BF", "ClawRockBase_2_BF", "ClawRockBase_3_BF",
                "ClawRockBase_4_BF", "ClawRockBase_5_BF",
                "ClawRockS_1_BF", "ClawRockS_2_BF", "ClawRockS_3_BF",
                "ClawRockSmall_1_BF", "ClawRockSmall_2_BF", "ClawRockSmall_3_BF", "ClawRockSmall_4_BF",
                "IceRock_1_BF", "IceRock_2_BF", "IceRock_3_BF", "IceRock_4_BF", "IceRock_5_BF",
                "IceRock_6_BF", "IceRock_7_BF", "IceRock_8_BF", "IceRock_9_BF", "IceRock_10_BF"
            });
            names.UnionWith(new[] {
                "Crystal_Blue_1_BF", "Crystal_Blue_2_BF", "Crystal_Blue_3_BF",
                "Crystal_Blue_4_BF", "Crystal_Blue_5_BF", "Crystal_Blue_6_BF",
                "Crystal_Teal_1_BF", "Crystal_Teal_2_BF", "Crystal_Teal_3_BF",
                "Crystal_Teal_4_BF", "Crystal_Teal_5_BF", "Crystal_Teal_6_BF",
                "Crystal_Purple_1_BF", "Crystal_Purple_2_BF", "Crystal_Purple_3_BF",
                "Crystal_Purple_4_BF", "Crystal_Purple_5_BF", "Crystal_Purple_6_BF",
                "CrystalRock_1_BF", "CrystalRock_2_BF"
            });

            // --- PLAINS ---
            names.UnionWith(new[] {
                "PoplarTree_BF", "PoplarYoung_BF", "PoplarMature_BF",
                "PoplarAged_BF", "PoplarOld_BF", "PoplarAncient_BF",
                "MapleAged_BF", "MapleOld_BF", "MapleMature_BF", "MapleAncient_BF",
                "RedMapleMature_BF", "RedMapleAged_BF", "RedMapleOld_BF", "RedMapleAncient_BF",
                "RedOakAged_BF", "RedOakOld_BF", "RedOakAncient_BF",
                "BushYellow_1_BF",
                "Aloe_1_BF", "Aloe_2_BF", "Aloe_3_BF",
                "Yucca_1_BF", "Yucca_2_BF",
                "DeadPlant_Desert_BF",
                "DesertThistle_1_BF", "DesertThistle_2_BF", "DesertThistle_3_BF",
                "Cactus_1_BF", "Cactus_2_BF", "Cactus_3_BF", "Cactus_4_BF",
                "Cactus_5_BF", "Cactus_6_BF", "Cactus_7_BF", "Cactus_8_BF",
                "SmallCactus_1_BF", "SmallCactus_2_BF",
                "Grass_Plains_1_BF", "Grass_Plains_2_BF", "Grass_Plains_3_BF", "Grass_Plains_4_BF",
                "DesertSage_BF",
                "Pickable_Sunroot_BF", "Pickable_Sunroot_Small_BF"
            });

            // --- MISTLANDS ---
            names.UnionWith(new[] {
                "GiantMushroom_Blue_1_BF", "GiantMushroom_Blue_2_BF", "GiantMushroom_Blue_3_BF",
                "GiantMushroom_Purple_1_BF", "GiantMushroom_Purple_2_BF", "GiantMushroom_Purple_3_BF",
                "GiantMushroom_Red_1_BF", "GiantMushroom_Red_2_BF", "GiantMushroom_Red_3_BF",
                "GiantMushroom_Yellow_1_BF", "GiantMushroom_Yellow_2_BF", "GiantMushroom_Yellow_3_BF"
            });
            // Tube mushrooms
            for (int i = 1; i <= 4; i++)
                foreach (var color in new[] { "Blue", "Green", "Purple", "Red", "Yellow" })
                    names.Add($"GiantTubeMushroom_{color}_{i}_BF");
            // Glowing mushrooms
            for (int i = 1; i <= 4; i++)
                foreach (var color in new[] { "Purple", "Red", "Orange", "Pink", "White", "Yellow", "Teal" })
                    names.Add($"GlowingMushroom_{color}_{i}_BF");
            names.UnionWith(new[] {
                "Crystal_Green_1_BF", "Crystal_Green_2_BF", "Crystal_Green_3_BF",
                "Crystal_Green_4_BF", "Crystal_Green_5_BF", "Crystal_Green_6_BF",
                "Crystal_Orange_1_BF", "Crystal_Orange_2_BF", "Crystal_Orange_3_BF",
                "Crystal_Orange_4_BF", "Crystal_Orange_5_BF", "Crystal_Orange_6_BF",
                "Crystal_Red_1_BF", "Crystal_Red_2_BF", "Crystal_Red_3_BF",
                "Crystal_Red_4_BF", "Crystal_Red_5_BF", "Crystal_Red_6_BF",
                "ObsidianBlock_1_BF", "ObsidianBlock_2_BF", "ObsidianBlock_3_BF",
                "ObsidianPillar_1_BF",
                "ObsidianPillarSmall_1_BF", "ObsidianPillarSmall_2_BF", "ObsidianPillarSmall_3_BF",
                "ObsidianRockSmall_1_BF", "ObsidianRockSmall_2_BF", "ObsidianRockSmall_3_BF",
                "ObsidianRockTiny_1_BF", "ObsidianRockTiny_2_BF",
                "ObsidianSmallBlock_1_BF", "ObsidianSmallBlock_2_BF",
                "Astrubrac_1_BF", "Astrubrac_2_BF", "Astrubrac_3_BF",
                "Astrubrac_1_Large_BF", "Astrubrac_2_Large_BF",
                "Astrubrac_3_Large_BF", "Astrubrac_4_Large_BF",
                "TwistedCollard_BF", "DevilsDuscle_BF", "GrimPoke_BF", "TauntingPoppy_BF",
                "WhompingLotus_1_BF", "WhompingLotus_2_BF",
                "Vaisy_1_BF", "Vaisy_2_BF", "Vaisy_3_BF", "Vaisy_4_BF", "Vaisy_5_BF",
                "Pickable_Crystal_Blue_BF", "Pickable_Crystal_Green_BF",
                "Pickable_Crystal_Orange_BF", "Pickable_Crystal_Purple_BF",
                "Pickable_Crystal_Red_BF", "Pickable_Crystal_Teal_BF"
            });

            // --- ASHLANDS ---
            names.UnionWith(LavaRockPrefabs);
            for (int i = 1; i <= 8; i++) names.Add($"Prop_SandBlock_{i}_BF");
            for (int i = 1; i <= 4; i++) names.Add($"Prop_SandFin_{i}_BF");
            for (int i = 1; i <= 15; i++) names.Add($"Prop_SandPlate_{i}_BF");
            for (int i = 1; i <= 15; i++) names.Add($"Prop_SandRock_{i}_BF");
            for (int i = 1; i <= 14; i++) names.Add($"Prop_SandSediment_{i}_BF");
            for (int i = 1; i <= 19; i++) names.Add($"Prop_SandSpire_{i}_BF");
            names.UnionWith(new[] {
                "DesertTree_BF", "DesertTree_Dead_BF",
                "PalmTree_BF", "PalmYoung_BF", "PalmMature_BF",
                "PalmAged_BF", "PalmOld_BF", "PalmAncient_BF",
                "BushJungle_1_BF", "BushJungle_2_BF", "BushJungle_3_BF", "BushJungle_4_BF",
                "BushJungle_5_BF", "BushJungle_6_BF", "BushJungle_7_BF",
                "LargeBushJungle_1_BF", "LargeBushJungle_1S_BF", "LargeBushJungle_2_BF",
                "LargeBushJungle_3_BF", "LargeBushJungle_4_BF",
                "ClutterJungle_1_BF", "ClutterJungle_1L_BF", "ClutterJungle_2_BF",
                "ClutterJungle_3_BF", "ClutterJungle_4_BF", "ClutterJungle_5_BF",
                "ClutterJungle_6_BF", "ClutterJungle_6F_BF",
                "ClutterJungle_7_BF", "ClutterJungle_7F_BF",
                "GrassJungle_1_BF", "GrassJungle_2_BF", "GrassJungle_3_BF"
            });

            // --- DEEP NORTH ---
            names.UnionWith(new[] {
                "GiantIceCliff_1_BF", "GiantIceCliff_2_BF", "GiantIceCliff_3_BF", "GiantIceCliff_4_BF",
                "GiantIceCliff_5_BF", "GiantIceCliff_6_BF", "GiantIceCliff_7_BF", "GiantIceCliff_8_BF",
                "Ice_1_BF", "Ice_2_BF", "Ice_3_BF", "Ice_4_BF",
                "Ice_Large_1_BF", "Ice_Large_2_BF", "Ice_Large_3_BF", "Ice_Large_4_BF",
                "IceCrystal_1_BF", "IceCrystal_2_BF", "IceCrystal_3_BF",
                "IceCrystal_Group_1_BF", "IceCrystal_Group_2_BF", "IceCrystal_Group_3_BF",
                "IceFloating_1_BF", "IceFloating_2_BF", "IceFloating_3_BF", "IceFloating_4_BF",
                "IceFloating_Large_1_BF", "IceFloating_Large_2_BF",
                "IceFloating_Large_3_BF", "IceFloating_Large_4_BF"
            });

            // --- OCEAN ---
            names.UnionWith(new[] {
                "Coral_1_1_BF", "Coral_1_2_BF", "Coral_2_1_BF",
                "Coral_3_1_BF", "Coral_3_2_BF",
                "Coral_4_1_BF", "Coral_4_2_BF",
                "Coral_5_1_BF", "Coral_5_2_BF", "Coral_6_1_BF",
                "Coral_7_1_BF", "Coral_7_2_BF",
                "Coral_9_1_BF", "Coral_9_2_BF", "Coral_9_3_BF",
                "Coral_13_1_BF", "Coral_13_2_BF", "Coral_13_3_BF",
                "Coral_14_1_BF", "Coral_14_2_BF", "Coral_14_3_BF", "Coral_14_4_BF",
                "Coral_15_1_BF", "Coral_15_2_BF", "Coral_15_3_BF",
                "Coral_16_1_BF", "Coral_16_2_BF",
                "RedSeaFern_1_BF", "RedSeaFern_2_BF",
                "SeaGrass_1_BF", "SeaGrass_2_BF", "SeaGrass_3_BF",
                "Seaweed_1_BF", "Seaweed_2_BF",
                "Pickable_BloodIronOre_BF", "Pickable_GoldOre_BF",
                "Pickable_MithrilOre_BF", "Pickable_MoonIronOre_BF",
                "Pickable_OrichalcumOre_BF", "Pickable_VibraniumOre_BF", "Pickable_ZincOre_BF"
            });

            return names;
        }
    }
}
