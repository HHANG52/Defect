using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DefectVision.Core.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DefectVision.Core.Services
{
    public class LabelMeConvertResult
    {
        public int ImageCount { get; set; }
        public int AnnotationCount { get; set; }
        public int TrainCount { get; set; }
        public int ValCount { get; set; }
        public List<string> ClassNames { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
        public string DataYamlPath { get; set; } = "";
    }

    public class LabelMeToYoloConverter
    {
        /// <summary>
        /// Convert LabelMe JSON dataset to YOLO format
        /// </summary>
        public LabelMeConvertResult Convert(string sourceFolder, DefectProject project)
        {
            var result = new LabelMeConvertResult();

            // Find all json files
            var jsonFiles = Directory.GetFiles(sourceFolder, "*.json", SearchOption.AllDirectories)
                .Where(f => !Path.GetFileName(f).StartsWith("."))
                .ToList();

            if (jsonFiles.Count == 0)
                throw new FileNotFoundException($"No JSON files found in {sourceFolder}");

            // Step 1: Scan all JSONs to collect class names
            var allEntries = new List<LabelMeEntry>();
            var classSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var jsonPath in jsonFiles)
            {
                try
                {
                    var entry = ParseLabelMeJson(jsonPath, sourceFolder);
                    if (entry != null && entry.Shapes.Count > 0)
                    {
                        allEntries.Add(entry);
                        foreach (var s in entry.Shapes)
                            classSet.Add(s.Label);
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{Path.GetFileName(jsonPath)}: {ex.Message}");
                }
            }

            if (allEntries.Count == 0)
                throw new Exception("No valid LabelMe annotations found");

            // Build class list (sorted for consistency)
            var classList = classSet.OrderBy(c => c).ToList();
            result.ClassNames = classList;

            var classToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < classList.Count; i++)
                classToId[classList[i]] = i;

            // Step 2: Create YOLO dataset directories
            string datasetDir = Path.Combine(project.RootPath, "dataset");
            string imgTrainDir = Path.Combine(datasetDir, "images", "train");
            string imgValDir = Path.Combine(datasetDir, "images", "val");
            string lblTrainDir = Path.Combine(datasetDir, "labels", "train");
            string lblValDir = Path.Combine(datasetDir, "labels", "val");

            Directory.CreateDirectory(imgTrainDir);
            Directory.CreateDirectory(imgValDir);
            Directory.CreateDirectory(lblTrainDir);
            Directory.CreateDirectory(lblValDir);

            // Step 3: Shuffle and split 80/20
            var rng = new Random(42);
            var shuffled = allEntries.OrderBy(_ => rng.Next()).ToList();
            int trainCount = (int)(shuffled.Count * 0.8);
            if (trainCount < 1) trainCount = 1;

            int totalAnnotations = 0;

            for (int i = 0; i < shuffled.Count; i++)
            {
                var entry = shuffled[i];
                bool isTrain = i < trainCount;
                string imgDst = isTrain ? imgTrainDir : imgValDir;
                string lblDst = isTrain ? lblTrainDir : lblValDir;

                // Copy image
                if (!string.IsNullOrEmpty(entry.ImagePath) && File.Exists(entry.ImagePath))
                {
                    string imgName = Path.GetFileName(entry.ImagePath);
                    string destImg = Path.Combine(imgDst, imgName);
                    if (!File.Exists(destImg))
                        File.Copy(entry.ImagePath, destImg, false);

                    // Generate YOLO label file
                    string lblName = Path.ChangeExtension(imgName, ".txt");
                    string destLbl = Path.Combine(lblDst, lblName);

                    var lines = new List<string>();
                    foreach (var shape in entry.Shapes)
                    {
                        if (!classToId.ContainsKey(shape.Label)) continue;
                        int classId = classToId[shape.Label];

                        // Convert polygon/rectangle points to bounding box
                        var bbox = GetBoundingBox(shape.Points,
                            entry.ImageWidth, entry.ImageHeight);

                        if (bbox.HasValue)
                        {
                            var b = bbox.Value;
                            // YOLO format: classId cx cy w h (normalized 0-1)
                            lines.Add($"{classId} {b.cx:F6} {b.cy:F6} {b.w:F6} {b.h:F6}");
                            totalAnnotations++;
                        }
                    }

                    if (lines.Count > 0)
                        File.WriteAllLines(destLbl, lines);
                }
            }

            // Step 4: Generate data.yaml
            string yamlPath = Path.Combine(datasetDir, "data.yaml");
            var yamlLines = new List<string>
            {
                $"path: {datasetDir.Replace("\\", "/")}",
                "train: images/train",
                "val: images/val",
                "",
                $"nc: {classList.Count}",
                $"names: [{string.Join(", ", classList.Select(c => $"'{c}'"))}]"
            };
            File.WriteAllLines(yamlPath, yamlLines);

            // ★ Don't assign DataYamlPath directly (it may be read-only)
            // Instead, store the path in the result for the caller to handle
            result.DataYamlPath = yamlPath;

            result.ImageCount = allEntries.Count;
            result.AnnotationCount = totalAnnotations;
            result.TrainCount = trainCount;
            result.ValCount = shuffled.Count - trainCount;

            return result;
        }

        private LabelMeEntry ParseLabelMeJson(string jsonPath, string sourceFolder)
        {
            string json = File.ReadAllText(jsonPath);
            var obj = JObject.Parse(json);

            var entry = new LabelMeEntry
            {
                ImageWidth = obj["imageWidth"]?.Value<int>() ?? 0,
                ImageHeight = obj["imageHeight"]?.Value<int>() ?? 0
            };

            // Find image file
            string imagePath = obj["imagePath"]?.Value<string>() ?? "";
            if (!string.IsNullOrEmpty(imagePath))
            {
                // Try relative to json file location
                string jsonDir = Path.GetDirectoryName(jsonPath) ?? sourceFolder;

                if (Path.IsPathRooted(imagePath) && File.Exists(imagePath))
                {
                    entry.ImagePath = imagePath;
                }
                else
                {
                    string candidate = Path.Combine(jsonDir, imagePath);
                    if (File.Exists(candidate))
                        entry.ImagePath = Path.GetFullPath(candidate);
                    else
                    {
                        // Try same name as json but with image extension
                        string baseName = Path.GetFileNameWithoutExtension(jsonPath);
                        foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".bmp", ".tiff" })
                        {
                            candidate = Path.Combine(jsonDir, baseName + ext);
                            if (File.Exists(candidate))
                            {
                                entry.ImagePath = candidate;
                                break;
                            }
                        }
                    }
                }
            }

            // Parse shapes
            var shapes = obj["shapes"] as JArray;
            if (shapes != null)
            {
                foreach (var shape in shapes)
                {
                    string label = shape["label"]?.Value<string>();
                    string shapeType = shape["shape_type"]?.Value<string>();
                    var points = shape["points"] as JArray;

                    if (string.IsNullOrEmpty(label) || points == null || points.Count < 2)
                        continue;

                    var parsedPoints = new List<(double x, double y)>();
                    foreach (var pt in points)
                    {
                        var arr = pt as JArray;
                        if (arr != null && arr.Count >= 2)
                        {
                            parsedPoints.Add((arr[0].Value<double>(), arr[1].Value<double>()));
                        }
                    }

                    if (parsedPoints.Count >= 2)
                    {
                        entry.Shapes.Add(new LabelMeShape
                        {
                            Label = label,
                            ShapeType = shapeType ?? "polygon",
                            Points = parsedPoints
                        });
                    }
                }
            }

            return entry;
        }

        private (double cx, double cy, double w, double h)? GetBoundingBox(
            List<(double x, double y)> points, int imgW, int imgH)
        {
            if (points.Count < 2 || imgW <= 0 || imgH <= 0) return null;

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var (x, y) in points)
            {
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }

            // Clamp to image bounds
            minX = Math.Max(0, minX);
            minY = Math.Max(0, minY);
            maxX = Math.Min(imgW, maxX);
            maxY = Math.Min(imgH, maxY);

            double w = maxX - minX;
            double h = maxY - minY;

            if (w < 1 || h < 1) return null;

            // Normalize to 0-1
            double cx = (minX + w / 2.0) / imgW;
            double cy = (minY + h / 2.0) / imgH;
            double nw = w / imgW;
            double nh = h / imgH;

            return (cx, cy, nw, nh);
        }
    }

    internal class LabelMeEntry
    {
        public string ImagePath { get; set; }
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }
        public List<LabelMeShape> Shapes { get; set; } = new List<LabelMeShape>();
    }

    internal class LabelMeShape
    {
        public string Label { get; set; }
        public string ShapeType { get; set; }
        public List<(double x, double y)> Points { get; set; }
    }
}