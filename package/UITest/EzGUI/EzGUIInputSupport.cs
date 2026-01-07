#if HAS_EZ_GUI
using UnityEngine;

namespace ODDGames.UITest
{
    /// <summary>
    /// Extends UITestInputInterceptor to detect EZ GUI (AnB Software) component types.
    /// Only compiled when HAS_EZ_GUI is defined in Project Settings.
    /// </summary>
    public static class EzGUIInputSupport
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            UITestInputInterceptor.RegisterComponentTypeDetector(DetectEzGUIType);
            Debug.Log("[UITest] Registered EZ GUI component type detector");
        }

        static string DetectEzGUIType(GameObject go)
        {
            if (go == null) return null;

            // Check for EZ GUI types
            if (go.GetComponent<UIButton3D>() != null) return "UIButton3D";
            if (go.GetComponent<AutoSpriteControlBase>() != null) return "AutoSpriteControlBase";

            // Add more EZ GUI types as needed
            // if (go.GetComponent<UIScrollList>() != null) return "UIScrollList";

            return null;
        }
    }
}
#endif
