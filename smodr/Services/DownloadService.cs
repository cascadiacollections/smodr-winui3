using System.Diagnostics;
using smodr.Models;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace smodr.Services;

public class DownloadService : IDisposable
{
    private const int MaxFileNameLength = 200;
    private readonly HttpClient _httpClient = new();

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task<bool> DownloadEpisodeAsync(Episode episode, nint windowHandle)
    {
        try
        {
            if (string.IsNullOrEmpty(episode.MediaUrl))
            {
                throw new ArgumentException("Episode has no media URL to download.");
            }

            var savePicker = new FileSavePicker();
            InitializeWithWindow.Initialize(savePicker, windowHandle);

            var fileExtension = GetFileExtension(episode.MediaUrl);
            savePicker.FileTypeChoices.Add($"{fileExtension.ToUpperInvariant()} File", [fileExtension]);
            savePicker.SuggestedFileName = SanitizeFileName($"{episode.Title}{fileExtension}");
            savePicker.SuggestedStartLocation = PickerLocationId.MusicLibrary;

            var file = await savePicker.PickSaveFileAsync();
            if (file is null)
            {
                return false;
            }

            using var response =
                await _httpClient.GetAsync(new Uri(episode.MediaUrl), HttpCompletionOption.ResponseHeadersRead);
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
        catch (UriFormatException)
        {
            return ".mp3";
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        fileName = string.Create(fileName.Length, (fileName, invalidChars), static (span, state) =>
        {
            state.fileName.CopyTo(span);
            foreach (ref var c in span)
            {
                if (state.invalidChars.AsSpan().Contains(c))
                {
                    c = '_';
                }
            }
        });

        if (fileName.Length > MaxFileNameLength)
        {
            var extension = Path.GetExtension(fileName);
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            fileName = string.Concat(nameWithoutExtension.AsSpan(0, MaxFileNameLength - extension.Length), extension);
        }

        return fileName;
    }
}
