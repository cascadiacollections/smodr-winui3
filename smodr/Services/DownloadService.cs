using System.Diagnostics;
using smodr.Models;
using Windows.Storage.Pickers;

namespace smodr.Services;

public class DownloadService : IDisposable
{
    private readonly HttpClient _httpClient = new();
    private const int MaxFileNameLength = 200;

    public async Task<bool> DownloadEpisodeAsync(Episode episode, nint windowHandle)
    {
        try
        {
            if (string.IsNullOrEmpty(episode.MediaUrl))
                throw new ArgumentException("Episode has no media URL to download.");

            var savePicker = new FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(savePicker, windowHandle);

            var fileExtension = GetFileExtension(episode.MediaUrl);
            savePicker.FileTypeChoices.Add($"{fileExtension.ToUpper()} File", [fileExtension]);
            savePicker.SuggestedFileName = SanitizeFileName($"{episode.Title}{fileExtension}");
            savePicker.SuggestedStartLocation = PickerLocationId.MusicLibrary;

            var file = await savePicker.PickSaveFileAsync();
            if (file is null)
                return false;

            using var response = await _httpClient.GetAsync(episode.MediaUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = await file.OpenStreamForWriteAsync();

            await contentStream.CopyToAsync(fileStream);

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error downloading episode: {ex.Message}");
            throw;
        }
    }

    private static string GetFileExtension(string url)
    {
        try
        {
            var uri = new Uri(url);
            var extension = Path.GetExtension(uri.LocalPath);
            return string.IsNullOrEmpty(extension) ? ".mp3" : extension;
        }
        catch
        {
            return ".mp3";
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var invalidChar in invalidChars)
        {
            fileName = fileName.Replace(invalidChar, '_');
        }

        if (fileName.Length > MaxFileNameLength)
        {
            var extension = Path.GetExtension(fileName);
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            fileName = nameWithoutExtension[..(MaxFileNameLength - extension.Length)] + extension;
        }

        return fileName;
    }

    public void Dispose() => _httpClient?.Dispose();
}
