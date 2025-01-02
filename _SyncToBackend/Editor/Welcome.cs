// Shows either a welcome message, only once per session.
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace _SyncToBackend.Editor
{
    static class Welcome
    {
        [InitializeOnLoadMethod]
        static void OnInitializeOnLoad()
        {
            // InitializeOnLoad is called on start and after each rebuild,
            // but we only want to show this once per editor session.
            if (!SessionState.GetBool("MIRROR_SYNC_TO_BACKEND_WELCOME", false))
            {
                SessionState.SetBool("MIRROR_SYNC_TO_BACKEND_WELCOME", true);
                Debug.Log("Mirror | SyncToBackend");
            }
        }
    }
}
#endif
