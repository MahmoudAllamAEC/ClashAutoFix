using Autodesk.Revit.DB;
using ClashAutoFix.Revit.Detection;
using ClashAutoFix.Revit.Models;
using ClashAutoFix.Revit.Services;
using ClashAutoFix.UI.Commands;

namespace ClashAutoFix.UI.ViewModels
{
    /// <summary>
    /// The brain behind the window. Holds the settings the user picks, runs the
    /// Scan (preview), then runs Create Openings only after the user confirms.
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        private readonly Document _doc;
        private System.Collections.Generic.List<Penetration> _lastScan;

        public OpeningSettings Settings { get; } = new OpeningSettings();

        // ---- tolerance shown in millimetres in the UI ----
        private double _toleranceMm = 50;
        public double ToleranceMm
        {
            get => _toleranceMm;
            set { Set(ref _toleranceMm, value); }
        }
        public bool UseTolerance
        {
            get => Settings.UseTolerance;
            set { Settings.UseTolerance = value; OnPropertyChanged(); }
        }

        // ---- status text shown to the user ----
        private string _status = "Ready. Choose options, then Scan.";
        public string Status { get => _status; set => Set(ref _status, value); }

        public RelayCommand ScanCommand { get; }
        public RelayCommand CreateCommand { get; }

        public MainViewModel(Document doc)
        {
            _doc = doc;
            ScanCommand   = new RelayCommand(_ => Scan());
            CreateCommand = new RelayCommand(_ => Create(), _ => _lastScan != null && _lastScan.Count > 0);
        }

        /// <summary>Preview — count the holes WITHOUT changing the model.</summary>
        private void Scan()
        {
            // mm -> feet before handing to Revit's API, which works in feet.
            Settings.ToleranceFeet = ToleranceMm / 304.8;

            var detector = new IntersectionDetector(_doc);
            _lastScan = detector.Detect(Settings);

            Status = $"Found {_lastScan.Count} openings to create. " +
                     "Ready to  click Create Openings.";
        }

        /// <summary>Actually cut the holes after the user confirms.</summary>
        private void Create()
        {
            if (_lastScan == null || _lastScan.Count == 0) return;

            var service = new OpeningService(_doc);

            // CreateOpenings returns a CreationReport, not a bare count, so a run that places
            // nothing can say WHY (missing sleeve families, and so on) instead of just "0".
            // It also needs the settings: "Cut services running along hosts" is decided per
            // run, not per clash.
            CreationReport report = service.CreateOpenings(_lastScan, Settings);

            var msg = $"{report.Created} openings created.";
            if (report.SkippedExisting > 0)
                msg += $" {report.SkippedExisting} already existed.";
            if (report.SkippedNoFamily > 0)
                msg += $" {report.SkippedNoFamily} skipped — no matching sleeve family loaded" +
                       $" ({string.Join(", ", report.MissingFamilies)}).";
            // Explain cut failures too, so a "0" is never left unexplained.
            if (report.SkippedHostNotCuttable > 0)
                msg += $" {report.SkippedHostNotCuttable} skipped — host cannot be cut with a void.";
            // The service runs along the host instead of through it. Name the switch that
            // turns this on, so the skip is a choice the user can reverse rather than a
            // refusal with no way forward.
            if (report.SkippedRunsAlongHost > 0)
                msg += $" {report.SkippedRunsAlongHost} skipped — the service runs along the host" +
                       " rather than through it. Tick \"Cut services running along hosts\" to" +
                       " cut these too.";
            // The family can't be stretched across the host without a Length parameter.
            if (report.SkippedNoLengthParam > 0)
                msg += $" {report.SkippedNoLengthParam} skipped — the sleeve family needs an" +
                       " instance parameter named \"Length\" to drive the void.";
            if (report.FailedCut > 0)
                msg += $" {report.FailedCut} failed during the cut.";

            Status = msg;

            // Only clear the scan if EVERY penetration became a hole. If anything was
            // skipped or failed for a fixable reason (load the family, add a void, …),
            // keep the scan so the user can fix it and click Create again without re-scanning.
            bool everythingCut = report.Created == _lastScan.Count;
            if (everythingCut)
                _lastScan = null;
        }
    }
}
