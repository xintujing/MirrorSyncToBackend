using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using _SyncToBackend.Editor.Weaver.EntryPointILPostProcessor;
using Mirror;
using Mono.CecilX;
using Mono.CecilX.Cil;
using Mono.CecilX.Metadata;
using Mono.CecilX.Rocks;
using UnityEngine;

namespace _SyncToBackend.Editor.Weaver
{
    public enum MethodType
    {
        Command = 1,
        TargetRpc = 2,
        ClientRpc = 3,
    }

    public struct MethodData
    {
        public string SubClass;
        public string Name;
        public bool RequiresAuthority;
        public MethodType Type;
        public Dictionary<string, string> Parameters;
        public List<string> RpcList;
        public Dictionary<byte, List<string>> SyncVars;
    }

    public struct SyncVarData
    {
        public string Fullname;
        public string SubClass;
        public string Name;
        public string Type;
        public int DirtyBit;
    }

    public struct BackendData
    {
        public List<SyncVarData> SyncVars;
        public List<MethodData> Methods;
    }

    public static class SyncToBackend
    {
        public static Logger log;

        private static BackendData _data = new BackendData()
        {
            SyncVars = new List<SyncVarData>(),
            Methods = new List<MethodData>(),
        };

        public const string Filepath = "_SyncToBackend/build.bin";

        private static readonly object _lock = new object();

        static SyncToBackend()
        {
        }

        public static void NetworkBehaviourSyncVars(TypeDefinition subClass, int dirtyBit,
            List<FieldDefinition> syncVars)
        {
            lock (_lock)
            {
                //data.dirtyBits[subClass.FullName] = dirtyBit;

                foreach (FieldDefinition field in syncVars)
                {
                    _data.SyncVars.Add(new SyncVarData()
                    {
                        Fullname = field.FullName,
                        SubClass = field.DeclaringType.FullName,
                        Name = field.Name,
                        Type = field.FieldType.FullName,
                        DirtyBit = dirtyBit,
                    });
                    dirtyBit += 1;
                }
            }
        }

        public static void NetworkBehaviourMethod(TypeDefinition netBehaviourSubclass)
        {
            lock (_lock)
            {
                // copy the list of methods because we will be adding methods in the loop
                List<MethodDefinition> methodList = new List<MethodDefinition>(netBehaviourSubclass.Methods);

                // find command and RPC functions
                foreach (MethodDefinition md in methodList)
                {
                    MethodType t = default;
                    bool requiresAuthority = true;
                    foreach (CustomAttribute ca in md.CustomAttributes)
                    {
                        if (ca.AttributeType.Is(typeof(CommandAttribute)))
                        {
                            t = MethodType.Command;
                            requiresAuthority = ca.GetField("requiresAuthority", true);
                        }
                        else if (ca.AttributeType.Is(typeof(TargetRpcAttribute)))
                        {
                            t = MethodType.TargetRpc;
                        }
                        else if (ca.AttributeType.Is(typeof(ClientRpcAttribute)))
                        {
                            t = MethodType.ClientRpc;
                        }
                    }

                    List<string> rpcList = new List<string> { };
                    Dictionary<byte, List<string>> commandSyncVars = new Dictionary<byte, List<string>> { };
                    if (t == MethodType.Command)
                    {
                        foreach (Instruction instruction in md.Body.Instructions)
                        {
                            // is this instruction a Call to a method?
                            // if yes, output the method so we can check it.
                            if (IsCallToMethod(instruction, out MethodDefinition calledMethod))
                            {
                                rpcList.Add(calledMethod.FullName);
                            }

                            if (IsReplaceObject(instruction, out string syncVar, out byte index))
                            {
                                if (commandSyncVars.ContainsKey(index))
                                {
                                    commandSyncVars[index].Add(syncVar);
                                }
                                else
                                {
                                    commandSyncVars.Add(index, new List<string>() { syncVar });
                                }
                            }
                        }
                    }

                    var parameters = new Dictionary<string, string> { };

                    for (int i = 0; i < md.Parameters.Count; i++)
                    {
                        parameters.Add(md.Parameters[i].Name, md.Parameters[i].ParameterType.FullName);
                    }

                    _data.Methods.Add(new MethodData
                    {
                        SubClass = netBehaviourSubclass.FullName,
                        Name = md.FullName,
                        RequiresAuthority = requiresAuthority,
                        Type = t,
                        Parameters = parameters,
                        RpcList = rpcList,
                        SyncVars = commandSyncVars,
                    });
                }
            }
        }

