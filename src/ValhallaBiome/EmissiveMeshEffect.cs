using UnityEngine;

namespace ValhallaBiome
{
    /// <summary>
    /// Replacement for Horem's EmissiveMesh_HS component.
    /// Pulses emission intensity on lava rock materials to simulate glowing lava.
    /// </summary>
    public class EmissiveMeshEffect : MonoBehaviour
    {
        private static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");

        private Renderer _renderer;
        private MaterialPropertyBlock _propBlock;
        private Color _baseEmissionColor;
        private bool _initialized;

        [Header("Emission Settings")]
        public float pulseSpeed = 0.8f;
        public float minIntensity = 0.4f;
        public float maxIntensity = 1.2f;

        private void Awake()
        {
            _propBlock = new MaterialPropertyBlock();
            _renderer = GetComponent<Renderer>();

            if (_renderer == null)
                _renderer = GetComponentInChildren<Renderer>();

            if (_renderer != null && _renderer.sharedMaterial != null)
            {
                if (_renderer.sharedMaterial.HasProperty(EmissionColorID))
                {
                    _baseEmissionColor = _renderer.sharedMaterial.GetColor(EmissionColorID);
                    if (_baseEmissionColor == Color.black)
                        _baseEmissionColor = new Color(1f, 0.3f, 0.05f); // Default lava orange
                    _initialized = true;
                }
            }
        }

        private void Update()
        {
            if (!_initialized) return;

            float t = (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f;
            float intensity = Mathf.Lerp(minIntensity, maxIntensity, t);

            _renderer.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(EmissionColorID, _baseEmissionColor * intensity);
            _renderer.SetPropertyBlock(_propBlock);
        }
    }
}
