using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using Dawn;
using Dawn.Utils;
using Dusk;
using static Observers.src.ScrapListenerPatch;

namespace Observers {
    //[BepInPlugin("Reiko888.Observer", "Observer", "1.1.0")]
    //[BepInDependency(LethalLib.Plugin.ModGUID)]
    //public class Plugin : BaseUnityPlugin
    //{
    //    internal static new ManualLogSource Logger = null!;
    //    internal static PluginConfig BoundConfig { get; private set; } = null!;
    //    public static AssetBundle? ModAssets;
    //    public static GameObject? SirenObj;
    //    public static GameObject? ObserverPrefab;
    //    public static GameObject? ManifestParticles;

    //    private void Awake()
    //    {
    //        Harmony harmony = new Harmony("Reiko888.Observer");
    //        harmony.PatchAll(Assembly.GetExecutingAssembly());
    //        Logger = base.Logger;

    //        BoundConfig = new PluginConfig(base.Config);

    //        InitializeNetworkBehaviours();

    //        ModAssets = AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(Info.Location), "Observers"));
    //        if (ModAssets == null)
    //        {
    //            Logger.LogError($"Failed to load custom assets.");
    //            return;
    //        }
    //        ObserverPrefab = ModAssets.LoadAsset<GameObject>("ObserverSpectre");
    //        SirenObj = ModAssets.LoadAsset<GameObject>("SirenSFX");
    //        ManifestParticles = ModAssets.LoadAsset<GameObject>("Manifest");

    //        Logger.LogInfo("Observer Prefab loaded successfully!");

    //        var ObserversEnemy = ModAssets.LoadAsset<EnemyType>("ObserversEnemy");
    //        var ObserverTN = ModAssets.LoadAsset<TerminalNode>("ObserverTN");
    //        var ObserverTK = ModAssets.LoadAsset<TerminalKeyword>("ObserverTK");

    //        NetworkPrefabs.RegisterNetworkPrefab(ObserversEnemy.enemyPrefab);
    //        NetworkPrefabs.RegisterNetworkPrefab(SirenObj);
    //        NetworkPrefabs.RegisterNetworkPrefab(ManifestParticles);

    //        Dictionary<Levels.LevelTypes, int> vanillaWeights = new Dictionary<Levels.LevelTypes, int>
    //        {
    //            { Levels.LevelTypes.TitanLevel, BoundConfig.SpawnWeightTitan.Value },
    //            { Levels.LevelTypes.RendLevel, BoundConfig.SpawnWeightRend.Value },
    //            { Levels.LevelTypes.DineLevel, BoundConfig.SpawnWeightDine.Value },
    //            { Levels.LevelTypes.OffenseLevel, BoundConfig.SpawnWeightOffense.Value },
    //            { Levels.LevelTypes.MarchLevel, BoundConfig.SpawnWeightMarch.Value },
    //            { Levels.LevelTypes.ExperimentationLevel, BoundConfig.SpawnWeightExperimentation.Value },
    //            { Levels.LevelTypes.AssuranceLevel, BoundConfig.SpawnWeightAssurance.Value },
    //            { Levels.LevelTypes.VowLevel, BoundConfig.SpawnWeightVow.Value },
    //            { Levels.LevelTypes.ArtificeLevel, BoundConfig.SpawnWeightArtifice.Value },
    //            { Levels.LevelTypes.EmbrionLevel, BoundConfig.SpawnWeightEmbrion.Value },
    //            { Levels.LevelTypes.AdamanceLevel, BoundConfig.SpawnWeightAdamance.Value },
    //            { Levels.LevelTypes.Modded, BoundConfig.SpawnWeightModded.Value }
    //        };
    //        Dictionary<string, int> customWeights = new Dictionary<string, int>();

    //        Enemies.RegisterEnemy(ObserversEnemy, vanillaWeights, customWeights, ObserverTN, ObserverTK);

    //        Logger.LogInfo($"Observer enemy has loaded!");
    //    }


        [BepInPlugin(modGUID, modName, modVersion)]
        [BepInDependency(DawnLib.PLUGIN_GUID)]
        internal class Plugin : BaseUnityPlugin
        {
            public const string modGUID = "Reiko888.Observer";
            public const string modName = "Observer";
            public const string modVersion = "2.0.0";

            public static Plugin Instance = null!;
            internal static new ManualLogSource Logger = null!;
            internal static readonly Harmony harmony = new Harmony(modGUID);
            internal static DuskMod mod = null!;

            private void Awake()
            {
                if (Instance == null)
                    Instance = this;
                Logger = base.Logger;
                AssetBundle mainBundle = AssetBundleUtils.LoadBundle(Assembly.GetExecutingAssembly(), "observer_cont");
                mod = DuskMod.RegisterMod(this, mainBundle);
                mod.RegisterContentHandlers();
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                Logger.LogInfo($"Plugin {modName} is loaded!");
            }
        }
}