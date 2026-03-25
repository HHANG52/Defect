using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using DefectVision.Core.Models;
using DefectVision.Core.Services.PythonBridge;
using DefectVision.UI.Services;

namespace DefectVision.UI.ViewModels
{
    public partial class TrainingViewModel : ObservableObject
    {
        private DefectProject _project;
        private CancellationTokenSource _cts;
        private Process _trainingProcess;
        private string _lastModelPath;
        private DateTime _startTime;

        [ObservableProperty] private string _selectedBaseModel = "yolo11m.pt";
        [ObservableProperty] private int _imgSize = 1280;
        [ObservableProperty] private int _batchSize = 8;
        [ObservableProperty] private int _epochs = 200;
        [ObservableProperty] private float _learningRate = 0.01f;
        [ObservableProperty] private int _patience = 50;
        [ObservableProperty] private string _device = "0";
        [ObservableProperty] private float _mosaic = 1.0f;
        [ObservableProperty] private float _mixup = 0.1f;
        [ObservableProperty] private float _scale = 0.5f;
        [ObservableProperty] private bool _multiScale;

        [ObservableProperty] private bool _isTraining;
        [ObservableProperty] private bool _canResume;
        [ObservableProperty] private double _trainingProgress;
        [ObservableProperty] private string _progressText = "0 / 0";
        [ObservableProperty] private string _mapAP50Text = "--";
        [ObservableProperty] private string _precisionText = "--";
        [ObservableProperty] private string _recallText = "--";
        [ObservableProperty] private string _boxLossText = "--";
        [ObservableProperty] private string _elapsedText = "00:00:00";
        [ObservableProperty] private string _trainingLog = "";

        public ObservableCollection<EpochMetrics> EpochHistory { get; } = new ObservableCollection<EpochMetrics>();
        public event Action<ModelVersion> TrainingCompleted;

        public TrainingViewModel() { }

        public void Initialize(DefectProject project)
        {
            _project = project;
        }

        // ==================== Public methods (called from View) ====================

        public async Task DoBrowseBaseModel()
        {
            var path = await DialogService.OpenFileAsync(
                "\u9009\u62E9\u9884\u8BAD\u7EC3\u6A21\u578B", "pt");
            if (!string.IsNullOrEmpty(path))
                SelectedBaseModel = path;
        }

