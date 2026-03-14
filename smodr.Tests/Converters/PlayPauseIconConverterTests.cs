using smodr.Converters;

namespace smodr.Tests.Converters;

[TestClass]
public sealed class PlayPauseIconConverterTests
{
    private readonly PlayPauseIconConverter _converter = new();

    [TestMethod]
    public void Convert_WhenPlaying_ReturnsPauseIcon()
    {
        var result = _converter.Convert(true, typeof(string), null!, string.Empty);

        Assert.AreEqual("⏸", result);
    }

    [TestMethod]
    public void Convert_WhenNotPlaying_ReturnsPlayIcon()
    {
        var result = _converter.Convert(false, typeof(string), null!, string.Empty);

        Assert.AreEqual("▶", result);
    }

    [TestMethod]
    public void ConvertBack_PauseIcon_ReturnsTrue()
    {
        var result = _converter.ConvertBack("⏸", typeof(bool), null!, string.Empty);

        Assert.IsTrue((bool)result);
    }

    [TestMethod]
    public void ConvertBack_PlayIcon_ReturnsFalse()
    {
        var result = _converter.ConvertBack("▶", typeof(bool), null!, string.Empty);

        Assert.IsFalse((bool)result);
    }

    [TestMethod]
    public void ConvertBack_UnknownString_ReturnsFalse()
    {
        var result = _converter.ConvertBack("unknown", typeof(bool), null!, string.Empty);

        Assert.IsFalse((bool)result);
    }
}
