import bpy, sys, math
from mathutils import Vector, Euler, Matrix

smd_path, out_path, tex_dir = sys.argv[-3], sys.argv[-2], sys.argv[-1]

# ---- parse SMD -------------------------------------------------------------
nodes, bind, tris = {}, {}, []
section = None
pending = []
for raw in open(smd_path, encoding="utf-8", errors="ignore"):
    line = raw.strip()
    if not line: continue
    if line in ("nodes", "skeleton", "triangles"):
        section = line; continue
    if line == "end":
        section = None; continue
    if section == "nodes":
        i = line.index('"'); j = line.rindex('"')
        nodes[int(line[:i])] = (line[i+1:j], int(line[j+1:]))
    elif section == "skeleton":
        if line.startswith("time"): continue
        p = line.split()
        bind[int(p[0])] = ([float(x) for x in p[1:4]], [float(x) for x in p[4:7]])
    elif section == "triangles":
        p = line.split()
        if len(p) < 9:
            continue  # material name line
        pos = Vector((float(p[1]), float(p[2]), float(p[3])))
        links = []
        if len(p) > 9:
            n = int(p[9])
            for k in range(n):
                links.append((int(p[10 + k*2]), float(p[11 + k*2])))
        else:
            links.append((int(p[0]), 1.0))
        pending.append((pos, links))
        if len(pending) == 3:
            tris.append(pending); pending = []

print(f"[smd] bones={len(nodes)} tris={len(tris)}")

# ---- build armature --------------------------------------------------------
bpy.ops.wm.read_factory_settings(use_empty=True)
arm_data = bpy.data.armatures.new("GorillaRig")
arm = bpy.data.objects.new("GorillaRig", arm_data)
bpy.context.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")

world = {}
ebones = {}
for i in sorted(nodes):
    name, parent = nodes[i]
    pos, rot = bind[i]
    local = Matrix.Translation(Vector(pos)) @ Euler(rot, "XYZ").to_matrix().to_4x4()
    world[i] = (world[parent] @ local) if parent >= 0 else local
    eb = arm_data.edit_bones.new(name)
    eb.head = world[i].to_translation()
    eb.tail = eb.head + world[i].to_3x3() @ Vector((0, 0.06, 0))
    if parent >= 0:
        eb.parent = ebones[parent]
    ebones[i] = eb
bpy.ops.object.mode_set(mode="OBJECT")

# ---- build mesh ------------------------------------------------------------
verts, faces, weights = [], [], []
for tri in tris:
    idx = []
    for pos, links in tri:
        idx.append(len(verts)); verts.append(pos); weights.append(links)
    faces.append(tuple(idx))

mesh = bpy.data.meshes.new("GorillaMesh")
mesh.from_pydata(verts, [], faces)
mesh.update()
obj = bpy.data.objects.new("Gorilla", mesh)
bpy.context.collection.objects.link(obj)

groups = {}
for i, (name, _) in nodes.items():
    groups[i] = obj.vertex_groups.new(name=name)
for vi, links in enumerate(weights):
    for bone, w in links:
        if bone in groups and w > 0:
            groups[bone].add([vi], w, "REPLACE")

obj.parent = arm
mod = obj.modifiers.new("Armature", "ARMATURE")
mod.object = arm

# --- material ---------------------------------------------------------------
# The asset has to be right standalone. Binding this at runtime meant the scene view
# showed an untextured model forever, which made it impossible to eyeball.
import os
mat = bpy.data.materials.new("GorillaMat")
mat.use_nodes = True
nt = mat.node_tree
bsdf = nt.nodes["Principled BSDF"]
bsdf.inputs["Roughness"].default_value = 0.9   # fur isn't shiny

diff = nt.nodes.new("ShaderNodeTexImage")
diff.image = bpy.data.images.load(os.path.join(tex_dir, "TGorilla_Diffuse.png"))
nt.links.new(bsdf.inputs["Base Color"], diff.outputs["Color"])

nrm_img = nt.nodes.new("ShaderNodeTexImage")
nrm_img.image = bpy.data.images.load(os.path.join(tex_dir, "TGorilla_Normal.png"))
nrm_img.image.colorspace_settings.name = "Non-Color"
nrm_map = nt.nodes.new("ShaderNodeNormalMap")
nt.links.new(nrm_map.inputs["Color"], nrm_img.outputs["Color"])
nt.links.new(bsdf.inputs["Normal"], nrm_map.outputs["Normal"])

obj.data.materials.append(mat)
print("[smd] material GorillaMat built with diffuse + normal")

# merge the duplicated triangle corners back into a solid mesh
bpy.context.view_layer.objects.active = obj
obj.select_set(True)
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.mesh.remove_doubles(threshold=0.0001)
bpy.ops.object.mode_set(mode="OBJECT")
print(f"[smd] merged mesh verts={len(mesh.vertices)}")
print(f"[smd] dims={tuple(round(d,3) for d in obj.dimensions)}")

# Bake the scale here rather than leaning on Unity's import scale. Source units are huge and
# the previous route left roughly 100x on the imported root - mesh bounds looked right while
# world bounds were 254 units.
TARGET = 1.9
height = obj.dimensions.z             # model is Z-up, so Z is his height (not max - that is the arm span)
factor = TARGET / height if height > 0 else 1.0
for o in (arm, obj):
    o.select_set(True)
bpy.context.view_layer.objects.active = arm
arm.scale = (factor, factor, factor)
bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
print(f"[smd] height {height:.2f} -> factor {factor:.5f} -> {tuple(round(d,3) for d in obj.dimensions)}")

for o in bpy.data.objects: o.select_set(True)
# bake_space_transform writes the Z-up -> Y-up conversion into the vertices and bones,
# so the asset itself is upright. Without it the fbx stays Z-up and something downstream
# has to keep rotating it, which is what made the scene view useless.
bpy.ops.export_scene.fbx(filepath=out_path, use_selection=True,
                         add_leaf_bones=False, bake_anim=False, path_mode="COPY",
                         axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                         global_scale=1.0, apply_unit_scale=True, apply_scale_options="FBX_SCALE_NONE")
print("[smd] exported", out_path)
