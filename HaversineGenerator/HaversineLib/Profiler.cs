using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;

namespace Final.PerformanceAwareCourse
{
    static class Rdtsc
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
        public static ulong Get() => Timestamp();
    }

    public enum RecordType : int
    {
        None = 0,
        /// <summary>
        /// The first record that is recorded by the profiler
        /// </summary>
        ProfilerStart,
        /// <summary>
        /// The last record that is recorded by the profiler
        /// </summary>
        ProfilerEnd,
        /// <summary>
        /// Starts a section (code block)
        /// </summary>
        SectionBegin,
        /// <summary>
        /// Ends a section (code block)
        /// </summary>
        SectionEnd,
    }

    /// <summary>
    /// Defines a location in a "tracked" section inside a code block.
    /// </summary>
    public readonly struct ProfileLocation
    {
        /// <summary>
        /// Gets the optional section name.
        /// </summary>
        public string SectionName { get; }
        /// <summary>
        /// Gets the name of the function/method.
        /// </summary>
        public string FunctionName { get; }
        /// <summary>
        /// Gets the full file path of the source file.
        /// </summary>
        public string FilePath { get; }
        /// <summary>
        /// Gets the line number of source file.
        /// </summary>
        public int LineNumber { get; }
        /// <summary>
        /// Padding field
        /// </summary>
        public int Unused { get; }

        /// <summary>
        /// Gets an identifier from all relevant fields.
        /// </summary>
        public string Id
        {
            get
            {
                StringBuilder s = new StringBuilder();
                s.Append(FilePath);
                s.Append('|');
                s.Append(LineNumber);
                s.Append('|');
                s.Append(FunctionName);
                if (!string.IsNullOrEmpty(SectionName))
                {
                    s.Append('|');
                    s.Append(SectionName);
                }
                return string.Intern(s.ToString());
            }
        }

        public ProfileLocation(string sectionName, string functionName, string filePath, int lineNumber)
        {
            SectionName = sectionName;
            FunctionName = functionName;
            FilePath = filePath;
            LineNumber = lineNumber;
            Unused = 0;
        }

        public ProfileLocation TrimPath(string trimLeft)
        {
            string newFilePath;
            if (!string.IsNullOrWhiteSpace(FilePath) && !string.IsNullOrWhiteSpace(trimLeft) && FilePath.StartsWith(trimLeft, StringComparison.InvariantCultureIgnoreCase))
                newFilePath = FilePath.Substring(trimLeft.Length);
            else
                newFilePath = FilePath;
            return new ProfileLocation(SectionName, FunctionName, newFilePath, LineNumber);
        }

        public override string ToString() => Id;
    }

    /// <summary>
    /// Defines one record that stores just the record type, the raw RDTSC value, the thread id and a location.
    /// </summary>
    public readonly struct ProfileRecord
    {
        /// <summary>
        /// Gets the location.
        /// </summary>
        public ProfileLocation Location { get; }
        /// <summary>
        /// Gets the raw RDTSC value.
        /// </summary>
        public ulong Cycles { get; }
        /// <summary>
        /// Gets the record type.
        /// </summary>
        public RecordType Type { get; }
        /// <summary>
        /// Gets the thread id.
        /// </summary>
        public int ThreadId { get; }
        /// <summary>
        /// First padding field.
        /// </summary>
        public ulong Unused0 { get; }
        /// <summary>
        /// Second padding field.
        /// </summary>
        public ulong Unused1 { get; }

        public ProfileRecord(RecordType type, ulong cycles, int threadId, ProfileLocation location)
        {
            Type = type;
            Cycles = cycles;
            ThreadId = threadId;
            Location = location;
            Unused0 = Unused1 = 0;
        }

        public override string ToString() => $"Type: {Type}, Loc: {Location}, Thread: {ThreadId}, Cycles: {Cycles}";
    }

    /// <summary>
    /// Represents one node inside a tree, that contains the children and its current delta cycles.
    /// </summary>
    public class ProfileNode : IEquatable<ProfileNode>
    {
        /// <summary>
        /// Gets the parent node.
        /// </summary>
        public ProfileNode Parent { get; }
        /// <summary>
        /// Gets the location.
        /// </summary>
        public ProfileLocation Location { get; }
        /// <summary>
        /// Gets the location id (see: <see cref="ProfileLocation.Id"/>).
        /// </summary>
        public string Id { get; }
        /// <summary>
        /// Gets the current time spent in this section for all calls.
        /// </summary>
        public TimeSpan Time { get; private set; }
        /// <summary>
        /// Gets the total number of cycles for all calls.
        /// </summary>
        public ulong TotalCycles => _cycles;
        /// <summary>
        /// Gets the average number of cycles for one call.
        /// </summary>
        public double AvgCycles => CallCount > 0 ? _cycles / (double)CallCount : 0;
        /// <summary>
        /// Gets the number of calls.
        /// </summary>
        public ulong CallCount { get; private set; }
        /// <summary>
        /// Gets the thread id.
        /// </summary>
        public int ThreadId { get; }

        /// <summary>
        /// Gets the percentage in range of 0.0 to 100.0, without clamping it.
        /// </summary>
        public double Percentage => _percentage * 100.0;

        /// <summary>
        /// Gets the first child node in the linked list.
        /// </summary>
        public LinkedListNode<ProfileNode> FirstChild => _children.First;

        private ulong _cycles;
        private double _percentage;

        private readonly LinkedList<ProfileNode> _children;

        internal ProfileNode(ProfileNode parent, ProfileLocation location, string id, int threadId)
        {
            _children = new LinkedList<ProfileNode>();
            _cycles = 0;
            _percentage = 0;
            Parent = parent;
            Location = location;
            Id = id;
            Time = TimeSpan.Zero;
            CallCount = 0;
            ThreadId = threadId;
        }

        /// <summary>
        /// Add the specified <paramref name="deltaCycles"/> to this <see cref="ProfileNode"/>, increment the <see cref="CallCount"/> and update the <see cref="Time"/>.
        /// </summary>
        /// <param name="deltaCycles">The number of CPU cycles.</param>
        /// <param name="cpuFreq">The CPU frequency, that is used to compute the <see cref="Time"/>.</param>
        public void AddCall(ulong deltaCycles, ulong cpuFreq)
        {
            _cycles += deltaCycles;

            ++CallCount;

            double secs = _cycles / (double)cpuFreq;
            Time = TimeSpan.FromSeconds(secs);
        }

        /// <summary>
        /// Adds the specified <paramref name="child"/>.
        /// </summary>
        /// <param name="child">The child node.</param>
        public void AddChild(ProfileNode child)
        {
            _children.AddLast(child);
        }

        /// <summary>
        /// Computes the percentage from the specified <paramref name="totalCycles"/>.
        /// </summary>
        /// <param name="totalCycles">The total number of cycles from the profiler run.</param>
        public void Finalize(ulong totalCycles)
        {
            _percentage = TotalCycles / (double)totalCycles;
        }

        public bool Equals(ProfileNode other) => other != null && string.Equals(Id, other.Id);
        public override bool Equals(object obj) => obj is ProfileNode node && Equals(node);
        public override int GetHashCode() => Id.GetHashCode();

        public override string ToString() => FormattableString.Invariant($"{Id} => {CallCount} calls, {TotalCycles:n0} total cycles, {AvgCycles:n} avg cycles, {Time.TotalMilliseconds:F5} ms [{Percentage:F2} %]");
    }

    public class ProfilerResult
    {
        public ProfileNode Root { get; }
        public ImmutableArray<ProfileNode> List { get; }

        public ProfilerResult(ProfileNode root, ImmutableArray<ProfileNode> list)
        {
            Root = root;
            List = list;
        }

        private static void Print(int ident, ProfileNode node)
        {
            string identString = new string(' ', ident * 2);
            Console.WriteLine($"{identString}{node}");
            PrintChildren(ident + 1, node.FirstChild);
        }

        private static void PrintChildren(int ident, LinkedListNode<ProfileNode> first)
        {
            LinkedListNode<ProfileNode> n = first;
            while (n is not null)
            {
                Print(ident, n.Value);
                n = n.Next;
            }
        }

        public void PrintTree() => Print(0, Root);

        public void PrintList()
        {
            foreach (ProfileNode node in List)
                Console.WriteLine(node.ToString());
        }
    }

    /// <summary>
    /// Represents a simple struct that calls <see cref="Profiler.Begin(ProfileLocation)"/> at construction and <see cref="Profiler.End(ProfileLocation)"/> when it gets destructed.
    /// </summary>
    public readonly struct ProfileSection : IDisposable
    {
        private readonly Profiler _profiler;
        private readonly ProfileLocation _location;

        public ProfileSection(Profiler profiler, ProfileLocation location)
        {
            _profiler = profiler;
            _location = location;
            _profiler.Begin(_location);
        }

        public void Dispose() => _profiler.End(_location);
    }

    public class Profiler
    {
        private readonly ProfileRecord[] _records;
        private readonly ulong _cpuFreq;
        private readonly long _maxRecordCount;
        private long _recordIndex;
        private long _active;

        public Profiler(ulong maxRecordCount = 4096 * 1024)
        {
            _cpuFreq = Rdtsc.EstimateFrequency();
            _records = new ProfileRecord[maxRecordCount];
            _maxRecordCount = (long)maxRecordCount;
            _recordIndex = 0;
            _active = 0;
        }

        public void Start()
        {
            if (Interlocked.CompareExchange(ref _active, 1, 0) == 0)
                Push(RecordType.ProfilerStart, new ProfileLocation());
        }

        public ProfilerResult StopAndCollect(string pathTrim = null)
        {
            if (Interlocked.CompareExchange(ref _active, 0, 1) == 1)
            {
                Push(RecordType.ProfilerEnd, new ProfileLocation());

                Dictionary<string, ProfileNode> _nodeMap = new Dictionary<string, ProfileNode>();
                List<ProfileNode> nodes = new List<ProfileNode>();

                ProfileNode root = new ProfileNode(null, new ProfileLocation(), "ROOT", 0);
                nodes.Add(root);

                Stack<(ProfileNode, ulong)> nodeStack = new Stack<(ProfileNode, ulong)>();

                ulong recordCount = (ulong)_recordIndex;
                for (ulong recordIndex = 0; recordIndex < recordCount; ++recordIndex)
                {
                    ProfileRecord record = _records[recordIndex];

                    // Replace record by trimming the location file path if needed
                    if (!string.IsNullOrWhiteSpace(pathTrim))
                        record = new ProfileRecord(record.Type, record.Cycles, record.ThreadId, record.Location.TrimPath(pathTrim));

                    string id = record.Location.Id;

                    bool isFinished = false;
                    switch (record.Type)
                    {
                        case RecordType.ProfilerStart:
                        {
                            // Simply push the root and its cycles into to the stack
                            nodeStack.Push((root, record.Cycles));
                        }
                        break;

                        case RecordType.ProfilerEnd:
                        {
                            // Remove the root node from the stack
                            (ProfileNode, ulong) cur = nodeStack.Pop();
                            Debug.Assert(ReferenceEquals(cur.Item1, root));
                            Debug.Assert(nodeStack.Count == 0);

                            // Get delta cycles
                            Debug.Assert(record.Cycles >= cur.Item2);
                            ulong delta = record.Cycles - cur.Item2;

                            // Add the delta cycles to the root
                            root.AddCall(delta, _cpuFreq);

                            // Do not process and records anymore
                            isFinished = true;

                        }
                        break;

                        case RecordType.SectionBegin:
                        {
                            // We need at least the root node to put the node into
                            if (!nodeStack.TryPeek(out (ProfileNode, ulong) cur))
                                throw new InvalidOperationException($"Node stack empty!");

                            ProfileNode topNode = cur.Item1;

                            // Get or add section node to the top node
                            if (!_nodeMap.TryGetValue(id, out ProfileNode node))
                            {
                                node = new ProfileNode(topNode, record.Location, id, record.ThreadId);
                                _nodeMap.Add(id, node);
                                nodes.Add(node);
                                topNode.AddChild(node);
                            }

                            // Push the new or existing node to the stack
                            nodeStack.Push((node, record.Cycles));
                        }
                        break;

                        case RecordType.SectionEnd:
                        {
                            // Pop top node from the stack
                            if (!nodeStack.TryPop(out (ProfileNode, ulong) cur))
                                throw new InvalidOperationException($"Node stack empty!");

                            ProfileNode topNode = cur.Item1;

                            Debug.Assert(topNode.Id.Equals(id), $"Expect ID '{id}', but got '{topNode.Id}'");

                            // Get delta cycles
                            Debug.Assert(record.Cycles >= cur.Item2, $"Expect record cycles '{record.Cycles}' to be greater than the section cycles '{cur.Item2}'");
                            ulong delta = record.Cycles - cur.Item2;

                            // Add the delta cycles to the node
                            topNode.AddCall(delta, _cpuFreq);
                        }
                        break;

                        default:
                            throw new NotSupportedException($"The profile record type '{record.Type}' not supported");
                    }
                    if (isFinished)
                        break;
                }

                // Compute percentage of all nodes from the total cycles of the root
                ulong totalCycles = root.TotalCycles;
                foreach (ProfileNode node in nodes)
                {
                    Debug.Assert(node.TotalCycles <= totalCycles);
                    node.Finalize(totalCycles);
                }

                ProfilerResult result = new ProfilerResult(root, nodes.ToImmutableArray());

                return result;
            }
            return null;
        }

        private void Push(RecordType type, ProfileLocation location)
        {
            int threadId = Thread.CurrentThread.ManagedThreadId;

            long index = Interlocked.Increment(ref _recordIndex) - 1;
            Debug.Assert(index < _maxRecordCount);

            ulong cycles = Rdtsc.Get();
            _records[index] = new ProfileRecord(type, cycles, threadId, location);
        }

        public void Begin(string sectionName = null, [CallerMemberName] string functionName = null, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = 0)
        {
            if (_active == 0)
                return;
            Push(RecordType.SectionBegin, new ProfileLocation(sectionName, functionName, filePath, lineNumber));
        }
        public void Begin(out ProfileLocation location, string sectionName = null, [CallerMemberName] string functionName = null, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = 0)
        {
            location = new ProfileLocation(sectionName, functionName, filePath, lineNumber);
            if (_active == 0)
                return;
            Push(RecordType.SectionBegin, location);
        }
        internal void Begin(ProfileLocation location)
        {
            if (_active == 0)
                return;
            Push(RecordType.SectionBegin, location);
        }

        public void End(string sectionName = null, [CallerMemberName] string functionName = null, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = 0)
        {
            if (_active == 0)
                return;
            Push(RecordType.SectionEnd, new ProfileLocation(sectionName, functionName, filePath, lineNumber));
        }
        public void End(ProfileLocation location)
        {
            if (_active == 0)
                return;
            Push(RecordType.SectionEnd, location);
        }

        public IDisposable Section(string sectionName = null, [CallerMemberName] string functionName = null, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = 0)
        {
            if (_active == 0)
                return null;
            return new ProfileSection(this, new ProfileLocation(sectionName, functionName, filePath, lineNumber));
        }
    }
}
