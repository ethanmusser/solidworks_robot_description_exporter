using System.Windows;

namespace SW2RD.UI
{
    public interface IMessageBox
    {
        MessageBoxResult Show(string message);
        MessageBoxResult Show(string message, string caption, MessageBoxButton buttons);
    }
}