        public static void Export()
        {
            string folderPath = Path.GetDirectoryName(Filepath);
            if (folderPath != null && !Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }
            lock (_lock)
            {
                using (MemoryStream s = new MemoryStream())
                {
                    w(s, (ushort)1);
                    w(s, _data.SyncVars);

                    w(s, (ushort)2);
                    w(s, _data.Methods);

                    using (FileStream fileStream = new FileStream(Filepath, FileMode.Create, FileAccess.Write))
                    using (BinaryWriter writer = new BinaryWriter(fileStream))
                    {
                        writer.Write(s.ToArray());
                    }
                }
            }
        }

        private static void w(MemoryStream s, bool i)
        {
            w(s, (byte)(i ? 1 : 0));
        }

        private static void w(MemoryStream s, int i)
        {
            s.Write(BitConverter.GetBytes(i), 0, 4);
        }

        private static void w(MemoryStream s, ushort i)
        {
            s.Write(BitConverter.GetBytes(i), 0, 2);
        }

        private static void w(MemoryStream s, string i)
        {
            w(s, (ushort)i.Length);
            var b = Encoding.ASCII.GetBytes(i);
            s.Write(b, 0, b.Length);
        }

        private static void w(MemoryStream s, List<MethodData> i)
        {
            w(s, (ushort)i.Count);
            foreach (var md in i)
            {
                w(s, md.SubClass);
                w(s, md.Name);
                w(s, md.RequiresAuthority);
                w(s, (byte)md.Type);
                w(s, md.Parameters);
                if (md.Type == MethodType.Command)
                {
                    w(s, md.RpcList);
                    w(s, md.SyncVars);
                }
            }
        }

        private static void w(MemoryStream s, Dictionary<string, string> i)
        {
            w(s, (ushort)i.Count);
            foreach (KeyValuePair<string, string> kvp in i)
            {
                w(s, kvp.Key);
                w(s, kvp.Value);
            }
        }

        private static void w(MemoryStream s, Dictionary<byte, List<string>> i)
        {
            w(s, (ushort)i.Count);
            foreach (KeyValuePair<byte, List<string>> kvp in i)
            {
                w(s, kvp.Key);
                w(s, kvp.Value);
            }
        }

        private static void w(MemoryStream s, List<string> i)
        {
            w(s, (ushort)i.Count);
            foreach (var v in i)
            {
                w(s, v);
            }
        }

        private static void w(MemoryStream s, byte i)
        {
            s.WriteByte(i);
        }

        private static void w(MemoryStream s, byte[] i)
        {
            w(s, (ushort)i.Length);
            s.Write(i, 0, i.Length);
        }

        private static void w(MemoryStream s, List<SyncVarData> i)
        {
            w(s, (ushort)i.Count);
            foreach (var v in i)
            {
                w(s, v.Fullname);
                w(s, v.SubClass);
                w(s, v.Name);
                w(s, v.Type);
                w(s, v.DirtyBit);
            }
        }

        static bool IsCallToMethod(Instruction instruction, out MethodDefinition calledMethod)
        {
            if (instruction.OpCode == OpCodes.Call &&
                instruction.Operand is MethodDefinition method)
            {
                calledMethod = method;
                return true;
            }
            else
            {
                calledMethod = null;
                return false;
            }
        }

        static bool IsReplaceObject(Instruction instruction, out string fd, out byte index)
        {
            fd = "";
            index = 0;
            if (instruction.OpCode == OpCodes.Stfld && instruction.Operand is FieldDefinition field)
            {
                if (instruction.Previous.OpCode == OpCodes.Ldarg_0)
                {
                    index = 0;
                }
                else if (instruction.Previous.OpCode == OpCodes.Ldarg_1)
                {
                    index = 1;
                }
                else if (instruction.Previous.OpCode == OpCodes.Ldarg_2)
                {
                    index = 2;
                }
                else if (instruction.Previous.OpCode == OpCodes.Ldarg_3)
                {
                    index = 3;
                }
                else if (instruction.Previous.OpCode == OpCodes.Ldarg_S)
                {
                    if (instruction.Previous.Operand is ParameterDefinition pd)
                    {
                        index = (byte)(pd.Index + 1);
                    }
                    else
                    {
                        log.Error("IsReplaceObject unknown type");
                    }
                }
                else
                {
                    return false;
                }

                fd = field.FullName;

                return true;
            }

            return false;
        }

