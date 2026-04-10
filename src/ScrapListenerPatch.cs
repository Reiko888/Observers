using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using GameNetcodeStuff;
using System.Diagnostics;
using BepInEx.Logging;

namespace Observers.src
{
    internal class ScrapListenerPatch
    {
        [HarmonyPatch(typeof(RoundManager), "SyncScrapValuesClientRpc")]
        public class ScrapSyncPatch
        {
            [HarmonyPostfix]
            public static void GetSpawnedScrap(NetworkObjectReference[] spawnedScrap)
            {
                foreach (NetworkObjectReference scrapRef in spawnedScrap)
                {
                    if (scrapRef.TryGet(out NetworkObject netObj))
                    { ulong id = netObj.NetworkObjectId;
                        ObserverScrapListener.SpawnedScrapReg.Add(id);
                        Plugin.Logger.LogInfo($"Added scrap with ID {id} to SpawnedScrapReg");
                    }
                }   
            }
        }

        [HarmonyPatch(typeof(PlayerControllerB), "GrabObjectClientRpc")]
        public class pickedUpUniqueScrapPatch
        {
            [HarmonyPostfix]
            public static void addItemToScrapReg(PlayerControllerB __instance, bool grabValidated, NetworkObjectReference grabbedObject)
            {
                if (!grabValidated) return;

                if (grabbedObject.TryGet(out NetworkObject netObj, null))
                {
                    ulong scrapId = netObj.NetworkObjectId;
                    ulong playerId = __instance.playerClientId;
                    ObserverScrapListener.RegisterScrapPickup(playerId, scrapId);
                }
            }
        }
    }
}
