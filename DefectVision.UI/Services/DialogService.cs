using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace DefectVision.UI.Services
{
    /// <summary>
    /// 文件/文件夹选择对话框服务（Avalonia 11）
    /// </summary>
    public static class DialogService
    {
        private static Window GetMainWindow()
        {
            return (Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                ?.MainWindow;
        }

        /// <summary>
        /// 选择单个文件
        /// </summary>
        public static async Task<string> OpenFileAsync(string title, params string[] extensions)
        {
            var window = GetMainWindow();
            if (window == null) return null;

            var filters = new List<FilePickerFileType>();
            if (extensions != null && extensions.Length > 0)
            {
                var patterns = extensions.Select(e => e.StartsWith("*") ? e : $"*.{e}").ToList();
                filters.Add(new FilePickerFileType("支持的文件") { Patterns = patterns });
            }
            filters.Add(new FilePickerFileType("所有文件") { Patterns = new[] { "*.*" } });

            var result = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = filters,
            });

            return result.Count > 0 ? result[0].Path.LocalPath : null;
        }

        /// <summary>
        /// 选择多个文件
        /// </summary>
        public static async Task<List<string>> OpenFilesAsync(string title, params string[] extensions)
        {
            var window = GetMainWindow();
            if (window == null) return new List<string>();

            var filters = new List<FilePickerFileType>();
            if (extensions != null && extensions.Length > 0)
            {
                var patterns = extensions.Select(e => e.StartsWith("*") ? e : $"*.{e}").ToList();
                filters.Add(new FilePickerFileType("支持的文件") { Patterns = patterns });
            }
            filters.Add(new FilePickerFileType("所有文件") { Patterns = new[] { "*.*" } });

            var result = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = true,
                FileTypeFilter = filters,
            });

            return result.Select(f => f.Path.LocalPath).ToList();
        }

        /// <summary>
        /// 选择文件夹
        /// </summary>
        public static async Task<string> OpenFolderAsync(string title)
        {
            var window = GetMainWindow();
            if (window == null) return null;

            var result = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
            });

            return result.Count > 0 ? result[0].Path.LocalPath : null;
        }

        /// <summary>
        /// 保存文件对话框
        /// </summary>
        public static async Task<string> SaveFileAsync(string title, string defaultName, params string[] extensions)
        {
            var window = GetMainWindow();
            if (window == null) return null;

            var filters = new List<FilePickerFileType>();
            if (extensions != null && extensions.Length > 0)
            {
                var patterns = extensions.Select(e => e.StartsWith("*") ? e : $"*.{e}").ToList();
                filters.Add(new FilePickerFileType("支持的文件") { Patterns = patterns });
            }

            var result = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = defaultName,
                FileTypeChoices = filters,
            });

            return result?.Path.LocalPath;
        }
    }
}