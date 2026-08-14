import bpy, bmesh, math, sys
from mathutils import Vector, Matrix

out_dir = sys.argv[-1]

def make_banana(name, length, curve_deg, thickness, sides=7, segments=24):
    """A banana is a tapered tube swept along an arc. Cross sections are a low-sided n-gon
    because real bananas are faceted, not round - that ridge line is most of the read."""
    verts, faces = [], []
    sweep = math.radians(curve_deg)
    radius = length / sweep if sweep > 0.001 else 1e6

    rings = []
    for s in range(segments + 1):
        t = s / segments
        ang = (t - 0.5) * sweep

        # spine point on the arc
        centre = Vector((math.sin(ang) * radius, 0.0, math.cos(ang) * radius - radius))
        fwd = Vector((math.cos(ang), 0.0, -math.sin(ang))).normalized()
        up = Vector((0, 1, 0))
        side = fwd.cross(up).normalized()

        # fat in the middle, pointed at both ends, and blunter at the stalk end
        taper = math.sin(math.pi * t) ** 0.55
        end_bias = 0.75 + 0.25 * t
        r = thickness * taper * end_bias

        ring = []
        for i in range(sides):
            a = 2 * math.pi * i / sides
            # squash one axis so the cross section is a rounded triangle-ish shape
            local = side * (math.cos(a) * r) + up * (math.sin(a) * r * 0.82)
            ring.append(len(verts))
            verts.append(centre + local)
        rings.append(ring)

    for s in range(segments):
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
    for poly in mesh.polygons:
        poly.use_smooth = True
    return obj

def banana_material(name, base, tip):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    b = mat.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*base, 1.0)
    b.inputs["Roughness"].default_value = 0.55
    return mat

specs = [
    # name,        length, curve, thickness
    # Silhouette is the whole read at a glance, so each one is a different shape rather than the
    # same banana at different scales: stubby, long, fat and bent, or very long and straight.
    ("BananaPistol",  0.26, 55, 0.035),
    ("BananaRifle",   0.62, 38, 0.040),
    ("BananaShotgun", 0.44, 70, 0.058),
    ("BananaSniper",  0.95, 18, 0.034),
    ("BananaPeel",    0.20, 95, 0.028),
]

bpy.ops.wm.read_factory_settings(use_empty=True)
for name, length, curve, thick in specs:
    for o in bpy.data.objects:
        o.select_set(False)
    obj = make_banana(name, length, curve, thick)
    obj.data.materials.append(banana_material(name + "Mat", (0.95, 0.80, 0.15), (0.45, 0.35, 0.10)))

    # The arc is built along Blender's +X. Blender is Z-up and Unity is Y-up, so Blender +Y is
    # what becomes Unity's forward (+Z) - that's the axis a held weapon has to point down.
    # Rotating +90 about Z maps +X onto +Y.
    obj.rotation_euler = (0, 0, math.radians(90))
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

    d = obj.dimensions
    print(f"[banana] {name}: {len(obj.data.vertices)} verts, dims=({d.x:.3f},{d.y:.3f},{d.z:.3f})")

    path = f"{out_dir}/{name}.fbx"
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, add_leaf_bones=False,
                             bake_anim=False, axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True, path_mode="COPY")
    print(f"[banana] exported {path}")
    bpy.data.objects.remove(obj, do_unlink=True)
