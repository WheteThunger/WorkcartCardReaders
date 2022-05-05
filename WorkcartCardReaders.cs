using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries.Covalence;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VLB;

namespace Oxide.Plugins
{
    [Info("Workcart Card Readers", "WhiteThunder", "0.4.1")]
    [Description("Adds card readers to workcarts which players must authorize on to ride.")]
    internal class WorkcartCardReaders : CovalencePlugin
    {
        #region Fields

        [PluginReference]
        private Plugin AutomatedWorkcarts, CargoTrainEvent;

        private static WorkcartCardReaders _pluginInstance;
        private static Configuration _pluginConfig;
        private static StoredMapData _mapData;

        private const string PermissionFreeRides = "workcartcardreaders.freerides";
        private const string PermissionManageCardReaders = "workcartcardreaders.manage";

        private const string CardReaderPrefab = "assets/prefabs/io/electric/switches/cardreader.prefab";
        private const string ItemBrokenEffectPrefab = "assets/bundled/prefabs/fx/item_break.prefab";
        private const string CardReaderDeployEffectPrefab = "assets/prefabs/deployable/tuna can wall lamp/effects/tuna-can-lamp-deploy.prefab";

        private static readonly Vector3 StaticCardReaderOffset = new Vector3(0, 1.35f, 0);

        private readonly Dictionary<ulong, Timer> _playerTimers = new Dictionary<ulong, Timer>();
        private readonly Dictionary<ulong, float> _playerLastWarned = new Dictionary<ulong, float>();
        private readonly HashSet<ulong> _globallyAuthorizedPlayers = new HashSet<ulong>();
        private readonly MapCardReaderManager _mapCardReaderManager = new MapCardReaderManager();

        #endregion

        #region Hooks

        private void Init()
        {
            _pluginInstance = this;
            _mapData = StoredMapData.Load();

            permission.RegisterPermission(PermissionFreeRides, this);
            permission.RegisterPermission(PermissionManageCardReaders, this);

            Unsubscribe(nameof(OnEntitySpawned));
            Unsubscribe(nameof(OnWorkcartAutomationStarted));
            Unsubscribe(nameof(OnWorkcartAutomationStopped));
        }

        private void OnServerInitialized()
        {
            CheckDependencies();

            if (_mapData != null && _mapData.CardReaderPositions.Count > 0)
            {
                if (_pluginConfig.EnableGlobalAuthorization)
                {
                    _mapCardReaderManager.CreateAll();
                    Puts($"Spawned {_mapData.CardReaderPositions.Count} card readers in the map.");
                }
                else
                {
                    LogWarning($"Not spawning {_mapData.CardReaderPositions.Count} card reader(s) in the map because EnableGlobalAuthorization is false.");
                }
            }

            if (_pluginConfig.AddToAllWorkcarts)
            {
                foreach (var workcart in BaseNetworkable.serverEntities.OfType<TrainEngine>())
                    AddCardReader(workcart);

                Subscribe(nameof(OnEntitySpawned));
            }
            else if (_pluginConfig.AddToAutomatedWorkcarts)
            {
                var workcartList = GetAutomatedWorkcarts();
                if (workcartList != null)
                {
                    foreach (var workcart in workcartList)
                        AddCardReader(workcart);
                }

                Subscribe(nameof(OnWorkcartAutomationStarted));
                Subscribe(nameof(OnWorkcartAutomationStopped));
            }
        }

        private void Unload()
        {
            _mapCardReaderManager.DestroyAll();

            foreach (var workcart in BaseNetworkable.serverEntities.OfType<TrainEngine>())
                WorkcartCardReader.RemoveFromWorkcart(workcart);

            _mapData = null;
            _pluginConfig = null;
            _pluginInstance = null;
        }

        private void OnEntitySpawned(TrainEngine workcart)
        {
            // Delay to give other plugins a chance to save the id in order to block the hook.
            NextTick(() =>
            {
                if (workcart != null)
                    AddCardReader(workcart);
            });
        }

        // This hook is exposed by plugin: Automated Workcarts (AutomatedWorkcarts).
        private void OnWorkcartAutomationStarted(TrainEngine workcart)
        {
            AddCardReader(workcart);
        }

        // This hook is exposed by plugin: Automated Workcarts (AutomatedWorkcarts).
        private void OnWorkcartAutomationStopped(TrainEngine workcart)
        {
            WorkcartCardReader.RemoveFromWorkcart(workcart);
        }

