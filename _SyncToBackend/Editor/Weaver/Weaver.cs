using System;
using System.Collections.Generic;
using _SyncToBackend.Editor.Weaver.Processors;
using Mirror;
using Mono.CecilX;
using Mono.CecilX.Rocks;

namespace _SyncToBackend.Editor.Weaver
{
    // not static, because ILPostProcessor is multithreaded
    internal class Weaver
    {
        // generated code class
        public const string GeneratedCodeNamespace = "MirrorToBackend";
        public const string GeneratedCodeClassName = "GeneratedNetworkCode";
        TypeDefinition GeneratedCodeClass;

        // for resolving Mirror.dll in ReaderWriterProcessor, we need to know
        // Mirror.dll name
        public const string MirrorAssemblyName = "MirrorToBackend";

        SyncVarAccessLists syncVarAccessLists;
        AssemblyDefinition CurrentAssembly;

        // in case of weaver errors, we don't stop immediately.
        // we log all errors and then eventually return false if
        // weaving has failed.
        // this way the user can fix multiple errors at once, instead of having
        // to fix -> recompile -> fix -> recompile for one error at a time.
        bool WeavingFailed;

        // logger functions can be set from the outside.
        // for example, Debug.Log or ILPostProcessor Diagnostics log for
        // multi threaded logging.
        public Logger Log;
        
        public Weaver(Logger Log)
        {
            this.Log = Log;
            SyncToBackend.log = Log;
        }
        
        public bool Weave(AssemblyDefinition assembly, IAssemblyResolver resolver, out bool modified)
        {
            WeavingFailed = false;
            modified = false;
            try
            {
                CurrentAssembly = assembly;

                // fix "No writer found for ..." error
                // https://github.com/vis2k/Mirror/issues/2579
                // -> when restarting Unity, weaver would try to weave a DLL
                //    again
                // -> resulting in two GeneratedNetworkCode classes (see ILSpy)
                // -> the second one wouldn't have all the writer types setup
                if (CurrentAssembly.MainModule.ContainsClass(GeneratedCodeNamespace, GeneratedCodeClassName))
                {
                    //Log.Warning($"Weaver: skipping {CurrentAssembly.Name} because already weaved");
                    return true;
                }

                // WeaverList depends on WeaverTypes setup because it uses Import
                syncVarAccessLists = new SyncVarAccessLists();
                
                ModuleDefinition moduleDefinition = CurrentAssembly.MainModule;
                Console.WriteLine($"Script Module: {moduleDefinition.Name}");

                modified |= WeaveModule(moduleDefinition);
                
                SyncToBackend.Export();

                return true;
            }
            catch (Exception e)
            {
                Log.Error($"Exception :{e}");
                WeavingFailed = true;
                return false;
            }
        }
        bool WeaveNetworkBehavior(TypeDefinition td)
        {
            if (!td.IsClass)
                return false;

            if (!td.IsDerivedFrom<NetworkBehaviour>())
            {
                return false;
            }

            // process this and base classes from parent to child order

            List<TypeDefinition> behaviourClasses = new List<TypeDefinition>();

            TypeDefinition parent = td;
            while (parent != null)
            {
                if (parent.Is<NetworkBehaviour>())
                {
                    break;
                }

                try
                {
                    behaviourClasses.Insert(0, parent);
                    parent = parent.BaseType.Resolve();
                }
                catch (AssemblyResolutionException)
                {
                    // this can happen for plugins.
                    //Console.WriteLine("AssemblyResolutionException: "+ ex.ToString());
                    break;
                }
            }
            
            bool modified = false;
            foreach (TypeDefinition behaviour in behaviourClasses)
            {
                modified |= new NetworkBehaviourProcessor(CurrentAssembly, syncVarAccessLists, Log, behaviour).Process(ref WeavingFailed);
            }
            return modified;
        }
        bool WeaveModule(ModuleDefinition moduleDefinition)
        {
            bool modified = false;
            TypeDefinition networkBehaviour = null;
            
            foreach (TypeDefinition td in moduleDefinition.GetAllTypes())
            {
                if (td.IsClass && td.FullName != null && (
                        td.Namespace.StartsWith("kcp2k") ||
                        td.Namespace.StartsWith("_SyncToBackend") ||
                        td.Namespace.StartsWith("System") ||
                        (td.Namespace.StartsWith("Mirror") && td.FullName != typeof(NetworkBehaviour).FullName) ||
                        td.Namespace.StartsWith("GeneratedReaderWriter") ||
                        td.Namespace.StartsWith("Weaver") ||
                        td.Namespace.StartsWith("StinkySteak") ||
                        td.Namespace.StartsWith("Microsoft") ||
                        td.Namespace.StartsWith("Edgegap") ||

                        (td.FullName.StartsWith("Mirror") && td.FullName != typeof(NetworkBehaviour).FullName) ||
                        td.FullName.StartsWith("Weaver") ||
                        td.FullName.StartsWith("Unity") ||
                        td.FullName.StartsWith("StinkySteak") ||
                        td.FullName.StartsWith("kcp2k") ||
                        td.FullName.StartsWith("Edgegap")
                        )
                    )
                {
                    continue;
                }
                if (td.IsClass && td.FullName == typeof(NetworkBehaviour).FullName)
                {
                    networkBehaviour = td;
                }
                
                if (td.IsClass && td.BaseType.CanBeResolved())
                {
                    modified |= WeaveNetworkBehavior(td);
                }
            }

            if (networkBehaviour != null)
            {
                modified |= SyncToBackend.MonoBehaviourHook(moduleDefinition, networkBehaviour, Log);
            }
            return modified;
        }
    }
}