using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;

namespace Final.PerformanceAwareCourse
{
    public static class Rdtsc
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct SystemInfo
        {
            public ushort wProcessorArchitecture;
            public ushort wReserved;
            public uint dwPageSize;
            public IntPtr lpMinimumApplicationAddress;
            public IntPtr lpMaximumApplicationAddress;
            public IntPtr dwActiveProcessorMask;
            public uint dwNumberOfProcessors;
            public uint dwProcessorType;
            public uint dwAllocationGranularity;
            public ushort wProcessorLevel;
            public ushort wProcessorRevision;
        }

        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern void GetNativeSystemInfo(out SystemInfo lpSystemInfo);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr VirtualAlloc(IntPtr lpAddress, IntPtr dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool VirtualProtect(IntPtr lpAddress, IntPtr dwSize, uint flAllocationType, out uint lpflOldProtect);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool VirtualFree(IntPtr lpAddress, IntPtr dwSize, uint dwFreeType);

        private const uint PAGE_READWRITE = 0x04;
        private const uint PAGE_EXECUTE = 0x10;
        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RELEASE = 0x8000;

        [SuppressUnmanagedCodeSecurity]
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate ulong TimestampDelegate();

        private static readonly TimestampDelegate Timestamp;

        static Rdtsc()
        {
            SystemInfo systemInfo;
            GetNativeSystemInfo(out systemInfo);

            if (systemInfo.wProcessorArchitecture != 0 /* PROCESSOR_ARCHITECTURE_INTEL */ &&
                systemInfo.wProcessorArchitecture != 9 /* PROCESSOR_ARCHITECTURE_AMD64 */)
            {
                // Fallback for ARM/IA64/...
                Timestamp = StopwatchGetTimestamp;
                return;
            }

            byte[] body;

            if (Environment.Is64BitProcess)
            {
                body = new byte[]
                {
                    0x0f, 0x31, // rdtsc
                    0x48, 0xc1, 0xe2, 0x20, // shl rdx,20h 
                    0x48, 0x0b, 0xc2, // or rax,rdx 
                    0xc3, // ret
                };
            }
            else
            {
                body = new byte[]
                {
                    0x0f, 0x31, // rdtsc
                    0xc3, // ret
                };
            }

            IntPtr buf = IntPtr.Zero;

            try
            {
                // We VirtualAlloc body.Length bytes, with R/W access
                // Note that from what I've read, MEM_RESERVE is useless
                // if the first parameter is IntPtr.Zero
                buf = VirtualAlloc(IntPtr.Zero, (IntPtr)body.Length, MEM_COMMIT, PAGE_READWRITE);

                if (buf == IntPtr.Zero)
                {
                    throw new Win32Exception();
                }

                // Copy our instructions in the buf
                Marshal.Copy(body, 0, buf, body.Length);

                // Change the access of the allocated memory from R/W to Execute
                uint oldProtection;
                bool result = VirtualProtect(buf, (IntPtr)body.Length, PAGE_EXECUTE, out oldProtection);

                if (!result)
                {
                    throw new Win32Exception();
                }

                // Create a delegate to the "function"
                Timestamp = (TimestampDelegate)Marshal.GetDelegateForFunctionPointer(buf, typeof(TimestampDelegate));

                buf = IntPtr.Zero;
            }
            finally
            {
                // There was an error!
                if (buf != IntPtr.Zero)
                {
                    // Free the allocated memory
                    bool result = VirtualFree(buf, IntPtr.Zero, MEM_RELEASE);

                    if (!result)
                    {
                        throw new Win32Exception();
                    }
                }
            }
        }

        // Fallback if rdtsc isn't available
        private static ulong StopwatchGetTimestamp()
        {
            return unchecked((ulong)Stopwatch.GetTimestamp());
        }

        /// <summary>
        /// Estimates the number of CPU cycles per second, which resulting in return the base clock of the CPU.
        /// </summary>
        /// <param name="milliseconds">The number of milliseconds to wait (less is faster, but loses precision)</param>
        /// <returns>The number of CPU cycles per second.</returns>
        public static ulong EstimateFrequency(ulong milliseconds = 100)
        {
            ulong osFreq = (ulong)Stopwatch.Frequency;

            ulong cpuStart = Timestamp();
            ulong osStart = (ulong)Stopwatch.GetTimestamp();
            ulong osEnd = 0;
            ulong osElapsed = 0;
            ulong osWaitTime = osFreq * milliseconds / 1000;
            while (osElapsed < osWaitTime)
            {
                osEnd = (ulong)Stopwatch.GetTimestamp();
                osElapsed = osEnd - osStart;
            }

            ulong cpuEnd = Timestamp();
            ulong cpuElapsed = cpuEnd - cpuStart;
            ulong cpuFreq = osElapsed > 0 ? osFreq * cpuElapsed / osElapsed : cpuElapsed;

            return cpuFreq;
        }

        /// <summary>
        /// Returns the RDTSC value from the CPU.
        /// </summary>
        /// <returns>The resulting timestamp counter.</returns>
        public static ulong Read() => Timestamp();
    }
}
