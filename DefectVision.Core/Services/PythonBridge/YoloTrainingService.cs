using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Python.Runtime;
using DefectVision.Core.Models;

namespace DefectVision.Core.Services
{
    /// <summary>
    /// YOLO 训练服务（集成 Python 脚本回调）
    /// </summary>
    public class YoloTrainingService
    {
        private readonly PythonBridge.PythonEnvironment _pyEnv;
        private CancellationTokenSource _cts;

        public event Action<TrainingStatus> StatusChanged;
        public event Action<EpochMetrics> EpochCompleted;

        public YoloTrainingService()
        {
            _pyEnv = PythonBridge.PythonEnvironment.Instance;
        }

        /// <summary>
        /// 通过 Python 脚本启动训练（支持实时回调）
        /// </summary>
        public async Task<ModelVersion> StartTrainingAsync(
            DefectProject project,
            TrainingConfig config,
            CancellationToken externalToken = default)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            var status = new TrainingStatus
            {
                State = TrainingState.Preparing,
                TotalEpochs = config.Epochs,
                StartTime = DateTime.Now
            };
            NotifyStatus(status);

            string runName = $"train_{DateTime.Now:yyyyMMdd_HHmmss}";
            ModelVersion version = null;

            await Task.Run(() =>
            {
                _pyEnv.Execute(() =>
                {
                    // 导入训练脚本模块
                    // 先把脚本所在目录加入 sys.path
                    dynamic sys = Py.Import("sys");
                    string scriptDir = GetPythonScriptsDir();
                    sys.path.insert(0, scriptDir);

                    dynamic train_module = Py.Import("train_yolo");

                    // 创建 C# 回调桥接到 Python
                    // 使用 PyObject 包装 C# 委托
                    Action<string, PyObject> callbackAction = (eventName, metricsObj) =>
                    {
                        try
                        {
                            HandlePythonCallback(eventName, metricsObj, status);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"回调异常: {ex.Message}");
                        }
                    };

                    status.State = TrainingState.Training;
                    NotifyStatus(status);

                    // 调用 train_yolo.train()
                    dynamic result = train_module.train(
                        data_yaml: project.DataYamlPath,
                        base_model: config.BaseModel,
                        epochs: config.Epochs,
                        imgsz: config.ImgSize,
                        batch: config.BatchSize,
                        device: config.Device,
                        lr0: config.LearningRate,
                        patience: config.Patience,
                        workers: config.Workers,
                        augment: config.Augment,
                        mosaic: config.Mosaic,
                        mixup: config.Mixup,
                        scale: config.Scale,
                        project: project.ModelsDir,
                        name: runName,
                        resume: config.Resume
                    );

                    // 解析结果
                    string bestPath = (string)result["best_model_path"];
                    float bestMap50 = (float)(double)result["best_map50"];

                    status.State = TrainingState.Completed;
                    status.BestModelPath = bestPath;
                    status.MapAP50 = bestMap50;
                    NotifyStatus(status);

                    version = new ModelVersion
                    {
                        Name = runName,
                        ModelPath = bestPath,
                        TrainingConfig = config,
                        Status = ModelStatus.Ready,
                        Metrics = new ModelMetrics { MapAP50 = bestMap50 }
                    };
                });
            }, _cts.Token);

            return version;
        }

        public void CancelTraining()
        {
            _cts?.Cancel();
        }

        /// <summary>
        /// 评估模型
        /// </summary>
        public ModelMetrics EvaluateModel(string modelPath, string dataYaml, string device = "0")
        {
            return _pyEnv.Execute(() =>
            {
                dynamic sys = Py.Import("sys");
                sys.path.insert(0, GetPythonScriptsDir());

                dynamic eval_module = Py.Import("evaluate_model");
                dynamic result = eval_module.evaluate(
                    model_path: modelPath,
                    data_yaml: dataYaml,
                    device: device
                );

                var metrics = new ModelMetrics
                {
                    MapAP50 = (float)(double)result["overall"]["map50"],
                    MapAP5095 = (float)(double)result["overall"]["map5095"],
                    Precision = (float)(double)result["overall"]["precision"],
                    Recall = (float)(double)result["overall"]["recall"],
                };

                foreach (dynamic cls in result["per_class"])
                {
                    string name = (string)cls["class_name"];
                    metrics.PerClassMetrics[name] = new ClassMetrics
                    {
                        ClassName = name,
                        AP50 = (float)(double)cls["ap50"],
                    };
                }

                return metrics;
            });
        }

        /// <summary>
        /// 导出 TensorRT
        /// </summary>
        public string ExportToTensorRT(string modelPath, int imgSize = 1280, bool half = true, string device = "0")
        {
            return _pyEnv.Execute(() =>
            {
                dynamic sys = Py.Import("sys");
                sys.path.insert(0, GetPythonScriptsDir());

                dynamic export_module = Py.Import("export_model");
                dynamic result = export_module.export_model(
                    model_path: modelPath,
                    format: "engine",
                    imgsz: imgSize,
                    half: half,
                    device: device
                );

                return (string)result["export_path"];
            });
        }

        /// <summary>
        /// 速度基准测试
        /// </summary>
        public (double avgMs, double fps) BenchmarkModel(string modelPath, int imgSize = 1280, string device = "0")
        {
            return _pyEnv.Execute(() =>
            {
                dynamic sys = Py.Import("sys");
                sys.path.insert(0, GetPythonScriptsDir());

                dynamic export_module = Py.Import("export_model");
                dynamic result = export_module.benchmark_model(
                    model_path: modelPath,
                    imgsz: imgSize,
                    device: device,
                    runs: 50
                );

                return ((double)result["avg_ms"], (double)result["fps"]);
            });
        }

        private void HandlePythonCallback(string eventName, PyObject metricsObj, TrainingStatus status)
        {
            dynamic metrics = metricsObj;

            if (eventName == "val_end")
            {
                var epoch = new EpochMetrics
                {
                    MapAP50 = (float)(double)metrics["map50"],
                    MapAP5095 = (float)(double)metrics["map5095"],
                    Precision = (float)(double)metrics["precision"],
                    Recall = (float)(double)metrics["recall"],
                };

                status.MapAP50 = epoch.MapAP50;
                status.MapAP5095 = epoch.MapAP5095;
                status.Precision = epoch.Precision;
                status.Recall = epoch.Recall;

                EpochCompleted?.Invoke(epoch);
                NotifyStatus(status);
            }
            else if (eventName == "epoch_train_end")
            {
                status.CurrentEpoch = (int)metrics["epoch"];
                status.BoxLoss = (float)(double)metrics["box_loss"];

                NotifyStatus(status);
            }
        }

        private string GetPythonScriptsDir()
        {
            // Python 脚本相对于应用程序的路径
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string scriptsDir = Path.Combine(baseDir, "python_scripts");

            if (!Directory.Exists(scriptsDir))
            {
                // 开发环境下的路径
                scriptsDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "DefectVision.Python"));
            }

            return scriptsDir;
        }

        private void NotifyStatus(TrainingStatus status)
        {
            StatusChanged?.Invoke(status);
        }
    }
}