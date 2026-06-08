using System;

namespace SourceGit.Views
{
    public partial class TerminalStandalone : ChromelessWindow
    {
        public TerminalStandalone()
        {
            InitializeComponent();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            if (DataContext is ViewModels.TerminalViewModel vm)
            {
                vm.Dispose();
            }
        }
    }
}
