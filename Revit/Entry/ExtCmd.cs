using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClashAutoFix.UI.ViewModels;
using ClashAutoFix.UI.Views;

namespace ClashAutoFix.Revit.Entry
{
    /// <summary>
    /// The click doorway. Revit runs Execute() every time the user clicks the
    /// ribbon button; it opens the tool's window and hands it the current document.
    /// </summary>
    [Transaction(TransactionMode.Manual)]   // we open transactions ourselves
    public class ExtCmd : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.Application;
                UIDocument uiDoc = uiApp.ActiveUIDocument;
                Document doc = uiDoc.Document;

                // Build the view-model (the "brain behind the window") and show it.
                var vm = new MainViewModel(doc);
                var window = new MainWindow { DataContext = vm };

                // Own the dialog to Revit's main window so it always stays in front of
                // the document instead of slipping behind it, and minimises/restores
                // together with Revit.
                new System.Windows.Interop.WindowInteropHelper(window).Owner = uiApp.MainWindowHandle;

                window.ShowDialog();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                // Never crash Revit — report the error politely instead.
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
