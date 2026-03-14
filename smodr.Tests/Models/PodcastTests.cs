using smodr.Models;

namespace smodr.Tests.Models;

[TestClass]
public sealed class PodcastTests
{
    [TestMethod]
    public void Catalog_IsNotEmpty()
    {
        Assert.IsTrue(Podcast.Catalog.Count > 0);
    }

    [TestMethod]
    public void Catalog_ContainsExpectedPodcasts()
    {
        var ids = Podcast.Catalog.Select(p => p.Id).ToList();

        CollectionAssert.Contains(ids, "smodcast");
        CollectionAssert.Contains(ids, "babbleon");
        CollectionAssert.Contains(ids, "jaysilentbob");
        CollectionAssert.Contains(ids, "fatmanbeyond");
        CollectionAssert.Contains(ids, "tesd");
        CollectionAssert.Contains(ids, "smoviemakers");
    }

    [TestMethod]
    public void Catalog_AllEntriesHaveRequiredFields()
    {
        foreach (var podcast in Podcast.Catalog)
        {
            Assert.IsFalse(string.IsNullOrEmpty(podcast.Id), $"Podcast has empty Id");
            Assert.IsFalse(string.IsNullOrEmpty(podcast.Name), $"Podcast '{podcast.Id}' has empty Name");
            Assert.IsFalse(string.IsNullOrEmpty(podcast.FeedUrl), $"Podcast '{podcast.Id}' has empty FeedUrl");
        }
    }

    [TestMethod]
    public void Catalog_AllEntriesHaveUniqueIds()
    {
        var ids = Podcast.Catalog.Select(p => p.Id).ToList();
        var uniqueIds = ids.Distinct().ToList();

        Assert.AreEqual(ids.Count, uniqueIds.Count, "Catalog contains duplicate Ids");
    }

