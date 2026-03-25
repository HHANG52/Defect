using Avalonia.Controls;
using DefectVision.UI.Behaviors;
using DefectVision.UI.ViewModels;

namespace DefectVision.UI.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainWindowViewModel();
            DataContext = _viewModel;

            // 挂载全局快捷键
            HotKeyBehavior.Attach(this, _viewModel);

            // 在代码中设置中文文字，避免 AXAML 编码乱码
            ApplyLocalizedText();
        }

        private void ApplyLocalizedText()
        {
            // 窗口标题
            Title = "DefectVision - 目标检测训练标注软件";
            // 缺陷检测一体化平台

            // 顶部按钮
            BtnOpenProject.Content = "\uD83D\uDCC2 \u6253\u5F00\u9879\u76EE";     // 打开项目
            BtnNewProject.Content = "\u2795 \u65B0\u5EFA\u9879\u76EE";             // 新建项目
            BtnPythonSetup.Content = "\u2699 Python \u8BBE\u7F6E";                 // Python 设置

            // Tab 页标题
            TabAnnotation.Header = "\uD83D\uDCD0 \u6807\u6CE8";                   // 标注
            TabTraining.Header = "\uD83C\uDFCB \u8BAD\u7EC3";                     // 训练
            TabModel.Header = "\uD83D\uDCE6 \u6A21\u578B";                        // 模型
            TabDetection.Header = "\uD83D\uDD0D \u68C0\u6D4B";                    // 检测

            // 状态栏
            LblModel.Text = "\u5F53\u524D\u6A21\u578B: ";                          // 当前模型:
            LblGpu.Text = "GPU: ";
        }
    }
}