using System;
using System.Diagnostics;
using System.Linq.Expressions;
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

    public readonly struct ProfileRecord
    {
        public string SectionName { get; }
        public string FilePath { get; }
        public string FunctionName { get; }
        public ulong Cycles { get; }
        public ProfileType Type { get; }
        public int LineNumber { get; }
        public int ThreadId { get; }

        public ProfileRecord(ProfileType type, string sectionName, ulong cycles, int threadId, string filePath, string functionName, int lineNumber)
        {
            Type = type;
            SectionName = sectionName;
            Cycles = cycles;
            FilePath = filePath;
            FunctionName = functionName;
            LineNumber = lineNumber;
            ThreadId = threadId;
        }
    }

    public readonly struct ProfileSection : IDisposable
    {
        private readonly Profiler _profiler;

        public string SectionName { get; }
        public string FilePath { get; }
        public string FunctionName { get; }
        public int LineNumber { get; }

        public ProfileSection(Profiler profiler, string sectionName, string filePath, string functionName, int lineNumber)
        {
            _profiler = profiler;
            SectionName = sectionName;
            FilePath = filePath;
            FunctionName = functionName;
            LineNumber = lineNumber;
            _profiler.Begin(SectionName, FilePath, FunctionName, LineNumber);
        }

        public void Dispose()
        {
            _profiler.End(SectionName, FilePath, FunctionName, LineNumber);
        }
    }

    public class Profiler
    {
        public const long MaxRecordCount = 4096 * 1024;

        private readonly ProfileRecord[] _records;
        private long _recordIndex;
        private ulong _cpuFreq;

        public Profiler()
        {
            _records = new ProfileRecord[MaxRecordCount];
            _recordIndex = 0;
            _cpuFreq = Rdtsc.EstimateFrequency();
        }

        private void Push(ProfileType type, string sectionName, string functionName, string filePath, int lineNumber)
        {
            int threadId = Thread.CurrentThread.ManagedThreadId;

            long index = Interlocked.Increment(ref _recordIndex) - 1;
            Debug.Assert(index < MaxRecordCount);

            ulong cycles = Rdtsc.Get();
            _records[index] = new ProfileRecord(type, sectionName, cycles, threadId, filePath, functionName, lineNumber);
        }

        internal void Begin(string sectionName = null, [CallerMemberName] string functionName = null, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = 0)
            => Push(ProfileType.Begin, sectionName, functionName, filePath, lineNumber);

        internal void End(string sectionName = null, [CallerMemberName] string functionName = null, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = 0)
            => Push(ProfileType.End, sectionName, functionName, filePath, lineNumber);

        public ProfileSection Section(string sectionName = null, [CallerMemberName] string functionName = null, [CallerFilePath] string filePath = null, [CallerLineNumber] int lineNumber = 0)
            => new ProfileSection(this, sectionName, functionName, filePath, lineNumber);
    }
}
