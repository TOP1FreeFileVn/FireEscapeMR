"""
Stage B: tách mặt nạ SCBA thành vật thể riêng, vá lỗ mặt, dựng xương (tên theo chuẩn Unity), gán trọng số da,
tạo 8 animation, gộp texture PBR và xuất FBX cho Unity.
Chạy: blender -b firefighter_A.blend -P firefighter_stageB.py -- <out_dir_unity> <preview_dir> <texture_dir>
"""
import bpy, bmesh, sys, os, math
from mathutils import Vector, Quaternion, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
OUT_DIR, PREVIEW, TEX_DIR = argv[0], argv[1], argv[2]
os.makedirs(OUT_DIR, exist_ok=True); os.makedirs(PREVIEW, exist_ok=True)
FPS = 30
scene = bpy.context.scene
scene.render.fps = FPS

body = bpy.data.objects["FirefighterBody"]
me = body.data
V = [v.co.copy() for v in me.vertices]

def centroid(pts):
    s = Vector((0, 0, 0))
    for p in pts: s += p
    return s / max(1, len(pts))

# ------------------------------------------------------------------ mốc giải phẫu
side = {}
for sgn, key in ((1, "L"), (-1, "R")):        # nhân vật nhìn -Y: tay trái của nhân vật ở +X
    hand = [p for p in V if p.x * sgn > 0.28 and 0.62 < p.z < 0.95]
    ankle = [p for p in V if p.x * sgn > 0.04 and 0.07 < p.z < 0.15]
    foot = [p for p in V if p.x * sgn > 0.04 and p.z < 0.06]
    side[key] = {"hand": centroid(hand), "ankle": centroid(ankle), "foot": centroid(foot)}
    print(key, "hand", tuple(round(v, 3) for v in side[key]["hand"]), "ankle", tuple(round(v, 3) for v in side[key]["ankle"]), "n", len(hand), len(ankle))

head_pts = [p for p in V if p.z > 1.45 and abs(p.x) < 0.12]
head_c = centroid(head_pts)
torso_y = centroid([p for p in V if 1.0 < p.z < 1.3 and abs(p.x) < 0.15 and p.y < 0.12]).y
print("head", tuple(round(v, 3) for v in head_c), "torso_y", round(torso_y, 3))

J = {}
J["Hips"] = Vector((0, torso_y, 0.98))
J["Spine"] = Vector((0, torso_y, 1.10))
J["Chest"] = Vector((0, torso_y, 1.24))
J["Neck"] = Vector((0, torso_y - 0.02, 1.44))
J["Head"] = Vector((0, head_c.y + 0.03, 1.52))
J["HeadTop"] = Vector((0, head_c.y + 0.03, 1.78))
for key, sgn in (("L", 1), ("R", -1)):
    J[key + "Shoulder"] = Vector((0.05 * sgn, torso_y, 1.40))
    # tâm tay áo theo chiều trước-sau: tay áo phồng nằm lùi sau mặt ngực ~10 cm (đo lát cắt ngoài |x| > 0,22)
    def arm_y(z0):
        pts = [p for p in V if p.x * sgn > 0.22 and abs(p.z - z0) < 0.03]
        return (min(p.y for p in pts) + max(p.y for p in pts)) / 2 if pts else torso_y
    J[key + "UpperArm"] = Vector((0.19 * sgn, (torso_y + arm_y(1.28)) / 2, 1.38))
    hand = side[key]["hand"]
    wrist = hand + (J[key + "UpperArm"] - hand).normalized() * 0.09
    elbow = (J[key + "UpperArm"] + wrist) / 2 + Vector((0.02 * sgn, 0, 0))
    elbow.y = arm_y(elbow.z)
    print(key, "arm y shoulder", round(J[key + "UpperArm"].y, 3), "elbow", round(elbow.y, 3), "wrist", round(wrist.y, 3))
    J[key + "LowerArm"] = elbow
    J[key + "Hand"] = wrist
    J[key + "HandTip"] = hand - (J[key + "UpperArm"] - hand).normalized() * 0.08
    J[key + "UpperLeg"] = Vector((0.1 * sgn, torso_y, 0.92))
    ank = side[key]["ankle"]
    J[key + "LowerLeg"] = (J[key + "UpperLeg"] + ank) / 2 + Vector((0, -0.02, 0))
    J[key + "Foot"] = Vector((ank.x, ank.y + 0.02, 0.1))
    J[key + "Toes"] = Vector((ank.x, side[key]["foot"].y - 0.1, 0.03))
    J[key + "ToeTip"] = J[key + "Toes"] + Vector((0, -0.08, 0))

