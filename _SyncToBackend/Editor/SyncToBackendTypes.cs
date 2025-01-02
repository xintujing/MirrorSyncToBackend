using System;
using System.Collections.Generic;
using Mirror;

namespace _SyncToBackend.Editor
{
    
    [Serializable]
    public enum MethodType
    {
        Command = 1, TargetRpc = 2, ClientRpc = 3,
    }
    
    [Serializable]
    public struct MethodData
    {
        public ushort hashCode;
        public string subClass;
        public string name;
        public bool requiresAuthority;
        public MethodType type;
        public List<KeyValue<string, string>> parameters;
        public List<string> rpcList;
        public List<KeyValue<byte, string>> varList;
    }
    [Serializable]
    public struct SyncVarData
    {
        public byte index;
        public string fullname;
        public string subClass;
        public string name;
        public string type;
        public byte[] initialValue;
        public int dirtyBit;
    }

    [Serializable]
    public struct NetworkBehaviourComponent
    {
        public byte componentIndex;
        public string componentType;
        public NetworkBehaviourSetting networkBehaviourSetting;
        public NetworkTransformBaseSetting networkTransformBaseSetting;
        public NetworkTransformReliableSetting networkTransformReliableSetting;
        public NetworkTransformUnreliableSetting networkTransformUnreliableSetting;
        public NetworkAnimatorSetting networkAnimatorSetting;
    }

    [Serializable]
    public struct NetworkIdentityData
    {
        public uint assetId;
        public string sceneId;
        [NonSerialized]
        public ulong sceneIdLongType;
        public List<KeyValue<byte, NetworkBehaviourComponent>> networkBehaviourComponents;
    }

    [Serializable]
    public struct NetworkBehaviourSetting
    {
        public SyncDirection syncDirection;
    }
    
    [Serializable]
    public struct SnapshotInterpolationSetting
    {
        public double bufferTimeMultiplier;
        public int bufferLimit;
        public float catchupNegativeThreshold;
        public float catchupPositiveThreshold;
        public double catchupSpeed;
        public double slowdownSpeed;
        public int driftEmaDuration;
        public bool dynamicAdjustment;
        public float dynamicAdjustmentTolerance;
        public int deliveryTimeEmaDuration;
    }

    [Serializable]
    public struct NetworkManagerSetting
    {
        public bool dontDestroyOnLoad;
        public bool runInBackground;
        public string headlessStartMode;
        public bool editorAutoStart;
        public int sendRate;
        public string offlineScene;
        public string onlineScene;
        public float offlineSceneLoadDelay;
        public string transport;
        public string networkAddress;
        public int maxConnections;
        public bool disconnectInactiveConnections;
        public float disconnectInactiveTimeout;
        public string authenticator;
        public string playerPrefab;
        public bool autoCreatePlayer;
        public string playerSpawnMethod;
        public List<string> spawnPrefabs;
        public bool exceptionsDisconnect;
        public SnapshotInterpolationSetting snapshotSettings;
        public string evaluationMethod;
        public float evaluationInterval;
        public bool timeInterpolationGui;
    }

    [Serializable]
    public struct NetworkRoomManagerSetting
    {
        // This flag controls whether the default UI is shown for the room
        public bool showRoomGUI;
        // Minimum number of players to auto-start the game
        public int minPlayers;
        // Prefab to use for the Room Player
        public NetworkRoomPlayer roomPlayerPrefab;
    }

    [Serializable]
    public struct NetworkTransformBaseSetting
    {
        // selective sync
        public bool syncPosition;
        public bool syncRotation;
        public bool syncScale;

        // Bandwidth Savings
        public bool onlySyncOnChange;
        public bool compressRotation;
        
        // interpolation is on by default, but can be disabled to jump to
        // the destination immediately. some projects need this.

        public bool interpolatePosition;
        public bool interpolateRotation;
        public bool interpolateScale;
        
        // CoordinateSpace
        public Mirror.CoordinateSpace coordinateSpace;
        
        // Send Interval Multiplier
        // Range 1 - 120
        public uint sendIntervalMultiplier;
        
        // Timeline Offset
        public bool timelineOffset;
    }

    [Serializable]
    public enum AnimatorControllerParameterType
    {
        Float = 1,
        Int = 3,
        Bool = 4,
        Trigger = 9,
    }

