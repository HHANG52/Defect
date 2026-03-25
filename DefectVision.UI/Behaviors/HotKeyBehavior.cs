using Avalonia.Controls;
using Avalonia.Input;
using DefectVision.UI.ViewModels;

namespace DefectVision.UI.Behaviors
{
    public static class HotKeyBehavior
    {
        public static void Attach(Window window, MainWindowViewModel mainVm)
        {
            window.KeyDown += (sender, e) =>
            {
                var annotationVm = mainVm.AnnotationVM;
                if (annotationVm == null) return;

                // Ctrl 组合键
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    switch (e.Key)
                    {
                        case Key.S:
                            annotationVm.SaveAnnotationCommand.Execute(null);
                            e.Handled = true;
                            break;

                        case Key.Z:
                            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                                annotationVm.RedoCommand.Execute(null);
                            else
                                annotationVm.UndoCommand.Execute(null);
                            e.Handled = true;
                            break;

                        case Key.Y:
                            annotationVm.RedoCommand.Execute(null);
                            e.Handled = true;
                            break;

                        case Key.E:
                            annotationVm.ExportDatasetCommand.Execute(null);
                            e.Handled = true;
                            break;
                    }
                    return;
                }

                // 数字键 1-9：切换标注类别
                if (e.Key >= Key.D1 && e.Key <= Key.D9)
                {
                    int classIndex = e.Key - Key.D1;
                    if (classIndex < annotationVm.ClassList.Count)
                    {
                        var cls = annotationVm.ClassList[classIndex];
                        annotationVm.SelectedClassId = cls.Id;
                        annotationVm.SelectedClassName = cls.Name;
                    }
                    e.Handled = true;
                    return;
                }

                // 标注工具切换
                switch (e.Key)
                {
                    case Key.R:
                        annotationVm.IsRectTool = true;
                        annotationVm.IsPolygonTool = false;
                        annotationVm.IsPanTool = false;
                        e.Handled = true;
                        break;

                    case Key.P:
                        annotationVm.IsRectTool = false;
                        annotationVm.IsPolygonTool = true;
                        annotationVm.IsPanTool = false;
                        e.Handled = true;
                        break;

                    case Key.V:
                        annotationVm.IsRectTool = false;
                        annotationVm.IsPolygonTool = false;
                        annotationVm.IsPanTool = true;
                        e.Handled = true;
                        break;

                    case Key.Delete:
                    case Key.Back:
                        annotationVm.DeleteSelectedCommand.Execute(null);
                        e.Handled = true;
                        break;

                    case Key.Space:
                        annotationVm.MarkCompletedCommand.Execute(null);
                        e.Handled = true;
                        break;

                    case Key.A:
                        if (annotationVm.ImageList.Count > 0)
                        {
                            int idx = annotationVm.ImageList.IndexOf(annotationVm.SelectedImage);
                            if (idx > 0)
                                annotationVm.SelectedImage = annotationVm.ImageList[idx - 1];
                        }
                        e.Handled = true;
                        break;

                    case Key.D:
                        if (annotationVm.ImageList.Count > 0)
                        {
                            int idx = annotationVm.ImageList.IndexOf(annotationVm.SelectedImage);
                            if (idx < annotationVm.ImageList.Count - 1)
                                annotationVm.SelectedImage = annotationVm.ImageList[idx + 1];
                        }
                        e.Handled = true;
                        break;
                }
            };
        }
    }
}