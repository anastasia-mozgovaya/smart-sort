using System;
using System.IO.Ports;
using System.Threading.Tasks;

namespace eco.Services
{
    public class ArduinoService : IDisposable
    {
        private readonly SerialPort _port;

        public ArduinoService(string portName)
        {
            _port = new SerialPort(portName, 9600)
            {
                ReadTimeout = 16000,
                WriteTimeout = 500,
                DtrEnable = true,
                RtsEnable = true
            };

            try
            {
                if (!_port.IsOpen)
                {
                    _port.Open();
                    System.Threading.Thread.Sleep(2000);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка порта: {ex.Message}");
            }
        }

        public async Task<string> WaitForWasteAsync()
        {
            if (!_port.IsOpen) return "ERROR_PORT_CLOSED";

            try
            {
                _port.DiscardInBuffer();
                _port.DiscardOutBuffer();

                _port.WriteLine("START");
                System.Diagnostics.Debug.WriteLine("Команда START отправлена в Arduino.");

                return await Task.Run(() =>
                {
                    try
                    {
                        string response = _port.ReadLine().Trim();
                        System.Diagnostics.Debug.WriteLine($"Arduino прислала: {response}");

                        return response;
                    }
                    catch (TimeoutException)
                    {
                        System.Diagnostics.Debug.WriteLine("Тайм-аут программного чтения порта.");
                        return "ID:TIMEOUT";
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Ошибка чтения: {ex.Message}");
                        return "ERROR";
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка связи: {ex.Message}");
                return "ERROR";
            }
        }

        public void Dispose()
        {
            if (_port != null)
            {
                if (_port.IsOpen)
                {
                    try { _port.Close(); } catch { }
                }
                _port.Dispose();
            }
        }
    }
}