    [Serializable]
    public struct NetworkAnimatorStateSetting
    {
        public int fullPathHash;
        public float normalizedTime;
        public float layerWeight;
    }

    [Serializable]
    public struct NetworkAnimatorParameterSetting
    {
        public int index;
        public AnimatorControllerParameterType type;
        public byte[] value;
    }

    [Serializable]
    public struct NetworkAnimatorData
    {
        public List<NetworkAnimatorStateSetting> layers;
        public List<NetworkAnimatorParameterSetting> parameters;
    }

    [Serializable]
    public struct NetworkAnimatorSetting
    {
        public bool clientAuthority;
        // The animator component to synchronize.
        public NetworkAnimatorData animator;
        
        // Syncs animator.speed.
        // Default to 1 because Animator.speed defaults to 1.
        public float animatorSpeed;
        public float previousSpeed;
        

        // // Note: not an object[] array because otherwise initialization is real annoying
        // int[] lastIntParameters;
        // float[] lastFloatParameters;
        // bool[] lastBoolParameters;
        // // AnimatorControllerParameter[] parameters;
        //
        // // multiple layers
        // int[] animationHash;
        // int[] transitionHash;
        // float[] layerWeight;
        // double nextSendTime;
    }

    [Serializable]
    public struct NetworkTransformReliableSetting
    {
        // Additional Settings
        public float onlySyncOnChangeCorrectionMultiplier;
        
        // Rotation
        public float rotationSensitivity;
        
        // Precision
        // Range(0.00_01f, 1f)
        public float positionPrecision;
        // Range(0.00_01f, 1f)
        public float scalePrecision;
    }

    [Serializable]
    public struct NetworkTransformUnreliableSetting
    {
        // Additional Settings
        public float bufferResetMultiplier;
        
        // Sensitivity
        public float positionSensitivity;
        public float rotationSensitivity;
        public float scaleSensitivity;
    }
    
    [Serializable]
    public struct Data
    {
        public List<MethodData> methods;
        public List<NetworkIdentityData> networkIdentities;
        public List<NetworkManagerSetting> networkManagerSettings;
        public List<KeyValue<string, string>> sceneIds;
        public List<SyncVarData> syncVars;
        public List<KeyValue<uint, string>> assets;
    }
    
    

    [Serializable]
    public struct KeyValue<TKey,TValue>
    {
        public TKey key;
        public TValue value;
    }
    public class Singleton<TKey, TValue>
    {
        private static readonly Dictionary<int, Singleton<TKey, TValue>> Instances = new Dictionary<int, Singleton<TKey, TValue>>();
        public readonly Dictionary<TKey, TValue> Dictionary = new Dictionary<TKey, TValue>();

        private Singleton()
        {
        }

        public static Singleton<TKey, TValue> Instance(int hashCode)
        {
            if (Instances.TryGetValue(hashCode, out var exist))
            {
                return exist;
            }
            var instance = new Singleton<TKey, TValue>();
            Instances.Add(hashCode, instance);
            return instance;
        }

        public bool TryAdd(TKey key, TValue value)
        {
            return Dictionary.TryAdd(key, value);
        }
    }
    
    public static class ListExtensions
    {

        public static void Add<TKey, TValue>(this List<KeyValue<TKey, TValue>> list, TKey key, TValue value)
        {
            if (Singleton<TKey, TValue>.Instance(list.GetHashCode()).TryAdd(key, value))
            {
                list.Add(new KeyValue<TKey, TValue>()
                {
                    key = key,
                    value = value,
                });
            }
        }

        public static void ReNew<TKey, TValue>(this List<KeyValue<TKey, TValue>> list)
        {
            list.Clear();
            foreach (var kv in Singleton<TKey, TValue>.Instance(list.GetHashCode()).Dictionary)
            {
                list.Add(new KeyValue<TKey, TValue>()
                {
                    key = kv.Key,
                    value = kv.Value,
                });
            }
        }

        public static bool ContainsKey<TKey, TValue>(this List<KeyValue<TKey, TValue>> list, TKey key)
        {
            return Singleton<TKey, TValue>.Instance(list.GetHashCode()).Dictionary.ContainsKey(key);
        }

        public static void Set<TKey, TValue>(this List<KeyValue<TKey, TValue>> list, TKey key, TValue value)
        {
            Singleton<TKey, TValue>.Instance(list.GetHashCode()).Dictionary[key] = value;
            ReNew(list);
        }
    }
}