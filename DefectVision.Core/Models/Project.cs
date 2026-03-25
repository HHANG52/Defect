using System;
using System.Collections.Generic;

namespace DefectVision.Core.Models
{
    /// <summary>
    /// 项目/工作空间，一个项目对应一个检测场景（如"座椅缺陷检测"）
    /// </summary>
    public class DefectProject
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public string RootPath { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>数据集目录结构（YOLO 格式）</summary>
        public string ImagesTrainDir => System.IO.Path.Combine(RootPath, "dataset", "images", "train");
        public string ImagesValDir => System.IO.Path.Combine(RootPath, "dataset", "images", "val");
        public string LabelsTrainDir => System.IO.Path.Combine(RootPath, "dataset", "labels", "train");
        public string LabelsValDir => System.IO.Path.Combine(RootPath, "dataset", "labels", "val");
        public string ModelsDir => System.IO.Path.Combine(RootPath, "models");
        public string ExportsDir => System.IO.Path.Combine(RootPath, "exports");
        public string DataYamlPath => System.IO.Path.Combine(RootPath, "dataset", "data.yaml");

        /// <summary>缺陷类别定义</summary>
        public List<DefectClass> Classes { get; set; } = new List<DefectClass>();

        /// <summary>当前使用的模型版本</summary>
        public string ActiveModelVersionId { get; set; } = "";
    }

    /// <summary>
    /// 缺陷类别
    /// </summary>
    public class DefectClass
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Color { get; set; } = "#FF0000";
        public string Description { get; set; } = "";
    }
}