using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DefectVision.Core.Models;

namespace DefectVision.Core.Services
{
    /// <summary>
    /// 导出服务：检测报告、数据集、标注数据
    /// </summary>
    public class ExportService
    {
        /// <summary>
        /// 导出 CSV 格式检测报告
        /// </summary>
        public string ExportDetectionReportCsv(
            List<ImageDetectionResult> results,
            string outputPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("图片名称,缺陷数量,是否合格,类别,置信度,X,Y,宽,高,面积,推理耗时(ms)");

            foreach (var result in results)
            {
                string imgName = Path.GetFileName(result.ImagePath);
                string qualified = result.HasDefect ? "不合格" : "合格";

                if (result.Detections.Count == 0)
                {
                    sb.AppendLine($"{imgName},0,{qualified},,,,,,,,{result.InferenceTimeMs:F0}");
                }
                else
                {
                    foreach (var det in result.Detections)
                    {
                        sb.AppendLine(
                            $"{imgName}," +
                            $"{result.Detections.Count}," +
                            $"{qualified}," +
                            $"{det.ClassName}," +
                            $"{det.Score:F3}," +
                            $"{det.X:F0},{det.Y:F0}," +
                            $"{det.Width:F0},{det.Height:F0}," +
                            $"{det.Area}," +
                            $"{result.InferenceTimeMs:F0}");
                    }
                }
            }

            File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
            return outputPath;
        }

