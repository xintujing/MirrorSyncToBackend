using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;

namespace _SyncToBackend.Editor
{
    public class SyncToBackendNetworkAnimator : MonoBehaviour
    {
        public static NetworkAnimatorSetting Animator(NetworkAnimator networkAnimator)
        {
            
            // MonoBehaviour[] scripts = prefabGameObject.GetComponents<MonoBehaviour>();
            //
            // foreach (var script in scripts)
            // {
            //     Destroy(script);
            // }
            //
            // var instantiatedObject = Instantiate(prefabGameObject, Vector3.zero, Quaternion.identity);
            //
            //
                        
            // Scene activeScene = SceneManager.GetActiveScene();
            // SceneManager.MoveGameObjectToScene(prefabGameObject, activeScene);
            
            // Animator animator = instantiatedObject.GetComponentInChildren<Animator>();
            Animator animator = networkAnimator.animator;
            
            var networkAnimatorSetting = new NetworkAnimatorSetting();
            networkAnimatorSetting.animatorSpeed = BitConverter.ToSingle(SyncToBackendUtils.GetInitialFieldValue(networkAnimator, "animatorSpeed"), 0);
            networkAnimatorSetting.previousSpeed = BitConverter.ToSingle(SyncToBackendUtils.GetInitialFieldValue(networkAnimator, "previousSpeed"), 0);
            networkAnimatorSetting.animator = new NetworkAnimatorData();
            byte layerCount = (byte)animator.layerCount;
            networkAnimatorSetting.animator.layers = new List<NetworkAnimatorStateSetting>();
            
            for (int j = 0; j < layerCount; j++)
            {
                AnimatorStateInfo st = animator.IsInTransition(j)
                    ? animator.GetNextAnimatorStateInfo(j)
                    : animator.GetCurrentAnimatorStateInfo(j);
                var networkAnimatorStateSetting = new NetworkAnimatorStateSetting();
                networkAnimatorStateSetting.fullPathHash = st.fullPathHash;
                networkAnimatorStateSetting.normalizedTime = st.normalizedTime;
                networkAnimatorStateSetting.layerWeight = animator.GetLayerWeight(j);
                networkAnimatorSetting.animator.layers.Add(networkAnimatorStateSetting);
            }
            
            var parameters = animator.parameters
                .Where(par => !animator.IsParameterControlledByCurve(par.nameHash))
                .ToArray();
            
            byte parameterCount = (byte)parameters.Length;
            networkAnimatorSetting.animator.parameters = new List<NetworkAnimatorParameterSetting>();
            for (int j = 0; j < parameterCount; j++)
            {
                AnimatorControllerParameter par = parameters[j];
                var networkAnimatorParameterSetting = new NetworkAnimatorParameterSetting();
                networkAnimatorParameterSetting.index = j;
                if (par.type == UnityEngine.AnimatorControllerParameterType.Int)
                {
                    networkAnimatorParameterSetting.type = AnimatorControllerParameterType.Int;
                    int newIntValue = animator.GetInteger(par.nameHash);
                    networkAnimatorParameterSetting.value = BitConverter.GetBytes(newIntValue);
                }
                else if (par.type == UnityEngine.AnimatorControllerParameterType.Float)
                {
                    networkAnimatorParameterSetting.type = AnimatorControllerParameterType.Float;
                    float newFloatValue = animator.GetFloat(par.nameHash);
                    networkAnimatorParameterSetting.value = BitConverter.GetBytes(newFloatValue);
                }
                else if (par.type == UnityEngine.AnimatorControllerParameterType.Bool)
                {
                    networkAnimatorParameterSetting.type = AnimatorControllerParameterType.Bool;
                    bool newBoolValue = animator.GetBool(par.nameHash);
                    networkAnimatorParameterSetting.value = BitConverter.GetBytes(newBoolValue);
                }else if (par.type == UnityEngine.AnimatorControllerParameterType.Trigger)
                {
                    networkAnimatorParameterSetting.type = AnimatorControllerParameterType.Trigger;
                }
                
                networkAnimatorSetting.animator.parameters.Add(networkAnimatorParameterSetting);
            }

            return networkAnimatorSetting;
        }
    }
}