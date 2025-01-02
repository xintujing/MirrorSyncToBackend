using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Mirror;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace _SyncToBackend.Editor
{
    public static class SyncToBackend
    {
        private static bool init = false;
        public static Data data = new Data();
        
        private const string JsonPath = "_SyncToBackend/tobackend.json";

        static SyncToBackend()
        {
            // ResetBuildData();
        }

        public static void ResetBuildData()
        {
            data.syncVars = new List<SyncVarData>();
            data.methods = new List<MethodData>();
            data.sceneIds = new List<KeyValue<string, string>>();
            data.assets = new List<KeyValue<uint, string>>();
            data.networkIdentities = new List<NetworkIdentityData>();
            data.networkManagerSettings = new List<NetworkManagerSetting>();
        }

        public static void Awake(MonoBehaviour mb)
        {
            // Debug.Log(mb.GetType().ToString());
            // Debug.Log(mb.name);
            if (mb is NetworkAnimator networkAnimator)
            {

                var networkIdentity = networkAnimator.GetComponent<NetworkIdentity>();
                if (networkIdentity)
                {
                    var na = SyncToBackendNetworkAnimator.Animator(networkAnimator);
                    string json = JsonUtility.ToJson(na, true);
                    File.WriteAllText("_SyncToBackend/network_animator_" + networkIdentity.assetId + ".json", json);
                }
            }
        }

        public static void SceneLoaded()
        {
                
            if (!init)
            {
                MonoBehaviourHook();
                LoadBuild();
                LoadAssets();
                ExportPrefabData();
                init = true;
            }

            GetActiveSceneNetworkIdentityData();
            ExportDataJson();
        }

        public static void ExportDataJson()
        {
            string[] sceneIdPaths = Directory.GetFiles("_SyncToBackend", "scene_id_*.json");

            foreach (var filePath in sceneIdPaths)
            {
                if (File.Exists(filePath))
                {
                    string path = File.ReadAllText(filePath);
                    string fileName = Path.GetFileNameWithoutExtension(filePath);
                    var id = fileName.Split('_').LastOrDefault();
                    
                    if (data.sceneIds.ContainsKey(path))
                    {
                        data.sceneIds.Set(path, id);
                        var networkIdentityDataPath = "_SyncToBackend/network_identity_" + id + ".json";
                        if (File.Exists(networkIdentityDataPath))
                        {
                            var networkIdentityData = JsonUtility.FromJson<NetworkIdentityData>(File.ReadAllText(networkIdentityDataPath));
                            data.networkIdentities.Add(networkIdentityData);
                        }
                    }
                }
            }
            
            // string[] networkIdentityPaths = Directory.GetFiles("_SyncToBackend", "network_identity_*.json");
            //
            // foreach (var filePath in networkIdentityPaths)
            // {
            //     string fileName = Path.GetFileNameWithoutExtension(filePath);
            //     var id = fileName.Split('_').LastOrDefault();
            // }

            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(JsonPath, json);
        }

        public static void MonoBehaviourHook()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (Assembly assembly in assemblies)
            {
                if (assembly.GetName().Name == "Mirror")
                {
                    Type targetType = assembly.GetType("Mirror.MonoBehaviourSyncToBackend");
                        
                    if (targetType != null)
                    {
                        FieldInfo awakeField = targetType.GetField("awakeMethod", BindingFlags.Public | BindingFlags.Static);
                        if (awakeField != null)
                        {
                            Type actionType = typeof(Action<>).MakeGenericType(typeof(MonoBehaviour));
                            Delegate awakeDelegate = Delegate.CreateDelegate(actionType, null, typeof(SyncToBackend).GetMethod("Awake"));

                            awakeField.SetValue(null, awakeDelegate);
                        }
                        FieldInfo startField = targetType.GetField("startMethod", BindingFlags.Public | BindingFlags.Static);
                        if (startField != null)
                        {
                            Type actionType = typeof(Action<>).MakeGenericType(typeof(MonoBehaviour));
                            Delegate awakeDelegate = Delegate.CreateDelegate(actionType, null, typeof(SyncToBackend).GetMethod("Awake"));

                            startField.SetValue(null, awakeDelegate);
                        }
                    }
                }
            }
        }

        public static string ReadString(BinaryReader binaryReader)
        {
            var length = binaryReader.ReadUInt16();
            return Encoding.ASCII.GetString(binaryReader.ReadBytes(length));
        }

        public static void ReadSyncVars(BinaryReader binaryReader)
        {
            data.syncVars = new List<SyncVarData>();
            var length = binaryReader.ReadUInt16();
            for (int i = 0; i < length; i++)
            {
                data.syncVars.Add(new SyncVarData()
                {
                    fullname = ReadString(binaryReader),
                    subClass = ReadString(binaryReader),
                    name = ReadString(binaryReader),
                    type = ReadString(binaryReader),
                    initialValue = new byte[]{},
                    dirtyBit = binaryReader.ReadInt32(),
                });
            }
        }

        public static void ReadMethods(BinaryReader binaryReader)
        {
            var length = binaryReader.ReadUInt16();
            for (int i = 0; i < length; i++)
            {
                var subClass = ReadString(binaryReader);
                var name = ReadString(binaryReader);
                var requiresAuthority = binaryReader.ReadByte() != 0;
                var type = (MethodType)binaryReader.ReadByte();
                var parameters = new List<KeyValue<string, string>>() { };
                var parametersLength = binaryReader.ReadUInt16();
                for (var j = 0; j < parametersLength; j++) {
                    parameters.Add(ReadString(binaryReader),ReadString(binaryReader));
                }

                var rpcs = new List<string>();
                var vars = new List<KeyValue<byte, string>>();
                if (type == MethodType.Command)
                {
                    var rpcLength = binaryReader.ReadUInt16();
                    for (int j = 0; j < rpcLength; j++)
                    {
                        rpcs.Add(ReadString(binaryReader));
                    }

                    var syncVarsLength = binaryReader.ReadUInt16();
                    for (int j = 0; j < syncVarsLength; j++)
                    {
                        var index = binaryReader.ReadByte();
                        var syncVarsIndexDataLength = binaryReader.ReadUInt16();
                        
                        for (int k = 0; k < syncVarsIndexDataLength; k++)
                        {
                            vars.Add(index, ReadString(binaryReader));
                        }
                    }
                }

                data.methods.Add(new MethodData()
                {
                    hashCode = (ushort)(name.GetStableHashCode() & 0xFFFF),
                    subClass = subClass,
                    name = name,
                    requiresAuthority = requiresAuthority,
                    type = type,
                    parameters = parameters,
                    rpcList = rpcs,
                    varList = vars,
                });
            }
        }

        public static void LoadBuild()
        {
            ResetBuildData();

            using (FileStream fileStream = new FileStream(Weaver.SyncToBackend.Filepath, FileMode.Open))
            {
                using (BinaryReader binaryReader = new BinaryReader(fileStream))
                {
                    while (binaryReader.BaseStream.Position < binaryReader.BaseStream.Length)
                    {
                        var type = binaryReader.ReadUInt16();
                        switch (type) {
                            case 1:
                                ReadSyncVars(binaryReader);
                                break;
                            case 2:
                                ReadMethods(binaryReader);
                                break;
                            default:
                                binaryReader.BaseStream.Position = binaryReader.BaseStream.Length;
                                break;
                        }
                    }
                }
            }

        }

        private static void LoadAssets()
        {
            string[] guids = AssetDatabase.FindAssets("", new[] { "Assets" });
            foreach (var guidString in guids)
            {
                if (Guid.TryParse(guidString, out Guid guid))
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guidString);
                    var assetId = NetworkIdentity.AssetGuidToUint(guid);
            
                    data.assets.Add(assetId, assetPath);
            
                    string extension = Path.GetExtension(assetPath);
                    if (extension == ".scene")
                    {
                        data.sceneIds.Add(assetPath, null);
                    }
                }
            }
        }

        private static void ExportPrefabData()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab");

            foreach (string guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith("Assets/Mirror/Tests/") || path.StartsWith("Assets/Mirror/Examples/"))
                {
                    continue;
                }
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                if (GetNetworkIdentity(prefab, out var networkIdentityData))
                {
                    data.networkIdentities.Add(networkIdentityData);
                }
                if (GetNetworkRoomManager(prefab, out var networkRoomManagerSetting))
                {
                    data.networkRoomManagerSettings.Add(networkRoomManagerSetting);
                }
                else if (GetNetworkManager(prefab, out var networkManagerSetting))
                {
                    data.networkManagerSettings.Add(networkManagerSetting);
                }
                
            }
        }

        private static void GetActiveSceneNetworkIdentityData()
        {
            Scene currentScene = SceneManager.GetActiveScene();
            GameObject[] rootObjects = currentScene.GetRootGameObjects();
            List<Component> allComponents = new List<Component>();

            foreach (GameObject rootObject in rootObjects)
            {
                GetComponentsRecursive(currentScene.path, rootObject, allComponents);
            }
        } 
        static void GetComponentsRecursive(string scenePath, GameObject gameObject, List<Component> componentList)
        {
            Component[] components = gameObject.GetComponents<Component>();
            componentList.AddRange(components);
             
            if (gameObject.TryGetComponent(out NetworkIdentity networkIdentity))
            {
                ExportSceneData(scenePath, gameObject);
            }
            
            for (int i = 0; i < gameObject.transform.childCount; i++)
            {
                GetComponentsRecursive(scenePath, gameObject.transform.GetChild(i).gameObject, componentList);
            }
        }

        private static bool GetNetworkRoomManager(GameObject gameObject, out NetworkRoomManagerSetting networkRoomManagerSetting)
        {
            networkRoomManagerSetting = new NetworkRoomManagerSetting();

            if (gameObject.TryGetComponent(out NetworkRoomManager networkRoomManager))
            {
                networkRoomManagerSetting = SetNetworkRoomManagerSetting(networkRoomManager);
                return true;
            }

            return false;
        }
        private static bool GetNetworkManager(GameObject gameObject, out NetworkManagerSetting networkManagerSetting)
        {
            networkManagerSetting = new NetworkManagerSetting();

            if (gameObject.TryGetComponent(out NetworkManager networkManager))
            {
                networkManagerSetting = SetNetworkManagerSetting(networkManager);
                return true;
            }

            return false;
        }

        private static bool GetNetworkIdentity(GameObject gameObject, out NetworkIdentityData networkIdentityData)
        {
            networkIdentityData = new NetworkIdentityData();

            if (gameObject.TryGetComponent(out NetworkIdentity networkIdentity))
            {

                networkIdentityData.assetId = networkIdentity.assetId;

                if (networkIdentity.sceneId != 0)
                {
                    string scenePath = gameObject.scene.path.ToLower();
                    
                    // get deterministic scene hash
                    uint pathHash = (uint)scenePath.GetStableHashCode();
                
                    // shift hash from 0x000000FFFFFFFF to 0xFFFFFFFF00000000
                    ulong shiftedHash = (ulong)pathHash << 32;
                
                    // OR into scene id
                    networkIdentityData.sceneIdLongType = (networkIdentity.sceneId & 0xFFFFFFFF) | shiftedHash;
                    networkIdentityData.sceneId = networkIdentityData.sceneIdLongType.ToString();

                    // if (data.sceneIds.ContainsKey(gameObject.scene.path))
                    // {
                    //     data.sceneIds.Set(gameObject.scene.path, networkIdentityData.sceneId);
                    // }
                    // else
                    // {
                    //     data.sceneIds.Add(gameObject.scene.path, networkIdentityData.sceneId);
                    // }
                    
                    File.WriteAllText("_SyncToBackend/scene_id_" + networkIdentityData.sceneId + ".json", gameObject.scene.path);
                }
                
                NetworkBehaviour[] networkBehaviours = gameObject.GetComponentsInChildren<NetworkBehaviour>(true);

                networkIdentityData.networkBehaviourComponents = new List<KeyValue<byte, NetworkBehaviourComponent>>();

                for (byte i = 0; i < networkBehaviours.Length; ++i)
                {
                    var comp = new NetworkBehaviourComponent();
                    comp.componentIndex = i;
                    
                    var component = networkBehaviours[i];
                    if (component is NetworkTransformBase networkTransformBase)
                    {
                        comp.networkBehaviourSetting = SetNetworkBehaviour(networkTransformBase);
                        comp.networkTransformBaseSetting = SetNetworkTransformBase(networkTransformBase);
                    }
                    if (component is NetworkTransformUnreliable networkTransformUnreliable)
                    {
                        comp.componentType = networkTransformUnreliable.GetType().ToString();
                        comp.networkTransformUnreliableSetting = SetNetworkTransformUnreliable(networkTransformUnreliable);
                    } else if (component is NetworkTransformReliable networkTransformReliable)
                    {
                        comp.componentType = networkTransformReliable.GetType().ToString();
                        comp.networkTransformReliableSetting = SetNetworkTransformReliable(networkTransformReliable);
                    } else if (component is NetworkAnimator networkAnimator)
                    {
                        comp.componentType = networkAnimator.GetType().ToString();

                        var networkAnimatorSettingPath = "_SyncToBackend/network_animator_" + networkIdentityData.assetId + ".json";
                        if (File.Exists(networkAnimatorSettingPath))
                        {
                            var networkAnimatorSetting = JsonUtility.FromJson<NetworkAnimatorSetting>(File.ReadAllText(networkAnimatorSettingPath));
                            comp.networkAnimatorSetting = networkAnimatorSetting;
                        }
                    } else if (component is NetworkRoomPlayer networkRoomPlayerSetting)
                    {
                        comp.componentType = networkRoomPlayerSetting.GetType().ToString();
                        comp.networkTransformReliableSetting = new NetworkTransformReliableSetting();
                    } else
                    {
                        comp.componentType = component.GetType().FullName;
                        for (var k = 0; k < data.syncVars.Count; k++) {
                            if (data.syncVars[k].subClass == component.GetType().FullName)
                            {
                                var v = data.syncVars[k];
                                v.initialValue = SyncToBackendUtils.GetInitialFieldValue(component, data.syncVars[k].name);
                                data.syncVars[k] = v;
                            }
                        }
                    }

                    networkIdentityData.networkBehaviourComponents.Add(i, comp);
                }

                return true;
            }
            
            return false;
        }

        private static NetworkBehaviourSetting SetNetworkBehaviour(NetworkBehaviour i)
        {
            var o = new NetworkBehaviourSetting();
            o.syncDirection = i.syncDirection;
            return o;
        }

        private static SnapshotInterpolationSetting SetSnapshotInterpolationSetting(SnapshotInterpolationSettings i)
        {
            var o = new SnapshotInterpolationSetting();
            o.bufferTimeMultiplier = i.bufferTimeMultiplier;
            o.bufferLimit = i.bufferLimit;
            o.catchupNegativeThreshold = i.catchupNegativeThreshold;
            o.catchupPositiveThreshold = i.catchupPositiveThreshold;
            o.catchupSpeed = i.catchupSpeed;
            o.slowdownSpeed = i.slowdownSpeed;
            o.driftEmaDuration = i.driftEmaDuration;
            o.dynamicAdjustment = i.dynamicAdjustment;
            o.dynamicAdjustmentTolerance = i.dynamicAdjustmentTolerance;
            o.deliveryTimeEmaDuration = i.deliveryTimeEmaDuration;
            return o;
        }

        private static NetworkRoomManagerSetting SetNetworkRoomManagerSetting(NetworkRoomManager i)
        {
            var o = new NetworkRoomManagerSetting();
            o.showRoomGUI = i.showRoomGUI;
            o.minPlayers = i.minPlayers;
            o.roomPlayerPrefab = UnityEditor.AssetDatabase.GetAssetPath(i.roomPlayerPrefab);
            o.networkManagerSetting = SetNetworkManagerSetting(i);
            return o;
        }

        private static NetworkManagerSetting SetNetworkManagerSetting(NetworkManager i)
        {
            var o = new NetworkManagerSetting();
            o.dontDestroyOnLoad = i.dontDestroyOnLoad;
            o.runInBackground = i.runInBackground;
            o.headlessStartMode = i.headlessStartMode.ToString();
            o.editorAutoStart = i.editorAutoStart;
            o.sendRate = i.sendRate;
            o.offlineScene = i.offlineScene;
            o.onlineScene = i.onlineScene;
            o.offlineSceneLoadDelay = i.offlineSceneLoadDelay;
            o.transport = i.transport.name;
            o.networkAddress = i.networkAddress;
            o.maxConnections = i.maxConnections;
            o.disconnectInactiveConnections = i.disconnectInactiveConnections;
            o.disconnectInactiveTimeout = i.disconnectInactiveTimeout;
            if (i.authenticator)
            {
                o.authenticator = i.authenticator.name;
            }
            o.playerPrefab = UnityEditor.AssetDatabase.GetAssetPath(i.playerPrefab);
            o.autoCreatePlayer = i.autoCreatePlayer;
            o.playerSpawnMethod = i.playerSpawnMethod.ToString();
            var spawnPrefabs = new List<string>(){};
            foreach (var spawn in i.spawnPrefabs)
            {
                spawnPrefabs.Add(spawn.name);
            }
            o.spawnPrefabs = spawnPrefabs;
            o.exceptionsDisconnect = i.exceptionsDisconnect;
            o.snapshotSettings = SetSnapshotInterpolationSetting(i.snapshotSettings);
            o.evaluationMethod = i.evaluationMethod.ToString();
            o.evaluationInterval = i.evaluationInterval;
            o.timeInterpolationGui = i.timeInterpolationGui;
            return o;
        }

        private static NetworkTransformBaseSetting SetNetworkTransformBase(NetworkTransformBase i)
        {
            var o = new NetworkTransformBaseSetting();
            // selective sync
            o.syncPosition = i.syncPosition;
            o.syncRotation = i.syncRotation;
            o.syncScale = i.syncScale;

            // Bandwidth Savings
        
            o.onlySyncOnChange = i.onlySyncOnChange;
            o.compressRotation = i.compressRotation;
        
            // interpolation is on by default, but can be disabled to jump to
            // the destination immediately. some projects need this.

            o.interpolatePosition = i.interpolatePosition;
            o.interpolateRotation = i.interpolateRotation;
            o.interpolateScale = i.interpolateScale;
        
            // CoordinateSpace
            o.coordinateSpace = i.coordinateSpace;
        
            // Send Interval Multiplier
            // Range 1 - 120
            o.sendIntervalMultiplier = i.sendIntervalMultiplier;
        
            // Timeline Offset
            o.timelineOffset = i.timelineOffset;

            return o;
        }

        private static NetworkTransformReliableSetting SetNetworkTransformReliable(NetworkTransformReliable i)
        {
            var o = new NetworkTransformReliableSetting();
            // Additional Settings
            o.onlySyncOnChangeCorrectionMultiplier = i.onlySyncOnChangeCorrectionMultiplier;
        
            // Rotation
            o.rotationSensitivity = i.rotationSensitivity;
        
            // Precision
            // Range(0.00_01f, 1f)
            o.positionPrecision = i.positionPrecision;
            // Range(0.00_01f, 1f)
            o.scalePrecision = i.scalePrecision;
            return o;
        }

        private static NetworkTransformUnreliableSetting SetNetworkTransformUnreliable(NetworkTransformUnreliable i)
        {
            var o = new NetworkTransformUnreliableSetting();
            // Additional Settings
            o.bufferResetMultiplier = i.bufferResetMultiplier;
            
            // Sensitivity
            o.positionSensitivity = i.positionSensitivity;
            o.rotationSensitivity = i.rotationSensitivity;
            o.scaleSensitivity = i.scaleSensitivity;
            return o;
        }

        private static void ExportSceneData(string path, GameObject gameObject)
        {
            if (GetNetworkIdentity(gameObject, out NetworkIdentityData networkIdentityData))
            {
                // data.networkIdentities.Add(networkIdentityData);
                string json = JsonUtility.ToJson(networkIdentityData, true);
                File.WriteAllText("_SyncToBackend/network_identity_" + (networkIdentityData.sceneIdLongType != 0 ? networkIdentityData.sceneIdLongType : networkIdentityData.assetId) + ".json", json);
            }
            if (GetNetworkManager(gameObject, out NetworkManagerSetting networkManagerSetting))
            {
                // data.networkManagerSettings.Add(networkManagerSetting);
                string json = JsonUtility.ToJson(networkManagerSetting, true);
                File.WriteAllText("_SyncToBackend/network_manager.json", json);
            }
        }
    }
}