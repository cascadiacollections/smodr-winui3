using System.Reflection;

namespace smodr.Tests.Ui;

[TestClass]
public sealed class ProgramEntryPointTests
{
    [TestMethod]
    public void ProcessEntryPointRunsXamlOnAnStaThread()
    {
        // An async Main compiles to a synthesized entry point without [STAThread];
        // XAML then runs MTA and out-of-process UI Automation crashes the app.
        var entryPoint = typeof(Program).Assembly.EntryPoint;

        Assert.IsNotNull(entryPoint);
        Assert.AreEqual(nameof(Program.Main), entryPoint.Name);
        Assert.IsNotNull(entryPoint.GetCustomAttribute<STAThreadAttribute>());
    }
}
