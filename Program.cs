using System.Reflection;
using GTranslate;
namespace KenshiTranslator;

static class Program
{

    [STAThread]
    static void Main()
    {
        
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }    
}