using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DefectVision.Core.Models;
using DefectVision.Core.Services;
using DefectVision.UI.Services;

namespace DefectVision.UI.ViewModels
{
    public partial class ModelManagerViewModel : ObservableObject
    {
        private ModelManagerService _modelManager;
        private YoloTrainingService _trainingService;
        private DefectProject _project;

        public ObservableCollection<ModelVersion> ModelVersions { get; } = new ObservableCollection<ModelVersion>();

        [ObservableProperty] private ModelVersion _selectedVersion;
        [ObservableProperty] private string _statusMessage = "";

        public event Action<ModelVersion> ActiveModelChanged;

        public ModelManagerViewModel()
        {
            _trainingService = new YoloTrainingService();
        }

        public void Initialize(DefectProject project)
        {
            _project = project;
            _modelManager = new ModelManagerService(project);
            RefreshVersionList();
        }

        public void RegisterNewVersion(ModelVersion version)
        {
            _modelManager.RegisterVersion(version);
            RefreshVersionList();
            StatusMessage = $"\u65B0\u6A21\u578B\u5DF2\u6CE8\u518C: {version.Name}";
        }

        // ==================== 底部按钮命令（操作 SelectedVersion） ====================

        [RelayCommand]
        private void ActivateSelected()
        {
            if (SelectedVersion == null)
            {
                StatusMessage = "\u8BF7\u5148\u9009\u62E9\u4E00\u4E2A\u6A21\u578B"; // 请先选择一个模型
                return;
            }
            Activate(SelectedVersion);
        }

        [RelayCommand]
        private async Task EvaluateSelected()
        {
            if (SelectedVersion == null)
            {
                StatusMessage = "\u8BF7\u5148\u9009\u62E9\u4E00\u4E2A\u6A21\u578B";
                return;
            }
            await Evaluate(SelectedVersion);
        }

        [RelayCommand]
        private void ArchiveSelected()
        {
            if (SelectedVersion == null)
            {
                StatusMessage = "\u8BF7\u5148\u9009\u62E9\u4E00\u4E2A\u6A21\u578B";
                return;
            }
            Archive(SelectedVersion);
        }

        // ==================== 带参数的命令（供内部或列表项按钮调用） ====================

        private void Activate(ModelVersion version)
        {
            if (version == null) return;

            try
            {
                _modelManager.SwitchToVersion(version.Id);
                RefreshVersionList();
                StatusMessage = $"\u2705 \u5DF2\u5207\u6362\u5230: {version.Name}";
                ActiveModelChanged?.Invoke(version);
            }
            catch (Exception ex)
            {
                StatusMessage = $"\u274C \u5207\u6362\u5931\u8D25: {ex.Message}";
            }
        }

        private async Task Evaluate(ModelVersion version)
        {
            if (version == null || _project == null) return;

            StatusMessage = $"\u6B63\u5728\u8BC4\u4F30 {version.Name}...";

            try
            {
                var metrics = await Task.Run(() =>
                    _trainingService.EvaluateModel(version.ModelPath, _project.DataYamlPath, "0"));

                version.Metrics = metrics;
                _modelManager.RegisterVersion(version);
                RefreshVersionList();

                StatusMessage = $"\u8BC4\u4F30\u5B8C\u6210: mAP@50={metrics.MapAP50:P1} " +
                                $"P={metrics.Precision:P1} R={metrics.Recall:P1} " +
                                $"F1={metrics.F1Score:F3}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"\u274C \u8BC4\u4F30\u5931\u8D25: {ex.Message}";
            }
        }

        private void Archive(ModelVersion version)
        {
            if (version == null) return;

            try
            {
                _modelManager.ArchiveVersion(version.Id);
                RefreshVersionList();
                StatusMessage = $"\u5DF2\u5F52\u6863: {version.Name}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"\u274C \u5F52\u6863\u5931\u8D25: {ex.Message}";
            }
        }

        // ==================== 工具栏命令 ====================

