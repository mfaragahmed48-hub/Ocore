
using System.Windows;

namespace Ocore.desktop.Views
{
    public partial class ExpensesWindow : Window
    {
        public ExpensesWindow()
        {
            InitializeComponent();
        }

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }
    }
}
