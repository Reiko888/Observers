using GameNetcodeStuff;
using Observers.src;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Netcode;
using UnityEngine;

namespace Observers {

    class ObserversAI : EnemyAI
    {
        public static List<ObserversAI> allChasers = new List<ObserversAI>();
        public ObserversAI master;
        public bool isMaster;
        private bool choseMasterObserver;
#pragma warning disable 0649
        public GameObject ObserverPrefab;
        public GameObject activeObserver;
        public GameObject activeChaserModel;
        public int SirenAudioSourceIndex = 0;
        public int AtmosphereAudioSourceIndex = 1;
        public AudioClip[] AtmosphereClips;
        private float atmosphereTimer = 0f;
        public Vector3 SpectreRotationOffset = new Vector3(0, -90, 0);
        public float SirenDelay = 6.5f;
        public ScreenShakeType SirenShakeType = ScreenShakeType.Small;
        public float AirRaidLength = 60f;
        private Dictionary<int, Color> originalEmissions = new Dictionary<int, Color>();
        public HangarShipDoor shipDoor;
        public bool isPryingDoor;
        private bool inSpecialAnimation;
        private float pryingDoorAnimTime;
        private AudioSource spectreVoice;

        [Header("Door Prying")]
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
        [System.Serializable]
        public struct WeaponSoundSet
        {
            public AudioClip[] sounds;
        }
        public WeaponSoundSet[] weaponStabSounds;
        public int activeWeaponIndex = 0;
        private DoorLock[] cachedDoors;
        private Dictionary<DoorLock, float> doorCooldowns = new Dictionary<DoorLock, float>();
        private MineshaftElevatorController elevatorScript;
        private bool isInElevatorStartRoom = false;
        public AudioClip markedLineClip;
        public GameObject[] meshVariants;
        public GameObject[] knifeModels;
        private EntranceTeleport[] allTeleports;
        public GameObject possessionSwarmParticles;
        public Transform handBone;
        private ScanNodeProperties cachedScanNode;
        private bool isStunned = false;
        private float slowDownTimer = 0f;
        private float currentChaseDuration = 0f;
        private bool isBlasting = false;
        private float cameraShakeTimer = 0f;
        private float baseSpeed;
        private float raidVolume;
        private float observerCooldown;
        private float detectRange;
        private bool itemDrop;
        private float chaseTime;
        private int chaserDamage;
        private int scrapAnger;

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text) {
            Plugin.Logger.LogInfo(text);
        }

        public override void Start()
        {
            base.Start();
            detectRange = ObserverContentHandler.Instance.observerAssets.GetConfig<float>("Observer Detection Range").Value;
            raidVolume = ObserverContentHandler.Instance.observerAssets.GetConfig<float>("Observer Air Raid volume").Value;
            observerCooldown = ObserverContentHandler.Instance.observerAssets.GetConfig<float>("Observer Cooldown").Value;
            baseSpeed = ObserverContentHandler.Instance.observerAssets.GetConfig<float>("Chaser run speed").Value;
            itemDrop = ObserverContentHandler.Instance.observerAssets.GetConfig<bool>("Chaser item drop").Value;
            chaseTime = ObserverContentHandler.Instance.observerAssets.GetConfig<float>("Chaser chase time").Value;
            this.enemyHP = ObserverContentHandler.Instance.observerAssets.GetConfig<int>("Chaser health").Value;
            chaserDamage = ObserverContentHandler.Instance.observerAssets.GetConfig<int>("Chaser attack damage").Value;
            scrapAnger = ObserverContentHandler.Instance.observerAssets.GetConfig<int>("Base scrap anger").Value;
            LogIfDebugBuild("Observer Spawned");

            shipDoor = FindObjectOfType<HangarShipDoor>();
            chaserAnimator = GetComponentInChildren<Animator>();
            cachedDoors = FindObjectsOfType<DoorLock>();

            SetupChaserMesh();

            base.GetAINodes();
            mainEntrancePosition = RoundManager.FindMainEntrancePosition(true, isOutside);

            if (!isOutside && allAINodes != null && allAINodes.Length > 0)
            {
                Transform randomNode = allAINodes[enemyRandom.Next(0, allAINodes.Length)].transform;
                agent.Warp(randomNode.position);
                SyncPositionToClients();
                Plugin.Logger.LogInfo("Warped Chaser to a random internal AI node");
            }
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
                activeWeaponIndex = randomMeshIndex;
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
            DoAnimationServerRpc("isPryingDoor", true, true);
            SetPryingDoorServerRpc(true);
        }

