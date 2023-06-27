using System;
using System.Runtime.CompilerServices;

namespace Final.PerformanceAwareCourse
{
    public static class HaversineMath
    {
        public const double EarthRadius = 6372.8;

        const double Deg2RadFactor = Math.PI / 180.0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static double RadiansFromDegrees(double degrees) => degrees * Deg2RadFactor;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static double Square(double value) => value * value;

        public static double HaversineDistance(double x0, double y0, double x1, double y1, double radius = EarthRadius)
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
    }
}