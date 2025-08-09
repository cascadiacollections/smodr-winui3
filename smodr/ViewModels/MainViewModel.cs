using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using smodr.Models;
using smodr.Services;

namespace smodr.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly DataService _dataService;

        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        private string loadingMessage = "Loading episodes...";

        [ObservableProperty]
        private Episode? selectedEpisode;

        public ObservableCollection<Episode> Episodes { get; } = new();

        public MainViewModel()
        {
            _dataService = new DataService();
        }

        [RelayCommand]
        public async Task LoadEpisodesAsync()
        {
            IsLoading = true;
            LoadingMessage = "Fetching episodes from Smodcast RSS feed...";

            try
            {
                var episodes = await _dataService.GetEpisodesAsync();
                
                Episodes.Clear();
                foreach (var episode in episodes)
                {
                    Episodes.Add(episode);
                }

                if (Episodes.Count == 0)
                {
                    LoadingMessage = "No episodes found. Please check your internet connection.";
                }
            }
            catch (System.Exception ex)
            {
                LoadingMessage = $"Error loading episodes: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        public async Task RefreshEpisodesAsync()
        {
            await LoadEpisodesAsync();
        }

        [RelayCommand]
        public void SelectEpisode(Episode episode)
        {
            SelectedEpisode = episode;
            // TODO: Implement episode playback
        }

        public void Dispose()
        {
            _dataService?.Dispose();
        }
    }
}