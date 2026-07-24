using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;

namespace AUTOCAD_COMMANDS
{
    public class CalculatorCommands : IExtensionApplication
    {
        private static QuickCalculatorForm _calculatorForm;

        [CommandMethod("DXCALC")]
        public void ShowCalculator()
        {
            // If the form already exists and is not disposed, just show and activate it.
            // This handles the case where the user has hidden the form by clicking the 'X' button.
            if (_calculatorForm != null && !_calculatorForm.IsDisposed)
            {
                if (!_calculatorForm.Visible)
                {
                    AcAp.ShowModelessDialog(_calculatorForm);
                }

                if (_calculatorForm.WindowState == System.Windows.Forms.FormWindowState.Minimized)
                {
                    _calculatorForm.WindowState = System.Windows.Forms.FormWindowState.Normal;
                }

                _calculatorForm.Activate();
            }
            // Otherwise, create a new instance of the form.
            else
            {
                QuickCalculatorForm form = new QuickCalculatorForm();
                _calculatorForm = form;
                form.FormClosed += (s, e) =>
                {
                    if (ReferenceEquals(_calculatorForm, form))
                    {
                        _calculatorForm = null;
                    }
                };
                AcAp.ShowModelessDialog(form);
            }
        }

        public void Initialize()
        {
            // This method is called when the assembly is loaded.
        }

        public void Terminate()
        {
            // This method is called when the assembly is unloaded.
            if (_calculatorForm != null && !_calculatorForm.IsDisposed)
            {
                _calculatorForm.CloseForShutdown();
                _calculatorForm = null;
            }
        }
    }
}
