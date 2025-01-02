using UnityEditor.Callbacks;
using UnityEngine;

namespace _SyncToBackend.Editor
{
    public class NetworkScenePostProcess : MonoBehaviour
    {
        [PostProcessScene]
        public static void OnPostProcessScene()
        {
            SyncToBackend.SceneLoaded();
        }
    }
}
