using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace Final.PerformanceAwareCourse
{
    internal class Program
    {
        static double RandomRange(Random rnd, double min, double max)
        {
            double t = rnd.NextDouble();
            double result = (1.0 - t) * min + t * max;
            return result;
        }

        static double RandomDegree(Random rnd, double center, double radius, double range)
        {
            double min = Math.Max(-range, center - radius);
            double max = Math.Min(range, center + radius);
            double result = RandomRange(rnd, min, max);
            return result;
        }

        const string ClusterMethod = "cluster";
        const string UniformMethod = "uniform";

        static int Main(string[] args)
        {
            if (args.Length != 3)
            {
                string execPath = Path.GetFileName(Environment.ProcessPath);
                Console.Error.WriteLine($"Missing arguments: {execPath} [uniform/cluster] [seed] [number of pairs to generate]");
                return -1;
            }

            string method = args[0];
            string seedText = args[1];
            int seed = int.TryParse(args[1], out int parsedSeed) ? parsedSeed : seedText.GetHashCode();
            if (!ulong.TryParse(args[2], out ulong pairCount))
            {
                Console.Error.WriteLine($"Invalid count argument! Expect number, but got '{args[2]}'!");
                return -2;
            }

            Console.WriteLine($"Generating {pairCount} pairs with seed '{seedText}' ({seed})");

            Random rnd = new Random(seed);

            double xCenter = 0;
            double yCenter = 0;
            double xRange = 180;
            double yRange = 90;
            double xRadius = xRange;
            double yRadius = yRange;
            uint clustersPerPair = 64;
            ulong clusterCountLeft = ulong.MaxValue;

            ulong clusterCountMax = 1 + (pairCount / clustersPerPair);

            if (ClusterMethod.Equals(method, StringComparison.InvariantCultureIgnoreCase))
                clusterCountLeft = 0;
            else if (!UniformMethod.Equals(method, StringComparison.InvariantCultureIgnoreCase))
            {
                Console.Error.WriteLine($"Unsupported method '{method}'! Fallback to {UniformMethod} method.");
                method = UniformMethod;
            }

            string userDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            string outputDirectory = Path.Combine(userDirectory, "Downloads", "HaversineOutput");

            Directory.CreateDirectory(outputDirectory);

            string pairsFilePath = Path.Combine(outputDirectory, $"data_{pairCount}.json");

            string resultsFilePath = Path.Combine(outputDirectory, $"data_{pairCount}.results");

            double avg = 0.0;
            double sumCoef = 1.0 / (double)pairCount;

            using FileStream resultsStream = File.Create(resultsFilePath);
            using BinaryWriter resultsWriter = new BinaryWriter(resultsStream, Encoding.ASCII, leaveOpen: true);

            resultsWriter.Write(pairCount);

            using FileStream pairsStream = File.Create(pairsFilePath);
            using StreamWriter pairsWriter = new StreamWriter(pairsStream, Encoding.ASCII, leaveOpen: true);

            pairsWriter.WriteLine("{");
            pairsWriter.WriteLine("\"pairs\": [");

            for (ulong pairIndex = 0; pairIndex < pairCount; ++pairIndex)
            {
                // Change center and radius of cluster
                if (clusterCountLeft-- == 0)
                {
                    clusterCountLeft = clusterCountMax;
                    xCenter = RandomRange(rnd, -xRange, xRange);
                    yCenter = RandomRange(rnd, -yRange, yRange);
                    xRadius = RandomRange(rnd, 0, xRange);
                    yRadius = RandomRange(rnd, 0, yRange);
                }

                double x0 = RandomDegree(rnd, xCenter, xRadius, xRange);
                double y0 = RandomDegree(rnd, yCenter, yRadius, yRange);
                double x1 = RandomDegree(rnd, xCenter, xRadius, xRange);
                double y1 = RandomDegree(rnd, yCenter, yRadius, yRange);

                double haversineDistance = HaversineMath.HaversineDistance(x0, y0, x1, y1);

                string separator = pairIndex < pairCount - 1 ? "," : "";

                pairsWriter.WriteLine(FormattableString.Invariant($"{{\"x0\": {x0:F16}, \"y0\": {y0:F16}, \"x1\": {x1:F16}, \"y1\": {y1:F16}}}{separator}"));

                resultsWriter.Write(x0);
                resultsWriter.Write(y0);
                resultsWriter.Write(x1);
                resultsWriter.Write(y1);
                resultsWriter.Write(haversineDistance);

                avg += haversineDistance * sumCoef;
            }

            resultsWriter.Write(avg);

            pairsWriter.WriteLine("],");

            pairsWriter.WriteLine(FormattableString.Invariant($"\"avg\": {avg:F16},"));

            pairsWriter.WriteLine(FormattableString.Invariant($"\"count\": {pairCount}"));

            pairsWriter.WriteLine("}");

            resultsWriter.Flush();
            pairsWriter.Flush();

            Console.WriteLine($"Done");
            Console.WriteLine($"Output-Path: {outputDirectory}");
            Console.WriteLine($"Method: {method}");
            Console.WriteLine($"Seed: {seed} ({seedText})");
            Console.WriteLine(FormattableString.Invariant($"Expected Avg: {avg:F16}"));

            return 0;
        }
    }
}