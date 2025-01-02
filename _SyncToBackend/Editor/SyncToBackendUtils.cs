using System.Reflection;
using System.Text;
using Mirror;

namespace _SyncToBackend.Editor
{
    public class SyncToBackendUtils
    {
        public static byte[] GetInitialFieldValue(NetworkBehaviour comp, string fieldName)
        {
            FieldInfo field = comp.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                Log($"在类 {comp.GetType()} 中找不到字段: {fieldName}");
                return new byte[]{};
            }
            
            if (field.GetValue(comp) == null)
            {
                Log($"在类 {comp.GetType()} 中没有设置初始值: {fieldName}");
                return new byte[]{};
            }
            
            System.Type fieldType = field.GetValue(comp).GetType();
            
            System.Type classType = typeof(NetworkWriter);

            MethodInfo method = classType.GetMethod("Write");

            MethodInfo genericMethod = method.MakeGenericMethod(fieldType);

            NetworkWriter writer = new NetworkWriter();
            genericMethod.Invoke(writer,  new object[] { field.GetValue(comp) });
            
            return writer.ToArray();
        }
        
        public static string ByteArrayToString(byte[] ba)
        {
            StringBuilder hex = new StringBuilder(ba.Length * 2);
            foreach (byte b in ba)
                hex.AppendFormat("{0:x2}", b);
            return hex.ToString();
        }
        
        static void Log(string message)
        {
            UnityEngine.Debug.Log(message);
        }

        static void Warning(string message)
        {
            UnityEngine.Debug.LogWarning(message);
        }

        static void Error(string message)
        {
            UnityEngine.Debug.LogError(message);
        }
    }
}