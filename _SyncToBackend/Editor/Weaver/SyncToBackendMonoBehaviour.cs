using System;

namespace _SyncToBackend.Editor.Weaver
{
    public class SyncToBackendMonoBehaviour
    {
        public static Action<SyncToBackendMonoBehaviour> awakeMethod;
        public static Action<SyncToBackendMonoBehaviour> startMethod;

        void Awake()
        {
            awakeMethod?.Invoke(this);
        }
        void Start()
        {
            startMethod?.Invoke(this);
        }
    }
}