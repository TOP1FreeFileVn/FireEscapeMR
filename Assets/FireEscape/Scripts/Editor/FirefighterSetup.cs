using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace FireEscape.EditorTools
{
    /// <summary>Cấu hình import cho Firefighter.fbx (xuất từ BlenderSource/firefighter_stageB.py).</summary>
    class FirefighterImporter : AssetPostprocessor
    {
        const string Folder = "Assets/FireEscape/Resources/FireEscapeModels/Firefighter/";
        static readonly string[] Loops = { "Idle", "Talk", "Beckon", "Walk", "CrouchWalk", "MaskOffIdle", "Point" };

        bool Mine => assetPath.Replace('\\', '/').StartsWith(Folder);

        void OnPreprocessModel()
        {
            if (!Mine || !assetPath.EndsWith(".fbx")) return;
            var mi = (ModelImporter)assetImporter;
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.importAnimation = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importCameras = false;
            mi.importLights = false;
            mi.isReadable = false;
        }

        void OnPreprocessAnimation()
        {
            if (!Mine || !assetPath.EndsWith(".fbx")) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.name = c.name.Split('|').Last();
                c.loopTime = Loops.Contains(c.name) && c.name != "Point";
                c.lockRootRotation = c.lockRootHeightY = c.lockRootPositionXZ = true;
            }
            mi.clipAnimations = clips;
        }

        void OnPreprocessTexture()
        {
            if (!Mine) return;
            var ti = (TextureImporter)assetImporter;
            ti.maxTextureSize = 2048;
            if (assetPath.Contains("Normal")) ti.textureType = TextureImporterType.NormalMap;
            if (assetPath.Contains("MetallicSmoothness")) ti.sRGBTexture = false;
        }
    }

    public static class FirefighterSetup
    {
        const string Dir = "Assets/FireEscape/Resources/FireEscapeModels/Firefighter";

        [MenuItem("FireEscape/4. Tạo NPC hướng dẫn (lính cứu hỏa)", priority = 4)]
        public static void Build()
        {
            string fbx = Dir + "/Firefighter.fbx";
            AssetDatabase.ImportAsset(fbx, ImportAssetOptions.ForceUpdate);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (model == null) { Debug.LogError("FireEscape: chưa có " + fbx + " — chạy BlenderSource/firefighter_stageB.py trước."); return; }

            // ---- vật liệu PBR (URP Lit) ----
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(lit) { name = "Firefighter_Mat" };
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/Firefighter_BaseColor.png"));
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/Firefighter_Normal.png"));
            mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/Firefighter_MetallicSmoothness.png"));
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetFloat("_Smoothness", 0.8f);
            // hai mặt: khe cắt ở nách (xem firefighter_stageB.py) lộ mặt trong tối của áo thay vì lỗ trống
            mat.SetFloat("_Cull", 0f);
            mat.doubleSidedGI = true;
            SaveAsset(mat, Dir + "/Firefighter_Mat.mat");
            var face = new Material(lit) { name = "Firefighter_Face" };
            face.SetColor("_BaseColor", new Color(0.62f, 0.45f, 0.36f));
            face.SetFloat("_Smoothness", 0.35f);
            SaveAsset(face, Dir + "/Firefighter_Face.mat");

            // ---- Animator Controller: mỗi clip một state, mặc định Idle ----
            string ctrlPath = Dir + "/FirefighterGuide.controller";
            // dùng lại controller cũ (giữ GUID để prefab không mất tham chiếu), chỉ dọn sạch các lớp
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
            if (ctrl == null) ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            while (ctrl.layers.Length > 1) ctrl.RemoveLayer(ctrl.layers.Length - 1);
            var baseSm = ctrl.layers[0].stateMachine;
            foreach (var cs in baseSm.states.ToArray()) baseSm.RemoveState(cs.state);
            var sm = ctrl.layers[0].stateMachine;
            var clips = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToList();
            foreach (var clip in clips)
            {
                var st = sm.AddState(clip.name);
                st.motion = clip;
                if (clip.name == "Idle") sm.defaultState = st;
            }

            var root = new GameObject("FirefighterGuide");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.transform.SetParent(root.transform, false);

            // ---- lớp "Mask": chỉ điều khiển xương Mask → mọi động tác đều kết hợp được với đeo / tháo mặt nạ ----
            var avMask = new AvatarMask { name = "MaskOnly" };
            // đường dẫn phải tính từ node chứa Animator (AddTransformPath lại tính từ gốc hierarchy nên không dùng được)
            var all = inst.GetComponentsInChildren<Transform>(true).Where(t => t != inst.transform).ToList();
            avMask.transformCount = all.Count;
            for (int i = 0; i < all.Count; i++)
            {
                string path = AnimationUtility.CalculateTransformPath(all[i], inst.transform);
                avMask.SetTransformPath(i, path);
                avMask.SetTransformActive(i, path.EndsWith("/Mask"));
            }
            SaveAsset(avMask, Dir + "/MaskOnly.mask");
            ctrl.AddLayer("Mask");
            var layers = ctrl.layers;
            layers[1].avatarMask = avMask;
            layers[1].defaultWeight = 1f;
            layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
            ctrl.layers = layers;
            var msm = ctrl.layers[1].stateMachine;
            AnimationClip Clip(string n) => clips.First(c => c.name == n);
            var onHold = msm.AddState("MaskOnHold"); onHold.motion = Clip("Idle");
            var offHold = msm.AddState("MaskOffHold"); offHold.motion = Clip("MaskOffIdle");
            var putOn = msm.AddState("MaskOn"); putOn.motion = Clip("MaskOn");
            var takeOff = msm.AddState("MaskOff"); takeOff.motion = Clip("MaskOff");
            var t1 = putOn.AddTransition(onHold); t1.hasExitTime = true; t1.exitTime = 1f; t1.duration = 0.05f;
            var t2 = takeOff.AddTransition(offHold); t2.hasExitTime = true; t2.exitTime = 1f; t2.duration = 0.05f;
            msm.defaultState = offHold;

            // ---- prefab ----
            var anim = inst.GetComponent<Animator>();
            if (anim == null) anim = inst.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var r in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    mats[i] = r.name.Contains("Body") && i == 1 ? face : mat;   // khuôn mặt nay nằm trong texture chính (stage C); slot 1 chỉ còn với FBX cũ
                r.sharedMaterials = mats;
                r.updateWhenOffscreen = true;
            }
            PrefabUtility.SaveAsPrefabAsset(root, Dir + "/FirefighterGuide.prefab");
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log($"FireEscape: đã tạo NPC hướng dẫn ({clips.Count} animation: {string.Join(", ", clips.Select(c => c.name))}).");
        }

        static void SaveAsset(Object o, string path)
        {
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(o, path);
        }
    }
}
