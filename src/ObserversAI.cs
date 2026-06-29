using GameNetcodeStuff;
using HarmonyLib;
using Observers.src;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Observers {

    class ObserversAI : EnemyAI
    {
#pragma warning disable 0649
        public GameObject ObserverPrefab;
        public GameObject activeObserver;
        public GameObject activeChaserModel;
        public Transform attackArea = null!;
        public int SirenAudioSourceIndex = 0;
        public Vector3 SpectreRotationOffset = new Vector3(0, -90, 0);
        public float SirenDelay = 6.5f;
        public ScreenShakeType SirenShakeType = ScreenShakeType.Small;
        public float AirRaidLength = 60f;
        private Dictionary<int, Color> originalEmissions = new Dictionary<int, Color>();

        [Header("Door Prying")]
        public HangarShipDoor shipDoor;
        public bool isPryingDoor;
        private bool inSpecialAnimation;
        private float pryingDoorAnimTime;
        public float pryOpenDoorAnimLength = 3f;
        public AudioClip breakAndEnter;
        public AudioClip shipAlarm;
        public AudioSource breakDownDoorAudio;
        private int queuedState = -1;
        private Animator chaserAnimator;
#pragma warning restore 0649
        System.Random enemyRandom = null!;
        bool hasSpawnedSpectre = false;
        Vector3 mainEntrancePosition;
        bool isPlayingMarkAnimation = false;
        float chaseTimer = 0f;
        bool isStartingCooldown = false;
        private float attackCooldown = 0f;
        private Ray enemyRay; 
        private RaycastHit enemyRayHit;
        public AudioClip[] customFootstepSounds;
        private float footstepTimer = 0f;
        public AudioSource movementAudio;
        public GameObject knifeModel;
        public AudioClip[] knifeStabSounds;
        private DoorLock[] cachedDoors;
        private float slowScanTimer = 0f;
        private const float SLOW_SCAN_INTERVAL = 2.0f;
        private Dictionary<DoorLock, float> doorCooldowns = new Dictionary<DoorLock, float>();
        public AudioClip chaserDeathSound;
        public AudioClip chaserReanimateSound;
        private Vector3 lastPosition;
        private MineshaftElevatorController elevatorScript;
        private bool isInElevatorStartRoom = false;
        private bool isHalted = false;
        public AudioSource markedLineAudio;
        public ParticleSystem manifestationParticles;
        public SkinnedMeshRenderer chaserRenderer;
        public Mesh[] randomMeshes;
        public GameObject[] meshVariants;
        public GameObject[] knifeModels;
        private EntranceTeleport[] allTeleports;
        public GameObject possessionSwarmParticles;
        public AudioClip tauntVoiceline;
        public Transform handBone;
        private ScanNodeProperties cachedScanNode;
        private bool isStunned = false;
        private float slowDownTimer = 0f;
        private float currentChaseDuration = 0f;
        private bool isBlasting = false;
        private float cameraShakeTimer = 0f;

        enum State {
            Observing,
            MarkingPlayer,
            Chase,
            Cooldown
        }

        public void SyncStateSafely(int newStateIndex)
        {
            if (inSpecialAnimation)
            {
                queuedState = newStateIndex;
                return;
            }

            if (IsServer)
            {
                SyncStateSafelyClientRpc(newStateIndex);
            }
            else
            {
                SyncStateSafelyServerRpc(newStateIndex);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void SyncStateSafelyServerRpc(int newStateIndex)
        {
            SyncStateSafelyClientRpc(newStateIndex);
        }

        [ClientRpc]
        private void SyncStateSafelyClientRpc(int newStateIndex)
        {
            currentBehaviourStateIndex = newStateIndex;
            Plugin.Logger.LogInfo($"Observer State changed to {(State)newStateIndex}");
        }

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text) {
            Plugin.Logger.LogInfo(text);
        }

        public override void Start()
        {
            base.Start();
            LogIfDebugBuild("Observer Spawned");
            isHalted = true;
            enemyHP = 2;

            shipDoor = FindObjectOfType<HangarShipDoor>();
            chaserAnimator = GetComponentInChildren<Animator>();

            if (enemyType != null)
            {
                enemyType.stunTimeMultiplier = 2;
            }

            if (attackArea != null)
            {
                Vector3 newScale = attackArea.localScale;
                newScale.z = 2;
                attackArea.localScale = newScale;

                Vector3 newPos = attackArea.localPosition;
                newPos.z = newScale.z / 2f;
                attackArea.localPosition = newPos;
            }

            SetupChaserMesh();

            if (movementAudio != null)
            {
                movementAudio.spatialize = false;
                if (SoundManager.Instance != null && SoundManager.Instance.diageticMixer != null)
                {
                    movementAudio.outputAudioMixerGroup = SoundManager.Instance.diageticMixer.FindMatchingGroups("Master")[0];
                }
            }

            if (creatureSFX != null) creatureSFX.spatialize = false;
            Animator chaserAnim = GetComponentInChildren<Animator>();
            if (chaserAnim != null)
            {
                chaserAnim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                chaserAnim.enabled = true;
                chaserAnim.applyRootMotion = false;
            }

            base.GetAINodes();
            mainEntrancePosition = RoundManager.FindMainEntrancePosition(true, isOutside);

            if (!isOutside && allAINodes != null && allAINodes.Length > 0)
            {
                Transform randomNode = allAINodes[enemyRandom.Next(0, allAINodes.Length)].transform;
                agent.Warp(randomNode.position);
                SyncPositionToClients();
                Plugin.Logger.LogInfo("Warped Chaser to a random internal AI node");
            }

            currentBehaviourStateIndex = (int)State.Observing;
        }



        private void SetupChaserMesh()
        {
            enemyRandom = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);
            if (meshVariants != null && meshVariants.Length > 0)
            {
                foreach (GameObject variant in meshVariants)
                {
                    if (variant != null) variant.SetActive(false);
                }

                int randomMeshIndex = enemyRandom.Next(0, meshVariants.Length);
                activeChaserModel = meshVariants[randomMeshIndex];

                if (activeChaserModel != null)
                {
                    activeChaserModel.SetActive(true);
                }
                
                Plugin.Logger.LogInfo($"Variant {randomMeshIndex} active.");
            }
        }

        private void BeginPryOpenDoor()
        {
            DoAnimationClientRpc("isPryingDoor", true, true);
            SetPryingDoorClientRpc(true);
        }

        private void FinishPryOpenDoor()
        {
            DoAnimationClientRpc("isPryingDoor", true, false);
            SetPryingDoorClientRpc(false);
        }

        [ClientRpc]
        public void SetPryingDoorClientRpc(bool state)
        {
            isPryingDoor = state;
            inSpecialAnimation = state;
            agent.enabled = !state;
            
            if (state)
            {
                pryingDoorAnimTime = 0f;
                if (shipDoor != null)
                {
                    shipDoor.shipDoorsAnimator.SetBool("PryingOpenDoor", true);
                    shipDoor.shipDoorsAnimator.SetFloat("pryOpenDoor", 0f);
                }
                if (breakDownDoorAudio != null && breakAndEnter != null)
                {
                    breakDownDoorAudio.PlayOneShot(breakAndEnter);
                    WalkieTalkie.TransmitOneShotAudio(breakDownDoorAudio, breakAndEnter);
                }
                RoundManager.Instance.PlayAudibleNoise(base.transform.position, 15f, 0.9f);
                if (shipAlarm != null)
                {
                    StartOfRound.Instance.speakerAudioSource.PlayOneShot(shipAlarm);
                    WalkieTalkie.TransmitOneShotAudio(StartOfRound.Instance.speakerAudioSource, shipAlarm);
                }
                if (Vector3.Distance(StartOfRound.Instance.audioListener.transform.position, base.transform.position) < 18f)
                {
                    HUDManager.Instance.ShakeCamera(ScreenShakeType.VeryStrong);
                }
            }
            else
            {
                if (shipDoor != null) 
                {
                    shipDoor.shipDoorsAnimator.SetBool("Closed", false);
                    shipDoor.shipDoorsAnimator.SetBool("PryingOpenDoor", false);
                    StartOfRound.Instance.SetShipDoorsClosed(false);
                    StartOfRound.Instance.SetShipDoorsOverheatLocalClient();
                    shipDoor.doorPower = 0f;
                }
                
                if (queuedState != -1)
                {
                    if (IsServer) SyncStateSafely(queuedState);
                    queuedState = -1;
                }
            }
        }

        public bool BreakIntoShip()
        {
            if (shipDoor == null)
            {
                return false;
            }
            if (isPryingDoor)
            {
                if (pryingDoorAnimTime >= 1f)
                {
                    FinishPryOpenDoor();
                }
                return true;
            }
            if (StartOfRound.Instance.hangarDoorsClosed && targetPlayer != null && targetPlayer.isInHangarShipRoom && Vector3.Distance(base.transform.position, shipDoor.outsideDoorPoint.position) < 4f)
            {
                BeginPryOpenDoor();
                return true;
            }
            return false;
        }

        public override void OnDestroy()
        {
            base.OnDestroy();

            if (activeObserver != null) Destroy(activeObserver);
            if (creatureVoice != null) creatureVoice.Stop();

            Plugin.Logger.LogInfo("Enemy destroyed");
        }

        private void OnEnable()
        {
            ObserversEventManager.OnShipLeft += HandleShipLeft;
            ObserversEventManager.OnPlayerDied += HandlePlayerDeathOrDC;
            ObserversEventManager.OnPlayerDisconnect += HandlePlayerDeathOrDC;
        }

        private void OnDisable()
        {
            ObserversEventManager.OnShipLeft -= HandleShipLeft;
            ObserversEventManager.OnPlayerDied -= HandlePlayerDeathOrDC;
            ObserversEventManager.OnPlayerDisconnect -= HandlePlayerDeathOrDC;
        }

        private void HandleShipLeft()
        {
            Plugin.Logger.LogInfo("Ship is leaving! Cleaning up");

            if (creatureVoice != null) creatureVoice.Stop();
            SpectreSirenClientRpc(false);

            DespawnObserverSpectre();
            isHalted = false;
            targetPlayer = null;
            ObserverScrapListener.ClearData();
        }

        private void HandlePlayerDeathOrDC(PlayerControllerB player)
        {
            if (IsServer && !isEnemyDead && targetPlayer == player)
            {
                Plugin.Logger.LogInfo("Target died or disconnected.");

                if (currentBehaviourStateIndex == (int)State.Chase || currentBehaviourStateIndex == (int)State.MarkingPlayer)
                {
                    TriggerChaseEndClientRpc();
                }
            }
        }

        [ClientRpc]
        public void DoAnimationClientRpc(string animationTrigger, bool isBool = false, bool state = false)
        {
            Animator chaserAnim = GetComponentInChildren<Animator>();
            if (chaserAnim != null)
            {
                if (isBool) chaserAnim.SetBool(animationTrigger, state);
                else chaserAnim.SetTrigger(animationTrigger);
            }
        }

        [ClientRpc]
        public void PlayVictimWhisperClientRpc(int victimClientId)
        {
            if ((int)GameNetworkManager.Instance.localPlayerController.playerClientId == victimClientId)
            {
                if (markedLineAudio != null)
                {
                    markedLineAudio.Play();
                }
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

        [ClientRpc]
        public void PlayClientFXClientRpc(int fxType)
        {
            if (fxType == 0)
            {
                StartCoroutine(FlickerLightsDuringRise(7f));
            }
            else if (fxType == 1)
            {
                if (!creatureVoice.isPlaying)
                {
                    creatureVoice.Play();
                }
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

            if (spawnPos.y < doorPos.y)
            {
                spawnPos.y = doorPos.y + 5f;
            }

            Vector3 lookDirection = doorPos - spawnPos;
            lookDirection.y = 0;
            Quaternion spawnRot = Quaternion.LookRotation(lookDirection);

            if (ObserverPrefab != null)
            {
                Plugin.Logger.LogInfo($"Spawning Spectre at {spawnPos} to watch the door.");
                hasSpawnedSpectre = true;
                SpawnSpectreClientRpc(spawnPos, spawnRot);
            }
        }

        [ClientRpc]
        public void SpawnSpectreClientRpc(Vector3 spawnPos, Quaternion spawnRot)
        {
            if (activeObserver == null && ObserverPrefab != null)
            {
                activeObserver = Instantiate(ObserverPrefab, spawnPos, spawnRot);

                Terminal terminal = FindObjectOfType<Terminal>();
                if (terminal != null)
                {
                    foreach (TerminalNode node in terminal.enemyFiles)
                    {
                        if (node != null && node.creatureName.Contains("Observer"))
                        {
                            foreach (ScanNodeProperties sNode in GetComponentsInChildren<ScanNodeProperties>(true))
                            {
                                if (sNode.nodeType == 1)
                                {
                                    sNode.creatureScanID = node.creatureFileID;
                                }
                            }
                            break;
                        }
                    }
                }
            }

            //Instantiate(ManifestParticles, spawnPos + (Vector3.up * 1.5f), Quaternion.identity);

            ToggleAndMoveScanNode(true, spawnPos);
        }

        [ClientRpc]
        public void SyncTargetClientRpc(int playerId)
        {
            targetPlayer = StartOfRound.Instance.allPlayerScripts[playerId];
            Plugin.Logger.LogInfo($"Target synced to clients: {targetPlayer.playerUsername}");
        }

        [ClientRpc]
        public void PlayTauntInEarClientRpc(ulong targetClientId)
        {
            if (StartOfRound.Instance.localPlayerController.actualClientId == targetClientId)
            {

                if (tauntVoiceline != null)
                {
                    HUDManager.Instance.UIAudio.PlayOneShot(tauntVoiceline, 1f);
                }
                else
                {
                    Plugin.Logger.LogWarning("Taunt voiceline is missing");
                }
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
                Vector3 spawnPos = activeObserver.transform.position;
                if (manifestationParticles != null)
                {
                    ParticleSystem burst = Instantiate(manifestationParticles, spawnPos + (Vector3.up * 1.5f), Quaternion.identity);
                    burst.gameObject.SetActive(true);
                    burst.Play(true);
                    Destroy(burst.gameObject, 3f);
                }
                else
                {
                    Plugin.Logger.LogWarning("Manifest particles are missing in the Inspector! Skipping despawn VFX.");
                }

                Destroy(activeObserver);
            }

            ToggleAndMoveScanNode(false);
        }

        public override void Update()
        {
            if (isPryingDoor && inSpecialAnimation)
            {
                if (shipDoor != null)
                {
                    base.transform.position = Vector3.Lerp(base.transform.position, shipDoor.outsideDoorPoint.position, 7f * Time.deltaTime);
                    base.transform.rotation = Quaternion.Lerp(base.transform.rotation, shipDoor.outsideDoorPoint.rotation, 7f * Time.deltaTime);
                    pryingDoorAnimTime = Mathf.Min(pryingDoorAnimTime + Time.deltaTime / pryOpenDoorAnimLength, 1f);
                    
                    if (chaserAnimator != null) chaserAnimator.SetFloat("pryOpenDoor", pryingDoorAnimTime);
                    shipDoor.shipDoorsAnimator.SetFloat("pryOpenDoor", pryingDoorAnimTime);
                        
                    BreakIntoShip();
                    return;
                }
            }

            base.Update();
            
            if (isBlasting && activeObserver != null)
            {
                cameraShakeTimer -= Time.deltaTime;
                if (cameraShakeTimer <= 0f)
                {
                    cameraShakeTimer = 0.3f;
                    PlayerControllerB localPlayer = GameNetworkManager.Instance.localPlayerController;
                    if (localPlayer != null && !localPlayer.isPlayerDead)
                    {
                        float dist = Vector3.Distance(localPlayer.transform.position, activeObserver.transform.position);
                        if (dist < 35f)
                        {
                            HUDManager.Instance.ShakeCamera(SirenShakeType);
                        }
                    }
                }
            }

            if (stunNormalizedTimer > 0f)
            {
                agent.speed = 0f;

                if (!isStunned)
                {
                    isStunned = true;
                    DoAnimationClientRpc("hasBeenStunned");
                    Plugin.Logger.LogInfo("Chaser has been stunned");
                }
            }
            else
            {
                isStunned = false;
            }

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

            if (!isEnemyDead && enemyHP > 0 && currentBehaviourStateIndex == (int)State.Chase && stunNormalizedTimer <= 0f)
            {
                if (IsServer)
                {
                    currentChaseDuration += Time.deltaTime;
                    if (currentChaseDuration >= 60)
                    {
                        Plugin.Logger.LogInfo("Chase time expired");
                        TriggerChaseEndClientRpc();
                        return;
                    }
                }

                if (attackCooldown <= 0f && targetPlayer != null && attackArea != null)
                {
                    PlayerControllerB localPlayer = GameNetworkManager.Instance.localPlayerController;
                    if (localPlayer != null && !localPlayer.isPlayerDead && localPlayer == targetPlayer)
                    {
                        int playerLayer = 1 << 3;
                        Collider[] hitColliders = Physics.OverlapBox(attackArea.position, attackArea.localScale / 2f, attackArea.rotation, playerLayer);

                        foreach (var playerCollider in hitColliders)
                        {
                            PlayerControllerB player = MeetsStandardPlayerCollisionConditions(playerCollider);

                            if (player == localPlayer)
                            {
                                Plugin.Logger.LogInfo("Target stabbed");
                                localPlayer.DamagePlayer(20, hasDamageSFX: true, callRPC: true, CauseOfDeath.Stabbing);

                                attackCooldown = 1.5f;
                                slowDownTimer = 1.5f;
                                TriggerAttackAnimationServerRpc();
                                break;
                            }
                        }
                    }
                }
            }

            else if (currentBehaviourStateIndex == (int)State.Cooldown)
            {
                chaseTimer += Time.deltaTime;
                if (chaseTimer > 30f)
                {
                    chaseTimer = 0f;
                    targetPlayer = null;
                    if (IsServer)
                    {
                        SpectreAnimationClientRpc("cooldownOver");
                        SpectreSetEmissionClientRpc(12f);
                        SyncStateSafely((int)State.Observing);
                    }
                }
            }

            if (slowDownTimer > 0f) slowDownTimer -= Time.deltaTime;

            if (IsServer && !isEnemyDead && (currentBehaviourStateIndex == (int)State.Chase || currentBehaviourStateIndex == (int)State.MarkingPlayer))
            {
                if (targetPlayer != null && (targetPlayer.isPlayerDead || !targetPlayer.isPlayerControlled))
                {
                    Plugin.Logger.LogInfo("Victim died/DC'd. Chaser is ending the chase early");
                    TriggerChaseEndClientRpc(); 
                }
            }

            if (!isEnemyDead && currentBehaviourStateIndex == (int)State.Chase)
            {
                HandleCustomFootsteps();
                if (agent != null && agent.enabled && agent.isOnNavMesh)
                {
                    bool isMovingNow = agent.velocity.sqrMagnitude > 0.1f;

                    if (agent.speed == 0f && !isHalted)
                    {
                        isHalted = true;
                        DoAnimationClientRpc("isHalted");
                    }
                    else if (agent.speed > 0f && isHalted)
                    {
                        isHalted = false;
                        DoAnimationClientRpc("hasAccelerated");
                    }
                }
            }

            var state = currentBehaviourStateIndex;
        }

        public void LateUpdate()
        {
            if (activeObserver != null && !isEnemyDead &&
                (currentBehaviourStateIndex == (int)State.Observing || currentBehaviourStateIndex == (int)State.MarkingPlayer))
            {
                PlayerControllerB playerToLookAt = null;

                if (currentBehaviourStateIndex == (int)State.Observing)
                {
                    playerToLookAt = GameNetworkManager.Instance.localPlayerController;
                }
                else if (currentBehaviourStateIndex == (int)State.MarkingPlayer)
                {
                    playerToLookAt = targetPlayer;
                }

                if (playerToLookAt != null)
                {
                    Animator obsAnim = activeObserver.GetComponentInChildren<Animator>();
                    if (obsAnim != null) obsAnim.applyRootMotion = false;
                    Vector3 lookDir = playerToLookAt.gameplayCamera.transform.position - activeObserver.transform.position;
                    lookDir.y = 0;

                    if (lookDir != Vector3.zero)
                    {
                        Quaternion targetRot = Quaternion.LookRotation(lookDir) * Quaternion.Euler(SpectreRotationOffset);
                        activeObserver.transform.rotation = Quaternion.Slerp(activeObserver.transform.rotation, targetRot, Time.deltaTime * 5f);
                    }

                    Light eyeBeam = activeObserver.GetComponentInChildren<Light>();
                    if (eyeBeam != null)
                    {
                        Vector3 beamLookDir = playerToLookAt.gameplayCamera.transform.position - eyeBeam.transform.position;
                        if (beamLookDir != Vector3.zero)
                        {
                            eyeBeam.transform.rotation = Quaternion.Slerp(eyeBeam.transform.rotation, Quaternion.LookRotation(beamLookDir), Time.deltaTime * 10f);
                        }
                    }
                }
                if (cachedScanNode != null && cachedScanNode.gameObject.activeSelf)
                {
                    cachedScanNode.transform.position = activeObserver.transform.position + (Vector3.up * 1.5f);
                }
            }
        }
        
        [ClientRpc]
        public void SyncNewTargetClientRpc(int newVictimId)
        {
            targetPlayer = StartOfRound.Instance.allPlayerScripts[newVictimId];
            Plugin.Logger.LogInfo($"Clients synced new target: {targetPlayer.playerUsername}");
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
                    int scrapThreshold = 1;

                    PlayerControllerB greedyPlayer = ObserverScrapListener.CheckForGreedyPlayer(scrapThreshold);

                    if (greedyPlayer != null)
                    {
                        targetPlayer = greedyPlayer;
                        Plugin.Logger.LogInfo("Scrap anger threshold met");
                        SyncTargetClientRpc((int)greedyPlayer.playerClientId);
                        SyncStateSafely((int)State.MarkingPlayer);
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
                    }

                    break;
            }
        }

        private void ToggleAndMoveScanNode(bool enable, Vector3 newPosition = default)
        {
            ScanNodeProperties[] allScanNodes = GetComponentsInChildren<ScanNodeProperties>(true);

            foreach (ScanNodeProperties scanNode in allScanNodes)
            {
                if (scanNode.nodeType == 1)
                {
                    cachedScanNode = scanNode;

                    scanNode.gameObject.SetActive(enable);

                    Collider scanCollider = scanNode.GetComponent<Collider>();
                    if (scanCollider != null)
                    {
                        scanCollider.enabled = enable;
                    }

                    if (enable)
                    {
                        scanNode.transform.position = newPosition + (Vector3.up * 1.5f);
                    }

                    break;
                }
            }
        }

        [ClientRpc]
        public void TogglePossessionSwarmClientRpc(bool enable)
        {
            if (possessionSwarmParticles != null)
            {
                possessionSwarmParticles.SetActive(enable);
            }
        }

        private EntranceTeleport GetClosestDoorToChaser()
        {
            if (allTeleports == null || allTeleports.Length == 0)
            {
                allTeleports = FindObjectsOfType<EntranceTeleport>();
            }

            EntranceTeleport closestDoor = null;
            float minDist = float.MaxValue;

            foreach (EntranceTeleport door in allTeleports)
            {
                if (door.isEntranceToBuilding == this.isOutside)
                {
                    float dist = Vector3.Distance(transform.position, door.transform.position);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        closestDoor = door;
                    }
                }
            }
            return closestDoor;
        }

        private EntranceTeleport GetCorrespondingDoor(EntranceTeleport chaserDoor)
        {
            if (chaserDoor == null) return null;

            foreach (EntranceTeleport door in allTeleports)
            {
                if (door.entranceId == chaserDoor.entranceId && door.isEntranceToBuilding != chaserDoor.isEntranceToBuilding)
                {
                    return door;
                }
            }
            return null;
        }

        private void UseElevator(bool goUp)
        {
            if (elevatorScript == null) return;

            Vector3 outerButtonPosition = goUp ? elevatorScript.elevatorBottomPoint.position : elevatorScript.elevatorTopPoint.position;

            if (Vector3.Distance(transform.position, elevatorScript.elevatorInsidePoint.position) < 2f)
            {
                base.SetDestinationToPosition(elevatorScript.elevatorInsidePoint.position, false);
                agent.speed = 0f;

                if (elevatorScript.elevatorFinishedMoving && elevatorScript.elevatorDoorOpen && elevatorScript.elevatorMovingDown == goUp)
                {
                    elevatorScript.PressElevatorButtonOnServer(true);
                }

                if (!elevatorScript.elevatorFinishedMoving)
                {
                    agent.enabled = false;
                    transform.position = elevatorScript.elevatorInsidePoint.position;
                }
                else
                {
                    agent.enabled = true; 
                }

                return; 
            }

            agent.enabled = true;

            bool elevatorIsHereAndOpen = elevatorScript.elevatorFinishedMoving && elevatorScript.elevatorDoorOpen && elevatorScript.elevatorMovingDown == goUp;

            if (elevatorIsHereAndOpen)
            {
                base.SetDestinationToPosition(elevatorScript.elevatorInsidePoint.position, false);
                agent.speed = 5f;
            }
            else
            {
                base.SetDestinationToPosition(outerButtonPosition, false);

                if (Vector3.Distance(transform.position, outerButtonPosition) < 2f)
                {
                    if (!elevatorScript.elevatorCalled && elevatorScript.elevatorMovingDown != goUp)
                    {
                        elevatorScript.CallElevatorOnServer(goUp);
                    }
                    agent.speed = 0f;
                }
                else
                {
                    agent.speed = 5f;
                }
            }
        }

        [ClientRpc]
        public void TriggerChaseEndClientRpc()
        {
            currentChaseDuration = 0f;
            creatureVoice.Stop();
            if (dieSFX != null)
            {
                creatureSFX.PlayOneShot(dieSFX, 1f);
            }

            agent.speed = 0f;
            SetChasePhysicsClientRpc(false);
            DoAnimationClientRpc("hasBeenKilled");

            if (IsServer)
            {
                chaseTimer = 0f;
                SyncStateSafely((int)State.Cooldown);
            }

            SpectreSirenClientRpc(false);
            ToggleVisualKnifeClientRpc(false);
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

            if (Vector3.Distance(targetPlayer.transform.position, triggerPos) < 30f)
            {
                if (!isPlayingMarkAnimation)
                {
                    isPlayingMarkAnimation = true;
                    StartCoroutine(MarkAndChaseSequence());
                }
            }
        }

        private void HandleCustomFootsteps()
        {
            float distanceMoved = Vector3.Distance(transform.position, lastPosition);

            if (distanceMoved > 0.02f)
            {
                footstepTimer += Time.deltaTime;
                if (footstepTimer > 0.35f)
                {
                    footstepTimer = 0f;

                    if (customFootstepSounds != null && customFootstepSounds.Length > 0 && movementAudio != null)
                    {
                        int randomIndex = UnityEngine.Random.Range(0, customFootstepSounds.Length);
                        movementAudio.pitch = UnityEngine.Random.Range(0.9f, 1.1f);
                        movementAudio.PlayOneShot(customFootstepSounds[randomIndex], 1f);
                    }
                }
            }
            else
            {
                footstepTimer = 0f;
            }
            lastPosition = transform.position;
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
            if (IsServer && targetPlayer != null)
            {
                PlayTauntInEarClientRpc(targetPlayer.actualClientId);
            }

            SpectreAnimationClientRpc("isMarked");

            if (targetPlayer != null)
            {
                PlayVictimWhisperClientRpc((int)targetPlayer.playerClientId);
            }

            yield return new WaitForSeconds(SirenDelay);
            if (isEnemyDead || currentBehaviourStateIndex != (int)State.MarkingPlayer) yield break;
            
            Plugin.Logger.LogInfo("!!SIREN SOUNDING!!");
            SpectreSirenClientRpc(true);
            
            SpectreSetBoolClientRpc("isBlasting", true);
            StartCoroutine(FinishAirRaidAfter(AirRaidLength));

            yield return new WaitForSeconds(11f);
            if (isEnemyDead) yield break;
            if (currentBehaviourStateIndex != (int)State.MarkingPlayer) yield break;
            
            DoAnimationClientRpc("hasBeenSummoned");
            ToggleVisualKnifeClientRpc(true);
            PlayClientFXClientRpc(0);
            PlayChaserSFXClientRpc("reanimate");
            TogglePossessionSwarmClientRpc(true);
            StartCoroutine(FlickerLightsDuringRise(6f));
            yield return new WaitForSeconds(8f);
            TogglePossessionSwarmClientRpc(false);
            if (isEnemyDead) yield break;
            yield return new WaitForSeconds(3f);
            if (isEnemyDead) yield break;
            DoAnimationClientRpc("riseFinished");
            PlayClientFXClientRpc(1);
            SetChasePhysicsClientRpc(true);
            isHalted = false;
            agent.speed = 5f;
            SyncStateSafely((int)State.Chase);
            isPlayingMarkAnimation = false;
        }

        IEnumerator FinishAirRaidAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            SpectreSetBoolClientRpc("isBlasting", false);
            SpectreAnimationClientRpc("isPoweringDown");
            SpectreSetEmissionClientRpc(0f);
        }

        [ClientRpc]
        public void SpectreSetEmissionClientRpc(float intensity)
        {
            if (activeObserver != null)
            {
                Renderer[] renderers = activeObserver.GetComponentsInChildren<Renderer>();
                foreach (var r in renderers)
                {
                    foreach (var mat in r.materials)
                    {
                        string[] props = { "_EmissionColor", "_EmissiveColor" };
                        foreach (string prop in props)
                        {
                            if (mat.HasProperty(prop))
                            {
                                int matId = mat.GetInstanceID();
                                if (!originalEmissions.ContainsKey(matId))
                                {
                                    originalEmissions[matId] = mat.GetColor(prop);
                                }
                                
                                Color orig = originalEmissions[matId];
                                float max = Mathf.Max(orig.r, Mathf.Max(orig.g, orig.b));
                                if (max > 0.0001f)
                                {
                                    mat.SetColor(prop, (orig / max) * intensity);
                                }
                                else
                                {
                                    mat.SetColor(prop, Color.white * intensity);
                                }
                            }
                        }
                    }
                }
            }
        }

        [ClientRpc]
        public void SpectreSetBoolClientRpc(string boolName, bool state)
        {
            if (boolName == "isBlasting") isBlasting = state;
            
            if (activeObserver != null)
            {
                Animator obsAnim = activeObserver.GetComponentInChildren<Animator>();
                if (obsAnim != null)
                {
                    obsAnim.SetBool(boolName, state);
                    Plugin.Logger.LogInfo($"Set Spectre bool: {boolName} to {state}");
                }
            }
        }

        [ClientRpc]
        public void SpectreSirenClientRpc(bool play)
        {
            if (activeObserver != null)
            {
                AudioSource[] sources = activeObserver.GetComponentsInChildren<AudioSource>();
                if (SirenAudioSourceIndex >= 0 && SirenAudioSourceIndex < sources.Length)
                {
                    AudioSource src = sources[SirenAudioSourceIndex];
                    if (src != null)
                    {
                        if (play) src.Play();
                        else src.Stop();
                    }
                }
                else
                {
                    Plugin.Logger.LogWarning($"Siren index {SirenAudioSourceIndex} out of bounds! Found {sources.Length} sources.");
                }
            }
        }

        [ClientRpc]
        public void ToggleVisualKnifeClientRpc(bool enable)
        {
            if (knifeModels != null && knifeModels.Length > 0)
            {
                foreach (GameObject knife in knifeModels)
                {
                    if (knife != null)
                    {
                        knife.SetActive(enable);
                    }
                }
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

            if (knifeToDrop == null) return;

            Vector3 rawSpawnPos = (handBone != null) ? handBone.position : transform.position + (Vector3.up * 1.5f);

            Vector3 safeSpawnPos = RoundManager.Instance.GetNavMeshPosition(rawSpawnPos, RoundManager.Instance.navHit, 5f, -1);
            if (!RoundManager.Instance.GotNavMeshPositionResult)
            {
                safeSpawnPos = transform.position; 
            }

            safeSpawnPos.y += 1.0f;

            GameObject knifeDrop = Instantiate(knifeToDrop.spawnPrefab, safeSpawnPos, Quaternion.identity, RoundManager.Instance.spawnedScrapContainer);
            GrabbableObject knifeGrabbable = knifeDrop.GetComponent<GrabbableObject>();

            knifeGrabbable.fallTime = 0f;
            knifeGrabbable.targetFloorPosition = knifeGrabbable.GetItemFloorPosition(safeSpawnPos);

            int scrapValue = (int)(UnityEngine.Random.Range(knifeToDrop.minValue, knifeToDrop.maxValue) * RoundManager.Instance.scrapValueMultiplier);
            knifeGrabbable.SetScrapValue(scrapValue);

            knifeDrop.GetComponent<NetworkObject>().Spawn();

            knifeGrabbable.EnableItemMeshes(true);

            Plugin.Logger.LogInfo("Dropped kitchen knife item");
        }

        void ManageChaseState()
        {
            if (targetPlayer == null) return;
            if (BreakIntoShip()) return;

            bool playerIsOutside = !targetPlayer.isInsideFactory;
            bool chaserIsOutside = this.isOutside;

            agent.speed = (slowDownTimer > 0f) ? 3f : 5f;

            BustOpenDoorsInRadius();

            if (!this.isOutside && RoundManager.Instance.currentDungeonType == 4)
            {
                if (elevatorScript == null) elevatorScript = FindObjectOfType<MineshaftElevatorController>();

                if (elevatorScript != null)
                {
                    if (this.isInElevatorStartRoom)
                    {
                        if (Vector3.Distance(transform.position, elevatorScript.elevatorBottomPoint.position) < 10f)
                            this.isInElevatorStartRoom = false;
                    }
                    else if (Vector3.Distance(transform.position, elevatorScript.elevatorTopPoint.position) < 10f)
                    {
                        this.isInElevatorStartRoom = true;
                    }

                    bool playerAtTop = Mathf.Abs(targetPlayer.transform.position.y - elevatorScript.elevatorTopPoint.position.y) < Mathf.Abs(targetPlayer.transform.position.y - elevatorScript.elevatorBottomPoint.position.y);

                    if (playerAtTop != this.isInElevatorStartRoom)
                    {
                        bool goUp = !this.isInElevatorStartRoom;
                        UseElevator(goUp);
                        return;
                    }
                }
            }

            if (this.isOutside != playerIsOutside)
            {
                movingTowardsTargetPlayer = false;

                EntranceTeleport chaserDoor = GetClosestDoorToChaser();

                if (chaserDoor != null)
                {
                    base.SetDestinationToPosition(chaserDoor.transform.position, false);

                    if (Vector3.Distance(transform.position, chaserDoor.transform.position) < 4f)
                    {
                        EntranceTeleport exitDoor = GetCorrespondingDoor(chaserDoor);
                        if (exitDoor != null)
                        {
                            TeleportEnemyServerRpc(exitDoor.entrancePoint.position, playerIsOutside);
                            return;
                        }
                    }
                }
                return;
            }

            moveTowardsDestination = true;
            SetDestinationToPosition(targetPlayer.transform.position, checkForPath: true);
        }

        private void BustOpenDoorsInRadius()
        {
            if (!IsServer || cachedDoors == null) return;

            float bashRangeSq = 4f;

            foreach (DoorLock door in cachedDoors)
            {
                if (door == null) continue;

                if ((transform.position - door.transform.position).sqrMagnitude < bashRangeSq)
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

                        DoAnimationClientRpc("bashDoor");

                        doorSwing.TriggerAnimationNonPlayer(true, true, false);
                        door.OpenDoorAsEnemyServerRpc();

                        Plugin.Logger.LogInfo("Chaser bashed a door open");

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

        public override void OnCollideWithPlayer(Collider other)
        {
            base.OnCollideWithPlayer(other);
        }

        [ServerRpc(RequireOwnership = false)]
        public void TriggerAttackAnimationServerRpc()
        {
            TriggerAttackAnimationClientRpc();
        }

        [ClientRpc]
        public void TriggerAttackAnimationClientRpc()
        {
            attackCooldown = 2.0f;
            slowDownTimer = 1.5f;
            Animator chaserAnim = GetComponentInChildren<Animator>();
            if (chaserAnim != null)
            {
                chaserAnim.SetTrigger("stabAttack");
            }
            if (knifeStabSounds != null && knifeStabSounds.Length > 0 && creatureSFX != null)
            {
                int randomIndex = UnityEngine.Random.Range(0, knifeStabSounds.Length);
                if (knifeStabSounds[randomIndex] != null)
                {
                    creatureSFX.PlayOneShot(knifeStabSounds[randomIndex], 1f);
                }
            }
        }

        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);

            if (isEnemyDead || currentBehaviourStateIndex == (int)State.Cooldown) return;

            enemyHP -= force;

            if (enemyHP <= 0)
            {
                agent.speed = 0f;
                attackCooldown = 10f;
                if (creatureVoice != null)
                {
                    creatureVoice.Stop();
                }
            }
            else if (IsServer)
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
            if (isEnemyDead) return;

            agent.speed = 0f;
            agent.enabled = false;
            if (creatureVoice != null) creatureVoice.Stop();

            AudioClip nativeDeathSound = dieSFX;
            dieSFX = null;
            base.KillEnemy(destroy);

            dieSFX = nativeDeathSound;

            if (dieSFX != null && creatureSFX != null)
            {
                creatureSFX.PlayOneShot(dieSFX, 1f);
            }

            if (IsServer)
            {
                DoAnimationClientRpc("hasBeenKilled");
                SpawnKnifeItemLocally();
                ToggleVisualKnifeClientRpc(false);
            }

            SpectreSirenClientRpc(false);
            DespawnObserverSpectre();
        }
    }
}