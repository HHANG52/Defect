using System;
using System.Collections.Generic;

namespace DefectVision.Core.Models
{
    /// <summary>
    /// 一张图片的标注数据
    /// </summary>
    public class ImageAnnotation
    {
        public string ImagePath { get; set; } = "";
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }
        public List<Annotation> Annotations { get; set; } = new List<Annotation>();
        public AnnotationStatus Status { get; set; } = AnnotationStatus.Unlabeled;
        public DateTime LastModified { get; set; } = DateTime.Now;
    }

    public enum AnnotationStatus
    {
        Unlabeled,    // 未标注
        InProgress,   // 标注中
        Completed,    // 已完成
        Reviewed      // 已审核
    }

    /// <summary>
    /// 单个标注（一个缺陷框/多边形）
    /// </summary>
    public class Annotation
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public int ClassId { get; set; }
        public string ClassName { get; set; } = "";
        public AnnotationType Type { get; set; } = AnnotationType.BoundingBox;

        /// <summary>矩形框（归一化坐标 0~1）</summary>
        public NormalizedRect BBox { get; set; } = new NormalizedRect();

        /// <summary>多边形点（归一化坐标 0~1）</summary>
        public List<NormalizedPoint> Polygon { get; set; } = new List<NormalizedPoint>();

        /// <summary>是否难例</summary>
        public bool IsDifficult { get; set; }
    }

    public enum AnnotationType
    {
        BoundingBox,
        Polygon
    }

    /// <summary>归一化矩形（YOLO 格式：中心点 + 宽高）</summary>
    public class NormalizedRect
    {
        public float CenterX { get; set; }
        public float CenterY { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }

        /// <summary>转换为像素坐标的左上角矩形</summary>
        public (float x, float y, float w, float h) ToPixel(int imgWidth, int imgHeight)
        {
            float w = Width * imgWidth;
            float h = Height * imgHeight;
            float x = CenterX * imgWidth - w / 2;
            float y = CenterY * imgHeight - h / 2;
            return (x, y, w, h);
        }

        /// <summary>从像素坐标的左上角矩形创建</summary>
        public static NormalizedRect FromPixel(float x, float y, float w, float h, int imgWidth, int imgHeight)
        {
            return new NormalizedRect
            {
                CenterX = (x + w / 2) / imgWidth,
                CenterY = (y + h / 2) / imgHeight,
                Width = w / imgWidth,
                Height = h / imgHeight
            };
        }
    }

    public class NormalizedPoint
    {
        public float X { get; set; }
        public float Y { get; set; }
    }
}