import bpy, sys, os, math
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT = argv[0], argv[1]
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)

print("=== OBJECTS ===")
for o in bpy.context.scene.objects:
    info = f"{o.name} type={o.type} loc={tuple(round(v,3) for v in o.location)} rot={tuple(round(math.degrees(v),1) for v in o.rotation_euler)} scale={tuple(round(v,3) for v in o.scale)} parent={o.parent.name if o.parent else None}"
    if o.type == 'MESH':
        me = o.data
        info += f" verts={len(me.vertices)} faces={len(me.polygons)} mats={[m.name for m in me.materials if m]} uv={len(me.uv_layers)} vcol={len(me.color_attributes)}"
        ws = [o.matrix_world @ v.co for v in me.vertices]
        mn = Vector((min(p.x for p in ws), min(p.y for p in ws), min(p.z for p in ws)))
        mx = Vector((max(p.x for p in ws), max(p.y for p in ws), max(p.z for p in ws)))
        info += f" bbox_min={tuple(round(v,3) for v in mn)} bbox_max={tuple(round(v,3) for v in mx)}"
        info += f" vgroups={len(o.vertex_groups)} modifiers={[m.type for m in o.modifiers]}"
    if o.type == 'ARMATURE':
        info += f" bones={len(o.data.bones)}"
    print(info)

# render front / side / 3-4 views with material preview (workbench texture)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
allp = [o.matrix_world @ v.co for o in meshes for v in o.data.vertices]
mn = Vector((min(p.x for p in allp), min(p.y for p in allp), min(p.z for p in allp)))
mx = Vector((max(p.x for p in allp), max(p.y for p in allp), max(p.z for p in allp)))
c = (mn + mx) / 2
size = max(mx - mn)
scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
sh = scene.display.shading
sh.light = "STUDIO"
sh.color_type = "TEXTURE"
sh.show_cavity = True
scene.render.resolution_x = 900
scene.render.resolution_y = 1200
cam_data = bpy.data.cameras.new("Cam")
cam_data.type = 'ORTHO'
cam_data.ortho_scale = size * 1.15
cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
views = {"front": Vector((0, -1, 0)), "side": Vector((1, 0, 0)), "back": Vector((0, 1, 0)), "q34": Vector((0.7, -0.7, 0.15))}
for name, d in views.items():
    cam.location = c + d.normalized() * size * 3
    cam.rotation_euler = (c - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(OUT, f"ff_{name}.png")
    bpy.ops.render.render(write_still=True)
# head close-up
head_c = Vector((c.x, c.y, mx.z - size * 0.09))
cam_data.ortho_scale = size * 0.25
for name, d in {"head_front": Vector((0, -1, 0)), "head_side": Vector((1, 0, 0))}.items():
    cam.location = head_c + d * size * 3
    cam.rotation_euler = (head_c - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(OUT, f"ff_{name}.png")
    bpy.ops.render.render(write_still=True)
print("CENTER", tuple(round(v, 3) for v in c), "SIZE", round(size, 3))
