using System.Collections.Generic;
using DefectVision.Core.Models;

namespace DefectVision.Core.Interfaces
{
    /// <summary>
    /// 模型版本管理接口
    /// </summary>
    public interface IModelManager
    {
        IReadOnlyList<ModelVersion> Versions { get; }
        ModelVersion ActiveVersion { get; }

        void RegisterVersion(ModelVersion version);
        void SwitchToVersion(string versionId);
        void Rollback();
        void ArchiveVersion(string versionId);
        string GetActiveModelPath();
    }
}