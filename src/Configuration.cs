using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;

namespace Observers.Configuration {
    public class PluginConfig
    {
        public ConfigEntry<int> SpawnWeightTitan;
        public ConfigEntry<int> SpawnWeightRend;
        public ConfigEntry<int> SpawnWeightDine;
        public ConfigEntry<int> SpawnWeightOffense;
        public ConfigEntry<int> SpawnWeightMarch;
        public ConfigEntry<int> SpawnWeightExperimentation;
        public ConfigEntry<int> SpawnWeightAssurance;
        public ConfigEntry<int> SpawnWeightVow;
        public ConfigEntry<int> SpawnWeightArtifice;
        public ConfigEntry<int> SpawnWeightEmbrion;
        public ConfigEntry<int> SpawnWeightAdamance;
        public ConfigEntry<int> SpawnWeightModded;
        public ConfigEntry<float> BaseChaseTime;
        public ConfigEntry<float> SpectreTriggerRadius;
        public ConfigEntry<float> ChaserChaseSpeed;
        public ConfigEntry<float> ChaserHitRange;
        public ConfigEntry<int> ChaserHealth;
        public ConfigEntry<float> ChaserStunMultiplier;
        public ConfigEntry<int> ChaserDamage;
        public ConfigEntry<int> BaseScrapThreshold;
        public ConfigEntry<float> SirenVolume;

        public ConfigEntry<int> SpawnWeight;
        public PluginConfig(ConfigFile cfg)
        {
            BaseChaseTime = cfg.Bind("1. Chaser Settings", "Base Chase Time", 60f, "How many seconds the Chaser will pursue a player before giving up (Timeout).");
            SpectreTriggerRadius = cfg.Bind("1. Chaser Settings", "Spectre Trigger Radius", 30f, "How close the marked player must get to the Spectre to trigger the rise.");
            ChaserChaseSpeed = cfg.Bind("1. Chaser Settings", "Chaser Chase Speed", 8f, "Top speed of the Chaser while running.");
            ChaserHitRange = cfg.Bind("1. Chaser Settings", "Chaser Hit Range", 4f, "How far forward the Chaser's knife attack reaches.");
            ChaserHealth = cfg.Bind("1. Chaser Settings", "Chaser Health", 4, "How many hits the Chaser can take before dying.");
            ChaserStunMultiplier = cfg.Bind("1. Chaser Settings", "Chaser Stun Time Multiplier", 1f, "Multiplier for how long the Chaser stays stunned (1 = default vanilla).");
            ChaserDamage = cfg.Bind("1. Chaser Settings", "Chaser Damage", 30, "How much damage a single stab does to the player (100 = instant kill).");
            BaseScrapThreshold = cfg.Bind("1. Chaser Settings", "Base Scrap Anger Threshold", 6, "Base number used for scrap target calculation (Base - Active Players = Items needed to anger).");
            SirenVolume = cfg.Bind("1. Chaser Settings", "Siren Volume", 1f, "Volume of the global siren when a player is marked (0.0 = completely silent, 1.0 = normal volume, can go higher if you want to deafen them).");
            SpawnWeightTitan = cfg.Bind("0. Spawn Weights", "Titan", 60, "Spawn weight of Observer on Titan.");
            SpawnWeightRend = cfg.Bind("0. Spawn Weights", "Rend", 15, "Spawn weight of Observer on Rend.");
            SpawnWeightDine = cfg.Bind("0. Spawn Weights", "Dine", 15, "Spawn weight of Observer on Dine.");
            SpawnWeightOffense = cfg.Bind("0. Spawn Weights", "Offense", 30, "Spawn weight of Observer on Offense.");
            SpawnWeightMarch = cfg.Bind("0. Spawn Weights", "March", 45, "Spawn weight of Observer on March.");
            SpawnWeightExperimentation = cfg.Bind("0. Spawn Weights", "Experimentation", 60, "Spawn weight of Observer on Experimentation.");
            SpawnWeightAssurance = cfg.Bind("0. Spawn Weights", "Assurance", 40, "Spawn weight of Observer on Assurance.");
            SpawnWeightVow = cfg.Bind("0. Spawn Weights", "Vow", 70, "Spawn weight of Observer on Vow.");
            SpawnWeightArtifice = cfg.Bind("0. Spawn Weights", "Artifice", 45, "Spawn weight of Observer on Artifice.");
            SpawnWeightEmbrion = cfg.Bind("0. Spawn Weights", "Embrion", 50, "Spawn weight of Observer on Embrion.");
            SpawnWeightAdamance = cfg.Bind("0. Spawn Weights", "Adamance", 30, "Spawn weight of Observer on Adamance.");
            SpawnWeightModded = cfg.Bind("0. Spawn Weights", "Modded Moons", 45, "Base spawn weight of Observer on custom modded moons.");

            ClearUnusedEntries(cfg);
        }

        private void ClearUnusedEntries(ConfigFile cfg) {
            PropertyInfo orphanedEntriesProp = cfg.GetType().GetProperty("OrphanedEntries", BindingFlags.NonPublic | BindingFlags.Instance);
            var orphanedEntries = (Dictionary<ConfigDefinition, string>)orphanedEntriesProp.GetValue(cfg, null);
            orphanedEntries.Clear(); 
            cfg.Save(); 
        }
    }
}