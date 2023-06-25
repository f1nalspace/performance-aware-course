using System;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Final.PerformanceAwareCourse
{
    internal class Program
    {
        readonly struct HaversinePair
        {
            [JsonPropertyName("x0")]
            public double X0 { get; }
            [JsonPropertyName("y0")]
            public double Y0 { get; }
            [JsonPropertyName("x1")]
            public double X1 { get; }
            [JsonPropertyName("y1")]
            public double Y1 { get; }

            public HaversinePair(double x0, double y0, double x1, double y1) : this()
            {
                X0 = x0;
                Y0 = y0;
                X1 = x1;
                Y1 = y1;
            }

            public override string ToString() => FormattableString.Invariant($"({X0}, {Y0}) to ({X1}, {Y1})");
        }

        class HaversineSamples
        {
            [JsonPropertyName("pairs")]
            public HaversinePair[] Pairs { get; set; }

            [JsonPropertyName("distances")]
            public double[] Distances { get; set; }

            [JsonPropertyName("avg")]
            public double Avg { get; set; }
        }

        const double Deg2RadFactor = Math.PI / 180.0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static double RadiansFromDegrees(double degrees) => degrees * Deg2RadFactor;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static double Square(double value) => value * value;

        static double HaversineDistance(double x0, double y0, double x1, double y1, double radius)
        {
            double lat1 = y0;
            double lat2 = y1;
            double lon1 = x0;
            double lon2 = x1;

            double dLat = RadiansFromDegrees(lat2 - lat1);
            double dLon = RadiansFromDegrees(lon2 - lon1);
            lat1 = RadiansFromDegrees(lat1);
            lat2 = RadiansFromDegrees(lat2);

            double a = Square(Math.Sin(dLat / 2.0)) + Square(Math.Sin(dLon / 2.0)) * Math.Cos(lat1) * Math.Cos(lat2);
            double c = 2.0 * Math.Asin(Math.Sqrt(a));

            double result = radius * c;

            return result;
        }

        static double HaversineDistance(HaversinePair pair, double radius) => HaversineDistance(pair.X0, pair.Y0, pair.X1, pair.Y1, radius);

        static readonly (HaversinePair Pair, double Result) TestData = (new HaversinePair(51.5007, 0.1246, 40.6892, 74.0445), 8254.1781969571875);

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
        const double EarthRadius = 6372.8;

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

            // Test distance computation
            {
                double r = HaversineDistance(TestData.Pair, EarthRadius);
                Contract.Assert(Math.Abs(r - TestData.Result) < double.Epsilon);
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

            string pairsFilePath = Path.Combine(outputDirectory, "pairs.json");

            double sum = 0.0;
            double sumCoef = 1.0 / (double)pairCount;

            using (FileStream stream = File.Create(pairsFilePath))
            {
                using (StreamWriter writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true))
                {
                    writer.WriteLine("{");
                    writer.WriteLine("\"pairs\": [");

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

                        double haversineDistance = HaversineDistance(x0, y0, x1, y1, EarthRadius);

                        string separator = pairIndex < pairCount - 1 ? "," : "";

                        writer.WriteLine(FormattableString.Invariant($"{{\"x0\": {x0:F16}, \"y0\": {y0:F16}, \"x1\": {x1:F16}, \"y1\": {y1:F16}}}{separator}"));

                        sum += haversineDistance * sumCoef;
                    }

                    writer.WriteLine("],");

                    writer.WriteLine(FormattableString.Invariant($"\"sum\": {sum:F16},"));

                    writer.WriteLine(FormattableString.Invariant($"\"count\": {pairCount}"));

                    writer.WriteLine("}");
                }
            }

            Console.WriteLine($"Done");
            Console.WriteLine($"Output-Path: {outputDirectory}");
            Console.WriteLine($"Method: {method}");
            Console.WriteLine($"Seed: {seed} ({seedText})");
            Console.WriteLine(FormattableString.Invariant($"Expected Sum: {sum:F16}"));

            return 0;
        }
    }
}