using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using System;
using System.Collections.Generic;

namespace AUTOCAD_COMMANDS
{
    public class AutoTrimPreviewTransient : IDisposable
    {
        private readonly List<Entity> _transientEntities = new List<Entity>();
        private readonly IntegerCollection _viewports = new IntegerCollection();
        private bool _isDisplayed;

        public void DisplayPreview(AutoTrimAnalysisResult analysis, Editor editor)
        {
            Clear(editor);
            if (analysis == null) return;

            try
            {
                TransientManager tm = TransientManager.CurrentTransientManager;
                if (tm == null) return;

                foreach (AutoTrimEntityPlan plan in analysis.EntityPlans)
                {
                    if (plan.Action == AutoTrimAction.KeepUnchanged) continue;

                    if (plan.Action == AutoTrimAction.EraseCompletely)
                    {
                        // Ghost entire erased entity in RED
                        if (plan.OriginalCurve != null && !plan.OriginalCurve.IsDisposed)
                        {
                            try
                            {
                                Curve ghost = plan.OriginalCurve.Clone() as Curve;
                                if (ghost != null)
                                {
                                    ghost.ColorIndex = 1; // Red
                                    ghost.LineWeight = LineWeight.LineWeight060;
                                    tm.AddTransient(ghost, TransientDrawingMode.DirectShortTerm, 128, _viewports);
                                    _transientEntities.Add(ghost);
                                }
                            }
                            catch { }
                        }
                    }
                    else if (plan.Action == AutoTrimAction.SplitAndKeepSome)
                    {
                        // Ghost removed pieces in RED
                        foreach (AutoTrimPiece rem in plan.RemovePieces)
                        {
                            if (rem.Curve == null || rem.Curve.IsDisposed) continue;
                            try
                            {
                                Curve ghost = rem.Curve.Clone() as Curve;
                                if (ghost != null)
                                {
                                    ghost.ColorIndex = 1; // Red
                                    ghost.LineWeight = LineWeight.LineWeight060;
                                    tm.AddTransient(ghost, TransientDrawingMode.DirectShortTerm, 128, _viewports);
                                    _transientEntities.Add(ghost);
                                }
                            }
                            catch { }
                        }

                        // Ghost intersection points in CYAN
                        foreach (Point3d pt in plan.IntersectionPoints)
                        {
                            try
                            {
                                Circle marker = new Circle(pt, Vector3d.ZAxis, 3.0)
                                {
                                    ColorIndex = 4 // Cyan
                                };
                                tm.AddTransient(marker, TransientDrawingMode.DirectShortTerm, 128, _viewports);
                                _transientEntities.Add(marker);
                            }
                            catch { }
                        }
                    }
                }

                _isDisplayed = true;
                Autodesk.AutoCAD.ApplicationServices.Application.UpdateScreen();
            }
            catch { }
        }

        public void Clear(Editor editor)
        {
            if (!_isDisplayed && _transientEntities.Count == 0) return;

            try
            {
                TransientManager tm = TransientManager.CurrentTransientManager;
                if (tm != null)
                {
                    foreach (Entity ent in _transientEntities)
                    {
                        try
                        {
                            tm.EraseTransient(ent, _viewports);
                            ent.Dispose();
                        }
                        catch { }
                    }
                }
                _transientEntities.Clear();
                _isDisplayed = false;
                Autodesk.AutoCAD.ApplicationServices.Application.UpdateScreen();
            }
            catch { }
        }

        public void Dispose()
        {
            Clear(null);
        }
    }
}