        public async Task DoStartTraining()
        {
            if (_project == null)
            {
                AppendLog("\u274C \u8BF7\u5148\u521B\u5EFA\u6216\u6253\u5F00\u9879\u76EE");
                return;
            }

            if (IsTraining)
            {
                AppendLog("\u26A0 \u5DF2\u5728\u8BAD\u7EC3\u4E2D");
                return;
            }

            TrainingLog = "";
            AppendLog("\u6B63\u5728\u51C6\u5907...");

            // Find Python
            string pythonExe = FindPythonExeSimple();
            AppendLog($"Python: {pythonExe ?? "null"}");

            if (string.IsNullOrEmpty(pythonExe))
            {
                AppendLog("\u274C \u627E\u4E0D\u5230 python.exe");
                ShowPythonDebugInfo();
                return;
            }

            // Check dataset
            string dataYaml = _project.DataYamlPath;
            if (!File.Exists(dataYaml))
            {
                AppendLog($"\u274C \u627E\u4E0D\u5230 data.yaml: {dataYaml}");
                AppendLog("\u8BF7\u5148\u5728[\u6807\u6CE8]\u9875\u70B9\u51FB[\u5BFC\u51FA YOLO \u6570\u636E\u96C6]");
                return;
            }

            // Resolve model
            string baseModel = ResolveModelPath(SelectedBaseModel);

            IsTraining = true;
            _cts = new CancellationTokenSource();
            _startTime = DateTime.Now;

            AppendLog("\u5F00\u59CB\u8BAD\u7EC3...");
            AppendLog($"\u6A21\u578B: {baseModel}");
            AppendLog($"ImgSize: {ImgSize}  Batch: {BatchSize}  Epochs: {Epochs}");
            AppendLog($"Device: {Device}  LR: {LearningRate}");
            AppendLog($"data.yaml: {dataYaml}");
            AppendLog("---");

            string script = BuildTrainScript(baseModel, dataYaml);

            try
            {
                await RunPythonProcessAsync(pythonExe, script, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                AppendLog("\n\u23F9 \u8BAD\u7EC3\u5DF2\u53D6\u6D88");
                CanResume = true;
            }
            catch (Exception ex)
            {
                AppendLog($"\n\u274C \u8BAD\u7EC3\u5931\u8D25: {ex.Message}");
            }
            finally
            {
                IsTraining = false;
            }
        }

        public void DoStopTraining()
        {
            _cts?.Cancel();
            try
            {
                if (_trainingProcess != null && !_trainingProcess.HasExited)
                {
                    _trainingProcess.Kill(true);
                    AppendLog("\u6B63\u5728\u505C\u6B62...");
                }
            }
            catch { }

            // ★ Find last.pt for resume
            FindLastCheckpoint();
        }

        private void FindLastCheckpoint()
        {
            try
            {
                // Search for the latest last.pt in models directory
                string modelsDir = _project.ModelsDir;
                if (!Directory.Exists(modelsDir)) return;

                string latestLast = null;
                DateTime latestTime = DateTime.MinValue;

                foreach (var dir in Directory.GetDirectories(modelsDir, "train_*"))
                {
                    string weightsDir = Path.Combine(dir, "weights");
                    string lastPt = Path.Combine(weightsDir, "last.pt");
                    if (File.Exists(lastPt))
                    {
                        var time = File.GetLastWriteTime(lastPt);
                        if (time > latestTime)
                        {
                            latestTime = time;
                            latestLast = lastPt;
                        }
                    }
                }

                if (latestLast != null)
                {
                    _lastModelPath = latestLast;
                    CanResume = true;
                    AppendLog($"\u627E\u5230\u68C0\u67E5\u70B9: {latestLast}");
                }
                else
                {
                    AppendLog("\u672A\u627E\u5230 last.pt\uFF0C\u65E0\u6CD5\u7EED\u8BAD");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"\u67E5\u627E\u68C0\u67E5\u70B9\u5931\u8D25: {ex.Message}");
            }
        }

        public async Task DoResumeTraining()
        {
            if (string.IsNullOrEmpty(_lastModelPath) || !File.Exists(_lastModelPath))
            {
                // Try to find it
                FindLastCheckpoint();
                if (string.IsNullOrEmpty(_lastModelPath))
                {
                    AppendLog("\u274C \u6CA1\u6709\u53EF\u7EED\u8BAD\u7684\u6A21\u578B");
                    return;
                }
            }

            AppendLog($"\u4ECE\u68C0\u67E5\u70B9\u7EED\u8BAD: {_lastModelPath}");

            if (_project == null || IsTraining) return;

            string pythonExe = FindPythonExeSimple();
            if (string.IsNullOrEmpty(pythonExe))
            {
                AppendLog("\u274C \u627E\u4E0D\u5230 python.exe");
                return;
            }

            IsTraining = true;
            _cts = new CancellationTokenSource();
            _startTime = DateTime.Now;

            // ★ Build resume script (not retrain)
            string script = BuildResumeScript(_lastModelPath);
            AppendLog($"Python: {pythonExe}");
            AppendLog("---");

            try
            {
                await RunPythonProcessAsync(pythonExe, script, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                AppendLog("\n\u23F9 \u8BAD\u7EC3\u5DF2\u53D6\u6D88");
            }
            catch (Exception ex)
            {
                AppendLog($"\n\u274C \u7EED\u8BAD\u5931\u8D25: {ex.Message}");
            }
            finally
            {
                IsTraining = false;
            }
        }

        /// <summary>
        /// Build a resume script that uses YOLO's built-in resume
        /// </summary>
        private string BuildResumeScript(string lastPtPath)
        {
            string model = lastPtPath.Replace("\\", "/");

            return $@"
import sys
import json
import multiprocessing

def main():
    from ultralytics import YOLO

    print('Resuming training from: {model}', flush=True)
    model = YOLO('{model}')

    # resume=True tells YOLO to continue from where it stopped
    results = model.train(resume=True)

    best_path = str(model.trainer.best) if hasattr(model, 'trainer') else ''
    last_path = str(model.trainer.last) if hasattr(model, 'trainer') else ''

    result = {{'best': best_path, 'last': last_path}}

    try:
        result['map50'] = float(model.trainer.metrics.get('metrics/mAP50(B)', 0))
        result['map5095'] = float(model.trainer.metrics.get('metrics/mAP50-95(B)', 0))
        result['precision'] = float(model.trainer.metrics.get('metrics/precision(B)', 0))
        result['recall'] = float(model.trainer.metrics.get('metrics/recall(B)', 0))
    except:
        pass

    print('TRAIN_RESULT_JSON:' + json.dumps(result), flush=True)

if __name__ == '__main__':
    multiprocessing.freeze_support()
    main()
";
        }

        // ==================== Private helpers ====================

        private string FindPythonExeSimple()
        {
            // 1. From PythonEnvironment.DllPath
            string dllPath = PythonEnvironment.Instance.DllPath;
            if (!string.IsNullOrEmpty(dllPath))
            {
                string dir = Path.GetDirectoryName(dllPath);
                if (dir != null)
                {
                    string exe = Path.Combine(dir, "python.exe");
                    if (File.Exists(exe)) return exe;
                }
            }

            // 2. From project config
            try
            {
                string pyConfigPath = Path.Combine(_project.RootPath, "python_config.json");
                if (File.Exists(pyConfigPath))
                {
                    var config = Newtonsoft.Json.JsonConvert.DeserializeObject<PythonConfig>(
                        File.ReadAllText(pyConfigPath));
                    if (config != null && !string.IsNullOrEmpty(config.python_dll_path))
                    {
                        string dir = Path.GetDirectoryName(config.python_dll_path);
                        if (dir != null)
                        {
                            string exe = Path.Combine(dir, "python.exe");
                            if (File.Exists(exe)) return exe;
                        }
                    }
                }
            }
            catch { }

            // 3. Common Conda paths
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] paths = {
                Path.Combine(home, "anaconda3", "envs", "yolo", "python.exe"),
                Path.Combine(home, "miniconda3", "envs", "yolo", "python.exe"),
                Path.Combine(home, "anaconda3", "python.exe"),
            };
            foreach (var p in paths)
                if (File.Exists(p)) return p;

            // 4. PATH (file check only)
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    string exe = Path.Combine(dir.Trim(), "python.exe");
                    if (File.Exists(exe)) return exe;
                }
                catch { }
            }

