using GameNetcodeStuff;
using HarmonyLib;
using Observers.src;
using System.Collections;
using System.Diagnostics;
using Unity.Netcode;
using UnityEngine;

namespace Observers {

    class ObserversAI : EnemyAI
    {
        // We set these in our Asset Bundle, so we can disable warning CS0649:
        // Field 'field' is never assigned to, and will always have its default value 'value'
#pragma warning disable 0649
        public GameObject activeObserver;
        public Transform turnCompass = null!;
        public Transform attackArea = null!;
        #pragma warning restore 0649
        float timeSinceHittingLocalPlayer;
        float timeSinceNewRandPos;
        Vector3 positionRandomness;
        Vector3 StalkPos;
        System.Random enemyRandom = null!;
        bool isDeadAnimationDone;
        bool hasSpawnedSpectre = false;
        Vector3 mainEntrancePosition;
        bool isPlayingMarkAnimation = false;
        float chaseTimer = 0f;
        bool isStartingCooldown = false;

        enum State {
            Observing,
            MarkingPlayer,
            Chase,
            Cooldown
        }

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text) {
            Plugin.Logger.LogInfo(text);
        }

        public override void Start() {
            base.Start();
            LogIfDebugBuild("Observers Spawned");
            timeSinceNewRandPos = 0;
            enemyRandom = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);
            isDeadAnimationDone = false;
            Animator chaserAnim = GetComponentInChildren<Animator>();
            if (chaserAnim != null)
            {
                chaserAnim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                chaserAnim.enabled = true;
            }
            currentBehaviourStateIndex = (int)State.Observing;
        }

        public override void OnDestroy()
        { 
            base.OnDestroy();
            if (this.activeObserver != null) Destroy(this.activeObserver);
        }

        public void SpawnObserverSpectre()
        {
            EntranceTeleport[] allTeleports = FindObjectsOfType<EntranceTeleport>();
            EntranceTeleport mainEntrance = null;

            foreach (EntranceTeleport teleport in allTeleports)
            {
                if (teleport.entranceId == 0 && teleport.isEntranceToBuilding)
                {
                    mainEntrance = teleport;
                    break;
                }
            }
            if (mainEntrance == null) return;

            Vector3 doorPos = mainEntrance.transform.position;
            mainEntrancePosition = doorPos;
            Vector3 bestDirection = mainEntrance.transform.forward;
            float furthestDistance = 0f;

            for (int i = 0; i < 8; i++)
            {
                Vector3 direction = Quaternion.Euler(0, i * 45, 0) * Vector3.forward;
                RaycastHit hit;

                if (Physics.Raycast(doorPos + Vector3.up, direction, out hit, 40f))
                {
                    if (hit.distance > furthestDistance)
                    {
                        furthestDistance = hit.distance;
                        bestDirection = direction;
                    }
                }
                else
                {
                    furthestDistance = 40f;
                    bestDirection = direction;
                    break;
                }
            }

            Vector3 horizontalPos = doorPos + (bestDirection * (furthestDistance * 0.6f));

            Vector3 skyPos = horizontalPos + (Vector3.up * 20f);
            Vector3 groundPos = horizontalPos; // Fallback

            if (Physics.Raycast(skyPos, Vector3.down, out RaycastHit groundHit, 60f, StartOfRound.Instance.collidersAndRoomMask))
            {
                groundPos = groundHit.point;
            }

            if (UnityEngine.AI.NavMesh.SamplePosition(groundPos, out UnityEngine.AI.NavMeshHit navHit, 10f, UnityEngine.AI.NavMesh.AllAreas))
            {
                groundPos = navHit.position;
            }
            else
            {
                groundPos = doorPos;
                Plugin.Logger.LogWarning("ObserversAI: Could not find NavMesh for Spectre! Falling back to door.");
            }

            Vector3 finalSpawnPos = groundPos;
            float desiredHoverHeight = 15f;

            if (Physics.Raycast(groundPos + (Vector3.up * 0.5f), Vector3.up, out RaycastHit ceilingHit, desiredHoverHeight, StartOfRound.Instance.collidersAndRoomMask))
            {
                finalSpawnPos.y = ceilingHit.point.y - 2.0f; // 2 meters below the ceiling
                Plugin.Logger.LogInfo("Ceiling detected above NavMesh. Adjusting Spectre hover height.");
            }
            else
            {
                finalSpawnPos.y += desiredHoverHeight;
            }

            Quaternion spawnRot = Quaternion.identity;

            if (Plugin.ObserverPrefab != null)
            {
                Plugin.Logger.LogInfo("Host has calculated safe NavMesh spawn position. Sending to clients...");
                hasSpawnedSpectre = true;
                SpawnSpectreClientRpc(finalSpawnPos, spawnRot);
            }
        }

        [ClientRpc]
        public void SpawnSpectreClientRpc(Vector3 spawnPos, Quaternion spawnRot)
        {
            if (activeObserver == null && Plugin.ObserverPrefab!=null)
            {
                activeObserver = Instantiate(Plugin.ObserverPrefab, spawnPos, spawnRot);
            }
        }

        public void DespawnObserverSpectre()
        {
            if (!IsServer) return;
            DespawnSpectreClientRpc();
            hasSpawnedSpectre = false;
        }

        [ClientRpc]
        public void DespawnSpectreClientRpc()
        {
            if (activeObserver != null)
            {
                Destroy(activeObserver);
            }
        }

        public override void Update() {
            base.Update();
            //if(isEnemyDead){
            //    // For some weird reason I can't get an RPC to get called from HitEnemy() (works from other methods), so we do this workaround. We just want the enemy to stop playing the song.
            //    if(!isDeadAnimationDone){ 
            //        LogIfDebugBuild("Stopping enemy voice with janky code.");
            //        isDeadAnimationDone = true;
            //        creatureVoice.Stop();
            //        creatureVoice.PlayOneShot(dieSFX);
            //    }
            //    return;
            //}
            //timeSinceHittingLocalPlayer += Time.deltaTime;
            //timeSinceNewRandPos += Time.deltaTime;
            
            var state = currentBehaviourStateIndex;
            //if(targetPlayer != null && (state == (int)State.StickingInFrontOfPlayer || state == (int)State.HeadSwingAttackInProgress)){
            //    turnCompass.LookAt(targetPlayer.gameplayCamera.transform.position);
            //    transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(new Vector3(0f, turnCompass.eulerAngles.y, 0f)), 4f * Time.deltaTime);
            //}
            //if (stunNormalizedTimer > 0f)
            //{
            //    agent.speed = 0f;
            //}
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead) return;

            switch (currentBehaviourStateIndex)
            {

                case (int)State.Observing:
                    if (!hasSpawnedSpectre)
                    {
                        SpawnObserverSpectre();
                    }
                    int activePlayers = Mathf.Min(StartOfRound.Instance.connectedPlayersAmount + 1, 4);
                    int scrapThreshold = 2 - activePlayers;

                    PlayerControllerB greedyPlayer = ObserverScrapListener.CheckForGreedyPlayer(scrapThreshold);

                    if (greedyPlayer != null)
                    {
                        targetPlayer = greedyPlayer;
                        Plugin.Logger.LogInfo("greedy boy");
                        SwitchToBehaviourClientRpc((int)State.MarkingPlayer);
                    }
                    break;

                case (int)State.MarkingPlayer:
                    ManageMarkingState();
                    break;

                case (int)State.Chase:
                    ManageChaseState();

                    break;

                case (int)State.Cooldown:
                    if (!isStartingCooldown)
                    {
                        isStartingCooldown = true;
                        StartCoroutine(ManageCooldownState());
                    }

                    break;
            }
        }

        [ClientRpc]
        public void SetChasePhysicsClientRpc(bool enablePhysics)
        {
            GetComponentInChildren<Collider>().enabled = enablePhysics;
            if (enablePhysics == false)
            {
                agent.speed = 0f;
            }
        }

        void ManageMarkingState()
        {
            if (targetPlayer == null) return;
            if (Vector3.Distance(targetPlayer.transform.position, mainEntrancePosition) < 10f)
            {
                Plugin.Logger.LogInfo("SIREN SOUNDING!!!");

                if (!isPlayingMarkAnimation)
                {
                    isPlayingMarkAnimation = true;
                    StartCoroutine(MarkAndChaseSequence());
                }

                // creatureAnimator.SetTrigger("WakeUp");
            }
        }

        IEnumerator MarkAndChaseSequence()
        {
            //Play Map-Wide Siren
            Plugin.Logger.LogInfo("SIREN SOUNDING! The trap is sprung!");

            activeObserver.GetComponentInChildren<Animator>().SetTrigger("isMarked");

            yield return new WaitForSeconds(10f);

            DespawnObserverSpectre();

            SwitchToBehaviourClientRpc((int)State.Chase);
            isPlayingMarkAnimation = false;
        }

        void ManageChaseState()
        {
            if (targetPlayer == null) return;
            GetComponentInChildren<Collider>().enabled = true;
            //creatureAnimator.SetTrigger("WakeUp");
            SetDestinationToPosition(targetPlayer.transform.position);

            chaseTimer += Time.deltaTime;

            if (chaseTimer > 60f)
            {
                chaseTimer = 0f;
                GetComponentInChildren<Animator>().SetTrigger("collapseChaser");
                SetChasePhysicsClientRpc(false);

                SwitchToBehaviourClientRpc((int)State.Cooldown);
            }
        }


        IEnumerator ManageCooldownState()
        {
            targetPlayer = null;
            yield return new WaitForSeconds(30f);
            isStartingCooldown = false;
            SwitchToBehaviourClientRpc((int)State.Observing);
        }

        public override void OnCollideWithPlayer(Collider other)
        {
            if (timeSinceHittingLocalPlayer < 1f)
            {
                return;
            }
            PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(other);

            if (playerControllerB != null)
            {
                Plugin.Logger.LogInfo("Chaser stabbed the player");
                timeSinceHittingLocalPlayer = 0f;
                playerControllerB.DamagePlayer(20);
            }
        }

        //void StickingInFrontOfPlayer() {
        //    // We only run this method for the host because I'm paranoid about randomness not syncing I guess
        //    // This is fine because the game does sync the position of the enemy.
        //    // Also the attack is a ClientRpc so it should always sync
        //    if (targetPlayer == null || !IsOwner) {
        //        return;
        //    }
        //    if(timeSinceNewRandPos > 0.7f){
        //        timeSinceNewRandPos = 0;
        //        if(enemyRandom.Next(0, 5) == 0){
        //            // Attack
        //            StartCoroutine(SwingAttack());
        //        }
        //        else{
        //            // Go in front of player
        //            positionRandomness = new Vector3(enemyRandom.Next(-2, 2), 0, enemyRandom.Next(-2, 2));
        //            StalkPos = targetPlayer.transform.position - Vector3.Scale(new Vector3(-5, 0, -5), targetPlayer.transform.forward) + positionRandomness;
        //        }
        //        SetDestinationToPosition(StalkPos, checkForPath: false);
        //    }
        ////}

        //IEnumerator SwingAttack() {
        //    SwitchToBehaviourClientRpc((int)State.HeadSwingAttackInProgress);
        //    StalkPos = targetPlayer.transform.position;
        //    SetDestinationToPosition(StalkPos);
        //    yield return new WaitForSeconds(0.5f);
        //    if(isEnemyDead){
        //        yield break;
        //    }
        //    DoAnimationClientRpc("swingAttack");
        //    yield return new WaitForSeconds(0.35f);
        //    SwingAttackHitClientRpc();
        //    // In case the player has already gone away, we just yield break (basically same as return, but for IEnumerator)
        //    if(currentBehaviourStateIndex != (int)State.HeadSwingAttackInProgress){
        //        yield break;
        //    }
        //    SwitchToBehaviourClientRpc((int)State.StickingInFrontOfPlayer);
        //}

        //public override void OnCollideWithPlayer(Collider other) {
        //    if (timeSinceHittingLocalPlayer < 1f) {
        //        return;
        //    }
        //    PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(other);
        //    if (playerControllerB != null)
        //    {
        //        LogIfDebugBuild("Chaser collision with Player!");
        //        timeSinceHittingLocalPlayer = 0f;
        //        playerControllerB.DamagePlayer(20);
        //    }
        //}

        //public override void HitEnemy(int force = 1, PlayerControllerB? playerWhoHit = null, bool playHitSFX = false, int hitID = -1) {
        //    base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
        //    if(isEnemyDead){
        //        return;
        //    }
        //    enemyHP -= force;
        //    if (IsOwner) {
        //        if (enemyHP <= 0 && !isEnemyDead) {
        //            // Our death sound will be played through creatureVoice when KillEnemy() is called.
        //            // KillEnemy() will also attempt to call creatureAnimator.SetTrigger("KillEnemy"),
        //            // so we don't need to call a death animation ourselves.

        //            StopCoroutine(SwingAttack());
        //            // We need to stop our search coroutine, because the game does not do that by default.
        //            StopCoroutine(searchCoroutine);
        //            KillEnemyOnOwnerClient();
        //        }
        //    }
        //}

        //[ClientRpc]
        //public void DoAnimationClientRpc(string animationName) {
        //    LogIfDebugBuild($"Animation: {animationName}");
        //    creatureAnimator.SetTrigger(animationName);
        //}

        //[ClientRpc]
        //public void SwingAttackHitClientRpc() {
        //    LogIfDebugBuild("SwingAttackHitClientRPC");
        //    int playerLayer = 1 << 3; // This can be found from the game's Asset Ripper output in Unity
        //    Collider[] hitColliders = Physics.OverlapBox(attackArea.position, attackArea.localScale, Quaternion.identity, playerLayer);
        //    if(hitColliders.Length > 0){
        //        foreach (var player in hitColliders){
        //            PlayerControllerB playerControllerB = MeetsStandardPlayerCollisionConditions(player);
        //            if (playerControllerB != null)
        //            {
        //                LogIfDebugBuild("Swing attack hit player!");
        //                timeSinceHittingLocalPlayer = 0f;
        //                playerControllerB.DamagePlayer(40);
        //            }
        //        }
        //    }
        //}
    }
}