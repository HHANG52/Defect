using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Python.Runtime;
using DefectVision.Core.Models;
using DefectVision.Core.Services.PythonBridge;

namespace DefectVision.Core.Services
{
    /// <summary>
    /// SAHI 切图推理服务，支持模型热更新
    /// </summary>
    public class SahiInferenceService
    {
        private readonly PythonEnvironment _pyEnv;
        private string _currentModelPath;
        private PyObject _detectionModel;      // 缓存的 SAHI 模型对象
        private readonly object _modelLock = new object();

        public SahiInferenceService()
        {
            _pyEnv = PythonEnvironment.Instance;
        }

        /// <summary>
        /// 热更新模型（线程安全）
        /// </summary>
        public void LoadModel(string modelPath, string device = "0", float confidence = 0.25f)
        {
            lock (_modelLock)
            {
                _pyEnv.Execute(() =>
                {
                    // 释放旧模型
                    _detectionModel?.Dispose();

                    dynamic sahi = Py.Import("sahi");
                    _detectionModel = sahi.AutoDetectionModel.from_pretrained(
                        model_type: "ultralytics",
                        model_path: modelPath,
                        confidence_threshold: confidence,
                        device: device
                    );

                    _currentModelPath = modelPath;
                });
            }
        }

        /// <summary>
        /// 推理单张图片
        /// </summary>
        public ImageDetectionResult Detect(string imagePath, DetectionConfig config)
        {
            if (_detectionModel == null)
                throw new InvalidOperationException("模型未加载");

            // 如果模型路径变了，自动热更新
            if (_currentModelPath != config.ModelPath)
                LoadModel(config.ModelPath, config.Device, config.Confidence);

            var result = new ImageDetectionResult { ImagePath = imagePath };
            var sw = Stopwatch.StartNew();

            _pyEnv.Execute(() =>
            {
                dynamic sahi_predict = Py.Import("sahi.predict");

                dynamic prediction;

                if (config.UseSahi)
                {
                    prediction = sahi_predict.get_sliced_prediction(
                        image: imagePath,
                        detection_model: _detectionModel,
                        slice_height: config.SliceHeight,
                        slice_width: config.SliceWidth,
                        overlap_height_ratio: config.OverlapRatio,
                        overlap_width_ratio: config.OverlapRatio,
                        postprocess_type: "NMS",
                        postprocess_match_metric: "IOU",
                        postprocess_match_threshold: config.IouThreshold,
                        verbose: 0
                    );
                }
                else
                {
                    prediction = sahi_predict.get_prediction(
                        image: imagePath,
                        detection_model: _detectionModel,
                        verbose: 0
                    );
                }

                foreach (dynamic obj in prediction.object_prediction_list)
                {
                    dynamic bbox = obj.bbox;
                    float x1 = (float)bbox.minx;
                    float y1 = (float)bbox.miny;
                    float x2 = (float)bbox.maxx;
                    float y2 = (float)bbox.maxy;

                    result.Detections.Add(new DetectedObject
                    {
                        X = x1,
                        Y = y1,
                        Width = x2 - x1,
                        Height = y2 - y1,
                        ClassId = (int)obj.category.id,
                        ClassName = (string)obj.category.name,
                        Score = (float)obj.score.value,
                        Area = (int)((x2 - x1) * (y2 - y1))
                    });
                }
            });

            sw.Stop();
            result.InferenceTimeMs = sw.Elapsed.TotalMilliseconds;

            return result;
        }

        /// <summary>
        /// 批量推理
        /// </summary>
        public List<ImageDetectionResult> DetectBatch(List<string> imagePaths, DetectionConfig config,
            Action<int, int> progressCallback = null)
        {
            var results = new List<ImageDetectionResult>();
            for (int i = 0; i < imagePaths.Count; i++)
            {
                results.Add(Detect(imagePaths[i], config));
                progressCallback?.Invoke(i + 1, imagePaths.Count);
            }
            return results;
        }
    }
}