        // When players board an automated workcart...
        private void OnEntityEnter(TriggerParent triggerParent, BasePlayer player)
        {
            var workcart = triggerParent.gameObject.ToBaseEntity() as TrainEngine;
            if (workcart == null)
                return;

            var workcartCardReader = WorkcartCardReader.GetForWorkcart(workcart);
            if (workcartCardReader == null || IsPlayerAuthorized(player, workcartCardReader))
                return;

            // Ignore if the player is already onboard somehow (there are multiple triggers).
            if (HasPlayerOnBoard(workcart, player))
                return;

            NextTick(() =>
            {
                if (workcart == null || workcartCardReader == null || player == null)
                    return;

                DestroyExistingPlayerTimer(player);

                float lastWarned;
                if (!_playerLastWarned.TryGetValue(player.userID, out lastWarned)
                    || Time.realtimeSinceStartup > lastWarned + 5)
                {
                    ChatMessage(player, Lang.WarningSwipeRequired, _pluginConfig.AllowedSecondsToSwipeBeforeEject);
                    _playerLastWarned[player.userID] = Time.realtimeSinceStartup;
                }

                _playerTimers[player.userID] = timer.Once(_pluginConfig.AllowedSecondsToSwipeBeforeEject, () =>
                {
                    if (!IsPlayerAuthorized(player, workcartCardReader) && HasPlayerOnBoard(workcart, player))
                        PerformEject(workcart, player);

                    _playerTimers.Remove(player.userID);
                });
            });
        }

        // When players leave an automated workcart...
        private void OnEntityLeave(TriggerParent triggerParent, BasePlayer player)
        {
            var workcart = triggerParent.gameObject.ToBaseEntity() as TrainEngine;
            if (workcart == null)
                return;

            var workcartCardReader = WorkcartCardReader.GetForWorkcart(workcart);
            if (workcartCardReader == null)
                return;

            NextTick(() =>
            {
                if (workcart == null || workcartCardReader == null || player == null)
                    return;

                // Ignore if the player is still onboard somehow (there are multiple triggers).
                if (HasPlayerOnBoard(workcart, player))
                    return;

                DestroyExistingPlayerTimer(player);

                if (IsPlayerAuthorized(player, workcartCardReader)
                    && !permission.UserHasPermission(player.UserIDString, PermissionFreeRides))
                {
                    ChatMessage(player, Lang.InfoStillAuthorized, _pluginConfig.AuthorizationGraceTimeSecondsOffWorkcart);
                    _playerTimers[player.userID] = timer.Once(_pluginConfig.AuthorizationGraceTimeSecondsOffWorkcart, () =>
                    {
                        if (DeauthorizePlayer(workcart, workcartCardReader, player))
                            _playerTimers.Remove(player.userID);
                    });
                }
            });
        }

        // When players swipe a card reader that is static or on an automated workcart...
        private bool? OnCardSwipe(CardReader cardReader, Keycard keycard, BasePlayer player)
        {
            var workcart = cardReader.GetParentEntity() as TrainEngine;
            WorkcartCardReader workcartCardReader = null;

            if (!_mapCardReaderManager.IsMapCardReader(cardReader))
            {
                if (workcart == null)
                    return null;

                workcartCardReader = WorkcartCardReader.GetForWorkcart(workcart);
                if (workcartCardReader == null)
                    return null;
            }

            var cardItem = keycard.GetItem();
            var cardConfig = _pluginConfig.FindMatchingCardConfig(keycard, cardItem);

            if (cardConfig == null)
            {
                ChatMessage(player, Lang.ErrorCardNotAccepted);
                Effect.server.Run(cardReader.accessDeniedEffect.resourcePath, cardReader.audioPosition.position, Vector3.up);
                return false;
            }

            Effect.server.Run(cardReader.accessGrantedEffect.resourcePath, cardReader.audioPosition.position, Vector3.up);

            if (IsPlayerAuthorized(player, workcartCardReader))
            {
                ChatMessage(player, Lang.SuccessAlreadyAuthorized);
                return false;
            }

            if (!AuthorizePlayer(workcart, workcartCardReader, player))
                return false;

            if (cardConfig.PercentConditionLossPerSwipe > 0f)
            {
                cardItem.conditionNormalized -= cardConfig.PercentConditionLossPerSwipe * 0.01f;
                if (cardItem.condition <= 0.01f)
                {
                    Effect.server.Run(ItemBrokenEffectPrefab, player, 0u, Vector3.zero, Vector3.zero);
                    cardItem.Remove();
                }
                else
                {
                    cardItem.MarkDirty();
                }
            }

            DestroyExistingPlayerTimer(player);

            if (_pluginConfig.EnableGlobalAuthorization)
                ChatMessage(player, Lang.SuccessAuthorizedToAllWorkcarts);
            else
                ChatMessage(player, Lang.SuccessAuthorizedToWorkcart);

            return false;
        }

