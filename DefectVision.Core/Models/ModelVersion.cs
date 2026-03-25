using System;
using System.Collections.Generic;

namespace DefectVision.Core.Models
{
    /// <summary>
    /// 模型版本，支持热更新和回滚
    /// </summary>
    public class ModelVersion
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>.pt 模型文件路径</summary>
        public string ModelPath { get; set; } = "";

        /// <summary>TensorRT .engine 文件路径（可选）</summary>
        public string EnginePath { get; set; } = "";

        /// <summary>训练配置快照</summary>
        public TrainingConfig TrainingConfig { get; set; }

        /// <summary>评估指标</summary>
        public ModelMetrics Metrics { get; set; } = new ModelMetrics();

        /// <summary>版本状态</summary>
        public ModelStatus Status { get; set; } = ModelStatus.Draft;

        /// <summary>训练使用的数据集统计</summary>
        public DatasetStats DatasetStats { get; set; } = new DatasetStats();
    }

    public enum ModelStatus
    {
        Draft,      // 训练中
        Ready,      // 可用
        Active,     // 当前激活
        Archived    // 已归档
    }

    public class ModelMetrics
    {
        public float MapAP50 { get; set; }
        public float MapAP5095 { get; set; }
        public float Precision { get; set; }
        public float Recall { get; set; }
        public float F1Score => Precision + Recall > 0 ? 2 * Precision * Recall / (Precision + Recall) : 0;
        public Dictionary<string, ClassMetrics> PerClassMetrics { get; set; } = new Dictionary<string, ClassMetrics>();
    }

    public class ClassMetrics
    {
        public string ClassName { get; set; } = "";
        public float AP50 { get; set; }
        public float Precision { get; set; }
        public float Recall { get; set; }
        public int TruePositives { get; set; }
        public int FalsePositives { get; set; }
        public int FalseNegatives { get; set; }
    }

    public class DatasetStats
    {
        public int TotalImages { get; set; }
        public int TrainImages { get; set; }
        public int ValImages { get; set; }
        public Dictionary<string, int> ClassCounts { get; set; } = new Dictionary<string, int>();
    }
}