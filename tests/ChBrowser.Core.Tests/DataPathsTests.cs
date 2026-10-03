using System.IO;
using ChBrowser.Models;
using ChBrowser.Services.Bbs;
using ChBrowser.Services.Storage;
using Xunit;

namespace ChBrowser.Core.Tests;

public class DataPathsTests
{
    [Fact]
    public void DataPaths_UsesRootOverrideCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "chbrowser_test_" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new DataPaths(tempDir);
            Assert.Equal(tempDir, paths.Root);

            var boardDir = paths.BoardDir("bbs.eddibb.cc", "liveedge");
            Assert.Equal(Path.Combine(tempDir, "eddibb.cc", "liveedge"), boardDir);
            Assert.True(Directory.Exists(boardDir));

            var datPath = paths.DatPath("bbs.eddibb.cc", "liveedge", "1234567890");
            Assert.Equal(Path.Combine(tempDir, "eddibb.cc", "liveedge", "1234567890.dat"), datPath);

            var settingPath = paths.SettingTxtPath("bbs.eddibb.cc", "liveedge");
            Assert.Equal(Path.Combine(tempDir, "eddibb.cc", "liveedge", "_SETTING.TXT"), settingPath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void DataPaths_CustomMediaCacheDir_SwitchesMediaDirsCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "chbrowser_test_" + System.Guid.NewGuid().ToString("N"));
        var customCacheDir = Path.Combine(Path.GetTempPath(), "chbrowser_custom_cache_" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new DataPaths(tempDir);
            Assert.Equal(Path.Combine(tempDir, "cache"), paths.MediaCacheBaseDir);
            Assert.Equal(Path.Combine(tempDir, "cache", "images"), paths.CacheImagesDir);
            Assert.Equal(Path.Combine(tempDir, "cache", "videos"), paths.CacheVideosDir);

            paths.CustomMediaCacheDir = customCacheDir;
            Assert.Equal(customCacheDir, paths.MediaCacheBaseDir);
            Assert.Equal(Path.Combine(customCacheDir, "images"), paths.CacheImagesDir);
            Assert.Equal(Path.Combine(customCacheDir, "videos"), paths.CacheVideosDir);
            Assert.True(Directory.Exists(paths.CacheImagesDir));
            Assert.True(Directory.Exists(paths.CacheVideosDir));

            paths.CustomMediaCacheDir = null;
            Assert.Equal(Path.Combine(tempDir, "cache"), paths.MediaCacheBaseDir);
            Assert.Equal(Path.Combine(tempDir, "cache", "images"), paths.CacheImagesDir);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            if (Directory.Exists(customCacheDir)) Directory.Delete(customCacheDir, true);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task ImageCacheService_UpdateCacheDirectories_SwitchesLocation()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "chbrowser_test_" + System.Guid.NewGuid().ToString("N"));
        var customCacheDir = Path.Combine(Path.GetTempPath(), "chbrowser_custom_cache_" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new DataPaths(tempDir);
            using var cache = new ChBrowser.Services.Image.ImageCacheService(paths);

            // 切り替え
            paths.CustomMediaCacheDir = customCacheDir;
            cache.UpdateCacheDirectories(paths);

            var testUrl = "http://example.com/test.jpg";
            var testBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };
            using (var ms = new MemoryStream(testBytes))
            {
                await cache.SaveAsync(testUrl, ms, "image/jpeg", ChBrowser.Services.Image.CacheKind.Image);
            }

