using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DefectVision.Core.Models;
using DefectVision.Core.Services;
using DefectVision.UI.Services;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using BitMiracle.LibTiff.Classic;
using SkiaSharp;

namespace DefectVision.UI.ViewModels
{
    public partial class AnnotationViewModel : ObservableObject
    {
        private AnnotationService _annotationService;
        private DefectProject _project;

        // 撤销/重做栈
        private readonly Stack<UndoAction> _undoStack = new();
        private readonly Stack<UndoAction> _redoStack = new();

        // ===== 图片列表 =====
        public ObservableCollection<ImageListItem> ImageList { get; } = [];

        [ObservableProperty] private ImageListItem _selectedImage;
        [ObservableProperty] private int _labeledCount;
        [ObservableProperty] private int _totalCount;
        [ObservableProperty] private double _labelProgress;

        // ===== 当前图片 =====
        [ObservableProperty] private Bitmap _currentImageSource;
        [ObservableProperty] private string _currentFileName = "";
        [ObservableProperty] private string _imageSizeText = "";
        [ObservableProperty] private string _zoomPercentText = "100%";
        [ObservableProperty] private int _imageWidth;
        [ObservableProperty] private int _imageHeight;

        // ===== 标注工具 =====
        [ObservableProperty] private bool _isRectTool = true;
        [ObservableProperty] private bool _isPolygonTool;
        [ObservableProperty] private bool _isPanTool;
        [ObservableProperty] private string _selectedClassName = "defect";

        [ObservableProperty] private string _statusMessage = "";

        // ===== 类别 =====
        public ObservableCollection<ClassDisplayItem> ClassList { get; } = [];
        [ObservableProperty] private int _selectedClassId;

        [ObservableProperty] private ClassDisplayItem _selectedClassItem;

        partial void OnSelectedClassItemChanged(ClassDisplayItem value)
        {
            if (value == null) return;
            SelectedClassId = value.Id;
            SelectedClassName = value.Name;
        }

        // ===== 当前标注 =====
        public ObservableCollection<AnnotationDisplayItem> CurrentAnnotations { get; } = new();

        [ObservableProperty] private AnnotationDisplayItem _selectedAnnotation;
        [ObservableProperty] private ImageAnnotation _currentAnnotation;

        public AnnotationViewModel()
        {
        }

        public void Initialize(DefectProject project)
        {
            _project = project;
            _annotationService = new AnnotationService(project);

            ClassList.Clear();
            for (int i = 0; i < project.Classes.Count; i++)
            {
                var cls = project.Classes[i];
                ClassList.Add(new ClassDisplayItem
                {
                    Id = cls.Id,
                    Name = cls.Name,
                    Color = cls.Color,
                    Shortcut = $"{i + 1}"
                });
            }

            if (ClassList.Count > 0)
            {
                SelectedClassItem = ClassList[0]; // ★ 设置默认选中项
                SelectedClassId = ClassList[0].Id;
                SelectedClassName = ClassList[0].Name;
            }
            else
            {
                SelectedClassId = 0;
                SelectedClassName = "defect";
            }

            ScanExistingImages();
        }

