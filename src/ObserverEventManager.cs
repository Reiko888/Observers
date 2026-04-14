using System;
using GameNetcodeStuff;
using HarmonyLib;

namespace Observers.src
{
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