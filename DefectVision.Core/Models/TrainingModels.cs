using System;
using System.Collections.Generic;

namespace DefectVision.Core.Models
{
    /// <summary>
    /// 训练配置
    /// </summary>
    public class TrainingConfig
    {
        public string BaseModel { get; set; } = "yolo11m.pt";
        public int ImgSize { get; set; } = 1280;
        public int BatchSize { get; set; } = 8;
        public int Epochs { get; set; } = 200;
        public float LearningRate { get; set; } = 0.01f;
        public int Patience { get; set; } = 50;
        public string Device { get; set; } = "0";
        public int Workers { get; set; } = 4;
        public bool Augment { get; set; } = true;
        public bool Resume { get; set; } = false;
        public string ResumePath { get; set; } = "";

        // 高级参数
        public float Mosaic { get; set; } = 1.0f;
        public float Mixup { get; set; } = 0.1f;
        public float Scale { get; set; } = 0.5f;
        public bool MultiScale { get; set; } = false;
    }

    /// <summary>
    /// 训练状态（实时更新）
    /// </summary>
    public class TrainingStatus
    {
        public TrainingState State { get; set; } = TrainingState.Idle;
        public int CurrentEpoch { get; set; }
        public int TotalEpochs { get; set; }
        public float Progress => TotalEpochs > 0 ? (float)CurrentEpoch / TotalEpochs : 0;

        // 训练指标
        public float BoxLoss { get; set; }
        public float ClsLoss { get; set; }
        public float DflLoss { get; set; }
        public float MapAP50 { get; set; }
        public float MapAP5095 { get; set; }
        public float Precision { get; set; }
        public float Recall { get; set; }

        // 历史记录
        public List<EpochMetrics> History { get; set; } = new List<EpochMetrics>();

        public string BestModelPath { get; set; } = "";
        public string LastModelPath { get; set; } = "";
        public string Message { get; set; } = "";
        public DateTime? StartTime { get; set; }
        public TimeSpan Elapsed => StartTime.HasValue ? DateTime.Now - StartTime.Value : TimeSpan.Zero;
    }

    public enum TrainingState
    {
        Idle,
        Preparing,
        Training,
        Validating,
        Completed,
        Failed,
        Cancelled
    }

    public class EpochMetrics
    {
        public int Epoch { get; set; }
        public float BoxLoss { get; set; }
        public float ClsLoss { get; set; }
        public float MapAP50 { get; set; }
        public float MapAP5095 { get; set; }
        public float Precision { get; set; }
        public float Recall { get; set; }
        public float LearningRate { get; set; }
    }
}