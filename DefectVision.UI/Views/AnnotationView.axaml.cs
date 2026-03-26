using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DefectVision.Core.Models;
using DefectVision.UI.ViewModels;

namespace DefectVision.UI.Views
{
    public partial class AnnotationView : UserControl
    {
        private bool _isDrawingRect;
        private bool _isDrawingPolygon;
        private bool _isDrawingPolyline;
        private bool _isPanning;
        private Point _startPoint;
        private Point _lastPanPoint;
        private Rectangle _currentRect;

        // 描边点集 自由手绘点集 (用于 Polygon 模式)
        private readonly List<Point> _freehandPoints = [];
        private Polyline _freehandLine;
        // 折线绘制点 点对点绘制点集 (用于 Polyline 模式)
        private readonly List<Point> _activePoints = []; 
        // 用于显示“橡皮筋”预览
        private Line _rubberBandLine;

        private double _scale = 1.0;
        private double _offsetX, _offsetY; // image offset in container coords

        private readonly List<Control> _annotationShapes = new List<Control>();
        private AnnotationViewModel _subscribedVm;

        public AnnotationView()
        {
            InitializeComponent();
            ApplyLocalizedText();
            this.Loaded += OnLoaded;
            this.DataContextChanged += OnDataContextChanged;
        }

        private void OnLoaded(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            CanvasContainer.PropertyChanged += (s, ev) =>
            {
                if (ev.Property.Name == "Bounds" && VM?.CurrentImageSource != null)
                    FitImageToView();
            };
        }

        private void OnDataContextChanged(object sender, EventArgs e)
        {
            if (_subscribedVm != null)
            {
                _subscribedVm.PropertyChanged -= VM_PropertyChanged;
                _subscribedVm.CurrentAnnotations.CollectionChanged -= Annotations_CollectionChanged;
            }

            _subscribedVm = DataContext as AnnotationViewModel;
            if (_subscribedVm != null)
            {
                _subscribedVm.PropertyChanged += VM_PropertyChanged;
                _subscribedVm.CurrentAnnotations.CollectionChanged += Annotations_CollectionChanged;
            }
        }

        private void ApplyLocalizedText()
        {
            LblImageList.Text = "\uD83D\uDCC1 \u56FE\u7247\u5217\u8868";
            BtnImport.Content = "\u5BFC\u5165";
            RunLabeledPrefix.Text = "\u5DF2\u6807\u6CE8: ";
            LblDefectClasses.Text = "\uD83C\uDFF7 \u7F3A\u9677\u7C7B\u522B (\u70B9\u51FB\u9009\u62E9)";
            BtnAddClass.Content = "+ \u6DFB\u52A0\u7C7B\u522B";
            // BtnUndo.Content = null; // We use Path + TextBlock in XAML now
            // BtnRedo.Content = null;
            // BtnDeleteSel.Content = null;
            TxtToolHint.Text = "\u6EDA\u8F6E\u7F29\u653E | \u53F3\u952E\u5E73\u79FB | 1-9 \u5207\u6362";
            RunFileLabel.Text = "\u6587\u4EF6: ";
            RunSizeLabel.Text = "\u5C3A\u5BF8: ";
            RunZoomLabel.Text = "\u7F29\u653E: ";
            RunAnnotListPrefix.Text = "\uD83D\uDCCB \u6807\u6CE8\u5217\u8868 (";
            BtnMarkComplete.Content = "\u2705 \u5B8C\u6210\u5E76\u4E0B\u4E00\u5F20";
            BtnSaveAnnotation.Content = "\uD83D\uDCBE \u4FDD\u5B58\u6807\u6CE8";
            BtnExportYolo.Content = "\uD83D\uDCE4 \u5BFC\u51FA YOLO \u6570\u636E\u96C6";
            BtnImportYolo.Content = "\uD83D\uDCE5 \u5BFC\u5165 YOLO \u6807\u6CE8";
            BtnImportLabelMe.Content = "\uD83D\uDCE6 \u5BFC\u5165 LabelMe \u6570\u636E\u96C6";
            BtnImportCustomJson.Content = "\uD83D\uDCC2 \u5BFC\u5165\u81EA\u5B9A\u4E49 JSON \u6570\u636E\u96C6";
        }

        private AnnotationViewModel VM => DataContext as AnnotationViewModel;

        // ==================== Image fit ====================

