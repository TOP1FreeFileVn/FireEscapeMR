using UnityEngine;
using UnityEngine.InputSystem;

namespace FireEscape
{
    public enum InteractKind { Alarm, Extinguisher, Door, Elevator, Window, Npc }

    public class Interactable : MonoBehaviour
    {
        public InteractKind kind;
        public string id;
    }

    /// <summary>Trừu tượng hóa người chơi: PC (bàn phím/chuột) hoặc Quest (headset + tay cầm).</summary>
    public abstract class PlayerRig : MonoBehaviour
    {
        public bool controlsEnabled;
        public abstract bool IsXR { get; }
        public abstract Camera Cam { get; }
        public abstract Transform Head { get; }
        public abstract Transform HoldAnchor { get; }
        public abstract Ray AimRay { get; }
        public abstract Vector3 FootPos { get; }
        public abstract float HeadHeight { get; }
        public virtual bool Crouching => HeadHeight < 1.2f;
        public virtual float Yaw => Head.eulerAngles.y;
        public abstract bool InteractDown { get; }
        public abstract bool InteractHeld { get; }
        public abstract bool SecondaryDown { get; }
        public abstract bool DropDown { get; }
        public abstract bool MenuDown { get; }
        public abstract bool ConfirmDown { get; }
        public abstract void Teleport(Vector3 foot, float yaw);
        public abstract void SetMenuMode(bool menu);

        public string Key(string action) => IsXR
            ? action switch { "E" => "Trigger", "F" => "A", "Q" => "B", "C" => "Cúi người", _ => action }
            : action switch { "C" => "C/Ctrl", _ => action };
    }

    public class DesktopPlayer : PlayerRig
    {
        CharacterController cc;
        Camera cam;
        Transform camT, hold;
        float yaw, pitch, eye = 1.65f, vy;
        bool menuMode = true;

        public static DesktopPlayer Create()
        {
            var go = new GameObject("DesktopPlayer");
            var p = go.AddComponent<DesktopPlayer>();
            p.cc = go.AddComponent<CharacterController>();
            p.cc.height = 1.8f; p.cc.radius = 0.28f; p.cc.center = new Vector3(0, 0.9f, 0);
            p.cc.stepOffset = 0.3f; p.cc.skinWidth = 0.03f;
            var camGo = new GameObject("Camera");
            camGo.tag = "MainCamera";
            p.camT = camGo.transform;
            p.camT.SetParent(go.transform, false);
            p.camT.localPosition = new Vector3(0, p.eye, 0);
            p.cam = camGo.AddComponent<Camera>();
            p.cam.nearClipPlane = 0.05f;
            p.cam.fieldOfView = 70f;
            p.cam.backgroundColor = new Color(0.05f, 0.06f, 0.08f);
            p.cam.clearFlags = CameraClearFlags.SolidColor;
            camGo.AddComponent<AudioListener>();
            var h = new GameObject("Hold");
            p.hold = h.transform;
            p.hold.SetParent(p.camT, false);
            p.hold.localPosition = new Vector3(0.28f, -0.32f, 0.55f);
            return p;
        }

        public override bool IsXR => false;
        public override Camera Cam => cam;
        public override Transform Head => camT;
        public override Transform HoldAnchor => hold;
        public override Ray AimRay => new Ray(camT.position, camT.forward);
        public override Vector3 FootPos => transform.position;
        public override float HeadHeight => eye;
        public override float Yaw => yaw;

        static Keyboard Kb => Keyboard.current;
        static Mouse Ms => Mouse.current;
        bool Locked => Cursor.lockState == CursorLockMode.Locked;

        public override bool InteractDown => controlsEnabled && Kb != null && (Kb.eKey.wasPressedThisFrame || (Locked && Ms != null && Ms.leftButton.wasPressedThisFrame));
        public override bool InteractHeld => controlsEnabled && Kb != null && (Kb.eKey.isPressed || (Locked && Ms != null && Ms.leftButton.isPressed));
        public override bool SecondaryDown => controlsEnabled && Kb != null && Kb.fKey.wasPressedThisFrame;
        public override bool DropDown => controlsEnabled && Kb != null && Kb.qKey.wasPressedThisFrame;
        public override bool MenuDown => Kb != null && (Kb.escapeKey.wasPressedThisFrame || Kb.pKey.wasPressedThisFrame);
        public override bool ConfirmDown => Kb != null && (Kb.enterKey.wasPressedThisFrame || Kb.spaceKey.wasPressedThisFrame);

        public override void Teleport(Vector3 foot, float yawDeg)
        {
            cc.enabled = false;
            transform.position = foot;
            yaw = yawDeg; pitch = 0f;
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            camT.localRotation = Quaternion.identity;
            cc.enabled = true;
            vy = 0f;
        }

        public override void SetMenuMode(bool menu)
        {
            menuMode = menu;
            Cursor.lockState = menu ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = menu;
        }

