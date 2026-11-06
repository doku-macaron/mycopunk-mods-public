namespace MycopunkPingDistance
{
    /// <summary>
    /// "123m" for a distance in metres (world units). Returns the same string instance while the rounded value
    /// does not change, so a label is only rebuilt when the number does. Empty below <c>minMetres</c>.
    /// </summary>
    internal sealed class DistanceText
    {
        private int _metres = int.MinValue;
        private string _text = "";

        public string Get(float distance, float minMetres)
        {
            int m = distance < minMetres || float.IsNaN(distance) ? -1 : (int)(distance + 0.5f);
            if (m != _metres)
            {
                _metres = m;
                _text = m < 0 ? "" : m + "m";
            }
            return _text;
        }
    }
}
