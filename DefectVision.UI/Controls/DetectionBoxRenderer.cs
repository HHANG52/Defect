using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DefectVision.Core.Models;

namespace DefectVision.UI.Controls
{
    /// <summary>
    /// 在图片上绘制检测框和标签的 Avalonia 控件
    /// 同时用于标注模式和检测结果显示
    /// </summary>
    public class DetectionBoxRenderer : Control
    {
        // ===== 检测结果 =====
        public static readonly StyledProperty<List<DetectedObject>> DetectionsProperty =
            AvaloniaProperty.Register<DetectionBoxRenderer, List<DetectedObject>>(nameof(Detections));

        public List<DetectedObject> Detections
        {
            get => GetValue(DetectionsProperty);
            set => SetValue(DetectionsProperty, value);
        }

        // ===== 标注数据（标注模式用） =====
        public static readonly StyledProperty<List<AnnotationRenderItem>> AnnotationsProperty =
            AvaloniaProperty.Register<DetectionBoxRenderer, List<AnnotationRenderItem>>(nameof(Annotations));

        public List<AnnotationRenderItem> Annotations
        {
            get => GetValue(AnnotationsProperty);
            set => SetValue(AnnotationsProperty, value);
        }

        // ===== 图片原始尺寸 =====
        public static readonly StyledProperty<double> ImagePixelWidthProperty =
            AvaloniaProperty.Register<DetectionBoxRenderer, double>(nameof(ImagePixelWidth), 1.0);

        public double ImagePixelWidth
        {
            get => GetValue(ImagePixelWidthProperty);
            set => SetValue(ImagePixelWidthProperty, value);
        }

        public static readonly StyledProperty<double> ImagePixelHeightProperty =
            AvaloniaProperty.Register<DetectionBoxRenderer, double>(nameof(ImagePixelHeight), 1.0);

        public double ImagePixelHeight
        {
            get => GetValue(ImagePixelHeightProperty);
            set => SetValue(ImagePixelHeightProperty, value);
        }

        // ===== 选中项高亮 =====
        public static readonly StyledProperty<string> SelectedIdProperty =
            AvaloniaProperty.Register<DetectionBoxRenderer, string>(nameof(SelectedId), "");

        public string SelectedId
        {
            get => GetValue(SelectedIdProperty);
            set => SetValue(SelectedIdProperty, value);
        }

        // 预定义颜色
        private static readonly Color[] BoxColors =
        {
            Colors.Red, Colors.Lime, Colors.DeepSkyBlue, Colors.Yellow,
            Colors.Magenta, Colors.Orange, Colors.Cyan, Colors.HotPink,
            Colors.Gold, Colors.SpringGreen, Colors.Tomato, Colors.Violet
        };

        static DetectionBoxRenderer()
        {
            AffectsRender<DetectionBoxRenderer>(
                DetectionsProperty, AnnotationsProperty,
                ImagePixelWidthProperty, ImagePixelHeightProperty,
                SelectedIdProperty);
        }

        public override void Render(DrawingContext dc)
        {
            base.Render(dc);

            double controlW = Bounds.Width;
            double controlH = Bounds.Height;
            double imgW = ImagePixelWidth;
            double imgH = ImagePixelHeight;

            if (controlW <= 0 || controlH <= 0 || imgW <= 0 || imgH <= 0) return;

            // Uniform 缩放
            double scaleX = controlW / imgW;
            double scaleY = controlH / imgH;
            double scale = Math.Min(scaleX, scaleY);
            double renderW = imgW * scale;
            double renderH = imgH * scale;
            double offsetX = (controlW - renderW) / 2.0;
            double offsetY = (controlH - renderH) / 2.0;

            // 绘制检测结果
            var detections = Detections;
            if (detections != null)
            {
                foreach (var det in detections)
                {
                    DrawBox(dc, det.X, det.Y, det.Width, det.Height,
                        det.ClassId, $"{det.ClassName} {det.Score:F2}",
                        scale, offsetX, offsetY, false);
                }
            }

            // 绘制标注
            var annotations = Annotations;
            if (annotations != null)
            {
                foreach (var ann in annotations)
                {
                    bool isSelected = ann.Id == SelectedId;
                    DrawBox(dc, ann.X, ann.Y, ann.Width, ann.Height,
                        ann.ClassId, ann.Label, scale, offsetX, offsetY, isSelected);
                }
            }
        }

        private void DrawBox(DrawingContext dc,
            float bx, float by, float bw, float bh,
            int classId, string label,
            double scale, double offsetX, double offsetY,
            bool isSelected)
        {
            double rx = bx * scale + offsetX;
            double ry = by * scale + offsetY;
            double rw = bw * scale;
            double rh = bh * scale;

            Color color = BoxColors[classId % BoxColors.Length];
            double lineWidth = isSelected ? 3.0 : 2.0;

            var pen = new Pen(new SolidColorBrush(color), lineWidth);

            // 选中时加虚线高亮
            if (isSelected)
            {
                var highlightPen = new Pen(Brushes.White, lineWidth + 2);
                dc.DrawRectangle(null, highlightPen, new Rect(rx, ry, rw, rh));
            }

            // 画矩形框
            dc.DrawRectangle(null, pen, new Rect(rx, ry, rw, rh));

            // 标签
            if (!string.IsNullOrEmpty(label))
            {
                var formattedText = new FormattedText(
                    label,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Microsoft YaHei", FontStyle.Normal, FontWeight.Normal),
                    12, Brushes.White);

                double labelW = formattedText.Width + 6;
                double labelH = formattedText.Height + 2;
                double labelY = ry - labelH;
                if (labelY < 0) labelY = ry;

                var bgBrush = new SolidColorBrush(Color.FromArgb(200, color.R, color.G, color.B));
                dc.DrawRectangle(bgBrush, null, new Rect(rx, labelY, labelW, labelH));
                dc.DrawText(formattedText, new Point(rx + 3, labelY + 1));
            }
        }
    }

    /// <summary>
    /// 标注渲染项（标注模式用）
    /// </summary>
    public class AnnotationRenderItem
    {
        public string Id { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public int ClassId { get; set; }
        public string Label { get; set; } = "";
    }
}