        public static void MonoCecilXAddThisType()
        {
            var assemblyPath = "Assets/Mirror/Plugins/Mono.Cecil/Mono.CecilX.dll";
            var assemblySyncToBackendPath = assemblyPath+".synctobackend";
            if (File.Exists(assemblySyncToBackendPath))
            {
                return;
            }
            try
            {
                ReaderParameters readerParameters = new ReaderParameters{
                    ReadWrite = true,
                    // ReadSymbols = true,
                    // custom reflection importer to fix System.Private.CoreLib
                    // not being found in custom assembly resolver above.
                    ReflectionImporterProvider = new ILPostProcessorReflectionImporterProvider()
                };
                var assemblyDefinition = AssemblyDefinition.ReadAssembly(assemblyPath, readerParameters);
                var targetTypeReference = assemblyDefinition.MainModule.GetType("Mono.CecilX.TypeReference");
                var genericParameterType = assemblyDefinition.MainModule.GetType("Mono.CecilX.GenericParameter");
                var moduleDefinitionType = assemblyDefinition.MainModule.GetType("Mono.CecilX.ModuleDefinition");
                var iMetadataScopeType = assemblyDefinition.MainModule.GetType("Mono.CecilX.IMetadataScope");
                // var elementTypeEnum = assemblyDefinition.MainModule.GetType("Mono.CecilX.Metadata.ElementType");
                // elementTypeEnum.IsPublic = true;
                // var targetTypeReference = assemblyDefinition.MainModule.ImportReference(
                //     assemblyDefinition.MainModule.GetType("Mono.CecilX.TypeReference"));

                var etype = targetTypeReference.Fields.First(m => m.Name == "etype");
                etype.Attributes = FieldAttributes.Public;
                // Type
                var thisType = new TypeDefinition(
                    targetTypeReference.Namespace,
                    "ThisType",
                    TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.BeforeFieldInit,
                    genericParameterType
                );
                
                // Method
                var thisMethod = new MethodDefinition(
                    ".ctor",
                    MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                    assemblyDefinition.MainModule.ImportReference(typeof(void))
                );
                var moduleParam = new ParameterDefinition("module", ParameterAttributes.None, moduleDefinitionType);
                var scopeParam = new ParameterDefinition("scope", ParameterAttributes.None, iMetadataScopeType);
                var eTypeParam = new ParameterDefinition("etype", ParameterAttributes.None, assemblyDefinition.MainModule.ImportReference(typeof(byte)));
                thisMethod.Parameters.Add(moduleParam);
                thisMethod.Parameters.Add(scopeParam);
                thisMethod.Parameters.Add(eTypeParam);
                
                var ilProcessor = thisMethod.Body.GetILProcessor();
                ilProcessor.Append(ilProcessor.Create(OpCodes.Ldarg_0));
                ilProcessor.Append(ilProcessor.Create(OpCodes.Ldnull));
                ilProcessor.Append(ilProcessor.Create(OpCodes.Ldstr, "!0"));
                ilProcessor.Append(ilProcessor.Create(OpCodes.Ldarg_1));
                ilProcessor.Append(ilProcessor.Create(OpCodes.Ldarg_2));
                var baseTypeReferenceMethod = targetTypeReference.Resolve().GetConstructors().First(
                    m => 
                        m.Parameters.Count == 4 &&
                        // m.Parameters[0].ParameterType == assemblyDefinition.MainModule.ImportReference(typeof(String)) &&
                        // m.Parameters[1].ParameterType == assemblyDefinition.MainModule.ImportReference(typeof(String)) &&
                        m.Parameters[2].ParameterType == moduleDefinitionType &&
                        m.Parameters[3].ParameterType == iMetadataScopeType
                );
                var baseTypeReference = assemblyDefinition.MainModule.ImportReference(baseTypeReferenceMethod);
                ilProcessor.Append(ilProcessor.Create(OpCodes.Call, baseTypeReference));
                ilProcessor.Append(ilProcessor.Create(OpCodes.Ldarg_0));
                ilProcessor.Append(ilProcessor.Create(OpCodes.Ldarg_3));
                var eTypeField = targetTypeReference.Fields.First(m => m.Name == "etype");
                ilProcessor.Append(ilProcessor.Create(OpCodes.Stfld, eTypeField));
                ilProcessor.Append(ilProcessor.Create(OpCodes.Ret)); 
                
                thisType.Methods.Add(thisMethod);
                assemblyDefinition.MainModule.Types.Add(thisType);
                WriterParameters writerParameters = new WriterParameters
                {
                    SymbolWriterProvider = new PortablePdbWriterProvider(),
                    WriteSymbols = true
                };
                assemblyDefinition.Write(assemblySyncToBackendPath, writerParameters);
            }
            catch (Exception e)
            {
                log.Warning($"Error loading assembly: {e.Message}");
            }
        }

