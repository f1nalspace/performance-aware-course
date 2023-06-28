using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Final.PerformanceAwareCourse
{
    public enum ProfileType : int
    {
        None = 0,
        Begin,
        End
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
        public const long MaxRecordCount = 4096 * 1024;

        private readonly ProfileRecord[] _records;
        private long _recordIndex;
        private ulong _cpuFreq;
        private long _active;

        public Profiler()
        {
            _records = new ProfileRecord[MaxRecordCount];
            _recordIndex = 0;
            _active = 1;
            _cpuFreq = Rdtsc.EstimateFrequency();
        }

        public void Start()
        {
            if (Interlocked.CompareExchange(ref _active, 1, 0) == 0)
            {

            }
        }

        public void StopAndCollect()
        {
            if (Interlocked.CompareExchange(ref _active, 0, 1) == 1)
            {

            }
        }

        private void Push(ProfileType type, ProfileLocation location)
        {
            int threadId = Thread.CurrentThread.ManagedThreadId;

            long index = Interlocked.Increment(ref _recordIndex) - 1;
            Debug.Assert(index < MaxRecordCount);

            ulong cycles = Rdtsc.Get();
            _records[index] = new ProfileRecord(type, cycles, threadId, location);
        }

        public void Begin(string sectionName = null, [CallerMemberName] string functionName = null, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = 0)
        {
            if (_active == 0) return;
            Push(ProfileType.Begin, new ProfileLocation(sectionName, functionName, filePath, lineNumber));
        }
        internal void Begin(ProfileLocation location)
        {
            if (_active == 0) return;
            Push(ProfileType.Begin, location);
        }

        public void End(string sectionName = null, [CallerMemberName] string functionName = null, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = 0)
        {
            if (_active == 0) return;
            Push(ProfileType.End, new ProfileLocation(sectionName, functionName, filePath, lineNumber));
        }
        internal void End(ProfileLocation location)
        {
            if (_active == 0) return;
            Push(ProfileType.End, location);
        }

        public IDisposable Section(string sectionName = null, [CallerMemberName] string functionName = null, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = 0)
        {
            if (_active == 0) return null;
            return new ProfileSection(this, new ProfileLocation(sectionName, functionName, filePath, lineNumber));
        }
    }
}
