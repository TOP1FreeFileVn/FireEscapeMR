"""
Stage C — làm lại texture lính cứu hỏa.
Atlas gốc của Tripo gồm hàng trăm mảnh UV vụn → lem màu ở đường nối, vệt sáng in sẵn.
1) Trải UV mới (Smart UV Project, mảnh lớn) cho thân + mặt nạ chung một atlas.
2) Bake texture gốc 4096 (màu, normal, roughness, metallic) sang UV mới bằng Cycles.
3) Làm sạch màu: vải xanh đen bớt vết bóng đổ loang lổ, vạch phản quang vàng/bạc đều và nét hơn.
4) Ghi texture 2048 + FBX cho Unity.

Chạy: blender -b firefighter_rigged.blend --factory-startup -P firefighter_stageC_texture.py -- <OUT_DIR> <PREVIEW_DIR>
"""
import bpy, bmesh, sys, os, math
import numpy as np

BAD_SKEW = float(os.environ.get("FF_SKEW", "0.3"))
argv = sys.argv[sys.argv.index("--") + 1:]
OUT_DIR, PREVIEW = argv[0], argv[1]
SIZE = 2048
scene = bpy.context.scene
body = bpy.data.objects["FirefighterBody"]
mask = bpy.data.objects["SCBA_Mask"]
rig = bpy.data.objects["FirefighterRig"]
rig.data.pose_position = 'REST'
rig.animation_data.action = None

# ------------------------------------------------------------------ 1) UV mới
for o in (body, mask):
    uv = o.data.uv_layers.new(name="UVClean")
    o.data.uv_layers.active = uv
# Lưới AI nhiễu → Smart UV ra hàng nghìn mảnh li ti. Thay vào đó: đường may theo vùng xương
# (mỗi xương tách nửa trước / nửa sau) → mỗi phần cơ thể 2 mảnh lớn, trải ABF.
from mathutils import Vector
MERGE = {"LeftToes": "LeftFoot", "RightToes": "RightFoot", "LeftShoulder": "Chest", "RightShoulder": "Chest",
         "Neck": "Head", "Root": "Hips"}
segs = {b.name: (b.head_local.copy(), b.tail_local.copy()) for b in rig.data.bones}
def closest(p, a, b):
    ab = b - a; t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
    return a + ab * t
names = [g.name for g in body.vertex_groups]
bm = bmesh.new(); bm.from_mesh(body.data)
dl = bm.verts.layers.deform.active
vb = {}
for v in bm.verts:
    d = v[dl]
    gi = max(d.items(), key=lambda kv: kv[1])[0] if len(d) else names.index("Hips")
    n = names[gi]; vb[v.index] = MERGE.get(n, n)
bm.faces.ensure_lookup_table()
lab = []
for f in bm.faces:
    cnt = {}
    for v in f.verts: cnt[vb[v.index]] = cnt.get(vb[v.index], 0) + 1
    bone = max(cnt, key=cnt.get)
    c = f.calc_center_median()
    a, b = segs[bone]
    side = 0 if (c - closest(c, a, b)).y < 0 else 1
    lab.append((bone, side))
for it in range(4):   # làm mượt nhãn mặt → đường may gọn, không có mảnh vụn
    new = lab[:]
    for f in bm.faces:
        cnt = {}
        for e in f.edges:
            for g in e.link_faces:
                if g is not f: cnt[lab[g.index]] = cnt.get(lab[g.index], 0) + 1
        cnt[lab[f.index]] = cnt.get(lab[f.index], 0) + 1.5
        new[f.index] = max(cnt, key=cnt.get)
    lab = new
seams = 0
for e in bm.edges:
    fs = e.link_faces
    e.seam = len(fs) == 2 and lab[fs[0].index] != lab[fs[1].index]
    seams += e.seam
bm.to_mesh(body.data); bm.free()
print("UV regions", len(set(lab)), "seam edges", seams)

bpy.ops.object.select_all(action='DESELECT')
for o in (body, mask): o.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.unwrap(method='ANGLE_BASED', margin=0.004)

