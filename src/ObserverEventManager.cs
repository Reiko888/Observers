using System;
using GameNetcodeStuff;
using HarmonyLib;

namespace Observers.src
{
    [HarmonyPatch(typeof(Terminal))]
    internal class TerminalVideoInjectorPatch
    {
        [HarmonyPatch("Start")]
        [HarmonyPostfix]
        public static void InjectVideo(Terminal __instance)
        {
            if (ObserverContentHandler.Instance.observerAssets.ObserverChaser == null) return;
            ObserverTerminalAssets container = ObserverContentHandler.Instance.observerAssets.ObserverChaser.GetComponent<ObserverTerminalAssets>();
            if (container == null || container.bestiaryVideo == null) return;
            foreach (TerminalNode node in __instance.enemyFiles)
            {
                if (node != null && node.name == "ObserverBestiaryNode")
                {
                    node.displayVideo = container.bestiaryVideo;
                    return;
                }
            }

            Plugin.Logger.LogWarning("Could not find a TerminalNode named 'ObserverBestiaryNode' in the Bestiary.");
        }
    }

    [HarmonyPatch]
    public static class ObserversEventManager
    {
        public static event Action OnShipLeft;
        public static event Action<PlayerControllerB> OnPlayerDied;
        public static event Action<PlayerControllerB> OnPlayerDisconnect;

        [HarmonyPatch(typeof(StartOfRound), "ShipLeave")]
        [HarmonyPostfix]
        private static void TriggerShipLeave()
        {
            OnShipLeft?.Invoke();
        }

        [HarmonyPatch(typeof(PlayerControllerB), "KillPlayer")]
        [HarmonyPostfix]
        private static void TriggerPlayerDied(PlayerControllerB __instance)
        {
            OnPlayerDied?.Invoke(__instance);
        }

        [HarmonyPatch(typeof(StartOfRound), "OnPlayerDC")]
        [HarmonyPostfix]
        private static void TriggerPlayerDisconnect(int playerObjectNumber)
        {
            if (StartOfRound.Instance != null && playerObjectNumber >= 0 && playerObjectNumber < StartOfRound.Instance.allPlayerScripts.Length)
            {
                OnPlayerDisconnect?.Invoke(StartOfRound.Instance.allPlayerScripts[playerObjectNumber]);
            }
        }
    }
}