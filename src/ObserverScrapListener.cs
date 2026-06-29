using GameNetcodeStuff;
using System.Collections.Generic;

namespace Observers.src
{
    public static class ObserverScrapListener
    {
        public static Dictionary<ulong, HashSet<ulong>> playerScrapReg = new Dictionary<ulong, HashSet<ulong>>();
        public static HashSet<ulong> SpawnedScrapReg = new HashSet<ulong>();
        public static HashSet<ulong> globallyFoundScrapIDs = new HashSet<ulong>();

        public static void RegisterScrapPickup(ulong playerId, ulong scrapId)
        {
            if (!SpawnedScrapReg.Contains(scrapId)) return;

            if (globallyFoundScrapIDs.Contains(scrapId)) return;

            globallyFoundScrapIDs.Add(scrapId);

            if (!playerScrapReg.ContainsKey(playerId))
            {
                playerScrapReg[playerId] = new HashSet<ulong>();
            }

            playerScrapReg[playerId].Add(scrapId);
            Plugin.Logger.LogInfo($"Registered pickup of scrap with ID {scrapId} by player with ID {playerId}");
        }

        public static PlayerControllerB CheckForGreedyPlayer(int threshold)
        {
            PlayerControllerB greediestPlayer = null;

            int highestScrapCount = threshold - 1;

            foreach (var playerEntry in playerScrapReg)
            {
                int playerScrapCount = playerEntry.Value.Count;

                if (playerScrapCount > highestScrapCount)
                {
                    PlayerControllerB p = StartOfRound.Instance.allPlayerScripts[playerEntry.Key];
                    if (p != null && !p.isPlayerDead && p.isPlayerControlled)
                    {
                        greediestPlayer = p;
                        highestScrapCount = playerScrapCount; 
                    }
                }
            }

            return greediestPlayer;
        }
        public static void ClearData()
        {
            playerScrapReg.Clear();
            SpawnedScrapReg.Clear();
            globallyFoundScrapIDs.Clear();
            Plugin.Logger.LogInfo("Scrap list cleared for new round");
        }
    }
}