        #endregion

        #region Dependencies

        private void CheckDependencies()
        {
            if (_pluginConfig.AddToAllWorkcarts)
                return;

            if (_pluginConfig.AddToAutomatedWorkcarts && AutomatedWorkcarts == null)
                LogError("AutomatedWorkcarts is not loaded, get it at http://umod.org. If you don't intend to use this plugin with Automated Workcarts, then set \"AddToAutomatedWorkcarts\" to false in the config and you will no longer see this message.");
        }

        private TrainEngine[] GetAutomatedWorkcarts()
        {
            return AutomatedWorkcarts?.Call("API_GetAutomatedWorkcarts") as TrainEngine[];
        }

        private bool IsCargoTrain(TrainEngine workcart)
        {
            return CargoTrainEvent?.Call("IsTrainSpecial", workcart.net.ID)?.Equals(true) ?? false;
        }

        #endregion

        #region API

        private bool API_AddCardReader(TrainEngine workcart)
        {
            if (WorkcartCardReader.GetForWorkcart(workcart) != null)
                return true;

            return AddCardReader(workcart);
        }

        private void API_RemoveCardReader(TrainEngine workcart)
        {
            WorkcartCardReader.RemoveFromWorkcart(workcart);
        }

        private bool API_HasCardReader(TrainEngine workcart)
        {
            return WorkcartCardReader.GetForWorkcart(workcart) != null;
        }

        private bool API_AuthorizePlayer(BasePlayer player, TrainEngine workcart)
        {
            WorkcartCardReader workcartCardReader = null;
            if (!_pluginConfig.EnableGlobalAuthorization)
            {
                workcartCardReader = WorkcartCardReader.GetForWorkcart(workcart);
                if (workcartCardReader == null)
                    return false;
            }

            if (IsPlayerAuthorized(player, workcartCardReader))
                return true;

            return AuthorizePlayer(workcart, workcartCardReader, player);
        }

        private bool API_DeauthorizePlayer(BasePlayer player, TrainEngine workcart)
        {
            WorkcartCardReader workcartCardReader = null;
            if (!_pluginConfig.EnableGlobalAuthorization)
            {
                workcartCardReader = WorkcartCardReader.GetForWorkcart(workcart);
                if (workcartCardReader == null)
                    return false;
            }

            if (!IsPlayerAuthorized(player, workcartCardReader))
                return true;

            return DeauthorizePlayer(workcart, workcartCardReader, player);
        }

        private bool API_IsPlayerAuthorized(BasePlayer player, TrainEngine workcart)
        {
            WorkcartCardReader workcartCardReader = null;
            if (!_pluginConfig.EnableGlobalAuthorization)
            {
                workcartCardReader = WorkcartCardReader.GetForWorkcart(workcart);
                if (workcartCardReader == null)
                    return false;
            }

            return IsPlayerAuthorized(player, workcartCardReader);
        }

        #endregion

        #region Exposed Hooks

        private static bool AddCardReaderWasBlocked(TrainEngine workcart)
        {
            object hookResult = Interface.CallHook("OnWorkcartCardReaderAdd", workcart);
            return hookResult is bool && (bool)hookResult == false;
        }

        private static bool AuthorizePlayerWasBlocked(BasePlayer player, TrainEngine workcart)
        {
            object hookResult = Interface.CallHook("OnWorkcartPlayerAuthorize", player, workcart);
            return hookResult is bool && (bool)hookResult == false;
        }

        private static void CallHookPlayerAuthorized(BasePlayer player, TrainEngine workcart)
        {
            Interface.CallHook("OnWorkcartPlayerAuthorized", player, workcart);
        }

        private static bool DeauthorizePlayerWasBlocked(BasePlayer player, TrainEngine workcart)
        {
            object hookResult = Interface.CallHook("OnWorkcartPlayerDeauthorize", player, workcart);
            return hookResult is bool && (bool)hookResult == false;
        }

        private static void CallHookPlayerDeauthorized(BasePlayer player, TrainEngine workcart)
        {
            Interface.CallHook("OnWorkcartPlayerDeauthorized", player, workcart);
        }

