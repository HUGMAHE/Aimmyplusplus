using Aimmy2.External;
using Other;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using static Other.LogManager;

namespace Aimmy2.External
{
    /// <summary>
    /// KMNet client for KMBox Net communication.
    /// </summary>
    public class KMNetClient : IDisposable
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private CancellationTokenSource _cts;
        private bool _isConnected;

        public string IP { get; set; } = "192.168.1.100";
        public int Port { get; set; } = 6721;
        public bool IsConnected => _isConnected;

        public event Action<bool> ConnectionStatusChanged;
        public event Action<string> ErrorOccurred;

        public async Task<bool> ConnectAsync(string ip, int port)
        {
            try
            {
                IP = ip;
                Port = port;
                _client = new TcpClient();
                var connectTask = _client.ConnectAsync(ip, port);
                var timeoutTask = Task.Delay(2000);
                var completed = await Task.WhenAny(connectTask, timeoutTask);
                if (completed == timeoutTask)
                {
                    throw new TimeoutException("Connection to KMBox timed out.");
                }
                await connectTask;
                _stream = _client.GetStream();
                _isConnected = true;
                ConnectionStatusChanged?.Invoke(true);
                return true;
            }
            catch (Exception ex)
            {
                _isConnected = false;
                string errorMsg = $"KMBox connection failed: {ex.Message}";
                Log(LogLevel.Error, errorMsg, true, 3000);
                ErrorOccurred?.Invoke(errorMsg);
                ConnectionStatusChanged?.Invoke(false);
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                _isConnected = false;
                _stream?.Close();
                _client?.Close();
                ConnectionStatusChanged?.Invoke(false);
            }
            catch { }
        }

        public Task SendMovementAsync(int deltaX, int deltaY)
        {
            return SendRawAsync(KMNetProtocol.KMNetCommand.MouseMove, deltaX, deltaY, KMNetProtocol.MouseButton.None);
        }

        public Task SendClickAsync(KMNetProtocol.MouseButton button)
        {
            return SendRawAsync(KMNetProtocol.KMNetCommand.MouseClick, 0, 0, button);
        }

        public Task SendReleaseAsync(KMNetProtocol.MouseButton button)
        {
            return SendRawAsync(KMNetProtocol.KMNetCommand.MouseRelease, 0, 0, button);
        }

        public Task SendScrollAsync(int delta)
        {
            return SendRawAsync(KMNetProtocol.KMNetCommand.MouseScroll, 0, delta, KMNetProtocol.MouseButton.None);
        }

        private Task SendRawAsync(KMNetProtocol.KMNetCommand command, int deltaX, int deltaY, KMNetProtocol.MouseButton button)
        {
            try
            {
                if (!_isConnected || _stream == null)
                    return Task.CompletedTask;

                var packet = new KMNetProtocol.KMNetPacket
                {
                    Command = command,
                    DeltaX = (short)deltaX,
                    DeltaY = (short)deltaY,
                    Button = button,
                    Checksum = 0x00
                };

                byte[] data = packet.ToByteArray();
                return _stream.WriteAsync(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"KMNet send error: {ex.Message}", false);
                return Task.CompletedTask;
            }
        }

        public void Dispose()
        {
            try
            {
                _isConnected = false;
                _stream?.Dispose();
                _client?.Dispose();
            }
            catch { }
        }
    }
}
