using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using JetBrains.Annotations;
using LethalLib.Modules;
using Observers.Configuration;
using Observers.Configuration;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static Observers.src.ScrapListenerPatch;

namespace Observers {
    [BepInPlugin("Reiko888.Observer", "Observer", "1.0.0")]
    [BepInDependency(LethalLib.Plugin.ModGUID)]
    public class Plugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger = null!;
        internal static PluginConfig BoundConfig { get; private set; } = null!;
        public static AssetBundle? ModAssets;
        public static GameObject? SirenObj;
        public static GameObject? ObserverPrefab;
        public static GameObject? ManifestParticles;

        private void Awake()
        {
            Harmony harmony = new Harmony("Reiko888.Observer");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Logger = base.Logger;

            BoundConfig = new PluginConfig(base.Config);

            InitializeNetworkBehaviours();

            ModAssets = AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(Info.Location), "Observers"));
            if (ModAssets == null)
            {
                Logger.LogError($"Failed to load custom assets.");
                return;
            }
            ObserverPrefab = ModAssets.LoadAsset<GameObject>("ObserverSpectre");
            SirenObj = ModAssets.LoadAsset<GameObject>("SirenSFX");
            ManifestParticles = ModAssets.LoadAsset<GameObject>("Manifest");

            Logger.LogInfo("Observer Prefab loaded successfully!");

            var ObserversEnemy = ModAssets.LoadAsset<EnemyType>("ObserversEnemy");
            var ObserverTN = ModAssets.LoadAsset<TerminalNode>("ObserverTN");
            var ObserverTK = ModAssets.LoadAsset<TerminalKeyword>("ObserverTK");

            NetworkPrefabs.RegisterNetworkPrefab(ObserversEnemy.enemyPrefab);
            NetworkPrefabs.RegisterNetworkPrefab(SirenObj);
            NetworkPrefabs.RegisterNetworkPrefab(ManifestParticles);

            Dictionary<Levels.LevelTypes, int> vanillaWeights = new Dictionary<Levels.LevelTypes, int>
            {
                { Levels.LevelTypes.TitanLevel, 60 },
                { Levels.LevelTypes.RendLevel, 15 },
                { Levels.LevelTypes.DineLevel, 15 },
                { Levels.LevelTypes.OffenseLevel, 30 },
                { Levels.LevelTypes.MarchLevel, 45 },
                { Levels.LevelTypes.ExperimentationLevel, 60 },
                { Levels.LevelTypes.AssuranceLevel, 40 },
                { Levels.LevelTypes.VowLevel, 70 },
                { Levels.LevelTypes.ArtificeLevel, 45 },
                { Levels.LevelTypes.EmbrionLevel, 50 },
                { Levels.LevelTypes.AdamanceLevel, 30 },
                { Levels.LevelTypes.Modded, 45 }
        };
            Dictionary<string, int> customWeights = new Dictionary<string, int>();

            Enemies.RegisterEnemy(ObserversEnemy, vanillaWeights, customWeights, ObserverTN, ObserverTK);

            Logger.LogInfo($"Observer enemy has loaded!");
        }

        private static void InitializeNetworkBehaviours()
        {
            var types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (var type in types)
            {
                var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                foreach (var method in methods)
                {
                    var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                    if (attributes.Length > 0)
                    {
                        method.Invoke(null, null);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Terminal))]
        public class TerminalKeywordPatch
        {
            [HarmonyPatch("Awake")]
            [HarmonyPostfix]
            public static void AddKeyword(Terminal __instance)
            {
                var ObserverTK = ModAssets.LoadAsset<TerminalKeyword>("ObserverTK");

                if (ObserverTK == null || ObserverTK == null) return;

                var keywordList = __instance.terminalNodes.allKeywords.ToList();

                if (!keywordList.Contains(ObserverTK)) keywordList.Add(ObserverTK);

                __instance.terminalNodes.allKeywords = keywordList.ToArray();

                Plugin.Logger.LogInfo("Successfully injected extra Vecna keywords into the Terminal!");
            }
        }
    }
}