using UnityEngine;

namespace GeoART.BoardRender
{
    /// <summary>
    /// Placement reticle (A-2): the board's footprint as corner brackets plus a centre cross, so the user sees
    /// where the 40 cm board will land and which way it faces before tapping.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ReticleView : MonoBehaviour
    {
        [SerializeField] float sideMeters = BoardGeometry.DefaultSideMeters;
        [SerializeField] Material material;
        [SerializeField] Color color = new Color(1f, 1f, 1f, 0.95f);
        [SerializeField] Color fillColor = new Color(1f, 1f, 1f, 0.12f);

        Mesh _mesh;
        Material _instance;

        void Awake()
        {
            var mb = new BoardMeshBuilder();
            float h = sideMeters * 0.5f;
            float arm = sideMeters * 0.18f;
            float w = 0.004f * sideMeters / BoardGeometry.DefaultSideMeters;

            mb.AddQuad(new Vector3(-h, 0, -h), new Vector3(-h, 0, h), new Vector3(h, 0, h), new Vector3(h, 0, -h), fillColor);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    var corner = new Vector3(sx * h, 0f, sz * h);
                    mb.AddSegment(corner, corner - new Vector3(sx * arm, 0f, 0f), w, color);
                    mb.AddSegment(corner, corner - new Vector3(0f, 0f, sz * arm), w, color);
                }
            float c = sideMeters * 0.04f;
            mb.AddSegment(new Vector3(-c, 0, 0), new Vector3(c, 0, 0), w * 0.75f, color);
            mb.AddSegment(new Vector3(0, 0, -c), new Vector3(0, 0, c), w * 0.75f, color);
            // Arrow on the far edge: the side that becomes the top of the board.
            mb.AddSegment(new Vector3(-c, 0, h - c * 1.5f), new Vector3(0, 0, h - c * 0.5f), w * 0.75f, color);
            mb.AddSegment(new Vector3(c, 0, h - c * 1.5f), new Vector3(0, 0, h - c * 0.5f), w * 0.75f, color);

            _mesh = new Mesh { name = "BoardReticle" };
            mb.Apply(_mesh);
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = gameObject.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            if (material != null)
            {
                _instance = new Material(material) { renderQueue = 3003 };
                mr.sharedMaterial = _instance;
            }
        }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_instance != null) Destroy(_instance);
        }
    }
}
