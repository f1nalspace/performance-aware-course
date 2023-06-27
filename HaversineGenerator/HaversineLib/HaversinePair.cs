using System;

namespace Final.PerformanceAwareCourse
{
    public readonly struct HaversinePair
    {
        public double X0 { get; }
        public double Y0 { get; }
        public double X1 { get; }
        public double Y1 { get; }

        public HaversinePair(double x0, double y0, double x1, double y1)
        {
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
        }

        public override string ToString() => FormattableString.Invariant($"{X0:F16}, {Y0:F16}, {X1:F16}, {Y1:F16}");
    }
}
