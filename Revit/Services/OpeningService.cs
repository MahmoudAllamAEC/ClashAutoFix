using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using ClashAutoFix.Revit.Models;

namespace ClashAutoFix.Revit.Services
{
    /// <summary>
    /// Makes the holes. Takes the list of penetrations from the detector and places
    /// a "sleeve" void family at each one, inside a single Transaction so it is one
    /// clean Undo.
    /// </summary>
    public class OpeningService
    {
        private readonly Document _doc;

        // Names of the void families the tool places (loaded into the project).
        private const string RoundFamily = "CAF_Sleeve_Round";
        private const string RectFamily  = "CAF_Sleeve_Rect";

        // How far the void pokes out past each face of the host. Boolean cuts dislike faces
        // that land exactly on top of each other, so we always overshoot a little.
        //
        // This is ONLY that clearance — it deliberately does not scale with the crossing
        // angle. An angled crossing needs roughly "radius x tan(angle)" of extra reach at
        // each end, and detection already supplies it: the solid-to-solid overlap in
        // GeometryHelper.TryFindPierce is longer than the centre-line chord by exactly that
        // amount. Do not add trigonometry here; it would double-count.
        private const double OverShootFeet = 50.0 / 304.8;   // 50 mm past each end

        // If the service crosses more than this far off the host's "through" direction it is
        // running ALONG the host, not through it (e.g. a pipe buried in a slab). Cutting a
        // trench the length of that run would be wrong, so those are skipped and reported.
        private const double GrazingAngleDegrees = 75.0;

        public OpeningService(Document doc)
        {
            _doc = doc;
        }

