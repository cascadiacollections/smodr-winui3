using System.Diagnostics;
using smodr.Models;

namespace smodr.Services;

/// <summary>Archived podcast playback API, retained for the unexposed legacy view model.</summary>
public partial class AudioService
{
    public Episode? CurrentEpisode { get; private set; }
    public event EventHandler<Episode>? EpisodeChanged;

    public Task PlayEpisodeAsync(Episode episode)
    {
        if (!_isInitialized) Initialize();
        if (string.IsNullOrEmpty(episode.MediaUrl))
            throw new ArgumentException("Episode has no media URL to play.");

        try
        {
            _recovery.Pause();
            _radioEnded = false;
            if (_mediaPlayer is not null
                && string.Equals(CurrentEpisode?.MediaUrl, episode.MediaUrl, StringComparison.Ordinal))
            {
                _mediaPlayer.Play();
                return Task.CompletedTask;
            }

            CreatePlayer();
            CurrentEpisode = episode;
            CurrentStation = null;
            ClearTrack();
            EpisodeChanged?.Invoke(this, episode);
            SetPlayerSource(new Uri(episode.MediaUrl), NowPlayingMetadata.ForEpisode(episode));
            _mediaPlayer!.Play();
            Debug.WriteLine($"Started playing: {episode.Title}");
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Error playing episode: {exception.Message}");
            throw;
        }
        return Task.CompletedTask;
    }
}
