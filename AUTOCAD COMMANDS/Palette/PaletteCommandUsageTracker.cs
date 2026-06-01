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

    // ======================================================
    // PALETTE COMMAND USAGE TRACKER
    // Đếm số lần dùng command trong DXPALETTE.
    // Theo dõi cả command .NET/DLL và một số command LISP thông qua event AutoCAD.
    // ======================================================
    internal static class PaletteCommandUsageTracker
    {
        private static readonly HashSet<string> KnownCommands =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<IntPtr> HookedDocumentPointers =
            new HashSet<IntPtr>();
        private static readonly Dictionary<IntPtr, string> PendingLispCommands =
            new Dictionary<IntPtr, string>();

        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            DocumentCollection documentManager = Application.DocumentManager;
            documentManager.DocumentCreated += OnDocumentCreated;
            documentManager.DocumentToBeDestroyed += OnDocumentToBeDestroyed;

            foreach (Document document in documentManager)
            {
                HookDocument(document);
            }

            _initialized = true;
        }

        public static void Terminate()
        {
            if (!_initialized)
            {
                return;
            }

            DocumentCollection documentManager = Application.DocumentManager;
            documentManager.DocumentCreated -= OnDocumentCreated;
            documentManager.DocumentToBeDestroyed -= OnDocumentToBeDestroyed;

            foreach (Document document in documentManager)
            {
                UnhookDocument(document);
            }

            HookedDocumentPointers.Clear();
            PendingLispCommands.Clear();
            _initialized = false;
        }

        public static void SetKnownCommands(IEnumerable<string> commandNames)
        {
            KnownCommands.Clear();
            foreach (string commandName in commandNames ?? Enumerable.Empty<string>())
            {
                string normalized = NormalizeCommandName(commandName);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    KnownCommands.Add(normalized);
                }
            }
        }

        private static void OnDocumentCreated(object sender, DocumentCollectionEventArgs e)
        {
            HookDocument(e.Document);
        }

        private static void OnDocumentToBeDestroyed(object sender, DocumentCollectionEventArgs e)
        {
            UnhookDocument(e.Document);
        }

        private static void HookDocument(Document document)
        {
            if (document == null)
            {
                return;
            }

            IntPtr pointer = document.UnmanagedObject;
            if (pointer == IntPtr.Zero || !HookedDocumentPointers.Add(pointer))
            {
                return;
            }

            document.CommandEnded += OnDocumentCommandEnded;
            document.LispWillStart += OnDocumentLispWillStart;
            document.LispEnded += OnDocumentLispEnded;
            document.LispCancelled += OnDocumentLispCancelled;
        }

        private static void UnhookDocument(Document document)
        {
            if (document == null)
            {
                return;
            }

            IntPtr pointer = document.UnmanagedObject;
            if (pointer != IntPtr.Zero && HookedDocumentPointers.Remove(pointer))
            {
                document.CommandEnded -= OnDocumentCommandEnded;
                document.LispWillStart -= OnDocumentLispWillStart;
                document.LispEnded -= OnDocumentLispEnded;
                document.LispCancelled -= OnDocumentLispCancelled;
                PendingLispCommands.Remove(pointer);
            }
        }

        private static void OnDocumentCommandEnded(object sender, CommandEventArgs e)
        {
            string commandName = NormalizeCommandName(e?.GlobalCommandName);
            if (string.IsNullOrWhiteSpace(commandName) || !KnownCommands.Contains(commandName))
            {
                return;
            }

            int usageCount = PaletteUsageStore.Increment(commandName);
            DungXPaletteHost.NotifyCommandUsage(commandName, usageCount);
        }

        private static void OnDocumentLispWillStart(object sender, LispWillStartEventArgs e)
        {
            Document document = sender as Document;
            if (document == null)
            {
                return;
            }

            string commandName = TryResolveKnownLispCommandName(e?.FirstLine);
            if (string.IsNullOrWhiteSpace(commandName))
            {
                return;
            }

            PendingLispCommands[document.UnmanagedObject] = commandName;
        }

        private static void OnDocumentLispEnded(object sender, EventArgs e)
        {
            CompletePendingLispCommand(sender as Document);
        }

        private static void OnDocumentLispCancelled(object sender, EventArgs e)
        {
            Document document = sender as Document;
            if (document == null)
            {
                return;
            }

            PendingLispCommands.Remove(document.UnmanagedObject);
        }

        private static void CompletePendingLispCommand(Document document)
        {
            if (document == null)
            {
                return;
            }

            IntPtr pointer = document.UnmanagedObject;
            if (pointer == IntPtr.Zero ||
                !PendingLispCommands.TryGetValue(pointer, out string commandName) ||
                string.IsNullOrWhiteSpace(commandName))
            {
                return;
            }

            PendingLispCommands.Remove(pointer);
            int usageCount = PaletteUsageStore.Increment(commandName);
            DungXPaletteHost.NotifyCommandUsage(commandName, usageCount);
        }

        private static string NormalizeCommandName(string commandName)
        {
            string normalized = (commandName ?? string.Empty).Trim();
            while (normalized.StartsWith(".", StringComparison.Ordinal) ||
                   normalized.StartsWith("_", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(1);
            }

            return normalized;
        }

        private static string TryResolveKnownLispCommandName(string firstLine)
        {
            string normalizedLine = NormalizeCommandName(firstLine);
            if (!string.IsNullOrWhiteSpace(normalizedLine) && KnownCommands.Contains(normalizedLine))
            {
                return normalizedLine;
            }

            string line = (firstLine ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            Match cCommandMatch = Regex.Match(
                line,
                @"(?i)(?:\(\s*defun\s+[cC]:|[cC]:)(?<name>[a-z0-9_\-$]+)");
            if (cCommandMatch.Success)
            {
                string candidate = NormalizeCommandName(cCommandMatch.Groups["name"].Value);
                if (KnownCommands.Contains(candidate))
                {
                    return candidate;
                }
            }

            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("(", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring(1).TrimStart();
            }

            string leadingToken = new string(
                trimmed.TakeWhile(ch =>
                    !char.IsWhiteSpace(ch) &&
                    ch != '(' &&
                    ch != ')' &&
                    ch != '"' &&
                    ch != '\'')
                .ToArray());

            string normalizedToken = NormalizeCommandName(leadingToken);
            return KnownCommands.Contains(normalizedToken)
                ? normalizedToken
                : null;
        }
    }
}
