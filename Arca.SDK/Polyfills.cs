#if NET48
// Polyfill para habilitar la palabra clave 'init' (C# 9) en .NET Framework 4.8.1
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif
