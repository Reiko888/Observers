using GameNetcodeStuff;
using HarmonyLib;
using Observers.src;
using System.Collections;
using System.Collections.Generic;
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
        private GameObject activeSiren;
        private float attackCooldown = 0f;
        private Ray enemyRay;
        private RaycastHit enemyRayHit;
        private int currentFootstepSurfaceIndex;
        private int previousFootstepClip;
        public AudioSource movementAudio;
        public GameObject knifeModel;
        public Item knifeItemPrefab;
        public AudioClip[] knifeStabSounds;
        private DoorLock[] cachedDoors;
        private float slowScanTimer = 0f;
        private const float SLOW_SCAN_INTERVAL = 2.0f;
        private Dictionary<DoorLock, float> doorCooldowns = new Dictionary<DoorLock, float>();
        public AudioClip chaserDeathSound;
        public AudioClip chaserReanimateSound;


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

        public override void Start()
        {
            base.Start();
            LogIfDebugBuild("Observers Spawned");
            enemyHP = 4;
            timeSinceNewRandPos = 0;
            enemyRandom = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);
            isDeadAnimationDone = false;

            Animator chaserAnim = GetComponentInChildren<Animator>();
            if (chaserAnim != null)
            {
                chaserAnim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                chaserAnim.enabled = true;
            }

            if (creatureSFX != null)
            {
                creatureSFX.spatialize = false;
            }

            base.GetAINodes();
            mainEntrancePosition = RoundManager.FindMainEntrancePosition(true, isOutside);

            if (!isOutside && allAINodes != null && allAINodes.Length > 0)
            {
                Transform randomNode = allAINodes[enemyRandom.Next(0, allAINodes.Length)].transform;
                agent.Warp(randomNode.position);
                SyncPositionToClients();
                Plugin.Logger.LogInfo("Warped Chaser to a random internal AI node.");
            }

            currentBehaviourStateIndex = (int)State.Observing;
        }

        public override void OnDestroy()
        { 
            base.OnDestroy();
            if (this.activeObserver != null) Destroy(this.activeObserver);
        }

        [ClientRpc]
        public void DoAnimationClientRpc(string animationTrigger)
        {
            Animator chaserAnim = GetComponentInChildren<Animator>();
            if (chaserAnim != null)
            {
                chaserAnim.SetTrigger(animationTrigger);
                Plugin.Logger.LogInfo($"Triggered animation: {animationTrigger}");
            }
        }

        [ClientRpc]
        public void PlayChaserSFXClientRpc(string sfxType)
        {
            if (sfxType == "reanimate")
            {
                creatureSFX.Play();
            }
            else if (sfxType == "death" && dieSFX != null)
            {
                creatureVoice.PlayOneShot(dieSFX);
            }
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

            GameObject[] outsideNodes = GameObject.FindGameObjectsWithTag("OutsideAINode");
            Vector3 spawnPos = doorPos; // Default fallback
            float closestDist = float.MaxValue;

            if (outsideNodes != null)
            {
                foreach (GameObject node in outsideNodes)
                {
                    float dist = Vector3.Distance(doorPos, node.transform.position);

                    if (dist > 12f && dist < 40f && dist < closestDist)
                    {
                        closestDist = dist;
                        spawnPos = node.transform.position;
                    }
                }

                if (closestDist == float.MaxValue && outsideNodes.Length > 0)
                {
                    spawnPos = outsideNodes[0].transform.position;
                    foreach (GameObject node in outsideNodes)
                    {
                        if (Vector3.Distance(doorPos, node.transform.position) < Vector3.Distance(doorPos, spawnPos))
                        {
                            spawnPos = node.transform.position;
                        }
                    }
                }
            }

            spawnPos.y += 15f;

            Vector3 lookDirection = doorPos - spawnPos;
            lookDirection.y = 0;
            Quaternion spawnRot = Quaternion.LookRotation(lookDirection);

            if (Plugin.ObserverPrefab != null)
            {
                Plugin.Logger.LogInfo($"Spawning Spectre at {spawnPos} to watch the door.");
                hasSpawnedSpectre = true;
                SpawnSpectreClientRpc(spawnPos, spawnRot);
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

        public override void Update()
        {
            base.Update();
            if (attackCooldown > 0f)
            {
                attackCooldown -= Time.deltaTime;
            }

            if (!isEnemyDead)
            {
                slowScanTimer += Time.deltaTime;
                if (slowScanTimer >= SLOW_SCAN_INTERVAL)
                {
                    slowScanTimer = 0f;
                    cachedDoors = FindObjectsOfType<DoorLock>();
                }
            }

            if (!isEnemyDead && currentBehaviourStateIndex == (int)State.Chase)
            {
                chaseTimer += Time.deltaTime;
                if (chaseTimer > 40f)
                {
                    chaseTimer = 0f;
                    agent.speed = 0f;

                    DoAnimationClientRpc("hasBeenKilled");
                    SetChasePhysicsClientRpc(false);

                    ToggleVisualKnifeClientRpc(false);
                    if (IsServer) SpawnKnifeItemLocally();

                    if (dieSFX != null) creatureVoice.PlayOneShot(dieSFX);

                    SwitchToBehaviourClientRpc((int)State.Cooldown);
                }
            }

            var state = currentBehaviourStateIndex;
        }

        public void LateUpdate()
        {
            if (activeObserver != null && !isEnemyDead &&
                (currentBehaviourStateIndex == (int)State.Observing || currentBehaviourStateIndex == (int)State.MarkingPlayer))
            {
                PlayerControllerB playerToWatch = targetPlayer;
                if (playerToWatch == null) playerToWatch = GetClosestPlayer(requireLineOfSight: false);

                if (playerToWatch != null)
                {
                    Animator obsAnim = activeObserver.GetComponentInChildren<Animator>();
                    if (obsAnim != null) obsAnim.applyRootMotion = false;

                    Vector3 lookDir = playerToWatch.gameplayCamera.transform.position - activeObserver.transform.position;

                    lookDir.y = 0;

                    if (lookDir != Vector3.zero)
                    {
                        Quaternion targetRot = Quaternion.LookRotation(lookDir);
                        activeObserver.transform.rotation = Quaternion.Slerp(activeObserver.transform.rotation, targetRot, Time.deltaTime * 5f);
                    }
                }
            }
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

            Vector3 triggerPos = activeObserver != null ? activeObserver.transform.position : RoundManager.FindMainEntrancePosition(true, isOutside);

            if (Vector3.Distance(targetPlayer.transform.position, triggerPos) < 15f)
            {
                if (!isPlayingMarkAnimation)
                {
                    isPlayingMarkAnimation = true;
                    StartCoroutine(MarkAndChaseSequence());
                }
            }
        }

        [ClientRpc]
        public void SpectreAnimationClientRpc(string animationTrigger)
        {
            if (activeObserver != null)
            {
                Animator obsAnim = activeObserver.GetComponentInChildren<Animator>();
                if (obsAnim != null)
                {
                    obsAnim.SetTrigger(animationTrigger);
                    Plugin.Logger.LogInfo($"Triggered Spectre animation: {animationTrigger}");
                }
            }
        }

        IEnumerator MarkAndChaseSequence()
        {
            Plugin.Logger.LogInfo("!!SIREN SOUNDING!!");

            if (Plugin.SirenObj != null && activeSiren == null)
            {
                Vector3 skyPosition = mainEntrancePosition + (Vector3.up * 40f);
                activeSiren = Instantiate(Plugin.SirenObj, skyPosition, Quaternion.identity);
                activeSiren.GetComponent<NetworkObject>().Spawn();
            }

            SpectreAnimationClientRpc("isMarked");

            yield return new WaitForSeconds(15f);

            DespawnObserverSpectre();
            DoAnimationClientRpc("hasBeenSummoned");
            ToggleVisualKnifeClientRpc(true);
            PlayChaserSFXClientRpc("reanimate");
            StartCoroutine(FlickerLightsDuringRise(13.5f));
            yield return new WaitForSeconds(13.5f);
            DoAnimationClientRpc("riseFinished");
            if (!creatureVoice.isPlaying)
            {
                creatureVoice.Play();
            }
            SwitchToBehaviourClientRpc((int)State.Chase);
            isPlayingMarkAnimation = false;
        }

        public void GetMaterialStandingOn()
        {
            enemyRay = new Ray(base.transform.position + Vector3.up, -Vector3.up);
            if (Physics.Raycast(enemyRay, out enemyRayHit, 6f, StartOfRound.Instance.walkableSurfacesMask, QueryTriggerInteraction.Ignore))
            {
                if (!enemyRayHit.collider.CompareTag(StartOfRound.Instance.footstepSurfaces[currentFootstepSurfaceIndex].surfaceTag))
                {
                    for (int i = 0; i < StartOfRound.Instance.footstepSurfaces.Length; i++)
                    {
                        if (enemyRayHit.collider.CompareTag(StartOfRound.Instance.footstepSurfaces[i].surfaceTag))
                        {
                            currentFootstepSurfaceIndex = i;
                            return;
                        }
                    }
                }
            }
        }

        [ClientRpc]
        public void ToggleVisualKnifeClientRpc(bool enable)
        {
            if (knifeModel != null)
            {
                knifeModel.SetActive(enable);
            }
        }

        public void SpawnKnifeItemLocally()
        {
            if (!IsServer) return;

            Item knifeToDrop = null;
            foreach (Item item in StartOfRound.Instance.allItemsList.itemsList)
            {
                if (item.itemName == "Kitchen knife" || item.itemName == "Knife")
                {
                    knifeToDrop = item;
                    break;
                }
            }

            if (knifeToDrop == null)
            {
                Plugin.Logger.LogWarning("Could not find the Knife item in the game's item database!");
                return;
            }

            Vector3 spawnPos;
            if (knifeModel != null)
            {
                spawnPos = knifeModel.transform.position;
            }
            else
            {
                spawnPos = transform.position + Vector3.up * 1.5f;
            }

            GameObject knifeDrop = Instantiate(knifeToDrop.spawnPrefab, spawnPos, Quaternion.identity, RoundManager.Instance.spawnedScrapContainer);
            GrabbableObject knifeGrabbable = knifeDrop.GetComponent<GrabbableObject>();

            knifeGrabbable.fallTime = 0f;
            knifeGrabbable.targetFloorPosition = knifeGrabbable.GetItemFloorPosition(spawnPos);

            int scrapValue = (int)(UnityEngine.Random.Range(knifeToDrop.minValue, knifeToDrop.maxValue) * RoundManager.Instance.scrapValueMultiplier);
            knifeGrabbable.SetScrapValue(scrapValue);

            knifeDrop.GetComponent<NetworkObject>().Spawn();
            Plugin.Logger.LogInfo("Dropped knife scrap from the Chaser's hand!");
        }

        public void PlayFootstepSound()
        {
            Plugin.Logger.LogInfo("Footstep event fired!");
            GetMaterialStandingOn();
            int num = UnityEngine.Random.Range(0, StartOfRound.Instance.footstepSurfaces[currentFootstepSurfaceIndex].clips.Length);
            if (num == previousFootstepClip)
            {
                num = (num + 1) % StartOfRound.Instance.footstepSurfaces[currentFootstepSurfaceIndex].clips.Length;
            }
            movementAudio.pitch = UnityEngine.Random.Range(0.93f, 1.07f);
            movementAudio.PlayOneShot(StartOfRound.Instance.footstepSurfaces[currentFootstepSurfaceIndex].clips[num], 1f);
            previousFootstepClip = num;
        }

        public override void AnimationEventA()
        {
            base.AnimationEventA();
            PlayFootstepSound();
        }

        void ManageChaseState()
        {
            if (targetPlayer == null) return;
            GetComponentInChildren<Collider>().enabled = true;
            agent.speed = 8f;

            BustOpenDoorsInRadius();

            bool playerIsOutside = !targetPlayer.isInsideFactory;
            bool chaserIsOutside = this.isOutside;

            if (playerIsOutside != chaserIsOutside)
            {
                Vector3 doorInEnemyZone = RoundManager.FindMainEntrancePosition(true, chaserIsOutside);
                SetDestinationToPosition(doorInEnemyZone);

                if (Vector3.Distance(transform.position, doorInEnemyZone) < 4f)
                {
                    Plugin.Logger.LogInfo("Chaser is traversing the main door!");
                    bool goingOutside = !chaserIsOutside;
                    Vector3 newPos = RoundManager.FindMainEntrancePosition(true, goingOutside);
                    TeleportEnemyServerRpc(newPos, goingOutside);
                }
            }
            else
            {
                SetDestinationToPosition(targetPlayer.transform.position);
            }
        }

        private void BustOpenDoorsInRadius()
        {
            if (!IsServer || cachedDoors == null) return;

            float blastRangeSq = 16f;

            foreach (DoorLock door in cachedDoors)
            {
                if (door == null) continue;

                if ((transform.position - door.transform.position).sqrMagnitude < blastRangeSq)
                {
                    bool isOnCooldown = doorCooldowns.ContainsKey(door) && (Time.time - doorCooldowns[door] < 3f);
                    if (isOnCooldown) continue;

                    AnimatedObjectTrigger doorSwing = door.GetComponent<AnimatedObjectTrigger>();

                    if (doorSwing != null && !doorSwing.boolValue)
                    {
                        if (door.isLocked)
                        {
                            door.UnlockDoorServerRpc();
                        }

                        doorSwing.TriggerAnimationNonPlayer(true, true, false);
                        door.OpenDoorAsEnemyServerRpc();

                        Plugin.Logger.LogInfo("Chaser blasted a door open from the cache!");

                        doorCooldowns[door] = Time.time;
                    }
                }
            }
        }

        private IEnumerator FlickerLightsDuringRise(float duration)
        {
            Light[] allLights = FindObjectsOfType<Light>();
            Dictionary<Light, float> originalIntensities = new Dictionary<Light, float>();
            List<Light> nearbyLights = new List<Light>();

            float radiusSq = 20f * 20f; // 20 meter radius

            foreach (Light l in allLights)
            {
                if (l == null || l.type == LightType.Directional) continue;

                string nameLower = l.gameObject.name.ToLower();
                if (nameLower.Contains("helmet") || nameLower.Contains("visor") || nameLower.Contains("sun")) continue;

                if ((l.transform.position - transform.position).sqrMagnitude <= radiusSq)
                {
                    nearbyLights.Add(l);
                    originalIntensities[l] = l.intensity;
                }
            }

            float timer = 0f;
            while (timer < duration)
            {
                foreach (Light l in nearbyLights)
                {
                    if (l != null && originalIntensities.ContainsKey(l))
                    {
                        float rand = UnityEngine.Random.value;
                        if (rand > 0.8f) l.intensity = originalIntensities[l] * 2.5f; // Intense flash
                        else if (rand > 0.5f) l.intensity = originalIntensities[l] * 0.1f; // Dim out
                        else l.intensity = originalIntensities[l]; // Normal
                    }
                }

                yield return new WaitForSeconds(0.1f);
                timer += 0.1f;
            }

            foreach (var kvp in originalIntensities)
            {
                if (kvp.Key != null) kvp.Key.intensity = kvp.Value;
            }
        }

        [ServerRpc]
        public void TeleportEnemyServerRpc(Vector3 pos, bool setOutside)
        {
            TeleportEnemyClientRpc(pos, setOutside);
        }

        [ClientRpc]
        public void TeleportEnemyClientRpc(Vector3 pos, bool setOutside)
        {
            TeleportEnemyLocally(pos, setOutside);
        }

        private void TeleportEnemyLocally(Vector3 pos, bool setOutside)
        {
            agent.enabled = false;
            transform.position = pos;
            agent.enabled = true;
            serverPosition = pos;
            SetEnemyOutside(setOutside);
            mainEntrancePosition = RoundManager.FindMainEntrancePosition(true, isOutside);

            EntranceTeleport door = RoundManager.FindMainEntranceScript(setOutside);
            if (door != null && door.doorAudios != null && door.doorAudios.Length > 0)
            {
                door.entrancePointAudio.PlayOneShot(door.doorAudios[0]);
            }
        }

        IEnumerator ManageCooldownState()
        {
            targetPlayer = null;
            yield return new WaitForSeconds(30f);
            isStartingCooldown = false;
            SwitchToBehaviourClientRpc((int)State.Observing);
            creatureVoice.Stop();
        }

        public override void OnCollideWithPlayer(Collider other)
        {
            base.OnCollideWithPlayer(other);

            if (isEnemyDead || currentBehaviourStateIndex != (int)State.Chase || attackCooldown > 0f) return;

            PlayerControllerB player = MeetsStandardPlayerCollisionConditions(other, false, true);

            if (player != null)
            {
                Plugin.Logger.LogInfo("Chaser stabbed the player!");
                attackCooldown = 1.5f;

                DoAnimationClientRpc("stabAttack");

                if (knifeStabSounds != null && knifeStabSounds.Length > 0)
                {
                    int randomIndex = UnityEngine.Random.Range(0, knifeStabSounds.Length);
                    if (knifeStabSounds[randomIndex] != null)
                    {
                        creatureSFX.PlayOneShot(knifeStabSounds[randomIndex], 1f);
                    }
                }

                player.DamagePlayer(20, hasDamageSFX: true, callRPC: true, CauseOfDeath.Stabbing);
            }
        }

        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            if (isEnemyDead) return;

            enemyHP -= force;

            if (enemyHP > 0)
            {
                DoAnimationClientRpc("TakeDamage");
            }

            if (enemyHP <= 0 && IsOwner)
            {
                KillEnemyOnOwnerClient(true);
            }
        }

        public override void KillEnemy(bool destroy = false)
        {
            base.KillEnemy(destroy);
            isEnemyDead = true;
            agent.speed = 0f;

            DoAnimationClientRpc("hasBeenKilled");
            PlayChaserSFXClientRpc("death");
            creatureVoice.Stop();
            ToggleVisualKnifeClientRpc(false);
            if (IsServer) SpawnKnifeItemLocally();

            if (activeSiren != null)
            {
                activeSiren.GetComponent<NetworkObject>().Despawn();
            }

            DespawnObserverSpectre();
            Plugin.Logger.LogInfo("The Chaser has been killed.");
        }
    }
}