# mảnh trải hỏng (vùng dạng ống kín → ABF kéo thành hình gai): phát hiện theo độ phủ bbox & tỉ lệ diện tích, trải lại bằng Smart UV
bmE = bmesh.from_edit_mesh(body.data)
uvl = bmE.loops.layers.uv["UVClean"]
bmE.faces.ensure_lookup_table()
seen = set(); islands = []
for f0 in bmE.faces:
    if f0.index in seen: continue
    stack = [f0]; seen.add(f0.index); isl = []
    while stack:
        f = stack.pop(); isl.append(f)
        for e in f.edges:
            if e.seam: continue
            for g in e.link_faces:
                if g.index not in seen: seen.add(g.index); stack.append(g)
    islands.append(isl)
def uv_area(f):
    uv = [l[uvl].uv for l in f.loops]
    return abs(sum(uv[i].x * uv[i - 1].y - uv[i - 1].x * uv[i].y for i in range(len(uv)))) / 2
stats = []
for isl in islands:
    a3 = sum(f.calc_area() for f in isl); a2 = sum(uv_area(f) for f in isl)
    us = [l[uvl].uv for f in isl for l in f.loops]
    bb = (max(u.x for u in us) - min(u.x for u in us)) * (max(u.y for u in us) - min(u.y for u in us))
    # độ lệch giãn giữa các mặt trong mảnh: mảnh hình gai có nhiều mặt bị kéo/nén rất mạnh
    fr = sorted(uv_area(f) / max(f.calc_area(), 1e-12) for f in isl)
    fm = fr[len(fr) // 2]
    skew = sum(1 for x in fr if x > 4 * fm or x < fm / 4) / len(fr)
    stats.append((isl, a2 / max(a3, 1e-9), a2 / max(bb, 1e-12), skew))
ratios = sorted(s[1] for s in stats if len(s[0]) > 50)
med = ratios[len(ratios) // 2] if ratios else 1
for s in sorted(stats, key=lambda s: -len(s[0]))[:40]:
    print("ISL faces %5d area %.2f fill %.2f skew %.2f" % (len(s[0]), s[1] / med, s[2], s[3]))
bad = [s[0] for s in stats if len(s[0]) > 20 and (s[2] < 0.12 or s[3] > BAD_SKEW or s[1] < 0.35 * med or s[1] > 3 * med)]
for f in bmE.faces: f.select = False
for isl in bad:
    for f in isl: f.select = True
bmesh.update_edit_mesh(body.data)
bmM = bmesh.from_edit_mesh(mask.data)
for f in bmM.faces: f.select = False
bmesh.update_edit_mesh(mask.data)
print("UV islands", len(islands), "re-projected", len(bad), "faces", sum(len(i) for i in bad))
if bad:
    # chia mảnh hỏng theo hướng pháp tuyến (±X ±Y ±Z, đã làm mượt) → các mảnh gần phẳng, trải ABF lại
    badf = {f.index for isl in bad for f in isl}
    def sector(f):
        n = f.normal; ax = max(range(3), key=lambda i: abs(n[i]))
        return ax * 2 + (n[ax] > 0)
    sub = {i: sector(bmE.faces[i]) for i in badf}
    for it in range(5):
        new = dict(sub)
        for i in badf:
            cnt = {}
            for e in bmE.faces[i].edges:
                for g in e.link_faces:
                    if g.index in sub and g.index != i: cnt[sub[g.index]] = cnt.get(sub[g.index], 0) + 1
            cnt[sub[i]] = cnt.get(sub[i], 0) + 1.5
            new[i] = max(cnt, key=cnt.get)
        sub = new
    for e in bmE.edges:
        fs = e.link_faces
        if len(fs) == 2 and fs[0].index in sub and fs[1].index in sub and sub[fs[0].index] != sub[fs[1].index]:
            e.seam = True
    bmesh.update_edit_mesh(body.data)
    bpy.ops.uv.unwrap(method='ANGLE_BASED', margin=0.004)
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.average_islands_scale()
bpy.ops.uv.pack_islands(rotate=True, margin=0.004)
bpy.ops.object.mode_set(mode='OBJECT')
for o in (body, mask):
    o.data.uv_layers["UVClean"].active_render = True

# ------------------------------------------------------------------ 2) bake
scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 1
scene.render.bake.margin = 8
scene.render.bake.use_clear = True

mat = bpy.data.materials["tripo_mat_f30a6356"]
face = bpy.data.materials["FirefighterFace"]
nt = mat.node_tree; N = nt.nodes; Lk = nt.links
bsdf = next(n for n in N if n.type == 'BSDF_PRINCIPLED')
out = next(n for n in N if n.type == 'OUTPUT_MATERIAL')
tex = {k: next(n for n in N if n.type == 'TEX_IMAGE' and k in n.image.name) for k in ("basecolor", "normal", "roughness", "metallic")}
uvn = N.new("ShaderNodeUVMap"); uvn.uv_map = "UVMap"
for t in tex.values(): Lk.new(uvn.outputs["UV"], t.inputs["Vector"])
nmap = next(n for n in N if n.type == 'NORMAL_MAP'); nmap.uv_map = "UVMap"
emit = N.new("ShaderNodeEmission")

fnt = face.node_tree
fbsdf = next(n for n in fnt.nodes if n.type == 'BSDF_PRINCIPLED')
fout = next(n for n in fnt.nodes if n.type == 'OUTPUT_MATERIAL')
femit = fnt.nodes.new("ShaderNodeEmission")
skin = tuple(fbsdf.inputs["Base Color"].default_value)

def target(name, noncolor):
    img = bpy.data.images.new(name, SIZE, SIZE, alpha=False, float_buffer=False)
    if noncolor: img.colorspace_settings.name = 'Non-Color'
    for tree in (nt, fnt):
        n = tree.nodes.get("BAKE_TARGET") or tree.nodes.new("ShaderNodeTexImage")
        n.name = "BAKE_TARGET"; n.image = img
        tree.nodes.active = n
    return img

# nguồn bake: nếu có bản gốc (lưới đã remesh) thì chiếu từ bản gốc → lưới mới (selected-to-active),
# ngược lại bake chính nó theo UV cũ
SRC = bpy.data.objects.get("FirefighterSource")
if SRC:
    SRC.hide_set(False); SRC.hide_render = False
    for o in (body, mask): o.hide_render = False
def do_bake(**kw):
    if not SRC:
        bpy.ops.object.bake(**kw); return
    for i, tgt in enumerate((body, mask)):
        bpy.ops.object.select_all(action='DESELECT')
        SRC.select_set(True); tgt.select_set(True)
        bpy.context.view_layer.objects.active = tgt
        bpy.ops.object.bake(use_selected_to_active=True, cage_extrusion=0.02, max_ray_distance=0.06,
                            use_clear=(i == 0), **kw)

def bake_emit(src_socket, face_value, name, noncolor):
    img = target(name, noncolor)
    Lk.new(src_socket, emit.inputs["Color"]); Lk.new(emit.outputs[0], out.inputs["Surface"])
    femit.inputs["Color"].default_value = face_value
    fnt.links.new(femit.outputs[0], fout.inputs["Surface"])
    do_bake(type='EMIT')
    print("BAKED", name)
    return img

base_img = bake_emit(tex["basecolor"].outputs["Color"], skin, "FF_Base", False)
rough_img = bake_emit(tex["roughness"].outputs["Color"], (0.65, 0.65, 0.65, 1), "FF_Rough", True)
met_img = bake_emit(tex["metallic"].outputs["Color"], (0, 0, 0, 1), "FF_Metal", True)
# normal: trả shader về Principled (có normal map theo UV cũ) rồi bake tangent-space theo UV mới
Lk.new(bsdf.outputs[0], out.inputs["Surface"]); fnt.links.new(fbsdf.outputs[0], fout.inputs["Surface"])
nrm_img = target("FF_Normal", True)
do_bake(type='NORMAL', normal_space='TANGENT')
if SRC: SRC.hide_render = True; SRC.hide_set(True)
print("BAKED normal")

# ------------------------------------------------------------------ 2b) KHUÔN MẶT: vẽ texture mặt (chiếu thẳng từ trước) và bake đè lên các mặt vá
# hệ toạ độ trùng với phần tạo hình mặt ở stage B (mắt z≈1,496 x=±0,04; mày 1,518; mũi 1,435–1,45; miệng 1,41)
FX0, FX1, FZ0, FZ1, FS = -0.12, 0.12, 1.40, 1.64, 1024
EYE_X, EYE_Z, BROW_Z, NOSE_Z, MOUTH_Z = 0.032, 1.545, 1.567, 1.490, 1.462   # trùng với stage B
def paint_face():
    xs = np.linspace(FX0, FX1, FS, dtype=np.float32); zs = np.linspace(FZ0, FZ1, FS, dtype=np.float32)
    X, Z = np.meshgrid(xs, zs)                     # hàng = z (dưới lên), cột = x
    skin = np.array([0.80, 0.60, 0.49], np.float32)
    img = np.ones((FS, FS, 3), np.float32) * skin
    def blend(mask, col, a=1.0):
        m = np.clip(mask, 0, 1)[..., None] * a
        img[:] = img * (1 - m) + np.array(col, np.float32) * m
    def soft(d, w):                                # 1 bên trong, mềm dần ra ngoài khoảng w
        return np.clip(0.5 - d / w, 0, 1)
    gauss = lambda cx, cz, sx, sz: np.exp(-(((X - cx) / sx) ** 2 + ((Z - cz) / sz) ** 2))
    r = np.sqrt((X / 0.075) ** 2 + ((Z - 1.505) / 0.10) ** 2)
    img *= (1 - 0.30 * np.clip(r - 0.6, 0, 1))[..., None]                   # tối dần ra mép (bóng mũ trùm)
    img *= (1 - 0.18 * np.clip((Z - 1.575) / 0.03, 0, 1))[..., None]        # bóng vành mũ trên trán
    for s in (-1, 1):
        ex = s * EYE_X
        blend(gauss(s * 0.047, 1.505, 0.016, 0.014), (0.84, 0.52, 0.45), 0.22)   # má hồng
        blend(gauss(ex, EYE_Z, 0.019, 0.011), (0.55, 0.38, 0.32), 0.35)          # hốc mắt
        e = np.sqrt(((X - ex) / 0.0125) ** 2 + ((Z - EYE_Z) / 0.0052) ** 2)
        blend(soft(e - 1, 0.25), (0.90, 0.87, 0.84))                              # lòng trắng
        d = np.sqrt((X - ex) ** 2 + (Z - EYE_Z) ** 2)
        blend(soft(d - 0.0048, 0.0010) * (e < 1.1), (0.30, 0.19, 0.11))          # tròng mắt
        blend(soft(d - 0.0021, 0.0008) * (e < 1.1), (0.03, 0.03, 0.03))          # con ngươi
        blend(soft(np.sqrt((X - ex - 0.0015) ** 2 + (Z - EYE_Z - 0.0015) ** 2) - 0.0008, 0.0005), (1, 1, 1), 0.9)
        blend(soft(np.abs(e - 1.0), 0.35) * (Z > EYE_Z - 0.001), (0.12, 0.08, 0.06), 0.9)   # viền mí trên
        bx = (X - ex) / 0.019
        bz = BROW_Z + 0.0035 * (1 - bx ** 2) - 0.0015 * s * bx
        blend(soft(np.abs(Z - bz) - 0.0020, 0.0010) * soft(np.abs(bx) - 1, 0.2), (0.17, 0.11, 0.08))  # lông mày
        blend(gauss(s * 0.0065, NOSE_Z - 0.003, 0.003, 0.0018), (0.32, 0.18, 0.15), 0.8)            # lỗ mũi
        blend(gauss(s * 0.011, 1.52, 0.004, 0.02), (0.55, 0.38, 0.32), 0.25)                          # bóng sống mũi
    lip = np.sqrt((X / 0.020) ** 2 + ((Z - MOUTH_Z) / 0.006) ** 2)
    blend(soft(lip - 1, 0.3), (0.64, 0.37, 0.33))                                                      # môi
    blend(soft(np.abs(Z - (MOUTH_Z - 0.0013 * (X / 0.02) ** 2)) - 0.0005, 0.0007) * soft(np.abs(X) - 0.019, 0.004), (0.28, 0.13, 0.12))
    rng = np.random.default_rng(3)
    grain = rng.random((FS, FS)).astype(np.float32)
    beard = np.clip((1.478 - Z) / 0.02, 0, 1) * np.clip(1 - np.abs(X) / 0.07, 0, 1) * (lip > 1.15)
    img *= (1 - 0.22 * beard * (0.6 + 0.4 * grain))[..., None]                                           # râu lún phún
    img *= (0.97 + 0.03 * grain)[..., None]
    out = bpy.data.images.new("FacePaint", FS, FS, alpha=True)
    px4 = np.ones((FS, FS, 4), np.float32); px4[..., :3] = np.clip(img, 0, 1)
    out.pixels.foreach_set(px4.ravel())
    out.filepath_raw = os.path.join(PREVIEW, "FacePaint.png"); out.file_format = 'PNG'; out.save()
    return out
FACE_IMG = paint_face()

def face_overlay(img, mode):
    """Bake riêng các mặt vá (material 1) vào `img`, không xoá phần đã bake."""
    if not any(p.material_index == 1 for p in body.data.polygons): return
    dup = body.copy(); dup.data = body.data.copy(); dup.modifiers.clear(); dup.parent = None
    dup.matrix_world = body.matrix_world.copy()
    scene.collection.objects.link(dup)
    bm = bmesh.new(); bm.from_mesh(dup.data)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index != 1], context='FACES')
    bm.to_mesh(dup.data); bm.free()
    fm = bpy.data.materials.new("FaceBake"); fm.use_nodes = True
    t = fm.node_tree; Nn = t.nodes; L = t.links
    o = next(n for n in Nn if n.type == 'OUTPUT_MATERIAL'); b = next(n for n in Nn if n.type == 'BSDF_PRINCIPLED')
    if mode == "base":
        tc = Nn.new("ShaderNodeTexCoord"); sp = Nn.new("ShaderNodeSeparateXYZ"); cb = Nn.new("ShaderNodeCombineXYZ")
        mu = Nn.new("ShaderNodeMath"); mu.operation = 'MULTIPLY_ADD'; mu.inputs[1].default_value = 1 / (FX1 - FX0); mu.inputs[2].default_value = -FX0 / (FX1 - FX0)
        mv = Nn.new("ShaderNodeMath"); mv.operation = 'MULTIPLY_ADD'; mv.inputs[1].default_value = 1 / (FZ1 - FZ0); mv.inputs[2].default_value = -FZ0 / (FZ1 - FZ0)
        ti = Nn.new("ShaderNodeTexImage"); ti.image = FACE_IMG; ti.extension = 'EXTEND'
        em = Nn.new("ShaderNodeEmission")
        L.new(tc.outputs["Object"], sp.inputs[0]); L.new(sp.outputs["X"], mu.inputs[0]); L.new(sp.outputs["Z"], mv.inputs[0])
        L.new(mu.outputs[0], cb.inputs["X"]); L.new(mv.outputs[0], cb.inputs["Y"]); L.new(cb.outputs[0], ti.inputs["Vector"])
        L.new(ti.outputs["Color"], em.inputs["Color"]); L.new(em.outputs[0], o.inputs["Surface"])
    elif mode in ("rough", "metal"):
        em = Nn.new("ShaderNodeEmission"); v = 0.62 if mode == "rough" else 0.0
        em.inputs["Color"].default_value = (v, v, v, 1); L.new(em.outputs[0], o.inputs["Surface"])
    tgt = Nn.new("ShaderNodeTexImage"); tgt.image = img; Nn.active = tgt
    dup.data.materials.clear(); dup.data.materials.append(fm)
    for p in dup.data.polygons: p.material_index = 0
    bpy.ops.object.select_all(action='DESELECT'); dup.select_set(True); bpy.context.view_layer.objects.active = dup
    kw = dict(type='NORMAL', normal_space='TANGENT') if mode == "normal" else dict(type='EMIT')
    bpy.ops.object.bake(use_selected_to_active=False, use_clear=False, margin=4, **kw)
    bpy.data.objects.remove(dup); bpy.data.materials.remove(fm)
    print("FACE baked", mode)