        /// <summary>
        /// Import LabelMe format dataset (json + images)
        /// </summary>
        public async Task DoImportLabelMe()
        {
            if (_project == null)
            {
                StatusMessage = "\u8BF7\u5148\u521B\u5EFA\u6216\u6253\u5F00\u9879\u76EE";
                return;
            }

            // Select folder containing LabelMe json + images
            string folder = await DialogService.OpenFolderAsync(
                "\u9009\u62E9 LabelMe \u6570\u636E\u96C6\u6587\u4EF6\u5939");
            if (string.IsNullOrEmpty(folder)) return;

            StatusMessage = "\u6B63\u5728\u5BFC\u5165 LabelMe \u6570\u636E\u96C6...";

            try
            {
                var converter = new LabelMeToYoloConverter();
                var result = converter.Convert(folder, _project);

                // Save data.yaml path to project config
                if (!string.IsNullOrEmpty(result.DataYamlPath))
                {
                    string configPath = Path.Combine(_project.RootPath, "project.json");
                    if (File.Exists(configPath))
                    {
                        var projectJson = Newtonsoft.Json.Linq.JObject.Parse(
                            File.ReadAllText(configPath));
                        projectJson["DataYamlPath"] = result.DataYamlPath;
                        File.WriteAllText(configPath,
                            projectJson.ToString(Newtonsoft.Json.Formatting.Indented));
                    }
                    else
                    {
                        // Create new project.json
                        var projectJson = Newtonsoft.Json.Linq.JObject.FromObject(_project);
                        projectJson["DataYamlPath"] = result.DataYamlPath;
                        File.WriteAllText(configPath,
                            projectJson.ToString(Newtonsoft.Json.Formatting.Indented));
                    }
                }

                // Update project classes if new ones found
                if (result.ClassNames.Count > 0)
                {
                    _project.Classes.Clear();
                    string[] defaultColors =
                    {
                        "#FF0000", "#00FF00", "#0000FF", "#FFFF00",
                        "#FF00FF", "#00FFFF", "#FF8000", "#8000FF",
                        "#FF4444", "#44FF44", "#4444FF", "#FFAA00"
                    };

                    for (int i = 0; i < result.ClassNames.Count; i++)
                    {
                        _project.Classes.Add(new DefectClass
                        {
                            Id = i,
                            Name = result.ClassNames[i],
                            Color = defaultColors[i % defaultColors.Length]
                        });
                    }

                    // Save updated project with new classes
                    string configPath = Path.Combine(_project.RootPath, "project.json");
                    File.WriteAllText(configPath,
                        Newtonsoft.Json.JsonConvert.SerializeObject(_project,
                            Newtonsoft.Json.Formatting.Indented));
                }

                // Build status message
                StatusMessage = $"\u2705 \u5BFC\u5165\u5B8C\u6210: {result.ImageCount} \u5F20\u56FE\u7247, " +
                                $"{result.AnnotationCount} \u4E2A\u6807\u6CE8, " +
                                $"{result.ClassNames.Count} \u4E2A\u7C7B\u522B " +
                                $"(train:{result.TrainCount} val:{result.ValCount})";

                if (result.Errors.Count > 0)
                {
                    StatusMessage += $"\n\u26A0 {result.Errors.Count} \u4E2A\u6587\u4EF6\u89E3\u6790\u5931\u8D25";
                    foreach (var err in result.Errors.Take(5))
                        StatusMessage += $"\n  - {err}";
                }

                // Refresh UI - reload classes and image list
                Initialize(_project);
            }
            catch (Exception ex)
            {
                StatusMessage = $"\u274C \u5BFC\u5165\u5931\u8D25: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[LabelMe Import] {ex}");
            }
        }

