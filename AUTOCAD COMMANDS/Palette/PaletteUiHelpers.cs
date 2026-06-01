using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using Autodesk.Windows;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using WF = System.Windows.Forms;
using Media = System.Windows.Media;
using Imaging = System.Windows.Media.Imaging;


namespace AUTOCAD_COMMANDS
{

    internal static class PaletteUiHelpers
    {
        public static string ShowTextPrompt(string title, string label)
        {
            Color backgroundColor = Color.FromArgb(45, 45, 48);
            Color panelColor = Color.FromArgb(37, 37, 38);
            Color foregroundColor = Color.FromArgb(241, 241, 241);

            using (WF.Form form = new WF.Form())
            using (WF.TextBox textBox = new WF.TextBox())
            using (WF.Label textLabel = new WF.Label())
            using (WF.Button okButton = new WF.Button())
            using (WF.Button cancelButton = new WF.Button())
            {
                form.Text = title;
                form.StartPosition = WF.FormStartPosition.CenterParent;
                form.FormBorderStyle = WF.FormBorderStyle.FixedDialog;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(420, 120);
                form.BackColor = backgroundColor;
                form.ForeColor = foregroundColor;

                textLabel.Text = label;
                textLabel.Left = 16;
                textLabel.Top = 16;
                textLabel.Width = 380;
                textLabel.ForeColor = foregroundColor;

                textBox.Left = 16;
                textBox.Top = 42;
                textBox.Width = 380;
                textBox.BackColor = panelColor;
                textBox.ForeColor = foregroundColor;

                okButton.Text = "OK";
                okButton.DialogResult = WF.DialogResult.OK;
                okButton.Left = 240;
                okButton.Top = 80;

                cancelButton.Text = "Cancel";
                cancelButton.DialogResult = WF.DialogResult.Cancel;
                cancelButton.Left = 322;
                cancelButton.Top = 80;

                form.Controls.Add(textLabel);
                form.Controls.Add(textBox);
                form.Controls.Add(okButton);
                form.Controls.Add(cancelButton);
                form.AcceptButton = okButton;
                form.CancelButton = cancelButton;

                return form.ShowDialog() == WF.DialogResult.OK
                    ? textBox.Text
                    : string.Empty;
            }
        }
    }
}
