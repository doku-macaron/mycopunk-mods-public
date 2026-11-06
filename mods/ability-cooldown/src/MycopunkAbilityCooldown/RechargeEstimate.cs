namespace MycopunkAbilityCooldown
{
    /// <summary>
    /// Seconds until an ability's next charge, measured from how fast its charge actually grows.
    ///
    /// Charge is in charges (0..max). Cooldown.Update adds deltaTime / rechargeDuration, but other abilities fill
    /// their charge their own way (upgrades change the speed, some only charge on kills), so the rate is taken from
    /// the last <see cref="Window"/> seconds instead of from the data. While the charge does not grow (paused, or
    /// charged by events), there is no estimate (NaN). A drop in charge (used) starts over.
    ///
    /// The window starts when the charge starts growing, not when it dropped: a grenade does not recharge during its
    /// throw, and counting that pause made the first numbers too large (0.1.3).
    /// Pure logic, no Unity: the caller passes the time and the charge.
    /// </summary>
    internal sealed class RechargeEstimate
    {
        public const float Window = 0.5f;

        private bool _started;
        private float _t0 = float.NaN, _c0;   // window start: the last time before growth was seen
        private float _lastT, _lastC;          // the last time the charge grew, and to what
        private float _prevT;                  // the previous call
        private float _rate;

        /// <returns>Seconds to the next whole charge, or NaN when unknown or full.</returns>
        public float Update(float time, float charge, int maxCharges)
        {
            if (maxCharges <= 0 || charge >= maxCharges)
            {
                Reset();
                return float.NaN;
            }
            if (!_started || charge < _lastC)
            {
                Reset();
                _started = true;
                _lastT = _prevT = time;
                _lastC = charge;
                return float.NaN;
            }

            if (charge > _lastC)
            {
                if (float.IsNaN(_t0))
                {
                    // Growth began somewhere in (previous call, now]: start the window there.
                    _t0 = _prevT;
                    _c0 = _lastC;
                }
                _lastT = time;
                _lastC = charge;
            }
            _prevT = time;

            // No growth for a whole window: paused, or charged by events, not over time.
            if (time - _lastT > Window)
            {
                _t0 = float.NaN;
                _rate = 0f;
                return float.NaN;
            }
            if (!float.IsNaN(_t0))
            {
                float span = _lastT - _t0;
                if (span >= Window * 0.5f)
                {
                    _rate = (_lastC - _c0) / span;
                    // Slide the window so a changed speed shows within about one window.
                    if (span > Window)
                    {
                        _t0 = _lastT;
                        _c0 = _lastC;
                    }
                }
            }
            if (_rate <= 0f)
                return float.NaN;
            float next = (float)System.Math.Floor(charge) + 1f;
            return (next - charge) / _rate;
        }

        public void Reset()
        {
            _started = false;
            _t0 = float.NaN;
            _c0 = _lastC = _lastT = _prevT = 0f;
            _rate = 0f;
        }
    }

    /// <summary>"12" from 10 s up, "4.2" below. The same string instance while the shown value does not change.</summary>
    internal sealed class SecondsText
    {
        private int _tenths = int.MinValue;
        private string _text = "";

        public string Get(float seconds)
        {
            int t = float.IsNaN(seconds) || seconds < 0f ? -1
                : seconds >= 10f ? (int)System.Math.Ceiling(seconds) * 10
                : (int)System.Math.Ceiling(seconds * 10f);
            if (t != _tenths)
            {
                _tenths = t;
                _text = t < 0 ? "" : t >= 100 ? (t / 10).ToString() : (t / 10) + "." + (t % 10);
            }
            return _text;
        }
    }
}