        public static bool MonoBehaviourHook(ModuleDefinition moduleDefinition, TypeDefinition td, Logger log)
        {
            SyncToBackend.log = log;
            MonoCecilXAddThisType();
            if (td.FullName != typeof(NetworkBehaviour).FullName)
            {
                return false;
            }

            // Type
            var syncToBackendType = new TypeDefinition(
                td.Namespace,
                "MonoBehaviourSyncToBackend",
                TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.BeforeFieldInit,
                td.BaseType
            );

            // Default Method
            var syncToBackendMethod = new MethodDefinition(
                "SyncToBackend",
                MethodAttributes.Private | MethodAttributes.Static,
                moduleDefinition.ImportReference(typeof(void))
            );

            var tdParam = new ParameterDefinition("td", ParameterAttributes.None, syncToBackendType);
            syncToBackendMethod.Parameters.Add(tdParam);

            var ilProcessor = syncToBackendMethod.Body.GetILProcessor();
            var logMethod =
                moduleDefinition.ImportReference(
                    typeof(UnityEngine.Debug).GetMethod("LogError", new[] { typeof(string) }));
            var getTypeMethod = moduleDefinition.ImportReference(typeof(object).GetMethod("GetType"));
            var fullNameProperty = moduleDefinition.ImportReference(typeof(Type).GetProperty("FullName").GetMethod);
            var concatMethod = moduleDefinition.ImportReference(typeof(string).GetMethod("Concat",
                new[] { typeof(string), typeof(string) }));

            ilProcessor.Append(Instruction.Create(OpCodes.Ldstr, "[ERROR] SyncToBackendMonoBehaviour: "));
            ilProcessor.Append(Instruction.Create(OpCodes.Ldarg_0));
            ilProcessor.Append(Instruction.Create(OpCodes.Callvirt, getTypeMethod));
            ilProcessor.Append(Instruction.Create(OpCodes.Callvirt, fullNameProperty));
            ilProcessor.Append(Instruction.Create(OpCodes.Call, concatMethod));
            ilProcessor.Append(Instruction.Create(OpCodes.Call, logMethod));
            ilProcessor.Append(Instruction.Create(OpCodes.Ret));

            syncToBackendType.Methods.Add(syncToBackendMethod);

            var actionType = moduleDefinition.ImportReference(typeof(Action<MonoBehaviour>));

            // add field & method
            
            TypeReference nullTypeReferenct = new TypeReference(null, null, moduleDefinition, null);
            nullTypeReferenct.DeclaringType = moduleDefinition.ImportReference(typeof(Action<>));
            
            var nullTypeReferenctGenericParameters = nullTypeReferenct.GenericParameters;
            
            GenericParameter nullTypeReferenctgenericParameter = new GenericParameter(nullTypeReferenct);
            nullTypeReferenctGenericParameters.Insert(0, nullTypeReferenctgenericParameter);
            
            var self = nullTypeReferenctGenericParameters.First();

            
            var invokeMethodRef = new MethodReference(
                "Invoke",
                moduleDefinition.ImportReference(typeof(void)),
                new GenericInstanceType(
                    moduleDefinition.ImportReference(typeof(Action<>))
                )
                {
                    GenericArguments = { moduleDefinition.ImportReference(typeof(MonoBehaviour)) }
                }
            )
            {
                HasThis = true,
                // ExplicitThis = true,
                // CallingConvention = MethodCallingConvention.ThisCall,
                Parameters =
                {
                    new ParameterDefinition(string.Empty, ParameterAttributes.None, self)
                }
            };

            List<KeyValuePair<string, string>> methodList = new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string, string>("awakeMethod", "Awake"),
                new KeyValuePair<string, string>("startMethod", "Start"),
            };

