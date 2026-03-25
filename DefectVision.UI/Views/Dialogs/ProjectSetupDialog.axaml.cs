using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DefectVision.Core.Models;
using DefectVision.UI.Services;

namespace DefectVision.UI.Views.Dialogs
{
    public partial class ProjectSetupDialog : Window
    {
        public DefectProject Result { get; private set; }
        public string PythonDllPath { get; private set; }
        public string DefaultDevice { get; private set; }

        public ProjectSetupDialog()
        {
            InitializeComponent();
            ApplyLocalizedText();
        }

        /// <summary>
        /// 在代码中设置中文文字，避免 AXAML 编码问题导致乱码
        /// </summary>
        private void ApplyLocalizedText()
        {
            Title = "\u9879\u76EE\u8BBE\u7F6E"; // 项目设置
            LblProjectName.Text = "\u9879\u76EE\u540D\u79F0"; // 项目名称
            ProjectNameBox.Watermark = "\u4F8B\u5982\uFF1A\u5EA7\u6905\u7F3A\u9677\u68C0\u6D4B"; // 例如：座椅缺陷检测

            LblPythonDll.Text = "Python DLL \u8DEF\u5F84"; // Python DLL 路径
            PythonDllBox.Watermark = @"C:\Users\jiyon\anaconda3\envs\yolo\python312.dll";
            LblDllHint.Text = "\u8BF7\u9009\u62E9 Conda \u73AF\u5883\u4E2D\u7684 python3xx.dll"; // 请选择 Conda 环境中的 python3xx.dll
            BtnBrowseDll.Content = "\u6D4F\u89C8"; // 浏览

            LblClasses.Text = "\u7F3A\u9677\u7C7B\u522B\u5B9A\u4E49"; // 缺陷类别定义
            LblClassesHint.Text = "\u6BCF\u884C\u4E00\u4E2A\u7C7B\u522B\uFF0C\u683C\u5F0F\uFF1A\u7C7B\u522B\u540D,\u989C\u8272\u4EE3\u7801"; // 每行一个类别，格式：类别名,颜色代码
            ClassesBox.Watermark = "xiantou,#FF0000\nhuahen,#00FF00\nwuzi,#0000FF";

            LblDevice.Text = "\u9ED8\u8BA4\u63A8\u7406\u8BBE\u5907"; // 默认推理设备

            BtnCancel.Content = "\u53D6\u6D88"; // 取消
            BtnOK.Content = "\u786E\u5B9A"; // 确定
        }

        public void LoadExisting(DefectProject project, string pythonDll)
        {
            ProjectNameBox.Text = project.Name;
            PythonDllBox.Text = pythonDll;

            var lines = new List<string>();
            foreach (var cls in project.Classes)
                lines.Add($"{cls.Name},{cls.Color}");
            ClassesBox.Text = string.Join("\n", lines);
        }

        private async void BrowsePythonDll_Click(object sender, RoutedEventArgs e)
        {
            var path = await DialogService.OpenFileAsync(
                "\u9009\u62E9 Python DLL", "dll"); // 选择 Python DLL
            if (!string.IsNullOrEmpty(path))
                PythonDllBox.Text = path;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            string projectName = ProjectNameBox.Text?.Trim();
            if (string.IsNullOrEmpty(projectName))
            {
                ProjectNameBox.Watermark = "\u26A0 \u8BF7\u8F93\u5165\u9879\u76EE\u540D\u79F0\uFF01"; // ⚠ 请输入项目名称！
                return;
            }

            PythonDllPath = PythonDllBox.Text?.Trim();
            DefaultDevice = DeviceGpu.IsChecked == true ? "0" : "cpu";

            var classes = new List<DefectClass>();
            string classText = ClassesBox.Text?.Trim() ?? "";

            if (!string.IsNullOrEmpty(classText))
            {
                int id = 0;
                foreach (var line in classText.Split('\n'))
                {
                    var parts = line.Trim().Split(',');
                    if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0])) continue;

                    classes.Add(new DefectClass
                    {
                        Id = id++,
                        Name = parts[0].Trim(),
                        Color = parts.Length > 1 ? parts[1].Trim() : GetDefaultColor(id - 1)
                    });
                }
            }

            Result = new DefectProject
            {
                Name = projectName,
                Classes = classes
            };

            Close(true);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close(false);
        }

        private string GetDefaultColor(int index)
        {
            string[] defaults = { "#FF0000", "#00FF00", "#0000FF", "#FFFF00",
                                  "#FF00FF", "#00FFFF", "#FF8000", "#8000FF" };
            return defaults[index % defaults.Length];
        }
    }
}