        private static bool EjectPlayerWasBlocked(BasePlayer player, TrainEngine workcart)
        {
            object hookResult = Interface.CallHook("OnWorkcartPlayerEject", player, workcart);
            return hookResult is bool && (bool)hookResult == false;
        }

        private static Vector3? CallHookDetermineEjectPosition(BasePlayer player, TrainEngine workcart)
        {
            object hookResult = Interface.CallHook("OnWorkcartEjectPositionDetermine", player, workcart);
            return hookResult is Vector3 ? (Vector3)hookResult : (Vector3?)null;
        }

        private static void CallHookPlayerEjected(BasePlayer player, TrainEngine workcart)
        {
            Interface.CallHook("OnWorkcartPlayerEjected", player, workcart);
        }

        #endregion

        #region Commands

        [Command("wcr.spawn")]
        private void CommandSpawnCardReader(IPlayer player)
        {
            if (player.IsServer
                || !VerifyGlobalAuthorizationEnabled(player)
                || !VerifyPermission(player, PermissionManageCardReaders))
                return;

            var basePlayer = player.Object as BasePlayer;

            RaycastHit hit;
            if (!TryRaycast(basePlayer, out hit, 10))
            {
                ReplyToPlayer(player, Lang.ErrorNoSurface);
                return;
            }

            var hitVector = hit.point - basePlayer.eyes.position;
            var reflectVector = Vector3.Reflect(hitVector, hit.normal);
            var middleVector = Vector3.Lerp(-hitVector, reflectVector, 0.5f);
            var rotation = Quaternion.LookRotation(middleVector);

            _mapCardReaderManager.CreateMapCardReader(new PositionAndRotation
            {
                Position = hit.point - rotation * StaticCardReaderOffset,
                RotationAngles = rotation.eulerAngles,
            });
        }

        [Command("wcr.kill")]
        private void CommandKillCardReader(IPlayer player)
        {
            if (player.IsServer
                || !VerifyGlobalAuthorizationEnabled(player)
                || !VerifyPermission(player, PermissionManageCardReaders))
                return;

            var basePlayer = player.Object as BasePlayer;
            var cardReader = GetLookEntity(basePlayer, 10) as CardReader;
            if (cardReader == null)
            {
                ReplyToPlayer(player, Lang.ErrorNoCardReaderFound);
                return;
            }

            if (!_mapCardReaderManager.IsMapCardReader(cardReader))
            {
                ReplyToPlayer(player, Lang.ErrorNotMapCardReader);
                return;
            }

            _mapCardReaderManager.DestroyMapCardReader(cardReader);
        }

        #endregion

        #region Helper Methods - Command Checks

        private bool VerifyPermission(IPlayer player, string permissionName)
        {
            if (player.HasPermission(permissionName))
                return true;

            ReplyToPlayer(player, Lang.ErrorNoPermission);
            return false;
        }

        private bool VerifyGlobalAuthorizationEnabled(IPlayer player)
        {
            if (_pluginConfig.EnableGlobalAuthorization)
                return true;

            ReplyToPlayer(player, Lang.ErrorGlobalAuthorizationDisabled);
            return false;
        }

        #endregion

        #region Helper Methods

