import bpy, bmesh, math, sys
from mathutils import Vector

out_dir = sys.argv[-1]

def limb(name, segments, sides=8):
    """A tapered tube through a list of (point, radius). Gorilla arms are thick and taper hard
    into the wrist, which is most of what makes them read as an ape rather than a person."""
    verts, faces = [], []
    rings = []
    for pt, r in segments:
        ring = []
        for i in range(sides):
            a = 2 * math.pi * i / sides
            ring.append(len(verts))
            verts.append(Vector(pt) + Vector((math.cos(a) * r, math.sin(a) * r, 0)))
        rings.append(ring)
    for s in range(len(rings) - 1):
        a, b = rings[s], rings[s + 1]
        for i in range(sides):
            j = (i + 1) % sides
            faces.append((a[i], a[j], b[j], b[i]))
    faces.append(tuple(reversed(rings[0])))
    faces.append(tuple(rings[-1]))

    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(v) for v in verts], [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    bm = bmesh.new(); bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh); bm.free()
    for p in mesh.polygons:
        p.use_smooth = True
    return obj

bpy.ops.wm.read_factory_settings(use_empty=True)

# Built in Blender space: +Y is forward (becomes Unity +Z), +Z is up (becomes Unity +Y).
# Arms come from behind and below the camera and reach forward to where the weapon sits.
def arm(name, side):
    """Forearm and hand only. The first version ran from the shoulder, which is most of a metre
    of arm that can never be on screen - a viewmodel only ever shows from about the elbow."""
    x = 0.085 * side
    return limb(name, [
        ((x * 1.30, -0.20, -0.075), 0.052),   # elbow, just off the bottom of the frame
        ((x * 1.15, -0.09, -0.055), 0.047),   # forearm
        ((x * 1.02,  0.00, -0.035), 0.043),   # wrist
        ((x * 0.98,  0.05, -0.025), 0.050),   # hand swells at the knuckles
        ((x * 0.96,  0.10, -0.018), 0.047),   # fist
        ((x * 0.95,  0.13, -0.014), 0.026),   # fingertips curl over
    ])

pieces = [arm("ArmL", -1), arm("ArmR", 1)]
for p in pieces:
    p.select_set(True)
bpy.context.view_layer.objects.active = pieces[0]
bpy.ops.object.join()

obj = bpy.context.view_layer.objects.active
obj.name = "ViewArms"

mat = bpy.data.materials.new("ViewArmsMat")
mat.use_nodes = True
b = mat.node_tree.nodes["Principled BSDF"]
b.inputs["Base Color"].default_value = (0.19, 0.17, 0.16, 1.0)
b.inputs["Roughness"].default_value = 0.85
obj.data.materials.append(mat)

d = obj.dimensions
print(f"[arms] {len(obj.data.vertices)} verts, dims=({d.x:.3f},{d.y:.3f},{d.z:.3f})")

obj.select_set(True)
bpy.ops.export_scene.fbx(filepath=f"{out_dir}/ViewArms.fbx", use_selection=True,
                         add_leaf_bones=False, bake_anim=False,
                         axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                         path_mode="COPY")
print(f"[arms] exported {out_dir}/ViewArms.fbx")
