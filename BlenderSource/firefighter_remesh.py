"""
Remesh lính cứu hỏa: lưới AI (Tripo) gồm nhiều mảnh rời, tam giác méo, lớp chồng nhau → biến dạng lởm chởm khi rig.
1) Giữ bản gốc làm "FirefighterSource" (ẩn) để stage C bake texture/normal sang lưới mới.
2) Voxel remesh → một khối kín, liền mạch (hàn các mảnh vụn, bịt khe).
3) Decimate về ~44k tam giác, mật độ đồng nhất (khớp gập mượt khi skinning).
Chạy: blender -b firefighter_A.blend --factory-startup -P firefighter_remesh.py -- <out.blend> [voxel] [tris]
Sau đó: stage B trên <out.blend> → stage C (bake từ FirefighterSource).
"""
import bpy, bmesh, sys, time

argv = sys.argv[sys.argv.index("--") + 1:]
OUT = argv[0]
VOXEL = float(argv[1]) if len(argv) > 1 else 0.007
TRIS = int(argv[2]) if len(argv) > 2 else 44000

body = bpy.data.objects["FirefighterBody"]
src = body.copy(); src.data = body.data.copy()
src.name = src.data.name = "FirefighterSource"
bpy.context.scene.collection.objects.link(src)
src.hide_render = True; src.hide_set(True)

bpy.ops.object.select_all(action='DESELECT')
bpy.context.view_layer.objects.active = body; body.select_set(True)
me = body.data
print("SOURCE verts", len(me.vertices), "tris", sum(len(p.vertices) - 2 for p in me.polygons))

t = time.time()
me.remesh_voxel_size = VOXEL
me.remesh_voxel_adaptivity = 0.0
me.use_remesh_fix_poles = True
bpy.ops.object.voxel_remesh()
print("VOXEL %.3f → verts %d (%.1fs)" % (VOXEL, len(body.data.vertices), time.time() - t))

# bỏ các khối rời rất nhỏ (bụi voxel) — giữ vỏ chính và phụ kiện đáng kể
bm = bmesh.new(); bm.from_mesh(body.data)
bm.verts.ensure_lookup_table()
seen = set(); comps = []
for v0 in bm.verts:
    if v0.index in seen: continue
    st = [v0]; seen.add(v0.index); comp = []
    while st:
        v = st.pop(); comp.append(v)
        for e in v.link_edges:
            w = e.other_vert(v)
            if w.index not in seen: seen.add(w.index); st.append(w)
    comps.append(comp)
comps.sort(key=len, reverse=True)
kill = [v for c in comps[1:] if len(c) < 200 for v in c]
bmesh.ops.delete(bm, geom=kill, context='VERTS')
bm.to_mesh(body.data); bm.free()
print("COMPONENTS", len(comps), "removed small", sum(1 for c in comps[1:] if len(c) < 200))

# giảm lưới: voxel đã cho mật độ đều → Decimate (collapse) giữ chi tiết ở vùng cong, gộp vùng phẳng
# (QuadriFlow từ chối lưới voxel nhiều lớp vỏ lồng nhau: "normals not consistent")
tris = sum(len(p.vertices) - 2 for p in body.data.polygons)
mod = body.modifiers.new("Dec", "DECIMATE"); mod.decimate_type = "COLLAPSE"
mod.ratio = TRIS / max(1, tris); mod.use_collapse_triangulate = True
bpy.ops.object.modifier_apply(modifier=mod.name)
me = body.data
me.name = "FirefighterBody"
if not me.uv_layers: me.uv_layers.new(name="UVMap")   # UV tạm; stage C trải và bake lại
bpy.ops.object.shade_smooth()
print("REMESH tris", sum(len(p.vertices) - 2 for p in me.polygons))
bpy.ops.wm.save_as_mainfile(filepath=OUT)
print("SAVED", OUT)
