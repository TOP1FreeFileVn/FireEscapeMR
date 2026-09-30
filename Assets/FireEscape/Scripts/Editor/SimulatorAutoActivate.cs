using System;
using UnityEditor;
using UnityEngine;

namespace FireEscape.EditorTools
{
    /// <summary>
    /// Meta chỉ giữ trạng thái "Activate" của Meta XR Simulator trong tiến trình Unity hiện tại,
    /// mở lại Unity là mất. Script này tự bật Simulator ngay trước khi vào Play nếu scene có OVRCameraRig.
    /// Tắt bằng menu khi dùng kính Quest thật qua Link.
    /// </summary>
    [InitializeOnLoad]
    static class SimulatorAutoActivate
    {
        const string PrefKey = "FireEscape.AutoActivateXRSimulator";
        const string MenuPath = "FireEscape/Tự bật Meta XR Simulator khi Play (scene Quest)";
        const string ActivateMenu = "Window/Meta/Meta XR Simulator/Activate";

        static SimulatorAutoActivate()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        static bool Enabled => EditorPrefs.GetBool(PrefKey, true);

        [MenuItem(MenuPath, priority = 30)]
        static void Toggle() => EditorPrefs.SetBool(PrefKey, !Enabled);

        [MenuItem(MenuPath, true)]
        static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode || !Enabled) return;
            if (UnityEngine.Object.FindFirstObjectByType<OVRCameraRig>() == null) return;
            var selected = Environment.GetEnvironmentVariable("XR_SELECTED_RUNTIME_JSON");
            if (!string.IsNullOrEmpty(selected) && selected.Contains("MetaXRSimulator")) return;
            if (EditorApplication.ExecuteMenuItem(ActivateMenu))
                Debug.Log("[FireEscape] Đã tự bật Meta XR Simulator trước khi vào Play.");
            else
                Debug.LogWarning("[FireEscape] Không bật được Meta XR Simulator — kiểm tra Window/Meta/Meta XR Simulator.");
        }
    }
}
