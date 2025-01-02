using System.Collections.Generic;
using Mono.CecilX;
using Mono.CecilX.Cil;
using Logger = _SyncToBackend.Editor.Weaver.Logger;

namespace _SyncToBackend.Editor.Weaver.Processors
{
    public class NetworkBehaviourProcessor
    {
        AssemblyDefinition assembly;
        SyncVarAccessLists syncVarAccessLists;
        SyncVarAttributeProcessor syncVarAttributeProcessor;
        Logger Log;
        
        List<FieldDefinition> syncVars = new List<FieldDefinition>();
        List<FieldDefinition> syncObjects = new List<FieldDefinition>();
        
        readonly TypeDefinition netBehaviourSubclass;
        public NetworkBehaviourProcessor(AssemblyDefinition assembly, SyncVarAccessLists syncVarAccessLists, Logger Log, TypeDefinition td)
        {
            this.assembly = assembly;
            this.syncVarAccessLists = syncVarAccessLists;
            this.Log = Log;
            syncVarAttributeProcessor = new SyncVarAttributeProcessor(assembly, syncVarAccessLists, Log);
            netBehaviourSubclass = td;
        }
        public bool Process(ref bool WeavingFailed)
        {
            // only process once
            if (WasProcessed(netBehaviourSubclass))
            {
                return false;
            }
            
            MarkAsProcessed(netBehaviourSubclass);
            
            // deconstruct tuple and set fields
            syncVars = syncVarAttributeProcessor.ProcessSyncVars(netBehaviourSubclass);
            
            ProcessMethods(ref WeavingFailed);
            if (WeavingFailed)
            {
                // originally Process returned true in every case, except if already processed.
                // maybe return false here in the future.
                return true;
            }
            
            GenerateDeSerialization(ref WeavingFailed);
            if (WeavingFailed)
            {
                // originally Process returned true in every case, except if already processed.
                // maybe return false here in the future.
                return true;
            }

            return true;
        }

        void ProcessMethods(ref bool WeavingFailed)
        {
            SyncToBackend.NetworkBehaviourMethod(netBehaviourSubclass);
        }

        void GenerateDeSerialization(ref bool WeavingFailed)
        {
            const string DeserializeMethodName = "DeserializeSyncVars";
            if (netBehaviourSubclass.GetMethod(DeserializeMethodName) != null)
                return;
            
            // conditionally read each syncvar
            // start at number of syncvars in parent
            int dirtyBit = syncVarAccessLists.GetSyncVarStart(netBehaviourSubclass.BaseType.FullName);
            // foreach (FieldDefinition syncVar in syncVars)
            // {
            //     dirtyBit += 1;
            // }
            SyncToBackend.NetworkBehaviourSyncVars(netBehaviourSubclass, dirtyBit, syncVars);
        }
        
        public const string ProcessedFunctionName = "ToBackendWeaved";
        public static bool WasProcessed(TypeDefinition td)
        {
            return td.GetMethod(ProcessedFunctionName) != null;
        }
        public void MarkAsProcessed(TypeDefinition td)
        {
            if (!WasProcessed(td))
            {
                // add a function:
                //   public override bool MirrorProcessed() { return true; }
                // ReuseSlot means 'override'.
                MethodDefinition versionMethod = new MethodDefinition(
                    ProcessedFunctionName,
                    MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.ReuseSlot,
                    assembly.MainModule.ImportReference(typeof(bool)));
                ILProcessor worker = versionMethod.Body.GetILProcessor();
                worker.Emit(OpCodes.Ldc_I4_1);
                worker.Emit(OpCodes.Ret);
                td.Methods.Add(versionMethod);
            }
        }
    }
}