        private static bool TryRaycast(BasePlayer player, out RaycastHit hit, float maxDistance) =>
            Physics.Raycast(player.eyes.HeadRay(), out hit, maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        private static BaseEntity GetLookEntity(BasePlayer player, float maxDistance)
        {
            RaycastHit hit;
            return TryRaycast(player, out hit, maxDistance)
                ? hit.GetEntity()
                : null;
        }

        private static bool AddCardReader(TrainEngine workcart)
        {
            if (AddCardReaderWasBlocked(workcart))
                return false;

            if (_pluginInstance.IsCargoTrain(workcart))
                return false;

            WorkcartCardReader.AddToWorkcart(workcart);
            return true;
        }

        private static CardReader CreateCardReader(Vector3 position, Quaternion rotation, BaseEntity parent = null)
        {
            var reader = GameManager.server.CreateEntity(CardReaderPrefab, position, rotation) as CardReader;
            if (reader == null)
                return null;

            reader.SetFlag(IOEntity.Flag_HasPower, true);
            reader.accessLevel = _pluginConfig.CardReaderAccessLevel;
            reader.SetParent(parent);
            reader.EnableSaving(false);
            reader.Spawn();

            Effect.server.Run(CardReaderDeployEffectPrefab, reader.transform.position, Vector3.up);

            return reader;
        }

        private static bool HasPlayerOnBoard(TrainEngine workcart, BasePlayer player)
        {
            if (!workcart.platformParentTrigger.HasAnyEntityContents)
                return false;

            foreach (var entity in workcart.platformParentTrigger.entityContents)
            {
                if (player == entity)
                    return true;
            }

            return false;
        }

        private void DestroyExistingPlayerTimer(BasePlayer player)
        {
            Timer timerInstance;
            if (_playerTimers.TryGetValue(player.userID, out timerInstance))
            {
                timerInstance.Destroy();
                _playerTimers.Remove(player.userID);
            }
        }

        private bool IsPlayerAuthorized(BasePlayer player, WorkcartCardReader workcartCardReader)
        {
            if (permission.UserHasPermission(player.UserIDString, PermissionFreeRides))
                return true;

            return _pluginConfig.EnableGlobalAuthorization
                ? _globallyAuthorizedPlayers.Contains(player.userID)
                : workcartCardReader.IsPlayerAuthorized(player);
        }

        private bool AuthorizePlayer(TrainEngine workcart, WorkcartCardReader workcartCardReader, BasePlayer player)
        {
            if (AuthorizePlayerWasBlocked(player, workcart))
                return false;

            if (_pluginConfig.EnableGlobalAuthorization)
                _globallyAuthorizedPlayers.Add(player.userID);
            else
                workcartCardReader.AuthorizePlayer(player);

            CallHookPlayerAuthorized(player, workcart);

            return true;
        }

        private bool DeauthorizePlayer(TrainEngine workcart, WorkcartCardReader workcartCardReader, BasePlayer player)
        {
            if (DeauthorizePlayerWasBlocked(player, workcart))
                return false;

            if (_pluginConfig.EnableGlobalAuthorization)
                _globallyAuthorizedPlayers.Remove(player.userID);
            else
                workcartCardReader.DeauthorizePlayer(player);

            CallHookPlayerDeauthorized(player, workcart);

            return true;
        }

        private Vector3 DetermineEjectPosition(BasePlayer player, TrainEngine workcart)
        {
            var overridePosition = CallHookDetermineEjectPosition(player, workcart);
            if (overridePosition != null)
                return (Vector3)overridePosition;

            var playerLocalPosition = workcart.transform.InverseTransformPoint(player.transform.position);
            return workcart.transform.TransformPoint(new Vector3(-2.3f, 1.34f, playerLocalPosition.z));
        }

        private void PerformEject(TrainEngine workcart, BasePlayer player)
        {
            if (EjectPlayerWasBlocked(player, workcart))
                return;

            var ejectPosition = DetermineEjectPosition(player, workcart);

            // Remove the player from the parent trigger to unparent them before teleporting,
            // or else they may appear underwater briefly.
            workcart.platformParentTrigger.RemoveEntity(player);

            player.Teleport(ejectPosition);
            player.ForceUpdateTriggers();
            ChatMessage(player, Lang.InfoRemovedForNotSwiping);
            CallHookPlayerEjected(player, workcart);
        }

        #endregion

        #region Workcart Card Readers

        private class WorkcartCardReader : EntityComponent<TrainEngine>
        {
            private const float SpeedTolerance = 3;

            public static void AddToWorkcart(TrainEngine workcart) =>
                workcart.GetOrAddComponent<WorkcartCardReader>();

            public static WorkcartCardReader GetForWorkcart(TrainEngine workcart) =>
                workcart.GetComponent<WorkcartCardReader>();

            public static void RemoveFromWorkcart(TrainEngine workcart) =>
                UnityEngine.Object.DestroyImmediate(workcart.gameObject.GetComponent<WorkcartCardReader>());

            private HashSet<ulong> _authorizedUsers;

            private TrainEngine _workcart;
            private float _previousSpeed;

            private CardReader[] _cardReaders;

            private void Awake()
            {
                if (!_pluginConfig.EnableGlobalAuthorization)
                    _authorizedUsers = new HashSet<ulong>();

                var readerPositions = _pluginConfig.CardReaderPositions;
                _cardReaders = new CardReader[readerPositions.Length];
                for (var i = 0; i < readerPositions.Length; i++)
                {
                    var entry = readerPositions[i];
                    _cardReaders[i] = CreateCardReader(entry.Position, entry.Rotation, baseEntity);
                }

                _previousSpeed = baseEntity.GetTrackSpeed();
                InvokeRandomized(CheckSpeed, 1, 1, 0.1f);
            }

            public void AuthorizePlayer(BasePlayer player) =>
                _authorizedUsers.Add(player.userID);

            public bool IsPlayerAuthorized(BasePlayer player) =>
                _authorizedUsers.Contains(player.userID);

            public void DeauthorizePlayer(BasePlayer player) =>
                _authorizedUsers.Remove(player.userID);

            private void CheckSpeed()
            {
                var trackSpeed = baseEntity.GetTrackSpeed();
                if (trackSpeed < SpeedTolerance && _previousSpeed > SpeedTolerance)
                {
                    foreach (var cardReader in _cardReaders)
                    {
                        // Temporarily fix the card reader being invisible.
                        cardReader.TerminateOnClient(BaseNetworkable.DestroyMode.None);
                        cardReader.SendNetworkUpdateImmediate();
                    }
                }

                _previousSpeed = trackSpeed;
            }

            private void OnDestroy()
            {
                if (_cardReaders == null)
                    return;

                foreach (var cardReader in _cardReaders)
                {
                    if (cardReader != null && !cardReader.IsDestroyed)
                        cardReader.Kill();
                }
            }
        }

        #endregion

        #region Map Card Readers

        private class MapCardReaderManager
        {
            private Dictionary<CardReader, PositionAndRotation> _spawnedMapCardReaders = new Dictionary<CardReader, PositionAndRotation>();

            public void CreateMapCardReader(PositionAndRotation positionAndRotation)
            {
                var cardReader = CreateCardReader(positionAndRotation.Position, positionAndRotation.Rotation);
                _spawnedMapCardReaders[cardReader] = positionAndRotation;
                _mapData.AddCardReader(positionAndRotation);
            }

            public void DestroyMapCardReader(CardReader cardReader)
            {
                PositionAndRotation positionAndRotation;
                if (_spawnedMapCardReaders.TryGetValue(cardReader, out positionAndRotation))
                {
                    _mapData.RemoveCardReader(positionAndRotation);
                    cardReader.Kill();
                }
            }

            public bool IsMapCardReader(CardReader cardReader) =>
                _spawnedMapCardReaders.ContainsKey(cardReader);

            public void CreateAll()
            {
                foreach (var positionAndRotation in _mapData.CardReaderPositions)
                {
                    var cardReader = CreateCardReader(positionAndRotation.Position, positionAndRotation.Rotation);
                    _spawnedMapCardReaders[cardReader] = positionAndRotation;
                }
            }

            public void DestroyAll()
            {
                foreach (var cardReader in _spawnedMapCardReaders.Keys)
                {
                    if (cardReader != null)
                        cardReader.Kill();
                }
            }
        }

        #endregion

        #region Map Data

        private class StoredMapData
        {
            [JsonProperty("CardReaderPositions")]
            public List<PositionAndRotation> CardReaderPositions = new List<PositionAndRotation>();

            // Return example: proceduralmap.1500.548423.212
            private static string GetPerWipeSaveName() =>
                World.SaveFileName.Substring(0, World.SaveFileName.LastIndexOf("."));

            // Return example: proceduralmap.1500.548423
            private static string GetCrossWipeSaveName()
            {
                var saveName = GetPerWipeSaveName();
                return saveName.Substring(0, saveName.LastIndexOf("."));
            }

            private static bool IsProcedural() => World.SaveFileName.StartsWith("proceduralmap");

            private static string GetFilepath() => IsProcedural()
                ? $"{_pluginInstance.Name}/{GetPerWipeSaveName()}"
                : $"{_pluginInstance.Name}/{GetCrossWipeSaveName()}";

            public static StoredMapData Load()
            {
                var filepath = GetFilepath();

                if (Interface.Oxide.DataFileSystem.ExistsDatafile(filepath))
                    return Interface.Oxide.DataFileSystem.ReadObject<StoredMapData>(filepath) ?? new StoredMapData();

                return new StoredMapData();
            }

            public StoredMapData Save()
            {
                Interface.Oxide.DataFileSystem.WriteObject(GetFilepath(), this);
                return this;
            }

            public void AddCardReader(PositionAndRotation positionAndRotation)
            {
                CardReaderPositions.Add(positionAndRotation);
                Save();
            }

            public void RemoveCardReader(PositionAndRotation positionAndRotation)
            {
                CardReaderPositions.Remove(positionAndRotation);
                Save();
            }
        }

        #endregion

        #region Configuration

        private class PositionAndRotation
        {
            [JsonProperty("Position")]
            public Vector3 Position;

            [JsonProperty("RotationAngles")]
            public Vector3 RotationAngles;

            private Quaternion? _rotation;
            [JsonIgnore]
            public Quaternion Rotation
            {
                get
                {
                    if (_rotation == null)
                        _rotation = Quaternion.Euler(RotationAngles);

                    return (Quaternion)_rotation;
                }
            }
        }

        private class CardSettings
        {
            [JsonProperty("AccessLevel", DefaultValueHandling = DefaultValueHandling.Ignore)]
            public int AccessLevel;

            [JsonProperty("Skin", DefaultValueHandling = DefaultValueHandling.Ignore)]
            public ulong Skin;

            [JsonProperty("PercentConditionLossPerSwipe")]
            public int PercentConditionLossPerSwipe;
        }

        private class Configuration : SerializableConfiguration
        {
            [JsonProperty("AddToAllWorkcarts")]
            public bool AddToAllWorkcarts = false;

            [JsonProperty("AddToAutomatedWorkcarts")]
            public bool AddToAutomatedWorkcarts = true;

            [JsonProperty("AllowedSecondsToSwipeBeforeEject")]
            public float AllowedSecondsToSwipeBeforeEject = 10;

            [JsonProperty("EnableGlobalAuthorization")]
            public bool EnableGlobalAuthorization = false;

            [JsonProperty("AuthorizationGraceTimeSecondsOffWorkcart")]
            public float AuthorizationGraceTimeSecondsOffWorkcart = 60;

            [JsonProperty("CardReaderAccessLevel")]
            public int CardReaderAccessLevel = 1;

            [JsonProperty("AcceptedCards")]
            public CardSettings[] AcceptedCards = new CardSettings[]
            {
                new CardSettings
                {
                    AccessLevel = 1,
                    PercentConditionLossPerSwipe = 20,
                },
                new CardSettings
                {
                    AccessLevel = 2,
                    PercentConditionLossPerSwipe = 15,
                },
                new CardSettings
                {
                    AccessLevel = 3,
                    PercentConditionLossPerSwipe = 10,
                },
                new CardSettings
                {
                    Skin = 1988408422,
                    PercentConditionLossPerSwipe = 0,
                },
            };

            [JsonProperty("CardReaderPositions")]
            public PositionAndRotation[] CardReaderPositions = new PositionAndRotation[]
            {
                new PositionAndRotation
                {
                    Position = new Vector3(0.1f, 1.4f, 1.8165f),
                    RotationAngles = new Vector3(0, 180, 0),
                },
            };

            public CardSettings FindMatchingCardConfig(Keycard keycard, Item cardItem)
            {
                foreach (var cardConfig in AcceptedCards)
                {
                    if (cardItem.skin != 0)
                    {
                        // The card has a skin which must match.
                        if (cardConfig.Skin == cardItem.skin)
                            return cardConfig;
                    }
                    else if (cardConfig.AccessLevel != 0)
                    {
                        // The card does not have a skin but has a specified access level which must match.
                        if (cardConfig.AccessLevel == keycard.accessLevel)
                            return cardConfig;
                    }
                }

                return null;
            }
        }

        private Configuration GetDefaultConfig() => new Configuration();

        #endregion

        #region Configuration Boilerplate

        private class SerializableConfiguration
        {
            public string ToJson() => JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() => JsonHelper.Deserialize(ToJson()) as Dictionary<string, object>;
        }

        private static class JsonHelper
        {
            public static object Deserialize(string json) => ToObject(JToken.Parse(json));

            private static object ToObject(JToken token)
            {
                switch (token.Type)
                {
                    case JTokenType.Object:
                        return token.Children<JProperty>()
                                    .ToDictionary(prop => prop.Name,
                                                  prop => ToObject(prop.Value));

                    case JTokenType.Array:
                        return token.Select(ToObject).ToList();

                    default:
                        return ((JValue)token).Value;
                }
            }
        }

        private bool MaybeUpdateConfig(SerializableConfiguration config)
        {
            var currentWithDefaults = config.ToDictionary();
            var currentRaw = Config.ToDictionary(x => x.Key, x => x.Value);
            return MaybeUpdateConfigDict(currentWithDefaults, currentRaw);
        }

        private bool MaybeUpdateConfigDict(Dictionary<string, object> currentWithDefaults, Dictionary<string, object> currentRaw)
        {
            bool changed = false;

            foreach (var key in currentWithDefaults.Keys)
            {
                object currentRawValue;
                if (currentRaw.TryGetValue(key, out currentRawValue))
                {
                    var defaultDictValue = currentWithDefaults[key] as Dictionary<string, object>;
                    var currentDictValue = currentRawValue as Dictionary<string, object>;

                    if (defaultDictValue != null)
                    {
                        if (currentDictValue == null)
                        {
                            currentRaw[key] = currentWithDefaults[key];
                            changed = true;
                        }
                        else if (MaybeUpdateConfigDict(defaultDictValue, currentDictValue))
                            changed = true;
                    }
                }
                else
                {
                    currentRaw[key] = currentWithDefaults[key];
                    changed = true;
                }
            }

            return changed;
        }

        protected override void LoadDefaultConfig() => _pluginConfig = GetDefaultConfig();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _pluginConfig = Config.ReadObject<Configuration>();
                if (_pluginConfig == null)
                {
                    throw new JsonException();
                }

                if (MaybeUpdateConfig(_pluginConfig))
                {
                    LogWarning("Configuration appears to be outdated; updating and saving");
                    SaveConfig();
                }
            }
            catch (Exception e)
            {
                LogError(e.Message);
                LogWarning($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Log($"Configuration changes saved to {Name}.json");
            Config.WriteObject(_pluginConfig, true);
        }

        #endregion

        #region Localization

        private string GetMessage(string playerId, string messageName, params object[] args)
        {
            var message = lang.GetMessage(messageName, this, playerId);
            return args.Length > 0 ? string.Format(message, args) : message;
        }

        private string GetMessage(IPlayer player, string messageName, params object[] args) =>
            GetMessage(player.Id, messageName, args);

        private string GetMessage(BasePlayer player, string messageName, params object[] args) =>
            GetMessage(player.UserIDString, messageName, args);

        private void ReplyToPlayer(IPlayer player, string messageName, params object[] args) =>
            player.Reply(string.Format(GetMessage(player, messageName), args));

        private void ChatMessage(BasePlayer player, string messageName, params object[] args) =>
            player.ChatMessage(string.Format(GetMessage(player, messageName), args));

        private class Lang
        {
            public const string ErrorNoPermission = "Error.NoPermission";
            public const string ErrorCardNotAccepted = "Error.CardNotAccepted";
            public const string ErrorGlobalAuthorizationDisabled = "Error.GlobalAuthorizationDisabled";
            public const string ErrorNoSurface = "Error.NoSurface";
            public const string ErrorNoCardReaderFound = "Error.NoCardReaderFound";
            public const string ErrorNotMapCardReader = "Error.NotMapCardReader";
            public const string WarningSwipeRequired = "Warning.SwipeRequired";
            public const string SuccessAuthorizedToWorkcart = "Success.AuthorizedToWorkcart";
            public const string SuccessAuthorizedToAllWorkcarts = "Success.AuthorizedToAllWorkcarts";
            public const string SuccessAlreadyAuthorized = "Success.AlreadyAuthorized";
            public const string InfoStillAuthorized = "Info.StillAuthorized";
            public const string InfoRemovedForNotSwiping = "Info.RemovedForNotSwiping";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.ErrorNoPermission] = "You don't have permission to do that.",
                [Lang.ErrorCardNotAccepted] = "Error: That card is not accepted here.",
                [Lang.ErrorGlobalAuthorizationDisabled] = "Error: Static card readers are not allowed while global authorizaton is disabled.",
                [Lang.ErrorNoSurface] = "Error: No surface.",
                [Lang.ErrorNoCardReaderFound] = "Error: No card reader found.",
                [Lang.ErrorNotMapCardReader] = "Error: Not a map card reader.",
                [Lang.WarningSwipeRequired] = "You have <color=#f30>{0}</color> seconds to swipe a workcart pass.",
                [Lang.SuccessAuthorizedToWorkcart] = "You are <color=#3f3>authorized</color> to ride this workcart.",
                [Lang.SuccessAuthorizedToAllWorkcarts] = "You are <color=#3f3>authorized</color> to ride all workcarts.",
                [Lang.SuccessAlreadyAuthorized] = "You are already <color=#3f3>authorized</color>.",
                [Lang.InfoStillAuthorized] = "You are still authorized for <color=#fd4>{0}</color> seconds.",
                [Lang.InfoRemovedForNotSwiping] = "You were removed from the workcart because you did not swipe a workcart pass in time.",
            }, this, "en");
        }

        #endregion
    }
}
