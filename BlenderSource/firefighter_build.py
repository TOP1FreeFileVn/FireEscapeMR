"""
Dựng lính cứu hỏa từ đầu bằng code (không dùng lưới AI), theo ảnh tham chiếu turnaround:
mũ đỏ vạch vàng, mặt nạ SCBA đen + van thở đỏ, áo/quần chống cháy xanh navy với vạch phản quang vàng–bạc,
đai đeo + đồng hồ áp suất, bình khí sau lưng, găng tay, đệm gối, túi hộp, ủng.

Các bước (biến STAGE):
  "model"  — dựng hình + vật liệu (xem trước được ngay trong viewport)
  "rig"    — xương (tên chuẩn Unity, như bản cũ), trọng số da, 9 animation
  "bake"   — trải UV, bake BaseColor/Normal/MetallicSmoothness 2048, xuất FBX + texture cho Unity, lưu .blend
  "all"    — cả ba

Chạy trong Blender đang mở (Blender MCP):  exec(open(r"...firefighter_build.py", encoding="utf-8").read(), {"STAGE": "model"})
Chạy nền:  blender -b -P BlenderSource/firefighter_build.py -- <preview_dir>
Quy ước: mét, chân ở z = 0, nhân vật nhìn về -Y, tay trái nhân vật ở +X.
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix, Quaternion

G = globals()
STAGE = G.get("STAGE", "all")
ROOT = G.get("PROJECT", r"C:/Users/dangp/FireEscapeMR")
OUT_DIR = ROOT + "/Assets/FireEscape/Resources/FireEscapeModels/Firefighter"
BLEND = ROOT + "/BlenderSource/firefighter.blend"
if "--" in sys.argv and len(sys.argv) > sys.argv.index("--") + 1:
    PREVIEW = sys.argv[sys.argv.index("--") + 1]
else:
    PREVIEW = G.get("PREVIEW", ROOT + "/Temp/firefighter_preview")
TEX = 2048
X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))


def log(*a):
    print("[firefighter]", *a)


# ====================================================================== vật liệu
# (màu tuyến tính, roughness, metallic, kiểu bề mặt)
PALETTE = {
    "navy":        ((0.016, 0.021, 0.042), 0.88, 0.0, "fabric"),
    "navy_worn":   ((0.010, 0.011, 0.016), 0.80, 0.0, "fabric"),
    "yellow":      ((0.62, 0.88, 0.02), 0.45, 0.0, None),
    "silver":      ((0.50, 0.52, 0.53), 0.30, 0.35, "tape"),
    "red":         ((0.62, 0.010, 0.006), 0.22, 0.0, None),
    "rubber":      ((0.013, 0.013, 0.014), 0.55, 0.0, None),
    "black":       ((0.016, 0.016, 0.018), 0.85, 0.0, "fabric"),
    "leather":     ((0.012, 0.011, 0.011), 0.45, 0.0, "leather"),
    "glass":       ((0.03, 0.035, 0.04), 0.04, 0.0, None),
    "metal":       ((0.62, 0.62, 0.62), 0.28, 1.0, None),
    "tank":        ((0.23, 0.24, 0.21), 0.30, 0.35, "tank"),
    "skin":        ((0.45, 0.28, 0.20), 0.60, 0.0, None),
    "white":       ((0.80, 0.80, 0.78), 0.40, 0.0, None),
    "gold":        ((0.75, 0.52, 0.15), 0.30, 1.0, None),
    "eye":         ((0.02, 0.015, 0.01), 0.20, 0.0, None),
}
MATS = {}


def make_materials():
    for name, (col, rough, metal, kind) in PALETTE.items():
        m = bpy.data.materials.new("FF_" + name)
        m.diffuse_color = (*col, 1)
        m.roughness = rough; m.metallic = metal
        try: m.use_nodes = True
        except Exception: pass
        nt = m.node_tree; N = nt.nodes; L = nt.links
        bsdf = next(n for n in N if n.type == 'BSDF_PRINCIPLED')
        bsdf.inputs["Roughness"].default_value = rough
        bsdf.inputs["Metallic"].default_value = metal
        coord = N.new("ShaderNodeTexCoord")
        if kind in ("fabric", "leather", "tank", "tape"):
            # màu: loang nhẹ (bẩn, bạc màu) — nhiễu thô
            nz = N.new("ShaderNodeTexNoise"); nz.inputs["Scale"].default_value = 6.0; nz.inputs["Detail"].default_value = 4.0
            L.new(coord.outputs["Object"], nz.inputs["Vector"])
            ramp = N.new("ShaderNodeValToRGB")
            lo = 0.72 if kind == "fabric" else 0.85
            ramp.color_ramp.elements[0].color = (col[0] * lo, col[1] * lo, col[2] * lo, 1)
            ramp.color_ramp.elements[1].color = (col[0] * 1.25, col[1] * 1.25, col[2] * 1.25, 1)
            L.new(nz.outputs["Fac"], ramp.inputs["Fac"]); L.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
            # normal: sợi vải / vân da / sợi carbon
            fine = N.new("ShaderNodeTexNoise")
            fine.inputs["Scale"].default_value = {"fabric": 420.0, "leather": 90.0, "tank": 160.0, "tape": 600.0}[kind]
            fine.inputs["Detail"].default_value = 2.0
            L.new(coord.outputs["Object"], fine.inputs["Vector"])
            bump = N.new("ShaderNodeBump")
            bump.inputs["Strength"].default_value = {"fabric": 0.22, "leather": 0.12, "tank": 0.08, "tape": 0.10}[kind]
            bump.inputs["Distance"].default_value = 0.002
            L.new(fine.outputs["Fac"], bump.inputs["Height"]); L.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
        else:
            bsdf.inputs["Base Color"].default_value = (*col, 1)
        MATS[name] = m


# ====================================================================== công cụ hình học
def ring_pts(c, ax, ay, rx, ry, n, p=2.0, arc=None):
    """Vòng siêu-ellipse quanh c trong mặt phẳng (ax, ay). ax × ay = hướng tiến của ống → pháp tuyến hướng ra."""
    pts = []
    a0, a1 = arc if arc else (0.0, 2 * math.pi)
    cnt = n if arc is None else n + 1
    for i in range(cnt):
        a = a0 + (a1 - a0) * i / n
        ca, sa = math.cos(a), math.sin(a)
        x = math.copysign(abs(ca) ** (2 / p), ca) * rx
        y = math.copysign(abs(sa) ** (2 / p), sa) * ry
        pts.append(c + ax * x + ay * y)
    return pts


def loft(bm, rings, closed=True, cap0=False, cap1=False, wrap=False):
    vs = [[bm.verts.new(p) for p in r] for r in rings]
    n = len(rings[0])
    seq = list(range(len(vs) - 1)) + ([len(vs) - 1] if wrap else [])
    faces = []
    for i in seq:
        a, b = vs[i], vs[(i + 1) % len(vs)]
        for j in range(n if closed else n - 1):
            k = (j + 1) % n
            faces.append(bm.faces.new((a[j], a[k], b[k], b[j])))
    if cap0: faces.append(bm.faces.new(list(reversed(vs[0]))))
    if cap1: faces.append(bm.faces.new(vs[-1]))
    return faces


def catmull(P, sps):
    out = []
    n = len(P)
    for i in range(n - 1):
        p0, p1, p2, p3 = P[max(i - 1, 0)], P[i], P[i + 1], P[min(i + 2, n - 1)]
        for s in range(sps):
            t = s / sps
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3))
    out.append(P[-1].copy())
    return out


def transport_frames(P, ref=None):
    Ts = [(P[min(i + 1, len(P) - 1)] - P[max(i - 1, 0)]).normalized() for i in range(len(P))]
    ref = ref or (Vector((0, -1, 0)) if abs(Ts[0].y) < 0.9 else Vector((1, 0, 0)))
    ax = (ref - Ts[0] * ref.dot(Ts[0])).normalized()
    out = []
    for T in Ts:
        ax = (ax - T * ax.dot(T)).normalized()
        out.append((T, ax, T.cross(ax)))
    return out


class Limb:
    """Ống trơn theo đường cong Catmull-Rom; bán kính (rx theo ax ≈ trước–sau, ry theo ay ≈ ngang)."""
    def __init__(s, ctrl, rads, sps=10, ref=None):
        s.P = catmull([Vector(c) for c in ctrl], sps)
        R = catmull([Vector((r[0], r[1], 0)) for r in rads], sps)
        s.R = [(r.x, r.y) for r in R]
        s.F = transport_frames(s.P, ref)
        s.N = len(s.P) - 1

    def at(s, u):
        u = max(0.0, min(s.N, u))
        i = min(int(u), s.N - 1); f = u - i
        p = s.P[i].lerp(s.P[i + 1], f)
        r = (s.R[i][0] * (1 - f) + s.R[i + 1][0] * f, s.R[i][1] * (1 - f) + s.R[i + 1][1] * f)
        T = s.F[i][0].lerp(s.F[i + 1][0], f).normalized()
        ax = s.F[i][1].lerp(s.F[i + 1][1], f); ax = (ax - T * ax.dot(T)).normalized()
        return p, T, ax, T.cross(ax), r

    def u_at_z(s, z):
        for i in range(s.N):
            a, b = s.P[i].z, s.P[i + 1].z
            if (a - z) * (b - z) <= 0 and a != b:
                return i + (z - a) / (b - a)
        return 0.0 if abs(s.P[0].z - z) < abs(s.P[-1].z - z) else float(s.N)

    def rings(s, u0=0.0, u1=None, steps=None, add=0.0, scale=1.0, n=24, p=2.0, arc=None):
        u1 = s.N if u1 is None else u1
        steps = steps or max(2, int(abs(u1 - u0) * 1.0))
        out = []
        for k in range(steps + 1):
            c, T, ax, ay, r = s.at(u0 + (u1 - u0) * k / steps)
            out.append(ring_pts(c, ax, ay, r[0] * scale + add, r[1] * scale + add, n, p, arc))
        return out

    def band(s, z0, z1, add, n=24, p=2.0, steps=3):
        return s.rings(s.u_at_z(z0), s.u_at_z(z1), steps, add=add, n=n, p=p)


PARTS = []


def part(name, bm, mats, skin, smooth=True, mat_index=None):
    """Tạo object cho một bộ phận. skin: ("rigid", bone) | ("smooth", [bones]) | ("hose", None)."""
    mats = [mats] if isinstance(mats, str) else mats
    if mat_index:
        for f in bm.faces: f.material_index = mat_index(f)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    for f in bm.faces: f.smooth = smooth
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for m in mats: me.materials.append(MATS[m])
    ob = bpy.data.objects.new(name, me)
    COLL.objects.link(ob)
    ob["skin_kind"] = skin[0]
    ob["skin_bones"] = skin[1] if isinstance(skin[1], str) else ",".join(skin[1] or [])
    PARTS.append((ob, skin))
    return ob


def parts_from_scene():
    out = []
    for ob in COLL.objects:
        if ob.type != 'MESH' or "skin_kind" not in ob: continue
        k, b = ob["skin_kind"], ob["skin_bones"]
        out.append((ob, (k, b if k == "rigid" else b.split(","))))
    return out


def box(bm, c, size, frame=None, bevel=0.0, segs=2):
    r = bmesh.ops.create_cube(bm, size=1.0)
    vs = r["verts"]
    fr = frame or Matrix.Identity(3)
    for v in vs:
        v.co = c + fr @ Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
    if bevel > 0:
        es = list({e for v in vs for e in v.link_edges})
        bmesh.ops.bevel(bm, geom=vs + es, offset=bevel, segments=segs, affect='EDGES', profile=0.5)
    return vs


def cylinder(bm, c, axis, r, depth, segs=24, r2=None):
    res = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segs, radius1=r, radius2=r if r2 is None else r2, depth=depth)
    q = Vector((0, 0, 1)).rotation_difference(axis.normalized())
    for v in res["verts"]:
        v.co = c + q @ v.co
    return res["verts"]


def ellipsoid(bm, c, radii, u=24, v=16):
    res = bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=1.0)
    for vv in res["verts"]:
        vv.co = c + Vector((vv.co.x * radii[0], vv.co.y * radii[1], vv.co.z * radii[2]))
    return res["verts"]


def ribbon(bm, path, normal_fn, half_w, half_t, sps=6):
    """Dây đai dẹt bám theo bề mặt: pháp tuyến bề mặt do normal_fn(p) cho."""
    P = catmull([Vector(p) for p in path], sps)
    rings = []
    for i, p in enumerate(P):
        T = (P[min(i + 1, len(P) - 1)] - P[max(i - 1, 0)]).normalized()
        nrm = normal_fn(p); nrm = (nrm - T * nrm.dot(T)).normalized()
        side = T.cross(nrm)
        rings.append([p + side * half_w + nrm * half_t, p - side * half_w + nrm * half_t,
                      p - side * half_w - nrm * half_t, p + side * half_w - nrm * half_t])
    loft(bm, rings, cap0=True, cap1=True)


# ====================================================================== dựng hình
TORSO = Limb(
    [(0, 0, 0.78), (0, 0, 0.86), (0, 0, 0.96), (0, 0, 1.06), (0, 0, 1.16), (0, 0, 1.26), (0, 0, 1.34),
     (0, 0, 1.40), (0, 0, 1.44), (0, 0.005, 1.47), (0, 0.008, 1.50), (0, 0.008, 1.555)],
    [(0.142, 0.206), (0.133, 0.197), (0.126, 0.188), (0.128, 0.190), (0.132, 0.198), (0.134, 0.205), (0.128, 0.200),
     (0.116, 0.182), (0.096, 0.132), (0.078, 0.088), (0.075, 0.080), (0.082, 0.088)], sps=8)


def sleeve_limb(s):
    return Limb([(0.10 * s, 0.0, 1.385), (0.17 * s, 0.0, 1.395), (0.228 * s, 0.005, 1.35), (0.255 * s, 0.015, 1.25),
                 (0.28 * s, 0.02, 1.13), (0.305 * s, 0.005, 1.01), (0.325 * s, -0.015, 0.91)],
                [(0.070, 0.066), (0.078, 0.074), (0.080, 0.078), (0.078, 0.076), (0.072, 0.070), (0.066, 0.064), (0.064, 0.062)])


def leg_limb(s):
    return Limb([(0.098 * s, 0.0, 0.97), (0.102 * s, -0.005, 0.78), (0.105 * s, -0.018, 0.52), (0.108 * s, -0.005, 0.32), (0.11 * s, 0.005, 0.13)],
                [(0.115, 0.100), (0.108, 0.098), (0.096, 0.090), (0.092, 0.088), (0.100, 0.095)])


ELBOW = lambda s: Vector((0.28 * s, 0.02, 1.13))
WRIST = lambda s: Vector((0.325 * s, -0.015, 0.91))
TORSO_B = ["Hips", "Spine", "Chest"]


def build_torso():
    bm = bmesh.new(); loft(bm, TORSO.rings(n=40, p=3.0, steps=60))
    part("Coat", bm, "navy", ("smooth", TORSO_B + ["Neck"]))
    # vạch phản quang: vàng rộng, bạc ở giữa (ngực + gấu áo)
    for z0, z1 in ((1.19, 1.255), (0.80, 0.865)):
        bm = bmesh.new(); loft(bm, TORSO.band(z0, z1, 0.004, n=40, p=3.0)); part("CoatBandY", bm, "yellow", ("smooth", TORSO_B))
        m = (z0 + z1) / 2
        bm = bmesh.new(); loft(bm, TORSO.band(m - 0.013, m + 0.013, 0.0065, n=40, p=3.0)); part("CoatBandS", bm, "silver", ("smooth", TORSO_B))
    # nẹp cài giữa áo + túi hông có nắp
    bm = bmesh.new(); box(bm, Vector((0, -0.132, 1.07)), (0.05, 0.012, 0.56), bevel=0.004)
    part("Placket", bm, "navy", ("smooth", TORSO_B), smooth=False)
    for s in (1, -1):
        bm = bmesh.new(); box(bm, Vector((0.12 * s, -0.132, 0.905)), (0.12, 0.02, 0.13), bevel=0.008)
        box(bm, Vector((0.12 * s, -0.145, 0.965)), (0.128, 0.014, 0.045), bevel=0.005)
        part("CoatPocket", bm, "navy", ("smooth", ["Hips", "Spine"]), smooth=False)
    # thân dưới (che khe giữa hai ống quần dưới vạt áo)
    pel = Limb([(0, 0, 0.84), (0, 0, 1.02)], [(0.12, 0.185), (0.12, 0.185)], sps=2)
    bm = bmesh.new(); loft(bm, pel.rings(n=32, p=3.0, steps=3), cap0=True); part("Pelvis", bm, "navy", ("smooth", ["Hips"]))


def build_arms():
    for s, side in ((1, "Left"), (-1, "Right")):
        arm = [side + "Shoulder", side + "UpperArm", side + "LowerArm", side + "Hand"]
        L = sleeve_limb(s)
        bm = bmesh.new(); loft(bm, L.rings(n=24, steps=48)); part(side + "Sleeve", bm, "navy", ("smooth", arm))
        for z0, z1 in ((1.19, 1.255), (0.955, 1.02)):
            bm = bmesh.new(); loft(bm, L.band(z0, z1, 0.004)); part("SleeveBandY", bm, "yellow", ("smooth", arm))
            m = (z0 + z1) / 2
            bm = bmesh.new(); loft(bm, L.band(m - 0.013, m + 0.013, 0.0065)); part("SleeveBandS", bm, "silver", ("smooth", arm))
        # găng tay: cổ găng + lòng bàn tay + 4 ngón + ngón cái
        w, e = WRIST(s), ELBOW(s)
        D = (w - e).normalized()
        F = Vector((0, -1, 0)); F = (F - D * F.dot(D)).normalized()   # phía trước
        Sd = D.cross(F) * s                                           # ra ngoài (phía ngón út → ngoài)
        cuff = Limb([w - D * 0.06, w + D * 0.045], [(0.060, 0.058), (0.056, 0.052)], sps=2)
        bm = bmesh.new(); loft(bm, cuff.rings(n=20, steps=3), cap0=True, cap1=True)
        part(side + "GloveCuff", bm, "leather", ("smooth", [side + "LowerArm", side + "Hand"]))
        bm = bmesh.new()
        pc = w + D * 0.10
        fr = Matrix((Sd, F, D)).transposed()
        for vv in ellipsoid(bm, Vector((0, 0, 0)), (0.034, 0.056, 0.068), 16, 12):
            vv.co = pc + fr @ vv.co
        for i, off in enumerate((-0.036, -0.012, 0.012, 0.036)):
            base = pc + D * 0.04 + F * off
            ln = 0.075 - abs(off) * 0.45
            fl = Limb([base, base + D * ln * 0.6 - Sd * 0.006, base + D * ln - Sd * 0.024], [(0.0155, 0.0155)] * 3, sps=3)
            loft(bm, fl.rings(n=10, steps=6), cap0=True, cap1=True)
        tb = pc - D * 0.012 + F * 0.048 - Sd * 0.012
        th = Limb([tb, tb + F * 0.03 + D * 0.025, tb + F * 0.04 + D * 0.06 - Sd * 0.014], [(0.017, 0.017), (0.016, 0.016), (0.015, 0.015)], sps=3)
        loft(bm, th.rings(n=10, steps=6), cap0=True, cap1=True)
        part(side + "Glove", bm, "leather", ("rigid", side + "Hand"))


def build_legs():
    for s, side in ((1, "Left"), (-1, "Right")):
        leg = ["Hips", side + "UpperLeg", side + "LowerLeg"]
        L = leg_limb(s)
        bm = bmesh.new(); loft(bm, L.rings(n=28, steps=40)); part(side + "Pants", bm, "navy", ("smooth", leg))
        bm = bmesh.new(); loft(bm, L.band(0.265, 0.20, 0.004, n=28)); part("PantBandY", bm, "yellow", ("smooth", leg))
        bm = bmesh.new(); loft(bm, L.band(0.245, 0.22, 0.0065, n=28)); part("PantBandS", bm, "silver", ("smooth", leg))
        # đệm gối (nửa trước)
        bm = bmesh.new()
        loft(bm, L.rings(L.u_at_z(0.63), L.u_at_z(0.40), 8, add=0.005, n=12, arc=(-1.25, 1.25)), closed=False)
        part(side + "KneePad", bm, "navy_worn", ("smooth", leg[1:]))
        # túi hộp mặt ngoài đùi
        bm = bmesh.new()
        box(bm, Vector((0.205 * s, -0.012, 0.625)), (0.03, 0.14, 0.19), bevel=0.01)
        box(bm, Vector((0.214 * s, -0.012, 0.705)), (0.03, 0.148, 0.05), bevel=0.008)
        part(side + "CargoPocket", bm, "navy", ("rigid", side + "UpperLeg"), smooth=False)
        # ủng: cổ ủng + thân bàn chân + đế
        foot_b = [side + "LowerLeg", side + "Foot", side + "Toes"]
        x = 0.11 * s
        shaft = Limb([(x, 0.012, 0.27), (x, 0.01, 0.08)], [(0.074, 0.068), (0.074, 0.066)], sps=2)
        bm = bmesh.new(); loft(bm, shaft.rings(n=24, steps=4))
        secs = [(0.085, 0.046, 0.11), (0.065, 0.060, 0.14), (0.020, 0.064, 0.15), (-0.030, 0.064, 0.13),
                (-0.090, 0.063, 0.095), (-0.150, 0.060, 0.08), (-0.195, 0.050, 0.07), (-0.215, 0.034, 0.058)]
        rings = [ring_pts(Vector((x, y, (0.025 + h) / 2)), X, Z, w, (h - 0.025) / 2, 24, 2.6) for y, w, h in secs]
        loft(bm, rings, cap0=True, cap1=True)
        part(side + "Boot", bm, "leather", ("smooth", foot_b))
        bm = bmesh.new()
        rings = [ring_pts(Vector((x, y + (0.004 if i == 0 else -0.004 if i == len(secs) - 1 else 0), 0.016)), X, Z, w + 0.007, 0.016, 24, 3.0)
                 for i, (y, w, h) in enumerate(secs)]
        loft(bm, rings, cap0=True, cap1=True)
        part(side + "Sole", bm, "rubber", ("smooth", foot_b[1:]))


HEAD_C = Vector((0, -0.005, 1.625))


def build_head():
    bm = bmesh.new(); ellipsoid(bm, HEAD_C, (0.078, 0.094, 0.108), 24, 16)
    part("Head", bm, "skin", ("rigid", "Head"))
    bm = bmesh.new()
    cylinder(bm, Vector((0, -0.100, 1.612)), Vector((0, -1, -0.25)), 0.014, 0.022, 10, r2=0.004)
    part("Nose", bm, "skin", ("rigid", "Head"))
    bm = bmesh.new()
    for s in (1, -1): ellipsoid(bm, Vector((0.03 * s, -0.087, 1.645)), (0.009, 0.006, 0.006), 10, 6)
    part("Eyes", bm, "eye", ("rigid", "Head"))
    # mũ trùm chống cháy (hở mặt) + cổ
    bm = bmesh.new(); ellipsoid(bm, Vector((0, 0.0, 1.63)), (0.088, 0.103, 0.118), 28, 18)
    kill = [f for f in bm.faces if (lambda c: c.y < -0.035 and abs(c.x) < 0.058 and 1.555 < c.z < 1.705)(f.calc_center_median())]
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    neck = Limb([(0, 0.005, 1.585), (0, 0.005, 1.45)], [(0.066, 0.07), (0.07, 0.074)], sps=2)
    loft(bm, neck.rings(n=24, steps=4))
    part("Hood", bm, "black", ("smooth", ["Chest", "Neck", "Head"]))


HELM_C = Vector((0, 0.012, 1.672))
HELM_R = (0.123, 0.143, 0.128)


def build_helmet():
    cx, cy, cz = HELM_C
    rx, ry, rz = HELM_R
    n = 36
    bm = bmesh.new()
    rings = [ring_pts(Vector((cx, cy, cz + rz * math.sin(math.radians(e)))), X, Y,
                      rx * math.cos(math.radians(e)), ry * math.cos(math.radians(e)), n) for e in range(0, 86, 5)]
    loft(bm, rings, cap1=True)
    part("HelmetDome", bm, "red", ("rigid", "Head"))
    # vành mũ: dài ở sau, ngắn ở trước, rủ xuống phía sau
    A, B, C, D = [], [], [], []
    for i in range(n):
        a = 2 * math.pi * i / n
        ca, sa = math.cos(a), math.sin(a)
        ext = 0.055 + 0.045 * sa
        drop = 0.012 + 0.032 * max(0.0, sa) ** 1.5
        A.append(Vector((cx + ca * rx * 0.985, cy + sa * ry * 0.985, cz + 0.004)))
        B.append(Vector((cx + ca * (rx + ext), cy + sa * (ry + ext), cz - drop)))
        C.append(B[-1] - Vector((0, 0, 0.011)))
        D.append(A[-1] - Vector((0, 0, 0.014)))
    bm = bmesh.new(); loft(bm, [A, B, C, D], wrap=True)
    part("HelmetBrim", bm, "red", ("rigid", "Head"))
    # vạch vàng quanh chỏm
    bm = bmesh.new()
    rings = [ring_pts(Vector((cx, cy, cz + rz * math.sin(math.radians(e)))), X, Y,
                      rx * math.cos(math.radians(e)) + 0.003, ry * math.cos(math.radians(e)) + 0.003, n) for e in (14, 19, 24)]
    loft(bm, rings); part("HelmetBand", bm, "yellow", ("rigid", "Head"))
    # sống mũ
    path = [(cx, cy - ry * math.cos(math.radians(p)), cz + rz * math.sin(math.radians(p))) for p in range(28, 170, 14)]
    comb = Limb(path, [(0.013, 0.013)] * len(path), sps=4)
    bm = bmesh.new(); loft(bm, comb.rings(n=12), cap0=True, cap1=True); part("HelmetComb", bm, "red", ("rigid", "Head"))
    # huy hiệu phía trước
    e = math.radians(24)
    nrm = Vector((0, -math.cos(e) / ry, math.sin(e) / rz)).normalized()
    pos = Vector((cx, cy - ry * math.cos(e), cz + rz * math.sin(e)))
    bm = bmesh.new(); cylinder(bm, pos + nrm * 0.004, nrm, 0.033, 0.008, 24); part("Badge", bm, "gold", ("rigid", "Head"), smooth=False)
    bm = bmesh.new(); cylinder(bm, pos + nrm * 0.008, nrm, 0.022, 0.006, 24); part("BadgeCenter", bm, "white", ("rigid", "Head"), smooth=False)
    bm = bmesh.new(); cylinder(bm, pos + nrm * 0.0115, nrm, 0.009, 0.004, 16); part("BadgeDot", bm, "red", ("rigid", "Head"), smooth=False)
    # kẹp kính che + khăn che gáy
    bm = bmesh.new()
    for s in (1, -1): box(bm, Vector((0.121 * s, -0.035, cz - 0.005)), (0.018, 0.045, 0.03), bevel=0.004)
    part("VisorClips", bm, "rubber", ("rigid", "Head"), smooth=False)
    bm = bmesh.new()
    rings = [ring_pts(Vector((0, 0.012, z)), X, Y, rxx, ryy, 20, 2.0, arc=(math.radians(-10), math.radians(190)))
             for z, rxx, ryy in ((1.655, 0.108, 0.125), (1.60, 0.110, 0.126), (1.535, 0.114, 0.128))]
    loft(bm, rings, closed=False)
    part("NeckCurtain", bm, "black", ("rigid", "Head"))


MASK_C = Vector((0, -0.015, 1.60))


def build_mask():
    bm = bmesh.new(); ellipsoid(bm, MASK_C, (0.086, 0.100, 0.118), 32, 20)
    kill = [v for v in bm.verts if v.co.y > -0.028 or v.co.z > 1.705 or v.co.z < 1.49]
    bmesh.ops.delete(bm, geom=kill, context='VERTS')
    # kính trong: khoét ô kính để nhìn thấy mặt bên trong (một vật liệu đục duy nhất không làm được kính trong)
    lens = [f for f in bm.faces if (lambda c: c.z > 1.575 and c.z < 1.69 and abs(c.x) < 0.066 and c.y < -0.05)(f.calc_center_median())]
    bmesh.ops.delete(bm, geom=lens, context='FACES')
    part("MaskFace", bm, "rubber", ("rigid", "Mask"))
    bm = bmesh.new()
    cylinder(bm, Vector((0, -0.108, 1.527)), Vector((0, -1, 0)), 0.030, 0.045, 24)
    part("Regulator", bm, "rubber", ("rigid", "Mask"))
    bm = bmesh.new(); cylinder(bm, Vector((0, -0.133, 1.527)), Vector((0, -1, 0)), 0.021, 0.008, 24)
    part("RegulatorCap", bm, "red", ("rigid", "Mask"))
    # dây mặt nạ vòng qua đầu
    bm = bmesh.new()
    for z in (1.66, 1.56):
        rings = [ring_pts(Vector((0, 0.0, z + dz)), X, Y, 0.091 + (0.004 if z < 1.6 else 0), 0.106, 28, 2.0,
                          arc=(math.radians(-25), math.radians(205))) for dz in (-0.009, 0.009)]
        loft(bm, rings, closed=False)
    part("MaskStraps", bm, "rubber", ("rigid", "Mask"))


def build_harness():
    def nfn(p): return Vector((0, p.y, max(p.z - 1.30, 0.0) * 1.5))
    bm = bmesh.new()
    for s in (1, -1):
        path = [(0.09 * s, 0.150, 1.40), (0.095 * s, 0.12, 1.448), (0.10 * s, 0.06, 1.462), (0.10 * s, 0.0, 1.468),
                (0.10 * s, -0.06, 1.455), (0.105 * s, -0.112, 1.425), (0.112 * s, -0.143, 1.36), (0.116 * s, -0.149, 1.26),
                (0.12 * s, -0.147, 1.14), (0.125 * s, -0.143, 1.04)]
        ribbon(bm, path, nfn, 0.026, 0.0045)
    part("ShoulderStraps", bm, "rubber", ("smooth", TORSO_B), smooth=False)
    # thắt lưng + khoá + túi
    bm = bmesh.new(); loft(bm, TORSO.band(0.975, 1.030, 0.013, n=40, p=3.0)); part("Belt", bm, "rubber", ("smooth", ["Hips", "Spine"]))
    bm = bmesh.new(); box(bm, Vector((0, -0.145, 1.002)), (0.075, 0.018, 0.058), bevel=0.006); part("Buckle", bm, "metal", ("smooth", ["Hips", "Spine"]), smooth=False)
    bm = bmesh.new(); box(bm, Vector((0, -0.155, 1.002)), (0.05, 0.006, 0.034), bevel=0.003); part("BuckleCenter", bm, "rubber", ("smooth", ["Hips", "Spine"]), smooth=False)
    bm = bmesh.new()
    for s in (1, -1):
        box(bm, Vector((0.13 * s, -0.135, 0.965)), (0.05, 0.036, 0.075), bevel=0.008)
        box(bm, Vector((0.19 * s, -0.035, 0.965)), (0.036, 0.055, 0.08), bevel=0.008)
    part("BeltPouches", bm, "black", ("smooth", ["Hips", "Spine"]), smooth=False)
    # đồng hồ áp suất (ngực trái) + bộ đàm (ngực phải)
    g = Vector((0.125, -0.172, 1.20))
    bm = bmesh.new(); cylinder(bm, g, Vector((0, -1, 0)), 0.032, 0.022, 28); part("Gauge", bm, "metal", ("rigid", "Chest"), smooth=False)
    bm = bmesh.new(); cylinder(bm, g + Vector((0, -0.0115, 0)), Vector((0, -1, 0)), 0.026, 0.002, 28); part("GaugeFace", bm, "white", ("rigid", "Chest"), smooth=False)
    bm = bmesh.new(); box(bm, g + Vector((0.008, -0.0135, 0.006)), (0.02, 0.002, 0.003), Matrix.Rotation(math.radians(35), 3, 'Y')); part("GaugeNeedle", bm, "rubber", ("rigid", "Chest"), smooth=False)
    hose = Limb([g + Vector((0, 0.005, -0.03)), (0.13, -0.165, 1.12), (0.14, -0.15, 1.06)], [(0.009, 0.009)] * 3, sps=5)
    bm = bmesh.new(); loft(bm, hose.rings(n=10), cap0=True, cap1=True); part("GaugeHose", bm, "rubber", ("smooth", TORSO_B))
    bm = bmesh.new(); box(bm, Vector((-0.125, -0.166, 1.165)), (0.046, 0.03, 0.088), bevel=0.006)
    cylinder(bm, Vector((-0.138, -0.166, 1.245)), Z, 0.0045, 0.075, 8)
    part("Radio", bm, "rubber", ("rigid", "Chest"), smooth=False)
    # ống thở: từ van thở xuống ngực phải (gợn sóng) — trọng số chuyển dần Mask → Chest
    pts = [(-0.012, -0.118, 1.50), (-0.04, -0.158, 1.44), (-0.066, -0.168, 1.36), (-0.085, -0.162, 1.29), (-0.10, -0.157, 1.235)]
    tube = Limb(pts, [(0.013, 0.013)] * len(pts), sps=16)
    rings = []
    for k in range(tube.N + 1):
        c, T, ax, ay, r = tube.at(k)
        rr = 0.0125 + 0.0022 * math.sin(k * 1.9)
        rings.append(ring_pts(c, ax, ay, rr, rr, 12))
    bm = bmesh.new(); loft(bm, rings, cap0=True, cap1=True); part("BreathingHose", bm, "rubber", ("hose", None))


def build_scba():
    tc = Vector((0, 0.232, 0))
    prof = []
    for e in range(-80, 1, 10): prof.append((0.955 + 0.075 * math.sin(math.radians(e)), 0.075 * math.cos(math.radians(e))))
    for e in range(0, 81, 10): prof.append((1.365 + 0.075 * math.sin(math.radians(e)), 0.075 * math.cos(math.radians(e))))
    bm = bmesh.new(); loft(bm, [ring_pts(tc + Vector((0, 0, z)), X, Y, r, r, 28) for z, r in prof], cap0=True, cap1=True)
    part("Tank", bm, "tank", ("rigid", "Chest"))
    for (z0, z1), mat, add in (((1.285, 1.325), "yellow", 0.003), ((1.02, 1.05), "rubber", 0.005), ((1.20, 1.23), "rubber", 0.005)):
        bm = bmesh.new(); loft(bm, [ring_pts(tc + Vector((0, 0, z)), X, Y, 0.075 + add, 0.075 + add, 28) for z in (z0, z1)])
        part("TankBand", bm, mat, ("rigid", "Chest"))
    bm = bmesh.new(); cylinder(bm, tc + Vector((0, 0, 0.865)), Z, 0.024, 0.05, 20); part("TankValve", bm, "metal", ("rigid", "Chest"))
    bm = bmesh.new(); cylinder(bm, tc + Vector((0, 0, 0.838)), Z, 0.036, 0.016, 20); part("ValveWheel", bm, "rubber", ("rigid", "Chest"), smooth=False)
    bm = bmesh.new(); box(bm, Vector((0, 0.146, 1.14)), (0.25, 0.022, 0.58), bevel=0.012)
    for s in (1, -1):
        rail = Limb([(0.118 * s, 0.162, 0.86), (0.118 * s, 0.162, 1.42)], [(0.01, 0.01)] * 2, sps=2)
        loft(bm, rail.rings(n=10, steps=2), cap0=True, cap1=True)
    part("Backplate", bm, "rubber", ("rigid", "Chest"), smooth=False)


def build_model():
    global COLL
    for ob in list(bpy.data.objects): bpy.data.objects.remove(ob, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.armatures, bpy.data.actions, bpy.data.cameras, bpy.data.lights, bpy.data.images):
        for d in list(coll):
            try: coll.remove(d)
            except Exception: pass
    for c in list(bpy.data.collections):
        if c.name == "Firefighter": bpy.data.collections.remove(c)
    COLL = bpy.data.collections.new("Firefighter")
    bpy.context.scene.collection.children.link(COLL)
    PARTS.clear(); MATS.clear()
    make_materials()
    build_torso(); build_arms(); build_legs(); build_head(); build_helmet(); build_mask(); build_harness(); build_scba()
    tris = sum(sum(len(p.vertices) - 2 for p in ob.data.polygons) for ob, _ in PARTS)
    log("MODEL parts", len(PARTS), "tris", tris)


# ====================================================================== xương + trọng số + animation
def joints():
    J = {"Hips": Vector((0, 0, 0.97)), "Spine": Vector((0, 0, 1.10)), "Chest": Vector((0, 0, 1.25)),
         "Neck": Vector((0, 0, 1.45)), "Head": Vector((0, 0, 1.54)), "HeadTop": Vector((0, 0, 1.80))}
    for k, s in (("L", 1), ("R", -1)):
        J[k + "Shoulder"] = Vector((0.05 * s, 0, 1.40))
        J[k + "UpperArm"] = Vector((0.19 * s, 0.0, 1.395))
        J[k + "LowerArm"] = ELBOW(s)
        J[k + "Hand"] = WRIST(s)
        J[k + "HandTip"] = WRIST(s) + (WRIST(s) - ELBOW(s)).normalized() * 0.16
        J[k + "UpperLeg"] = Vector((0.10 * s, 0, 0.93))
        J[k + "LowerLeg"] = Vector((0.105 * s, -0.018, 0.52))
        J[k + "Foot"] = Vector((0.11 * s, 0.01, 0.10))
        J[k + "Toes"] = Vector((0.11 * s, -0.10, 0.035))
        J[k + "ToeTip"] = Vector((0.11 * s, -0.19, 0.035))
    return J


def seg_dist(p, a, b):
    ab = b - a; t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
    return (p - (a + ab * t)).length


def build_rig():
    scene = bpy.context.scene
    J = joints()
    arm_data = bpy.data.armatures.new("FirefighterRig")
    rig = bpy.data.objects.new("FirefighterRig", arm_data)
    COLL.objects.link(rig)
    for o in bpy.context.selected_objects: o.select_set(False)
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
    bone("Mask", Vector((0, -0.12, 1.56)), Vector((0, -0.24, 1.56)), "Head")
    for k, full in (("L", "Left"), ("R", "Right")):
        bone(full + "Shoulder", J[k + "Shoulder"], J[k + "UpperArm"], "Chest")
        bone(full + "UpperArm", J[k + "UpperArm"], J[k + "LowerArm"], full + "Shoulder", True)
        bone(full + "LowerArm", J[k + "LowerArm"], J[k + "Hand"], full + "UpperArm", True)
        bone(full + "Hand", J[k + "Hand"], J[k + "HandTip"], full + "LowerArm", True)
        bone(full + "UpperLeg", J[k + "UpperLeg"], J[k + "LowerLeg"], "Hips")
        bone(full + "LowerLeg", J[k + "LowerLeg"], J[k + "Foot"], full + "UpperLeg", True)
        bone(full + "Foot", J[k + "Foot"], J[k + "Toes"], full + "LowerLeg", True)
        bone(full + "Toes", J[k + "Toes"], J[k + "ToeTip"], full + "Foot", True)
    for b in eb:
        b.align_roll(Vector((0, -1, 0)) if abs(b.vector.normalized().y) < 0.9 else Vector((0, 0, 1)))
    bpy.ops.object.mode_set(mode='OBJECT')
    segs = {b.name: (b.head_local.copy(), b.tail_local.copy()) for b in arm_data.bones}

    # ---- trọng số: mỗi bộ phận chỉ được gắn vào các xương cho phép → tay không kéo theo sườn áo
    parts = parts_from_scene()
    for ob, (kind, bones) in parts:
        ob.vertex_groups.clear()
        vg = {}
        def grp(n):
            if n not in vg: vg[n] = ob.vertex_groups.new(name=n)
            return vg[n]
        for v in ob.data.vertices:
            p = v.co
            if kind == "rigid":
                grp(bones).add([v.index], 1.0, 'REPLACE')
            elif kind == "hose":
                t = max(0.0, min(1.0, (1.49 - p.z) / (1.49 - 1.25)))
                t = t * t * (3 - 2 * t)
                if t < 0.999: grp("Mask").add([v.index], 1 - t, 'REPLACE')
                if t > 0.001: grp("Chest").add([v.index], t, 'REPLACE')
            else:
                cand = sorted((max(seg_dist(p, *segs[n]), 0.004), n) for n in bones)[:3]
                ws = [(1.0 / d ** 4, n) for d, n in cand]
                tot = sum(w for w, _ in ws)
                for w, n in ws:
                    if w / tot > 0.02: grp(n).add([v.index], w / tot, 'REPLACE')

    # ---- gộp thành một lưới
    for o in bpy.context.selected_objects: o.select_set(False)
    objs = [ob for ob, _ in parts]
    for o in objs: o.select_set(True)
    body = objs[0]
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()
    body.name = "FirefighterBody"; body.data.name = "FirefighterBody"
    body.parent = rig
    mod = body.modifiers.new("Armature", 'ARMATURE'); mod.object = rig
    make_animations(rig, arm_data)
    log("RIG bones", len(arm_data.bones), "verts", len(body.data.vertices))
    return rig, body


def make_animations(rig, arm_data):
    """9 animation — giữ nguyên tên/ý nghĩa như bản cũ để Animator trong Unity không phải đổi."""
    rig.rotation_mode = 'QUATERNION'
    pbs = rig.pose.bones
    for pb in pbs: pb.rotation_mode = 'QUATERNION'
    AX, AY, AZ = (1, 0, 0), (0, 1, 0), (0, 0, 1)

    def pose(name, axis, deg):
        r = arm_data.bones[name].matrix_local.to_quaternion()
        return r.inverted() @ Quaternion(Vector(axis).normalized(), math.radians(deg)) @ r

    def compose(name, rots):
        q = Quaternion()
        for axis, deg in rots: q = pose(name, axis, deg) @ q
        return q

    def make(name, keys):
        act = bpy.data.actions.new(name)
        rig.animation_data_create(); rig.animation_data.action = act
        for f, rots, locs in keys:
            for pb in pbs:
                pb.rotation_quaternion = compose(pb.name, rots.get(pb.name, []))
                pb.location = Vector(locs.get(pb.name, (0, 0, 0)))
                pb.keyframe_insert("rotation_quaternion", frame=f)
                pb.keyframe_insert("location", frame=f)
        rig.animation_data.action = None
        act.use_fake_user = True

    def relaxed(extra=None):
        r = {"LeftUpperArm": [(AY, 8)], "RightUpperArm": [(AY, -8)], "LeftLowerArm": [(AX, -12)], "RightLowerArm": [(AX, -12)]}
        for k, v in (extra or {}).items(): r[k] = r.get(k, []) + v
        return r

    def merge(a, b):
        r = {k: list(v) for k, v in a.items()}
        for k, v in b.items(): r[k] = r.get(k, []) + v
        return r

    make("Idle", [
        (1, relaxed({"Chest": [(AX, 0)], "Head": [(AZ, 0)]}), {}),
        (60, relaxed({"Chest": [(AX, -2.5)], "Head": [(AZ, 6), (AX, 2)], "Spine": [(AY, 1.5)]}), {"Hips": (0, 0.004, 0)}),
        (120, relaxed({"Chest": [(AX, 0)], "Head": [(AZ, 0)]}), {}),
    ])
    make("Talk", [
        (1, relaxed({"RightUpperArm": [(AX, -25), (AY, 10)], "RightLowerArm": [(AX, -55)]}), {}),
        (15, relaxed({"RightUpperArm": [(AX, -32), (AY, 14)], "RightLowerArm": [(AX, -70), (AZ, 15)], "Head": [(AX, -6)]}), {}),
        (30, relaxed({"RightUpperArm": [(AX, -22), (AY, 6)], "RightLowerArm": [(AX, -50), (AZ, -10)], "Head": [(AX, 3), (AZ, 8)]}), {}),
        (45, relaxed({"RightUpperArm": [(AX, -30), (AY, 12)], "RightLowerArm": [(AX, -65)], "Head": [(AX, -4), (AZ, -6)]}), {}),
        (60, relaxed({"RightUpperArm": [(AX, -25), (AY, 10)], "RightLowerArm": [(AX, -55)]}), {}),
    ])
    point_pose = relaxed({"RightShoulder": [(AX, -12)], "RightUpperArm": [(AX, -42), (AZ, -22)], "RightLowerArm": [(AX, -18)],
                          "RightHand": [(AX, -10)], "Chest": [(AZ, -12)], "Head": [(AZ, -16)]})
    make("Point", [(1, relaxed(), {}), (12, point_pose, {}), (45, point_pose, {}), (60, relaxed(), {})])
    bk_up = relaxed({"RightShoulder": [(AX, -12)], "RightUpperArm": [(AX, -35), (AZ, -12)], "RightLowerArm": [(AX, -118)], "Chest": [(AZ, -12)], "Head": [(AZ, -15)]})
    bk_out = relaxed({"RightShoulder": [(AX, -12)], "RightUpperArm": [(AX, -32), (AZ, -18)], "RightLowerArm": [(AX, -78)], "Chest": [(AZ, -12)], "Head": [(AZ, -15)]})
    make("Beckon", [(1, bk_out, {}), (10, bk_up, {}), (20, bk_out, {}), (30, bk_up, {}), (40, bk_out, {})])

    def walk_keys(amp, knee, arm, bob, crouch=None):
        base = crouch or {}
        ks = []
        for i in range(9):
            ph = i / 8 * 2 * math.pi
            s, c = math.sin(ph), math.cos(ph)
            r = relaxed({"LeftUpperLeg": [(AX, -amp * s)], "RightUpperLeg": [(AX, amp * s)],
                         "LeftLowerLeg": [(AX, knee * max(0.0, c))], "RightLowerLeg": [(AX, knee * max(0.0, -c))],
                         "LeftUpperArm": [(AX, arm * s)], "RightUpperArm": [(AX, -arm * s)],
                         "Spine": [(AZ, 3 * s)], "Chest": [(AZ, -4 * s)]})
            for k, v in base.get("rots", {}).items(): r[k] = r.get(k, []) + v
            ks.append((1 + i * 4, r, {"Hips": (0, base.get("drop", 0) + bob * abs(c), 0)}))
        return ks
    make("Walk", walk_keys(28, 45, 18, 0.025))
    crouch = {"drop": -0.32, "rots": {
        "Hips": [(AX, 10)], "Spine": [(AX, 22)], "Chest": [(AX, 12)], "Head": [(AX, -28)],
        "LeftUpperLeg": [(AX, -55)], "RightUpperLeg": [(AX, -55)], "LeftLowerLeg": [(AX, 85)], "RightLowerLeg": [(AX, 85)],
        "LeftFoot": [(AX, -30)], "RightFoot": [(AX, -30)], "LeftUpperArm": [(AX, -60), (AY, -15)], "LeftLowerArm": [(AX, -80)]}}
    make("CrouchWalk", walk_keys(16, 25, 8, 0.015, crouch))
    hand_up = relaxed({"RightShoulder": [(AX, -12)], "RightUpperArm": [(AX, -42), (AZ, 18)], "RightLowerArm": [(AX, -138)], "Head": [(AX, -5)]})
    mask_down_loc = (0, 0.06, -0.28)
    mask_down = {"Mask": [(AX, 70)]}
    make("MaskOff", [
        (1, relaxed(), {}), (15, hand_up, {}), (22, hand_up, {"Mask": (0, 0.03, -0.02)}),
        (38, merge(relaxed({"RightUpperArm": [(AX, -30)], "RightLowerArm": [(AX, -60)]}), mask_down), {"Mask": mask_down_loc}),
        (50, merge(relaxed(), mask_down), {"Mask": mask_down_loc})])
    make("MaskOn", [
        (1, merge(relaxed(), mask_down), {"Mask": mask_down_loc}),
        (13, merge(relaxed({"RightUpperArm": [(AX, -30)], "RightLowerArm": [(AX, -60)]}), mask_down), {"Mask": mask_down_loc}),
        (30, hand_up, {"Mask": (0, 0.03, -0.02)}), (38, hand_up, {}), (50, relaxed(), {})])
    make("MaskOffIdle", [(1, merge(relaxed(), mask_down), {"Mask": mask_down_loc}),
                         (60, merge(relaxed({"Chest": [(AX, -2)]}), mask_down), {"Mask": mask_down_loc})])


# ====================================================================== UV + bake + xuất
def view3d_override():
    for win in bpy.context.window_manager.windows:
        for area in win.screen.areas:
            if area.type == 'VIEW_3D':
                reg = next(r for r in area.regions if r.type == 'WINDOW')
                return dict(window=win, area=area, region=reg, screen=win.screen)
    return {}


def bake_and_export(rig, body):
    import numpy as np
    scene = bpy.context.scene
    os.makedirs(OUT_DIR, exist_ok=True)
    for o in bpy.context.selected_objects: o.select_set(False)
    body.select_set(True); bpy.context.view_layer.objects.active = body
    with bpy.context.temp_override(**view3d_override()):
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.003)
        bpy.ops.object.mode_set(mode='OBJECT')

    prev_engine = scene.render.engine
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = 1
    scene.render.bake.margin = 6
    scene.render.bake.use_selected_to_active = False

    def new_img(name, noncolor, alpha=False):
        if name in bpy.data.images: bpy.data.images.remove(bpy.data.images[name])
        im = bpy.data.images.new(name, TEX, TEX, alpha=alpha)
        if noncolor: im.colorspace_settings.name = 'Non-Color'
        return im

    def target(im):
        for m in body.data.materials:
            nt = m.node_tree
            n = nt.nodes.get("BAKE_TARGET") or nt.nodes.new("ShaderNodeTexImage")
            n.name = "BAKE_TARGET"; n.image = im
            for x in nt.nodes: x.select = False
            n.select = True; nt.nodes.active = n

    def bake(im, **kw):
        target(im)
        bpy.ops.object.bake(use_clear=True, **kw)
        return np.array(im.pixels[:]).reshape(TEX, TEX, 4)

    base = bake(new_img("FF_Base", False), type='DIFFUSE', pass_filter={'COLOR'})
    rough = bake(new_img("FF_Rough", True), type='ROUGHNESS')
    nrm_img = new_img("FF_Normal", True)
    bake(nrm_img, type='NORMAL', normal_space='TANGENT')
    scene.cycles.samples = 48
    ao = bake(new_img("FF_AO", True), type='AO')
    scene.cycles.samples = 1
    # metallic: tạm nối Emission = giá trị metallic của từng vật liệu
    saved = []
    for m in body.data.materials:
        nt = m.node_tree
        out = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL')
        bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
        em = nt.nodes.new("ShaderNodeEmission")
        mv = bsdf.inputs["Metallic"].default_value
        em.inputs["Color"].default_value = (mv, mv, mv, 1)
        nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
        saved.append((nt, bsdf, out, em))
    metal = bake(new_img("FF_Metal", True), type='EMIT')
    for nt, bsdf, out, em in saved:
        nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"]); nt.nodes.remove(em)

    occl = 0.35 + 0.65 * ao[..., :1]
    out_base = base.copy(); out_base[..., :3] *= occl; out_base[..., 3] = 1
    img = new_img("Firefighter_BaseColor", False); img.pixels = out_base.ravel()
    img.filepath_raw = OUT_DIR + "/Firefighter_BaseColor.png"; img.file_format = 'PNG'; img.save()
    ms = np.zeros_like(base); ms[..., 0] = ms[..., 1] = ms[..., 2] = metal[..., 0]; ms[..., 3] = 1.0 - rough[..., 0]
    img = new_img("Firefighter_MetallicSmoothness", True, alpha=True); img.pixels = ms.ravel()
    img.filepath_raw = OUT_DIR + "/Firefighter_MetallicSmoothness.png"; img.file_format = 'PNG'; img.save()
    nrm_img.filepath_raw = OUT_DIR + "/Firefighter_Normal.png"; nrm_img.file_format = 'PNG'; nrm_img.save()
    log("BAKED textures", TEX)

    # một vật liệu duy nhất dùng texture vừa bake (Unity: Firefighter_Mat)
    final = bpy.data.materials.new("Firefighter")
    try: final.use_nodes = True
    except Exception: pass
    nt = final.node_tree
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = bpy.data.images["Firefighter_BaseColor"]
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    body.data.materials.clear(); body.data.materials.append(final)
    scene.render.engine = prev_engine

    # xuất FBX
    for o in bpy.context.selected_objects: o.select_set(False)
    rig.select_set(True); body.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(
        filepath=OUT_DIR + "/Firefighter.fbx", use_selection=True,
        object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False, armature_nodetype='NULL',
        apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
        bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False, bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, mesh_smooth_type='FACE', path_mode='STRIP')
    log("EXPORTED", OUT_DIR + "/Firefighter.fbx")
    bpy.ops.wm.save_as_mainfile(filepath=BLEND, copy=True)
    log("SAVED", BLEND)


# ====================================================================== xem trước (trước – nghiêng – sau, như ảnh tham chiếu)
def render_views(prefix, textured=False, action=None, frame=1):
    scene = bpy.context.scene
    os.makedirs(PREVIEW, exist_ok=True)
    prev = scene.render.engine
    scene.render.engine = "BLENDER_WORKBENCH"
    sh = scene.display.shading
    sh.light = "STUDIO"; sh.color_type = "TEXTURE" if textured else "MATERIAL"; sh.show_cavity = True
    sh.background_type = "VIEWPORT"; sh.background_color = (0.92, 0.92, 0.92)
    scene.render.resolution_x = 520; scene.render.resolution_y = 900
    cam = bpy.data.objects.get("PreviewCam")
    if cam is None:
        cd = bpy.data.cameras.new("PreviewCam"); cd.type = 'ORTHO'; cd.ortho_scale = 1.95
        cam = bpy.data.objects.new("PreviewCam", cd); scene.collection.objects.link(cam)
    scene.camera = cam
    rig = bpy.data.objects.get("FirefighterRig")
    if rig and rig.animation_data:
        rig.animation_data.action = bpy.data.actions.get(action) if action else None
        if not action:
            for pb in rig.pose.bones: pb.rotation_quaternion = Quaternion(); pb.location = Vector()
        scene.frame_set(frame)
    c = Vector((0, 0, 0.9))
    files = []
    for tag, d in (("front", Vector((0, -1, 0))), ("side", Vector((1, 0, 0))), ("back", Vector((0, 1, 0)))):
        cam.location = c + d * 5
        cam.rotation_euler = (c - cam.location).to_track_quat("-Z", "Y").to_euler()
        f = os.path.join(PREVIEW, f"{prefix}_{tag}.png")
        scene.render.filepath = f
        bpy.ops.render.render(write_still=True)
        files.append(f)
    scene.render.engine = prev
    log("PREVIEW", *files)
    return files


# ====================================================================== chạy
if STAGE in ("model", "all"):
    build_model()
    render_views("M")
if STAGE in ("rig", "bake", "all"):
    COLL = bpy.data.collections["Firefighter"]
    if STAGE in ("rig", "all") or bpy.data.objects.get("FirefighterRig") is None:
        rig, body = build_rig()
    rig = bpy.data.objects["FirefighterRig"]; body = bpy.data.objects["FirefighterBody"]
    for a, f in (("Point", 30), ("Walk", 5), ("MaskOffIdle", 1)):
        render_views("R_" + a, action=a, frame=f)
if STAGE in ("bake", "all"):
    bake_and_export(rig, body)
    render_views("T", textured=True)
