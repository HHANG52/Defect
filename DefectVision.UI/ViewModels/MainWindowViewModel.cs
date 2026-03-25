using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DefectVision.Core.Models;
using DefectVision.Core.Services;
using DefectVision.Core.Services.PythonBridge;
using DefectVision.UI.Services;
using DefectVision.UI.Views.Dialogs;
using Avalonia.Controls;

namespace DefectVision.UI.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        [ObservableProperty] private string _projectName = "\u672A\u6253\u5F00\u9879\u76EE";
        [ObservableProperty] private string _statusText = "\u5C31\u7EEA \u2014 \u8BF7\u65B0\u5EFA\u6216\u6253\u5F00\u9879\u76EE";
        [ObservableProperty] private string _activeModelName = "\u65E0";
        [ObservableProperty] private string _gpuInfo = "\u672A\u68C0\u6D4B";
        [ObservableProperty] private bool _pythonReady;

        private DefectProject _project;
        private string _pythonDllPath = "";
        private ModelManagerService _modelManager;

        public AnnotationViewModel AnnotationVM { get; }
        public TrainingViewModel TrainingVM { get; }
        public ModelManagerViewModel ModelManagerVM { get; }
        public DetectionViewModel DetectionVM { get; }

        public MainWindowViewModel()
        {
            AnnotationVM = new AnnotationViewModel();
            TrainingVM = new TrainingViewModel();
            ModelManagerVM = new ModelManagerViewModel();
            DetectionVM = new DetectionViewModel();

            TrainingVM.TrainingCompleted += version =>
            {
                ModelManagerVM.RegisterNewVersion(version);
                StatusText = $"\u8BAD\u7EC3\u5B8C\u6210\uFF01\u65B0\u7248\u672C: {version.Name} mAP@50={version.Metrics.MapAP50:P1}";
            };

            ModelManagerVM.ActiveModelChanged += version =>
            {
                string modelPath = !string.IsNullOrEmpty(version.EnginePath) && File.Exists(version.EnginePath)
                    ? version.EnginePath
                    : version.ModelPath;

                DetectionVM.SwitchModel(modelPath);
                ActiveModelName = version.Name;
                StatusText = $"\u6A21\u578B\u5DF2\u5207\u6362: {version.Name}";
            };
        }

        [RelayCommand]
        private async Task NewProject()
        {
            string folder = await DialogService.OpenFolderAsync("\u9009\u62E9\u9879\u76EE\u4FDD\u5B58\u76EE\u5F55");
            if (string.IsNullOrEmpty(folder)) return;

            var dialog = new ProjectSetupDialog();
            var result = await dialog.ShowDialog<bool?>(GetMainWindow());

            if (result != true || dialog.Result == null) return;

            _project = dialog.Result;
            _project.RootPath = folder;
            _pythonDllPath = dialog.PythonDllPath ?? "";

            CreateProjectDirectories();
            SaveProject();

            // ★ Initialize Python - show detailed status
            await InitializePythonAsync();

            InitializeModules();

            ProjectName = _project.Name;
            StatusText = $"\u9879\u76EE\u5DF2\u521B\u5EFA: {_project.Name}";
            if (!PythonReady)
                StatusText += " (\u26A0 Python \u672A\u521D\u59CB\u5316\uFF0C\u8BF7\u68C0\u67E5\u8BBE\u7F6E)";
        }

        [RelayCommand]
        private async Task OpenProject()
        {
            string folder = await DialogService.OpenFolderAsync("\u9009\u62E9\u9879\u76EE\u76EE\u5F55");
            if (string.IsNullOrEmpty(folder)) return;

            string configPath = Path.Combine(folder, "project.json");
            if (!File.Exists(configPath))
            {
                StatusText = "\u6240\u9009\u76EE\u5F55\u4E0D\u662F\u6709\u6548\u7684 DefectVision \u9879\u76EE";
                return;
            }

            _project = Newtonsoft.Json.JsonConvert.DeserializeObject<DefectProject>(
                File.ReadAllText(configPath));

            // ★ Read Python config
            LoadPythonConfig(folder);

            await InitializePythonAsync();
            InitializeModules();

            ProjectName = _project.Name;
            StatusText = $"\u9879\u76EE\u5DF2\u6253\u5F00: {_project.Name}";
            if (!PythonReady)
                StatusText += " (\u26A0 Python \u672A\u521D\u59CB\u5316\uFF0C\u8BF7\u70B9 Python \u8BBE\u7F6E)";
        }

        [RelayCommand]
        private async Task ConfigurePython()
        {
            if (_project == null)
            {
                StatusText = "\u8BF7\u5148\u521B\u5EFA\u6216\u6253\u5F00\u9879\u76EE";
                return;
            }

            var dialog = new ProjectSetupDialog();
            dialog.LoadExisting(_project, _pythonDllPath);
            var result = await dialog.ShowDialog<bool?>(GetMainWindow());

            if (result != true || dialog.Result == null) return;

            _project.Name = dialog.Result.Name;
            _project.Classes = dialog.Result.Classes;
            _pythonDllPath = dialog.PythonDllPath ?? "";

            SaveProject();
            await InitializePythonAsync();
            InitializeModules();

            ProjectName = _project.Name;
            if (PythonReady)
                StatusText = "\u8BBE\u7F6E\u5DF2\u66F4\u65B0\uFF0CPython \u5DF2\u521D\u59CB\u5316";
            else
                StatusText = "\u8BBE\u7F6E\u5DF2\u66F4\u65B0\uFF0C\u4F46 Python \u521D\u59CB\u5316\u5931\u8D25";
        }

        private async Task InitializePythonAsync()
        {
            PythonReady = false;

            // ★ Validate path
            if (string.IsNullOrWhiteSpace(_pythonDllPath))
            {
                StatusText = "\u26A0 Python DLL \u8DEF\u5F84\u672A\u914D\u7F6E\uFF0C\u8BF7\u70B9 [Python \u8BBE\u7F6E] \u914D\u7F6E";
                GpuInfo = "\u672A\u68C0\u6D4B";
                return;
            }

            if (!File.Exists(_pythonDllPath))
            {
                StatusText = $"\u26A0 Python DLL \u6587\u4EF6\u4E0D\u5B58\u5728: {_pythonDllPath}";
                GpuInfo = "\u672A\u68C0\u6D4B";
                return;
            }

            StatusText = $"\u6B63\u5728\u521D\u59CB\u5316 Python: {_pythonDllPath}";

            try
            {
                // ★ Already initialized? Skip re-init
                if (PythonEnvironment.Instance.IsInitialized)
                {
                    PythonReady = true;
                    StatusText = "Python \u5DF2\u521D\u59CB\u5316";
                    DetectGpu();
                    return;
                }

                await Task.Run(() =>
                {
                    PythonEnvironment.Instance.Initialize(_pythonDllPath);
                });

                PythonReady = true;
                StatusText = "Python \u73AF\u5883\u521D\u59CB\u5316\u6210\u529F";

                // ★ Detect GPU
                DetectGpu();
            }
            catch (Exception ex)
            {
                PythonReady = false;
                GpuInfo = "\u521D\u59CB\u5316\u5931\u8D25";
                StatusText = $"\u274C Python \u521D\u59CB\u5316\u5931\u8D25: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[Python Init Error] {ex}");
            }
        }

        private void DetectGpu()
        {
            try
            {
                string gpuName = PythonEnvironment.Instance.Execute(() =>
                {
                    dynamic torch = Python.Runtime.Py.Import("torch");
                    bool hasCuda = (bool)torch.cuda.is_available();
                    if (hasCuda)
                    {
                        string name = (string)torch.cuda.get_device_name(0);
                        return name;
                    }
                    return "CPU Only";
                });
                GpuInfo = gpuName;
            }
            catch (Exception ex)
            {
                GpuInfo = $"\u68C0\u6D4B\u5931\u8D25";
                System.Diagnostics.Debug.WriteLine($"[GPU Detect Error] {ex.Message}");
            }
        }

        private void InitializeModules()
        {
            AnnotationVM.Initialize(_project);
            TrainingVM.Initialize(_project);
            ModelManagerVM.Initialize(_project);

            _modelManager = new ModelManagerService(_project);
            var active = _modelManager.ActiveVersion;
            if (active != null)
            {
                string modelPath = !string.IsNullOrEmpty(active.EnginePath) && File.Exists(active.EnginePath)
                    ? active.EnginePath
                    : active.ModelPath;

                DetectionVM.SwitchModel(modelPath);
                ActiveModelName = active.Name;
            }
        }

        private void LoadPythonConfig(string projectFolder)
        {
            _pythonDllPath = "";

            string pyConfigPath = Path.Combine(projectFolder, "python_config.json");
            if (!File.Exists(pyConfigPath)) return;

            try
            {
                string json = File.ReadAllText(pyConfigPath);
                var config = Newtonsoft.Json.JsonConvert.DeserializeObject<PythonConfig>(json);
                if (config != null)
                    _pythonDllPath = config.python_dll_path ?? "";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadPythonConfig Error] {ex.Message}");
            }
        }

        private void CreateProjectDirectories()
        {
            Directory.CreateDirectory(_project.ImagesTrainDir);
            Directory.CreateDirectory(_project.ImagesValDir);
            Directory.CreateDirectory(_project.LabelsTrainDir);
            Directory.CreateDirectory(_project.LabelsValDir);
            Directory.CreateDirectory(_project.ModelsDir);
            Directory.CreateDirectory(_project.ExportsDir);
            Directory.CreateDirectory(Path.Combine(_project.RootPath, "annotations"));
            Directory.CreateDirectory(Path.Combine(_project.RootPath, "images_raw"));
        }

        private void SaveProject()
        {
            // Save project config
            string configPath = Path.Combine(_project.RootPath, "project.json");
            File.WriteAllText(configPath,
                Newtonsoft.Json.JsonConvert.SerializeObject(_project, Newtonsoft.Json.Formatting.Indented));

            // ★ Save Python config as strongly-typed object
            string pyConfigPath = Path.Combine(_project.RootPath, "python_config.json");
            var pyConfig = new PythonConfig { python_dll_path = _pythonDllPath };
            File.WriteAllText(pyConfigPath,
                Newtonsoft.Json.JsonConvert.SerializeObject(pyConfig, Newtonsoft.Json.Formatting.Indented));
        }

        private Window GetMainWindow()
        {
            return (Avalonia.Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                ?.MainWindow;
        }
    }

    /// <summary>
    /// Python config model for serialization (avoid dynamic)
    /// </summary>
    public class PythonConfig
    {
        public string python_dll_path { get; set; } = "";
    }
}