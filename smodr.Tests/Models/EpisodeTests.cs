using System.Globalization;
using smodr.Models;

namespace smodr.Tests.Models;

[TestClass]
public sealed class EpisodeTests
{
    [TestMethod]
    public void FormattedPublishDate_ReturnsExpectedFormat()
    {
        var episode = new Episode
        {
            Title = "Test",
            MediaUrl = "https://example.com/ep1.mp3",
            PublishDate = new DateTime(2024, 3, 15, 0, 0, 0, DateTimeKind.Utc)
        };

        var result = episode.FormattedPublishDate;

        Assert.AreEqual(episode.PublishDate.ToString("MMM dd, yyyy", CultureInfo.CurrentCulture), result);
    }

    [TestMethod]
    public void FormattedDuration_WhenEmpty_ReturnsUnknown()
    {
        var episode = new Episode
        {
            Title = "Test",
            MediaUrl = "https://example.com/ep1.mp3",
            Duration = string.Empty
        };

        Assert.AreEqual("Unknown", episode.FormattedDuration);
    }

    [TestMethod]
    public void FormattedDuration_WhenSet_ReturnsValue()
    {
        var episode = new Episode
        {
            Title = "Test",
            MediaUrl = "https://example.com/ep1.mp3",
            Duration = "01:23:45"
        };

        Assert.AreEqual("01:23:45", episode.FormattedDuration);
    }

    [TestMethod]
    public void FormattedFileSize_WhenZero_ReturnsUnknown()
    {
        var episode = new Episode
        {
            Title = "Test",
            MediaUrl = "https://example.com/ep1.mp3",
            FileSize = 0
        };

        Assert.AreEqual("Unknown", episode.FormattedFileSize);
    }

    [TestMethod]
    public void FormattedFileSize_WhenPositive_ReturnsFormattedMB()
    {
        var episode = new Episode
        {
            Title = "Test",
            MediaUrl = "https://example.com/ep1.mp3",
            FileSize = 10 * 1024 * 1024 // 10 MB
        };

        Assert.AreEqual("10.0 MB", episode.FormattedFileSize);
    }

    [TestMethod]
    public void FormattedFileSize_FractionalMB_ReturnsOneDecimal()
    {
        var episode = new Episode
        {
            Title = "Test",
            MediaUrl = "https://example.com/ep1.mp3",
            FileSize = (long)(1.5 * 1024 * 1024) // ~1.5 MB
        };

        Assert.AreEqual("1.5 MB", episode.FormattedFileSize);
    }

    [TestMethod]
    public void MetadataLine_CombinesDateAndDuration()
    {
        var episode = new Episode
        {
            Title = "Test",
            MediaUrl = "https://example.com/ep1.mp3",
            PublishDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Duration = "00:30:00"
        };

        var expected = $"{episode.FormattedPublishDate} · {episode.FormattedDuration}";
        Assert.AreEqual(expected, episode.MetadataLine);
    }

    [TestMethod]
    public void MetadataLine_WhenNoDuration_ShowsUnknown()
    {
        var episode = new Episode
        {
            Title = "Test",
            MediaUrl = "https://example.com/ep1.mp3",
            PublishDate = new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc)
        };

        StringAssert.Contains(episode.MetadataLine, "Unknown");
    }

    [TestMethod]
    public void Equals_Null_ReturnsFalse()
    {
        var episode = new Episode
        {
            Title = "Test",
            MediaUrl = "https://example.com/ep1.mp3"
        };

        Assert.IsFalse(episode.Equals(null));
    }

    [TestMethod]
    public void Equals_SameReference_ReturnsTrue()
    {
        var episode = new Episode
        {
            Title = "Test",
            MediaUrl = "https://example.com/ep1.mp3"
        };

        Assert.IsTrue(episode.Equals(episode));
    }

    [TestMethod]
    public void Equals_SameMediaUrl_ReturnsTrue()
    {
        var episode1 = new Episode
        {
            Title = "Episode 1",
            MediaUrl = "https://example.com/ep1.mp3"
        };

        var episode2 = new Episode
        {
            Title = "Episode 2 Different Title",
            MediaUrl = "https://example.com/ep1.mp3"
        };

        Assert.IsTrue(episode1.Equals(episode2));
    }

    [TestMethod]
    public void Equals_DifferentMediaUrl_ReturnsFalse()
    {
        var episode1 = new Episode
        {
            Title = "Test",
            MediaUrl = "https://example.com/ep1.mp3"
        };

        var episode2 = new Episode
        {
            Title = "Test",
            MediaUrl = "https://example.com/ep2.mp3"
        };

        Assert.IsFalse(episode1.Equals(episode2));
    }

    [TestMethod]
    public void GetHashCode_SameMediaUrl_ReturnsSameHash()
    {
        var episode1 = new Episode
        {
            Title = "Episode 1",
            MediaUrl = "https://example.com/ep1.mp3"
        };

        var episode2 = new Episode
        {
            Title = "Episode 2",
            MediaUrl = "https://example.com/ep1.mp3"
        };

        Assert.AreEqual(episode1.GetHashCode(), episode2.GetHashCode());
    }

    [TestMethod]
    public void GetHashCode_DifferentMediaUrl_ReturnsDifferentHash()
    {
        var episode1 = new Episode
        {
            Title = "Test",
            MediaUrl = "https://example.com/ep1.mp3"
        };

        var episode2 = new Episode
        {
            Title = "Test",
            MediaUrl = "https://example.com/ep2.mp3"
        };

        Assert.AreNotEqual(episode1.GetHashCode(), episode2.GetHashCode());
    }

    [TestMethod]
    public void DefaultValues_AreCorrect()
    {
        var episode = new Episode
        {
            Title = "Test",
            MediaUrl = "https://example.com/ep1.mp3"
        };

        Assert.AreEqual(string.Empty, episode.Description);
        Assert.AreEqual(string.Empty, episode.Duration);
        Assert.AreEqual(0, episode.FileSize);
        Assert.AreEqual(0, episode.CurrentTime);
        Assert.AreEqual(string.Empty, episode.ImageUrl);
        Assert.AreEqual(string.Empty, episode.EpisodeNumber);
    }

    [TestMethod]
    public void CurrentTime_CanBeSet()
    {
        var episode = new Episode
        {
            Title = "Test",
            MediaUrl = "https://example.com/ep1.mp3"
        };

        episode.CurrentTime = 120;

        Assert.AreEqual(120, episode.CurrentTime);
    }

    [TestMethod]
    public void Equals_OperatorEquality_UsesMediaUrl()
    {
        var episode1 = new Episode
        {
            Title = "Ep 1",
            MediaUrl = "https://example.com/same.mp3"
        };

        var episode2 = new Episode
        {
            Title = "Ep 2",
            MediaUrl = "https://example.com/same.mp3"
        };

        Assert.IsTrue(episode1 == episode2);
    }

    [TestMethod]
    public void Equals_OperatorInequality_UsesMediaUrl()
    {
        var episode1 = new Episode
        {
            Title = "Test",
            MediaUrl = "https://example.com/ep1.mp3"
        };

        var episode2 = new Episode
        {
            Title = "Test",
            MediaUrl = "https://example.com/ep2.mp3"
        };

        Assert.IsTrue(episode1 != episode2);
    }
}
