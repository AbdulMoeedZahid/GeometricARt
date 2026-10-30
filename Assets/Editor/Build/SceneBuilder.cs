using GeoART.App;
using GeoART.ARBoard;
using GeoART.BoardRender;
using GeoART.Mocks;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace GeoART.EditorTools
{
    /// <summary>
    /// Generates A's materials, prefabs and scenes from code (re-runnable; overwrites only A-owned assets):
    ///   Prefabs/Board/BoardRoot.prefab      ARBoardSpace + tap-to-place + Board (BoardView) + Reticle + CornerPicker
    ///   Prefabs/Board/FlatBoardRoot.prefab  FlatBoardSpace + Board, for Editor scenes (C's Flat.unity)
    ///   Prefabs/Board/ARPlane.prefab        detected plane, no visual (planes are raycast targets only, never drawn)
    ///   Scenes/Main.unity                   startup scene for the iPhone build (DevHud + temporary PlacementControls)
    ///   Scenes/Sandbox/Sandbox_A_AR.unity   Main + star fixture, to check rendering on the device
    ///   Scenes/Sandbox/Sandbox_A_Flat.unity Editor-only board with the star fixture and a perspective camera
    /// Menu: GeoART/Setup/2. Batch: -executeMethod GeoART.EditorTools.SceneBuilder.BuildBatch
    /// </summary>
    public static class SceneBuilder
    {
        public const string MainScene = "Assets/Scenes/Main.unity";
        public const string SandboxARScene = "Assets/Scenes/Sandbox/Sandbox_A_AR.unity";
        public const string SandboxFlatScene = "Assets/Scenes/Sandbox/Sandbox_A_Flat.unity";

        const string BoardShaderPath = "Assets/GeoART/BoardRender/Shaders/BoardUnlit.shader";
        const string MaterialsDir = "Assets/GeoART/BoardRender/Materials";
        const string PrefabsDir = "Assets/Prefabs/Board";

        [MenuItem("GeoART/Setup/2. Build A Scenes and Prefabs", priority = 2)]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            ProjectSetup.EnsureFolder(MaterialsDir);
            ProjectSetup.EnsureFolder(PrefabsDir);
            ProjectSetup.EnsureFolder("Assets/Scenes/Sandbox");

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(BoardShaderPath);
            if (shader == null) throw new System.InvalidOperationException($"Missing shader at {BoardShaderPath}");
            var boardMat = SaveMaterial($"{MaterialsDir}/BoardLines.mat", shader, Color.white, true);

            var planePrefab = BuildPlanePrefab();
            var boardRoot = BuildBoardRootPrefab(boardMat);
            var flatRoot = BuildFlatBoardRootPrefab(boardMat);

            BuildARScene(MainScene, boardRoot, planePrefab, EngineInstaller.Fixture.Empty);
            BuildARScene(SandboxARScene, boardRoot, planePrefab, EngineInstaller.Fixture.StarOnFullBoard);
            BuildFlatScene(SandboxFlatScene, flatRoot);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MainScene, true),
                new EditorBuildSettingsScene(SandboxARScene, false),
            };
            AssetDatabase.SaveAssets();
            Debug.Log("[GeoART] Built A prefabs and scenes; Main.unity is build scene 0.");
        }

        public static void BuildBatch() => ProjectSetup.RunBatch(Build);

        // ---------- assets ----------

        static Material SaveMaterial(string path, Shader shader, Color tint, bool vertexColor)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_Tint", tint);
            mat.SetFloat("_UseVertexColor", vertexColor ? 1f : 0f);
            mat.renderQueue = 3000;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>ARPlane only: no mesh visualizer or renderer, so detected planes are never drawn (no grey halos).</summary>
        static GameObject BuildPlanePrefab()
        {
            var go = new GameObject("ARPlane", typeof(ARPlane));
            return SavePrefab(go, $"{PrefabsDir}/ARPlane.prefab");
        }

        static GameObject BuildBoardRootPrefab(Material boardMat)
        {
            var root = new GameObject("BoardRoot");
            var space = root.AddComponent<ARBoardSpace>();
            root.AddComponent<ARPlacementTapHandler>();

            var board = NewChild(root.transform, "Board");
            var view = board.AddComponent<BoardView>();
            SetField(view, "material", boardMat);
            SetField(view, "showBase", false);   // the registered real surface is the board; draw only the design

            var reticle = NewChild(root.transform, "Reticle");
            var reticleView = reticle.AddComponent<ReticleView>();
            SetField(reticleView, "material", boardMat);

            var picker = NewChild(root.transform, "CornerPicker");
            var pickerView = picker.AddComponent<CornerPickerView>();
            SetField(pickerView, "material", boardMat);

            SetField(space, "boardRoot", board.transform);
            SetField(space, "boardView", view);
            SetField(space, "reticle", reticle.transform);
            SetField(space, "cornerView", pickerView);
            return SavePrefab(root, $"{PrefabsDir}/BoardRoot.prefab");
        }

        static GameObject BuildFlatBoardRootPrefab(Material boardMat)
        {
            var root = new GameObject("FlatBoardRoot");
            var space = root.AddComponent<FlatBoardSpace>();
            var board = NewChild(root.transform, "Board");
            SetField(board.AddComponent<BoardView>(), "material", boardMat);
            SetField(space, "board", board.transform);
            return SavePrefab(root, $"{PrefabsDir}/FlatBoardRoot.prefab");
        }

        // ---------- scenes ----------

        static void BuildARScene(string path, GameObject boardRootPrefab, GameObject planePrefab, EngineInstaller.Fixture fixture)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var session = new GameObject("AR Session", typeof(ARSession), typeof(ARInputManager));
            session.GetComponent<ARSession>().attemptUpdate = true;

            var origin = CreateXROrigin(out Camera arCamera);
            var planes = origin.gameObject.AddComponent<ARPlaneManager>();
            planes.planePrefab = planePrefab;
            // Tables only (D-09). If ARBoardSpace.allowVerticalSurfaces is ever enabled, this must become
            // PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical (deferred, CORNER_PLACEMENT_PLAN D8).
            planes.requestedDetectionMode = PlaneDetectionMode.Horizontal;
            var raycasts = origin.gameObject.AddComponent<ARRaycastManager>();
            origin.gameObject.AddComponent<ARAnchorManager>();

            var boardRoot = (GameObject)PrefabUtility.InstantiatePrefab(boardRootPrefab, scene);
            var space = boardRoot.GetComponent<ARBoardSpace>();
            SetField(space, "raycastManager", raycasts);
            SetField(space, "planeManager", planes);
            SetField(space, "arCamera", arCamera);
            SetField(space, "xrOrigin", origin);

            CreateEventSystem();
            var app = new GameObject("App");
            app.AddComponent<DevHud>();
            app.AddComponent<PlacementControls>();   // TEMPORARY (A) until C's UI binds ICornerPlacement
            AddEditorApp(app, (int)fixture);

            EditorSceneManager.SaveScene(scene, path);
        }

        static void BuildFlatScene(string path, GameObject flatRootPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.17f, 0.2f);
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 10f;
            cam.fieldOfView = 60f;
            // Phone held ~45 cm from the board centre, looking down at ~50 degrees: perspective like the device.
            camGo.transform.position = new Vector3(0f, 0.36f, -0.3f);
            camGo.transform.LookAt(Vector3.zero);
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = false;

            PrefabUtility.InstantiatePrefab(flatRootPrefab, scene);
            CreateEventSystem();
            var app = new GameObject("App");
            var hud = app.AddComponent<DevHud>();
            SetField(hud, "showFps", true);
            AddEditorApp(app, (int)EngineInstaller.Fixture.StarOnFullBoard);

            EditorSceneManager.SaveScene(scene, path);
        }

        /// <summary>Same rig as AR Foundation's "XR Origin (Mobile AR)" menu item.</summary>
        static XROrigin CreateXROrigin(out Camera camera)
        {
            var originGo = new GameObject("XR Origin", typeof(XROrigin));
            var offset = NewChild(originGo.transform, "Camera Offset");
            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener),
                typeof(ARCameraManager), typeof(ARCameraBackground), typeof(TrackedPoseDriver));
            camGo.transform.SetParent(offset.transform, false);
            camGo.tag = "MainCamera";

            camera = camGo.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 20f;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;

            var driver = camGo.GetComponent<TrackedPoseDriver>();
            var position = new InputAction("Position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");
            position.AddBinding("<HandheldARInputDevice>/devicePosition");
            var rotation = new InputAction("Rotation", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");
            rotation.AddBinding("<HandheldARInputDevice>/deviceRotation");
            driver.positionInput = new InputActionProperty(position);
            driver.rotationInput = new InputActionProperty(rotation);

            var origin = originGo.GetComponent<XROrigin>();
            origin.CameraFloorOffsetObject = offset;
            origin.Camera = camera;
            return origin;
        }

        /// <summary>Real engine session plus the drawing UI: board input, tool bar, layer sidebar.</summary>
        static void AddEditorApp(GameObject app, int fixture)
        {
            SetField(app.AddComponent<EngineInstaller>(), "fixture", fixture);
            app.AddComponent<BoardInputRouter>();
            app.AddComponent<ToolBar>();
            app.AddComponent<LayerSidebar>();
            app.AddComponent<ToastView>();
        }

        static void CreateEventSystem() =>
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

        // ---------- helpers ----------

        static GameObject NewChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        static GameObject SavePrefab(GameObject go, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        /// <summary>Sets a private [SerializeField] through SerializedObject (works for prefab instances as overrides).</summary>
        static void SetField(Object target, string field, object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field) ?? throw new System.ArgumentException($"{target.GetType().Name} has no serialized field '{field}'");
            switch (value)
            {
                case Object o: prop.objectReferenceValue = o; break;
                case bool b: prop.boolValue = b; break;
                case int i when prop.propertyType == SerializedPropertyType.Enum: prop.enumValueIndex = i; break;
                case int i: prop.intValue = i; break;
                case float f: prop.floatValue = f; break;
                default: throw new System.ArgumentException($"Unsupported value type for {field}");
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
