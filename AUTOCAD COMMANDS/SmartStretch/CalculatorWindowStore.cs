using System.Collections.Generic;
using System.Drawing;

namespace AUTOCAD_COMMANDS
{
    internal static class CalculatorWindowStore
    {
        public static bool LoadVisible(bool defaultValue)
        {
            return WorkspaceUiStateStore.TryGetBool("calculator.visible", out bool visible)
                ? visible
                : defaultValue;
        }

        public static bool TryLoadSize(out Size size)
        {
            return WorkspaceUiStateStore.TryGetSize("calculator", out size);
        }

        public static bool TryLoadLocation(out Point location)
        {
            return WorkspaceUiStateStore.TryGetPoint("calculator", out location);
        }

        public static bool TryLoadSplitterDistance(out int distance)
        {
            return WorkspaceUiStateStore.TryGetInt("calculator.splitterDistance", out distance);
        }

        public static void SaveState(bool visible, Point location, Size size, int splitterDistance)
        {
            WorkspaceUiStateStore.SaveValues(
                new Dictionary<string, string>
                {
                    ["calculator.visible"] = visible ? "1" : "0",
                    ["calculator.x"] = WorkspaceUiStateStore.ToInvariant(location.X),
                    ["calculator.y"] = WorkspaceUiStateStore.ToInvariant(location.Y),
                    ["calculator.width"] = WorkspaceUiStateStore.ToInvariant(size.Width),
                    ["calculator.height"] = WorkspaceUiStateStore.ToInvariant(size.Height),
                    ["calculator.splitterDistance"] = WorkspaceUiStateStore.ToInvariant(splitterDistance)
                });
        }
    }
}
