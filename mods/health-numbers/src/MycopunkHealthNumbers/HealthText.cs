using System.Globalization;

namespace MycopunkHealthNumbers
{
    /// <summary>
    /// Builds the "health / max" text and keeps it until one of the inputs changes, so that a label that is
    /// refreshed every frame does not build a new string every frame. One instance per label.
    /// </summary>
    internal sealed class HealthText
    {
        private string _text;
        private float _health = float.NaN;
        private float _max = float.NaN;
        private float _cap = float.NaN;
        private int _decimals = -1;
        private string _format;
        private string _capFormat;

        /// <param name="cap">Temporary max-health cap as a fraction (1 = none). Shown only when below 1 and capFormat is not empty.</param>
        public string Get(float health, float max, float cap, int decimals, string format, string capFormat)
        {
            // The game only applies the cap when it is below 1.
            if (cap >= 1f || string.IsNullOrEmpty(capFormat))
                cap = 1f;
            if (_text != null && health == _health && max == _max && cap == _cap &&
                decimals == _decimals && format == _format && capFormat == _capFormat)
                return _text;

            _health = health;
            _max = max;
            _cap = cap;
            _decimals = decimals;
            _format = format;
            _capFormat = capFormat;

            string numberFormat = "F" + decimals;
            string cur = health.ToString(numberFormat, CultureInfo.InvariantCulture);
            string mx = max.ToString(numberFormat, CultureInfo.InvariantCulture);
            if (cap < 1f)
            {
                // While capped, the capped value takes the place of max and the original max goes in CapFormat.
                string capped = (max * cap).ToString(numberFormat, CultureInfo.InvariantCulture);
                _text = SafeFormat(format, cur + " / " + capped, cur, capped) +
                        SafeFormat(capFormat, " (" + mx + ")", mx);
            }
            else
            {
                _text = SafeFormat(format, cur + " / " + mx, cur, mx);
            }
            return _text;
        }

        private static string SafeFormat(string format, string fallback, params object[] args)
        {
            try
            {
                return string.Format(CultureInfo.InvariantCulture, format, args);
            }
            catch (System.FormatException)
            {
                return fallback;
            }
        }
    }
}
