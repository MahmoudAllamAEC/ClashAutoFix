using System.Windows;
using System.Windows.Media.Imaging;

namespace ClashAutoFix.UI.Views
{
    /// <summary>
    /// Code-behind for the window. Almost empty on purpose — all the logic
    /// lives in MainViewModel (that is what "MVVM" means).
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            TrySetWindowIcon();
        }

        // Use the small embedded project icon as the window's title-bar icon.
        // Loaded from the manifest stream (the icons are EmbeddedResource), the
        // same way the ribbon button loads them — so this doesn't disturb that.
        private void TrySetWindowIcon()
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                using (var s = asm.GetManifestResourceStream("ClashAutoFix.Resources.icons8-merge-horizontal-16.png"))
                {
                    if (s != null)
                        Icon = BitmapFrame.Create(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                }
            }
            catch { /* icon is cosmetic — ignore if it can't be loaded */ }
        }
    }
}
