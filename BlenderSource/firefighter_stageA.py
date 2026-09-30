"""Stage A: tách nhân vật nhìn thẳng khỏi turnaround 3 bản, chuẩn hoá tỉ lệ 1,8 m, giảm lưới, lưu .blend."""
import bpy, bmesh, sys, os
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT_BLEND, PREVIEW_DIR = argv[0], argv[1], argv[2]
TARGET_TRIS = 40000
HEIGHT = 1.80

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
obj = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
me = obj.data

# ---- histogram X để tìm khe giữa các nhân vật ----
xs = [v.co.x for v in me.vertices]
lo, hi = min(xs), max(xs)
BINS = 200
hist = [0] * BINS
for x in xs:
    hist[min(BINS - 1, int((x - lo) / (hi - lo) * BINS))] += 1
gaps = []
run = None
for i, c in enumerate(hist):
    if c == 0 and run is None: run = i
    if c != 0 and run is not None:
        gaps.append((run, i - 1)); run = None
print("GAPS(bins)", gaps)
cuts = [lo + (a + b + 1) / 2 / BINS * (hi - lo) for a, b in gaps]
print("CUT X", [round(c, 4) for c in cuts])
left_max = cuts[0] if cuts else (lo + (hi - lo) / 3)

# ---- giữ lại nhân vật bên trái (nhìn thẳng, mặt hướng -Y) ----
bm = bmesh.new()
bm.from_mesh(me)
kill = [v for v in bm.verts if v.co.x > left_max]
bmesh.ops.delete(bm, geom=kill, context='VERTS')
bm.to_mesh(me)
bm.free()
me.update()
print("FRONT FIGURE verts", len(me.vertices), "faces", len(me.polygons))

# ---- chuẩn hoá: chân ở z=0, tâm x/y = 0, cao 1,8 m ----
ps = [v.co for v in me.vertices]
mn = Vector((min(p.x for p in ps), min(p.y for p in ps), min(p.z for p in ps)))
mx = Vector((max(p.x for p in ps), max(p.y for p in ps), max(p.z for p in ps)))
s = HEIGHT / (mx.z - mn.z)
off = Vector(((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, mn.z))
for v in me.vertices:
    v.co = (v.co - off) * s
me.update()
print("SCALE", round(s, 4), "bbox size", tuple(round(v, 3) for v in (mx - mn) * s))

# ---- giảm lưới ----
bpy.context.view_layer.objects.active = obj
obj.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.remove_doubles(threshold=0.0005)
bpy.ops.mesh.delete_loose()
bpy.ops.object.mode_set(mode='OBJECT')
tris = sum(len(p.vertices) - 2 for p in me.polygons)
ratio = min(1.0, TARGET_TRIS / max(1, tris))
mod = obj.modifiers.new("Decimate", 'DECIMATE')
mod.decimate_type = 'COLLAPSE'
mod.ratio = ratio
mod.use_collapse_triangulate = True
bpy.ops.object.modifier_apply(modifier=mod.name)
print("DECIMATED tris", sum(len(p.vertices) - 2 for p in me.polygons), "ratio", round(ratio, 4))
bpy.ops.object.shade_smooth()
obj.name = "FirefighterBody"
me.name = "FirefighterBody"

bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND)

# ---- preview ----
scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
sh = scene.display.shading
sh.light = "STUDIO"; sh.color_type = "TEXTURE"; sh.show_cavity = True
scene.render.resolution_x = 700; scene.render.resolution_y = 1000
cd = bpy.data.cameras.new("Cam"); cd.type = 'ORTHO'; cd.ortho_scale = 2.0
cam = bpy.data.objects.new("Cam", cd); scene.collection.objects.link(cam); scene.camera = cam
for name, d in {"A_front": Vector((0, -1, 0)), "A_side": Vector((1, 0, 0))}.items():
    c = Vector((0, 0, 0.9))
    cam.location = c + d * 5
    cam.rotation_euler = (c - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(PREVIEW_DIR, name + ".png")
    bpy.ops.render.render(write_still=True)
