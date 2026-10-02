using System;
using System.Threading;

namespace Aimmy2.AILogic.Behavioral
{
    /// <summary>
    /// Injecte un bruit de capteur corrélé sur les déplacements IA,
    /// selon les piliers 1 et 2 de la stratégie anti-EAC (section 7.6).
    /// </summary>
    public class SensorNoiseInjector
    {
        private readonly Random _rng = new();
        private readonly object _lock = new();

        private double _horizontalStdDev = 1.5;
        private double _verticalStdDev = 1.2;
        private double _correlationFactor = 0.6;
        private double _magnitudeScale = 1.0;
        private double _noiseProbability = 0.35;

        public void Configure(double horizontalStdDev, double verticalStdDev,
            double correlationFactor, double magnitudeScale, double noiseProbability)
        {
            lock (_lock)
            {
                _horizontalStdDev = Math.Max(0.1, horizontalStdDev);
                _verticalStdDev = Math.Max(0.1, verticalStdDev);
                _correlationFactor = Math.Clamp(correlationFactor, 0.0, 1.0);
                _magnitudeScale = Math.Max(0.1, magnitudeScale);
                _noiseProbability = Math.Clamp(noiseProbability, 0.0, 1.0);
            }
        }

        public (int X, int Y) Apply(int targetX, int targetY, int imageSize = 640)
        {
            lock (_lock)
            {
                if (_rng.NextDouble() > _noiseProbability)
                    return (targetX, targetY);

                double hStd = _horizontalStdDev * _magnitudeScale;
                double vStd = _verticalStdDev * _magnitudeScale;

                double nx = ApplyGaussian(_rng) * hStd;
                double ny = ApplyGaussian(_rng) * vStd;

                nx += _correlationFactor * targetX / (imageSize * 0.5);
                ny += _correlationFactor * targetY / (imageSize * 0.5);

                int noisyX = (int)Math.Round(targetX + nx);
                int noisyY = (int)Math.Round(targetY + ny);

                return (noisyX, noisyY);
            }
        }

        private double ApplyGaussian(Random rng)
        {
            double u1 = 1.0 - rng.NextDouble();
            double u2 = rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }
}
