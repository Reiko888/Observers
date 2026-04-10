using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using JetBrains.Annotations;
using LethalLib.Modules;
using Observers.Configuration;
using System.IO;
using System.Reflection;
using UnityEngine;
using static Observers.src.ScrapListenerPatch;

namespace Observers {
    [BepInPlugin("Reiko888.Observers", "Observers", "1.0.0")]
    [BepInDependency(LethalLib.Plugin.ModGUID)] 
    public class Plugin : BaseUnityPlugin {
        internal static new ManualLogSource Logger = null!;
        internal static PluginConfig BoundConfig { get; private set; } = null!;
        public static AssetBundle? ModAssets;
        public static GameObject? SirenObj;
        public static GameObject? ObserverPrefab;

        private void Awake() {
            Harmony harmony = new Harmony("Reiko888.Observers");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Logger = base.Logger;

            BoundConfig = new PluginConfig(base.Config);

            InitializeNetworkBehaviours();

            ModAssets = AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(Info.Location), "Observers"));
            if (ModAssets == null) {
                Logger.LogError($"Failed to load custom assets.");
                return;
            }
            ObserverPrefab = ModAssets.LoadAsset<GameObject>("ObserverSpectre");
            SirenObj = ModAssets.LoadAsset<GameObject>("SirenSFX");
            Logger.LogInfo("Observer Prefab loaded successfully!");

            var ObserversEnemy = ModAssets.LoadAsset<EnemyType>("ObserversEnemy");
            var ExampleEnemyTN = ModAssets.LoadAsset<TerminalNode>("ExampleEnemyTN");
            var ExampleEnemyTK = ModAssets.LoadAsset<TerminalKeyword>("ExampleEnemyTK");

            NetworkPrefabs.RegisterNetworkPrefab(ObserversEnemy.enemyPrefab);
            NetworkPrefabs.RegisterNetworkPrefab(SirenObj);

            Enemies.RegisterEnemy(ObserversEnemy, BoundConfig.SpawnWeight.Value, Levels.LevelTypes.All, ExampleEnemyTN, ExampleEnemyTK);
            
            Logger.LogInfo($"Plugin Observers is loaded!");
        }

        private static void InitializeNetworkBehaviours() {
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
    }
}