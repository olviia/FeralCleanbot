using System;
using Paint;
using UnityEngine;
using UnityEngine.Rendering;

namespace Features.Modules.CleaningModule.Dust
{
    /// <summary>
    /// Draws N concentric shells of the host mesh in ONE instanced draw call.
    /// Shell 0 is the dust film; 1..N-1 carry grit, mat and fibre.
    /// Knows nothing about the base material — dust composites over whatever is underneath.
    /// </summary>

    [RequireComponent(typeof(MeshFilter))]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class DustRenderer:MonoBehaviour
    {
        [Header("Shells")] 
        [SerializeField] private Material dustMaterial;
        [SerializeField, Range(1, 64)] private int shellCount = 24;
        [SerializeField, Range(0f, 0.2f)] private float maxHeight = 0.03f;

        [Header("Mask")] [SerializeField] private PaintCanvas maskCanvas; //1 = dusty
        
        private static readonly int shellCountId = Shader.PropertyToID("_ShellCount");
        private static readonly int maxHeightId = Shader.PropertyToID("_MaxHeight");
        private static readonly int dustMaskId = Shader.PropertyToID("_DustMask");
        private static readonly int paintRectId = Shader.PropertyToID("_PaintRect");

        private Mesh _mesh;
        private MaterialPropertyBlock _mpb;
        private RenderParams _rp;
        private Matrix4x4[] _matrices;

        private void OnEnable()
        {
            _mesh = GetComponent<MeshFilter>().sharedMesh;
            _mpb = new MaterialPropertyBlock();
            if (maskCanvas != null)
            {
                maskCanvas.Changed += OnMaskChanged;
                OnMaskChanged(maskCanvas.Texture);
            }
            BuildRenderParams();
        }


        private void OnDisable()
        {if (maskCanvas != null)
            {
                maskCanvas.Changed -= OnMaskChanged;
            }
        }


        private void OnMaskChanged(RenderTexture mask)
        {
            _mpb.SetTexture(dustMaskId, mask);
        }

        private void BuildRenderParams()
        {
            _rp = new RenderParams(dustMaterial);
            _rp.matProps = _mpb;
            _rp.shadowCastingMode = ShadowCastingMode.Off;
            _rp.receiveShadows = true;
        }

        private void Update()
        {
            if (_mesh == null || dustMaterial == null) return;
            
            _rp.worldBounds = ComputeWorldBounds();
            _mpb.SetFloat(shellCountId, shellCount);
            _mpb.SetFloat(maxHeightId, maxHeight);
            if(maskCanvas != null)
                _mpb.SetVector(paintRectId, maskCanvas.PaintRect);
            
            if(_matrices == null || _matrices.Length != shellCount)
                _matrices = new Matrix4x4[shellCount];
            Matrix4x4 m = transform.localToWorldMatrix;
            for(int i = 0; i< _matrices.Length; i++)
                _matrices[i] = m;
            Graphics.RenderMeshInstanced(_rp, _mesh, 0, _matrices);
        }
        
        private Bounds ComputeWorldBounds()
        {
            Bounds local = _mesh.bounds;
            Vector3 scale = transform.lossyScale;
            float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            float radius = (local.extents.magnitude + maxHeight)* maxScale;
            return new Bounds(transform.TransformPoint(local.center), Vector3.one * (radius*2f));
        }
    }
}