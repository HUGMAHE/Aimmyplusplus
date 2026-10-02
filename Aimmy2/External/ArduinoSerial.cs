using System;
using System.IO.Ports;
using System.Threading.Tasks;
using Other;
using static Other.LogManager;

namespace Aimmy2.External
{
    /// <summary>
    /// MODIFICATION DUAL-PC v2: Communication série avec Arduino HID
    /// Implémente un système dual-stream:
    /// - "U,X,Y\n" = Mouvements souris utilisateur (IMMÉDIAT)
    /// - "A,X,Y\n" = Corrections IA (ASYNCHRONE, dès disponibilité)
    /// Utilise un thread dédié pour éviter de bloquer la détection IA
    /// </summary>
    public class ArduinoSerial : IDisposable
    {
         // Types de commandes série
        private enum CommandType { User, AI, ClickLeft, ClickRight, ClickMiddle, ClickButton3, ClickButton4, ScrollWheel }

        private SerialPort? _serialPort;
        private readonly object _serialLock = new object();
        private bool _isConnected = false;
        private Queue<(CommandType type, int X, int Y)> _commandQueue = new Queue<(CommandType, int, int)>();
        private Queue<(CommandType type, string button, bool isPressed)> _clickQueue = new Queue<(CommandType, string, bool)>();
        private Queue<(CommandType type, int direction)> _wheelQueue = new Queue<(CommandType, int)>();
        private Thread? _sendThread;
        private volatile bool _threadRunning = false;

        /// <summary>
        /// Événement déclenché quand l'Arduino est connecté
        /// </summary>
        public event Action? OnConnected;

        /// <summary>
        /// Événement déclenché quand l'Arduino est déconnecté
        /// </summary>
        public event Action? OnDisconnected;

        /// <summary>
        /// Événement déclenché en cas d'erreur de communication
        /// </summary>
        public event Action<string>? OnError;

        public bool IsConnected => _isConnected;

        /// <summary>
        /// Connecte à l'Arduino sur le port COM spécifié
        /// </summary>
        public async Task<bool> ConnectAsync(string portName, int baudRate = 500000)
        {
            return await Task.Run(() =>
            {
                lock (_serialLock)
                {
                    try
                    {
                        if (_isConnected)
                        {
                            Disconnect();
                        }

                        // MODIFICATION: Configuration du port série pour Arduino à 500k baud
                        _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
                        {
                            Handshake = Handshake.None,
                            RtsEnable = true,
                            DtrEnable = true,
                            ReadTimeout = 1000,
                            WriteTimeout = 1000
                        };

                        _serialPort.Open();
                        _isConnected = true;

                        // Démarrer le thread d'envoi asynchrone
                        _threadRunning = true;
                        _sendThread = new Thread(SendQueueWorker)
                        {
                            IsBackground = true,
                            Name = "ArduinoSendThread"
                        };
                        _sendThread.Start();

                        Log(LogLevel.Info, $"Arduino connecté sur {portName} @ {baudRate} baud", true, 2000);
                        OnConnected?.Invoke();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        _isConnected = false;
                        string errorMsg = $"Erreur connexion Arduino: {ex.Message}";
                        Log(LogLevel.Error, errorMsg, true, 3000);
                        OnError?.Invoke(errorMsg);
                        return false;
                    }
                }
            });
        }

        /// <summary>
        /// Envoie les déltas de mouvement UTILISATEUR (souris brute)
        /// Format: "U,X,Y\n"
        /// CRUCIAL: Appelé depuis MouseHooker - doit être NON-BLOQUANT et IMMÉDIAT
        /// </summary>
        public void SendRawUserMovement(int deltaX, int deltaY)
        {
            if (!_isConnected)
                return;

            // Skip sending if no movement
            if (deltaX == 0 && deltaY == 0)
                return;

            // Ajouter à la queue pour envoi asynchrone (non-bloquant)
            lock (_serialLock)
            {
                _commandQueue.Enqueue((CommandType.User, deltaX, deltaY));
            }
        }

         /// <summary>
         /// Envoie les corrections IA (aim assist)
         /// Format: "A,X,Y\n"
         /// IMPORTANT: Appelé depuis l'analyse IA - peut être appelé avec latence
         /// mais n'affecte pas le mouvement utilisateur brut
         /// </summary>
         public void SendAICorrection(int correctionX, int correctionY)
         {
             if (!_isConnected)
                 return;

             // Skip sending if no correction
             if (correctionX == 0 && correctionY == 0)
                 return;

             // Ajouter à la queue pour envoi asynchrone
             lock (_serialLock)
             {
                 _commandQueue.Enqueue((CommandType.AI, correctionX, correctionY));
             }
         }

