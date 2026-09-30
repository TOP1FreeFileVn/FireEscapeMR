using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FireEscape.EditorTools
{
    public static class FireEscapeSetup
    {
        const string Root = "Assets/FireEscape";
        const string MatDir = Root + "/Resources/FireEscapeMats";
        const string SceneDir = Root + "/Scenes";

        [MenuItem("FireEscape/1. Tạo scene PC (bàn phím + chuột)", priority = 1)]
        public static void CreateDesktopScene() => CreateScene(false);

        [MenuItem("FireEscape/2. Tạo scene Quest (MR passthrough + quét phòng)", priority = 2)]
        public static void CreateQuestScene() => CreateScene(true);

        [MenuItem("FireEscape/Tạo lại vật liệu mẫu (cần cho bản build)", priority = 20)]
        public static void EnsureMaterials()
        {
            Directory.CreateDirectory(MatDir);
            Make("FE_Lit", "Universal Render Pipeline/Lit", null);
            Make("FE_Unlit", "Universal Render Pipeline/Unlit", false);
            Make("FE_Particle", "Universal Render Pipeline/Particles/Unlit", true);
            Make("FE_ParticleAlpha", "Universal Render Pipeline/Particles/Unlit", false);
            AssetDatabase.SaveAssets();
        }

        static void Make(string name, string shader, bool? additive)
        {
            string path = $"{MatDir}/{name}.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
            var s = Shader.Find(shader);
            if (s == null) { Debug.LogWarning("FireEscape: không tìm thấy shader " + shader); return; }
            var m = new Material(s);
            if (additive.HasValue) MatLib.MakeTransparent(m, additive.Value);
            AssetDatabase.CreateAsset(m, path);
        }

        [MenuItem("FireEscape/Xoá hồ sơ người chơi (Error Profile)", priority = 40)]
        public static void ResetProfile()
        {
            var p = Path.Combine(Application.persistentDataPath, "fireescape_profile.json");
            if (File.Exists(p)) File.Delete(p);
            Debug.Log("FireEscape: đã xoá " + p);
        }

        static void CreateScene(bool quest)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureMaterials();
            Directory.CreateDirectory(SceneDir);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("FireEscape");
            var game = go.AddComponent<FireEscapeGame>();
            game.forceDesktop = !quest;

            if (quest)
            {
                var rigPrefab = FindPrefab("OVRCameraRig");
                if (rigPrefab == null) { Debug.LogError("FireEscape: không tìm thấy OVRCameraRig.prefab (Meta XR Core SDK)."); return; }
                var rigGo = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
                var mgr = rigGo.GetComponent<OVRManager>();
                if (mgr == null) mgr = rigGo.AddComponent<OVRManager>();
                mgr.isInsightPassthroughEnabled = true;
                mgr.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
                if (rigGo.GetComponent<OVRPassthroughLayer>() == null) rigGo.AddComponent<OVRPassthroughLayer>();
                foreach (var cam in rigGo.GetComponentsInChildren<Camera>(true))
                {
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0, 0, 0, 0);
                }

                AddControllers(rigGo);

                var mrukPrefab = FindPrefab("MRUK");
                if (mrukPrefab != null) PrefabUtility.InstantiatePrefab(mrukPrefab);
                else Debug.LogWarning("FireEscape: không tìm thấy MRUK.prefab — sẽ dùng không gian mô phỏng (VR).");

                var cfg = OVRProjectConfig.CachedProjectConfig;
                cfg.sceneSupport = OVRProjectConfig.FeatureSupport.Required;
                cfg.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Required;
                OVRProjectConfig.CommitProjectConfig(cfg);
            }
            else
            {
                var light = new GameObject("Fill Light").AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 0.15f;
                light.transform.rotation = Quaternion.Euler(60, 30, 0);
            }

            string path = $"{SceneDir}/FireEscape_{(quest ? "Quest" : "PC")}.unity";
            EditorSceneManager.SaveScene(scene, path);
            var list = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
            list.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log($"FireEscape: đã tạo {path}. Nhấn Play để chạy{(quest ? " (cần Quest qua Link hoặc build Android)" : "")}.");
        }

        [MenuItem("FireEscape/3. Thêm tay cầm Touch vào scene đang mở", priority = 3)]
        public static void AddControllersToOpenScene()
        {
            var rig = Object.FindFirstObjectByType<OVRCameraRig>();
            if (rig == null) { Debug.LogError("FireEscape: scene không có OVRCameraRig — tạo scene Quest trước (menu 2)."); return; }
            AddControllers(rig.gameObject);
            EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
            EditorSceneManager.SaveScene(rig.gameObject.scene);
        }

        /// <summary>Gắn model tay cầm Touch (đi theo tay cầm thật hoặc tay cầm ảo của Meta XR Simulator).</summary>
        static void AddControllers(GameObject rigGo)
        {
            var prefab = FindPrefab("OVRControllerPrefab");
            if (prefab == null) { Debug.LogWarning("FireEscape: không tìm thấy OVRControllerPrefab."); return; }
            foreach (var (anchorName, controller) in new[] { ("LeftControllerAnchor", OVRInput.Controller.LTouch), ("RightControllerAnchor", OVRInput.Controller.RTouch) })
            {
                var anchor = rigGo.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == anchorName);
                if (anchor == null) { Debug.LogWarning("FireEscape: thiếu " + anchorName); continue; }
                if (anchor.GetComponentInChildren<OVRControllerHelper>(true) != null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, anchor);
                go.name = controller == OVRInput.Controller.LTouch ? "LeftTouchController" : "RightTouchController";
                var helper = go.GetComponent<OVRControllerHelper>();
                helper.m_controller = controller;
                helper.m_showState = OVRInput.InputDeviceShowState.Always;
            }
        }

        static GameObject FindPrefab(string name)
        {
            foreach (var guid in AssetDatabase.FindAssets(name + " t:Prefab"))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) == name) return AssetDatabase.LoadAssetAtPath<GameObject>(p);
            }
            return null;
        }
    }
}
