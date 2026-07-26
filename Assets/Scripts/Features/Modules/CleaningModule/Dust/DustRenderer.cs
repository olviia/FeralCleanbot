using System;
using UnityEngine;

namespace Features.Modules.CleaningModule.Dust
{
    [RequireComponent(typeof(MeshFilter))]
    [ExecuteAlways]
    public class DustRenderer:MonoBehaviour
    {
        public Material dustMaterial;
        public int shellCount = 16;

        private Mesh mesh;
        MaterialPropertyBlock mpb;

        private void Start()
        {
            mesh = GetComponent<MeshFilter>().sharedMesh;
            mpb = new MaterialPropertyBlock();
        }

        private void Update()
        {
            for (int i = 0; i < shellCount; i++)
            {
                mpb.SetFloat("_ShellIndex", i);
                mpb.SetFloat("_ShellCount", shellCount);
                
                var rp = new RenderParams(dustMaterial);
                rp.matProps = mpb;
                Graphics.RenderMesh(rp, mesh, 0, transform.localToWorldMatrix);

            }
        }
    }
}