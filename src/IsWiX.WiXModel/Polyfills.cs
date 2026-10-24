#if NETSTANDARD2_0
namespace System.Runtime.CompilerServices
{
    // The compiler recognizes this attribute by name; netstandard2.0 does not define it.
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute
    {
    }
}
#endif
