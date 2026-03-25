using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DefectVision.Core.Models;
using DefectVision.Core.Services;
using DefectVision.UI.Services;

namespace DefectVision.UI.ViewModels
{
    public partial class DetectionViewModel : ObservableObject
    {
        private SahiInferenceService _inferenceService;
        private ExportService _exportService;
        private List<ImageDetectionResult> _allResults = new List<ImageDetectionResult>();

        [ObservableProperty] private string _sourcePath = "";
        [ObservableProperty] private float _confidence = 0.25f;
        [ObservableProperty] private bool _useSahi = true;
        [ObservableProperty] private bool _isDetecting;

        private string _modelPath = "";
        private string _device = "0";

        [ObservableProperty] private Bitmap _currentImage;
        [ObservableProperty] private string _currentImagePath = "";
        [ObservableProperty] private string _imagePositionText = "\u65E0\u56FE\u7247"; // 无图片
        [ObservableProperty] private string _inferenceTimeText = "--";
        [ObservableProperty] private int _currentIndex = -1;

        public ObservableCollection<DetectedObject> DetectionList { get; } = new ObservableCollection<DetectedObject>();
        [ObservableProperty] private int _detectionCount;
        [ObservableProperty] private int _defectImageCount;
        [ObservableProperty] private int _totalImageCount;
        [ObservableProperty] private int _totalDefectCount;

        public DetectionViewModel()
        {
            _inferenceService = new SahiInferenceService();
            _exportService = new ExportService();
        }

        public void SwitchModel(string modelPath, string device = "0")
        {
            _modelPath = modelPath;
            _device = device;
            // 模型会在第一次推理时自动加载
        }

        [RelayCommand]
        private async Task BrowseSource()
        {
            string folder = await DialogService.OpenFolderAsync(
                "\u9009\u62E9\u56FE\u7247\u6587\u4EF6\u5939"); // 选择图片文件夹
            if (!string.IsNullOrEmpty(folder))
                SourcePath = folder;
        }

        [RelayCommand]
        private async Task RunDetection()
        {
            if (string.IsNullOrWhiteSpace(SourcePath))
            {
                ImagePositionText = "\u8BF7\u5148\u9009\u62E9\u56FE\u7247\u8DEF\u5F84"; // 请先选择图片路径
                return;
            }

            if (string.IsNullOrWhiteSpace(_modelPath) || !File.Exists(_modelPath))
            {
                ImagePositionText = "\u8BF7\u5148\u6FC0\u6D3B\u6A21\u578B"; // 请先激活模型
                return;
            }

            IsDetecting = true;
            _allResults.Clear();
            DetectionList.Clear();
            ImagePositionText = "\u6B63\u5728\u521D\u59CB\u5316..."; // 正在初始化...

            try
            {
                var config = new DetectionConfig
                {
                    ModelPath = _modelPath,
                    SourcePath = SourcePath,
                    Device = _device,
                    Confidence = Confidence,
                    UseSahi = UseSahi,
                    SliceWidth = 1280,
                    SliceHeight = 1280,
                    OverlapRatio = 0.2f,
                    IouThreshold = 0.5f
                };

                var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };

                var imagePaths = new List<string>();
                if (File.Exists(SourcePath))
                {
                    if (extensions.Contains(Path.GetExtension(SourcePath)))
                        imagePaths.Add(SourcePath);
                }
                else if (Directory.Exists(SourcePath))
                {
                    imagePaths = Directory.GetFiles(SourcePath, "*.*", SearchOption.TopDirectoryOnly)
                        .Where(f => extensions.Contains(Path.GetExtension(f)))
                        .OrderBy(f => f)
                        .ToList();
                }

                if (imagePaths.Count == 0)
                {
                    ImagePositionText = "\u672A\u627E\u5230\u56FE\u7247"; // 未找到图片
                    return;
                }

                // 确保模型已加载
                await Task.Run(() =>
                {
                    _inferenceService.LoadModel(_modelPath, _device, Confidence);
                });

                _allResults = await Task.Run(() =>
                    _inferenceService.DetectBatch(imagePaths, config, (done, total) =>
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            ImagePositionText = $"\u68C0\u6D4B\u4E2D... {done}/{total}"; // 检测中...
                        });
                    })
                );

                TotalImageCount = _allResults.Count;
                DefectImageCount = _allResults.Count(r => r.HasDefect);
                TotalDefectCount = _allResults.Sum(r => r.Detections.Count);

                if (_allResults.Count > 0)
                    CurrentIndex = 0;
                else
                    ImagePositionText = "\u65E0\u56FE\u7247"; // 无图片
            }
            catch (Exception ex)
            {
                ImagePositionText = $"\u68C0\u6D4B\u5931\u8D25: {ex.Message}"; // 检测失败
            }
            finally
            {
                IsDetecting = false;
            }
        }

        [RelayCommand]
        private void PrevImage()
        {
            if (CurrentIndex > 0) CurrentIndex--;
        }

        [RelayCommand]
        private void NextImage()
        {
            if (CurrentIndex < _allResults.Count - 1) CurrentIndex++;
        }

        partial void OnCurrentIndexChanged(int value)
        {
            UpdateCurrentDisplay();
        }

        private void UpdateCurrentDisplay()
        {
            if (CurrentIndex < 0 || CurrentIndex >= _allResults.Count)
            {
                CurrentImage = null;
                CurrentImagePath = "";
                DetectionList.Clear();
                DetectionCount = 0;
                return;
            }

            var result = _allResults[CurrentIndex];
            CurrentImagePath = result.ImagePath;
            ImagePositionText = $"{CurrentIndex + 1} / {_allResults.Count}";
            InferenceTimeText = $"{result.InferenceTimeMs:F0} ms";

            try
            {
                //  Use Stream to support TIF/TIFF formats
                using var stream = File.OpenRead(result.ImagePath);
                CurrentImage = new Bitmap(stream);
            }
            catch { CurrentImage = null; }

            DetectionList.Clear();
            foreach (var det in result.Detections)
                DetectionList.Add(det);

            DetectionCount = result.Detections.Count;
        }

        [RelayCommand]
        private async Task ExportReport()
        {
            if (_allResults.Count == 0) return;

            string folder = await DialogService.OpenFolderAsync(
                "\u9009\u62E9\u62A5\u544A\u4FDD\u5B58\u76EE\u5F55"); // 选择报告保存目录
            if (string.IsNullOrEmpty(folder)) return;

            try
            {
                // 导出 CSV
                string csvPath = Path.Combine(folder, $"report_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                _exportService.ExportDetectionReportCsv(_allResults, csvPath);

                // 导出汇总
                string summaryPath = Path.Combine(folder, $"summary_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                _exportService.ExportSummaryReport(_allResults, summaryPath);

                // 导出 HTML
                string htmlPath = _exportService.ExportHtmlReport(_allResults, folder);

                // 分拣缺陷/合格图片
                _exportService.ExportDefectImages(_allResults, folder);

                ImagePositionText = $"\u62A5\u544A\u5DF2\u5BFC\u51FA: {folder}"; // 报告已导出
            }
            catch (Exception ex)
            {
                ImagePositionText = $"\u5BFC\u51FA\u5931\u8D25: {ex.Message}"; // 导出失败
            }
        }
    }
}