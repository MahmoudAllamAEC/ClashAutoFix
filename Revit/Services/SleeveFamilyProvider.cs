using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;

namespace ClashAutoFix.Revit.Services
{
    /// <summary>
    /// Makes sure the two sleeve families are present in a document. They ship INSIDE
    /// the add-in DLL as embedded resources, so nothing extra has to be installed on
    /// disk. Revit can only load a family from a file path (never a stream), so each
    /// missing family is spilled to a temporary .rfa and loaded from there.
    /// </summary>
    public static class SleeveFamilyProvider
    {
        /// <summary>
        /// Load whichever sleeve families this document doesn't already have.
        /// Must be called with NO transaction open — LoadFamily runs its own.
        /// </summary>
        public static void EnsureLoaded(Document doc)
        {
            if (doc == null || doc.IsFamilyDocument) return;   // can't load into a family editor
            EnsureOne(doc, "CAF_Sleeve_Round", "CAF_Sleeve_Round.rfa");
            EnsureOne(doc, "CAF_Sleeve_Rect",  "CAF_Sleeve_Rect.rfa");
        }

        private static void EnsureOne(Document doc, string familyName, string resourceFile)
        {
            if (IsLoaded(doc, familyName)) return;             // already there — nothing to do

            string tempPath = null;
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                // Match by suffix so we don't depend on MSBuild's exact resource name.
                string res = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith(resourceFile, StringComparison.OrdinalIgnoreCase));
                if (res == null) return;                       // not embedded (build problem)

                // Revit can't load from a stream, so spill the embedded bytes to a temp .rfa.
                tempPath = Path.Combine(Path.GetTempPath(), resourceFile);
                using (Stream src = asm.GetManifestResourceStream(res))
                using (FileStream dst = File.Create(tempPath))
                    src.CopyTo(dst);

                // LoadFamily needs an open transaction to modify the document; without
                // one it fails silently (returns false). Commit on success, roll back
                // on failure so a refused load leaves no trace in the undo history.
                using (var t = new Transaction(doc, "Load Sleeve Family"))
                {
                    t.Start();
                    bool ok = doc.LoadFamily(tempPath, new SilentLoad(), out Family _);
                    if (ok) t.Commit();
                    else    t.RollBack();
                }
            }
            catch
            {
                // Leave it: OpeningService still reports "no matching sleeve family",
                // so the run explains itself and the user can load it by hand.
            }
            finally
            {
                if (tempPath != null && File.Exists(tempPath))
                    try { File.Delete(tempPath); } catch { }
            }
        }

        private static bool IsLoaded(Document doc, string familyName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Any(f => f.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Loads without the interactive "family already exists" prompt.</summary>
        private class SilentLoad : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = true;
                return true;
            }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse,
                out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = true;
                return true;
            }
        }
    }
}
