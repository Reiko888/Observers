using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System.Reflection;
using UnityEngine;
using Dawn;
using Dawn.Utils;
using Dusk;
using static Observers.src.ScrapListenerPatch;

namespace Observers {
        [BepInPlugin(modGUID, modName, modVersion)]
        [BepInDependency(DawnLib.PLUGIN_GUID)]
        internal class Plugin : BaseUnityPlugin
        {
            public const string modGUID = "Reiko888.Observer";
            public const string modName = "Observer";
            public const string modVersion = "2.1.0";

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