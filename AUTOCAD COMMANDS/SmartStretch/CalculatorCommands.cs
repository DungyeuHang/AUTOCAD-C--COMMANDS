using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Windows.Forms;

namespace AUTOCAD_COMMANDS
{
    public class CalculatorCommands
    {
        private const int MaxStartupShowAttempts = 120;

        private static QuickCalculatorForm _calculatorForm;
        private static bool _startupShowHooked;
        private static int _startupShowAttempts;
        private static DateTime _nextStartupShowAttemptUtc;

        [CommandMethod("DXCALC")]
        public void ShowCalculator()
        {
            ShowCalculatorCore();
        }

        public static void InitializeApplication()
        {
            var ed = AcAp.DocumentManager.MdiActiveDocument?.Editor;
            WorkspaceUiStateStore.Initialize(ed);

            // DXCALC is a startup tool.  A fresh state file must not make it
            // disappear merely because it has never been shown before.
            if (CalculatorWindowStore.LoadVisible(defaultValue: true))
            {
                ScheduleStartupShow();
            }
        }

        public static void TerminateApplication()
        {
            RemoveStartupShowHook();

            if (_calculatorForm != null && !_calculatorForm.IsDisposed)
            {
                bool persistedVisible = WorkspaceUiStateStore.TryGetBool("calculator.visible", out bool visible)
                    ? visible
                    : _calculatorForm.Visible;
                _calculatorForm.SetUserVisible(persistedVisible);
                _calculatorForm.SaveCurrentState();
                _calculatorForm.CloseForShutdown();
                _calculatorForm = null;
            }

            var ed = AcAp.DocumentManager.MdiActiveDocument?.Editor;
            WorkspaceUiStateStore.Commit(ed);
        }

        private static void ShowCalculatorCore()
        {
            EnsureCalculatorExists();

            if (_calculatorForm == null || _calculatorForm.IsDisposed)
            {
                return;
            }

            _calculatorForm.SetUserVisible(true);
            _calculatorForm.RestoreSavedState();

            if (!_calculatorForm.Visible)
            {
                AcAp.ShowModelessDialog(_calculatorForm);
            }

            if (_calculatorForm.WindowState == FormWindowState.Minimized)
            {
                _calculatorForm.WindowState = FormWindowState.Normal;
            }

            _calculatorForm.Activate();
            _calculatorForm.SaveCurrentState();
        }

        private static void EnsureCalculatorExists()
        {
            if (_calculatorForm == null || _calculatorForm.IsDisposed)
            {
                _calculatorForm = new QuickCalculatorForm();
                _calculatorForm.FormClosed += (s, e) =>
                {
                    if (ReferenceEquals(_calculatorForm, s))
                    {
                        _calculatorForm = null;
                    }
                };
            }
        }

        private static void ScheduleStartupShow()
        {
            if (_startupShowHooked)
            {
                return;
            }

            _startupShowAttempts = 0;
            _nextStartupShowAttemptUtc = DateTime.MinValue;
            AcAp.Idle += ShowCalculatorOnIdle;
            _startupShowHooked = true;
        }

        private static void RemoveStartupShowHook()
        {
            if (!_startupShowHooked)
            {
                return;
            }

            AcAp.Idle -= ShowCalculatorOnIdle;
            _startupShowHooked = false;
        }

        private static void ShowCalculatorOnIdle(object sender, EventArgs e)
        {
            if (!CalculatorWindowStore.LoadVisible(defaultValue: true))
            {
                RemoveStartupShowHook();
                return;
            }

            // Idle can occur before AutoCAD has created a drawing document.
            // Do not spend the retry budget until modeless dialogs are usable.
            if (AcAp.DocumentManager.MdiActiveDocument == null)
            {
                return;
            }

            // Idle can be raised repeatedly in one UI turn.  Space retries out
            // so a not-yet-ready modeless host does not consume every attempt.
            if (DateTime.UtcNow < _nextStartupShowAttemptUtc)
            {
                return;
            }

            _nextStartupShowAttemptUtc = DateTime.UtcNow.AddMilliseconds(250);

            _startupShowAttempts++;

            try
            {
                ShowCalculatorCore();
                if (_calculatorForm != null && !_calculatorForm.IsDisposed && _calculatorForm.Visible)
                {
                    RemoveStartupShowHook();
                    return;
                }
            }
            catch
            {
                // AutoCAD can raise Idle before the main frame is ready for modeless forms.
            }

            if (_startupShowAttempts >= MaxStartupShowAttempts)
            {
                RemoveStartupShowHook();
            }
        }
    }
}