    [TestMethod]
    public void Catalog_AllFeedUrlsAreValidUrls()
    {
        foreach (var podcast in Podcast.Catalog)
        {
            Assert.IsTrue(
                Uri.TryCreate(podcast.FeedUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https",
                $"Podcast '{podcast.Id}' has invalid FeedUrl: {podcast.FeedUrl}");
        }
    }

    [TestMethod]
    public void Catalog_AllEntriesHaveDescriptions()
    {
        foreach (var podcast in Podcast.Catalog)
        {
            Assert.IsFalse(
                string.IsNullOrEmpty(podcast.Description),
                $"Podcast '{podcast.Id}' has empty Description");
        }
    }

    [TestMethod]
    public void Catalog_AllEntriesHaveImageUrls()
    {
        foreach (var podcast in Podcast.Catalog)
        {
            Assert.IsFalse(
                string.IsNullOrEmpty(podcast.ImageUrl),
                $"Podcast '{podcast.Id}' has empty ImageUrl");
        }
    }

    [TestMethod]
    public void Catalog_AllEntriesHaveHosts()
    {
        foreach (var podcast in Podcast.Catalog)
        {
            Assert.IsFalse(
                string.IsNullOrEmpty(podcast.Hosts),
                $"Podcast '{podcast.Id}' has empty Hosts");
        }
    }

    [TestMethod]
    public void Catalog_IsReadOnly()
    {
        Assert.IsInstanceOfType<IReadOnlyList<Podcast>>(Podcast.Catalog);
    }

    [TestMethod]
    public void Equals_Null_ReturnsFalse()
    {
        var podcast = new Podcast
        {
            Id = "test",
            Name = "Test",
            FeedUrl = "https://example.com/feed"
        };

        Assert.IsFalse(podcast.Equals(null));
    }

    [TestMethod]
    public void Equals_SameReference_ReturnsTrue()
    {
        var podcast = new Podcast
        {
            Id = "test",
            Name = "Test",
            FeedUrl = "https://example.com/feed"
        };

        Assert.IsTrue(podcast.Equals(podcast));
    }

    [TestMethod]
    public void Equals_SameId_ReturnsTrue()
    {
        var podcast1 = new Podcast
        {
            Id = "test",
            Name = "Test 1",
            FeedUrl = "https://example.com/feed1"
        };

        var podcast2 = new Podcast
        {
            Id = "test",
            Name = "Test 2",
            FeedUrl = "https://example.com/feed2"
        };

        Assert.IsTrue(podcast1.Equals(podcast2));
    }

    [TestMethod]
    public void Equals_DifferentId_ReturnsFalse()
    {
        var podcast1 = new Podcast
        {
            Id = "test1",
            Name = "Test",
            FeedUrl = "https://example.com/feed"
        };

        var podcast2 = new Podcast
        {
            Id = "test2",
            Name = "Test",
            FeedUrl = "https://example.com/feed"
        };

        Assert.IsFalse(podcast1.Equals(podcast2));
    }

    [TestMethod]
    public void GetHashCode_SameId_ReturnsSameHash()
    {
        var podcast1 = new Podcast
        {
            Id = "test",
            Name = "Podcast 1",
            FeedUrl = "https://example.com/feed1"
        };

        var podcast2 = new Podcast
        {
            Id = "test",
            Name = "Podcast 2",
            FeedUrl = "https://example.com/feed2"
        };

        Assert.AreEqual(podcast1.GetHashCode(), podcast2.GetHashCode());
    }

    [TestMethod]
    public void GetHashCode_DifferentId_ReturnsDifferentHash()
    {
        var podcast1 = new Podcast
        {
            Id = "test1",
            Name = "Test",
            FeedUrl = "https://example.com/feed"
        };

        var podcast2 = new Podcast
        {
            Id = "test2",
            Name = "Test",
            FeedUrl = "https://example.com/feed"
        };

        Assert.AreNotEqual(podcast1.GetHashCode(), podcast2.GetHashCode());
    }

    [TestMethod]
    public void DefaultValues_AreCorrect()
    {
        var podcast = new Podcast
        {
            Id = "test",
            Name = "Test",
            FeedUrl = "https://example.com/feed"
        };

        Assert.AreEqual(string.Empty, podcast.Description);
        Assert.AreEqual(string.Empty, podcast.ImageUrl);
        Assert.AreEqual(string.Empty, podcast.Hosts);
        Assert.IsNull(podcast.ApplePodcastId);
    }

    [TestMethod]
    public void Equals_OperatorEquality_UsesId()
    {
        var podcast1 = new Podcast
        {
            Id = "same",
            Name = "P1",
            FeedUrl = "https://example.com/feed1"
        };

        var podcast2 = new Podcast
        {
            Id = "same",
            Name = "P2",
            FeedUrl = "https://example.com/feed2"
        };

        Assert.IsTrue(podcast1 == podcast2);
    }

    [TestMethod]
    public void Equals_OperatorInequality_UsesId()
    {
        var podcast1 = new Podcast
        {
            Id = "id1",
            Name = "Test",
            FeedUrl = "https://example.com/feed"
        };

        var podcast2 = new Podcast
        {
            Id = "id2",
            Name = "Test",
            FeedUrl = "https://example.com/feed"
        };

        Assert.IsTrue(podcast1 != podcast2);
    }

    [TestMethod]
    public void ApplePodcastId_CanBeSetToValue()
    {
        var podcast = new Podcast
        {
            Id = "test",
            Name = "Test",
            FeedUrl = "https://example.com/feed",
            ApplePodcastId = 123456789
        };

        Assert.AreEqual(123456789L, podcast.ApplePodcastId);
    }

    [TestMethod]
    public void Catalog_SModcastEntry_HasExpectedValues()
    {
        var smodcast = Podcast.Catalog.First(p => p.Id == "smodcast");

        Assert.AreEqual("SModcast", smodcast.Name);
        Assert.IsTrue(smodcast.FeedUrl.Contains("SModcast", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(215010467L, smodcast.ApplePodcastId);
    }
}