            return null;
        }

        private void ShowPythonDebugInfo()
        {
            string dllPath = PythonEnvironment.Instance.DllPath;
            AppendLog("");
            if (!string.IsNullOrEmpty(dllPath))
            {
                string dir = Path.GetDirectoryName(dllPath) ?? "";
                AppendLog($"DLL: {dllPath}");
                AppendLog($"\u671F\u671B: {Path.Combine(dir, "python.exe")}");
                AppendLog($"\u5B58\u5728: {File.Exists(Path.Combine(dir, "python.exe"))}");
                try
                {
                    var files = Directory.GetFiles(dir, "python*");
                    AppendLog($"\u76EE\u5F55 python*: {string.Join(", ", Array.ConvertAll(files, Path.GetFileName))}");
                }
                catch { }
            }
            else
            {
                AppendLog("DLL \u8DEF\u5F84\u4E3A\u7A7A\uFF0C\u8BF7\u70B9[Python \u8BBE\u7F6E]");
            }
        }

        private string ResolveModelPath(string model)
        {
            if (model.Contains("\\") || model.Contains("/"))
                return model;

            string[] searchPaths = {
                Path.Combine(_project.RootPath, model),
                Path.Combine(_project.ModelsDir, model),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), model),
            };
            foreach (var sp in searchPaths)
            {
                if (File.Exists(sp))
                {
                    AppendLog($"\u627E\u5230: {sp}");
                    return sp;
                }
            }
            AppendLog($"\u672C\u5730\u672A\u627E\u5230 {model}, YOLO \u5C06\u5C1D\u8BD5\u4E0B\u8F7D");
            return model;
        }

        private string BuildTrainScript(string baseModel, string dataYaml)
        {
            string runName = $"train_{DateTime.Now:yyyyMMdd_HHmmss}";
            string projectDir = _project.ModelsDir.Replace("\\", "/");
            string data = dataYaml.Replace("\\", "/");
            string model = baseModel.Replace("\\", "/");

            return $@"
import sys
import json
import multiprocessing

def main():
    from ultralytics import YOLO

    print('Loading model...', flush=True)
    model = YOLO('{model}')
    print('Model loaded, starting training...', flush=True)

    results = model.train(
        data='{data}',
        epochs={Epochs},
        imgsz={ImgSize},
        batch={BatchSize},
        device='{Device}',
        lr0={LearningRate.ToString(System.Globalization.CultureInfo.InvariantCulture)},
        patience={Patience},
        workers=0,
        project='{projectDir}',
        name='{runName}',
        exist_ok=True,
        verbose=True,
    )

    best_path = str(model.trainer.best) if hasattr(model, 'trainer') else ''
    last_path = str(model.trainer.last) if hasattr(model, 'trainer') else ''

    result = {{'best': best_path, 'last': last_path}}

    try:
        result['map50'] = float(model.trainer.metrics.get('metrics/mAP50(B)', 0))
        result['map5095'] = float(model.trainer.metrics.get('metrics/mAP50-95(B)', 0))
        result['precision'] = float(model.trainer.metrics.get('metrics/precision(B)', 0))
        result['recall'] = float(model.trainer.metrics.get('metrics/recall(B)', 0))
    except:
        pass

    print('TRAIN_RESULT_JSON:' + json.dumps(result), flush=True)

if __name__ == '__main__':
    multiprocessing.freeze_support()
    main()
";
        }

        private async Task RunPythonProcessAsync(string pythonExe, string script, CancellationToken ct)
        {
            string scriptPath = Path.Combine(Path.GetTempPath(), $"dv_train_{Guid.NewGuid():N}.py");
            await File.WriteAllTextAsync(scriptPath, script, System.Text.Encoding.UTF8, ct);

            AppendLog($"\u811A\u672C: {scriptPath}");
            AppendLog($"\u542F\u52A8: {pythonExe}");

            var psi = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = $"\"{scriptPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = _project.RootPath
            };
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
            psi.Environment["PYTHONUNBUFFERED"] = "1";

            _trainingProcess = new Process { StartInfo = psi };

            try
            {
                bool started = _trainingProcess.Start();
                AppendLog(started ? "\u8FDB\u7A0B\u5DF2\u542F\u52A8" : "\u274C \u8FDB\u7A0B\u542F\u52A8\u5931\u8D25");

                if (!started) return;

                var outTask = Task.Run(() => ReadStream(_trainingProcess.StandardOutput, ct), ct);
                var errTask = Task.Run(() => ReadStream(_trainingProcess.StandardError, ct), ct);

                // Wait for exit without blocking UI
                while (!_trainingProcess.HasExited)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(500, ct);
                    ElapsedText = (DateTime.Now - _startTime).ToString(@"hh\:mm\:ss");
                }

                await outTask;
                await errTask;

                int exitCode = _trainingProcess.ExitCode;
                AppendLog(exitCode == 0
                    ? "\n\u2705 \u8BAD\u7EC3\u5B8C\u6210\uFF01"
                    : $"\n\u274C \u9000\u51FA\u7801: {exitCode}");
            }
            finally
            {
                _trainingProcess = null;
                try { File.Delete(scriptPath); } catch { }
            }
        }

        private void ReadStream(StreamReader reader, CancellationToken ct)
        {
            try
            {
                while (!reader.EndOfStream && !ct.IsCancellationRequested)
                {
                    string line = reader.ReadLine();
                    if (line == null) continue;

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (line.StartsWith("TRAIN_RESULT_JSON:"))
                        {
                            ParseTrainResult(line.Substring("TRAIN_RESULT_JSON:".Length));
                            return;
                        }
                        ParseYoloOutput(line);
                        AppendLog(line);
                    });
                }
            }
            catch { }
        }

        private static readonly Regex EpochRegex = new Regex(
            @"^\s*(\d+)/(\d+)\s+[\d.]+G?\s+([\d.]+)", RegexOptions.Compiled);

        private static readonly Regex ValRegex = new Regex(
            @"all\s+\d+\s+\d+\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)", RegexOptions.Compiled);

        private void ParseYoloOutput(string line)
        {
            var m = EpochRegex.Match(line);
            if (m.Success)
            {
                int ep = int.Parse(m.Groups[1].Value);
                int tot = int.Parse(m.Groups[2].Value);
                ProgressText = $"{ep} / {tot}";
                TrainingProgress = (double)ep / tot * 100;
                if (float.TryParse(m.Groups[3].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float bl))
                    BoxLossText = bl.ToString("F4");
            }

            var v = ValRegex.Match(line);
            if (v.Success)
            {
                if (float.TryParse(v.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float p)) PrecisionText = p.ToString("P1");
                if (float.TryParse(v.Groups[2].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float r)) RecallText = r.ToString("P1");
                if (float.TryParse(v.Groups[3].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float map)) MapAP50Text = map.ToString("P1");
            }
        }

        private void ParseTrainResult(string json)
        {
            try
            {
                var result = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                if (result == null) return;

                string bestPath = result.ContainsKey("best") ? result["best"]?.ToString() : "";
                _lastModelPath = bestPath;
                CanResume = true;
                AppendLog($"\u6700\u4F73\u6A21\u578B: {bestPath}");

                var version = new ModelVersion
                {
                    Name = $"train_{DateTime.Now:yyyyMMdd_HHmmss}",
                    ModelPath = bestPath,
                    Status = ModelStatus.Ready,
                    Metrics = new ModelMetrics()
                };

                if (result.ContainsKey("map50") && float.TryParse(result["map50"]?.ToString(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float m50))
                    version.Metrics.MapAP50 = m50;
                if (result.ContainsKey("precision") && float.TryParse(result["precision"]?.ToString(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float pr))
                    version.Metrics.Precision = pr;
                if (result.ContainsKey("recall") && float.TryParse(result["recall"]?.ToString(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float rc))
                    version.Metrics.Recall = rc;

                TrainingCompleted?.Invoke(version);
            }
            catch (Exception ex)
            {
                AppendLog($"\u89E3\u6790\u5931\u8D25: {ex.Message}");
            }
        }

        private void AppendLog(string msg)
        {
            string ts = DateTime.Now.ToString("HH:mm:ss");
            TrainingLog += $"[{ts}] {msg}\n";
        }
    }
}