          /// <summary>
          /// Envoie les clics souris (left, right, middle, button 3, button 4)
          /// Format: "C,L/R/M/3/4,1/0\n" où 1=press, 0=release
          /// Appelé depuis MouseHooker - doit être NON-BLOQUANT
          /// </summary>
          public void SendMouseClick(string button, bool isPressed)
          {
              if (!_isConnected)
                  return;

              if (string.IsNullOrEmpty(button) || (button != "L" && button != "R" && button != "M" && button != "3" && button != "4"))
                  return;

              // Ajouter à la queue pour envoi asynchrone
              lock (_serialLock)
              {
                  CommandType type = button switch
                  {
                      "L" => CommandType.ClickLeft,
                      "R" => CommandType.ClickRight,
                      "M" => CommandType.ClickMiddle,
                      "3" => CommandType.ClickButton3,
                      "4" => CommandType.ClickButton4,
                      _ => CommandType.ClickLeft
                  };
                  _clickQueue.Enqueue((type, button, isPressed));
              }
          }

         /// <summary>
         /// Envoie la rotation de la molette de la souris
         /// Format: "W,1/-1\n" où 1=up, -1=down
         /// Appelé depuis MouseHooker - doit être NON-BLOQUANT
         /// </summary>
         public void SendScrollWheel(int direction)
         {
             if (!_isConnected)
                 return;

             if (direction == 0)
                 return;

             // Ajouter à la queue pour envoi asynchrone
             lock (_serialLock)
             {
                 _wheelQueue.Enqueue((CommandType.ScrollWheel, direction));
             }
         }

        /// <summary>
        /// DEPRECATED: Utiliser SendRawUserMovement() ou SendAICorrection() à la place
        /// Gardé pour compatibilité - redirige vers SendRawUserMovement()
        /// </summary>
        public void SendMovement(int deltaX, int deltaY)
        {
            SendRawUserMovement(deltaX, deltaY);
        }

         /// <summary>
         /// Thread worker pour envoyer les commandes sans bloquer le thread IA
         /// Gère trois types de messages:
         /// - "U,X,Y\n" = User movement (immédiat)
         /// - "A,X,Y\n" = AI correction (asynchrone)
         /// - "C,L/R/M,1/0\n" = Mouse click
         /// - "W,1/-1\n" = Scroll wheel
         /// </summary>
         private void SendQueueWorker()
         {
             while (_threadRunning)
             {
                 try
                 {
                     lock (_serialLock)
                     {
                         if (_serialPort != null && _isConnected)
                         {
                             // Priorité 1: Mouvements utilisateur + corrections IA
                             if (_commandQueue.Count > 0)
                             {
                                 var (commandType, deltaX, deltaY) = _commandQueue.Dequeue();
                                 string prefix = commandType == CommandType.User ? "U" : "A";
                                 string command = $"{prefix},{deltaX},{deltaY}\n";
                                 _serialPort.Write(command);
                             }

                             // Priorité 2: Clics souris
                             if (_clickQueue.Count > 0)
                             {
                                 var (commandType, button, isPressed) = _clickQueue.Dequeue();
                                 int pressValue = isPressed ? 1 : 0;
                                 string command = $"C,{button},{pressValue}\n";
                                 _serialPort.Write(command);
                             }

                             // Priorité 3: Molette souris
                             if (_wheelQueue.Count > 0)
                             {
                                 var (commandType, direction) = _wheelQueue.Dequeue();
                                 string command = $"W,{direction}\n";
                                 _serialPort.Write(command);
                             }
                         }
                     }

                     Thread.Sleep(1); // Petite pause pour éviter le spinning
                 }
                 catch (Exception ex)
                 {
                     Log(LogLevel.Error, $"Erreur envoi Arduino: {ex.Message}", false, 1000);
                     OnError?.Invoke($"Erreur d'envoi: {ex.Message}");
                 }
             }
         }

        /// <summary>
        /// Déconnecte du port série Arduino
        /// </summary>
        public void Disconnect()
        {
            lock (_serialLock)
            {
                _threadRunning = false;

                if (_sendThread != null)
                {
                    _sendThread.Join(1000); // Attendre que le thread se termine
                }

                if (_serialPort != null && _serialPort.IsOpen)
                {
                    try
                    {
                        _serialPort.Close();
                    }
                    catch { }
                }

                _isConnected = false;
                Log(LogLevel.Info, "Arduino déconnecté", true, 1500);
                OnDisconnected?.Invoke();
            }
        }

        /// <summary>
        /// Récupère la liste des ports COM disponibles
        /// </summary>
        public static string[] GetAvailablePorts()
        {
            return SerialPort.GetPortNames();
        }

        /// <summary>
        /// Teste la connexion Arduino en envoyant un ping
        /// </summary>
        public async Task<bool> TestConnectionAsync()
        {
            return await Task.Run(() =>
            {
                lock (_serialLock)
                {
                    try
                    {
                        if (_serialPort == null || !_isConnected)
                            return false;

                        _serialPort.Write("PING\n");
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }
            });
        }

        public void Dispose()
        {
            Disconnect();
            _serialPort?.Dispose();
        }
    }
}
