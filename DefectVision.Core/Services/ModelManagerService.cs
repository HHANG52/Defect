using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DefectVision.Core.Models;
using Newtonsoft.Json;

namespace DefectVision.Core.Services
{
    /// <summary>
    /// 模型版本管理：版本切换、热更新、回滚
    /// </summary>
    public class ModelManagerService
    {
        private readonly DefectProject _project;
        private List<ModelVersion> _versions = new List<ModelVersion>();
        private string _configPath;

        /// <summary>模型热更新事件</summary>
        public event Action<ModelVersion> ModelSwitched;

        public IReadOnlyList<ModelVersion> Versions => _versions.AsReadOnly();
        public ModelVersion ActiveVersion => _versions.FirstOrDefault(v => v.Status == ModelStatus.Active);

        public ModelManagerService(DefectProject project)
        {
            _project = project;
            _configPath = Path.Combine(project.ModelsDir, "versions.json");
            LoadVersions();
        }

        /// <summary>
        /// 注册新模型版本（训练完成后调用）
        /// </summary>
        public void RegisterVersion(ModelVersion version)
        {
            _versions.Add(version);
            SaveVersions();
        }

        /// <summary>
        /// 切换到指定版本（热更新）
        /// 下一次推理将自动使用新模型
        /// </summary>
        public void SwitchToVersion(string versionId)
        {
            var target = _versions.FirstOrDefault(v => v.Id == versionId);
            if (target == null)
                throw new ArgumentException($"找不到版本: {versionId}");

            if (!File.Exists(target.ModelPath))
                throw new FileNotFoundException($"模型文件不存在: {target.ModelPath}");

            // ���消当前激活的版本
            foreach (var v in _versions.Where(v => v.Status == ModelStatus.Active))
                v.Status = ModelStatus.Ready;

            // 激活新版本
            target.Status = ModelStatus.Active;
            _project.ActiveModelVersionId = target.Id;

            SaveVersions();
            ModelSwitched?.Invoke(target);
        }

        /// <summary>
        /// 回滚到上一个版本
        /// </summary>
        public void Rollback()
        {
            var active = ActiveVersion;
            if (active == null) return;

            // 找到上一个 Ready 状态的版本
            var previous = _versions
                .Where(v => v.Id != active.Id && v.Status == ModelStatus.Ready)
                .OrderByDescending(v => v.CreatedAt)
                .FirstOrDefault();

            if (previous != null)
                SwitchToVersion(previous.Id);
        }

        /// <summary>
        /// 对比两个版本的指标
        /// </summary>
        public (ModelVersion versionA, ModelVersion versionB, string comparison) CompareVersions(string idA, string idB)
        {
            var a = _versions.First(v => v.Id == idA);
            var b = _versions.First(v => v.Id == idB);

            string comparison =
                $"版本对比：{a.Name} vs {b.Name}\n" +
                $"mAP@50:    {a.Metrics.MapAP50:P1} vs {b.Metrics.MapAP50:P1} ({(b.Metrics.MapAP50 - a.Metrics.MapAP50):+0.0%;-0.0%})\n" +
                $"mAP@50-95: {a.Metrics.MapAP5095:P1} vs {b.Metrics.MapAP5095:P1} ({(b.Metrics.MapAP5095 - a.Metrics.MapAP5095):+0.0%;-0.0%})\n" +
                $"F1:        {a.Metrics.F1Score:F3} vs {b.Metrics.F1Score:F3}";

            return (a, b, comparison);
        }

        /// <summary>
        /// 归档旧版本
        /// </summary>
        public void ArchiveVersion(string versionId)
        {
            var v = _versions.First(v2 => v2.Id == versionId);
            if (v.Status == ModelStatus.Active)
                throw new InvalidOperationException("不能归档当前激活的模型");
            v.Status = ModelStatus.Archived;
            SaveVersions();
        }

        /// <summary>
        /// 获取当前应使用的模型路径（供推理服务调用）
        /// </summary>
        public string GetActiveModelPath()
        {
            var active = ActiveVersion;
            if (active == null)
                throw new InvalidOperationException("没有激活的模型版本");

            // 优先使用 TensorRT 引擎
            if (!string.IsNullOrEmpty(active.EnginePath) && File.Exists(active.EnginePath))
                return active.EnginePath;

            return active.ModelPath;
        }

        private void LoadVersions()
        {
            if (File.Exists(_configPath))
                _versions = JsonConvert.DeserializeObject<List<ModelVersion>>(File.ReadAllText(_configPath)) ?? new List<ModelVersion>();
        }

        private void SaveVersions()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_configPath));
            File.WriteAllText(_configPath, JsonConvert.SerializeObject(_versions, Formatting.Indented));
        }
    }
}