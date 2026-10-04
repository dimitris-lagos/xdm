using System;
using System.Windows;
using XDM.Wpf.UI.Win32;

namespace XDM.Wpf.UI.Dialogs.DeleteConfirm
{
    public partial class CleanCompletedConfirmDialog : Window
    {
        public CleanCompletedConfirmDialog(int count)
        {
            InitializeComponent();
            Description.Text = $"Remove all {count} completed download{(count == 1 ? "" : "s")} from the list?";
        }

        public bool DontAskAgain => DontAskCheckbox.IsChecked == true;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            NativeMethods.DisableMinMaxButton(this);
        }

        private void Clean_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
