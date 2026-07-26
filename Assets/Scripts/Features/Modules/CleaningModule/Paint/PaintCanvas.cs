using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Paint
{
    /// <summary>
    /// Owns one paintable surface: a live RenderTexture in VRAM that brushes
    /// write into and display materials read from. Hides HOW it's stored —  
    /// callers only ever touch Texture / Changed / Bind / Clear.
    /// </summary>

    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class PaintCanvas : MonoBehaviour
    {
        [Header("Surface")]
        [SerializeField] private int resolution = 1024;
        [SerializeField] private RenderTextureFormat format = RenderTextureFormat.ARGB32;
        [SerializeField] private Color clearColor = Color.clear;
        [SerializeField] private Renderer surfaceRenderer; 
        [SerializeField] private bool bindToSurface = true;
        [SerializeField] private string surfaceTextureProperty = "_BaseMap";

        
        private RenderTexture _rt;
        private readonly List<MaterialBinding> _bindings = new();
        
        [Header("Painting")]
        [SerializeField] private Shader paintShader;
        
        private Material _paintMat;
        private static readonly int RectMinId       = Shader.PropertyToID("_PaintRectMin");
        private static readonly int RectSizeId      = Shader.PropertyToID("_PaintRectSize");
        private static readonly int BrushCenterId   = Shader.PropertyToID("_BrushCenter");
        private static readonly int BrushYawId      = Shader.PropertyToID("_BrushYaw");
        private static readonly int BrushHalfSizeId = Shader.PropertyToID("_BrushHalfSize");
        private static readonly int FootprintId     = Shader.PropertyToID("_FootprintTex");
        private static readonly int BrushColorId    = Shader.PropertyToID("_BrushColor");

        private readonly struct MaterialBinding
        {
            private readonly Material _material;
            private readonly int _propertyId;

            public MaterialBinding(Material material, int propertyId)
            {
                _material = material;
                _propertyId = propertyId;
            }
            public void Apply(RenderTexture rt) => _material.SetTexture(_propertyId, rt);
        }
        
        /// <summary>Fires whenever the backing texture is (re)created.     
        ///Bind() users are handled for you.</summary>
        public event Action<RenderTexture> Changed;
        
        /// <summary>The live working copy. Lazily created so binding before 
        ///Awake still works.</summary>
        public RenderTexture Texture { get { EnsureCreated(); return _rt; } }

        private void Awake()
        {
            EnsureCreated();
            if (bindToSurface && surfaceRenderer != null)
                Bind(surfaceRenderer.material, surfaceTextureProperty);   
        }


        private void OnDestroy()
        {
            if (_rt != null)
            {
                _rt.Release(); //free the GPU allocation
                DestroyObject(_rt);
                _rt = null;
            }

            if (_paintMat != null)
            {
                DestroyObject(_paintMat);
                _paintMat = null;
            }
        }
        private new static void DestroyObject(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        
        // --- Public API
        public void Bind(Material material, int propertyId)
        {
            _bindings.Add(new MaterialBinding(material, propertyId));
            material.SetTexture(propertyId, Texture);
        }
        public void Bind(Material material, string propertyName) => Bind(material, Shader.PropertyToID(propertyName));

        public void Clear(Color value)
        {
            EnsureCreated();
            var prev = RenderTexture.active; //save
            RenderTexture.active = _rt;
            GL.Clear(false, true, value); //no depth, clear color to 'value'
            RenderTexture.active = prev; //restore
            
        }
        
        //--- Internals
        private void EnsureCreated()
        {
            if (_rt != null && _rt.IsCreated()) return;
            _rt = new RenderTexture(resolution, resolution, 0, format, RenderTextureReadWrite.sRGB)
            {
                name = $"{name}_Paint",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
            };
            _rt.Create();
            Clear(clearColor); //start state;
            Rebind();
            Changed?.Invoke(_rt);
        }

        private void Rebind()
        {
            for(int i = 0; i < _bindings.Count; i++)
                _bindings[i].Apply(_rt);
        }
        
        
        private void EnsurePaintMaterial()
        {
            if (_paintMat != null) return;
            _paintMat = new Material(paintShader) { hideFlags =
                HideFlags.HideAndDontSave };
        }

        // Step-2 TEMP signature — proves the write path. Becomes Stamp(inBrushStroke) at Step 4.
        public void Stamp(Vector3 worldPos, float yawRadians, Color color, in Brush brush)    
        { 
            EnsureCreated();
            EnsurePaintMaterial();

            Bounds b = surfaceRenderer.bounds;   // world AABB of the flat surface
            _paintMat.SetVector(RectMinId,      new Vector4(b.min.x,  b.min.z));     
            _paintMat.SetVector(RectSizeId,     new Vector4(b.size.x, b.size.z)); 
            _paintMat.SetVector(BrushCenterId,  new Vector4(worldPos.x, worldPos.z));
            _paintMat.SetFloat(BrushYawId,      yawRadians);
            _paintMat.SetFloat(BrushHalfSizeId, brush.halfSize);
            _paintMat.SetTexture(FootprintId,   brush.footprint);
            _paintMat.SetColor(BrushColorId, color);

            Graphics.Blit(null, _rt, _paintMat, 0);
        }

    }

    
}