# ------------------------------------------------------------------ tách mặt nạ SCBA
bpy.context.view_layer.objects.active = body
bm = bmesh.new(); bm.from_mesh(me)
bm.faces.ensure_lookup_table()
mask_y = head_c.y - 0.075
def in_mask(p):
    return 1.33 < p.z < 1.60 and abs(p.x) < 0.115 and p.y < mask_y
mask_faces = [f for f in bm.faces if all(in_mask(v.co) for v in f.verts)]
print("MASK faces", len(mask_faces))
for f in bm.faces: f.select = False
for f in mask_faces: f.select = True
bm.to_mesh(me); bm.free()
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.separate(type='SELECTED')
bpy.ops.object.mode_set(mode='OBJECT')
mask = [o for o in bpy.context.scene.objects if o.type == "MESH" and o != body and o.name != "FirefighterSource"][0]
mask.name = "SCBA_Mask"; mask.data.name = "SCBA_Mask"

# vá lỗ sau khi tách mặt nạ + DỰNG KHUÔN MẶT (lưới AI gốc không có mặt dưới mặt nạ)
# mốc đo từ bản gốc (ống thở ở miệng/mũi z≈1,47–1,50, vành mũ 1,63) — stage C vẽ texture theo đúng các mốc này (mét)
EYE_X, EYE_Z, BROW_Z, NOSE_Z, MOUTH_Z, CHIN_Z, FACE_MIN_Z = 0.032, 1.545, 1.567, 1.490, 1.462, 1.428, 1.415
import math as _m
def face_depth(x, z):
    """Độ nhô ra trước (m) tại điểm (x, z) trên mặt."""
    g = lambda a, b: _m.exp(-(a * a + b * b))
    dome = 0.022 * max(0.0, 1 - (x / 0.085) ** 2) * max(0.0, 1 - ((z - 1.505) / 0.10) ** 2)
    nose = 0.016 * _m.exp(-(x / 0.012) ** 2) * min(1.0, max(0.0, (1.56 - z) / 0.065)) * min(1.0, max(0.0, (z - NOSE_Z + 0.012) / 0.012))
    brow = 0.005 * _m.exp(-((z - BROW_Z) / 0.008) ** 2) * max(0.0, 1 - (x / 0.07) ** 2)
    eyes = -0.006 * g((abs(x) - EYE_X) / 0.016, (z - EYE_Z) / 0.009)
    lips = 0.004 * g(x / 0.024, (z - MOUTH_Z) / 0.008)
    chin = 0.007 * g(x / 0.03, (z - CHIN_Z) / 0.014)
    cheek = 0.004 * g((abs(x) - 0.05) / 0.025, (z - 1.51) / 0.02)
    return dome + nose + brow + eyes + lips + chin + cheek

bm = bmesh.new(); bm.from_mesh(me)
boundary = [e for e in bm.edges if e.is_boundary and all(in_mask(v.co) or (1.3 < v.co.z < 1.63 and abs(v.co.x) < 0.13 and v.co.y < mask_y + 0.03) for v in e.verts)]
res = bmesh.ops.holes_fill(bm, edges=boundary, sides=0)
for f in bm.faces: f.material_index = 0
# chỉ lỗ ở vùng mặt mới là da; lỗ khác (chỗ ống thở nối xuống ngực) giữ vật liệu áo
for f in res["faces"]:
    if f.calc_center_median().z > FACE_MIN_Z: f.material_index = 1
bmesh.ops.triangulate(bm, faces=res["faces"])
patch = {f for f in bm.faces if f.material_index == 1}
# chia nhỏ vừa đủ để tạo hình mặt (~1 cm)
for it in range(2):
    edges = list({e for f in patch if f.is_valid for e in f.edges})
    r = bmesh.ops.subdivide_edges(bm, edges=edges, cuts=1, use_grid_fill=True)
    patch = {f for f in bm.faces if f.material_index == 1}
    bmesh.ops.beautify_fill(bm, faces=list(patch), edges=list({e for f in patch for e in f.edges if not e.is_boundary}))
    patch = {f for f in bm.faces if f.material_index == 1}
inner = [v for v in {v for f in patch for v in f.verts} if all(ff.material_index == 1 for ff in v.link_faces)]
for it in range(12):
    bmesh.ops.smooth_vert(bm, verts=inner, factor=0.6, use_axis_x=True, use_axis_y=True, use_axis_z=True)
# giới hạn: mặt không được xuyên qua mặt nạ khi đeo → tra mặt trong của mặt nạ theo lưới xz 1 cm
mask_back = {}
for v in mask.data.vertices:
    k = (round(v.co.x / 0.01), round(v.co.z / 0.01))
    mask_back[k] = max(mask_back.get(k, -9), v.co.y)
