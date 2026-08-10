using UnityEngine;

namespace DinoDigger.Core
{
    /// <summary>
    /// THE BACKSTOP (DinoDigger-5k8.1). A flat quad parented to the camera, sized to the camera
    /// rect, drawn behind absolutely everything.
    ///
    /// WHY IT EXISTS. Fit-to-content framing (DinoDigger-kgm) made the camera correct at every
    /// aspect — and immediately exposed a second, independent invariant nobody had been holding:
    /// the ART HAS TO COVER THE VIEW. A portrait phone shows 11 x 23.8 world units of overworld;
    /// the painted tilemap is a 48x48 isometric diamond whose reach is |x| + 2|y - 11.75| &lt;=
    /// 23.5, and a portrait view needs halfWidth + 2 * halfHeight = 29.3 of that budget. There
    /// is NO camera position that fits — a clamp cannot solve it, only more painted world can,
    /// and the cheapest honest "more painted world" is open sea.
    ///
    /// So rather than patch the two places it currently shows, this makes the whole CLASS of bug
    /// impossible: whatever the camera can see, it sees something. Parented to the camera, so it
    /// follows for free and only its SIZE is ever written; at sorting order -1000 it is under the
    /// tilemaps (0-5), the dig backdrop (2) and every actor, so it can only ever be what is left
    /// when nothing else drew there.
    ///
    /// It is deliberately NOT the whole answer for the dig — sea blue above a cartoon sky and
    /// below a soil cutaway would be wrong, just wrong in a different colour. The dig covers
    /// itself properly (see Dig/DigBackdrop.cs) and this sits behind that as the guarantee.
    /// </summary>
    [DisallowMultipleComponent]
    public class CameraBackdrop : MonoBehaviour
    {
        private const string ChildName = "CameraBackdrop";

        // Under the tilemaps (0..5), the dig backdrop (2) and every actor. Nothing in the game
        // draws below this, which is exactly the point.
        private const int Order = -1000;

        // A little larger than the view so a fast rotate (or a resize mid-frame) can never flash
        // a hairline of nothing along an edge.
        private const float Overscan = 1.06f;

        private Camera _camera;
        private SpriteRenderer _renderer;
        private float _appliedOrtho = -1f;
        private float _appliedAspect = -1f;

        private static Sprite _quad;

        /// <summary>The colour the backstop paints. Set from GameConfig.SeaColor.</summary>
        public Color Tint
        {
            get => _renderer != null ? _renderer.color : Color.clear;
            set
            {
                if (_renderer != null)
                {
                    _renderer.color = value;
                }
            }
        }

        // TEST HOOKS (integration runner; no reflection).
        internal SpriteRenderer TestRenderer => _renderer;

        /// <summary>TEST HOOK. The world rect this quad currently paints — the thing a coverage
        /// case asserts CONTAINS the camera rect.</summary>
        internal Rect TestWorldRect
        {
            get
            {
                if (_renderer == null)
                {
                    return default;
                }

                Bounds b = _renderer.bounds;
                return Rect.MinMaxRect(b.min.x, b.min.y, b.max.x, b.max.y);
            }
        }

        /// <summary>Attach a backstop to <paramref name="camera"/> (idempotent) and paint it
        /// <paramref name="color"/>. Null-tolerant, so a scene with no camera simply gets
        /// nothing rather than a null reference.</summary>
        public static CameraBackdrop Ensure(Camera camera, Color color)
        {
            if (camera == null)
            {
                return null;
            }

            var backdrop = camera.GetComponentInChildren<CameraBackdrop>(true);
            if (backdrop == null)
            {
                var go = new GameObject(ChildName);
                go.transform.SetParent(camera.transform, false);
                backdrop = go.AddComponent<CameraBackdrop>();
            }

            backdrop.Bind(camera);
            backdrop.Tint = color;
            backdrop.Apply(force: true);
            return backdrop;
        }

        private void Awake()
        {
            Bind(GetComponentInParent<Camera>());
        }

        private void LateUpdate()
        {
            Apply(force: false);
        }

        private void Bind(Camera camera)
        {
            if (camera != null)
            {
                _camera = camera;
            }

            if (_renderer == null)
            {
                _renderer = GetComponent<SpriteRenderer>();
            }

            if (_renderer == null)
            {
                _renderer = gameObject.AddComponent<SpriteRenderer>();
                _renderer.sprite = Quad();
                _renderer.sortingOrder = Order;
            }

            // The camera sits at z -10 and the world is drawn at z 0; parking the quad ON the
            // world plane keeps it inside whatever near/far planes the camera is ever given.
            // (The camera is unparented and unrotated, so a local z offset is a world z offset.)
            if (_camera != null)
            {
                transform.localPosition = new Vector3(0f, 0f, -_camera.transform.position.z);
            }
        }

        /// <summary>Re-size to the camera rect. Cheap and idempotent — it writes nothing unless
        /// the framing actually changed, which (thanks to CameraFollow) is only on a rotate, a
        /// resize, or a zoom transition.</summary>
        public void Apply(bool force)
        {
            Bind(null);
            if (_camera == null || _renderer == null || !_camera.orthographic)
            {
                return;
            }

            float ortho = _camera.orthographicSize;
            float aspect = CameraFraming.ScreenAspect;
            if (!force
                && Mathf.Abs(ortho - _appliedOrtho) < 0.001f
                && Mathf.Abs(aspect - _appliedAspect) < 0.0005f)
            {
                return;
            }

            _appliedOrtho = ortho;
            _appliedAspect = aspect;

            // The quad sprite is 1x1 world unit, so the scale IS the size.
            float h = CameraFraming.VisibleHeight(ortho) * Overscan;
            float w = CameraFraming.VisibleWidth(ortho, aspect) * Overscan;
            transform.localScale = new Vector3(w, h, 1f);
        }

        /// <summary>A 1x1 white sprite, built once. Same idiom as the dig's white bone fallback:
        /// a flat fill needs no imported art, so the backstop works on a placeholder-only run.</summary>
        private static Sprite Quad()
        {
            if (_quad == null)
            {
                var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                _quad = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            }

            return _quad;
        }
    }
}
