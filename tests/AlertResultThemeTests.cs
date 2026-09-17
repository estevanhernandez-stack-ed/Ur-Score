using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;
using Labs626.UrScore.UI;

namespace UrScore.Tests;

public class AlertResultThemeTests
{
    [Fact]
    public void OrphanResultUsesItsFailureStateAndLivePalette()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var directory = new DirectoryInfo(AppContext.BaseDirectory);
                while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ur-Score.csproj"))) directory = directory.Parent;
                Assert.NotNull(directory);
                var pagePath = Path.Combine(directory.FullName, "src", "UI", "Setup", "AlertsPage.xaml");
                XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
                var page = XDocument.Load(pagePath);
                var line = page.Descendants(presentation + "TextBlock").Single(element => (string?)element.Attribute(xaml + "Name") == "AlertsResultLine");
                var text = (TextBlock)XamlReader.Parse(line.ToString());
                Assert.Contains("AlertsResultLine.DataContext = _ui;", File.ReadAllText(pagePath + ".cs"));
                text.Visibility = Visibility.Visible;

                foreach (var problem in new[] { false, true, false, true })
                {
                    text.DataContext = new AlertsUi(ResultIsProblem: problem);
                    var body = new SolidColorBrush(Colors.White);
                    var refusal = new SolidColorBrush(Colors.Magenta);
                    text.Resources["WhiteBrush"] = body;
                    text.Resources["MagentaBrush"] = refusal;
                    text.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                    Assert.Same(problem ? refusal : body, text.Foreground);
                    var replacement = new SolidColorBrush(Colors.Cyan);
                    text.Resources[problem ? "MagentaBrush" : "WhiteBrush"] = replacement;
                    Assert.Same(replacement, text.Foreground);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
