using System;
using System.Threading;

namespace Aimmy2.AILogic.Behavioral
{
    /// <summary>
    /// Modélise des erreurs de visée humaines réalistes pour les corrections IA :
    /// distribution gaussienne adaptative, loi de Fitts, et distribution
    /// asymétrique log-normale (pilier 3 de la stratégie anti-EAC).
    /// </summary>
    public class HumanAimingModel
    {
        private readonly Random _rng = new();
        private readonly object _lock = new();

        private double _errorStdDev = 3.5;
        private double _fittsIndex = 0.6;
        private double _logNormalSkew = 0.45;
        private double _magnitudeInfluence = 0.25;

        public void Configure(double errorStdDev, double fittsIndex, double logNormalSkew, double magnitudeInfluence)
        {
            lock (_lock)
            {
                _errorStdDev = Math.Max(0.1, errorStdDev);
                _fittsIndex = Math.Clamp(fittsIndex, 0.1, 5.0);
                _logNormalSkew = Math.Clamp(logNormalSkew, 0.0, 1.0);
                _magnitudeInfluence = Math.Clamp(magnitudeInfluence, 0.0, 1.0);
            }
        }

        public (int X, int Y) Apply(int targetX, int targetY, int currentX, int currentY, int imageSize = 640)
        {
            lock (_lock)
            {
                double distance = Math.Sqrt(Math.Pow(targetX - currentX, 2) + Math.Pow(targetY - currentY, 2));
                double normalizedDistance = distance / (imageSize * 0.5);

                double fittsError = Math.Log(normalizedDistance + 1.0) * _fittsIndex * 10.0;
                double magnitudeError = _magnitudeInfluence * distance * 0.15;

                double totalStd = _errorStdDev + fittsError + magnitudeError;

                double errorX = (ApplyGaussian(_rng) * totalStd) + (ApplyLogNormalBias(_rng) * _logNormalSkew * totalStd);
                double errorY = (ApplyGaussian(_rng) * totalStd) - (ApplyLogNormalBias(_rng) * _logNormalSkew * totalStd);

                int finalX = (int)Math.Round(targetX + errorX);
                int finalY = (int)Math.Round(targetY + errorY);

                return (finalX, finalY);
            }
        }

        private double ApplyGaussian(Random rng)
        {
            double u1 = 1.0 - rng.NextDouble();
            double u2 = rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        private double ApplyLogNormalBias(Random rng)
        {
            double u = rng.NextDouble();
            double value = Math.Log(u + 1e-9);
            value = Math.Exp(value + 1.0);
            return value * (rng.NextDouble() < 0.5 ? -1.0 : 1.0);
        }
    }
}
