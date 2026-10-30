using GeoART.App;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace GeoART.Tests.Verifier
{
    /// <summary>VERIFIER: the generated scenes carry the editor app (session, input, tool bar, layer sidebar, toasts) and an EventSystem.</summary>
    public sealed class VerifierSceneWiringTests
    {
        [TestCase("Assets/Scenes/Main.unity")]
        [TestCase("Assets/Scenes/Sandbox/Sandbox_A_AR.unity")]
        [TestCase("Assets/Scenes/Sandbox/Sandbox_A_Flat.unity")]
        public void SceneHasEditorApp(string path)
        {
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                int installers = 0, routers = 0, toolbars = 0, sidebars = 0, toasts = 0, eventSystems = 0;
                foreach (var root in scene.GetRootGameObjects())
                {
                    installers += root.GetComponentsInChildren<EngineInstaller>(true).Length;
                    routers += root.GetComponentsInChildren<BoardInputRouter>(true).Length;
                    toolbars += root.GetComponentsInChildren<ToolBar>(true).Length;
                    sidebars += root.GetComponentsInChildren<LayerSidebar>(true).Length;
                    toasts += root.GetComponentsInChildren<ToastView>(true).Length;
                    eventSystems += root.GetComponentsInChildren<EventSystem>(true).Length;
                    foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                        Assert.IsNotNull(mb, $"{path}: a component on {root.name} has a missing script");
                }
                Assert.AreEqual(1, installers, $"{path}: EngineInstaller");
                Assert.AreEqual(1, routers, $"{path}: BoardInputRouter");
                Assert.AreEqual(1, toolbars, $"{path}: ToolBar");
                Assert.AreEqual(1, sidebars, $"{path}: LayerSidebar");
                Assert.AreEqual(1, toasts, $"{path}: ToastView");
                Assert.AreEqual(1, eventSystems, $"{path}: EventSystem");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