        [RelayCommand]
        private void Rollback()
        {
            try
            {
                _modelManager.Rollback();
                RefreshVersionList();
                var active = _modelManager.ActiveVersion;
                StatusMessage = $"\u5DF2\u56DE\u6EDA\u5230: {active?.Name ?? "\u65E0"}";
                if (active != null) ActiveModelChanged?.Invoke(active);
            }
            catch (Exception ex)
            {
                StatusMessage = $"\u274C \u56DE\u6EDA\u5931\u8D25: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task ImportModel()
        {
            var path = await DialogService.OpenFileAsync(
                "\u9009\u62E9\u6A21\u578B\u6587\u4EF6", "pt", "engine", "onnx");

            if (string.IsNullOrEmpty(path)) return;

            try
            {
                string modelsDir = _project.ModelsDir;
                Directory.CreateDirectory(modelsDir);

                string fileName = Path.GetFileName(path);
                string destPath = Path.Combine(modelsDir, fileName);

                if (File.Exists(destPath))
                {
                    string nameOnly = Path.GetFileNameWithoutExtension(fileName);
                    string ext = Path.GetExtension(fileName);
                    destPath = Path.Combine(modelsDir, $"{nameOnly}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}");
                }

                File.Copy(path, destPath, false);

                var version = new ModelVersion
                {
                    Name = $"imported_{Path.GetFileNameWithoutExtension(fileName)}",
                    Description = $"\u5BFC\u5165\u81EA: {path}",
                    ModelPath = destPath,
                    Status = ModelStatus.Ready,
                };

                if (Path.GetExtension(destPath).Equals(".engine", StringComparison.OrdinalIgnoreCase))
                    version.EnginePath = destPath;

                _modelManager.RegisterVersion(version);
                RefreshVersionList();

                StatusMessage = $"\u6A21\u578B\u5DF2\u5BFC\u5165: {version.Name}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"\u274C \u5BFC\u5165\u5931\u8D25: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task ExportTensorRT()
        {
            if (SelectedVersion == null)
            {
                StatusMessage = "\u8BF7\u5148\u9009\u62E9\u4E00\u4E2A\u6A21\u578B";
                return;
            }

            if (string.IsNullOrEmpty(SelectedVersion.ModelPath) || !File.Exists(SelectedVersion.ModelPath))
            {
                StatusMessage = "\u6A21\u578B\u6587\u4EF6\u4E0D\u5B58\u5728";
                return;
            }

            StatusMessage = "\u6B63\u5728\u5BFC\u51FA TensorRT \u5F15\u64CE...";

            try
            {
                string enginePath = await Task.Run(() =>
                    _trainingService.ExportToTensorRT(SelectedVersion.ModelPath, 1280, true, "0"));

                SelectedVersion.EnginePath = enginePath;
                _modelManager.RegisterVersion(SelectedVersion);
                RefreshVersionList();

                StatusMessage = $"\u2705 TensorRT \u5BFC\u51FA\u5B8C\u6210: {enginePath}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"\u274C \u5BFC\u51FA\u5931\u8D25: {ex.Message}";
            }
        }

        [RelayCommand]
        private void CompareModels()
        {
            if (ModelVersions.Count < 2)
            {
                StatusMessage = "\u81F3\u5C11\u9700\u8981 2 \u4E2A\u6A21\u578B\u7248\u672C\u624D\u80FD\u5BF9\u6BD4";
                return;
            }

            if (SelectedVersion == null)
            {
                StatusMessage = "\u8BF7\u5148\u9009\u62E9\u4E00\u4E2A\u6A21\u578B";
                return;
            }

            ModelVersion best = null;
            float bestMap = -1;
            foreach (var v in ModelVersions)
            {
                if (v.Id == SelectedVersion.Id) continue;
                if (v.Metrics.MapAP50 > bestMap)
                {
                    bestMap = v.Metrics.MapAP50;
                    best = v;
                }
            }

            if (best == null)
            {
                StatusMessage = "\u6CA1\u6709\u53EF\u5BF9\u6BD4\u7684\u6A21\u578B";
                return;
            }

            var (_, _, comparison) = _modelManager.CompareVersions(SelectedVersion.Id, best.Id);
            StatusMessage = comparison;
        }

        private void RefreshVersionList()
        {
            ModelVersions.Clear();
            if (_modelManager == null) return;

            foreach (var v in _modelManager.Versions)
                ModelVersions.Add(v);
        }
    }
}