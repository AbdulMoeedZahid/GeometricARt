using System;
using GeoART.Contracts;

namespace GeoART.Engine
{
    /// <summary>
    /// One Euro filter (Casiez, Roussel, Vogel, CHI 2012) for a 2D point: an exponential low-pass whose cutoff rises with
    /// the filtered speed, so a resting finger is steady (cutoff <see cref="MinCutoff"/>) and a fast drag lags little
    /// (cutoff MinCutoff + Beta * speed). Time-based, not frame-based: the same samples at the same timestamps give the
    /// same output at any frame rate. Deterministic (no randomness, no clock reads). Units: point units and seconds.
    /// </summary>
    public sealed class OneEuroFilter2D
    {
        /// <summary>Cutoff at rest, Hz. Lower = steadier but laggier when the finger stops.</summary>
        public double MinCutoff;
        /// <summary>Cutoff increase per unit/s of speed. Higher = less lag while moving.</summary>
        public double Beta;
        /// <summary>Cutoff of the speed estimate, Hz.</summary>
        public double DerivativeCutoff;

        bool _has;
        Vec2 _x, _dx;
        double _t;

        public OneEuroFilter2D(double minCutoff, double beta, double derivativeCutoff = 1.0)
        {
            MinCutoff = minCutoff;
            Beta = beta;
            DerivativeCutoff = derivativeCutoff;
        }

        public bool HasValue => _has;
        public Vec2 Value => _x;

        /// <summary>Forget the history: the next sample passes through unchanged.</summary>
        public void Reset() => _has = false;

        /// <summary>Adds a sample taken at time <paramref name="t"/> (seconds) and returns the filtered point.
        /// A sample at the same or an earlier time than the previous one is ignored (returns the current value).</summary>
        public Vec2 Filter(Vec2 p, double t)
        {
            if (!p.IsFinite) return _x;
            if (!_has)
            {
                _has = true;
                _x = p;
                _dx = Vec2.Zero;
                _t = t;
                return _x;
            }
            double dt = t - _t;
            if (!(dt > 1e-6)) return _x;
            _t = t;
            Vec2 speed = (p - _x) * (1.0 / dt);
            _dx = _dx + (speed - _dx) * Alpha(DerivativeCutoff, dt);
            double cutoff = MinCutoff + Beta * _dx.Length;
            _x = _x + (p - _x) * Alpha(cutoff, dt);
            return _x;
        }

        static double Alpha(double cutoffHz, double dt)
        {
            if (!(cutoffHz > 0)) return 1.0;
            double tau = 1.0 / (2.0 * Math.PI * cutoffHz);
            return 1.0 / (1.0 + tau / dt);
        }
    }
}