face_overlay(rough_img, "rough"); face_overlay(met_img, "metal"); face_overlay(nrm_img, "normal")

# ------------------------------------------------------------------ 3) làm sạch màu
def px(img):
    a = np.empty(SIZE * SIZE * 4, dtype=np.float32); img.pixels.foreach_get(a)
    return a.reshape(SIZE, SIZE, 4)

def blur(a, r):
    """Box blur 2D (tách trục, cộng dồn) — a: HxW hoặc HxWxC."""
    def one(x, axis):
        pad = [(0, 0)] * x.ndim; pad[axis] = (r + 1, r)
        c = np.cumsum(np.pad(x, pad, mode='edge'), axis=axis, dtype=np.float64)
        hi = np.take(c, range(2 * r + 1, c.shape[axis]), axis=axis)
        lo = np.take(c, range(0, c.shape[axis] - 2 * r - 1), axis=axis)
        return ((hi - lo) / (2 * r + 1)).astype(np.float32)
    return one(one(a, 0), 1)

P = px(base_img)                        # giá trị đã ở không gian sRGB (0..1)
c = P[..., :3].copy()
L = c @ np.array([0.299, 0.587, 0.114], dtype=np.float32)
mx, mn = c.max(-1), c.min(-1)
sat = (mx - mn) / np.maximum(mx, 1e-4)
r, g, b = c[..., 0], c[..., 1], c[..., 2]
yellow = (r > 0.45) & (g > 0.45) & (b < 0.45 * g) & (sat > 0.45)
silver = (sat < 0.18) & (L > 0.5)
red = (r > 0.35) & (r > g * 1.6) & (r > b * 1.6)
fabric = (L < 0.33) & ~yellow & ~red

