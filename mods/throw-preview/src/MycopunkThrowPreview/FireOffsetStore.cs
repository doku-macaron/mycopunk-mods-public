using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MycopunkThrowPreview
{
    /// <summary>
    /// Where each kind of grenade leaves from, relative to the camera (right, up, forward in metres), kept in a small
    /// TSV file so the first throw of a later session already starts from the right place.
    ///
    /// One line per grenade: name (the GearInfo asset's name), x, y, z. Lines that do not parse are skipped. The file
    /// is only written when a value moves by more than <see cref="Tolerance"/>, so a steady throw does not write.
    /// It is this mod's own file; the save is not touched. Pure logic and file IO, no Unity types (x, y, z as floats).
    /// </summary>
    internal sealed class FireOffsetStore
    {
        public const float Tolerance = 0.01f;
        private const string Header = "# grenade\tright_m\tup_m\tforward_m  (Mycopunk Throw Preview: fire point relative to the camera)";

        private readonly string _path;
        private readonly Dictionary<string, float[]> _offsets = new Dictionary<string, float[]>();

        public FireOffsetStore(string path)
        {
            _path = path;
        }

        public int Count => _offsets.Count;

        public bool TryGet(string name, out float x, out float y, out float z)
        {
            x = y = z = 0f;
            if (name == null || !_offsets.TryGetValue(name, out var v))
                return false;
            x = v[0];
            y = v[1];
            z = v[2];
            return true;
        }

        /// <returns>True when the value changed enough to be worth saving.</returns>
        public bool Set(string name, float x, float y, float z)
        {
            if (string.IsNullOrEmpty(name) || name.IndexOf('\t') >= 0 || name.IndexOf('\n') >= 0)
                return false;
            if (_offsets.TryGetValue(name, out var v) &&
                System.Math.Abs(v[0] - x) <= Tolerance && System.Math.Abs(v[1] - y) <= Tolerance && System.Math.Abs(v[2] - z) <= Tolerance)
                return false;
            _offsets[name] = new[] { x, y, z };
            return true;
        }

        public void Load()
        {
            _offsets.Clear();
            if (!File.Exists(_path))
                return;
            foreach (var line in File.ReadAllLines(_path, Encoding.UTF8))
                Parse(line, _offsets);
        }

        public void Save()
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            sb.Append(Header).Append('\n');
            var names = new List<string>(_offsets.Keys);
            names.Sort(System.StringComparer.Ordinal);
            foreach (var name in names)
            {
                var v = _offsets[name];
                sb.Append(name).Append('\t').Append(F(v[0])).Append('\t').Append(F(v[1])).Append('\t').Append(F(v[2])).Append('\n');
            }
            // Write to a temporary file and move it over, so a crash mid-write does not leave half a file.
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(_path))
                File.Delete(_path);
            File.Move(tmp, _path);
        }

        internal static void Parse(string line, Dictionary<string, float[]> into)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                return;
            var parts = line.Split('\t');
            if (parts.Length < 4 || parts[0].Length == 0)
                return;
            if (float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
                float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                into[parts[0]] = new[] { x, y, z };
        }

        private static string F(float v) => v.ToString("0.000", CultureInfo.InvariantCulture);
    }
}
