using System.Collections.Generic;

namespace DefectVision.Core.Models
{
    /// <summary>
    /// 检测配置
    /// </summary>
    public class DetectionConfig
    {
        public string ModelPath { get; set; } = "";
        public string SourcePath { get; set; } = "";
        public string Device { get; set; } = "0";
        public int ImgSize { get; set; } = 1280;
        public float Confidence { get; set; } = 0.25f;
        public float IouThreshold { get; set; } = 0.5f;

        // SAHI 切图参数
        public bool UseSahi { get; set; } = true;
        public int SliceWidth { get; set; } = 1280;
        public int SliceHeight { get; set; } = 1280;
        public float OverlapRatio { get; set; } = 0.2f;
    }

    /// <summary>
    /// 单张图片检测结果
    /// </summary>
    public class ImageDetectionResult
    {
        public string ImagePath { get; set; } = "";
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }
        public List<DetectedObject> Detections { get; set; } = new List<DetectedObject>();
        public double InferenceTimeMs { get; set; }
        public bool HasDefect => Detections.Count > 0;
    }

    /// <summary>
    /// 单个检测到的目标
    /// </summary>
    public class DetectedObject
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public int ClassId { get; set; }
        public string ClassName { get; set; } = "";
        public float Score { get; set; }
        public int Area { get; set; }
    }
}