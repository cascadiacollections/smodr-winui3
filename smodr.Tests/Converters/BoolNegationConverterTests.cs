using smodr.Converters;

namespace smodr.Tests.Converters;

[TestClass]
public sealed class BoolNegationConverterTests
{
    private readonly BoolNegationConverter _converter = new();

    [TestMethod]
    public void Convert_True_ReturnsFalse()
    {
        var result = _converter.Convert(true, typeof(bool), null!, string.Empty);

        Assert.AreEqual(false, result);
    }

    [TestMethod]
    public void Convert_False_ReturnsTrue()
    {
        var result = _converter.Convert(false, typeof(bool), null!, string.Empty);

        Assert.AreEqual(true, result);
    }

    [TestMethod]
    public void ConvertBack_True_ReturnsFalse()
    {
        var result = _converter.ConvertBack(true, typeof(bool), null!, string.Empty);

        Assert.AreEqual(false, result);
    }

    [TestMethod]
    public void ConvertBack_False_ReturnsTrue()
    {
        var result = _converter.ConvertBack(false, typeof(bool), null!, string.Empty);

        Assert.AreEqual(true, result);
    }
}
