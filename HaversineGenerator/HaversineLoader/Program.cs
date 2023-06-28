using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Final.PerformanceAwareCourse
{
    internal class Program
    {
        const int FileBufferSize = 4096 * 256;

        static int Run(string[] args, [CallerFilePath] string classNameFilePath = null)
        {
            Profiler profiler = new Profiler();
            profiler.Start();

            profiler.Begin(out ProfileLocation totalLocation, "Total");

            string classNameFolderPath;
            string inputJsonFilePath;
            FileInfo inputJsonFile;
            using (var _ = profiler.Section("Get Arguments"))
            {
                classNameFolderPath = Path.GetDirectoryName(classNameFilePath) + Path.DirectorySeparatorChar;

                if (args.Length < 1)
                {
                    string execPath = Path.GetFileName(Environment.ProcessPath);
                    Console.Error.WriteLine($"Usage: {execPath} [input json file]");
                    return -1;
                }

                inputJsonFilePath = args[0];
                if (!File.Exists(inputJsonFilePath))
                {
                    Console.Error.WriteLine($"Input JSON file '{inputJsonFilePath}' does not exists!");
                    return -1;
                }

                inputJsonFile = new FileInfo(inputJsonFilePath);
            }

            byte[] jsonData;
            using (var _ = profiler.Section("Read File"))
            {
                jsonData = new byte[inputJsonFile.Length];

                int remainingBytes = (int)inputJsonFile.Length;
                int offset = 0;

                using (var stream = File.OpenRead(inputJsonFilePath))
                {
                    while (remainingBytes > 0)
                    {
                        int bytesToRead = Math.Min(remainingBytes, FileBufferSize);
                        int bytesRead = stream.Read(jsonData, offset, bytesToRead);
                        remainingBytes -= bytesRead;
                        offset += bytesRead;
                    }
                }
            }

            Result<JSONElement> parseRes;
            using (var _ = profiler.Section("Parse JSON"))
            {
                ReadOnlySpan<byte> data = jsonData.AsSpan();
                var parser = new JSONParser(profiler);
                parseRes = parser.Parse(data);
                if (!parseRes.Success)
                {
                    Console.Error.WriteLine(parseRes.Error.Message);
                    return -1;
                }
            }

            HaversinePair[] pairs;
            int pairCount;
            double expectedAvg = 0;
            long expectedCount = 0;
            using (var _ = profiler.Section("Lookup Haversine Pairs"))
            {
                JSONElement root = parseRes.Value;
                if (root.Kind != JSONElementKind.Object)
                {
                    Console.Error.WriteLine($"Expect JSON root kind to be object, but got '{root.Kind}'");
                    return -1;
                }

                JSONElement pairsNode = root.FindByLabel("pairs");
                if (pairsNode is null || pairsNode.Kind != JSONElementKind.Array)
                {
                    Console.Error.WriteLine($"No pairs node found!");
                    return -1;
                }

                JSONElement avgNode = root.FindByLabel("avg");
                if (avgNode is not null && avgNode.Kind == JSONElementKind.Number)
                    expectedAvg = avgNode.NumberValue;

                expectedCount = 0;
                JSONElement countNode = root.FindByLabel("count");
                if (countNode is not null && countNode.Kind == JSONElementKind.Number)
                    expectedCount = (long)countNode.NumberValue;

                pairCount = pairsNode.ChildCount;

                pairs = new HaversinePair[pairCount];

                long pairIndex = 0;
                foreach (JSONElement pairChild in pairsNode.Children)
                {
                    JSONElement x0 = pairChild.FindByLabel("x0");
                    JSONElement y0 = pairChild.FindByLabel("y0");
                    JSONElement x1 = pairChild.FindByLabel("x1");
                    JSONElement y1 = pairChild.FindByLabel("y1");
                    if (x0 is null || y0 is null || x1 is null || y1 is null)
                    {
                        Console.Error.WriteLine($"Pair by index '{pairIndex}' is missing properties at location '{pairChild.Location}'!");
                        return -1;
                    }
                    pairs[pairIndex] = new HaversinePair(x0.NumberValue, y0.NumberValue, x1.NumberValue, y1.NumberValue);
                    ++pairIndex;
                }
            }

            double avg;
            using (var _ = profiler.Section("Compute Haversine Avg"))
            {
                avg = 0.0;
                double coeff = 1.0 / (double)pairCount;
                foreach (HaversinePair pair in pairs)
                {
                    double distance = HaversineMath.HaversineDistance(pair.X0, pair.Y0, pair.X1, pair.Y1);
                    avg += distance * coeff;
                }
            }

            profiler.End(totalLocation);

            ProfilerResult profilerResult = profiler.StopAndCollect(classNameFolderPath);

            Console.WriteLine($"Input size: {inputJsonFile.Length}");
            Console.WriteLine($"Pair count: {pairCount}");
            Console.WriteLine($"Haversine sum: {avg:F16}");
            Console.WriteLine($"Total time: {profilerResult.Root.Time.TotalMilliseconds:F5} ms");
            Console.WriteLine();

            profilerResult.PrintTree();

            Console.WriteLine();

            profilerResult.PrintList();

            Console.WriteLine();

            if (expectedCount != pairCount)
            {
                Console.Error.WriteLine(FormattableString.Invariant($"Expect pair count of '{expectedCount}', but got '{pairCount}'"));
                return -1;
            }

            if (expectedAvg > 0)
            {
                double delta = Math.Abs(expectedAvg - avg);
                if (delta > 0.0001)
                {
                    Console.Error.WriteLine(FormattableString.Invariant($"Expect haversine avg of '{expectedAvg:F16}', but got '{avg:F16}'. Delta is '{delta:F16}'"));
                    return -1;
                }
            }

            return 0;
        }

        static int Main(string[] args)
        {
            int locationSize = Marshal.SizeOf<ProfileLocation>();
            int recordSize = Marshal.SizeOf<ProfileRecord>();
            Debug.Assert(locationSize == 32);
            Debug.Assert(recordSize == 64);
            return Run(args);
        }
    }
}