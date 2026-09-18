using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using System;
using System.Collections.Generic;

namespace AUTOCAD_COMMANDS
{
    public class AutoCutPreviewTransient : IDisposable
    {
        private readonly List<Entity> _transientEntities = new List<Entity>();
        private readonly IntegerCollection _viewports = new IntegerCollection();
        private bool _isDisplayed;

        public void DisplayPreview(AutoCutAnalysisResult analysis, Editor editor)
        {
            Clear(editor);
            if (analysis == null) return;

            try
            {
                TransientManager tm = TransientManager.CurrentTransientManager;
                if (tm == null) return;

                foreach (AutoCutTargetPlan plan in analysis.TargetPlans)
                {
                    // 1. Show REMOVE pieces in RED (ColorIndex = 1) with heavy lineweight
                    foreach (AutoCutPiece rem in plan.RemovePieces)
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
                        catch
                        {
                        }
                    }

                    // 2. Show intersection points in CYAN (ColorIndex = 4)
                    foreach (Point3d pt in plan.IntersectionPoints)
                    {
                        try
                        {
                            // Small circle marker at intersection
                            Circle marker = new Circle(pt, Vector3d.ZAxis, 3.0)
                            {
                                ColorIndex = 4 // Cyan
                            };
                            tm.AddTransient(marker, TransientDrawingMode.DirectShortTerm, 128, _viewports);
                            _transientEntities.Add(marker);
                        }
                        catch
                        {
                        }
                    }
                }

                _isDisplayed = true;
                Autodesk.AutoCAD.ApplicationServices.Application.UpdateScreen();
            }
            catch
            {
            }
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
                        catch
                        {
                        }
                    }
                }
                _transientEntities.Clear();
                _isDisplayed = false;
                Autodesk.AutoCAD.ApplicationServices.Application.UpdateScreen();
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            Clear(null);
        }
    }
}