        /// <summary>
        /// Places a sleeve for each penetration and reports what happened. Each skip is
        /// recorded against its cause in a CreationReport, so a run that places nothing can
        /// explain the 0 instead of leaving it a mystery.
        ///
        /// The void is placed UNHOSTED at the middle of the crossing segment, AIMED along
        /// that segment, and STRETCHED to its length — so the hole follows the service at any
        /// angle rather than being punched square through the host. (A face-hosted sleeve
        /// would be pinned to the host's FACE NORMAL, throwing the crossing angle away and
        /// leaving every angled service still clashing.) The segment already carries both the
        /// host thickness and the angle, so there is no trigonometry anywhere in here.
        /// </summary>
        public CreationReport CreateOpenings(IList<Penetration> penetrations, OpeningSettings settings)
        {
            var report = new CreationReport();

            // Make sure the sleeve families are in the document before we start cutting.
            // Family loading opens its own transaction, which cannot nest inside ours —
            // so this must run before tx.Start().
            SleeveFamilyProvider.EnsureLoaded(_doc);

            using (var tx = new Transaction(_doc, "Clash Auto Fix — Create Openings"))
            {
                tx.Start();
                try
                {
                    foreach (var p in penetrations)
                    {
                        // Each clash is handled in its own try/catch, so one bad clash is
                        // counted (FailedCut) and the rest still go through instead of rolling
                        // back the whole run. 'placed' is declared out here so the catch can
                        // remove a half-made sleeve (no orphans).
                        FamilyInstance placed = null;
                        try
                        {
                            FamilySymbol symbol = GetSymbol(p.Shape);
                            if (symbol == null)
                            {
                                // Record the exact family name we looked for so the UI can say
                                // which family to load, instead of a bare "0".
                                report.SkippedNoFamily++;
                                string missing = p.Shape == OpeningShape.Round ? RoundFamily : RectFamily;
                                if (!report.MissingFamilies.Contains(missing))
                                    report.MissingFamilies.Add(missing);
                                continue;
                            }
                            if (!symbol.IsActive) symbol.Activate();

                            // A service running ALONG the host is usually a routing problem, not
                            // a penetration — aligning to the segment would faithfully carve a
                            // trench the whole length of the run. It is skipped by default, but
                            // the user can ask for it: a service buried in a slab is a real
                            // chase, and the geometry handles it fine.
                            if (!settings.CutServicesAlongHosts && RunsAlongHost(p))
                            {
                                report.SkippedRunsAlongHost++;
                                continue;
                            }

                            // Check the host can take a void cut BEFORE placing anything, so we
                            // never leave a useless sleeve behind.
                            if (!InstanceVoidCutUtils.CanBeCutWithVoid(p.Host.Element))
                            {
                                report.SkippedHostNotCuttable++;
                                continue;
                            }

                            // Skip if we already put a sleeve here (safe re-runs).
                            if (OpeningAlreadyExists(p.Point))
                            {
                                report.SkippedExisting++;
                                continue;
                            }

                            // Place UNHOSTED, at the middle of the crossing segment. Face hosting
                            // is exactly what pins the void perpendicular to the host; placing it
                            // free is what lets us aim it along the service.
                            placed = _doc.Create.NewFamilyInstance(
                                p.Point, symbol, StructuralType.NonStructural);

                            // Size it — cross-section AND length along the service.
                            if (!SetSize(placed, p))
                            {
                                // The family has no "Length" instance parameter, so the void can't
                                // be stretched across the host. Say so instead of cutting nothing.
                                report.SkippedNoLengthParam++;
                                _doc.Delete(placed.Id);
                                placed = null;
                                continue;
                            }
                            _doc.Regenerate();

                            // Put it exactly on the segment midpoint (placement can drift).
                            SnapTo(placed, p.Point);
                            _doc.Regenerate();

                            // THE POINT OF ALL THIS — aim the void along the crossing.
                            AimAlong(placed, p.Point, p.Direction);
                            _doc.Regenerate();

                            // Aiming the axis is the whole job for a circle, but a rectangle also
                            // has to be SPUN about that axis so its cross-section squares up with
                            // the service. AimAlong applies the MINIMAL rotation, and the roll
                            // that falls out of it equals the crossing angle: zero for a
                            // perpendicular duct (which is why those look perfect) but 45 degrees
                            // for a duct crossing at 45, leaving the duct poking out of the void's
                            // corners and still clashing without this extra roll.
                            if (p.Shape == OpeningShape.Rectangular)
                            {
                                RollTo(placed, p.Point, p.Direction, ServiceUp(p));
                                _doc.Regenerate();
                            }

                            // The cut itself. A void placed by the API never cuts on its own.
                            bool cut = CutHost(p.Host.Element, placed);

                            // Keep the sleeve either way — it is positioned correctly, so even if
                            // the cut failed it is visible and can be cut by hand. Report truthfully.
                            p.CreatedOpeningId = placed.Id;
                            placed = null;
                            if (cut) report.Created++;
                            else     report.FailedCut++;
                        }
                        catch
                        {
                            // This one clash failed (bad geometry, wrong family type, …). Count it,
                            // remove the half-made sleeve, and carry on with the rest of the run.
                            report.FailedCut++;
                            if (placed != null)
                            {
                                try { _doc.Delete(placed.Id); } catch { /* already gone */ }
                            }
                        }
                    }

                    tx.Commit();        // keep every hole we managed to cut
                }
                catch
                {
                    tx.RollBack();      // catastrophic failure only -> leave the model untouched
                    throw;
                }
            }
            return report;
        }

        private FamilySymbol GetSymbol(OpeningShape shape)
        {
            string wanted = shape == OpeningShape.Round ? RoundFamily : RectFamily;
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(fs => fs.FamilyName == wanted);
        }

        /// <summary>
        /// Is the service running ALONG the host rather than through it? A pipe lying inside
        /// a slab crosses at ~90° to the slab's "through" direction and produces a very long
        /// inside-segment; turning that into an opening would carve a trench the whole length
        /// of the run, so we flag it instead.
        /// </summary>
        private bool RunsAlongHost(Penetration p)
        {
            XYZ normal = HostNormal(p);
            if (normal == null) return false;   // no reliable normal -> don't apply the test

            // 1 = a dead-perpendicular crossing, 0 = running flat along the host.
            double cos = Math.Abs(p.Direction.DotProduct(normal));
            double limit = Math.Cos(GrazingAngleDegrees * Math.PI / 180.0);
            return cos < limit;
        }