# vải: bỏ loang lổ tần số thấp (bóng đổ/AO in sẵn), giữ chi tiết nếp vải
Lb = blur(L, 28)
fab_mean = float(L[fabric].mean())
gain = np.clip(fab_mean / np.maximum(Lb, 1e-3), 0.7, 1.45) ** 0.55
c = np.where(fabric[..., None], c * gain[..., None], c)
# ám xanh navy đồng nhất cho vải
navy = np.array([0.16, 0.19, 0.30], dtype=np.float32)
Lc = c @ np.array([0.299, 0.587, 0.114], dtype=np.float32)
navy_px = navy * (Lc / float(navy @ np.array([0.299, 0.587, 0.114])))[..., None]
c = np.where(fabric[..., None], c * 0.55 + navy_px * 0.45, c)
# vạch phản quang
yref = np.array([0.86, 0.92, 0.12], dtype=np.float32)
c = np.where(yellow[..., None], c * 0.4 + yref * (0.75 + 0.25 * L / max(float(L[yellow].mean()), 1e-3))[..., None] * 0.6, c)
sref = np.array([0.78, 0.80, 0.80], dtype=np.float32)
c = np.where(silver[..., None], c * 0.5 + sref * 0.5, c)
# mũ đỏ: đều màu hơn
rref = np.array([0.85, 0.12, 0.08], dtype=np.float32)
c = np.where(red[..., None], c * 0.6 + rref * (L / max(float(L[red].mean()), 1e-3))[..., None] * 0.4, c)
# làm nét nhẹ
c = c + 0.35 * (c - blur(c, 2))
P[..., :3] = np.clip(c, 0, 1); P[..., 3] = 1
base_img.pixels.foreach_set(P.ravel())
print("CLEAN fabric %.2f yellow %.3f silver %.3f red %.3f" % (fabric.mean(), yellow.mean(), silver.mean(), red.mean()))

