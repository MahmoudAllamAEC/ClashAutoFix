using System;
using System.IO;
using System.Reflection;

namespace ClashAutoFix.Revit.Entry
{
    /// <summary>
    /// If the add-in uses extra DLLs (e.g. a helper library) placed next to our
    /// own DLL, .NET sometimes fails to find them. This hook tells .NET to look
    /// in the add-in's own folder first.
    /// </summary>
    public static class DependencyResolver
    {
        private static bool _registered;

        public static void Register()
        {
            if (_registered) return;
            _registered = true;
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            string folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string wantedName = new AssemblyName(args.Name).Name + ".dll";
            string candidate = Path.Combine(folder, wantedName);

            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        }
    }
}
