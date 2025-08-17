using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using smodr.Models;
using WinRT.Interop;

namespace smodr.Services
{
    public class DownloadService
    {
        private readonly HttpClient _httpClient = new();
        private const int MaxFileNameLength = 255;
        public async Task<bool> DownloadEpisodeAsync(Episode episode, object window)
        {
            try
            {
                if (string.IsNullOrEmpty(episode.MediaUrl))
                {
                    throw new ArgumentException("Episode has no media URL to download.");
                }

                // Create a file picker to let the user choose where to save
                var savePicker = new FileSavePicker();

                // Initialize the picker with the provided window
                var hWnd = WindowNative.GetWindowHandle(window);
                InitializeWithWindow.Initialize(savePicker, hWnd);

                // Set the file type and default name
                var fileExtension = GetFileExtension(episode.MediaUrl);
                savePicker.FileTypeChoices.Add($"{fileExtension.ToUpper()} File", [fileExtension]);
                savePicker.SuggestedFileName = SanitizeFileName($"{episode.Title}{fileExtension}");
                savePicker.SuggestedStartLocation = PickerLocationId.MusicLibrary;

                // Show the file picker
                var file = await savePicker.PickSaveFileAsync();
                if (file == null)
                {
                    // User cancelled
                    return false;
                }

                // Download the episode
                using var response = await _httpClient.GetAsync(episode.MediaUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                await using var contentStream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = await file.OpenStreamForWriteAsync();

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

                // Default to .mp3 if no extension found
                return string.IsNullOrEmpty(extension) ? ".mp3" : extension;
            }
            catch
            {
                return ".mp3";
            }
        }

        private static string SanitizeFileName(string fileName)
        {
            // Remove invalid characters from the file name
            var invalidChars = Path.GetInvalidFileNameChars();
            fileName = invalidChars.Aggregate(fileName, (current, invalidChar) => current.Replace(invalidChar, '_'));

            // Limit length
            if (fileName.Length <= MaxFileNameLength) return fileName;
            var extension = Path.GetExtension(fileName);
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            fileName = nameWithoutExtension[..(MaxFileNameLength - extension.Length)] + extension;

            return fileName;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}