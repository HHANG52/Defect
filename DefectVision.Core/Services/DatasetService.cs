using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DefectVision.Core.Models;
using DefectVision.Core.Services.PythonBridge;

namespace DefectVision.Core.Services
{
    /// <summary>
    /// 数据集管理服务：统计、拆分、格式转换
    /// </summary>
    public class DatasetService
    {
        private readonly DefectProject _project;

        public DatasetService(DefectProject project)
        {
            _project = project;
        }

        /// <summary>
        /// 获取数据集统计信息
        /// </summary>
        public DatasetStats GetStats()
        {
            var stats = new DatasetStats();

            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };

            // 统计训练集
            if (Directory.Exists(_project.ImagesTrainDir))
            {
                stats.TrainImages = Directory.GetFiles(_project.ImagesTrainDir)
                    .Count(f => extensions.Contains(Path.GetExtension(f)));
            }

            // 统计验证集
            if (Directory.Exists(_project.ImagesValDir))
            {
                stats.ValImages = Directory.GetFiles(_project.ImagesValDir)
                    .Count(f => extensions.Contains(Path.GetExtension(f)));
            }

            stats.TotalImages = stats.TrainImages + stats.ValImages;

            // 统计各类别数量
            stats.ClassCounts = new Dictionary<string, int>();
            CountLabels(_project.LabelsTrainDir, stats.ClassCounts);
            CountLabels(_project.LabelsValDir, stats.ClassCounts);

            return stats;
        }

        /// <summary>
        /// 验证数据集完整性
        /// </summary>
        public List<string> ValidateDataset()
        {
            var issues = new List<string>();

            // 检查 data.yaml
            if (!File.Exists(_project.DataYamlPath))
                issues.Add("缺少 data.yaml 文件");

            // 检查训练图片
            if (!Directory.Exists(_project.ImagesTrainDir) ||
                !Directory.GetFiles(_project.ImagesTrainDir).Any())
                issues.Add("训练集图片为空");

            // 检查验证图片
            if (!Directory.Exists(_project.ImagesValDir) ||
                !Directory.GetFiles(_project.ImagesValDir).Any())
                issues.Add("验证集图片为空");

            // 检查图片和标签一一对应
            CheckImageLabelPairs(_project.ImagesTrainDir, _project.LabelsTrainDir, "训练集", issues);
            CheckImageLabelPairs(_project.ImagesValDir, _project.LabelsValDir, "验证集", issues);

            // 检查类别数
            if (_project.Classes.Count == 0)
                issues.Add("未定义缺陷类别");

            return issues;
        }

        /// <summary>
        /// 通过 Python 获取详细数据集分析
        /// </summary>
        public Dictionary<string, object> AnalyzeDataset()
        {
            var pyEnv = PythonEnvironment.Instance;

            return pyEnv.Execute(() =>
            {
                dynamic sys = Python.Runtime.Py.Import("sys");
                string scriptDir = GetPythonScriptsDir();
                sys.path.insert(0, scriptDir);

                dynamic dataset_utils = Python.Runtime.Py.Import("dataset_utils");
                dynamic stats = dataset_utils.analyze_dataset(_project.DataYamlPath);

                // 转成 C# 字典
                var result = new Dictionary<string, object>();
                dynamic json_module = Python.Runtime.Py.Import("json");
                string jsonStr = (string)json_module.dumps(stats);
                result["raw_json"] = jsonStr;

                return result;
            });
        }

        private void CountLabels(string labelsDir, Dictionary<string, int> counts)
        {
            if (!Directory.Exists(labelsDir)) return;

            foreach (var lblFile in Directory.GetFiles(labelsDir, "*.txt"))
            {
                foreach (var line in File.ReadAllLines(lblFile))
                {
                    var parts = line.Trim().Split(' ');
                    if (parts.Length < 1) continue;

                    int classId;
                    if (!int.TryParse(parts[0], out classId)) continue;

                    string className = _project.Classes.FirstOrDefault(c => c.Id == classId)?.Name ?? $"class_{classId}";

                    if (counts.ContainsKey(className))
                        counts[className]++;
                    else
                        counts[className] = 1;
                }
            }
        }

        private void CheckImageLabelPairs(string imgDir, string lblDir, string splitName, List<string> issues)
        {
            if (!Directory.Exists(imgDir) || !Directory.Exists(lblDir)) return;

            var imgExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };

            var imgFiles = Directory.GetFiles(imgDir)
                .Where(f => imgExts.Contains(Path.GetExtension(f)))
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .ToHashSet();

            var lblFiles = Directory.GetFiles(lblDir, "*.txt")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .ToHashSet();

            int missingLabels = imgFiles.Count(f => !lblFiles.Contains(f));
            int orphanLabels = lblFiles.Count(f => !imgFiles.Contains(f));

            if (missingLabels > 0)
                issues.Add($"{splitName}: {missingLabels} 张图片缺少标签文件");

            if (orphanLabels > 0)
                issues.Add($"{splitName}: {orphanLabels} 个标签文件找不到对应图片");
        }

        private string GetPythonScriptsDir()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string scriptsDir = Path.Combine(baseDir, "python_scripts");
            if (!Directory.Exists(scriptsDir))
                scriptsDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "DefectVision.Python"));
            return scriptsDir;
        }
    }
}