            foreach (var item in methodList)
            {
                var methodField = new FieldDefinition(
                    item.Key,
                    FieldAttributes.Public | FieldAttributes.Static,
                    actionType
                );
                syncToBackendType.Fields.Add(methodField);

                var method = new MethodDefinition(
                    item.Value,
                    MethodAttributes.Public | MethodAttributes.HideBySig,
                    moduleDefinition.ImportReference(typeof(void))
                );
                ilProcessor = method.Body.GetILProcessor();
                ilProcessor.Append(ilProcessor.Create(OpCodes.Ldsfld, methodField));
                ilProcessor.Append(ilProcessor.Create(OpCodes.Ldarg_0));
                // var invoke = actionType.Resolve().GetMethods().First(m => m.Name == "Invoke");
                // var invokeRef = moduleDefinition.ImportReference(invoke);
                var instruction = ilProcessor.Create(OpCodes.Callvirt, invokeMethodRef);
                ilProcessor.Append(instruction);
                ilProcessor.Append(ilProcessor.Create(OpCodes.Ret));
                
                var mr = instruction.Operand as MethodReference;
                log.Warning(instruction.Operand.ToString());

                syncToBackendType.Methods.Add(method);
            }

            // Add default value

            var instanceConstructor = new MethodDefinition(
                ".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName |
                MethodAttributes.RTSpecialName,
                moduleDefinition.ImportReference(typeof(void))
            );

            var instanceIlProcessor = instanceConstructor.Body.GetILProcessor();
            instanceIlProcessor.Append(Instruction.Create(OpCodes.Ldarg_0));
            instanceIlProcessor.Append(Instruction.Create(OpCodes.Call, moduleDefinition.ImportReference(
                typeof(MonoBehaviour).GetConstructors().First())));
            instanceIlProcessor.Append(Instruction.Create(OpCodes.Ret));

            syncToBackendType.Methods.Add(instanceConstructor);

            var staticConstructor = new MethodDefinition(
                ".cctor",
                MethodAttributes.Private | MethodAttributes.HideBySig | MethodAttributes.SpecialName |
                MethodAttributes.RTSpecialName | MethodAttributes.Static,
                moduleDefinition.ImportReference(typeof(void))
            );

            var staticIlProcessor = staticConstructor.Body.GetILProcessor();
            var awakeFieldReference = new FieldReference("awakeMethod", actionType, syncToBackendType);
            var startFieldReference = new FieldReference("startMethod", actionType, syncToBackendType);
            var constructor = moduleDefinition.ImportReference(
                typeof(Action<MonoBehaviour>).GetConstructor(new[] { typeof(object), typeof(IntPtr) }));
            staticIlProcessor.Append(Instruction.Create(OpCodes.Ldnull));
            staticIlProcessor.Append(Instruction.Create(OpCodes.Ldftn, syncToBackendMethod));
            staticIlProcessor.Append(Instruction.Create(OpCodes.Newobj, constructor));
            staticIlProcessor.Append(Instruction.Create(OpCodes.Stsfld, awakeFieldReference));
            staticIlProcessor.Append(Instruction.Create(OpCodes.Ldnull));
            staticIlProcessor.Append(Instruction.Create(OpCodes.Ldftn, syncToBackendMethod));
            staticIlProcessor.Append(Instruction.Create(OpCodes.Newobj, constructor));
            staticIlProcessor.Append(Instruction.Create(OpCodes.Stsfld, startFieldReference));
            staticIlProcessor.Append(Instruction.Create(OpCodes.Ret));

            syncToBackendType.Methods.Add(staticConstructor);
            
            // Add class
            
            moduleDefinition.Types.Add(syncToBackendType);

            td.BaseType = syncToBackendType;

            return true;
        }
    }
}