            Assert.True(cache.Contains(testUrl));
            Assert.True(cache.TryGet(testUrl, out var hit));
            Assert.StartsWith(customCacheDir, hit.FilePath);
            Assert.Equal(Path.Combine(customCacheDir, "images", "example.com_test.jpg"), hit.FilePath);
            Assert.True(File.Exists(hit.FilePath));
            Assert.True(File.Exists(Path.Combine(customCacheDir, "images", "index.json")));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            if (Directory.Exists(customCacheDir)) Directory.Delete(customCacheDir, true);
        }
    }

    [Fact]
    public void ImageCacheService_UrlToFileName_GeneratesExpectedNames()
    {
        // 1. 基本的なURL (サーバ名 + 先頭スラッシュ除去、/ を _ に変換)
        Assert.Equal("example.com_test.jpg", ChBrowser.Services.Image.ImageCacheService.UrlToFileName("https://example.com/test.jpg", ".jpg", ChBrowser.Services.Image.CacheKind.Image));
        Assert.Equal("example.com_sub_image.jpg", ChBrowser.Services.Image.ImageCacheService.UrlToFileName("https://example.com/sub/image.jpg", ".jpg", ChBrowser.Services.Image.CacheKind.Image));
        Assert.Equal("example.com_2026_10_03_photo.png", ChBrowser.Services.Image.ImageCacheService.UrlToFileName("https://example.com/2026/10/03/photo.png", ".png", ChBrowser.Services.Image.CacheKind.Image));

        // 2. サーバ名が異なる場合のファイル名重複防止 (ホスト名は自動で小文字化される)
        var fileA = ChBrowser.Services.Image.ImageCacheService.UrlToFileName("https://serverA.com/photo.jpg", ".jpg", ChBrowser.Services.Image.CacheKind.Image);
        var fileB = ChBrowser.Services.Image.ImageCacheService.UrlToFileName("https://serverB.com/photo.jpg", ".jpg", ChBrowser.Services.Image.CacheKind.Image);
        Assert.Equal("servera.com_photo.jpg", fileA);
        Assert.Equal("serverb.com_photo.jpg", fileB);
        Assert.NotEqual(fileA, fileB);

        // 3. 動画本体
        Assert.Equal("example.com_sub_video.mp4", ChBrowser.Services.Image.ImageCacheService.UrlToFileName("https://example.com/sub/video.mp4", ".mp4", ChBrowser.Services.Image.CacheKind.Video));

        // 4. 動画サムネイル (末尾に .jpg 付加)
        Assert.Equal("example.com_sub_video.mp4.jpg", ChBrowser.Services.Image.ImageCacheService.UrlToFileName("https://example.com/sub/video.mp4", ".jpg", ChBrowser.Services.Image.CacheKind.VideoThumb));
    }

    [Fact]
    public async System.Threading.Tasks.Task ImageCacheService_SavesDirectlyUnderImages_WithoutSubdirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "chbrowser_test_" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new DataPaths(tempDir);
            using var cache = new ChBrowser.Services.Image.ImageCacheService(paths);

            var testUrl = "https://example.com/sub/dir/pic.jpg";
            var testBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };
            using (var ms = new MemoryStream(testBytes))
            {
                await cache.SaveAsync(testUrl, ms, "image/jpeg", ChBrowser.Services.Image.CacheKind.Image);
            }

            Assert.True(cache.TryGet(testUrl, out var hit));
            var expectedFile = Path.Combine(paths.CacheImagesDir, "example.com_sub_dir_pic.jpg");
            Assert.Equal(expectedFile, hit.FilePath);
            Assert.True(File.Exists(expectedFile));

            // images 直下にサブディレクトリが生成されていないことを確認
            var subDirs = Directory.GetDirectories(paths.CacheImagesDir);
            Assert.Empty(subDirs);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void DataPaths_DefaultLinuxXdg_UsesCacheDataSubdirectory()
    {
        var paths = new DataPaths(forceXdg: true);
        Assert.True(paths.IsXdgMode);
        Assert.EndsWith(Path.Combine("chbrowser", "data"), paths.Root);
        Assert.Equal(paths.DefaultRoot, paths.Root);
        Assert.Equal(Path.Combine(paths.CacheDir, "data"), paths.Root);
        Assert.True(Directory.Exists(paths.Root));
    }

    [Fact]
    public void DataPaths_CustomDataRootDir_SwitchesRootCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "chbrowser_test_" + System.Guid.NewGuid().ToString("N"));
        var customDataDir = Path.Combine(Path.GetTempPath(), "chbrowser_custom_data_" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new DataPaths(tempDir);
            Assert.Equal(tempDir, paths.Root);

            paths.CustomDataRootDir = customDataDir;
            Assert.Equal(customDataDir, paths.Root);
            Assert.True(Directory.Exists(customDataDir));

            var boardDir = paths.BoardDir("bbs.eddibb.cc", "liveedge");
            Assert.Equal(Path.Combine(customDataDir, "eddibb.cc", "liveedge"), boardDir);
            Assert.True(Directory.Exists(boardDir));

            paths.CustomDataRootDir = null;
            Assert.Equal(tempDir, paths.Root);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            if (Directory.Exists(customDataDir)) Directory.Delete(customDataDir, true);
        }
    }
}