def mask_limit(x, z):
    ys = [mask_back[k] for k in ((round(x / 0.01) + a, round(z / 0.01) + b) for a in (-1, 0, 1) for b in (-1, 0, 1)) if k in mask_back]
    return max(ys) + 0.006 if ys else -9
moved = 0
for v in inner:
    ny = v.co.y - face_depth(v.co.x, v.co.z)
    v.co.y = max(ny, mask_limit(v.co.x, v.co.z)); moved += 1
bmesh.ops.smooth_vert(bm, verts=inner, factor=0.3, use_axis_y=True)
bm.normal_update()
bm.to_mesh(me); bm.free()
skin = bpy.data.materials.new("FirefighterFace")
skin.diffuse_color = (0.62, 0.45, 0.36, 1)
try:
    skin.use_nodes = True
    skin.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.62, 0.45, 0.36, 1)
    skin.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.65
except Exception:
    pass
me.materials.append(skin)
me.update()
print("PATCH faces", sum(1 for p in me.polygons if p.material_index == 1), "face verts shaped", moved)

# ------------------------------------------------------------------ xương
arm_data = bpy.data.armatures.new("FirefighterRig")
rig = bpy.data.objects.new("FirefighterRig", arm_data)
scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode='EDIT')
eb = arm_data.edit_bones
def bone(name, head, tail, parent=None, connect=False):
    b = eb.new(name); b.head = head; b.tail = tail
    if parent: b.parent = eb[parent]; b.use_connect = connect
    return b
bone("Root", Vector((0, 0, 0)), Vector((0, 0.25, 0)))
bone("Hips", J["Hips"], J["Spine"], "Root")
bone("Spine", J["Spine"], J["Chest"], "Hips", True)
bone("Chest", J["Chest"], J["Neck"], "Spine", True)
bone("Neck", J["Neck"], J["Head"], "Chest", True)
bone("Head", J["Head"], J["HeadTop"], "Neck", True)
bone("Mask", Vector((0, mask_y - 0.02, 1.47)), Vector((0, mask_y - 0.14, 1.47)), "Head")
for k, full in (("L", "Left"), ("R", "Right")):
    bone(full + "Shoulder", J[k + "Shoulder"], J[k + "UpperArm"], "Chest")
    bone(full + "UpperArm", J[k + "UpperArm"], J[k + "LowerArm"], full + "Shoulder", True)
    bone(full + "LowerArm", J[k + "LowerArm"], J[k + "Hand"], full + "UpperArm", True)
    bone(full + "Hand", J[k + "Hand"], J[k + "HandTip"], full + "LowerArm", True)
    bone(full + "UpperLeg", J[k + "UpperLeg"], J[k + "LowerLeg"], "Hips")
    bone(full + "LowerLeg", J[k + "LowerLeg"], J[k + "Foot"], full + "UpperLeg", True)
    bone(full + "Foot", J[k + "Foot"], J[k + "Toes"], full + "LowerLeg", True)
    bone(full + "Toes", J[k + "Toes"], J[k + "ToeTip"], full + "Foot", True)
for b in eb:  # hướng roll thống nhất (trục Z của xương hướng về phía trước -Y)
    b.align_roll(Vector((0, -1, 0)) if abs(b.vector.normalized().y) < 0.9 else Vector((0, 0, 1)))
bpy.ops.object.mode_set(mode='OBJECT')

# ------------------------------------------------------------------ trọng số da (khoảng cách tới đoạn xương + ràng buộc vùng)
def seg_dist(p, a, b):
    ab = b - a; t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
    return (p - (a + ab * t)).length
bones_ws = {b.name: (b.head_local.copy(), b.tail_local.copy()) for b in arm_data.bones if b.name not in ("Root", "Mask")}

# bán kính gần đúng của từng đoạn cơ thể: khoảng cách tính tới BỀ MẶT khối trụ, không phải đường tâm
RADIUS = {"Hips": 0.2, "Spine": 0.21, "Chest": 0.21, "Neck": 0.08, "Head": 0.12,
          "Shoulder": 0.07, "UpperArm": 0.09, "LowerArm": 0.075, "Hand": 0.05,
          "UpperLeg": 0.09, "LowerLeg": 0.07, "Foot": 0.06, "Toes": 0.05}
def radius(name):
    for k, r in RADIUS.items():
        if name.endswith(k): return r
    return 0.06

def torso_half_width(z):
    return 0.225 if z < 1.28 else (0.19 if z < 1.36 else 0.16)

