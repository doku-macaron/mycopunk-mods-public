using System;
using System.Collections.Generic;

namespace MycopunkMissionLog
{
    /// <summary>
    /// Frame times of one mission without keeping every frame: a histogram of 0.25 ms buckets up to 250 ms, and
    /// the exact values of the (rare) frames above that.
    /// </summary>
    internal sealed class FrameStats
    {
        private const float BucketMs = 0.25f;
        private const int Buckets = 1000;

        private readonly int[] _histogram = new int[Buckets];
        private readonly List<float> _long = new List<float>();

        public int Frames { get; private set; }
        public double TotalMs { get; private set; }
        public float WorstMs { get; private set; }
        public int Over25 { get; private set; }
        public int Over50 { get; private set; }
        public int Over100 { get; private set; }

        public void Clear()
        {
            Array.Clear(_histogram, 0, Buckets);
            _long.Clear();
            Frames = 0;
            TotalMs = 0;
            WorstMs = 0;
            Over25 = Over50 = Over100 = 0;
        }

        public void Add(float ms)
        {
            if (ms <= 0 || float.IsNaN(ms) || float.IsInfinity(ms)) return;
            Frames++;
            TotalMs += ms;
            if (ms > WorstMs) WorstMs = ms;
            if (ms >= 25f) Over25++;
            if (ms >= 50f) Over50++;
            if (ms >= 100f) Over100++;
            int bucket = (int)(ms / BucketMs);
            if (bucket < Buckets) _histogram[bucket]++;
            else _long.Add(ms);
        }

        public float AverageFps => TotalMs > 0 ? (float)(Frames * 1000.0 / TotalMs) : 0f;

        /// <summary>
        /// "1% low": the frame rate of the frame time that 1% of the frames reach or exceed (99th percentile).
        /// Upper edge of the bucket, so it errs on the low side by at most 0.25 ms.
        /// </summary>
        public float OnePercentLowFps
        {
            get
            {
                if (Frames == 0) return 0f;
                int slowest = Math.Max(1, (int)Math.Ceiling(Frames * 0.01));
                if (_long.Count >= slowest)
                {
                    _long.Sort();
                    return 1000f / _long[_long.Count - slowest];
                }
                int seen = _long.Count;
                for (int i = Buckets - 1; i >= 0; i--)
                {
                    seen += _histogram[i];
                    if (seen >= slowest) return 1000f / ((i + 1) * BucketMs);
                }
                return 0f;
            }
        }
    }
}
