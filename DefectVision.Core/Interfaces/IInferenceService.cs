using System.Collections.Generic;
using DefectVision.Core.Models;

namespace DefectVision.Core.Interfaces
{
    /// <summary>
    /// 推理服务接口
    /// </summary>
    public interface IInferenceService
    {
        /// <summary>加载模型</summary>
        void LoadModel(string modelPath, string device = "0", float confidence = 0.25f);

        /// <summary>推理单张图片</summary>
        ImageDetectionResult Detect(string imagePath, DetectionConfig config);

        /// <summary>批量推理</summary>
        List<ImageDetectionResult> DetectBatch(List<string> imagePaths, DetectionConfig config,
            System.Action<int, int> progressCallback = null);
    }
}