def allowed(p, name):
    x, z = p.x, p.z
    back_pack = p.y > torso_y + 0.12 and 0.85 < z < 1.5 and abs(x) < torso_half_width(z) - 0.01   # bình khí trên lưng (không lấn sang tay áo)
    if back_pack: return name in ("Chest", "Spine")
    is_arm = any(k in name for k in ("Shoulder", "UpperArm", "LowerArm", "Hand"))
    if name.startswith("Left"):
        if "Leg" in name or "Foot" in name or "Toes" in name: return x > -0.02 and z < 1.02
        if name == "LeftShoulder": return x > 0.06 and z > 1.22
        return is_arm and x > torso_half_width(z) - 0.02 and z > 0.6
    if name.startswith("Right"):
        if "Leg" in name or "Foot" in name or "Toes" in name: return x < 0.02 and z < 1.02
        if name == "RightShoulder": return x < -0.06 and z > 1.22
        return is_arm and x < -(torso_half_width(z) - 0.02) and z > 0.6
    if name == "Head": return z > 1.4
    if name == "Neck": return 1.3 < z < 1.6
    if name == "Hips": return z < 1.15
    return 0.8 < z < 1.6

def assign(obj, force=None):
    obj.vertex_groups.clear()
    groups = {n: obj.vertex_groups.new(name=n) for n in list(bones_ws.keys()) + ["Mask"]}
    for v in obj.data.vertices:
        p = obj.matrix_world @ v.co
        if force:
            groups[force].add([v.index], 1.0, 'REPLACE'); continue
        cand = []
        for n, (a, b) in bones_ws.items():
            if not allowed(p, n): continue
            cand.append((max(0.004, seg_dist(p, a, b) - radius(n)), n))
        if not cand:
            cand = [(max(0.004, seg_dist(p, a, b) - radius(n)), n) for n, (a, b) in bones_ws.items()]
        cand.sort()
        cand = cand[:3]
        ws = [(1.0 / max(d, 0.01) ** 4, n) for d, n in cand]
        tot = sum(w for w, _ in ws)
        for w, n in ws:
            if w / tot > 0.02: groups[n].add([v.index], w / tot, 'REPLACE')
    mod = obj.modifiers.new("Armature", 'ARMATURE'); mod.object = rig
    obj.parent = rig

assign(body)
assign(mask, force="Mask")

# ---- tách tay áo khỏi sườn áo (AI hàn dính chúng ở tư thế chữ A) ----
from collections import Counter, deque
ARM_KEYS = ("UpperArm", "LowerArm", "Hand")
def region_of(v, obj):
    best, bw = None, -1.0
    for g in v.groups:
        if g.weight > bw: bw = g.weight; best = obj.vertex_groups[g.group].name
    if best is None: return "BODY"
    if best.startswith("Left") and any(k in best for k in ARM_KEYS): return "ARM_L"
    if best.startswith("Right") and any(k in best for k in ARM_KEYS): return "ARM_R"
    return "BODY"

nv = len(me.vertices)
lab = [region_of(v, body) for v in me.vertices]
adj = [[] for _ in range(nv)]
for e in me.edges:
    a, b = e.vertices
    adj[a].append(b); adj[b].append(a)
Z = [v.co.z for v in me.vertices]
SPLIT_Z = 1.30

# 1) làm mượt nhãn: đa số lân cận, lặp nhiều lần (chỉ dưới vai)
for it in range(12):
    new = lab[:]
    for i in range(nv):
        if Z[i] > SPLIT_Z or not adj[i]: continue
        c = Counter(lab[j] for j in adj[i]); c[lab[i]] += 0.5
        new[i] = max(c, key=c.get)
    lab = new

# 2) dọn đảo nhỏ: mỗi nhãn chỉ giữ thành phần liên thông lớn nhất (dưới vai), phần còn lại theo nhãn lân cận
changed = 0
for label in ("ARM_L", "ARM_R", "BODY"):
    seen = [False] * nv; comps = []
    for s in range(nv):
        if seen[s] or lab[s] != label or Z[s] > SPLIT_Z: continue
        q = deque([s]); seen[s] = True; comp = []
        while q:
            i = q.popleft(); comp.append(i)
            for j in adj[i]:
                if not seen[j] and lab[j] == label and Z[j] <= SPLIT_Z:
                    seen[j] = True; q.append(j)
        comps.append(comp)
    if not comps: continue
    comps.sort(key=len, reverse=True)
    keep_min = len(comps[0]) * 0.08
    for comp in comps[1:]:
        # thành phần nối lên vai (trên SPLIT_Z) vẫn hợp lệ
        touches_top = any(Z[j] > SPLIT_Z for i in comp for j in adj[i])
        if touches_top and label != "BODY" and len(comp) > 30: continue
        if len(comp) >= keep_min and label == "BODY": continue
        compset = set(comp)
        c = Counter(lab[j] for i in comp for j in adj[i] if j not in compset)
        if not c: continue
        other = max(c, key=c.get)
        for i in comp: lab[i] = other
        changed += len(comp)
