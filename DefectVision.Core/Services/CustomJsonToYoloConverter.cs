using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DefectVision.Core.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DefectVision.Core.Services
{
    /// <summary>
    /// 将自定义多边形 JSON 标注格式（Pens + Points）转换为 YOLO 训练数据集
    /// 同时生成 DefectVision 内部标注文件，以便界面显示缺陷
    /// </summary>
    public class CustomJsonToYoloConverter
    {
        public class ConvertResult
        {
            public int ImageCount { get; set; }
            public int AnnotationCount { get; set; }
            public int TrainCount { get; set; }
            public int ValCount { get; set; }
            public List<string> ClassNames { get; set; } = new List<string>();
            public string DataYamlPath { get; set; }
            public List<string> Errors { get; set; } = new List<string>();
        }

        private class AnnotationEntry
        {
            public string ImagePath { get; set; }
            public string JsonPath { get; set; }
            public int ImageWidth { get; set; }
            public int ImageHeight { get; set; }
            public List<PenItem> Pens { get; set; } = new List<PenItem>();
        }

        private class PenItem
        {
            public string Name { get; set; }
            public List<double[]> Points { get; set; } = new List<double[]>();
        }

        /// <summary>
        /// 将文件夹中的自定义 JSON 标注转换为 YOLO 数据集 + DefectVision 标注
        /// </summary>
        public ConvertResult Convert(
            string jsonFolder,
            string imageFolder,
            DefectProject project,
            float trainRatio = 0.8f,
            bool useSegmentation = false)
        {
            var result = new ConvertResult();

            if (!Directory.Exists(jsonFolder))
            {
                result.Errors.Add($"JSON 文件夹不存在: {jsonFolder}");
                return result;
            }

            // 1. 收集所有 JSON 文件
            var jsonFiles = Directory.GetFiles(jsonFolder, "*.json", SearchOption.AllDirectories)
                .OrderBy(f => f).ToList();

            if (jsonFiles.Count == 0)
            {
                result.Errors.Add("未找到任何 JSON 标注文件");
                return result;
            }

            // 2. 解析，收集类别
            var allEntries = new List<AnnotationEntry>();
            var classNameSet = new HashSet<string>();

            // 搜索图片的所有候选目录
            var searchDirs = new List<string>();
            if (!string.IsNullOrEmpty(imageFolder) && Directory.Exists(imageFolder))
                searchDirs.Add(imageFolder);
            searchDirs.Add(jsonFolder);
            string parentDir = Path.GetDirectoryName(jsonFolder);
            if (!string.IsNullOrEmpty(parentDir))
            {
                searchDirs.Add(parentDir);
                foreach (var sub in new[] { "SrcImage", "images", "Images", "imgs", "Src" })
                {
                    string d = Path.Combine(parentDir, sub);
                    if (Directory.Exists(d)) searchDirs.Add(d);
                }
            }

            foreach (var jsonFile in jsonFiles)
            {
                try
                {
                    var entry = ParseJson(jsonFile);
                    if (entry == null) continue;

                    string imgPath = FindImage(entry, jsonFile, searchDirs);
                    if (imgPath == null)
                    {
                        result.Errors.Add($"找不到图片: {Path.GetFileName(jsonFile)}");
                        continue;
                    }
                    entry.ImagePath = imgPath;

                    foreach (var pen in entry.Pens)
                        classNameSet.Add(pen.Name);

                    allEntries.Add(entry);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"解析失败 {Path.GetFileName(jsonFile)}: {ex.Message}");
                }
            }

            if (allEntries.Count == 0)
            {
                result.Errors.Add("没有有效的标注条目");
                return result;
            }

            // 3. 类别映射
            var classNames = classNameSet.OrderBy(c => c).ToList();
            var classMap = new Dictionary<string, int>();
            for (int i = 0; i < classNames.Count; i++)
                classMap[classNames[i]] = i;
            result.ClassNames = classNames;

            // 4. 输出目录
            string imgTrainDir = project.ImagesTrainDir;
            string imgValDir = project.ImagesValDir;
            string lblTrainDir = project.LabelsTrainDir;
            string lblValDir = project.LabelsValDir;
            string annotationsDir = Path.Combine(project.RootPath, "annotations");

            Directory.CreateDirectory(imgTrainDir);
            Directory.CreateDirectory(imgValDir);
            Directory.CreateDirectory(lblTrainDir);
            Directory.CreateDirectory(lblValDir);
            Directory.CreateDirectory(annotationsDir);

            // 5. 随机划分
            var rng = new Random(42);
            var shuffled = allEntries.OrderBy(_ => rng.Next()).ToList();
            int trainCount = (int)(shuffled.Count * trainRatio);

            for (int i = 0; i < shuffled.Count; i++)
            {
                var entry = shuffled[i];
                bool isTrain = i < trainCount;

                string destImgDir = isTrain ? imgTrainDir : imgValDir;
                string destLblDir = isTrain ? lblTrainDir : lblValDir;

                try
                {
                    // 复制图片
                    string imgFileName = Path.GetFileName(entry.ImagePath);
                    string destImgPath = Path.Combine(destImgDir, imgFileName);

                    if (File.Exists(destImgPath))
                    {
                        string nameOnly = Path.GetFileNameWithoutExtension(imgFileName);
                        string ext = Path.GetExtension(imgFileName);
                        imgFileName = $"{nameOnly}_{i}{ext}";
                        destImgPath = Path.Combine(destImgDir, imgFileName);
                    }

                    File.Copy(entry.ImagePath, destImgPath, true);

                    // 生成 YOLO 标签
                    string lblFileName = Path.GetFileNameWithoutExtension(imgFileName) + ".txt";
                    string destLblPath = Path.Combine(destLblDir, lblFileName);
                    var yoloLines = GenerateYoloLabels(entry, classMap, useSegmentation);
                    File.WriteAllLines(destLblPath, yoloLines, Encoding.UTF8);

                    // ★ 生成 DefectVision 内部标注文件（用于界面显示）
                    string annFileName = Path.GetFileNameWithoutExtension(imgFileName) + ".json";
                    string annFilePath = Path.Combine(annotationsDir, annFileName);
                    var dvAnnotation = BuildDefectVisionAnnotation(
                        entry, imgFileName, classMap, classNames);
                    string annJson = JsonConvert.SerializeObject(dvAnnotation, Formatting.Indented);
                    File.WriteAllText(annFilePath, annJson, Encoding.UTF8);

                    result.ImageCount++;
                    result.AnnotationCount += entry.Pens.Count;

                    if (isTrain) result.TrainCount++;
                    else result.ValCount++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"处理失败 {Path.GetFileName(entry.ImagePath)}: {ex.Message}");
                }
            }

            // 6. data.yaml
            string yamlPath = Path.Combine(project.RootPath, "data.yaml");
            WriteDataYaml(yamlPath, project.RootPath, classNames);
            result.DataYamlPath = yamlPath;

            return result;
        }

        /// <summary>
        /// 重载：不指定 imageFolder
        /// </summary>
        public ConvertResult Convert(
            string jsonFolder,
            DefectProject project,
            float trainRatio = 0.8f,
            bool useSegmentation = false)
        {
            return Convert(jsonFolder, null, project, trainRatio, useSegmentation);
        }

        // ==================== DefectVision 标注生成 ====================

        /// <summary>
        /// 构建 DefectVision 内部标注对象，包含矩形框和多边形两种形式
        /// </summary>
        private object BuildDefectVisionAnnotation(
            AnnotationEntry entry,
            string imageFileName,
            Dictionary<string, int> classMap,
            List<string> classNames)
        {
            var shapes = new List<object>();

            foreach (var pen in entry.Pens)
            {
                if (!classMap.ContainsKey(pen.Name)) continue;
                int classId = classMap[pen.Name];

                // 计算外接矩形
                double minX = pen.Points.Min(p => p[0]);
                double minY = pen.Points.Min(p => p[1]);
                double maxX = pen.Points.Max(p => p[0]);
                double maxY = pen.Points.Max(p => p[1]);

                // 多边形标注（保留完整轮廓）
                var polygonPoints = pen.Points.Select(p => new { x = p[0], y = p[1] }).ToList();

                shapes.Add(new
                {
                    label = pen.Name,
                    class_id = classId,
                    shape_type = "polygon",
                    // 外接矩形（用于快速显示）
                    bbox = new
                    {
                        x = minX,
                        y = minY,
                        width = maxX - minX,
                        height = maxY - minY
                    },
                    // 多边形点（用于精细显示）
                    points = polygonPoints,
                    // 面积
                    area = CalculatePolygonArea(pen.Points)
                });
            }

            return new
            {
                image_file = imageFileName,
                image_width = entry.ImageWidth,
                image_height = entry.ImageHeight,
                annotation_count = shapes.Count,
                shapes = shapes
            };
        }

        /// <summary>
        /// 计算多边形面积（Shoelace 公式）
        /// </summary>
        private double CalculatePolygonArea(List<double[]> points)
        {
            if (points.Count < 3) return 0;

            double area = 0;
            int n = points.Count;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                area += points[i][0] * points[j][1];
                area -= points[j][0] * points[i][1];
            }
            return Math.Abs(area) / 2.0;
        }

        // ==================== JSON 解析 ====================

        private AnnotationEntry ParseJson(string jsonPath)
        {
            string json = File.ReadAllText(jsonPath, Encoding.UTF8);
            var jObj = JObject.Parse(json);

            if (jObj["Pens"] == null || jObj["ImageWidth"] == null || jObj["ImageHeight"] == null)
                return null;

            var entry = new AnnotationEntry
            {
                JsonPath = jsonPath,
                ImagePath = jObj["ImagePath"]?.ToString(),
                ImageWidth = jObj["ImageWidth"]?.Value<int>() ?? 0,
                ImageHeight = jObj["ImageHeight"]?.Value<int>() ?? 0
            };

            if (entry.ImageWidth <= 0 || entry.ImageHeight <= 0)
                return null;

            var pens = jObj["Pens"] as JArray;
            if (pens == null) return entry;

            foreach (var pen in pens)
            {
                string name = pen["Name"]?.ToString();
                if (string.IsNullOrEmpty(name)) continue;

                var pointsArr = pen["Points"] as JArray;
                if (pointsArr == null || pointsArr.Count < 3) continue;

                var penItem = new PenItem { Name = name };

                foreach (var pt in pointsArr)
                {
                    var ptArr = pt as JArray;
                    if (ptArr == null || ptArr.Count < 2) continue;
                    penItem.Points.Add(new double[]
                    {
                        ptArr[0].Value<double>(),
                        ptArr[1].Value<double>()
                    });
                }

                if (penItem.Points.Count >= 3)
                    entry.Pens.Add(penItem);
            }

            return entry;
        }

        // ==================== 图片查找 ====================

        private string FindImage(AnnotationEntry entry, string jsonPath, List<string> searchDirs)
        {
            // 1. JSON 中绝对路径
            if (!string.IsNullOrEmpty(entry.ImagePath) && File.Exists(entry.ImagePath))
                return entry.ImagePath;

            string[] exts = { ".bmp", ".jpg", ".jpeg", ".png", ".tif", ".tiff" };

            // 2. 从 ImagePath 提取文件名
            if (!string.IsNullOrEmpty(entry.ImagePath))
            {
                string imgName = Path.GetFileName(entry.ImagePath);
                foreach (var dir in searchDirs)
                {
                    if (!Directory.Exists(dir)) continue;
                    string direct = Path.Combine(dir, imgName);
                    if (File.Exists(direct)) return direct;
                    var found = Directory.GetFiles(dir, imgName, SearchOption.AllDirectories).FirstOrDefault();
                    if (found != null) return found;
                }
            }

            // 3. 用 JSON 文件名找同名图片
            string baseName = Path.GetFileNameWithoutExtension(jsonPath);
            foreach (var dir in searchDirs)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var ext in exts)
                {
                    string candidate = Path.Combine(dir, baseName + ext);
                    if (File.Exists(candidate)) return candidate;
                    var found = Directory.GetFiles(dir, baseName + ext, SearchOption.AllDirectories)
                        .FirstOrDefault();
                    if (found != null) return found;
                }
            }

            return null;
        }

        // ==================== YOLO 标签生成 ====================

        private List<string> GenerateYoloLabels(
            AnnotationEntry entry,
            Dictionary<string, int> classMap,
            bool useSegmentation)
        {
            var lines = new List<string>();
            int imgW = entry.ImageWidth;
            int imgH = entry.ImageHeight;

            foreach (var pen in entry.Pens)
            {
                if (!classMap.ContainsKey(pen.Name)) continue;
                int classId = classMap[pen.Name];

                if (useSegmentation)
                {
                    var pts = SamplePoints(pen.Points, 200);
                    var sb = new StringBuilder();
                    sb.Append(classId);
                    foreach (var pt in pts)
                    {
                        double nx = Clamp(pt[0] / imgW, 0, 1);
                        double ny = Clamp(pt[1] / imgH, 0, 1);
                        sb.Append($" {nx:F6} {ny:F6}");
                    }
                    lines.Add(sb.ToString());
                }
                else
                {
                    double minX = pen.Points.Min(p => p[0]);
                    double minY = pen.Points.Min(p => p[1]);
                    double maxX = pen.Points.Max(p => p[0]);
                    double maxY = pen.Points.Max(p => p[1]);

                    minX = Math.Max(0, minX);
                    minY = Math.Max(0, minY);
                    maxX = Math.Min(imgW, maxX);
                    maxY = Math.Min(imgH, maxY);

                    double bw = maxX - minX;
                    double bh = maxY - minY;
                    if (bw < 2 || bh < 2) continue;

                    double cx = Clamp((minX + maxX) / 2.0 / imgW, 0, 1);
                    double cy = Clamp((minY + maxY) / 2.0 / imgH, 0, 1);
                    double nw = Clamp(bw / imgW, 0, 1);
                    double nh = Clamp(bh / imgH, 0, 1);

                    lines.Add($"{classId} {cx:F6} {cy:F6} {nw:F6} {nh:F6}");
                }
            }

            return lines;
        }

        private List<double[]> SamplePoints(List<double[]> points, int maxPoints)
        {
            if (points.Count <= maxPoints) return points;
            var sampled = new List<double[]>();
            double step = (double)(points.Count - 1) / (maxPoints - 1);
            for (int i = 0; i < maxPoints; i++)
            {
                int idx = Math.Min((int)Math.Round(i * step), points.Count - 1);
                sampled.Add(points[idx]);
            }
            return sampled;
        }

        private void WriteDataYaml(string path, string rootPath, List<string> classNames)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"path: {rootPath.Replace("\\", "/")}");
            sb.AppendLine("train: images/train");
            sb.AppendLine("val: images/val");
            sb.AppendLine();
            sb.AppendLine($"nc: {classNames.Count}");
            sb.Append("names: [");
            sb.Append(string.Join(", ", classNames.Select(n => $"'{n}'")));
            sb.AppendLine("]");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private double Clamp(double val, double min, double max)
        {
            if (val < min) return min;
            if (val > max) return max;
            return val;
        }
    }
}