        void Update()
        {
            if (Kb == null) return;
            float dt = Time.deltaTime;

            if (!menuMode && controlsEnabled)
            {
                if (Locked && Ms != null)
                {
                    var d = Ms.delta.ReadValue() * 0.08f;
                    yaw += d.x;
                    pitch = Mathf.Clamp(pitch - d.y, -80f, 80f);
                }
                else if (Ms != null && Ms.leftButton.wasPressedThisFrame)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
                // xoay bằng phím mũi tên cho người không dùng chuột
                if (Kb.leftArrowKey.isPressed) yaw -= 90f * dt;
                if (Kb.rightArrowKey.isPressed) yaw += 90f * dt;
                if (Kb.upArrowKey.isPressed) pitch = Mathf.Max(-80f, pitch - 60f * dt);
                if (Kb.downArrowKey.isPressed) pitch = Mathf.Min(80f, pitch + 60f * dt);
            }

            bool crouch = controlsEnabled && !menuMode && (Kb.cKey.isPressed || Kb.leftCtrlKey.isPressed);
            if (!crouch && eye < 1.6f && Physics.SphereCast(transform.position + Vector3.up * 0.5f, 0.25f, Vector3.up, out _, 1.2f, ~0, QueryTriggerInteraction.Ignore))
                crouch = true; // không đứng dậy khi có vật cản trên đầu
            eye = Mathf.MoveTowards(eye, crouch ? 0.95f : 1.65f, 3.5f * dt);
            cc.height = Mathf.Max(1.0f, eye + 0.15f);
            cc.center = new Vector3(0, cc.height / 2f, 0);

            Vector3 move = Vector3.zero;
            if (controlsEnabled && !menuMode)
            {
                float x = (Kb.dKey.isPressed ? 1 : 0) - (Kb.aKey.isPressed ? 1 : 0);
                float z = (Kb.wKey.isPressed ? 1 : 0) - (Kb.sKey.isPressed ? 1 : 0);
                var v = new Vector3(x, 0, z);
                if (v.sqrMagnitude > 1f) v.Normalize();
                float speed = crouch ? 1.3f : Kb.leftShiftKey.isPressed ? 4.2f : 2.6f;
                move = Quaternion.Euler(0, yaw, 0) * v * speed;
            }
            vy = cc.isGrounded ? -1f : vy - 9.81f * dt;
            move.y = vy;
            if (cc.enabled) cc.Move(move * dt);

            transform.rotation = Quaternion.Euler(0, yaw, 0);
            camT.localRotation = Quaternion.Euler(pitch, 0, 0);
            camT.localPosition = new Vector3(0, eye, 0);
        }
    }

    /// <summary>Quest: dùng OVRCameraRig. MR = đi lại thật trong phòng; VR = có thêm di chuyển bằng joystick.</summary>
    public class XRPlayer : PlayerRig
    {
        OVRCameraRig rig;
        float floorY;
        bool locomotion;
        LineRenderer laser;
        bool snapped;

        public static XRPlayer Create(OVRCameraRig rig, bool locomotion, float floorY)
        {
            var p = rig.gameObject.AddComponent<XRPlayer>();
            p.rig = rig;
            p.locomotion = locomotion;
            p.floorY = floorY;
            var lgo = new GameObject("Laser");
            lgo.transform.SetParent(rig.transform, false);
            p.laser = lgo.AddComponent<LineRenderer>();
            p.laser.positionCount = 2;
            p.laser.startWidth = 0.004f; p.laser.endWidth = 0.002f;
            p.laser.material = MatLib.Transparent(new Color(1f, 0.55f, 0.2f, 0.8f));
            p.laser.useWorldSpace = true;
            return p;
        }

        public override bool IsXR => true;
        public override Camera Cam => rig.centerEyeAnchor.GetComponent<Camera>();
        public override Transform Head => rig.centerEyeAnchor;
        public override Transform HoldAnchor => rig.rightHandAnchor;
        public override Ray AimRay => new Ray(rig.rightHandAnchor.position, rig.rightHandAnchor.forward);
        public override Vector3 FootPos { get { var h = Head.position; return new Vector3(h.x, 0f, h.z); } }
        public override float HeadHeight => Head.position.y - floorY;

        public override bool InteractDown => OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
        public override bool InteractHeld => OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
        public override bool SecondaryDown => OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch);
        public override bool DropDown => OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch);
        public override bool MenuDown => OVRInput.GetDown(OVRInput.Button.Start);
        public override bool ConfirmDown => OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch);

        public float LaserLength { get; set; } = 3f;

        public override void Teleport(Vector3 foot, float yaw)
        {
            if (!locomotion) return; // MR: người chơi đứng ở vị trí thật của họ
            float dy = yaw - Head.eulerAngles.y;
            rig.transform.RotateAround(Head.position, Vector3.up, dy);
            var h = Head.position;
            rig.transform.position += new Vector3(foot.x - h.x, 0, foot.z - h.z);
        }

        public override void SetMenuMode(bool menu) { }

        void Update()
        {
            var ray = AimRay;
            laser.SetPosition(0, ray.origin);
            laser.SetPosition(1, ray.origin + ray.direction * LaserLength);
            LaserLength = 3f;

            if (!locomotion || !controlsEnabled) return;
            var stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);
            if (stick.sqrMagnitude > 0.04f)
            {
                var fwd = Head.forward; fwd.y = 0; fwd.Normalize();
                var right = Head.right; right.y = 0; right.Normalize();
                var dir = (fwd * stick.y + right * stick.x);
                float speed = Crouching ? 1.2f : 2.2f;
                var from = Head.position;
                if (!Physics.SphereCast(new Vector3(from.x, floorY + 0.9f, from.z), 0.25f, dir.normalized, out _, 0.4f, ~0, QueryTriggerInteraction.Ignore))
                    rig.transform.position += dir * speed * Time.deltaTime;
            }
            var turn = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch).x;
            if (Mathf.Abs(turn) > 0.7f && !snapped)
            {
                snapped = true;
                rig.transform.RotateAround(Head.position, Vector3.up, Mathf.Sign(turn) * 30f);
            }
            else if (Mathf.Abs(turn) < 0.3f) snapped = false;
        }
    }
}