face_overlay(base_img, "base")   # sau bước làm sạch màu: lông mày/mắt tối không bị nhuộm thành vải navy
# gộp mặt vào material chính (Unity dùng một material; texture đã có khuôn mặt)
for p in body.data.polygons: p.material_index = 0
while len(body.data.materials) > 1: body.data.materials.pop(index=len(body.data.materials) - 1)
# ------------------------------------------------------------------ 4) ghi texture + gắn vào material + FBX
def save(img, fname):
    img.filepath_raw = os.path.join(OUT_DIR, fname); img.file_format = 'PNG'; img.save()
save(base_img, "Firefighter_BaseColor.png")
save(nrm_img, "Firefighter_Normal.png")
R = px(rough_img); M = px(met_img)
ms = np.zeros_like(R); ms[..., 0] = ms[..., 1] = ms[..., 2] = M[..., 0]
# vạch phản quang mịn hơn một chút, vải thô hơn
sm = 1.0 - R[..., 0]
ms[..., 3] = np.clip(sm, 0, 1)
ms_img = bpy.data.images.new("FF_MS", SIZE, SIZE, alpha=True, float_buffer=False)
ms_img.colorspace_settings.name = 'Non-Color'
ms_img.pixels.foreach_set(ms.ravel()); save(ms_img, "Firefighter_MetallicSmoothness.png")

