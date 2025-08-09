using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using smodr.Models;

namespace smodr.Services
{
    public class DownloadService
    {
        private readonly HttpClient _httpClient;

        public DownloadService()
        {
            _httpClient = new HttpClient();
        }

        public async Task<bool> DownloadEpisodeAsync(Episode episode)
        {
            try
            {
                if (string.IsNullOrEmpty(episode.MediaUrl))
                {
                    throw new ArgumentException("Episode has no media URL to download.");
                }

                // Create a file picker to let the user choose where to save
                var savePicker = new FileSavePicker();
                
                // Initialize the picker with the current window
                var window = App.MainWindow;
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hWnd);

                // Set the file type and default name
                var fileExtension = GetFileExtension(episode.MediaUrl);
                savePicker.FileTypeChoices.Add($"{fileExtension.ToUpper()} File", new[] { fileExtension });
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

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = await file.OpenStreamForWriteAsync();
                
                await contentStream.CopyToAsync(fileStream);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error downloading episode: {ex.Message}");
                throw;
            }
        }

        private string GetFileExtension(string url)
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

        private string SanitizeFileName(string fileName)
        {
            // Remove invalid characters from the file name
            var invalidChars = Path.GetInvalidFileNameChars();
            foreach (var invalidChar in invalidChars)
            {
                fileName = fileName.Replace(invalidChar, '_');
            }
            
            // Limit length
            if (fileName.Length > 100)
            {
                var extension = Path.GetExtension(fileName);
                var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
                fileName = nameWithoutExtension.Substring(0, 100 - extension.Length) + extension;
            }

            return fileName;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}