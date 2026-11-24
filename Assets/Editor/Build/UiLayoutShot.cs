using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GeoART.App;
using GeoART.App.Save;
using GeoART.BoardRender;
using GeoART.Contracts;
using GeoART.Engine;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace GeoART.EditorTools
{
    /// <summary>
    /// Screenshot-like PNGs of the app's runtime UI laid out for a phone screen (no device needed): the real ToolBar,
    /// ColorBar, LayerSidebar, PlacementControls and DesignsPanel, built for a placed board with a selection (so the action bar shows),
    /// over the board drawn by BoardView from a phone-like viewpoint. <see cref="UiScreen.Override"/> makes the UI lay
    /// itself out for the phone's size and safe area; the canvases are switched to Screen Space - Camera so they render
    /// into the phone-sized texture. One PNG per phone and state:
    ///   ui-{phone}-closed.png    the column and the action bar
    ///   ui-{phone}-palette.png   the Colour palette open (Blue current, a selection active)
    ///   ui-{phone}-wheel.png     the colour wheel open
    /// Batch (needs graphics, so no -nographics):
    ///   -executeMethod GeoART.EditorTools.UiLayoutShot.RunBatch   (PNGs go to $GEOART_UI_SHOT_OUT or Logs/GeoARtUiShot)
    /// </summary>
    public static class UiLayoutShot
    {
        const string MaterialPath = "Assets/GeoART/BoardRender/Materials/BoardLines.mat";
        const int UiLayer = 5;

        sealed class PlacedBoard : IBoardSpace
        {
            public PlacementState State => PlacementState.Placed;
#pragma warning disable 67
            public event Action<PlacementState> StateChanged;
#pragma warning restore 67
            public bool IsPlaced => true;
            public void PlaceAtReticle() { }
            public void Reset() { }
            public bool TryScreenToBoard(Vec2 px, out Vec2 board) { board = default; return false; }
            public bool TryBoardToScreen(Vec2 board, out Vec2 px) { px = default; return false; }
        }

        public static void RunBatch() => ProjectSetup.RunBatch(Run);

        [MenuItem("GeoART/Checks/UI layout shot")]
        public static void Run()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Debug.Log("[GeoART] UI layout shot skipped: no graphics device (-nographics).");
                return;
            }
            string outDir = Environment.GetEnvironmentVariable("GEOART_UI_SHOT_OUT");
            if (string.IsNullOrEmpty(outDir)) outDir = Path.GetFullPath("Logs/GeoARtUiShot");
            Directory.CreateDirectory(outDir);
            // Portrait safe-area insets in points (top, bottom); both phones are @3x.
            var phones = new[] { ("iphone14", 1170, 2532, 47f, 34f), ("iphone14promax", 1290, 2796, 59f, 34f) };
            foreach (var (name, w, h, top, bottom) in phones)
                foreach (var state in new[] { "closed", "palette", "wheel" })
                    Shoot(outDir, name, w, h, Rect.MinMaxRect(0f, bottom * 3f, w, h - top * 3f), state);
        }

        static void Shoot(string outDir, string phone, int w, int h, Rect safe, string state)
        {
            UiScreen.Override = (new Vector2Int(w, h), safe);
            ServiceHub.Clear();
            BoardAppearance.ResetForNewSession();
            var session = new EditorSession(GoldenBoards.StarOnFullBoard());
            session.SetSnapEnabled(false);
            ServiceHub.Register<IEditorSession>(session);
            ServiceHub.Register<IBoardSpace>(new PlacedBoard());

            var uiGo = new GameObject("UiShot");
            var boardGo = new GameObject("UiShotBoard");
            var boardCamGo = new GameObject("UiShotBoardCamera", typeof(Camera));
            var uiCamGo = new GameObject("UiShotUiCamera", typeof(Camera));
            var rt = new RenderTexture(w, h, 24) { antiAliasing = 4 };
            var textures = new List<Texture2D>();
            try
            {
                var parts = new MonoBehaviour[]
                {
                    uiGo.AddComponent<ToolBar>(), uiGo.AddComponent<ColorBar>(), uiGo.AddComponent<LayerSidebar>(),
                    uiGo.AddComponent<PlacementControls>(), uiGo.AddComponent<DesignsPanel>(),
                };
                foreach (var p in parts) Invoke(p, "Awake");
                foreach (var p in parts) Invoke(p, "Start");

                // A selection in the Select tool, so the action bar shows; Blue is the current colour.
                Layer layer = session.ActiveLayer;
                session.SetTool(ToolId.SelectMove);
                session.SetSelection(new[] { layer.Shapes[0].Id, layer.Shapes[1].Id });
                session.Style = session.Style.WithColor(Palette.Blue);

                var view = boardGo.AddComponent<BoardView>();
                var so = new SerializedObject(view);
                so.FindProperty("material").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
                so.FindProperty("showBase").boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
                Invoke(view, "Awake");
                view.Bind(session);
                Invoke(view, "LateUpdate");

                var boardCam = boardCamGo.GetComponent<Camera>();
                boardCam.clearFlags = CameraClearFlags.SolidColor;
                boardCam.backgroundColor = new Color(0.62f, 0.57f, 0.5f);   // a wooden table
                boardCam.cullingMask = ~(1 << UiLayer);
                boardCam.nearClipPlane = 0.02f;
                boardCam.farClipPlane = 10f;
                boardCam.fieldOfView = 60f;
                boardCam.transform.position = new Vector3(0f, 0.42f, -0.26f);
                boardCam.transform.LookAt(new Vector3(0f, 0f, 0.02f));
                boardCam.targetTexture = rt;

                // The UI camera is an overlay camera in the board camera's URP stack, so the UI draws over the board.
                var uiCam = uiCamGo.GetComponent<Camera>();
                uiCam.cullingMask = 1 << UiLayer;
                uiCam.orthographic = true;
                uiCam.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);   // see ToolHoldMenu.Open
                uiCam.GetUniversalAdditionalCameraData().renderType = CameraRenderType.Overlay;
                boardCam.GetUniversalAdditionalCameraData().cameraStack.Add(uiCam);
                foreach (var canvas in uiGo.GetComponentsInChildren<Canvas>(true))
                {
                    if (!canvas.isRootCanvas) continue;
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = uiCam;
                    canvas.planeDistance = 1f;
                }
                foreach (var t in uiGo.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = UiLayer;
                foreach (var scaler in uiGo.GetComponentsInChildren<CanvasScaler>(true)) Invoke(scaler, "Handle");
                Canvas.ForceUpdateCanvases();

                if (state == "palette")
                {
                    FindChild(uiGo, "Colour").GetComponent<ToolHoldMenu>().Show();
                    Canvas.ForceUpdateCanvases();
                }
                else if (state == "wheel")
                {
                    uiGo.GetComponent<ColorBar>().OpenWheel();
                    Canvas.ForceUpdateCanvases();
                }

                boardCam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                textures.Add(tex);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                string path = Path.Combine(outDir, $"ui-{phone}-{state}.png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Debug.Log($"[GeoART] UI layout shot wrote {path}");
                Invoke(view, "OnDestroy");
            }
            finally
            {
                UiScreen.Override = null;
                boardCamGo.GetComponent<Camera>().targetTexture = null;
                rt.Release();
                foreach (var t in textures) UnityEngine.Object.DestroyImmediate(t);
                UnityEngine.Object.DestroyImmediate(rt);
                foreach (var go in new[] { uiGo, boardGo, boardCamGo, uiCamGo }) UnityEngine.Object.DestroyImmediate(go);
                ServiceHub.Clear();
                BoardAppearance.ResetForNewSession();
            }
        }

        static GameObject FindChild(GameObject root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.gameObject;
            throw new InvalidOperationException($"no child named {name}");
        }

        static void Invoke(object target, string method)
        {
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                var m = t.GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null, Type.EmptyTypes, null);
                if (m == null) continue;
                m.Invoke(target, null);
                return;
            }
            throw new MissingMethodException(target.GetType().Name, method);
        }
    }
}
