using System.Text.Json;
using smodr.Services;

namespace smodr.Tests.Services;

[TestClass]
public sealed class RuntimeDiagnosticsTests
{
    [TestMethod]
    public async Task ConcurrentIncrementsAreLosslessAndSnapshotIsDetached()
    {
        var counters = new RuntimeDiagnosticCounters();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var index = 0; index < 1000; index++)
            {
                counters.Increment(RuntimeCounter.MetadataAccepted);
            }
        })));
        var snapshot = counters.Snapshot();
        Assert.AreEqual(8000L, snapshot[nameof(RuntimeCounter.MetadataAccepted)]);
        snapshot[nameof(RuntimeCounter.MetadataAccepted)] = -1;
        Assert.AreEqual(8000L, counters.Snapshot()[nameof(RuntimeCounter.MetadataAccepted)]);
        Assert.HasCount(Enum.GetValues<RuntimeCounter>().Length, snapshot);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(10000)]
    public void UnknownCategoriesCannotBecomeDiagnosticLabels(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RuntimeDiagnosticCounters().Increment((RuntimeCounter)value));
    }

    [TestMethod]
    public async Task SavedReportContainsOnlyVersionTimeAndAllowListedNumericCounts()
    {
        var directory = Directory.CreateTempSubdirectory("shoutkit-diagnostics-");
        try
        {
            var counters = new RuntimeDiagnosticCounters();
            counters.Increment(RuntimeCounter.RecoveryRetryScheduled);
            var path = Path.Combine(directory.FullName, "runtime-counters.json");
            await counters.SaveAsync(path);
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var root = report.RootElement;
            Assert.HasCount(3, root.EnumerateObject().ToArray());
            Assert.AreEqual(1, root.GetProperty("schemaVersion").GetInt32());
            Assert.IsTrue(root.GetProperty("capturedAtUtc").TryGetDateTimeOffset(out _));
            var values = root.GetProperty("counters");
            var expectedNames = Enum.GetNames<RuntimeCounter>();
            Assert.HasCount(expectedNames.Length, values.EnumerateObject().ToArray());
            foreach (var value in values.EnumerateObject())
            {
                CollectionAssert.Contains(expectedNames, value.Name);
                Assert.IsTrue(value.Value.TryGetInt64(out var count) && count >= 0);
            }

            Assert.AreEqual(1L, values.GetProperty(nameof(RuntimeCounter.RecoveryRetryScheduled)).GetInt64());
            counters.Increment(RuntimeCounter.RecoveryRetryScheduled);
            await counters.SaveAsync(path);
            using var replacement = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.AreEqual(2L,
                replacement.RootElement.GetProperty("counters")
                    .GetProperty(nameof(RuntimeCounter.RecoveryRetryScheduled)).GetInt64());
            Assert.HasCount(1, directory.GetFiles());
        }
        finally { directory.Delete(true); }
    }
}
