using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Chuvadi.Print.Tests
{
    /// <summary>
    /// D-217 item 2: every Windows entry point is a source-generated <see cref="LibraryImportAttribute"/> method, so the
    /// marshalling is built at compile time (trimming- and NativeAOT-safe). Runs on every OS: it only reads metadata.
    /// </summary>
    public class NativeInteropTests
    {
        private static readonly Type Native =
            typeof(Chuvadi.Print.Windows.WindowsPrinters).Assembly.GetType("Chuvadi.Print.Windows.NativeMethods", throwOnError: true)!;

        private static MethodInfo[] EntryPoints() => Native
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => !m.Name.StartsWith("<", StringComparison.Ordinal)) // the generator's own inner P/Invokes
            .ToArray();

        [Fact]
        public void EveryEntryPointIsSourceGenerated()
        {
            MethodInfo[] methods = EntryPoints();
            Assert.Equal(26, methods.Length);
            // A signature that needs no marshalling (GetDeviceCaps: integers in and out, no last error) is emitted by the
            // generator as a direct P/Invoke; that is still generated code and AOT-safe, so only the attribute is checked.
            foreach (MethodInfo m in methods)
                Assert.True(m.GetCustomAttribute<LibraryImportAttribute>() is not null, m.Name + " is a LibraryImport");
        }

        [Fact]
        public void LibrariesStringsAndErrorsAreAsBefore()
        {
            foreach (MethodInfo m in EntryPoints())
            {
                LibraryImportAttribute a = m.GetCustomAttribute<LibraryImportAttribute>()!;
                Assert.Contains(a.LibraryName, new[] { "winspool.drv", "gdi32.dll" });
                // GetDeviceCaps never set the last error; every other entry point does, as with DllImport.
                Assert.Equal(m.Name != "GetDeviceCaps", a.SetLastError);
                bool hasText = m.GetParameters().Any(p => p.ParameterType == typeof(string) || p.ParameterType == typeof(char[]));
                if (hasText) Assert.Equal(StringMarshalling.Utf16, a.StringMarshalling);
                if (m.ReturnType == typeof(bool))
                    Assert.Equal(UnmanagedType.Bool, m.ReturnParameter.GetCustomAttribute<MarshalAsAttribute>()!.Value);
            }
        }
    }
}