        /// <summary>
        /// The "through" direction of a plate-like host. Walls carry their own orientation;
        /// floors and ceilings are horizontal. Beams and columns have no single plate normal,
        /// so we return null and simply don't apply the grazing test to them.
        /// </summary>
        private XYZ HostNormal(Penetration p)
        {
            switch (p.Host.Category)
            {
                case BuiltInCategory.OST_Walls:
                    return (p.Host.Element as Wall)?.Orientation;
                case BuiltInCategory.OST_Floors:
                case BuiltInCategory.OST_Ceilings:
                    return XYZ.BasisZ;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Push the sizes into the family. Returns false if the family has no "Length"
        /// instance parameter, which means the void cannot be stretched across the host.
        ///
        /// The length comes straight from the crossing segment. That single number already
        /// contains the host thickness AND the crossing angle, which is why a 45° pipe needs
        /// no special case — it just produces a longer segment.
        /// </summary>
        private static bool SetSize(FamilyInstance fi, Penetration p)
        {
            // These parameter names must match the ones in the sleeve family.
            if (p.Shape == OpeningShape.Round)
            {
                fi.LookupParameter("Diameter")?.Set(p.Width);
            }
            else
            {
                fi.LookupParameter("Width")?.Set(p.Width);
                fi.LookupParameter("Height")?.Set(p.Height);
            }

            // "Depth" is accepted as an alias in case the family names it that way.
            Parameter length = fi.LookupParameter("Length") ?? fi.LookupParameter("Depth");
            if (length == null || length.IsReadOnly) return false;

            // Overshoot both ends so the void breaks cleanly out of the host's faces.
            length.Set(p.SegmentLength + 2 * OverShootFeet);
            return true;
        }

        /// <summary>
        /// Force a freshly placed instance onto an exact point. The unhosted overload can
        /// drop the instance somewhere slightly different from what we asked for; this moves
        /// it back. Idempotent — a correct placement moves 0.
        /// </summary>
        private void SnapTo(FamilyInstance fi, XYZ target)
        {
            if (fi.Location is LocationPoint lp)
            {
                XYZ delta = target - lp.Point;
                if (!delta.IsZeroLength())
                    ElementTransformUtils.MoveElement(_doc, fi.Id, delta);
            }
        }

        /// <summary>
        /// Rotate the void so its through-axis lies along the crossing segment. This is what
        /// makes angled penetrations work: the hole follows the service instead of being
        /// punched square through the host.
        ///
        /// Round voids only need this aim. A rectangular void also wants its cross-section
        /// squared to the duct — see RollTo, which runs straight after this one.
        /// </summary>
        private void AimAlong(FamilyInstance fi, XYZ origin, XYZ direction)
        {
            // Where the void points right now. Reading it off the instance is safer than
            // assuming: for a work-plane-based family this is the extrusion direction.
            // If your family extrudes along a different local axis, swap BasisZ for BasisX/Y.
            XYZ from = fi.GetTransform().BasisZ;
            XYZ to = direction.Normalize();

            double dot = from.DotProduct(to);
            if (dot > 0.99999) return;              // already aimed correctly

            XYZ axis;
            double angle;
            if (dot < -0.99999)
            {
                // Exactly opposite: spin 180° about ANY axis perpendicular to 'from'. Pick a
                // world axis that isn't parallel to it, otherwise the cross product is zero.
                axis = Math.Abs(from.DotProduct(XYZ.BasisZ)) > 0.9
                     ? from.CrossProduct(XYZ.BasisX).Normalize()
                     : from.CrossProduct(XYZ.BasisZ).Normalize();
                angle = Math.PI;
            }
            else
            {
                axis = from.CrossProduct(to).Normalize();
                angle = Math.Acos(dot);
            }

            ElementTransformUtils.RotateElement(_doc, fi.Id, Line.CreateUnbound(origin, axis), angle);
        }

        /// <summary>
        /// Spin the void about its own axis so its local Y — the "Height" direction of the
        /// sleeve family — lines up with the service's own up direction. Only rectangles need
        /// this; a rolled circle is still a circle.
        /// </summary>
        private void RollTo(FamilyInstance fi, XYZ origin, XYZ axis, XYZ targetUp)
        {
            if (targetUp == null) return;

            // Work in the plane square to the axis. Without stripping out the along-the-axis
            // part first, the angle between the two directions is not a pure roll.
            XYZ current = Flatten(fi.GetTransform().BasisY, axis);
            XYZ wanted  = Flatten(targetUp, axis);
            if (current == null || wanted == null) return;

            // Acos is undefined a hair outside [-1,1], which rounding can produce.
            double dot = current.DotProduct(wanted);
            if (dot > 1.0) dot = 1.0;
            else if (dot < -1.0) dot = -1.0;

            double angle = Math.Acos(dot);
            if (angle < 1e-9) return;      // already square

            // Acos only gives the size of the angle. The cross product gives the direction.
            if (current.CrossProduct(wanted).DotProduct(axis) < 0) angle = -angle;

            ElementTransformUtils.RotateElement(_doc, fi.Id, Line.CreateUnbound(origin, axis), angle);
        }

        /// <summary>Drop the part of v that runs along the axis, leaving the part square to it.</summary>
        private static XYZ Flatten(XYZ v, XYZ axis)
        {
            XYZ flat = v - axis * v.DotProduct(axis);
            return flat.GetLength() < 1e-9 ? null : flat.Normalize();
        }

        /// <summary>
        /// Which way is "up" across the service's cross-section? A duct's connector carries
        /// its own coordinate system, which stays honest even for a duct that has been rolled
        /// or sloped. Failing that we use world up, which is right for the level ducts that
        /// make up nearly all real routing.
        /// </summary>
        private static XYZ ServiceUp(Penetration p)
        {
            try
            {
                var curve = p.Mep.Element as MEPCurve;
                ConnectorSet connectors = curve?.ConnectorManager?.Connectors;

                if (connectors != null)
                {
                    foreach (Connector c in connectors)
                    {
                        // BasisZ runs along the duct; BasisY spans the cross-section's height.
                        // If openings come out rolled exactly 90 degrees, swap this for BasisX.
                        XYZ up = c.CoordinateSystem?.BasisY;
                        if (up != null && !up.IsZeroLength()) return up;
                    }
                }
            }
            catch
            {
                // Some connectors carry no usable coordinate system. Fall through to world up.
            }

            return XYZ.BasisZ;
        }

        /// <summary>
        /// Cut the host with the placed void, and say whether it worked. A void placed through
        /// the API never cuts on its own — the cut has to be asked for explicitly.
        /// </summary>
        private bool CutHost(Element host, FamilyInstance voidInstance)
        {
            // Already cut -> nothing to do, success.
            if (InstanceVoidCutUtils.InstanceVoidCutExists(host, voidInstance))
                return true;

            try
            {
                InstanceVoidCutUtils.AddInstanceVoidCut(_doc, host, voidInstance);
                _doc.Regenerate();
                return true;
            }
            catch
            {
                // e.g. the family carries no usable void, or its void misses the host.
                return false;
            }
        }

        /// <summary>
        /// Simple guard: is there already a sleeve within 10 mm of this point?
        /// The caller passes the crossing midpoint, which a re-run recomputes identically,
        /// so an existing sleeve is recognised instead of a duplicate being made.
        /// </summary>
        private bool OpeningAlreadyExists(XYZ where)
        {
            const double near = 10.0 / 304.8; // mm -> feet
            var nearby = new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(fi => fi.Symbol != null &&
                             (fi.Symbol.FamilyName == RoundFamily || fi.Symbol.FamilyName == RectFamily));

            foreach (var fi in nearby)
            {
                if ((fi.Location as LocationPoint)?.Point is XYZ pt &&
                    pt.DistanceTo(where) < near)
                    return true;
            }
            return false;
        }
    }
}
