using BepInEx;
using GameNetcodeStuff;
using HarmonyLib;
using LethalLib.Modules;
using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;

namespace Observers.src
{
    public static class ObserverScrapListener
    {
        public static Dictionary<ulong, HashSet<ulong>> playerScrapReg = new Dictionary<ulong, HashSet<ulong>>();
        public static HashSet<ulong> SpawnedScrapReg = new HashSet<ulong>();

        public static void RegisterScrapPickup(ulong playerId, ulong scrapId)
        {
            if (!SpawnedScrapReg.Contains(scrapId)) return;

            if (!playerScrapReg.ContainsKey(playerId))
            {
                playerScrapReg[playerId] = new HashSet<ulong>();
            }

            playerScrapReg[playerId].Add(scrapId);
            Plugin.Logger.LogInfo($"Registered pickup of scrap with ID {scrapId} by player with ID {playerId}");
        }

        public static PlayerControllerB CheckForGreedyPlayer(int threshold)
        {
            foreach (var playerEntry in playerScrapReg)
            {
                if (playerEntry.Value.Count >= threshold)
                {
                    return StartOfRound.Instance.allPlayerScripts[playerEntry.Key];
                }
            }
            return null;
        }
        public static void ClearData()
        {
            playerScrapReg.Clear();
            SpawnedScrapReg.Clear();
            Plugin.Logger.LogInfo("Scrap list cleared for new round");
        }
    }
}
