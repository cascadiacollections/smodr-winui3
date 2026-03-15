using Microsoft.UI.Xaml;
using smodr.Converters;

namespace smodr.Tests.Converters;

[TestClass]
public sealed class BoolToVisibilityConverterTests
{
    private readonly BoolToVisibilityConverter _converter = new();

    [TestMethod]
    public void Convert_True_ReturnsVisible()
    {
        var result = _converter.Convert(true, typeof(Visibility), null!, string.Empty);

        Assert.AreEqual(Visibility.Visible, result);
    }

    [TestMethod]
    public void Convert_False_ReturnsCollapsed()
    {
        var result = _converter.Convert(false, typeof(Visibility), null!, string.Empty);

        Assert.AreEqual(Visibility.Collapsed, result);
    }

    [TestMethod]
    public void Convert_TrueWithInvertParameter_ReturnsCollapsed()
    {
        var result = _converter.Convert(true, typeof(Visibility), "True", string.Empty);

        Assert.AreEqual(Visibility.Collapsed, result);
    }

    [TestMethod]
    public void Convert_FalseWithInvertParameter_ReturnsVisible()
    {
        var result = _converter.Convert(false, typeof(Visibility), "True", string.Empty);

        Assert.AreEqual(Visibility.Visible, result);
    }

    [TestMethod]
    public void ConvertBack_Visible_ReturnsTrue()
    {
        var result = _converter.ConvertBack(Visibility.Visible, typeof(bool), null!, string.Empty);

        Assert.IsTrue((bool)result);
    }

    [TestMethod]
    public void ConvertBack_Collapsed_ReturnsFalse()
    {
        var result = _converter.ConvertBack(Visibility.Collapsed, typeof(bool), null!, string.Empty);

        Assert.IsFalse((bool)result);
    }
}
