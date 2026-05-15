using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using eco.Services;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace eco.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private CancellationTokenSource _cts;
        private bool _isSnapRequested = false;
        private string? _base64Snapshot;

        private readonly YoloService _yoloService;
        private readonly OllamaService _ollamaService;
        private readonly MashaService _mashaService;
        private readonly ArduinoService _arduinoService;

        private bool _isOllamaActive = false;
        private Avalonia.Rect _lastStableBox;
        private DateTime _stableStartTime;
        private bool _isTracking = false;

        [ObservableProperty] private string _resObject = "";
        [ObservableProperty] private string _resMaterial = "";
        [ObservableProperty] private string _resBin = "";
        [ObservableProperty] private string _resRecommendation = "";
        [ObservableProperty] private string _resComment = "";
        [ObservableProperty] private Bitmap? _binImage;

        [ObservableProperty] private bool _isBusy = false;
        [ObservableProperty] private bool _isWaitingForArduino = false;

        [ObservableProperty] private bool _isWarningActive = false;

        [ObservableProperty] private string _response = "Наведите камеру на мусор...";
        [ObservableProperty] private Bitmap? _camera;

        [ObservableProperty] private bool _isModalVisible = false;
        [ObservableProperty] private string _modalMessage = "";
        [ObservableProperty] private IBrush _modalBackground = Brushes.Green;

        private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(3) };

        public MainWindowViewModel()
        {
            _yoloService = new YoloService("best.onnx");
            _ollamaService = new OllamaService(_httpClient);
            _mashaService = new MashaService(_httpClient);
            _arduinoService = new ArduinoService("COM3");

            _cts = new CancellationTokenSource();
            Task.Run(() => CameraLoop(_cts.Token));
        }

        private async Task<bool> CheckingTheNetwork()
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, "https://api.mashagpt.ru/");
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        private void CameraLoop(CancellationToken token)
        {
            using (var capture = new VideoCapture(0, VideoCaptureAPIs.DSHOW))
            {
                if (!capture.IsOpened()) return;

                using (var frame = new Mat())
                {
                    var lastPredictions = new List<Prediction>();
                    bool isProcessing = false;

                    while (!token.IsCancellationRequested)
                    {
                        Thread.Sleep(5);

                        if (_isOllamaActive || IsModalVisible || IsWaitingForArduino || IsWarningActive)
                        {
                            Thread.Sleep(100);
                            continue;
                        }

                        if (!capture.Read(frame) || frame.Empty()) continue;

                        if (!isProcessing)
                        {
                            isProcessing = true;
                            var frameCopy = frame.Clone();
                            Task.Run(() =>
                            {
                                try
                                {
                                    var results = _yoloService.Predict(frameCopy);
                                    if (results != null) lastPredictions = results;
                                }
                                catch { }
                                finally { frameCopy.Dispose(); isProcessing = false; }
                            });
                        }

                        if (lastPredictions.Count > 0 && !IsBusy && !IsWaitingForArduino && !IsWarningActive)
                        {
                            float maxScore = -1;
                            Avalonia.Rect bestBox = new Avalonia.Rect();
                            foreach (var p in lastPredictions)
                            {
                                if (p.Score > maxScore)
                                {
                                    maxScore = p.Score;
                                    bestBox = new Avalonia.Rect(p.Box.X, p.Box.Y, p.Box.Width, p.Box.Height);
                                }
                            }

                            if (!_isTracking)
                            {
                                _isTracking = true;
                                _lastStableBox = bestBox;
                                _stableStartTime = DateTime.Now;
                            }
                            else
                            {
                                int cx1 = (int)(_lastStableBox.X + _lastStableBox.Width / 2);
                                int cy1 = (int)(_lastStableBox.Y + _lastStableBox.Height / 2);
                                int cx2 = (int)(bestBox.X + bestBox.Width / 2);
                                int cy2 = (int)(bestBox.Y + bestBox.Height / 2);
                                double distance = Math.Sqrt(Math.Pow(cx2 - cx1, 2) + Math.Pow(cy2 - cy1, 2));

                                if (distance < 50)
                                {
                                    if ((DateTime.Now - _stableStartTime).TotalMilliseconds >= 3000)
                                    {
                                        _isTracking = false;
                                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                        {
                                            if (!IsBusy) PhotographCommand.Execute(null);
                                        });
                                    }
                                }
                                else { _lastStableBox = bestBox; _stableStartTime = DateTime.Now; }
                            }
                        }
                        else { _isTracking = false; }

                        foreach (var obj in lastPredictions)
                        {
                            var rect = new OpenCvSharp.Rect((int)obj.Box.X, (int)obj.Box.Y, (int)obj.Box.Width, (int)obj.Box.Height);
                            Cv2.Rectangle(frame, rect, new Scalar(115, 169, 66), 2);
                        }

                        if (_isSnapRequested)
                        {
                            _base64Snapshot = Convert.ToBase64String(frame.ToBytes(".jpg"));
                            _isSnapRequested = false;
                        }

                        byte[] imageBytes = frame.ToBytes(".bmp");
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            using (var ms = new System.IO.MemoryStream(imageBytes))
                            {
                                var oldBitmap = Camera;
                                Camera = new Bitmap(ms);
                                oldBitmap?.Dispose();
                            }
                        });
                    }
                }
            }
        }

        [RelayCommand]
        private async Task Photograph()
        {
            if (IsBusy || IsWaitingForArduino || IsWarningActive) return;
            IsBusy = true;

            ResObject = ResMaterial = ResBin = ResRecommendation = ResComment = "";
            BinImage = null;

            try
            {
                _isSnapRequested = true;
                await Task.Delay(400);

                if (string.IsNullOrEmpty(_base64Snapshot))
                {
                    Response = "Ошибка захвата кадра.";
                    IsBusy = false;
                    return;
                }

                _isOllamaActive = true;
                WasteResult? result = null;

                if (await CheckingTheNetwork())
                {
                    Response = "Анализ (Облако)...";
                    result = await _mashaService.AnalyzeAsync(_base64Snapshot);
                }

                if (result == null)
                {
                    Response = "Анализ (Локально)...";
                    result = await _ollamaService.AnalyzeAsync(_base64Snapshot);
                }

                _isOllamaActive = false;

                if (result != null)
                {
                    ResObject = result.Object;
                    ResMaterial = result.Material;
                    ResBin = result.Bin;
                    ResRecommendation = result.Recommendation;
                    ResComment = result.Comment;

                    string binLower = result.Bin.ToLower();

                    if (binLower.Contains("рано") ||
                        binLower.Contains("не утилизируется") ||
                        binLower.Contains("подготовк")) 
                    {
                        Response = "Внимание! Объект требует подготовки.";
                        IsWarningActive = true;
                        IsBusy = false;

                        await Task.Delay(10000); 

                        IsWarningActive = false;
                        return; 
                    }

                    string targetColor = "";
                    string fileName = "";
                    string expectedId = "";

                    if (binLower.Contains("синий")) { targetColor = "Синий"; fileName = "blue.JPG"; expectedId = "ID:1"; }
                    else if (binLower.Contains("желтый") || binLower.Contains("жёлтый")) { targetColor = "Желтый"; fileName = "yellow.JPG"; expectedId = "ID:2"; }
                    else if (binLower.Contains("серый")) { targetColor = "Серый"; fileName = "grey.jpg"; expectedId = "ID:3"; }
                    else if (binLower.Contains("красный")) { targetColor = "Красный"; fileName = "red.JPG"; expectedId = "ID:4"; }
                    else if (binLower.Contains("зеленый")) { targetColor = "Зеленый"; fileName = "green.JPG"; expectedId = "ID:5"; }

                    if (!string.IsNullOrEmpty(targetColor))
                    {
                        try
                        {
                            var assets = AssetLoader.Open(new Uri($"avares://eco/Assets/{fileName}"));
                            BinImage = new Bitmap(assets);
                        }
                        catch { BinImage = null; }

                        Response = $"Ожидание: выбросьте объект в {targetColor} бак";

                        IsWaitingForArduino = true;
                        IsBusy = false; 

                        await Task.Delay(500);
                        string arduinoResponse = await _arduinoService.WaitForWasteAsync();
                        IsWaitingForArduino = false;

                        if (arduinoResponse.Contains(expectedId))
                        {
                            ModalMessage = "УСПЕХ!\nВерная сортировка";
                            ModalBackground = Brushes.Green;
                        }
                        else if (arduinoResponse.Contains("TIMEOUT"))
                        {
                            ModalMessage = "ВРЕМЯ ИСТЕКЛО\nОбъект не обнаружен";
                            ModalBackground = Brushes.Orange;
                        }
                        else
                        {
                            ModalMessage = "ОШИБКА!\nНеверный контейнер";
                            ModalBackground = Brushes.Red;
                        }

                        IsModalVisible = true;
                        await Task.Delay(4000);
                        IsModalVisible = false;
                    }
                    else
                    {
                        Response = "Бак для этого отходов не найден.";
                        IsBusy = false;
                        await Task.Delay(3000);
                    }
                }
                else
                {
                    Response = "Не удалось распознать объект.";
                    IsBusy = false;
                    await Task.Delay(2000);
                }
            }
            catch (Exception ex)
            {
                Response = "Ошибка системы.";
                System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
                IsBusy = false;
            }
            finally
            {
                IsBusy = false;
                IsWaitingForArduino = false;
                IsWarningActive = false;
                _isOllamaActive = false;

                ResObject = "";
                ResMaterial = "";
                ResBin = "";
                ResRecommendation = "";
                ResComment = "";
                BinImage = null; 

                Response = "Наведите камеру на мусор...";
                _base64Snapshot = null;
            }
        }

        public void StopCamera()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _arduinoService?.Dispose();
        }
    }
}