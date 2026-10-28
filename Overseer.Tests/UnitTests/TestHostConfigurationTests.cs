using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit;

namespace Overseer.Tests.UnitTests
{
    /// <summary>
    /// Guards the <c>RuntimeHostConfigurationOption</c> for <c>System.Runtime.EnableWriteXorExecute</c> in
    /// <c>Overseer.Tests.csproj</c>, which runs the test host with W^X off. See testing_guidelines § 7.
    /// </summary>
    public class TestHostConfigurationTests
    {
        private const string WriteXorExecuteProperty = "System.Runtime.EnableWriteXorExecute";
        private const uint MemPrivate = 0x20000;
        private const uint MemMapped = 0x40000;

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public ushort PartitionId;
            public UIntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        [DllImport("kernel32.dll")]
        private static extern UIntPtr VirtualQuery(IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, UIntPtr dwLength);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int JitCompiledProbe(int value) => value * 3 + 1;

        [Fact]
        public void TestHost_RunsWithWriteXorExecuteOff()
        {
            var value = AppContext.GetData(WriteXorExecuteProperty) as string;
            Assert.True(value == "false",
                $"{WriteXorExecuteProperty} is '{value ?? "(null)"}', expected 'false': the RuntimeHostConfigurationOption " +
                "in Overseer.Tests.csproj is missing or not reaching the test host.");
        }

        [Fact]
        public void TestHost_JitCodeLivesInPrivateMemory()
        {
            Assert.SkipUnless(OperatingSystem.IsWindows(), "VirtualQuery is Windows-only.");

            var handle = typeof(TestHostConfigurationTests)
                .GetMethod(nameof(JitCompiledProbe), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MethodHandle;
            RuntimeHelpers.PrepareMethod(handle);
            var address = handle.GetFunctionPointer();

            var size = VirtualQuery(address, out var info, (UIntPtr)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>());

            Assert.NotEqual(UIntPtr.Zero, size);
            Assert.True(info.Type == MemPrivate,
                $"JIT code at 0x{address.ToInt64():X} is in memory of type 0x{info.Type:X}, expected MEM_PRIVATE (0x{MemPrivate:X}). " +
                $"MEM_MAPPED (0x{MemMapped:X}) means W^X is on: the setting in Overseer.Tests.csproj is missing, " +
                "or DOTNET_EnableWriteXorExecute in the environment overrides it.");
        }
    }
}