print("RELABELED verts", changed)

# 3) tính lại trọng số theo nhãn đã sạch
def weights_for(p, filt):
    cand = [(max(0.004, seg_dist(p, a, b) - radius(n)), n) for n, (a, b) in bones_ws.items() if filt(n)]
    cand.sort(); cand = cand[:3]
    ws = [(1.0 / max(d, 0.01) ** 4, n) for d, n in cand]
    tot = sum(w for w, _ in ws)
    return [(n, w / tot) for w, n in ws if w / tot > 0.02]
is_arm = lambda n: any(k in n for k in ARM_KEYS)
fixed = 0
for i, v in enumerate(me.vertices):
    if Z[i] > SPLIT_Z: continue
    cur = region_of(v, body)
    if cur == lab[i]: continue
    p = body.matrix_world @ v.co
    if lab[i] == "ARM_L": filt = lambda n: n.startswith("Left") and is_arm(n)
    elif lab[i] == "ARM_R": filt = lambda n: n.startswith("Right") and is_arm(n)
    else: filt = lambda n: not is_arm(n) and allowed(p, n)
    ws = weights_for(p, filt)
    if not ws: continue
    for g in list(v.groups): body.vertex_groups[g.group].remove([i])
    for n, w in ws: body.vertex_groups[n].add([i], w, 'REPLACE')
    fixed += 1
print("REWEIGHTED verts", fixed)

# 4) cắt tam giác nối giữa tay và thân (dưới vai)
bm = bmesh.new(); bm.from_mesh(me)
bm.verts.ensure_lookup_table()
bridge = [f for f in bm.faces if len({lab[v.index] for v in f.verts}) > 1 and max(v.co.z for v in f.verts) < 1.28]
bmesh.ops.delete(bm, geom=bridge, context='FACES_ONLY')
bm.to_mesh(me); bm.free(); me.update()
print("BRIDGE faces removed", len(bridge))

# ------------------------------------------------------------------ animation
rig.rotation_mode = 'QUATERNION'
pbs = rig.pose.bones
for pb in pbs: pb.rotation_mode = 'QUATERNION'

def rest_rot(name):
    return arm_data.bones[name].matrix_local.to_quaternion()

def pose(name, axis, deg):
    """Quay xương quanh trục thế giới (khung nghỉ) — trả về quaternion cục bộ."""
    r = rest_rot(name)
    q = Quaternion(Vector(axis).normalized(), math.radians(deg))
    return r.inverted() @ q @ r

def compose(name, rots):
    q = Quaternion()
    for axis, deg in rots: q = pose(name, axis, deg) @ q
    return q

X, Y, Z = (1, 0, 0), (0, 1, 0), (0, 0, 1)

def new_action(name, frames, loop):
    act = bpy.data.actions.new(name)
    rig.animation_data_create()
    rig.animation_data.action = act
    for pb in pbs:
        pb.rotation_quaternion = Quaternion(); pb.location = Vector((0, 0, 0))
    return act

def key(frame, rots=None, locs=None):
    rots = rots or {}; locs = locs or {}
    for pb in pbs:
        pb.rotation_quaternion = compose(pb.name, rots.get(pb.name, []))
        pb.location = Vector(locs.get(pb.name, (0, 0, 0)))
        pb.keyframe_insert("rotation_quaternion", frame=frame)
        pb.keyframe_insert("location", frame=frame)

def finish(act, loop):
    rig.animation_data.action = None
    track = rig.animation_data.nla_tracks.new(); track.name = act.name
    track.strips.new(act.name, int(act.frame_range[0]), act.action if hasattr(act, "action") else act)
    act.use_fake_user = True

actions = []
def make(name, keys, loop=True):
    act = new_action(name, 0, loop)
    for f, rots, locs in keys: key(f, rots, locs)
    rig.animation_data.action = None
    act.use_fake_user = True
    actions.append(act)
    print("ACTION", name, "frames", tuple(act.frame_range))

# tư thế tay buông tự nhiên hơn (hạ nhẹ từ tư thế chữ A)
def relaxed(extra=None):
    r = {"LeftUpperArm": [(Y, 8)], "RightUpperArm": [(Y, -8)], "LeftLowerArm": [(X, -12)], "RightLowerArm": [(X, -12)]}
    for k, v in (extra or {}).items(): r[k] = r.get(k, []) + v
    return r

