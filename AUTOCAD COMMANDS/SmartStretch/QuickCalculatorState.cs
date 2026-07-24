using System;

namespace AUTOCAD_COMMANDS
{
    internal static class QuickCalculatorState
    {
        private static readonly object SyncRoot = new object();
        private static double? _lastValue;
        private static WeakReference<QuickCalculatorForm> _formInstance;

        public static void RegisterForm(QuickCalculatorForm form)
        {
            if (form == null)
            {
                return;
            }

            lock (SyncRoot)
            {
                _formInstance = new WeakReference<QuickCalculatorForm>(form);
            }
        }

        public static void UnregisterForm(QuickCalculatorForm form)
        {
            lock (SyncRoot)
            {
                if (_formInstance == null)
                {
                    return;
                }

                if (!_formInstance.TryGetTarget(out QuickCalculatorForm currentForm) ||
                    ReferenceEquals(currentForm, form))
                {
                    _formInstance = null;
                }
            }
        }

        public static void SetLastValue(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return;
            }

            lock (SyncRoot)
            {
                _lastValue = value;
            }
        }

        public static bool TryGetLastValue(out double value)
        {
            lock (SyncRoot)
            {
                if (_lastValue.HasValue)
                {
                    value = _lastValue.Value;
                    return true;
                }
            }

            value = 0.0;
            return false;
        }

        public static bool TryGetCurrentDisplayValue(out double value)
        {
            QuickCalculatorForm form = null;

            lock (SyncRoot)
            {
                if (_formInstance != null)
                {
                    _formInstance.TryGetTarget(out form);
                }
            }

            if (form != null && !form.IsDisposed)
            {
                return form.TryGetCurrentDisplayValue(out value);
            }

            value = 0.0;
            return false;
        }

        public static void ClearLastValue()
        {
            lock (SyncRoot)
            {
                _lastValue = null;
            }
        }
    }
}