        private void VM_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AnnotationViewModel.CurrentImageSource))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    CancelAllDrawing();
                    FitImageToView();
                    RedrawAnnotations();
                });
            }
        }

        /// <summary>
        /// Calculate scale and offset so image fits centered in CanvasContainer
        /// </summary>
        private void FitImageToView()
        {
            var vm = VM;
            if (vm == null || vm.ImageWidth <= 0 || vm.ImageHeight <= 0) return;

            double cw = CanvasContainer.Bounds.Width;
            double ch = CanvasContainer.Bounds.Height;
            if (cw <= 0 || ch <= 0) return;

            double sx = cw / vm.ImageWidth;
            double sy = ch / vm.ImageHeight;
            _scale = Math.Min(sx, sy) * 0.95;

            double imgW = vm.ImageWidth * _scale;
            double imgH = vm.ImageHeight * _scale;
            _offsetX = (cw - imgW) / 2.0;
            _offsetY = (ch - imgH) / 2.0;

            ApplyLayout();
            vm.UpdateZoom(_scale);
        }

        /// <summary>
        /// Apply current scale/offset to the image and annotation canvas
        /// </summary>
        private void ApplyLayout()
        {
            var vm = VM;
            if (vm == null) return;

            // Set image position and size
            double imgW = vm.ImageWidth * _scale;
            double imgH = vm.ImageHeight * _scale;

            CanvasImage.Width = imgW;
            CanvasImage.Height = imgH;
            CanvasImage.Stretch = Avalonia.Media.Stretch.Uniform;

            // Position using Canvas attached properties
            Canvas.SetLeft(CanvasImage, _offsetX);
            Canvas.SetTop(CanvasImage, _offsetY);

            // Canvas itself fills the container (no transform on canvas)
            AnnotationCanvas.RenderTransform = null;
        }

        // ==================== Coordinate conversion ====================

        /// <summary>
        /// Convert screen position (relative to CanvasContainer) to image pixel coordinates
        /// </summary>
        private Point ScreenToImage(Point screenPos)
        {
            double imgX = (screenPos.X - _offsetX) / _scale;
            double imgY = (screenPos.Y - _offsetY) / _scale;
            return new Point(imgX, imgY);
        }

        /// <summary>
        /// Convert image pixel coordinates to screen position (relative to CanvasContainer)
        /// </summary>
        private Point ImageToScreen(double imgX, double imgY)
        {
            double sx = imgX * _scale + _offsetX;
            double sy = imgY * _scale + _offsetY;
            return new Point(sx, sy);
        }

        private Rect ImageRectToScreen(double x, double y, double w, double h)
        {
            var tl = ImageToScreen(x, y);
            return new Rect(tl.X, tl.Y, w * _scale, h * _scale);
        }

        // ==================== Annotation display ====================
        private async void OnImportLabelMeClick(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is AnnotationViewModel vm)
            {
                BtnImportLabelMe.IsEnabled = false;
                try
                {
                    await vm.DoImportLabelMe();
                }
                finally
                {
                    BtnImportLabelMe.IsEnabled = true;
                }
            }
        }

        private async void OnImportCustomJsonClick(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is AnnotationViewModel vm)
            {
                BtnImportCustomJson.IsEnabled = false;
                try
                {
                    await vm.DoImportCustomJson();
                }
                finally
                {
                    BtnImportCustomJson.IsEnabled = true;
                }
            }
        }

        private void Annotations_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(RedrawAnnotations);
        }

        private void RedrawAnnotations()
        {
            foreach (var s in _annotationShapes)
                AnnotationCanvas.Children.Remove(s);
            _annotationShapes.Clear();

            var vm = VM;
            if (vm == null || vm.ImageWidth <= 0) return;

            foreach (var ann in vm.CurrentAnnotations)
                DrawAnnotationBox(ann);
        }

        private void DrawAnnotationBox(AnnotationDisplayItem ann)
        {
            var vm = VM;
            if (vm == null || vm.ImageWidth <= 0) return;

            // 1. 获取颜色
            Color color;
            try
            {
                color = Color.Parse(ann.ClassColor ?? "#FF0000");
            }
            catch
            {
                color = Colors.Red;
            }

            var strokeBrush = new SolidColorBrush(color);
            var fillBrush = new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B));

            // 2. 获取原始数据 (注意：在 VM 中将 _currentAnnotation 改为 public 或添加属性)
            // 如果你还没改 VM，这里可能需要 vm.GetType().GetField("_currentAnnotation", ...).GetValue(vm) 
            // 建议在 VM 中加一个 public ImageAnnotation CurrentAnnotationData => _currentAnnotation;
            var rawData = vm.CurrentAnnotation?.Annotations.FirstOrDefault(a => a.Id == ann.Id);

            Control shape = null;
            Point labelPos = new Point(0, 0);

            // 绘制多边形/描边
            if (rawData != null && rawData.Type == AnnotationType.Polygon && rawData.Polygon != null &&
                rawData.Polygon.Count > 2)
            {
                var screenPoints = new List<Point>();
                foreach (var pt in rawData.Polygon)
                {
                    double px = pt.X * vm.ImageWidth;
                    double py = pt.Y * vm.ImageHeight;
                    screenPoints.Add(ImageToScreen(px, py));
                }

                shape = new Polygon
                {
                    Points = screenPoints,
                    Stroke = strokeBrush,
                    StrokeThickness = 2,
                    Fill = fillBrush,
                    IsHitTestVisible = false
                };
                labelPos = screenPoints[0];
            }
            // 绘制折线
            else if (rawData != null && rawData.Type == AnnotationType.Polyline && rawData.Polygon != null &&
                     rawData.Polygon.Count > 1)
            {
                var screenPoints = new List<Point>();
                foreach (var pt in rawData.Polygon)
                {
                    double px = pt.X * vm.ImageWidth;
                    double py = pt.Y * vm.ImageHeight;
                    screenPoints.Add(ImageToScreen(px, py));
                }

                shape = new Polyline
                {
                    Points = screenPoints,
                    Stroke = strokeBrush,
                    StrokeThickness = 2,
                    IsHitTestVisible = false
                };
                labelPos = screenPoints[0];
            }
            // 绘制矩形 (回退逻辑) ---
            else if (rawData != null && rawData.Type == AnnotationType.BoundingBox)
            {
                var text = ann.BBoxText.Trim('(', ')');
                var parts = text.Split(',');
                if (parts.Length < 4) return;

                if (float.TryParse(parts[0], out float ix) && float.TryParse(parts[1], out float iy) &&
                    float.TryParse(parts[2], out float iw) && float.TryParse(parts[3], out float ih))
                {
                    var screenRect = ImageRectToScreen(ix, iy, iw, ih);
                    shape = new Rectangle
                    {
                        Width = screenRect.Width,
                        Height = screenRect.Height,
                        Stroke = strokeBrush,
                        StrokeThickness = 2,
                        Fill = fillBrush,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(shape, screenRect.X);
                    Canvas.SetTop(shape, screenRect.Y);
                    labelPos = new Point(screenRect.X, screenRect.Y);
                }
            }

            // 3. 将图形和标签添加到画布
            if (shape != null)
            {
                AnnotationCanvas.Children.Add(shape);
                _annotationShapes.Add(shape);

                var label = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(200, color.R, color.G, color.B)),
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(4, 1),
                    IsHitTestVisible = false,
                    Child = new TextBlock { Text = ann.ClassName, FontSize = 11, Foreground = Brushes.White }
                };

                double finalY = labelPos.Y - 18;
                if (finalY < 0) finalY = labelPos.Y + 2;

                Canvas.SetLeft(label, labelPos.X);
                Canvas.SetTop(label, finalY);
                AnnotationCanvas.Children.Add(label);
                _annotationShapes.Add(label);
            }
        }

        // ==================== Pointer events ====================

        private void Canvas_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            var props = e.GetCurrentPoint(CanvasContainer).Properties;

            // 优先判定右键取消绘制
            if (props.IsRightButtonPressed)
            {
                // 如果当前正在“点对点”绘制（集合里有记录点），右键直接取消当前所有临时线段
                if (_activePoints.Count > 0)
                {
                    CancelAllDrawing();
                    SetToolMode("");
                    // 消耗掉这个事件，不触发后续的“右键平移”逻辑
                    e.Handled = true; 
                    return;
                }

                // 如果没有在绘制，右键则维持原有的“平移图片”功能
                _isPanning = true;
                _lastPanPoint = e.GetPosition(CanvasContainer);
                e.Pointer.Capture((IInputElement)sender);
                return;
            }

            if (VM == null) return;

            // Get position relative to container, then convert to image coords
            var screenPos = e.GetPosition(CanvasContainer);
            var imgPos = ScreenToImage(screenPos);

            // 矩形框
            if (VM.IsRectTool)
            {
                _isDrawingRect = true;
                _startPoint = imgPos; // store in image coords
                _currentRect = new Rectangle
                {
                    Stroke = new SolidColorBrush(Colors.Lime),
                    StrokeThickness = 2,
                    Fill = new SolidColorBrush(Color.FromArgb(30, 0, 255, 0)),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(_currentRect, screenPos.X);
                Canvas.SetTop(_currentRect, screenPos.Y);
                AnnotationCanvas.Children.Add(_currentRect);
                e.Pointer.Capture((IInputElement)sender);
                SetToolMode("rect");
            }
            // 描边绘制
            else if (VM.IsPolygonTool)
            {
                _isDrawingPolygon = true;
                _freehandPoints.Clear();
                _freehandPoints.Add(imgPos);

                _freehandLine = new Polyline
                {
                    Stroke = new SolidColorBrush(Colors.Lime),
                    StrokeThickness = 2,
                    IsHitTestVisible = false,
                    Points = new List<Point> { screenPos }
                };
                AnnotationCanvas.Children.Add(_freehandLine);
                e.Pointer.Capture((IInputElement)sender);
                SetToolMode("polygon");
            }
            // 折线绘制
            else if (VM.IsPolylineTool)
            {
                if (_activePoints.Count == 0)
                {
                    _activePoints.Add(imgPos);
                    // 创建完整轮廓线（实线）
                    _freehandLine = new Polyline
                    {
                        Stroke = Brushes.Yellow,
                        StrokeThickness = 2,
                        IsHitTestVisible = false,
                        Points = new List<Point> { screenPos } // 初始只有 A 点
                    };
                    // 创建橡皮筋预览线（虚线）
                    _rubberBandLine = new Line
                    {
                        Stroke = Brushes.Yellow,
                        StrokeThickness = 1,
                        StrokeDashArray = new AvaloniaList<double>(new[] { 4.0, 2.0 }),
                        StartPoint = screenPos, // 从 A 开始
                        EndPoint = screenPos,
                        IsHitTestVisible = false
                    };
        
                    AnnotationCanvas.Children.Add(_freehandLine);
                    AnnotationCanvas.Children.Add(_rubberBandLine);
                    SetToolMode("polyline");
                }
                else
                {   // 点击起始点附近或者双击封口
                    var isClosing = e.ClickCount == 2 || IsNearStartPoint(screenPos);
                    if (isClosing)
                    {
                        // 直接把“第一个点”的坐标加进去，而不是鼠标当前点
                        _activePoints.Add(_activePoints[0]);
                        FinishPolylineDrawing();
                    }
                    else
                    {
                        // === 正常添加中间点 ===
                        _activePoints.Add(imgPos);
                        var newPoints = _activePoints.Select(p => ImageToScreen(p.X, p.Y)).ToList();
                        _freehandLine.Points = newPoints; 
                        _rubberBandLine.StartPoint = screenPos;
                        _rubberBandLine.EndPoint = screenPos;
                    }
                }
            }
        }
        

        private void Canvas_PointerMoved(object sender, PointerEventArgs e)
        {
            var screenPos = e.GetPosition(CanvasContainer);

            if (_isPanning)
            {
                _offsetX += screenPos.X - _lastPanPoint.X;
                _offsetY += screenPos.Y - _lastPanPoint.Y;
                _lastPanPoint = screenPos;
                ApplyLayout();
                RedrawAnnotations();
                UpdateCurrentDrawingPositions();
                return;
            }

            // 矩形更新
            if (_isDrawingRect && _currentRect != null)
            {
                var imgPos = ScreenToImage(screenPos);
                double ix = Math.Min(_startPoint.X, imgPos.X), iy = Math.Min(_startPoint.Y, imgPos.Y);
                double iw = Math.Abs(imgPos.X - _startPoint.X), ih = Math.Abs(imgPos.Y - _startPoint.Y);
                var sr = ImageRectToScreen(ix, iy, iw, ih);
                Canvas.SetLeft(_currentRect, sr.X); Canvas.SetTop(_currentRect, sr.Y);
                _currentRect.Width = sr.Width; _currentRect.Height = sr.Height;
            }

            // 描边绘制
            if (_isDrawingPolygon  && _freehandLine != null)
            {
                var imgPos = ScreenToImage(screenPos);
                if (_freehandPoints.Count == 0 || Distance(imgPos, _freehandPoints.Last()) > 3)
                {
                    _freehandPoints.Add(imgPos);
                    // 同样，重新赋值以确保 UI 刷新
                    _freehandLine.Points = _freehandPoints.Select(p => ImageToScreen(p.X, p.Y)).ToList();
                }
            }
            // 折线绘制
            if (_activePoints.Count > 0 && _rubberBandLine != null)
            {
                _rubberBandLine.EndPoint = screenPos;
            }
        }

        private void Canvas_PointerReleased(object sender, PointerReleasedEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                e.Pointer.Capture(null);
                return;
            }

            if (_isDrawingRect)
            {
                _isDrawingRect = false;
                e.Pointer.Capture(null);
                SubmitRect(e.GetPosition(CanvasContainer));
                SetToolMode("");
            }
            // 描边绘制
            else if (_isDrawingPolygon)
            {
                _isDrawingPolygon = false;
                e.Pointer.Capture(null);
                if (_freehandPoints.Count >= 3)
                    VM?.AddPolygonAnnotation([.. _freehandPoints]);
                CancelAllDrawing();
                SetToolMode("");
            }
        }

        private void Canvas_PointerWheelChanged(object sender, PointerWheelEventArgs e)
        {
            var screenPos = e.GetPosition(CanvasContainer);
            var imgPt = ScreenToImage(screenPos);

            if (e.Delta.Y > 0) _scale = Math.Min(_scale * 1.15, 50);
            else _scale = Math.Max(_scale / 1.15, 0.01);

            _offsetX = screenPos.X - imgPt.X * _scale;
            _offsetY = screenPos.Y - imgPt.Y * _scale;

            ApplyLayout();
            VM?.UpdateZoom(_scale);
            RedrawAnnotations();
            UpdateCurrentDrawingPositions(); // 缩放后重置正在画的线条位置
            e.Handled = true;
        }

        // ==================== Helpers ====================

        private void CancelFreehand()
        {
            _isDrawingPolygon = false;
            _isDrawingPolyline = false;
            if (_freehandLine != null)
            {
                AnnotationCanvas.Children.Remove(_freehandLine);
                _freehandLine = null;
            }

            _freehandPoints.Clear();
        }

        private void SetToolMode(string mode)
        {
            if (TxtToolMode == null) return;
            if (mode == "rect")
                TxtToolMode.Text = "[\u62D6\u62FD\u753B\u6846...]";
            else if (mode == "polygon")
                TxtToolMode.Text = "[\u62D6\u62FD\u753B\u63CF\u8FB9...]";
            else if (mode == "polyline")
                TxtToolMode.Text = "[\u62D6\u62FD\u753B\u6298\u7EBF...]";
            else
                TxtToolMode.Text = "";
        }
        // 判断是否在起始点附近
        private bool IsNearStartPoint(Point currentScreenPos)
        {
            if (_activePoints.Count < 2) return false;
            var startScreen = ImageToScreen(_activePoints[0].X, _activePoints[0].Y);
            return Distance(currentScreenPos, startScreen) < 15; // 15像素磁吸距离
        }
        
        private void UpdateCurrentDrawingPositions()
        {
            // 更新点对点模式的线条位置
            if (_activePoints.Count > 0 && _freehandLine != null)
            {
                // 将所有已确定的图片像素点重新转换成当前缩放下的屏幕坐标
                var newScreenPoints = _activePoints.Select(p => ImageToScreen(p.X, p.Y)).ToList();
                // 更新实线轮廓
                _freehandLine.Points = newScreenPoints;
                // 更新虚线起点（即最后一个确定的点）
                if (_rubberBandLine != null)
                {
                    _rubberBandLine.StartPoint = newScreenPoints.Last();
                }
            }
        }
        
        private void FinishPolylineDrawing()
        {
            if (_activePoints.Count >= 2)
            {
                VM?.AddPolylineAnnotation([.. _activePoints]);
            }
            CancelAllDrawing();
            SetToolMode("");
        }
        private double Distance(Point p1, Point p2) => Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
        //
        private void SubmitRect(Point releasePos)
        {
            var imgPos = ScreenToImage(releasePos);
            double ix = Math.Max(0, Math.Min(_startPoint.X, imgPos.X));
            double iy = Math.Max(0, Math.Min(_startPoint.Y, imgPos.Y));
            double iw = Math.Abs(imgPos.X - _startPoint.X);
            double ih = Math.Abs(imgPos.Y - _startPoint.Y);

            if (iw > 3 && ih > 3 && VM != null)
            {
                if (ix + iw > VM.ImageWidth) iw = VM.ImageWidth - ix;
                if (iy + ih > VM.ImageHeight) ih = VM.ImageHeight - iy;
                VM.AddAnnotation(ix, iy, iw, ih);
            }
            CancelAllDrawing();
        }
        
        // 取消所有正在绘制的
        private void CancelAllDrawing()
        {
            _isDrawingRect = false;
            _isDrawingPolygon = false;
    
            AnnotationCanvas.Children.Remove(_freehandLine);
            AnnotationCanvas.Children.Remove(_rubberBandLine);
            AnnotationCanvas.Children.Remove(_currentRect);

            _freehandLine = null;
            _rubberBandLine = null;
            _currentRect = null;
            _freehandPoints.Clear();
            _activePoints.Clear();
        }
    }
}