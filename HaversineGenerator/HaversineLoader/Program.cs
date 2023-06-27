using System;
using System.IO;

namespace Final.PerformanceAwareCourse
{
    internal class Program
    {
        const int FileBufferSize = 4096 * 16;

        static int Main(string[] args)
        {
            ulong startupCyclesStart = Rdtsc.Read();

            if (args.Length < 1)
            {
                string execPath = Path.GetFileName(Environment.ProcessPath);
                Console.Error.WriteLine($"Usage: {execPath} [input json file]");
                return -1;
            }

            string inputJsonFilePath = args[0];
            if (!File.Exists(inputJsonFilePath))
            {
                Console.Error.WriteLine($"Input JSON file '{inputJsonFilePath}' does not exists!");
                return -1;
            }

            ulong startupCyclesEnd = Rdtsc.Read();

            ulong cpuFreq = Rdtsc.EstimateFrequency();

            ulong totalCyclesStart = Rdtsc.Read();

            ulong readCyclesStart = Rdtsc.Read();

            FileInfo inputJsonFile = new FileInfo(inputJsonFilePath);

            byte[] jsonData = new byte[inputJsonFile.Length];

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

            ReadOnlySpan<byte> data = jsonData.AsSpan();

            ulong readCyclesEnd = Rdtsc.Read();

            ulong parseCyclesStart = Rdtsc.Read();

            Result<JSONElement> parseRes = JSONParser.Parse(data);
            if (!parseRes.Success)
            {
                Console.Error.WriteLine(parseRes.Error.Message);
                return -1;
            }

            ulong parseCyclesEnd = Rdtsc.Read();

            ulong lookupCyclesStart = Rdtsc.Read();

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

            double expectedAvg = 0;
            JSONElement avgNode = root.FindByLabel("avg");
            if (avgNode is not null && avgNode.Kind == JSONElementKind.Number)
                expectedAvg = avgNode.NumberValue;

            long expectedCount = 0;
            JSONElement countNode = root.FindByLabel("count");
            if (countNode is not null && countNode.Kind == JSONElementKind.Number)
                expectedCount = (long)countNode.NumberValue;

            if (expectedCount != pairsNode.ChildCount)
            {
                Console.Error.WriteLine(FormattableString.Invariant($"Expect pair count of '{expectedCount}', but got '{pairsNode.ChildCount}'"));
                return -1;
            }

            int pairCount = pairsNode.ChildCount;

            HaversinePair[] pairs = new HaversinePair[pairCount];

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

            ulong lookupCyclesEnd = Rdtsc.Read();

            ulong sumCyclesStart = Rdtsc.Read();

            double avg = 0.0;
            double coeff = 1.0 / (double)pairs.Length;
            foreach (var pair in pairs)
            {
                double distance = HaversineMath.HaversineDistance(pair.X0, pair.Y0, pair.X1, pair.Y1);
                avg += distance * coeff;
            }

            ulong sumCyclesEnd = Rdtsc.Read();

            ulong totalCyclesEnd = Rdtsc.Read();

            ulong totalCyclesElapsed = totalCyclesEnd - totalCyclesStart;

            ulong startupCyclesElapsed = startupCyclesEnd - startupCyclesStart;
            ulong readCyclesElapsed = readCyclesEnd - readCyclesStart;
            ulong parseCyclesElapsed = parseCyclesEnd - parseCyclesStart;
            ulong lookupCyclesElapsed = lookupCyclesEnd - lookupCyclesStart;
            ulong sumCyclesElapsed = sumCyclesEnd - sumCyclesStart;

            Console.Error.WriteLine($"Input size: {inputJsonFile.Length}");
            Console.Error.WriteLine($"Pair count: {pairCount}");
            Console.Error.WriteLine($"Haversine sum: {avg:F16}");
            Console.Error.WriteLine();

            Console.WriteLine(FormattableString.Invariant($"Total time: {totalCyclesElapsed / (double)cpuFreq * 1000.0} ms (CPU Freq: {cpuFreq})"));
            Console.WriteLine(FormattableString.Invariant($"\tStartup: {startupCyclesElapsed} ({(startupCyclesElapsed / (double)totalCyclesElapsed) * 100.0:F2} %)"));
            Console.WriteLine(FormattableString.Invariant($"\tRead: {readCyclesElapsed} ({(readCyclesElapsed / (double)totalCyclesElapsed) * 100.0:F2} %)"));
            Console.WriteLine(FormattableString.Invariant($"\tParse: {parseCyclesElapsed} ({(parseCyclesElapsed / (double)totalCyclesElapsed) * 100.0:F2} %)"));
            Console.WriteLine(FormattableString.Invariant($"\tLookup: {lookupCyclesElapsed} ({(lookupCyclesElapsed / (double)totalCyclesElapsed) * 100.0:F2} %)"));
            Console.WriteLine(FormattableString.Invariant($"\tSum: {sumCyclesElapsed} ({(sumCyclesElapsed / (double)totalCyclesElapsed) * 100.0:F2} %)"));

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
    }
}