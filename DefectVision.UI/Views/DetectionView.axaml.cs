using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace DefectVision.UI.Views
{
    public partial class DetectionView : UserControl
    {
        private double _scale = 1.0;
        private double _tx, _ty;
        private bool _panning;
        private Point _lastPan;

        private readonly ScaleTransform _scaleTransform = new ScaleTransform(1, 1);
        private readonly TranslateTransform _translateTransform = new TranslateTransform(0, 0);

        public DetectionView()
        {
            InitializeComponent();

            DetectionCanvas.RenderTransform = new TransformGroup
            {
                Children = { _scaleTransform, _translateTransform }
            };
        }

        private void Image_PointerWheelChanged(object sender, PointerWheelEventArgs e)
        {
            var pos = e.GetPosition(this);
            double old = _scale;
            _scale = e.Delta.Y > 0 ? Math.Min(_scale * 1.15, 30) : Math.Max(_scale / 1.15, 0.3);
            double r = _scale / old;
            _tx = pos.X - (pos.X - _tx) * r;
            _ty = pos.Y - (pos.Y - _ty) * r;
            ApplyTransform();
            e.Handled = true;
        }

        private void Image_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            var props = e.GetCurrentPoint(this).Properties;
            if (props.IsLeftButtonPressed)
            {
                _panning = true;
                _lastPan = e.GetPosition(this);
                e.Pointer.Capture((IInputElement)sender);
            }
            else if (props.IsRightButtonPressed)
            {
                _scale = 1; _tx = 0; _ty = 0;
                ApplyTransform();
            }
        }

        private void Image_PointerMoved(object sender, PointerEventArgs e)
        {
            if (!_panning) return;
            var p = e.GetPosition(this);
            _tx += p.X - _lastPan.X;
            _ty += p.Y - _lastPan.Y;
            _lastPan = p;
            ApplyTransform();
        }

        private void Image_PointerReleased(object sender, PointerReleasedEventArgs e)
        {
            _panning = false;
            e.Pointer.Capture(null);
        }

        private void ApplyTransform()
        {
            _scaleTransform.ScaleX = _scale;
            _scaleTransform.ScaleY = _scale;
            _translateTransform.X = _tx;
            _translateTransform.Y = _ty;
        }
    }
}