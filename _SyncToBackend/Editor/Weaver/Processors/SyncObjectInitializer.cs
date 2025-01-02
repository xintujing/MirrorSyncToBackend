using Mirror;
using Mono.CecilX;

namespace _SyncToBackend.Editor.Weaver.Processors
{
    public static class SyncObjectInitializer
    {
        public static bool ImplementsSyncObject(TypeReference typeRef)
        {
            try
            {
                // value types cant inherit from SyncObject
                if (typeRef.IsValueType)
                {
                    return false;
                }

                return typeRef.Resolve().IsDerivedFrom<SyncObject>();
            }
            catch
            {
                // sometimes this will fail if we reference a weird library that can't be resolved, so we just swallow that exception and return false
            }

            return false;
        }
    }
}
