using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Final.PerformanceAwareCourse
{
    public enum ProfileType : int
    {
        None = 0,
        Start,
        End,
        SectionBegin,
        SectionEnd,
    }

    public readonly struct ProfileLocation
    {
        public string SectionName { get; }
        public string FunctionName { get; }
        public string FilePath { get; }
        public int LineNumber { get; }
        public int Unused { get; }

        public ProfileLocation(string sectionName, string functionName, string filePath, int lineNumber)
        {
            SectionName = sectionName;
            FunctionName = functionName;
            FilePath = filePath;
            LineNumber = lineNumber;
            Unused = 0;
        }
    }

    public readonly struct ProfileRecord
    {
        public ProfileLocation Location { get; }
        public ulong Cycles { get; }
        public ProfileType Type { get; }
        public int ThreadId { get; }
        public ulong Unused0 { get; }
        public ulong Unused1 { get; }

        public ProfileRecord(ProfileType type, ulong cycles, int threadId, ProfileLocation location)
        {
            Type = type;
            Cycles = cycles;
            ThreadId = threadId;
            Location = location;
            Unused0 = Unused1 = 0;
        }
    }

    public class ProfileResult
    {
        public ProfileLocation Location { get; }
        public Guid Id { get; }
        public TimeSpan DeltaTime { get; }
        public ulong DeltaCycles { get; }
        public int ThreadId { get; }

        public ProfileResult(Guid id, TimeSpan deltaTime, ulong deltaCycles, int threadId, ProfileLocation location)
        {
            Location = location;
            Id = id;
            DeltaTime = deltaTime;
            DeltaCycles = deltaCycles;
            ThreadId = threadId;
        }
    }

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

        public void Dispose()
        {
            _profiler.End(_location);
        }
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
            _active = 1;
        }

        public void Start()
        {
            if (Interlocked.CompareExchange(ref _active, 1, 0) == 0)
                Push(ProfileType.Start, new ProfileLocation());
        }

        public void StopAndCollect()
        {
            if (Interlocked.CompareExchange(ref _active, 0, 1) == 1)
                Push(ProfileType.End, new ProfileLocation());
        }

        private void Push(ProfileType type, ProfileLocation location)
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
            Push(ProfileType.SectionBegin, new ProfileLocation(sectionName, functionName, filePath, lineNumber));
        }
        internal void Begin(ProfileLocation location)
        {
            if (_active == 0) 
                return;
            Push(ProfileType.SectionBegin, location);
        }

        public void End(string sectionName = null, [CallerMemberName] string functionName = null, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = 0)
        {
            if (_active == 0) 
                return;
            Push(ProfileType.SectionEnd, new ProfileLocation(sectionName, functionName, filePath, lineNumber));
        }
        internal void End(ProfileLocation location)
        {
            if (_active == 0) 
                return;
            Push(ProfileType.SectionEnd, location);
        }

        public IDisposable Section(string sectionName = null, [CallerMemberName] string functionName = null, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = 0)
        {
            if (_active == 0) 
                return null;
            return new ProfileSection(this, new ProfileLocation(sectionName, functionName, filePath, lineNumber));
        }
    }
}
