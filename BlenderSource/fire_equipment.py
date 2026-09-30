"""
FireEscape MR — bộ dụng cụ PCCC dựng bằng code.
Chạy:  blender -b -P fire_equipment.py -- <thư_mục_xuất_fbx> <ảnh_preview.png>

Quy ước: mỗi model là 1 mesh, gốc toạ độ ở giữa đáy (model gắn tường thì gốc ở mặt sau, giữa),
mặt trước hướng -Y trong Blender (= +Z / forward trong Unity), đơn vị mét.
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT_DIR = argv[0] if len(argv) > 0 else os.path.join(os.path.dirname(__file__), "export")
PREVIEW = argv[1] if len(argv) > 1 else os.path.join(os.path.dirname(__file__), "preview.png")
os.makedirs(OUT_DIR, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

# ------------------------------------------------------------------ vật liệu
MATS = {}

def mat(name, rgb, metal=0.0, rough=0.5, emit=None, alpha=1.0):
    if name in MATS:
        return MATS[name]
    m = bpy.data.materials.new(name)
    try:
        m.use_nodes = True
    except Exception:
        pass
    b = m.node_tree.nodes.get("Principled BSDF")
    col = (rgb[0], rgb[1], rgb[2], 1.0)
    b.inputs["Base Color"].default_value = col
    b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = rough
    if emit:
        b.inputs["Emission Color"].default_value = (emit[0], emit[1], emit[2], 1.0)
        b.inputs["Emission Strength"].default_value = 3.0
    if alpha < 1.0:
        b.inputs["Alpha"].default_value = alpha
    m.diffuse_color = (rgb[0], rgb[1], rgb[2], alpha)
    m.metallic = metal
    m.roughness = rough
    MATS[name] = m
    return m

RED = mat("FE_Red", (0.62, 0.03, 0.02), 0.1, 0.35)
RED_DARK = mat("FE_RedDark", (0.35, 0.02, 0.01), 0.1, 0.5)
BLACK = mat("FE_BlackRubber", (0.02, 0.02, 0.02), 0.0, 0.8)
STEEL = mat("FE_Steel", (0.75, 0.76, 0.78), 1.0, 0.25)
BRASS = mat("FE_Brass", (0.8, 0.6, 0.25), 1.0, 0.3)
YELLOW = mat("FE_Yellow", (0.95, 0.75, 0.05), 0.2, 0.4)
WHITE = mat("FE_White", (0.9, 0.9, 0.88), 0.0, 0.5)
LABEL = mat("FE_Label", (0.95, 0.95, 0.92), 0.0, 0.6)
GREEN_E = mat("FE_ExitGreen", (0.05, 0.6, 0.25), 0.0, 0.3, emit=(0.1, 1.0, 0.4))
WHITE_E = mat("FE_WhiteGlow", (0.95, 0.95, 0.9), 0.0, 0.3, emit=(1.0, 1.0, 0.95))
LENS = mat("FE_Lens", (1.0, 0.92, 0.7), 0.0, 0.1, emit=(1.0, 0.9, 0.6))
GLASS = mat("FE_Glass", (0.6, 0.8, 0.9), 0.0, 0.05, alpha=0.35)
GAUGE = mat("FE_Gauge", (0.95, 0.95, 0.95), 0.0, 0.2)
GAUGE_G = mat("FE_GaugeGreen", (0.1, 0.7, 0.2), 0.0, 0.4)
HOSE = mat("FE_HoseCanvas", (0.85, 0.82, 0.72), 0.0, 0.9)
FABRIC = mat("FE_Fabric", (0.55, 0.05, 0.04), 0.0, 0.95)
PLASTIC_GREY = mat("FE_GreyPlastic", (0.3, 0.31, 0.33), 0.0, 0.6)

# ------------------------------------------------------------------ helpers
parts = []

def _finish(o, m, bevel=0.0, smooth=True):
    o.data.materials.append(m)
    if bevel > 0:
        mod = o.modifiers.new("Bevel", "BEVEL")
        mod.width = bevel
        mod.segments = 3
        mod.limit_method = "ANGLE"
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.modifier_apply(modifier=mod.name)
    if smooth:
        bpy.context.view_layer.objects.active = o
        o.select_set(True)
        try:
            bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
        except Exception:
            bpy.ops.object.shade_smooth()
        o.select_set(False)
    parts.append(o)
    return o

def cyl(r, h, loc, m, rot=(0, 0, 0), verts=40, bevel=0.0, r2=None):
    if r2 is None:
        bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=h, location=loc, rotation=rot)
    else:
        bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r, radius2=r2, depth=h, location=loc, rotation=rot)
    return _finish(bpy.context.object, m, bevel)

def box(size, loc, m, rot=(0, 0, 0), bevel=0.0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot)
    o = bpy.context.object
    o.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return _finish(o, m, bevel, smooth=bevel > 0)

def sphere(r, loc, m, scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=40, ring_count=20, radius=r, location=loc)
    o = bpy.context.object
    o.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return _finish(o, m)

def torus(R, r, loc, m, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_torus_add(major_radius=R, minor_radius=r, major_segments=48, minor_segments=12, location=loc, rotation=rot)
    return _finish(bpy.context.object, m)

def tube(points, radius, m):
    cu = bpy.data.curves.new("tube", "CURVE")
    cu.dimensions = "3D"
    cu.bevel_depth = radius
    cu.bevel_resolution = 4
    sp = cu.splines.new("BEZIER")
    sp.bezier_points.add(len(points) - 1)
    for bp, p in zip(sp.bezier_points, points):
        bp.co = Vector(p)
        bp.handle_left_type = bp.handle_right_type = "AUTO"
    o = bpy.data.objects.new("tube", cu)
    bpy.context.collection.objects.link(o)
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    bpy.ops.object.convert(target="MESH")
    o.select_set(False)
    return _finish(o, m)

def text(body, size, loc, m, rot=(math.radians(90), 0, 0), extrude=0.0015):
    bpy.ops.object.text_add(location=loc, rotation=rot)
    o = bpy.context.object
    o.data.body = body
    o.data.size = size
    o.data.extrude = extrude
    o.data.align_x = "CENTER"
    o.data.align_y = "CENTER"
    bpy.ops.object.convert(target="MESH")
    o.data.materials.append(m)
    parts.append(o)
    return o

def assemble(name):
    """Gộp các phần vừa tạo thành 1 mesh, gốc tại (0,0,0)."""
    global parts
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    o = bpy.context.object
    o.name = name
    o.data.name = name
    scene.cursor.location = (0, 0, 0)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    o.select_set(False)
    parts = []
    return o

# ------------------------------------------------------------------ 1. Bình CO2
def extinguisher_co2():
    R, H = 0.075, 0.52
    cyl(R, H, (0, 0, 0.03 + H / 2), RED, bevel=0.01)
    cyl(R + 0.004, 0.03, (0, 0, 0.015), BLACK)
    sphere(R, (0, 0, 0.03 + H), RED, scale=(1, 1, 0.55))
    cyl(R + 0.0015, 0.2, (0, 0, 0.3), LABEL)
    box((0.06, 0.004, 0.03), (0, -R - 0.002, 0.34), BLACK)
    text("CO2", 0.035, (0, -R - 0.005, 0.34), WHITE)
    text("5 kg", 0.018, (0, -R - 0.003, 0.285), BLACK)
    cyl(0.022, 0.05, (0, 0, 0.62), BRASS)
    box((0.05, 0.035, 0.05), (0, 0, 0.665), STEEL, bevel=0.004)
    box((0.15, 0.024, 0.012), (0.045, 0, 0.695), BLACK, bevel=0.003)                        # tay xách
    box((0.14, 0.022, 0.01), (0.045, 0, 0.72), BLACK, rot=(0, math.radians(-10), 0), bevel=0.003)  # cò bóp
    torus(0.012, 0.0025, (-0.03, -0.02, 0.705), YELLOW, rot=(math.radians(90), 0, 0))       # chốt an toàn
    cyl(0.003, 0.05, (-0.03, 0, 0.705), STEEL, rot=(math.radians(90), 0, 0), verts=12)
    tube([(0.02, 0.0, 0.67), (0.06, -0.02, 0.62), (0.095, -0.03, 0.45), (0.095, -0.03, 0.25)], 0.009, BLACK)
    cyl(0.045, 0.2, (0.095, -0.03, 0.14), BLACK, r2=0.011)                                   # loa phun CO2
    box((0.03, 0.02, 0.03), (0.086, -0.012, 0.38), BLACK)
    return assemble("Extinguisher_CO2")

# ------------------------------------------------------------------ 2. Bình bột ABC
def extinguisher_powder():
    R, H = 0.08, 0.44
    cyl(R, H, (0, 0, 0.03 + H / 2), RED, bevel=0.012)
    cyl(R + 0.004, 0.03, (0, 0, 0.015), BLACK)
    sphere(R, (0, 0, 0.03 + H), RED, scale=(1, 1, 0.45))
    cyl(R + 0.0015, 0.18, (0, 0, 0.27), LABEL)
    text("ABC", 0.036, (0, -R - 0.005, 0.3), RED_DARK)
    text("4 kg", 0.018, (0, -R - 0.003, 0.25), BLACK)
    cyl(0.022, 0.04, (0, 0, 0.53), BRASS)
    box((0.05, 0.035, 0.05), (0, 0, 0.57), STEEL, bevel=0.004)
    cyl(0.022, 0.012, (0, -0.024, 0.575), GAUGE, rot=(math.radians(90), 0, 0))              # đồng hồ áp suất
    box((0.018, 0.002, 0.005), (0.004, -0.031, 0.578), GAUGE_G, rot=(0, math.radians(25), 0))
    torus(0.023, 0.003, (0, -0.024, 0.575), STEEL, rot=(math.radians(90), 0, 0))
    box((0.15, 0.024, 0.012), (0.045, 0, 0.6), BLACK, bevel=0.003)
    box((0.14, 0.022, 0.01), (0.045, 0, 0.625), BLACK, rot=(0, math.radians(-10), 0), bevel=0.003)
    torus(0.012, 0.0025, (-0.03, -0.02, 0.61), YELLOW, rot=(math.radians(90), 0, 0))
    tube([(0.02, 0.0, 0.57), (0.07, -0.01, 0.52), (0.098, -0.02, 0.38), (0.1, -0.02, 0.26)], 0.008, BLACK)
    cyl(0.011, 0.05, (0.1, -0.02, 0.23), BLACK, r2=0.006)
    return assemble("Extinguisher_Powder")

# ------------------------------------------------------------------ 3. Nút báo cháy (Manual Call Point) — gắn tường
def call_point():
    box((0.13, 0.05, 0.13), (0, -0.025, 0), RED, bevel=0.008)
    box((0.09, 0.006, 0.09), (0, -0.051, -0.006), WHITE, bevel=0.002)
    box((0.07, 0.004, 0.07), (0, -0.055, -0.006), GLASS)
    cyl(0.012, 0.006, (0, -0.056, -0.006), BLACK, rot=(math.radians(90), 0, 0))
    text("BÁO CHÁY", 0.013, (0, -0.052, 0.052), WHITE)
    text("NHẤN VÀO ĐÂY", 0.007, (0, -0.057, -0.038), BLACK)
    return assemble("CallPoint")

# ------------------------------------------------------------------ 4. Chuông báo cháy — gắn tường
def fire_bell():
    cyl(0.05, 0.02, (0, -0.01, 0), RED_DARK, rot=(math.radians(90), 0, 0))
    bpy.ops.mesh.primitive_uv_sphere_add(segments=48, ring_count=24, radius=0.08, location=(0, -0.02, 0))
    o = bpy.context.object
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.y > 0.0], context="VERTS")
    bm.to_mesh(o.data)
    bm.free()
    o.scale = (1, 0.6, 1)
    bpy.ops.object.transform_apply(scale=True)
    sol = o.modifiers.new("Solid", "SOLIDIFY")
    sol.thickness = 0.004
    bpy.ops.object.modifier_apply(modifier=sol.name)
    _finish(o, RED)
    cyl(0.01, 0.02, (0, -0.075, 0), STEEL, rot=(math.radians(90), 0, 0))
    box((0.012, 0.02, 0.04), (0, -0.07, -0.05), STEEL)
    return assemble("FireBell")

# ------------------------------------------------------------------ 5. Biển EXIT phát sáng — gắn tường
def exit_sign():
    box((0.38, 0.045, 0.16), (0, -0.0225, 0), WHITE, bevel=0.006)
    box((0.34, 0.004, 0.12), (0, -0.046, 0), GREEN_E)
    text("EXIT", 0.07, (-0.04, -0.049, 0), WHITE_E)
    # người chạy (giản lược) + mũi tên
    sphere(0.009, (0.085, -0.049, 0.035), WHITE_E)
    box((0.012, 0.003, 0.035), (0.083, -0.049, 0.012), WHITE_E, rot=(0, math.radians(-15), 0))
    box((0.01, 0.003, 0.03), (0.075, -0.049, -0.018), WHITE_E, rot=(0, math.radians(25), 0))
    box((0.01, 0.003, 0.03), (0.093, -0.049, -0.02), WHITE_E, rot=(0, math.radians(-30), 0))
    box((0.035, 0.003, 0.008), (0.135, -0.049, 0), WHITE_E)
    box((0.02, 0.003, 0.008), (0.146, -0.049, 0.007), WHITE_E, rot=(0, math.radians(40), 0))
    box((0.02, 0.003, 0.008), (0.146, -0.049, -0.007), WHITE_E, rot=(0, math.radians(-40), 0))
    return assemble("ExitSign")

# ------------------------------------------------------------------ 6. Đèn chiếu sáng sự cố — gắn tường
def emergency_light():
    box((0.3, 0.07, 0.09), (0, -0.035, 0), WHITE, bevel=0.01)
    box((0.08, 0.004, 0.02), (0, -0.071, -0.02), GREEN_E)
    for sx in (-1, 1):
        cyl(0.012, 0.03, (sx * 0.1, -0.06, 0.045), PLASTIC_GREY, rot=(math.radians(90), 0, 0))
        head = cyl(0.035, 0.05, (sx * 0.1, -0.1, 0.06), WHITE, rot=(math.radians(70), 0, sx * math.radians(20)), bevel=0.004)
        cyl(0.03, 0.006, (sx * 0.107, -0.123, 0.052), LENS, rot=(math.radians(70), 0, sx * math.radians(20)))
    text("EMERGENCY", 0.012, (0, -0.071, 0.02), PLASTIC_GREY)
    return assemble("EmergencyLight")

# ------------------------------------------------------------------ 7. Tủ vòi chữa cháy — đặt sàn, sát tường
def hose_cabinet():
    W, D, Hh = 0.7, 0.22, 0.95
    base = 0.35
    # vỏ tủ (hộp rỗng mặt trước)
    box((W, 0.02, Hh), (0, -0.01, base + Hh / 2), RED)                   # lưng
    box((0.02, D, Hh), (-W / 2 + 0.01, -D / 2, base + Hh / 2), RED)
    box((0.02, D, Hh), (W / 2 - 0.01, -D / 2, base + Hh / 2), RED)
    box((W, D, 0.02), (0, -D / 2, base + 0.01), RED)
    box((W, D, 0.02), (0, -D / 2, base + Hh - 0.01), RED)
    # cửa: khung + kính
    box((W, 0.02, 0.06), (0, -D - 0.01, base + Hh - 0.03), RED_DARK, bevel=0.004)
    box((W, 0.02, 0.06), (0, -D - 0.01, base + 0.03), RED_DARK, bevel=0.004)
    box((0.06, 0.02, Hh), (-W / 2 + 0.03, -D - 0.01, base + Hh / 2), RED_DARK, bevel=0.004)
    box((0.06, 0.02, Hh), (W / 2 - 0.03, -D - 0.01, base + Hh / 2), RED_DARK, bevel=0.004)
    box((W - 0.12, 0.006, Hh - 0.12), (0, -D - 0.008, base + Hh / 2), GLASS)
    box((0.02, 0.03, 0.12), (W / 2 - 0.08, -D - 0.03, base + Hh / 2), STEEL, bevel=0.004)  # tay nắm
    # cuộn vòi
    cyl(0.03, D - 0.04, (0, -D / 2, base + 0.55), STEEL, rot=(math.radians(90), 0, 0))
    for i in range(5):
        torus(0.2 - i * 0.022, 0.018, (0, -D / 2 - 0.05 + i * 0.022, base + 0.55), HOSE, rot=(math.radians(90), 0, 0))
    cyl(0.018, 0.16, (0.22, -D / 2, base + 0.22), BRASS, rot=(0, math.radians(90), 0), r2=0.01)   # lăng phun
    cyl(0.025, 0.05, (-0.24, -0.06, base + 0.2), BRASS)                                              # van
    torus(0.04, 0.008, (-0.24, -0.06, base + 0.25), RED_DARK)
    text("VÒI CHỮA CHÁY", 0.045, (0, -D - 0.022, base + Hh + 0.06), WHITE)
    box((0.5, 0.012, 0.08), (0, -D + 0.01, base + Hh + 0.06), RED)
    # chân tủ
    box((W, D, base), (0, -D / 2, base / 2), RED_DARK)
    return assemble("HoseCabinet")

# ------------------------------------------------------------------ 8. Chăn chống cháy — gắn tường
def fire_blanket():
    box((0.28, 0.06, 0.34), (0, -0.03, 0), FABRIC, bevel=0.025)
    box((0.22, 0.004, 0.14), (0, -0.061, 0.05), LABEL, bevel=0.002)
    text("FIRE", 0.035, (0, -0.064, 0.075), RED_DARK)
    text("BLANKET", 0.028, (0, -0.064, 0.035), RED_DARK)
    for sx in (-1, 1):
        box((0.035, 0.012, 0.09), (sx * 0.07, -0.02, -0.2), BLACK, bevel=0.004)
    return assemble("FireBlanket")

# ------------------------------------------------------------------ build
builders = [extinguisher_co2, extinguisher_powder, call_point, fire_bell, exit_sign, emergency_light, hose_cabinet, fire_blanket]
objs = [b() for b in builders]

# ------------------------------------------------------------------ export FBX (mỗi model một file)
for o in objs:
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    path = os.path.join(OUT_DIR, o.name + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH"},
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
        bake_space_transform=True, mesh_smooth_type="FACE", add_leaf_bones=False,
        path_mode="AUTO")
    print("EXPORTED", path)

# ------------------------------------------------------------------ preview
x = 0.0
wall = {"CallPoint": 1.3, "FireBell": 1.9, "ExitSign": 2.2, "EmergencyLight": 2.0, "FireBlanket": 1.4}
spacing = {"HoseCabinet": 0.9}
for o in objs:
    w = spacing.get(o.name, 0.42)
    x += w / 2
    o.location = (x, 0, wall.get(o.name, 0.0) * 0.45 if o.name in wall else 0.0)
    x += w / 2 + 0.08
bpy.ops.mesh.primitive_plane_add(size=20, location=(x / 2, 0, 0))
floor = bpy.context.object
floor.data.materials.append(mat("FE_Floor", (0.55, 0.56, 0.58), 0, 0.8))
bpy.ops.mesh.primitive_plane_add(size=20, location=(x / 2, 0.06, 5), rotation=(math.radians(90), 0, 0))
backwall = bpy.context.object
backwall.data.materials.append(mat("FE_Wall", (0.82, 0.8, 0.74), 0, 0.9))

cam_data = bpy.data.cameras.new("Cam")
cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam)
cam.location = (x / 2 + 0.5, -6.2, 1.8)
direction = Vector((x / 2, 0, 0.55)) - cam.location
cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
cam_data.lens = 40
scene.camera = cam

scene.render.engine = "BLENDER_WORKBENCH"
sh = scene.display.shading
sh.light = "STUDIO"
sh.color_type = "MATERIAL"
sh.show_shadows = True
sh.show_cavity = True
sh.cavity_type = "BOTH"
scene.display.shadow_focus = 0.6
scene.render.resolution_x = 1920
scene.render.resolution_y = 900
scene.render.film_transparent = False
scene.render.filepath = PREVIEW
bpy.ops.render.render(write_still=True)
print("PREVIEW", PREVIEW)

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(os.path.dirname(os.path.abspath(__file__)), "fire_equipment.blend"))