# 1) Idle: thở, lắc nhẹ
make("Idle", [
    (1, relaxed({"Chest": [(X, 0)], "Head": [(Z, 0)]}), {}),
    (60, relaxed({"Chest": [(X, -2.5)], "Head": [(Z, 6), (X, 2)], "Spine": [(Y, 1.5)]}), {"Hips": (0, 0.004, 0)}),
    (120, relaxed({"Chest": [(X, 0)], "Head": [(Z, 0)]}), {}),
])

# 2) Talk: gật đầu + tay phải diễn tả
make("Talk", [
    (1, relaxed({"RightUpperArm": [(X, -25), (Y, 10)], "RightLowerArm": [(X, -55)], "Head": [(X, 0)]}), {}),
    (15, relaxed({"RightUpperArm": [(X, -32), (Y, 14)], "RightLowerArm": [(X, -70), (Z, 15)], "Head": [(X, -6)]}), {}),
    (30, relaxed({"RightUpperArm": [(X, -22), (Y, 6)], "RightLowerArm": [(X, -50), (Z, -10)], "Head": [(X, 3), (Z, 8)]}), {}),
    (45, relaxed({"RightUpperArm": [(X, -30), (Y, 12)], "RightLowerArm": [(X, -65)], "Head": [(X, -4), (Z, -6)]}), {}),
    (60, relaxed({"RightUpperArm": [(X, -25), (Y, 10)], "RightLowerArm": [(X, -55)], "Head": [(X, 0)]}), {}),
])

# 3) Point: giơ tay phải chỉ về phía trước (hướng lối thoát)
point_pose = relaxed({"RightShoulder": [(X, -12)], "RightUpperArm": [(X, -42), (Z, -22)], "RightLowerArm": [(X, -18)], "RightHand": [(X, -10)], "Chest": [(Z, -12)], "Head": [(Z, -16)]})
make("Point", [
    (1, relaxed(), {}),
    (12, point_pose, {}),
    (45, point_pose, {}),
    (60, relaxed(), {}),
])

# 4) Beckon: vẫy "đi theo tôi"
bk_up = relaxed({"RightShoulder": [(X, -12)], "RightUpperArm": [(X, -35), (Z, -12)], "RightLowerArm": [(X, -118)], "Chest": [(Z, -12)], "Head": [(Z, -15)]})
bk_out = relaxed({"RightShoulder": [(X, -12)], "RightUpperArm": [(X, -32), (Z, -18)], "RightLowerArm": [(X, -78)], "Chest": [(Z, -12)], "Head": [(Z, -15)]})
make("Beckon", [(1, bk_out, {}), (10, bk_up, {}), (20, bk_out, {}), (30, bk_up, {}), (40, bk_out, {})])

# 5) Walk: tại chỗ (script di chuyển NPC)
def walk_keys(amp, knee, arm, bob, crouch=None):
    base = crouch or {}
    def frame(phase):
        s = math.sin(phase); c = math.cos(phase)
        r = relaxed({
            "LeftUpperLeg": [(X, -amp * s)], "RightUpperLeg": [(X, amp * s)],
            "LeftLowerLeg": [(X, knee * max(0.0, c))], "RightLowerLeg": [(X, knee * max(0.0, -c))],
            "LeftUpperArm": [(X, arm * s)], "RightUpperArm": [(X, -arm * s)],
            "Spine": [(Z, 3 * s)], "Chest": [(Z, -4 * s)],
        })
        for k, v in base.get("rots", {}).items(): r[k] = r.get(k, []) + v
        loc = {"Hips": (0, base.get("drop", 0) + bob * abs(math.cos(phase)), 0)}
        return r, loc
    ks = []
    for i in range(9):
        f = 1 + i * 4
        r, l = frame(i / 8 * 2 * math.pi)
        ks.append((f, r, l))
    return ks
make("Walk", walk_keys(28, 45, 18, 0.025))

# 6) CrouchWalk: đi cúi thấp dưới lớp khói, tay trái che mặt nạ
crouch = {"drop": -0.32, "rots": {
    "Hips": [(X, 10)], "Spine": [(X, 22)], "Chest": [(X, 12)], "Head": [(X, -28)],
    "LeftUpperLeg": [(X, -55)], "RightUpperLeg": [(X, -55)], "LeftLowerLeg": [(X, 85)], "RightLowerLeg": [(X, 85)],
    "LeftFoot": [(X, -30)], "RightFoot": [(X, -30)],
    "LeftUpperArm": [(X, -60), (Y, -15)], "LeftLowerArm": [(X, -80)]}}
make("CrouchWalk", walk_keys(16, 25, 8, 0.015, crouch))

