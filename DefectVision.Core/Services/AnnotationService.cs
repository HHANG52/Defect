using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DefectVision.Core.Models;
using Newtonsoft.Json;

namespace DefectVision.Core.Services
{
    /// <summary>
    /// 标注数据管理：加载、保存、导入、导出
    /// 内部格式为 JSON，支持导出为 YOLO/COCO 格式
    /// </summary>
    public class AnnotationService
    {
        private readonly DefectProject _project;

        public AnnotationService(DefectProject project)
        {
            _project = project;
        }

        /// <summary>
        /// 保存单张图片的标注（内部 JSON 格式）
        /// </summary>
        public void SaveAnnotation(ImageAnnotation annotation)
        {
            string jsonDir = Path.Combine(_project.RootPath, "annotations");
            Directory.CreateDirectory(jsonDir);

            string baseName = Path.GetFileNameWithoutExtension(annotation.ImagePath);
            string jsonPath = Path.Combine(jsonDir, baseName + ".json");

            annotation.LastModified = DateTime.Now;
            File.WriteAllText(jsonPath, JsonConvert.SerializeObject(annotation, Formatting.Indented));
        }

        /// <summary>
        /// 加载单张图片的标注
        /// </summary>
        public ImageAnnotation LoadAnnotation(string imagePath)
        {
            string jsonDir = Path.Combine(_project.RootPath, "annotations");
            string baseName = Path.GetFileNameWithoutExtension(imagePath);
            string jsonPath = Path.Combine(jsonDir, baseName + ".json");

            if (File.Exists(jsonPath))
                return JsonConvert.DeserializeObject<ImageAnnotation>(File.ReadAllText(jsonPath));

            return new ImageAnnotation { ImagePath = imagePath };
        }

        /// <summary>
        /// 导出全部标注为 YOLO 格式（用于训练）
        /// </summary>
        public void ExportToYoloFormat(float trainRatio = 0.8f)
        {
            // 创建目录
            Directory.CreateDirectory(_project.ImagesTrainDir);
            Directory.CreateDirectory(_project.ImagesValDir);
            Directory.CreateDirectory(_project.LabelsTrainDir);
            Directory.CreateDirectory(_project.LabelsValDir);

            // 加载所有标注
            string jsonDir = Path.Combine(_project.RootPath, "annotations");
            if (!Directory.Exists(jsonDir)) return;

            var allAnnotations = Directory.GetFiles(jsonDir, "*.json")
                .Select(f => JsonConvert.DeserializeObject<ImageAnnotation>(File.ReadAllText(f)))
                .Where(a => a.Status == AnnotationStatus.Completed || a.Status == AnnotationStatus.Reviewed)
                .ToList();

            // 随机打乱并分割
            var rng = new Random(42);
            var shuffled = allAnnotations.OrderBy(_ => rng.Next()).ToList();
            int trainCount = (int)(shuffled.Count * trainRatio);

            for (int i = 0; i < shuffled.Count; i++)
            {
                bool isTrain = i < trainCount;
                var ann = shuffled[i];

                string imgDst = isTrain ? _project.ImagesTrainDir : _project.ImagesValDir;
                string lblDst = isTrain ? _project.LabelsTrainDir : _project.LabelsValDir;

                // 复制图片
                string imgName = Path.GetFileName(ann.ImagePath);
                if (File.Exists(ann.ImagePath))
                    File.Copy(ann.ImagePath, Path.Combine(imgDst, imgName), true);

                // 生成 YOLO 格式标签文件
                string lblName = Path.GetFileNameWithoutExtension(imgName) + ".txt";
                var lines = ann.Annotations.Select(a =>
                {
                    if (a.Type == AnnotationType.BoundingBox)
                    {
                        return string.Format(CultureInfo.InvariantCulture,
                            "{0} {1:F6} {2:F6} {3:F6} {4:F6}",
                            a.ClassId, a.BBox.CenterX, a.BBox.CenterY, a.BBox.Width, a.BBox.Height);
                    }
                    else
                    {
                        // 多边形（YOLO 分割格式）
                        var points = string.Join(" ", a.Polygon.Select(p =>
                            string.Format(CultureInfo.InvariantCulture, "{0:F6} {1:F6}", p.X, p.Y)));
                        return $"{a.ClassId} {points}";
                    }
                });

                File.WriteAllLines(Path.Combine(lblDst, lblName), lines);
            }

            // 生成 data.yaml
            GenerateDataYaml();
        }

        /// <summary>
        /// 导入 YOLO 格式标注（从已有数据集导入）
        /// </summary>
        public List<ImageAnnotation> ImportFromYoloFormat(string imagesDir, string labelsDir)
        {
            var results = new List<ImageAnnotation>();
            var imageExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };

            foreach (var imgFile in Directory.GetFiles(imagesDir))
            {
                if (!imageExts.Contains(Path.GetExtension(imgFile))) continue;

                string baseName = Path.GetFileNameWithoutExtension(imgFile);
                string lblFile = Path.Combine(labelsDir, baseName + ".txt");

                var ann = new ImageAnnotation
                {
                    ImagePath = imgFile,
                    Status = AnnotationStatus.Completed
                };

                if (File.Exists(lblFile))
                {
                    foreach (var line in File.ReadAllLines(lblFile))
                    {
                        var parts = line.Trim().Split(' ');
                        if (parts.Length < 5) continue;

                        int classId = int.Parse(parts[0]);
                        string className = _project.Classes.FirstOrDefault(c => c.Id == classId)?.Name ?? $"class_{classId}";

                        if (parts.Length == 5)
                        {
                            // 矩形框
                            ann.Annotations.Add(new Annotation
                            {
                                ClassId = classId,
                                ClassName = className,
                                Type = AnnotationType.BoundingBox,
                                BBox = new NormalizedRect
                                {
                                    CenterX = float.Parse(parts[1], CultureInfo.InvariantCulture),
                                    CenterY = float.Parse(parts[2], CultureInfo.InvariantCulture),
                                    Width = float.Parse(parts[3], CultureInfo.InvariantCulture),
                                    Height = float.Parse(parts[4], CultureInfo.InvariantCulture)
                                }
                            });
                        }
                        else
                        {
                            // 多边形
                            var polygon = new List<NormalizedPoint>();
                            for (int i = 1; i < parts.Length - 1; i += 2)
                            {
                                polygon.Add(new NormalizedPoint
                                {
                                    X = float.Parse(parts[i], CultureInfo.InvariantCulture),
                                    Y = float.Parse(parts[i + 1], CultureInfo.InvariantCulture)
                                });
                            }
                            ann.Annotations.Add(new Annotation
                            {
                                ClassId = classId,
                                ClassName = className,
                                Type = AnnotationType.Polygon,
                                Polygon = polygon
                            });
                        }
                    }
                }

                results.Add(ann);
                SaveAnnotation(ann);
            }

            return results;
        }

        private void GenerateDataYaml()
        {
            var lines = new List<string>
            {
                $"path: {_project.RootPath.Replace("\\", "/")}/dataset",
                "train: images/train",
                "val: images/val",
                "",
                $"nc: {_project.Classes.Count}",
                "names:"
            };

            foreach (var cls in _project.Classes.OrderBy(c => c.Id))
                lines.Add($"  {cls.Id}: {cls.Name}");

            File.WriteAllLines(_project.DataYamlPath, lines);
        }
    }
}