        /// <summary>
        /// Import custom JSON polygon dataset
        /// </summary>
        public async Task DoImportCustomJson()
        {
            if (_project == null)
            {
                StatusMessage = "请先创建或打开项目";
                return;
            }

            // 1. 选择 JSON 标注文件夹
            string jsonFolder = await DialogService.OpenFolderAsync(
                "第1步：选择 JSON 标注文件夹");
            if (string.IsNullOrEmpty(jsonFolder)) return;

            // 2. 检查是否需要单独指定图片目录
            string[] imgExts = { "*.bmp", "*.jpg", "*.jpeg", "*.png", "*.tif", "*.tiff" };
            bool hasImagesInSameDir = imgExts.Any(ext =>
                Directory.GetFiles(jsonFolder, ext, SearchOption.AllDirectories).Length > 0);

            string imageFolder = null;

            if (!hasImagesInSameDir)
            {
                // 尝试自动查找
                string parentDir = Path.GetDirectoryName(jsonFolder);
                string[] commonSubDirs = { "SrcImage", "images", "Images", "imgs", "Src" };
                foreach (var sub in commonSubDirs)
                {
                    string candidate = Path.Combine(parentDir ?? "", sub);
                    if (Directory.Exists(candidate))
                    {
                        bool hasImages = imgExts.Any(ext =>
                            Directory.GetFiles(candidate, ext).Length > 0);
                        if (hasImages)
                        {
                            imageFolder = candidate;
                            break;
                        }
                    }
                }

                if (imageFolder == null)
                {
                    imageFolder = await DialogService.OpenFolderAsync(
                        "第2步：JSON 目录中没有找到图片，请选择图片文件夹");
                    if (string.IsNullOrEmpty(imageFolder)) return;
                }
            }

            StatusMessage = "正在导入自定义 JSON 数据集...";

            try
            {
                var converter = new CustomJsonToYoloConverter();
                var result = converter.Convert(
                    jsonFolder,
                    imageFolder,
                    _project);

                // 更新项目类别
                if (result.ClassNames.Count > 0)
                {
                    _project.Classes.Clear();
                    string[] defaultColors =
                    {
                        "#FF0000", "#00FF00", "#0000FF", "#FFFF00",
                        "#FF00FF", "#00FFFF", "#FF8000", "#8000FF",
                        "#FF4444", "#44FF44", "#4444FF", "#FFAA00"
                    };

                    for (int i = 0; i < result.ClassNames.Count; i++)
                    {
                        _project.Classes.Add(new DefectClass
                        {
                            Id = i,
                            Name = result.ClassNames[i],
                            Color = defaultColors[i % defaultColors.Length]
                        });
                    }
                }

                // 保存项目配置
                if (!string.IsNullOrEmpty(result.DataYamlPath))
                {
                    string configPath = Path.Combine(_project.RootPath, "project.json");
                    Newtonsoft.Json.Linq.JObject projectJson;

                    if (File.Exists(configPath))
                        projectJson = Newtonsoft.Json.Linq.JObject.Parse(
                            File.ReadAllText(configPath));
                    else
                        projectJson = Newtonsoft.Json.Linq.JObject.FromObject(_project);

                    projectJson["DataYamlPath"] = result.DataYamlPath;
                    File.WriteAllText(configPath,
                        projectJson.ToString(Newtonsoft.Json.Formatting.Indented));
                }

                StatusMessage = $"✅ 导入完成: {result.ImageCount} 张图片, " +
                                $"{result.AnnotationCount} 个标注, " +
                                $"{result.ClassNames.Count} 个类别 " +
                                $"(train:{result.TrainCount} val:{result.ValCount})";

                if (result.Errors.Count > 0)
                {
                    StatusMessage += $"\n⚠ {result.Errors.Count} 个文件处理失败";
                    foreach (var err in result.Errors.Take(5))
                        StatusMessage += $"\n  - {err}";
                }

                // ★ 刷新界面，重新加载标注
                Initialize(_project);
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ 导入失败: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[CustomJson Import] {ex}");
            }
        }


        /// <summary>
        /// 扫描项目中已有的图片
        /// </summary>
        private void ScanExistingImages()
        {
            ImageList.Clear();
            if (_project == null) return;

            string annotationsDir = Path.Combine(_project.RootPath, "annotations");
            string imagesRawDir = Path.Combine(_project.RootPath, "images_raw");

            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };

            // 优先从 images_raw 目录加载
            if (Directory.Exists(imagesRawDir))
            {
                foreach (var file in Directory.GetFiles(imagesRawDir).OrderBy(f => f))
                {
                    if (!extensions.Contains(Path.GetExtension(file))) continue;
                    AddImageToList(file);
                }
            }

            // 也从 train/val 目录加载
            LoadImagesFromDir(_project.ImagesTrainDir, extensions);
            LoadImagesFromDir(_project.ImagesValDir, extensions);

