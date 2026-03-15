namespace smodr.Tests;

[TestClass]
public sealed class AppConstantsTests
{
    [TestMethod]
    public void UserAgent_IsNotNullOrEmpty()
    {
        Assert.IsFalse(string.IsNullOrEmpty(AppConstants.UserAgent));
    }

    [TestMethod]
    public void UserAgent_ContainsAppName()
    {
        StringAssert.Contains(AppConstants.UserAgent, "smodr", StringComparison.Ordinal);
    }

    [TestMethod]
    public void UserAgent_ContainsGitHubUrl()
    {
        StringAssert.Contains(AppConstants.UserAgent, "github.com", StringComparison.Ordinal);
    }
}
