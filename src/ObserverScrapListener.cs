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
    public class ObserverScrapListener : NetworkBehaviour
    {
        public static ObserverScrapListener Instance { get; private set; } = null!;
        public Dictionary<ulong, HashSet<ulong>> playerScrapReg= new Dictionary<ulong, HashSet<ulong>>();
        public HashSet<ulong> SpawnedScrapReg = new HashSet<ulong>();

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            playerScrapReg.Clear();
            Plugin.Logger.LogInfo($"Scrap list cleared");
        }

        public void RegisterScrapPickup(ulong playerId, ulong scrapId)
        {
            if (!SpawnedScrapReg.Contains(scrapId)) return;

            if (!playerScrapReg.ContainsKey(playerId))
            {
                playerScrapReg[playerId] = new HashSet<ulong>();
            }

            playerScrapReg[playerId].Add(scrapId);
            Plugin.Logger.LogInfo($"Registered pickup of scrap with ID {scrapId} by player with ID {playerId}");
        }

        public PlayerControllerB CheckForGreedyPlayer(int threshold)
        {
            foreach (var playerEntry in playerScrapReg)
            {

                if (playerEntry.Value.Count >= threshold)
                {

                    ulong greedyPlayerId = playerEntry.Key;

                    return StartOfRound.Instance.allPlayerScripts[greedyPlayerId];
                }
            }

            return null;
        }
    }
}
