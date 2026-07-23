using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ClashAutoFix.UI.ViewModels
{
    /// <summary>
    /// Raises PropertyChanged so the window updates automatically when a
    /// value in the view-model changes (the "VM" half of MVVM).
    /// </summary>
    public class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }
    }
}