        /// <summary>
        /// 导出汇总统计报告
        /// </summary>
        public string ExportSummaryReport(
            List<ImageDetectionResult> results,
            string outputPath,
            string modelName = "")
        {
            int totalImages = results.Count;
            int defectImages = results.Count(r => r.HasDefect);
            int okImages = totalImages - defectImages;
            int totalDefects = results.Sum(r => r.Detections.Count);
            double avgTime = results.Count > 0 ? results.Average(r => r.InferenceTimeMs) : 0;
            double defectRate = totalImages > 0 ? (double)defectImages / totalImages : 0;

            var classCounts = results
                .SelectMany(r => r.Detections)
                .GroupBy(d => d.ClassName)
                .OrderByDescending(g => g.Count())
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("╔══════════════════════════════════════════════╗");
            sb.AppendLine("║          缺 陷 检 测 报 告                   ║");
            sb.AppendLine("╚══════════════════════════════════════════════╝");
            sb.AppendLine();
            sb.AppendLine($"  报告时间:     {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            if (!string.IsNullOrEmpty(modelName))
                sb.AppendLine($"  使用模型:     {modelName}");
            sb.AppendLine();
            sb.AppendLine("──────────── 总体统计 ────────────");
            sb.AppendLine($"  检测图片总数:  {totalImages}");
            sb.AppendLine($"  缺陷图片数:    {defectImages}");
            sb.AppendLine($"  合格图片数:    {okImages}");
            sb.AppendLine($"  缺陷总数:      {totalDefects}");
            sb.AppendLine($"  不良率:        {defectRate:P2}");
            sb.AppendLine($"  平均推理耗时:  {avgTime:F1} ms");
            sb.AppendLine();

            if (classCounts.Count > 0)
            {
                sb.AppendLine("──────────── 各类别统计 ────────────");
                sb.AppendLine($"  {"类别",-15} {"数量",6} {"占比",8} {"平均置信度",10} {"最低置信度",10}");
                sb.AppendLine($"  {new string('-', 55)}");

                foreach (var group in classCounts)
                {
                    double ratio = totalDefects > 0 ? (double)group.Count() / totalDefects : 0;
                    double avgScore = group.Average(d => d.Score);
                    double minScore = group.Min(d => d.Score);

                    sb.AppendLine(
                        $"  {group.Key,-15} {group.Count(),6} {ratio,8:P1} {avgScore,10:F3} {minScore,10:F3}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("──────────── 缺陷图片清单 ────────────");
            foreach (var result in results.Where(r => r.HasDefect))
            {
                string imgName = Path.GetFileName(result.ImagePath);
                var detSummary = string.Join(", ",
                    result.Detections.GroupBy(d => d.ClassName)
                        .Select(g => $"{g.Key}×{g.Count()}"));
                sb.AppendLine($"  {imgName,-50} [{detSummary}]");
            }

            File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
            return outputPath;
        }

        /// <summary>
        /// 导出 HTML 格式可视化报告
        /// </summary>
        public string ExportHtmlReport(
            List<ImageDetectionResult> results,
            string outputDir,
            string modelName = "")
        {
            Directory.CreateDirectory(outputDir);

            int totalImages = results.Count;
            int defectImages = results.Count(r => r.HasDefect);
            int totalDefects = results.Sum(r => r.Detections.Count);
            double defectRate = totalImages > 0 ? (double)defectImages / totalImages : 0;
            double avgTime = results.Count > 0 ? results.Average(r => r.InferenceTimeMs) : 0;

            var classCounts = results
                .SelectMany(r => r.Detections)
                .GroupBy(d => d.ClassName)
                .OrderByDescending(g => g.Count())
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang='zh'><head><meta charset='UTF-8'>");
            sb.AppendLine("<title>缺陷检测报告</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body{font-family:'Microsoft YaHei',sans-serif;margin:20px;background:#f5f5f5;}");
            sb.AppendLine(".card{background:white;border-radius:8px;padding:20px;margin:16px 0;box-shadow:0 2px 8px rgba(0,0,0,0.1);}");
            sb.AppendLine("h1{color:#1565C0;} h2{color:#333;border-bottom:2px solid #1565C0;padding-bottom:8px;}");
            sb.AppendLine(".stats{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:16px;}");
            sb.AppendLine(".stat-box{background:#E3F2FD;border-radius:8px;padding:16px;text-align:center;}");
            sb.AppendLine(".stat-box .value{font-size:32px;font-weight:bold;color:#1565C0;}");
            sb.AppendLine(".stat-box .label{font-size:14px;color:#666;margin-top:4px;}");
            sb.AppendLine(".defect-rate{background:#FFEBEE;} .defect-rate .value{color:#B71C1C;}");
            sb.AppendLine("table{width:100%;border-collapse:collapse;} th,td{padding:8px 12px;text-align:left;border-bottom:1px solid #eee;}");
            sb.AppendLine("th{background:#F5F5F5;font-weight:600;} tr:hover{background:#F9F9F9;}");
            sb.AppendLine(".tag{display:inline-block;padding:2px 8px;border-radius:4px;font-size:12px;color:white;margin:1px;}");
            sb.AppendLine(".tag-defect{background:#E53935;} .tag-ok{background:#43A047;}");
            sb.AppendLine("</style></head><body>");

            sb.AppendLine("<h1>🔬 缺陷检测报告</h1>");
            sb.AppendLine($"<p>生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            if (!string.IsNullOrEmpty(modelName))
                sb.AppendLine($" | 模型: {modelName}");
            sb.AppendLine("</p>");

            // 统计卡片
            sb.AppendLine("<div class='card'><h2>📊 总体统计</h2><div class='stats'>");
            sb.AppendLine($"<div class='stat-box'><div class='value'>{totalImages}</div><div class='label'>检测图片数</div></div>");
            sb.AppendLine($"<div class='stat-box'><div class='value'>{defectImages}</div><div class='label'>缺陷图片数</div></div>");
            sb.AppendLine($"<div class='stat-box defect-rate'><div class='value'>{defectRate:P1}</div><div class='label'>不良率</div></div>");
            sb.AppendLine($"<div class='stat-box'><div class='value'>{totalDefects}</div><div class='label'>缺陷总数</div></div>");
            sb.AppendLine($"<div class='stat-box'><div class='value'>{avgTime:F0}ms</div><div class='label'>平均推理耗时</div></div>");
            sb.AppendLine("</div></div>");

            // 类别统计表格
            if (classCounts.Count > 0)
            {
                sb.AppendLine("<div class='card'><h2>🏷 各类别统计</h2>");
                sb.AppendLine("<table><tr><th>类别</th><th>数量</th><th>占比</th><th>平均置信度</th><th>最低置信度</th></tr>");
                foreach (var g in classCounts)
                {
                    double ratio = totalDefects > 0 ? (double)g.Count() / totalDefects : 0;
                    sb.AppendLine($"<tr><td>{g.Key}</td><td>{g.Count()}</td><td>{ratio:P1}</td>" +
                                  $"<td>{g.Average(d => d.Score):F3}</td><td>{g.Min(d => d.Score):F3}</td></tr>");
                }
                sb.AppendLine("</table></div>");
            }

            // 详细结果表格
            sb.AppendLine("<div class='card'><h2>📋 详细结果</h2>");
            sb.AppendLine("<table><tr><th>图片</th><th>状态</th><th>缺陷数</th><th>缺陷详情</th><th>耗时</th></tr>");
            foreach (var r in results)
            {
                string name = Path.GetFileName(r.ImagePath);
                string status = r.HasDefect
                    ? "<span class='tag tag-defect'>不合格</span>"
                    : "<span class='tag tag-ok'>合格</span>";
                string details = string.Join(" ",
                    r.Detections.Select(d => $"<span class='tag tag-defect'>{d.ClassName} {d.Score:F2}</span>"));

                sb.AppendLine($"<tr><td>{name}</td><td>{status}</td><td>{r.Detections.Count}</td>" +
                              $"<td>{details}</td><td>{r.InferenceTimeMs:F0}ms</td></tr>");
            }
            sb.AppendLine("</table></div>");

            sb.AppendLine("</body></html>");

            string htmlPath = Path.Combine(outputDir, $"report_{DateTime.Now:yyyyMMdd_HHmmss}.html");
            File.WriteAllText(htmlPath, sb.ToString(), Encoding.UTF8);

            return htmlPath;
        }

        /// <summary>
        /// 将检测到缺陷的图片复制到单独目录（便于人工复检）
        /// </summary>
        public string ExportDefectImages(
            List<ImageDetectionResult> results,
            string outputDir)
        {
            string defectDir = Path.Combine(outputDir, "defect_images");
            string okDir = Path.Combine(outputDir, "ok_images");
            Directory.CreateDirectory(defectDir);
            Directory.CreateDirectory(okDir);

            foreach (var result in results)
            {
                if (!File.Exists(result.ImagePath)) continue;

                string fileName = Path.GetFileName(result.ImagePath);
                string destDir = result.HasDefect ? defectDir : okDir;

                File.Copy(result.ImagePath, Path.Combine(destDir, fileName), true);
            }

            return outputDir;
        }
    }
}