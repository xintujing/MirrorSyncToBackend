using System.Collections.Generic;
using Mirror;
using Mono.CecilX;

namespace _SyncToBackend.Editor.Weaver.Processors
{
    
    // Processes [SyncVar] attribute fields in NetworkBehaviour
    // not static, because ILPostProcessor is multithreaded
    public class SyncVarAttributeProcessor
    {
        // ulong = 64 bytes
        const int SyncVarLimit = 64;

        AssemblyDefinition assembly;
        SyncVarAccessLists syncVarAccessLists;
        Logger Log;

        public SyncVarAttributeProcessor(AssemblyDefinition assembly, SyncVarAccessLists syncVarAccessLists, Logger Log)
        {
            this.assembly = assembly;
            this.syncVarAccessLists = syncVarAccessLists;
            this.Log = Log;
        }

        public List<FieldDefinition> ProcessSyncVars(TypeDefinition td)
        {
            List<FieldDefinition> syncVars = new List<FieldDefinition>();
            
            // the mapping of dirtybits to sync-vars is implicit in the order of the fields here. this order is recorded in m_replacementProperties.
            // start assigning syncvars at the place the base class stopped, if any
            int dirtyBitCounter = syncVarAccessLists.GetSyncVarStart(td.BaseType.FullName);

            // find syncvars
            foreach (FieldDefinition fd in td.Fields)
            {
                if (fd.HasCustomAttribute<SyncVarAttribute>())
                {
                    if ((fd.Attributes & FieldAttributes.Static) != 0)
                    {
                        Log.Error($"{fd.Name} cannot be static", fd);
                        continue;
                    }

                    if (fd.FieldType.IsGenericParameter)
                    {
                        Log.Error($"{fd.Name} has generic type. Generic SyncVars are not supported", fd);
                        continue;
                    }

                    if (SyncObjectInitializer.ImplementsSyncObject(fd.FieldType))
                    {
                        Log.Warning($"{fd.Name} has [SyncVar] attribute. SyncLists should not be marked with SyncVar", fd);
                    }
                    else
                    {
                        syncVars.Add(fd);
                        
                        dirtyBitCounter += 1;

                        if (dirtyBitCounter > SyncVarLimit)
                        {
                            Log.Error($"{td.Name} has > {SyncVarLimit} SyncVars. Consider refactoring your class into multiple components", td);
                            continue;
                        }
                    }
                }
            }

            // include parent class syncvars
            // fixes: https://github.com/MirrorNetworking/Mirror/issues/3457
            int parentSyncVarCount = syncVarAccessLists.GetSyncVarStart(td.BaseType.FullName);
            syncVarAccessLists.SetNumSyncVars(td.FullName, parentSyncVarCount + syncVars.Count);

            return syncVars;
        }
    }
}