# 7-8) MaskOff / MaskOn: tay phải đưa lên, mặt nạ hạ xuống treo trước ngực
hand_up = relaxed({"RightShoulder": [(X, -12)], "RightUpperArm": [(X, -42), (Z, 18)], "RightLowerArm": [(X, -138)], "Head": [(X, -5)]})
mask_down_loc = (0, 0.06, -0.28)      # toạ độ cục bộ của xương Mask (xấp xỉ: xuống & ra trước)
mask_down = {"Mask": [(X, 70)]}
def merge(a, b):
    r = {k: list(v) for k, v in a.items()}
    for k, v in b.items(): r[k] = r.get(k, []) + v
    return r
make("MaskOff", [
    (1, relaxed(), {}),
    (15, hand_up, {}),
    (22, hand_up, {"Mask": (0, 0.03, -0.02)}),
    (38, merge(relaxed({"RightUpperArm": [(X, -30)], "RightLowerArm": [(X, -60)]}), mask_down), {"Mask": mask_down_loc}),
    (50, merge(relaxed(), mask_down), {"Mask": mask_down_loc}),
], loop=False)
make("MaskOn", [
    (1, merge(relaxed(), mask_down), {"Mask": mask_down_loc}),
    (13, merge(relaxed({"RightUpperArm": [(X, -30)], "RightLowerArm": [(X, -60)]}), mask_down), {"Mask": mask_down_loc}),
    (30, hand_up, {"Mask": (0, 0.03, -0.02)}),
    (38, hand_up, {}),
    (50, relaxed(), {}),
], loop=False)
# tư thế tĩnh "mặt nạ đã tháo" (để giữ trạng thái sau MaskOff)
make("MaskOffIdle", [(1, merge(relaxed(), mask_down), {"Mask": mask_down_loc}), (60, merge(relaxed({"Chest": [(X, -2)]}), mask_down), {"Mask": mask_down_loc})])

# ------------------------------------------------------------------ chống "lởm chởm": đo độ giãn cạnh ở các tư thế khó
# Đỉnh có trọng số lệch hẳn so với lân cận bị kéo thành gai khi tay giơ cao. Lấy mẫu các tư thế,
# làm mượt trọng số (Laplacian) ở vùng bị giãn, cuối cùng cắt những tam giác vẫn còn giãn quá mức.
import numpy as np
scene = bpy.context.scene
SAMPLES = [("Point", f) for f in (15, 30, 45)] + [("Beckon", f) for f in (5, 10, 20, 30)] + \
          [("Talk", f) for f in (15, 30, 45)] + [(a, f) for a in ("Walk", "CrouchWalk") for f in (5, 13, 21, 29)] + \
          [("MaskOn", 25), ("MaskOff", 25)]
gnames = [g.name for g in body.vertex_groups]
nv = len(me.vertices)
ekeys = np.array(sorted({k for p in me.polygons for k in p.edge_keys}), dtype=np.int64)
rest = np.array([v.co[:] for v in me.vertices])
rest_len = np.linalg.norm(rest[ekeys[:, 0]] - rest[ekeys[:, 1]], axis=1) + 1e-6

def read_w():
    W = np.zeros((nv, len(gnames)))
    for v in me.vertices:
        for g in v.groups: W[v.index, g.group] = g.weight
    return W
def write_w(W, idx):
    for i in idx:
        v = me.vertices[i]
        for g in list(v.groups): body.vertex_groups[g.group].remove([int(i)])
        row = W[i]; s = row.sum()
        for gi in np.nonzero(row > 0.01)[0]:
            body.vertex_groups[gi].add([int(i)], float(row[gi] / s), 'REPLACE')

def max_stretch():
    worst = np.ones(len(ekeys))
    for act, f in SAMPLES:
        rig.animation_data.action = bpy.data.actions[act]
        scene.frame_set(f)
        dg = bpy.context.evaluated_depsgraph_get()
        ev = body.evaluated_get(dg); m = ev.to_mesh()
        co = np.empty(nv * 3); m.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
        ev.to_mesh_clear()
        ln = np.linalg.norm(co[ekeys[:, 0]] - co[ekeys[:, 1]], axis=1)
        worst = np.maximum(worst, np.where(ln > 0.012, ln / rest_len, 1.0))
    rig.animation_data.action = None
    return worst

