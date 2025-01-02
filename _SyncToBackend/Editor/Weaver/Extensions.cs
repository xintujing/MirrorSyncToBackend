using System;
using System.Linq;
using Mono.CecilX;

namespace _SyncToBackend.Editor.Weaver
{
    public static class Extensions
    {
        public static bool Is(this TypeReference td, Type type) =>
            type.IsGenericType
                ? td.GetElementType().FullName == type.FullName
                : td.FullName == type.FullName;

        // check if 'td' is exactly of type T.
        // it does not check if any base type is of <T>, only the specific type.
        // for example:
        //   NetworkConnection         Is NetworkConnection: true
        //   NetworkConnectionToClient Is NetworkConnection: false
        public static bool Is<T>(this TypeReference td) => Is(td, typeof(T));
        // check if 'tr' is derived from T.
        // it does not check if 'tr' is exactly T.
        // for example:
        //   NetworkConnection         IsDerivedFrom<NetworkConnection>: false
        //   NetworkConnectionToClient IsDerivedFrom<NetworkConnection>: true
        public static bool IsDerivedFrom<T>(this TypeReference tr) => IsDerivedFrom(tr, typeof(T));

        public static bool IsDerivedFrom(this TypeReference tr, Type baseClass)
        {
            TypeDefinition td = tr.Resolve();
            if (!td.IsClass)
                return false;

            // are ANY parent classes of baseClass?
            TypeReference parent = td.BaseType;

            if (parent == null)
                return false;

            if (parent.Is(baseClass))
                return true;

            if (parent.CanBeResolved())
                return IsDerivedFrom(parent.Resolve(), baseClass);

            return false;
        }
        
        
        // Given a method of a generic class such as ArraySegment`T.get_Count,
        // and a generic instance such as ArraySegment`int
        // Creates a reference to the specialized method  ArraySegment`int`.get_Count
        // Note that calling ArraySegment`T.get_Count directly gives an invalid IL error
        public static MethodReference MakeHostInstanceGeneric(this MethodReference self, ModuleDefinition module, GenericInstanceType instanceType)
        {
            MethodReference reference = new MethodReference(self.Name, self.ReturnType, instanceType)
            {
                CallingConvention = self.CallingConvention,
                HasThis = self.HasThis,
                ExplicitThis = self.ExplicitThis
            };

            foreach (ParameterDefinition parameter in self.Parameters)
                reference.Parameters.Add(new ParameterDefinition(parameter.ParameterType));

            foreach (GenericParameter generic_parameter in self.GenericParameters)
                reference.GenericParameters.Add(new GenericParameter(generic_parameter.Name, reference));

            return module.ImportReference(reference);
        }
        
        public static CustomAttribute GetCustomAttribute<TAttribute>(this ICustomAttributeProvider method)
        {
            return method.CustomAttributes.FirstOrDefault(ca => ca.AttributeType.Is<TAttribute>());
        }

        // Takes generic arguments from child class and applies them to parent reference, if possible
        // eg makes `Base<T>` in Child<int> : Base<int> have `int` instead of `T`
        // Originally by James-Frowen under MIT
        // https://github.com/MirageNet/Mirage/commit/cf91e1d54796866d2cf87f8e919bb5c681977e45
        public static TypeReference ApplyGenericParameters(this TypeReference parentReference,
            TypeReference childReference)
        {
            // If the parent is not generic, we got nothing to apply
            if (!parentReference.IsGenericInstance)
                return parentReference;

            GenericInstanceType parentGeneric = (GenericInstanceType)parentReference;
            // make new type so we can replace the args on it
            // resolve it so we have non-generic instance (eg just instance with <T> instead of <int>)
            // if we don't cecil will make it double generic (eg INVALID IL)
            GenericInstanceType generic = new GenericInstanceType(parentReference.Resolve());
            foreach (TypeReference arg in parentGeneric.GenericArguments)
                generic.GenericArguments.Add(arg);

            for (int i = 0; i < generic.GenericArguments.Count; i++)
            {
                // if arg is not generic
                // eg List<int> would be int so not generic.
                // But List<T> would be T so is generic
                if (!generic.GenericArguments[i].IsGenericParameter)
                    continue;

                // get the generic name, eg T
                string name = generic.GenericArguments[i].Name;
                // find what type T is, eg turn it into `int` if `List<int>`
                TypeReference arg = FindMatchingGenericArgument(childReference, name);

                // import just to be safe
                TypeReference imported = parentReference.Module.ImportReference(arg);
                // set arg on generic, parent ref will be Base<int> instead of just Base<T>
                generic.GenericArguments[i] = imported;
            }

            return generic;
        }
        
        // Finds the type reference for a generic parameter with the provided name in the child reference
        // Originally by James-Frowen under MIT
        // https://github.com/MirageNet/Mirage/commit/cf91e1d54796866d2cf87f8e919bb5c681977e45
        static TypeReference FindMatchingGenericArgument(TypeReference childReference, string paramName)
        {
            TypeDefinition def = childReference.Resolve();
            // child class must be generic if we are in this part of the code
            // eg Child<T> : Base<T>  <--- child must have generic if Base has T
            // vs Child : Base<int> <--- wont be here if Base has int (we check if T exists before calling this)
            if (!def.HasGenericParameters)
                throw new InvalidOperationException(
                    "Base class had generic parameters, but could not find them in child class");

            // go through parameters in child class, and find the generic that matches the name
            for (int i = 0; i < def.GenericParameters.Count; i++)
            {
                GenericParameter param = def.GenericParameters[i];
                if (param.Name == paramName)
                {
                    GenericInstanceType generic = (GenericInstanceType)childReference;
                    // return generic arg with same index
                    return generic.GenericArguments[i];
                }
            }

            // this should never happen, if it does it means that this code is bugged
            throw new InvalidOperationException("Did not find matching generic");
        }
        public static bool CanBeResolved(this TypeReference parent)
        {
            while (parent != null)
            {
                if (parent.Scope.Name == "Windows")
                {
                    return false;
                }

                if (parent.Scope.Name == "mscorlib")
                {
                    TypeDefinition resolved = parent.Resolve();
                    return resolved != null;
                }

                try
                {
                    parent = parent.Resolve().BaseType;
                }
                catch
                {
                    return false;
                }
            }
            return true;
        }
        public static bool ContainsClass(this ModuleDefinition module, string nameSpace, string className) =>
            module.GetTypes().Any(td => td.Namespace == nameSpace &&
                                  td.Name == className);
        
        public static bool HasCustomAttribute<TAttribute>(this ICustomAttributeProvider attributeProvider)
        {
            return attributeProvider.CustomAttributes.Any(attr => attr.AttributeType.Is<TAttribute>());
        }
        
        
        public static T GetField<T>(this CustomAttribute ca, string field, T defaultValue)
        {
            foreach (CustomAttributeNamedArgument customField in ca.Fields)
                if (customField.Name == field)
                    return (T)customField.Argument.Value;
            return defaultValue;
        }
        
        
        public static MethodDefinition GetMethod(this TypeDefinition td, string methodName)
        {
            return td.Methods.FirstOrDefault(method => method.Name == methodName);
        }
    }
}