        private void FinishPryOpenDoor()
        {
            DoAnimationServerRpc("isPryingDoor", true, false);
            SetPryingDoorServerRpc(false);
        }

        [ServerRpc(RequireOwnership = false)]
        public void SetPryingDoorServerRpc(bool state)
        {
            SetPryingDoorClientRpc(state);
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

                breakDownDoorAudio.PlayOneShot(breakAndEnter);
                WalkieTalkie.TransmitOneShotAudio(breakDownDoorAudio, breakAndEnter);
                RoundManager.Instance.PlayAudibleNoise(base.transform.position, 15f, 0.9f);
                StartOfRound.Instance.speakerAudioSource.PlayOneShot(shipAlarm);
                WalkieTalkie.TransmitOneShotAudio(StartOfRound.Instance.speakerAudioSource, shipAlarm);
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
                    if (IsServer)
                    {
                        SwitchToBehaviourState(queuedState);
                        ulong newOwner = (queuedState == 2 && targetPlayer != null)
                            ? targetPlayer.actualClientId
                            : StartOfRound.Instance.allPlayerScripts[0].actualClientId;
                        ChangeOwnershipOfEnemy(newOwner);
                    }
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
                foreach (ObserversAI chaser in allChasers)
                {
                    if (chaser != null && chaser != this && chaser.isPryingDoor)
                    {
                        return false;
                    }
                }
                BeginPryOpenDoor();
                return true;
            }
            return false;
        }

        private bool AreOtherChasersAlive()
        {
            if (allChasers == null) return false;
            foreach (var chaser in allChasers)
            {
                if (chaser != null && chaser != this && !chaser.isEnemyDead)
                {
                    return true;
                }
            }
            return false;
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            if (allChasers.Contains(this)) allChasers.Remove(this);

            if (!AreOtherChasersAlive())
            {
                if (activeObserver != null) Destroy(activeObserver);
            }
            ObserverScrapListener.ClearData();
        }

        [ServerRpc(RequireOwnership = false)]
        public void DoAnimationServerRpc(string animationTrigger, bool isBool = false, bool state = false)
        {
            DoAnimationClientRpc(animationTrigger, isBool, state);
        }

        [ClientRpc]
        public void DoAnimationClientRpc(string animationTrigger, bool isBool = false, bool state = false)
        {
            Animator chaserAnim = GetComponentInChildren<Animator>();

            if (isBool) chaserAnim.SetBool(animationTrigger, state);
            else chaserAnim.SetTrigger(animationTrigger);
        }

        [ClientRpc]
        public void PlayVictimWhisperClientRpc()
        {
            if (GameNetworkManager.Instance.localPlayerController == targetPlayer)
            {
                HUDManager.Instance.UIAudio.PlayOneShot(markedLineClip, 1f);
            }
        }

        [ClientRpc]
        public void PlayChaserSFXClientRpc(string sfxType)
        {
            if (sfxType == "reanimate")
            {
                creatureSFX.Play();
            }
            else if (sfxType == "death")
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


            Plugin.Logger.LogInfo($"Spawning Spectre at {spawnPos} to watch the door.");
            hasSpawnedSpectre = true;
            SpawnSpectreClientRpc(spawnPos, spawnRot);

        }

        [ClientRpc]
        public void SpawnSpectreClientRpc(Vector3 spawnPos, Quaternion spawnRot)
        {
            if (activeObserver == null)
            {
                GameObject newSpectre = Instantiate(ObserverPrefab, spawnPos, spawnRot);
                activeObserver = newSpectre;
                spectreVoice = activeObserver.GetComponentsInChildren<AudioSource>()[AtmosphereAudioSourceIndex];
                foreach (var chaser in allChasers)
                {
                    if (chaser != null)
                    {
                        chaser.activeObserver = newSpectre;
                        chaser.hasSpawnedSpectre = true;
                    }
                }
                Terminal terminal = FindObjectOfType<Terminal>();
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

            ToggleAndMoveScanNode(true, spawnPos);
        }

        [ClientRpc]
        public void SyncTargetClientRpc(int playerId)
        {
            if (playerId >= 0 && playerId < StartOfRound.Instance.allPlayerScripts.Length)
            {
                PlayerControllerB target = StartOfRound.Instance.allPlayerScripts[playerId];
                foreach (var chaser in allChasers)
                {
                    if (chaser != null) chaser.targetPlayer = target;
                }
                Plugin.Logger.LogInfo($"Target synced to all chasers: {target.playerUsername}");
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

            ToggleAndMoveScanNode(false);
        }



        private void ChooseMasterObserver()
        {
            ObserversAI[] array = FindObjectsByType<ObserversAI>(FindObjectsSortMode.None);
            int lowestIndex = int.MaxValue;
            int lowestIndexIdx = -1;
            for (int i = 0; i < array.Length; i++)
            {
                if (!array[i].isEnemyDead && array[i].thisEnemyIndex < lowestIndex)
                {
                    lowestIndex = array[i].thisEnemyIndex;
                    lowestIndexIdx = i;
                }
            }
            
            if (lowestIndexIdx == -1) return;
            master = array[lowestIndexIdx];
            
            if (IsServer)
            {
                allChasers = new List<ObserversAI>(array);
                foreach (ObserversAI chaser in allChasers)
                {
                    if (chaser != master) 
                    {
                        chaser.isMaster = false;
                        chaser.master = master;
                        chaser.activeObserver = master.activeObserver;
                        chaser.hasSpawnedSpectre = master.hasSpawnedSpectre;
                    }
                }
            }

            if (master == this)
            {
                isMaster = true;
                SyncMasterObserverClientRpc();
            }
            else if (IsServer)
            {
                master.SyncMasterObserverClientRpc();
            }
        }

        [ClientRpc]
        public void SyncMasterObserverClientRpc()
        {
            if (IsServer) return;
            master = this;
            isMaster = true;
            allChasers = new List<ObserversAI>(FindObjectsByType<ObserversAI>(FindObjectsSortMode.None));
            foreach (ObserversAI chaser in allChasers)
            {
                if (chaser != this) 
                {
                    chaser.isMaster = false;
                    chaser.master = this;
                    chaser.activeObserver = this.activeObserver;
                    chaser.hasSpawnedSpectre = this.hasSpawnedSpectre;
                }
            }
        }

        public override void Update()
        {
            base.Update();
            if (IsServer)
            {
                if (!choseMasterObserver)
                {
                    choseMasterObserver = true;
                    ChooseMasterObserver();
                }
                else if (master == null || master.isEnemyDead)
                {
                    ChooseMasterObserver();
                }
            }

            if (isPryingDoor && inSpecialAnimation)
            {
                if (shipDoor != null)
                {
                    base.transform.position = Vector3.Lerp(base.transform.position, shipDoor.outsideDoorPoint.position, 7f * Time.deltaTime);
                    base.transform.rotation = Quaternion.Lerp(base.transform.rotation, shipDoor.outsideDoorPoint.rotation, 7f * Time.deltaTime);
                    pryingDoorAnimTime = Mathf.Min(pryingDoorAnimTime + Time.deltaTime / pryOpenDoorAnimLength, 1f);

                    if (chaserAnimator != null)
                    {
                        chaserAnimator.SetFloat("pryOpenDoor", pryingDoorAnimTime);
                    }
                    shipDoor.shipDoorsAnimator.SetFloat("pryOpenDoor", pryingDoorAnimTime);
                        
                    BreakIntoShip();
                    return;
                }
            }
            
            if (IsServer && isMaster && !isEnemyDead && currentBehaviourStateIndex == 0)
            {
                atmosphereTimer += Time.deltaTime;
                if (atmosphereTimer >= 10f)
                {
                    atmosphereTimer = 0f;
                    if (UnityEngine.Random.value <= 0.30f)
                    {
                        int clipIndex = UnityEngine.Random.Range(0, AtmosphereClips.Length);
                        PlayAtmosphereClientRpc(clipIndex);
                    }
                }
            }
            
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

            if (!isEnemyDead && enemyHP > 0 && currentBehaviourStateIndex == 2 && stunNormalizedTimer <= 0f)
            {
                if (IsOwner)
                {
                    BustOpenDoorsInRadius();
                    float timerMultiplier = 1f;
                    if (targetPlayer != null && this.isOutside != !targetPlayer.isInsideFactory)
                    {
                        timerMultiplier = 0.5f;
                    }
                    currentChaseDuration += Time.deltaTime * timerMultiplier;
                    if (currentChaseDuration >= chaseTime)
                    {
                        currentChaseDuration = -9999f;
                        Plugin.Logger.LogInfo("Chase time expired");
                        if (isMaster)
                        {
                            TriggerChaseEndServerRpc();
                        }
                        return;
                    }
                }
            }

            else if (currentBehaviourStateIndex == 3)
            {
                chaseTimer += Time.deltaTime;
                if (chaseTimer > observerCooldown)
                {
                    chaseTimer = 0f;
                    targetPlayer = null;
                    if (IsServer && isMaster)
                    {
                        SpectreAnimationClientRpc("cooldownOver");
                        SpectreSetEmissionClientRpc(12f);
                        ToggleSpotlightClientRpc(true);
                        foreach (var chaser in allChasers)
                        {
                            if (chaser != null)
                            {
                                chaser.targetPlayer = null;
                                chaser.SwitchToBehaviourState(0);
                                ChangeOwnershipOfEnemy(StartOfRound.Instance.allPlayerScripts[0].actualClientId);
                            }
                        }
                    }
                }
            }

            if (IsServer && isMaster)
            {
                if (currentBehaviourStateIndex == 0)
                {
                    if (!hasSpawnedSpectre)
                    {
                        SpawnObserverSpectre();
                    }
                    int scrapThreshold = scrapAnger;
                    PlayerControllerB greedyPlayer = ObserverScrapListener.CheckForGreedyPlayer(scrapThreshold);
                    if (greedyPlayer != null)
                    {
                        targetPlayer = greedyPlayer;
                        Plugin.Logger.LogInfo("Scrap anger threshold met");
                        SyncTargetClientRpc((int)greedyPlayer.playerClientId);
                        SwitchToBehaviourState(1);
                    }
                }
                else if (currentBehaviourStateIndex == 1)
                {
                    if (targetPlayer != null && !isPlayingMarkAnimation)
                    {
                        Vector3 triggerPos = activeObserver != null ? activeObserver.transform.position : RoundManager.FindMainEntrancePosition(true, isOutside);
                        if (Vector3.Distance(targetPlayer.transform.position, triggerPos) < detectRange)
                        {
                            isPlayingMarkAnimation = true;
                            StartCoroutine(MarkAndChaseSequence());
                        }
                    }
                }
            }

            if (slowDownTimer > 0f) slowDownTimer -= Time.deltaTime;

            if (IsServer && !isEnemyDead && (currentBehaviourStateIndex == 2 || currentBehaviourStateIndex == 1))
            {
                if (targetPlayer != null && (targetPlayer.isPlayerDead || !targetPlayer.isPlayerControlled))
                {
                    Plugin.Logger.LogInfo("Victim died/DC'd. Chaser is ending the chase early");
                    TriggerChaseEndClientRpc(); 
                }
            }
        }

        public void LateUpdate()
        {
            if (activeObserver != null && !isEnemyDead &&
                (currentBehaviourStateIndex == 0 || currentBehaviourStateIndex == 1))
            {
                PlayerControllerB playerToLookAt = null;

                if (currentBehaviourStateIndex == 0)
                {
                    playerToLookAt = GameNetworkManager.Instance.localPlayerController;
                }
                else if (currentBehaviourStateIndex == 1)
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
                }
                if (cachedScanNode != null && cachedScanNode.gameObject.activeSelf)
                {
                    cachedScanNode.transform.position = activeObserver.transform.position + (Vector3.up * 1.5f);
                }
            }
        }


        public override void DoAIInterval()
        {
            base.DoAIInterval();
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead) return;

            switch (currentBehaviourStateIndex)
            {
                case 2:
                    if (targetPlayer == null) return;
                    if (BreakIntoShip()) return;

                    bool playerIsOutside = !targetPlayer.isInsideFactory;
                    bool chaserIsOutside = this.isOutside;

                    agent.speed = (slowDownTimer > 0f) ? 3f : baseSpeed;

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
            possessionSwarmParticles.SetActive(enable);
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

        public override void SetEnemyStunned(bool setToStunned, float setToStunTime = 2f, PlayerControllerB setStunnedByPlayer = null)
        {
            if (currentBehaviourStateIndex != 2) return;
            base.SetEnemyStunned(setToStunned, setToStunTime, setStunnedByPlayer);
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
                agent.speed = baseSpeed;
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
                    agent.speed = baseSpeed;
                }
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void TriggerChaseEndServerRpc()
        {
            foreach (var chaser in allChasers)
            {
                if (chaser != null) chaser.TriggerChaseEndClientRpc();
            }
        }

        [ClientRpc]
        public void TriggerChaseEndClientRpc()
        {
            currentChaseDuration = 0f;
            creatureVoice.Stop();
            creatureSFX.PlayOneShot(dieSFX);
            isPlayingMarkAnimation = false;

            agent.speed = 0f;
            DoAnimationClientRpc("hasBeenKilled");
            SpectreSetBoolClientRpc("isBlasting", false);

            if (IsServer)
            {
                chaseTimer = 0f;
                SwitchToBehaviourState(3);
                ChangeOwnershipOfEnemy(StartOfRound.Instance.allPlayerScripts[0].actualClientId);
            }

            SpectreSirenClientRpc(false);
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
            PlayVictimWhisperClientRpc();
            SpectreAnimationClientRpc("isMarked");

            yield return new WaitForSeconds(SirenDelay);
            if (isEnemyDead || currentBehaviourStateIndex != 1) yield break;

            Plugin.Logger.LogInfo("!!SIREN SOUNDING!!");
            SpectreSirenClientRpc(true);
            
            SpectreSetBoolClientRpc("isBlasting", true);
            StartCoroutine(FinishAirRaidAfter(AirRaidLength));

            yield return new WaitForSeconds(11f);
            if (isEnemyDead || currentBehaviourStateIndex != 1) yield break;

            foreach (var chaser in allChasers)
            {
                if (chaser != null && !chaser.isEnemyDead)
                {
                    chaser.DoAnimationClientRpc("hasBeenSummoned");
                    chaser.ToggleVisualKnifeClientRpc(true);
                    chaser.PlayClientFXClientRpc(0);
                    chaser.PlayChaserSFXClientRpc("reanimate");
                    chaser.TogglePossessionSwarmClientRpc(true);
                    chaser.StartCoroutine(chaser.FlickerLightsDuringRise(6f));
                }
            }
            yield return new WaitForSeconds(8f);
            foreach (var chaser in allChasers)
            {
                if (chaser != null && !chaser.isEnemyDead) chaser.TogglePossessionSwarmClientRpc(false);
            }
            yield return new WaitForSeconds(3f);
            if (isEnemyDead || currentBehaviourStateIndex != 1) yield break;

            foreach (var chaser in allChasers)
            {
                if (chaser != null && !chaser.isEnemyDead)
                {
                    chaser.DoAnimationClientRpc("riseFinished");
                    chaser.PlayClientFXClientRpc(1);
                    chaser.agent.speed = baseSpeed;
                    chaser.SwitchToBehaviourState(2);
                    chaser.ChangeOwnershipOfEnemy(targetPlayer.actualClientId);
                    chaser.isPlayingMarkAnimation = false;
                }
            }
        }

        IEnumerator FinishAirRaidAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            SpectreSetBoolClientRpc("isBlasting", false);
            SpectreAnimationClientRpc("isPoweringDown");
            SpectreSetEmissionClientRpc(0f);
            ToggleSpotlightClientRpc(false);
        }

        [ClientRpc]
        public void ToggleSpotlightClientRpc(bool enable)
        {
            if (activeObserver != null)
            {
                Transform[] allChildren = activeObserver.GetComponentsInChildren<Transform>(true);
                foreach (Transform child in allChildren)
                {
                    if (child.name == "Spot Light")
                    {
                        child.gameObject.SetActive(enable);
                        break;
                    }
                }
            }
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
                        string[] props = { "_EmissiveColor" };
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
            if (boolName == "isBlasting") 
            {
                isBlasting = state;
                if (activeObserver != null)
                {
                    Transform shockwave = activeObserver.transform.Find("Shockwave");
                    if (shockwave != null)
                    {
                        ParticleSystem ps = shockwave.GetComponent<ParticleSystem>();
                        if (ps != null)
                        {
                            if (state) ps.Play();
                            else ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                        }
                    }
                }
            }
            
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
                AudioSource src = activeObserver.GetComponentsInChildren<AudioSource>()[SirenAudioSourceIndex];
                src.volume = raidVolume;
                if (play) src.Play();
                else src.Stop();
            }
        }

        [ClientRpc]
        public void PlayAtmosphereClientRpc(int clipIndex)
        {
            spectreVoice.PlayOneShot(AtmosphereClips[clipIndex]);
        }

        [ClientRpc]
        public void ToggleVisualKnifeClientRpc(bool enable)
        {
            if (activeWeaponIndex >= 0 && activeWeaponIndex < knifeModels.Length)
            {
                for (int i = 0; i < knifeModels.Length; i++)
                {
                    knifeModels[i].SetActive(enable && i == activeWeaponIndex);
                }
            }
        }

        public void SpawnItemLocally()
        {
            if (!IsServer) return;

            string targetItemName = "Kitchen knife";
            if (activeWeaponIndex >= 0 && activeWeaponIndex < knifeModels.Length)
            {
                string modelName = knifeModels[activeWeaponIndex].name.ToLower();
                if (modelName.Contains("shovel")) targetItemName = "Shovel";
                else if (modelName.Contains("stop")) targetItemName = "Stop sign";
                else if (modelName.Contains("yield")) targetItemName = "Yield sign";
            }

            Item knifeToDrop = StartOfRound.Instance.allItemsList.itemsList.Find(x => x.itemName == targetItemName);
            if (knifeToDrop == null) return;

            Vector3 spawnPos = handBone != null ? handBone.position : transform.position + Vector3.up;

            GameObject knifeDrop = Instantiate(knifeToDrop.spawnPrefab, spawnPos, Quaternion.identity, RoundManager.Instance.spawnedScrapContainer);
            GrabbableObject knifeGrabbable = knifeDrop.GetComponent<GrabbableObject>();

            knifeGrabbable.fallTime = 0f;
            knifeGrabbable.targetFloorPosition = knifeGrabbable.GetItemFloorPosition(spawnPos);

            int scrapValue = (int)(UnityEngine.Random.Range(knifeToDrop.minValue, knifeToDrop.maxValue) * RoundManager.Instance.scrapValueMultiplier);
            knifeGrabbable.SetScrapValue(scrapValue);

            NetworkObject netObj = knifeDrop.GetComponent<NetworkObject>();
            netObj.Spawn();

            RoundManager.Instance.SyncScrapValuesClientRpc(new NetworkObjectReference[] { netObj }, new int[] { scrapValue });
        }

        private void BustOpenDoorsInRadius()
        {
            if (!IsOwner || cachedDoors == null) return;

            float bashRangeSq = 5.5f;

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

        private IEnumerator FlickerLightsDuringRise(float radius = 20f)
        {
            Vector3 chaserPos = transform.position;
            List<Animator> lightsNearby = new List<Animator>();

            for (int i = 0; i < RoundManager.Instance.allPoweredLightsAnimators.Count; i++)
            {
                Animator anim = RoundManager.Instance.allPoweredLightsAnimators[i];
                if (anim != null && Vector3.Distance(anim.transform.position, chaserPos) <= radius)
                {
                    lightsNearby.Add(anim);
                }
            }

            if (lightsNearby.Count == 0) yield break;

            int loopCount = 0;
            int b = 4;
            while (b > 0)
            {
                int limit = lightsNearby.Count / b;
                for (int j = loopCount; j < limit; j++)
                {
                    if (j < lightsNearby.Count && lightsNearby[j] != null)
                    {
                        lightsNearby[j].SetTrigger("Flicker");
                    }
                    loopCount++;
                }
                yield return new WaitForSeconds(0.05f);
                b--;
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
            if (isEnemyDead || currentBehaviourStateIndex != 2 || attackCooldown > 0f || stunNormalizedTimer > 0f) return;

            PlayerControllerB player = MeetsStandardPlayerCollisionConditions(other, false, false);
            if (player != null)
            {
                player.DamagePlayer(chaserDamage, hasDamageSFX: true, callRPC: true, CauseOfDeath.Stabbing);
                attackCooldown = 0.5f;
                slowDownTimer = 0.4f;
                TriggerAttackAnimationServerRpc();
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void TriggerAttackAnimationServerRpc()
        {
            int soundIdx = UnityEngine.Random.Range(0, weaponStabSounds[activeWeaponIndex].sounds.Length);
            TriggerAttackAnimationClientRpc(soundIdx);
        }

        [ClientRpc]
        public void TriggerAttackAnimationClientRpc(int soundIdx)
        {
            attackCooldown = 0.5f;
            slowDownTimer = 0.4f;
            Animator chaserAnim = GetComponentInChildren<Animator>();
            if (chaserAnim != null)
            {
                chaserAnim.SetTrigger("stabAttack");
            }
            creatureSFX.PlayOneShot(weaponStabSounds[activeWeaponIndex].sounds[soundIdx]);
        }

        public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            if (isEnemyDead || currentBehaviourStateIndex != 2) return;

            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            if (isEnemyDead) return;

            this.enemyHP -= force;

            if (this.enemyHP <= 0)
            {
                agent.speed = 0f;
                attackCooldown = 10f;
                creatureVoice.Stop();
                KillEnemyOnOwnerClient(false);
            }
            else if (IsServer)
            {
                DoAnimationClientRpc("TakeDamage");
            }
        }

        public override void KillEnemy(bool destroy = false)
        {
            if (isEnemyDead) return;

            agent.speed = 0f;
            agent.enabled = false;
            creatureVoice.Stop();
            base.KillEnemy(destroy);

            creatureSFX.PlayOneShot(dieSFX);

            if (IsServer)
            {
                DoAnimationClientRpc("hasBeenKilled");
                if (itemDrop) SpawnItemLocally();
                ToggleVisualKnifeClientRpc(false);

                if (!AreOtherChasersAlive())
                {
                    SpectreSirenClientRpc(false);
                    DespawnObserverSpectre();
                }
            }
        }
    }
}