nbr = [[] for _ in range(nv)]
for a, b in ekeys: nbr[a].append(b); nbr[b].append(a)
mask_g = gnames.index("Mask")
for rnd in range(4):
    st = max_stretch()
    bad = np.unique(ekeys[st > 1.5].ravel())
    print(f"STRETCH round {rnd}: edges>1.5 = {(st > 1.5).sum()}, >2.5 = {(st > 2.5).sum()}, verts {len(bad)}")
    if len(bad) == 0: break
    region = set(bad.tolist())
    for i in bad: region.update(nbr[i])
    region = np.array(sorted(region))
    W = read_w()
    for it in range(8):
        W2 = W.copy()
        for i in region:
            if not nbr[i] or W[i, mask_g] > 0.5: continue
            W2[i] = 0.5 * W[i] + 0.5 * W[nbr[i]].mean(axis=0)
        W = W2
    write_w(W, region)

# những tam giác vẫn bị kéo giãn mạnh (khe nách thật sự) → cắt; material hai mặt che lỗ
st = max_stretch()
bad_e = {tuple(k) for k in ekeys[(st > 2.0) & (rest[ekeys[:, 0], 2] > 1.05)].tolist()}
bm = bmesh.new(); bm.from_mesh(me)
kill = [f for f in bm.faces if any(tuple(sorted((e.verts[0].index, e.verts[1].index))) in bad_e for e in f.edges)]
bmesh.ops.delete(bm, geom=kill, context='FACES_ONLY')
loose = [e for e in bm.edges if not e.link_faces]
bmesh.ops.delete(bm, geom=loose, context='EDGES')
bm.to_mesh(me); bm.free(); me.update()
print("STRETCH faces cut", len(kill))

# ------------------------------------------------------------------ texture PBR cho Unity (2048)
import numpy as np
def load(name):
    for f in os.listdir(TEX_DIR):
        if name in f.lower():
            return bpy.data.images.load(os.path.join(TEX_DIR, f))
def save_resized(img, out, size=2048, fmt='PNG'):
    img.scale(size, size)
    img.filepath_raw = out
    img.file_format = fmt
    img.save()
base = load("basecolor"); save_resized(base, os.path.join(OUT_DIR, "Firefighter_BaseColor.png"))
nrm = load("normal"); save_resized(nrm, os.path.join(OUT_DIR, "Firefighter_Normal.png"))
met = load("metallic"); rough = load("roughness")
met.scale(2048, 2048); rough.scale(2048, 2048)
m = np.array(met.pixels[:]).reshape(-1, 4); r = np.array(rough.pixels[:]).reshape(-1, 4)
out = np.zeros_like(m); out[:, 0] = m[:, 0]; out[:, 1] = m[:, 0]; out[:, 2] = m[:, 0]; out[:, 3] = 1.0 - r[:, 0]
ms = bpy.data.images.new("Firefighter_MetallicSmoothness", 2048, 2048, alpha=True)
ms.pixels = out.ravel().tolist()
ms.filepath_raw = os.path.join(OUT_DIR, "Firefighter_MetallicSmoothness.png"); ms.file_format = 'PNG'; ms.save()
print("TEXTURES saved")

# ------------------------------------------------------------------ xuất FBX
bpy.ops.object.select_all(action='DESELECT')
for o in (rig, body, mask): o.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(
    filepath=os.path.join(OUT_DIR, "Firefighter.fbx"), use_selection=True,
    object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False, armature_nodetype='NULL',
    apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
    bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False, bake_anim_use_all_actions=True,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, mesh_smooth_type='FACE', path_mode='STRIP')
print("EXPORTED", os.path.join(OUT_DIR, "Firefighter.fbx"))
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(os.path.dirname(bpy.data.filepath), "firefighter_rigged.blend"))

# ------------------------------------------------------------------ preview các tư thế
scene.render.engine = "BLENDER_WORKBENCH"
sh = scene.display.shading; sh.light = "STUDIO"; sh.color_type = "TEXTURE"; sh.show_cavity = True
scene.render.resolution_x = 600; scene.render.resolution_y = 900
cd = bpy.data.cameras.new("Cam"); cd.type = 'ORTHO'; cd.ortho_scale = 2.1
cam = bpy.data.objects.new("Cam", cd); scene.collection.objects.link(cam); scene.camera = cam
c = Vector((0, 0, 0.95))
cam.location = c + Vector((0.55, -1, 0.1)).normalized() * 5
cam.rotation_euler = (c - cam.location).to_track_quat("-Z", "Y").to_euler()
for act_name, frame in (("Idle", 1), ("Point", 30), ("Beckon", 10), ("Walk", 5), ("CrouchWalk", 5), ("MaskOff", 50), ("Talk", 15)):
    rig.animation_data.action = bpy.data.actions[act_name]
    scene.frame_set(frame)
    scene.render.filepath = os.path.join(PREVIEW, f"B_{act_name}.png")
    bpy.ops.render.render(write_still=True)
rig.animation_data.action = None
