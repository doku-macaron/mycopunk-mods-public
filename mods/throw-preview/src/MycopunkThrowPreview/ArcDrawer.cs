using System.Collections.Generic;
using Pigeon.Movement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MycopunkThrowPreview
{
    /// <summary>Draws the predicted path and landing marker in world space.</summary>
    internal sealed class ArcDrawer
    {
        private GameObject _root;
        private LineRenderer _line, _markerDot;
        private Material _lineMaterial, _markerMaterial;
        private Texture2D _dashTexture;
        private float _dashRatio = -1f;
        private const int TextureWidth = 128;

        public void Draw(List<Vector3> points, bool hasEnd, Color color, float thickness, float dashLength, float gapLength)
        {
            var camera = PlayerLook.LocalCamera;
            if (camera == null || points.Count < 2 || !EnsureRoot(camera))
            {
                Hide();
                return;
            }

            float width = Mathf.Max(0.005f, thickness * 0.01f);
            float dash = Mathf.Max(1f, dashLength);
            float gap = Mathf.Max(0f, gapLength);
            float ratio = dash / (dash + gap);
            if (!Mathf.Approximately(ratio, _dashRatio))
            {
                _dashRatio = ratio;
                int visible = Mathf.Clamp(Mathf.RoundToInt(TextureWidth * ratio), 1, TextureWidth);
                for (int i = 0; i < TextureWidth; i++)
                    _dashTexture.SetPixel(i, 0, i < visible ? Color.white : Color.clear);
                _dashTexture.Apply();
            }
            _line.textureScale = new Vector2(20f / (dash + gap), 1f);
            SetColor(_lineMaterial, color);
            SetColor(_markerMaterial, color);
            _line.startWidth = _line.endWidth = width;
            _line.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++) _line.SetPosition(i, points[i]);
            _line.enabled = true;

            _markerDot.enabled = hasEnd;
            if (!hasEnd) return;
            var end = points[points.Count - 1];
            float depth = camera.WorldToViewportPoint(end).z;
            if (depth <= 0f)
            {
                _markerDot.enabled = false;
                return;
            }
            Vector3 toCamera = camera.transform.position - end;
            if (toCamera.sqrMagnitude > 0.0001f) end += toCamera.normalized * 0.025f;
            float viewHeight = camera.orthographic ? camera.orthographicSize * 2f :
                2f * depth * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
            float radius = Mathf.Max(0.08f, viewHeight * 0.022f);
            float markerWidth = Mathf.Max(width, viewHeight * 0.004f);
            Vector3 right = camera.transform.right * radius;
            _markerDot.startWidth = _markerDot.endWidth = markerWidth * 2f;
            _markerDot.SetPosition(0, end - right * 0.025f);
            _markerDot.SetPosition(1, end + right * 0.025f);
        }

        public void Hide()
        {
            if (_line != null) _line.enabled = false;
            if (_markerDot != null) _markerDot.enabled = false;
        }

        public void Destroy()
        {
            if (_root != null) Object.Destroy(_root);
            if (_lineMaterial != null) Object.Destroy(_lineMaterial);
            if (_markerMaterial != null) Object.Destroy(_markerMaterial);
            if (_dashTexture != null) Object.Destroy(_dashTexture);
            _root = null;
            _line = _markerDot = null;
            _lineMaterial = _markerMaterial = null;
            _dashTexture = null;
            _dashRatio = -1f;
        }

        private bool EnsureRoot(Camera camera)
        {
            if (_line != null && _markerDot != null) return true;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                         Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new System.InvalidOperationException("No suitable shader for the throw arc.");

            if (_lineMaterial == null)
            {
                _dashTexture = new Texture2D(TextureWidth, 1, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Point,
                    hideFlags = HideFlags.HideAndDontSave
                };
                _lineMaterial = CreateMaterial(shader, _dashTexture);
                _markerMaterial = CreateMaterial(shader, Texture2D.whiteTexture);
            }
            _root = new GameObject("MycoThrowArc") { hideFlags = HideFlags.DontSave };
            int layer = 0;
            while (layer < 31 && (camera.cullingMask & (1 << layer)) == 0) layer++;
            _root.layer = layer;
            _line = _root.AddComponent<LineRenderer>();
            Configure(_line, _lineMaterial);
            _line.textureMode = LineTextureMode.Tile;
            var dot = new GameObject("Landing marker center") { layer = layer };
            dot.transform.SetParent(_root.transform, false);
            _markerDot = dot.AddComponent<LineRenderer>();
            Configure(_markerDot, _markerMaterial);
            _markerDot.positionCount = 2;
            _markerDot.numCapVertices = 4;
            _markerDot.sortingOrder = 1;
            return true;
        }

        private static Material CreateMaterial(Shader shader, Texture texture)
        {
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, renderQueue = 3000 };
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return material;
        }

        private static void SetColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        }

        private static void Configure(LineRenderer line, Material material)
        {
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.startColor = line.endColor = Color.white;
            line.enabled = false;
        }
    }
}
