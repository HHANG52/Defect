using System.Threading;
using System.Threading.Tasks;
using DefectVision.Core.Models;

namespace DefectVision.Core.Interfaces
{
    /// <summary>
    /// 训练服务接口
    /// </summary>
    public interface ITrainingService
    {
        event System.Action<TrainingStatus> StatusChanged;
        event System.Action<EpochMetrics> EpochCompleted;

        Task<ModelVersion> StartTrainingAsync(DefectProject project, TrainingConfig config,
            CancellationToken token = default);
        void CancelTraining();
        ModelMetrics EvaluateModel(string modelPath, string dataYaml, string device = "0");
        string ExportToTensorRT(string modelPath, int imgSize = 1280, bool half = true, string device = "0");
    }
}