            UpdateProgress();
        }

        private void LoadImagesFromDir(string dir, HashSet<string> extensions)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.GetFiles(dir).OrderBy(f => f))
            {
                if (!extensions.Contains(Path.GetExtension(file))) continue;
                // 避免重复
                if (ImageList.Any(i => i.FullPath == file)) continue;
                AddImageToList(file);
            }
        }

        private void AddImageToList(string filePath)
        {
            var ann = _annotationService?.LoadAnnotation(filePath);
            ImageList.Add(new ImageListItem
            {
                FullPath = filePath,
                AnnotationCount = ann?.Annotations.Count ?? 0,
                Status = ann?.Status ?? AnnotationStatus.Unlabeled
            });
        }

        // ===== 导入图片 =====
        [RelayCommand]
        private async Task ImportImages()
        {
            var files = await DialogService.OpenFilesAsync(
                "\u9009\u62E9\u56FE\u7247", // 选择图片
                "jpg", "jpeg", "png", "bmp", "tif", "tiff");

            if (files == null || files.Count == 0) return;

            // 复制到项目的 images_raw 目录
            string rawDir = Path.Combine(_project.RootPath, "images_raw");
            Directory.CreateDirectory(rawDir);

            foreach (var srcFile in files)
            {
                string fileName = Path.GetFileName(srcFile);
                string destFile = Path.Combine(rawDir, fileName);

                // 避免覆盖
                if (File.Exists(destFile))
                {
                    string nameOnly = Path.GetFileNameWithoutExtension(fileName);
                    string ext = Path.GetExtension(fileName);
                    destFile = Path.Combine(rawDir, $"{nameOnly}_{DateTime.Now:HHmmss}{ext}");
                }

                File.Copy(srcFile, destFile, false);

                // 添加到列表
                if (!ImageList.Any(i => i.FullPath == destFile))
                {
                    ImageList.Add(new ImageListItem
                    {
                        FullPath = destFile,
                        AnnotationCount = 0,
                        Status = AnnotationStatus.Unlabeled
                    });
                }
            }

            TotalCount = ImageList.Count;
            UpdateProgress();
        }

        // ===== 添加类别 =====
        [RelayCommand]
        private void AddClass()
        {
            int newId = ClassList.Count > 0 ? ClassList.Max(c => c.Id) + 1 : 0;
            string[] defaultColors =
            {
                "#FF0000", "#00FF00", "#0000FF", "#FFFF00",
                "#FF00FF", "#00FFFF", "#FF8000", "#8000FF"
            };
            string color = defaultColors[newId % defaultColors.Length];

            string name = $"class_{newId}";

            ClassList.Add(new ClassDisplayItem
            {
                Id = newId,
                Name = name,
                Color = color,
                Shortcut = $"{newId + 1}"
            });

            // 同步到项目
            _project.Classes.Add(new DefectClass
            {
                Id = newId,
                Name = name,
                Color = color
            });
        }

        public void AddAnnotation(double x, double y, double w, double h)
        {
            if (_currentAnnotation == null || ImageWidth <= 0 || ImageHeight <= 0) return;

            var bbox = NormalizedRect.FromPixel((float)x, (float)y, (float)w, (float)h, ImageWidth, ImageHeight);

            // Use selected class info
            string className = SelectedClassName;
            string classColor = "#FF0000";

            if (_selectedClassItem != null)
            {
                className = _selectedClassItem.Name;
                classColor = _selectedClassItem.Color;
            }
            else
            {
                var cls = _project?.Classes.FirstOrDefault(c => c.Id == SelectedClassId);
                if (cls != null)
                {
                    className = cls.Name;
                    classColor = cls.Color;
                }
            }

            var ann = new Annotation
            {
                ClassId = SelectedClassId,
                ClassName = className,
                Type = AnnotationType.BoundingBox,
                BBox = bbox
            };

            _currentAnnotation.Annotations.Add(ann);
            _currentAnnotation.Status = AnnotationStatus.InProgress;

            CurrentAnnotations.Add(new AnnotationDisplayItem
            {
                Id = ann.Id,
                ClassName = className,
                ClassColor = classColor,
                BBoxText = $"({x:F0}, {y:F0}, {w:F0}, {h:F0})"
            });

            _undoStack.Push(new UndoAction { Type = UndoType.Add, AnnotationId = ann.Id });
            _redoStack.Clear();

            _annotationService.SaveAnnotation(_currentAnnotation);

            if (_selectedImage != null)
                _selectedImage.AnnotationCount = _currentAnnotation.Annotations.Count;
        }

        public void AddPolygonAnnotation(List<Point> polygonPoints)
        {
            if (_currentAnnotation == null || ImageWidth <= 0 || ImageHeight <= 0 || polygonPoints.Count < 3) return;

            string className = SelectedClassName;
            string classColor = "#FF0000";

            if (_selectedClassItem != null)
            {
                className = _selectedClassItem.Name;
                classColor = _selectedClassItem.Color;
            }
            else
            {
                var cls = _project?.Classes.FirstOrDefault(c => c.Id == SelectedClassId);
                if (cls != null)
                {
                    className = cls.Name;
                    classColor = cls.Color;
                }
            }

            var ann = new Annotation
            {
                ClassId = SelectedClassId,
                ClassName = className,
                Type = AnnotationType.Polygon,
                Polygon = polygonPoints.Select(p => 
                    new NormalizedPoint 
                    { 
                        X = (float)(p.X / ImageWidth), 
                        Y = (float)(p.Y / ImageHeight) 
                    }).ToList()
            };

            _currentAnnotation.Annotations.Add(ann);
            _currentAnnotation.Status = AnnotationStatus.InProgress;

            var (minX, minY, w, h) = GetPolygonBoundingBox(polygonPoints);
            CurrentAnnotations.Add(new AnnotationDisplayItem
            {
                Id = ann.Id,
                ClassName = className,
                ClassColor = classColor,
                BBoxText = $"多边形 ({w:F0}×{h:F0})"
            });

            _undoStack.Push(new UndoAction { Type = UndoType.Add, AnnotationId = ann.Id });
            _redoStack.Clear();

            _annotationService.SaveAnnotation(_currentAnnotation);

            if (_selectedImage != null)
                _selectedImage.AnnotationCount = _currentAnnotation.Annotations.Count;
        }

        private (double minX, double minY, double w, double h) GetPolygonBoundingBox(List<Avalonia.Point> points)
        {
            double minX = points.Min(p => p.X);
            double minY = points.Min(p => p.Y);
            double maxX = points.Max(p => p.X);
            double maxY = points.Max(p => p.Y);
            return (minX, minY, maxX - minX, maxY - minY);
        }


        public void UpdateZoom(double scale)
        {
            ZoomPercentText = $"{scale * 100:F0}%";
        }

        partial void OnSelectedImageChanged(ImageListItem value)
        {
            if (value == null) return;
            LoadImage(value.FullPath);
        }

        // ===== 加载图片 =====
        private void LoadImage(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

                Bitmap finalBitmap = null;
                var ext = Path.GetExtension(path).ToLower(); 
                // --- 专门针对 TIF 的处理逻辑 ---
                if (ext is ".tif" or ".tiff")
                {
                    using Tiff tiff = Tiff.Open(path, "r");
                    if (tiff == null) throw new Exception("无法打开 TIF 文件");

                    var width = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
                    var height = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
                    var raster = new int[width * height];

                    // 读取 RGBA 像素
                    if (tiff.ReadRGBAImage(width, height, raster))
                    {
                        finalBitmap = LoadTiffFast(path);
                    }
                }
                else
                {
                    // --- 针对 BMP, JPG, PNG 的原生高速通道 ---
                    // 直接使用 FileStream，不经过 SkiaSharp 转换，避免二次编解码
                    using var stream = File.OpenRead(path);
                    // 这种方式加载 BMP 和 JPG 是最快的，因为它直接解码到渲染格式
                    finalBitmap = new Bitmap(stream);
                }

                // --- 更新 UI 状态 ---
                if (finalBitmap != null)
                {
                    CurrentImageSource?.Dispose();
                    CurrentImageSource = finalBitmap;
                    ImageWidth = CurrentImageSource.PixelSize.Width;
                    ImageHeight = CurrentImageSource.PixelSize.Height;
                    CurrentFileName = Path.GetFileName(path);
                    ImageSizeText = $"{ImageWidth} × {ImageHeight}";

                    // 重新加载标注
                    LoadAnnotationOverlay(path);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $" 加载失败: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[LoadImage Error] {ex}");
            }
        }

        // 标注显示逻辑
        private void LoadAnnotationOverlay(string path)
        {
            _currentAnnotation = _annotationService.LoadAnnotation(path);
            CurrentAnnotations.Clear();
            foreach (var ann in _currentAnnotation.Annotations)
            {
                var (px, py, pw, ph) = ann.BBox.ToPixel(ImageWidth, ImageHeight);
                var cls = _project?.Classes.FirstOrDefault(c => c.Id == ann.ClassId);
                CurrentAnnotations.Add(new AnnotationDisplayItem
                {
                    Id = ann.Id,
                    ClassName = ann.ClassName,
                    ClassColor = cls?.Color ?? "#FF0000",
                    BBoxText = $"({px:F0}, {py:F0}, {pw:F0}, {ph:F0})"
                });
            }

            _undoStack.Clear();
            _redoStack.Clear();
        }
        // 加载LoadTiff
        private Bitmap LoadTiffFast(string path)
        {
            using Tiff tiff = Tiff.Open(path, "r");
            if (tiff == null) return null;

            int width = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
            int height = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
        
            // 1. 直接分配一个能够容纳 RGBA 数据的数组
            int[] raster = new int[width * height];

            // 2. LibTiff 读取到内存（此时是底层 RGBA 格式）
            if (!tiff.ReadRGBAImageOriented(width, height, raster, Orientation.TOPLEFT))
            {
                return null;
            }

            // 重点优化：直接通过内存指针创建 WriteableBitmap
            var writableBitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96), // 默认 DPI
                PixelFormat.Rgba8888, 
                AlphaFormat.Premul);

            using var lockedBitmap = writableBitmap.Lock();
            // 将 raster 数据直接拷贝到显存/内存位图中
            // LibTiff 的 int 数组其实就是 4 字节的 RGBA
            Marshal.Copy(raster, 0, lockedBitmap.Address, raster.Length);

            return writableBitmap;
        }

        // ===== 撤销 =====
        [RelayCommand]
        private void Undo()
        {
            if (_undoStack.Count == 0 || _currentAnnotation == null) return;

            var action = _undoStack.Pop();

            if (action.Type == UndoType.Add)
            {
                // 撤销添加 = 删除
                var ann = _currentAnnotation.Annotations.FirstOrDefault(a => a.Id == action.AnnotationId);
                if (ann != null)
                {
                    _currentAnnotation.Annotations.Remove(ann);
                    var displayItem = CurrentAnnotations.FirstOrDefault(d => d.Id == action.AnnotationId);
                    if (displayItem != null) CurrentAnnotations.Remove(displayItem);
                }
            }
            else if (action.Type == UndoType.Delete)
            {
                // 撤销删除 = 恢复
                if (action.DeletedAnnotation != null)
                {
                    _currentAnnotation.Annotations.Add(action.DeletedAnnotation);
                    if (action.DeletedDisplayItem != null)
                        CurrentAnnotations.Add(action.DeletedDisplayItem);
                }
            }

            _redoStack.Push(action);
            _annotationService.SaveAnnotation(_currentAnnotation);
        }

        // ===== 重做 =====
        [RelayCommand]
        private void Redo()
        {
            if (_redoStack.Count == 0 || _currentAnnotation == null) return;

            var action = _redoStack.Pop();

            if (action.Type == UndoType.Add)
            {
                // 重做添加
                if (action.DeletedAnnotation != null)
                {
                    _currentAnnotation.Annotations.Add(action.DeletedAnnotation);
                    if (action.DeletedDisplayItem != null)
                        CurrentAnnotations.Add(action.DeletedDisplayItem);
                }
            }
            else if (action.Type == UndoType.Delete)
            {
                // 重做删除
                var ann = _currentAnnotation.Annotations.FirstOrDefault(a => a.Id == action.AnnotationId);
                if (ann != null)
                {
                    _currentAnnotation.Annotations.Remove(ann);
                    var displayItem = CurrentAnnotations.FirstOrDefault(d => d.Id == action.AnnotationId);
                    if (displayItem != null) CurrentAnnotations.Remove(displayItem);
                }
            }

            _undoStack.Push(action);
            _annotationService.SaveAnnotation(_currentAnnotation);
        }

        // ===== 删除选中标注 =====
        [RelayCommand]
        private void DeleteSelected()
        {
            if (_selectedAnnotation == null || _currentAnnotation == null) return;
            DeleteAnnotationInternal(_selectedAnnotation);
        }

        // ===== 删除指定标注（列表中的删除按钮） =====
        [RelayCommand]
        private void DeleteAnnotation(AnnotationDisplayItem item)
        {
            if (item == null || _currentAnnotation == null) return;
            DeleteAnnotationInternal(item);
        }

        private void DeleteAnnotationInternal(AnnotationDisplayItem item)
        {
            var ann = _currentAnnotation.Annotations.FirstOrDefault(a => a.Id == item.Id);
            if (ann != null)
            {
                _currentAnnotation.Annotations.Remove(ann);
                CurrentAnnotations.Remove(item);

                // 推入撤销栈
                _undoStack.Push(new UndoAction
                {
                    Type = UndoType.Delete,
                    AnnotationId = ann.Id,
                    DeletedAnnotation = ann,
                    DeletedDisplayItem = item
                });
                _redoStack.Clear();

                _annotationService.SaveAnnotation(_currentAnnotation);

                if (_selectedImage != null)
                    _selectedImage.AnnotationCount = _currentAnnotation.Annotations.Count;
            }
        }

        // ===== 标记已完成 =====
        [RelayCommand]
        private void MarkCompleted()
        {
            if (_currentAnnotation == null) return;

            _currentAnnotation.Status = AnnotationStatus.Completed;
            _annotationService.SaveAnnotation(_currentAnnotation);

            if (_selectedImage != null)
            {
                _selectedImage.Status = AnnotationStatus.Completed;
                // 通知 UI 刷新颜色
                var idx = ImageList.IndexOf(_selectedImage);
                if (idx >= 0)
                {
                    var item = ImageList[idx];
                    ImageList.RemoveAt(idx);
                    ImageList.Insert(idx, item);
                }
            }

            UpdateProgress();

            // 自动跳到下一张未标注的图片
            var next = ImageList.FirstOrDefault(i =>
                i.Status == AnnotationStatus.Unlabeled || i.Status == AnnotationStatus.InProgress);
            if (next != null)
                SelectedImage = next;
            else
            {
                // 没有未标注的了，跳到下一张
                int currentIdx = ImageList.IndexOf(_selectedImage);
                if (currentIdx >= 0 && currentIdx < ImageList.Count - 1)
                    SelectedImage = ImageList[currentIdx + 1];
            }
        }

        // ===== 保存标注 =====
        [RelayCommand]
        private void SaveAnnotation()
        {
            if (_currentAnnotation != null)
            {
                _annotationService.SaveAnnotation(_currentAnnotation);
                System.Diagnostics.Debug.WriteLine("[Annotation] Saved.");
            }
        }

        // ===== 导出 YOLO 数据集 =====
        [RelayCommand]
        private async Task ExportDataset()
        {
            if (_annotationService == null || _project == null) return;

            try
            {
                _annotationService.ExportToYoloFormat(0.8f);

                // 统计结果
                int trainCount = Directory.Exists(_project.ImagesTrainDir)
                    ? Directory.GetFiles(_project.ImagesTrainDir).Length
                    : 0;
                int valCount = Directory.Exists(_project.ImagesValDir)
                    ? Directory.GetFiles(_project.ImagesValDir).Length
                    : 0;

                System.Diagnostics.Debug.WriteLine(
                    $"[Annotation] Dataset exported: train={trainCount} val={valCount}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Annotation] Export failed: {ex.Message}");
            }
        }

        // ===== 导入 YOLO 标注 =====
        [RelayCommand]
        private async Task ImportYolo()
        {
            // 选择图片目录
            string imagesDir = await DialogService.OpenFolderAsync(
                "\u9009\u62E9\u56FE\u7247\u76EE\u5F55"); // 选择图片目录
            if (string.IsNullOrEmpty(imagesDir)) return;

            // 选择标签目录
            string labelsDir = await DialogService.OpenFolderAsync(
                "\u9009\u62E9\u6807\u7B7E\u76EE\u5F55"); // 选择标签目录
            if (string.IsNullOrEmpty(labelsDir)) return;

            try
            {
                var imported = _annotationService.ImportFromYoloFormat(imagesDir, labelsDir);

                foreach (var ann in imported)
                {
                    if (!ImageList.Any(i => i.FullPath == ann.ImagePath))
                    {
                        ImageList.Add(new ImageListItem
                        {
                            FullPath = ann.ImagePath,
                            AnnotationCount = ann.Annotations.Count,
                            Status = ann.Status
                        });
                    }
                }

                UpdateProgress();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Annotation] Import YOLO failed: {ex.Message}");
            }
        }

        private void UpdateProgress()
        {
            LabeledCount = ImageList.Count(i =>
                i.Status == AnnotationStatus.Completed || i.Status == AnnotationStatus.Reviewed);
            TotalCount = ImageList.Count;
            LabelProgress = TotalCount > 0 ? (double)LabeledCount / TotalCount * 100 : 0;
        }
    }

    // ===== 撤销/重做 =====

    public enum UndoType
    {
        Add,
        Delete
    }

    public class UndoAction
    {
        public UndoType Type { get; set; }
        public string AnnotationId { get; set; }
        public Annotation DeletedAnnotation { get; set; }
        public AnnotationDisplayItem DeletedDisplayItem { get; set; }
    }

    // ===== 显示用辅助类 =====

    public class ImageListItem : ObservableObject
    {
        public string FullPath { get; set; } = "";
        public string FileName => Path.GetFileName(FullPath);
        public int AnnotationCount { get; set; }
        public AnnotationStatus Status { get; set; }

        public string StatusColor => Status switch
        {
            AnnotationStatus.Completed => "#4CAF50",
            AnnotationStatus.InProgress => "#FF9800",
            AnnotationStatus.Reviewed => "#2196F3",
            _ => "#BDBDBD"
        };
    }

    public class ClassDisplayItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Color { get; set; } = "#FF0000";
        public string Shortcut { get; set; } = "";
    }

    public class AnnotationDisplayItem
    {
        public string Id { get; set; } = "";
        public string ClassName { get; set; } = "";
        public string ClassColor { get; set; } = "#FF0000";
        public string BBoxText { get; set; } = "";
    }
}