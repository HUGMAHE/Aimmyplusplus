using System;
using System.Threading;

namespace Aimmy2.AILogic.Behavioral
{
    /// <summary>
    /// Profils de biais d'erreur adaptatifs selon le contexte de jeu,
    /// basés sur la distribution de tirs humains observables.
    /// </summary>
    public class AimBiasProfile
    {
        private readonly Random _rng = new();
        private readonly object _lock = new();

        private double _overshootProbability = 0.25;
        private double _maxOvershoot = 1.35;
        private double _microPauseProbability = 0.18;
        private int _microPauseMinMs = 18;
        private int _microPauseMaxMs = 55;
        private double _correctionJitter = 0.35;

        public void Configure(double overshootProbability, double maxOvershoot,
            double microPauseProbability, int microPauseMinMs, int microPauseMaxMs, double correctionJitter)
        {
            lock (_lock)
            {
                _overshootProbability = Math.Clamp(overshootProbability, 0.0, 1.0);
                _maxOvershoot = Math.Max(1.0, maxOvershoot);
                _microPauseProbability = Math.Clamp(microPauseProbability, 0.0, 1.0);
                _microPauseMinMs = Math.Max(1, microPauseMinMs);
                _microPauseMaxMs = Math.Max(_microPauseMinMs, microPauseMaxMs);
                _correctionJitter = Math.Max(0.0, correctionJitter);
            }
        }

        public (int X, int Y, bool MicroPause) Apply(int correctionX, int correctionY)
        {
            lock (_lock)
            {
                bool microPause = _rng.NextDouble() < _microPauseProbability;
                if (microPause)
                {
                    return (0, 0, true);
                }

                double overshoot = 1.0;
                if (_rng.NextDouble() < _overshootProbability)
                {
                    overshoot = 1.0 + _rng.NextDouble() * (_maxOvershoot - 1.0);
                }

                double jitterX = (_rng.NextDouble() - 0.5) * 2.0 * _correctionJitter;
                double jitterY = (_rng.NextDouble() - 0.5) * 2.0 * _correctionJitter;

                int finalX = (int)Math.Round(correctionX * overshoot + jitterX);
                int finalY = (int)Math.Round(correctionY * overshoot + jitterY);

                return (finalX, finalY, false);
            }
        }
    }
}