tex["basecolor"].image = base_img; tex["normal"].image = nrm_img
tex["roughness"].image = rough_img; tex["metallic"].image = met_img
for t in tex.values():
    for l in list(t.inputs["Vector"].links): Lk.remove(l)
nmap.uv_map = "UVClean"
for tree in (nt, fnt):
    n = tree.nodes.get("BAKE_TARGET")
    if n: tree.nodes.remove(n)
for o in (body, mask):
    o.data.uv_layers.remove(o.data.uv_layers["UVMap"])
    o.data.uv_layers["UVClean"].name = "UVMap"
nmap.uv_map = "UVMap"
rig.data.pose_position = 'POSE'

bpy.ops.object.select_all(action='DESELECT')
for o in (rig, body, mask): o.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(
    filepath=os.path.join(OUT_DIR, "Firefighter.fbx"), use_selection=True,
    object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False, armature_nodetype='NULL',
    apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
    bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False, bake_anim_use_all_actions=True,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, mesh_smooth_type='FACE', path_mode='STRIP')
print("EXPORTED")
for img in (base_img, nrm_img, rough_img, met_img): img.pack()
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(os.path.dirname(bpy.data.filepath), "firefighter_textured.blend"))

# ------------------------------------------------------------------ preview
from mathutils import Vector
scene.render.engine = "BLENDER_WORKBENCH"
sh = scene.display.shading; sh.light = "STUDIO"; sh.color_type = "TEXTURE"; sh.show_cavity = True
scene.render.resolution_x = 600; scene.render.resolution_y = 900
cd = bpy.data.cameras.new("Cam"); cd.type = 'ORTHO'; cd.ortho_scale = 2.1
cam = bpy.data.objects.new("Cam", cd); scene.collection.objects.link(cam); scene.camera = cam
ctr = Vector((0, 0, 0.95))
for name, off in (("front", Vector((0.55, -1, 0.1))), ("back", Vector((-0.5, 1, 0.1)))):
    cam.location = ctr + off.normalized() * 5
    cam.rotation_euler = (ctr - cam.location).to_track_quat("-Z", "Y").to_euler()
    rig.animation_data.action = bpy.data.actions["Point"]; scene.frame_set(30 if name == "front" else 1)
    scene.render.filepath = os.path.join(PREVIEW, f"C_{name}.png")
    bpy.ops.render.render(write_still=True)
print("DONE")
# cận mặt khi đã tháo mặt nạ
rig.animation_data.action = bpy.data.actions["MaskOffIdle"]; scene.frame_set(30)
cd.ortho_scale = 0.45
fc = Vector((0, 0, 1.47))
for name, off in (("face", Vector((0.25, -1, 0.05))), ("face_side", Vector((1, -0.6, 0.05)))):
    cam.location = fc + off.normalized() * 3
    cam.rotation_euler = (fc - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.resolution_x = scene.render.resolution_y = 600
    scene.render.filepath = os.path.join(PREVIEW, f"C_{name}.png")
    bpy.ops.render.render(write_still=True)
print("FACE PREVIEW")
