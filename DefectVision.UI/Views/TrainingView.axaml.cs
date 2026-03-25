using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DefectVision.UI.ViewModels;

namespace DefectVision.UI.Views
{
    public partial class TrainingView : UserControl
    {
        public TrainingView()
        {
            InitializeComponent();
            ApplyLocalizedText();
            LogTextBox.PropertyChanged += (s, e) =>
            {
                if (e.Property.Name == "Text")
                {
                    LogTextBox.CaretIndex = LogTextBox.Text?.Length ?? 0;
                }
            };
        }

        private TrainingViewModel VM => DataContext as TrainingViewModel;

        private async void OnStartTrainClick(object sender, RoutedEventArgs e)
        {
            if (VM == null) return;
            BtnStartTrain.IsEnabled = false;
            try
            {
                await VM.DoStartTraining();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Train Click Error] {ex}");
            }
            finally
            {
                BtnStartTrain.IsEnabled = !VM.IsTraining;
            }
        }

        private void OnStopTrainClick(object sender, RoutedEventArgs e)
        {
            VM?.DoStopTraining();
        }

        private async void OnResumeTrainClick(object sender, RoutedEventArgs e)
        {
            if (VM == null) return;
            BtnResumeTrain.IsEnabled = false;
            try
            {
                await VM.DoResumeTraining();
            }
            finally
            {
                BtnResumeTrain.IsEnabled = true;
            }
        }

        private async void OnBrowseModelClick(object sender, RoutedEventArgs e)
        {
            if (VM == null) return;
            await VM.DoBrowseBaseModel();
        }

        private void ApplyLocalizedText()
        {
            LblTrainingConfig.Text = "\uD83C\uDFCB \u8BAD\u7EC3\u914D\u7F6E";
            LblBaseModel.Text = "\u57FA\u7840\u6A21\u578B";
            BtnBrowseModel.Content = "\u6D4F\u89C8";
            LblModelHint.Text = "\u53EF\u70B9\u6D4F\u89C8\u9009\u62E9\u672C\u5730 .pt \u6587\u4EF6\uFF0C\u6216\u76F4\u63A5\u8F93\u5165\u6A21\u578B\u540D\u79F0";
            LblImgSize.Text = "\u8BAD\u7EC3\u56FE\u7247\u5C3A\u5BF8";
            LblBatch.Text = "\u6279\u5927\u5C0F (Batch)";
            LblEpochs.Text = "\u8BAD\u7EC3\u8F6E\u6570 (Epochs)";
            LblLR.Text = "\u5B66\u4E60\u7387";
            LblPatience.Text = "\u65E9\u505C\u8010\u5FC3\u503C";
            LblDevice.Text = "\u8BAD\u7EC3\u8BBE\u5907 (0=GPU, cpu=CPU)";
            BtnStartTrain.Content = "\uD83D\uDE80 \u5F00\u59CB\u8BAD\u7EC3";
            BtnStopTrain.Content = "\u23F9 \u505C\u6B62\u8BAD\u7EC3";
            BtnResumeTrain.Content = "\uD83D\uDD04 \u65AD\u70B9\u7EED\u8BAD";
            LblProgress.Text = "\u8FDB\u5EA6";
            LblPrecision.Text = "\u7CBE\u786E\u7387";
            LblRecall.Text = "\u53EC\u56DE\u7387";
            LblElapsed.Text = "\u7528\u65F6";
            LblTrainLog.Text = "\uD83D\uDCDD \u8BAD\u7EC3\u65E5\u5FD7";
            ExpanderEpoch.Header = "\uD83D\uDCC8 Epoch \u5386\u53F2";
        }
    }
}