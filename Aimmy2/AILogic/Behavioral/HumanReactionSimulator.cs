using System.Threading;
using System.Threading.Tasks;
using Threading = System.Threading;

namespace Aimmy2.AILogic.Behavioral
{
    /// <summary>
    /// Simule un délai de réaction humain réaliste autour du moment où
    /// une correction IA est prête, selon le pilier 2 de la stratégie
    /// anti-EAC (section 7.6).
    /// </summary>
    public class HumanReactionSimulator
    {
        private readonly Random _rng = new();
        private readonly object _lock = new();

        private int _minDelayMs = 50;
        private int _maxDelayMs = 150;

        public void Configure(int minDelayMs, int maxDelayMs)
        {
            lock (_lock)
            {
                _minDelayMs = Math.Max(0, minDelayMs);
                _maxDelayMs = Math.Max(_minDelayMs, maxDelayMs);
            }
        }

        public async Threading.Tasks.Task DelayAsync(CancellationToken ct = default)
        {
            int delay;
            lock (_lock)
            {
                delay = _rng.Next(_minDelayMs, Math.Max(_minDelayMs + 1, _maxDelayMs + 1));
            }

            try
            {
                await Threading.Tasks.Task.Delay(delay, ct);
            }
            catch
            {
                // cancellation ou interruption : on ne bloque pas le flux IA
            }
        }
    }
}
