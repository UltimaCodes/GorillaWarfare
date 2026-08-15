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

def make_cylinder(name, radius, length, axis="y", sides=12):
    """Drum for the revolver, tube for the scope. Not a banana, but a banana alone can't say
    'revolver' or 'sniper' - you need one recognisable gun part to carry the read."""
    verts, faces = [], []
    for end in (0, 1):
        for i in range(sides):
            a = 2 * math.pi * i / sides
            c, s2 = math.cos(a) * radius, math.sin(a) * radius
            off = (end - 0.5) * length
            verts.append(Vector((c, off, s2)) if axis == "y" else Vector((off, c, s2)))
    for i in range(sides):
        j = (i + 1) % sides
        faces.append((i, j, sides + j, sides + i))
    faces.append(tuple(range(sides)))
    faces.append(tuple(reversed(range(sides, sides * 2))))

    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(v) for v in verts], [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    bm = bmesh.new(); bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh); bm.free()
    return obj

# Each weapon is a shape, not a scale. A banana on its own reads as "banana" no matter how long
# you make it, so the ones that need to read as a specific gun get extra parts.
#   name          colour (rgb)          parts: (kind, args, position, rotation)
RIPE   = (0.95, 0.80, 0.15)
GREEN  = (0.62, 0.80, 0.22)
BROWN  = (0.55, 0.38, 0.14)
PALE   = (0.97, 0.93, 0.70)
SPOTTY = (0.86, 0.68, 0.20)

specs = [
    # revolver: stubby banana plus a fat drum where the cylinder would be
    ("BananaPistol", RIPE, [
        ("banana", (0.24, 60, 0.034), (0, 0, 0), (0, 0, 0)),
        ("cyl", (0.045, 0.055), (0, -0.02, 0.012), (0, 0, math.radians(90))),
    ]),
    # rifle: one long banana, two handed
    ("BananaRifle", GREEN, [
        ("banana", (0.66, 34, 0.038), (0, 0, 0), (0, 0, 0)),
    ]),
    # shotgun: two bananas taped side by side
    ("BananaShotgun", SPOTTY, [
        ("banana", (0.46, 62, 0.042), (0.024, 0, 0), (0, 0, 0)),
        ("banana", (0.46, 62, 0.042), (-0.024, 0, 0), (0, 0, 0)),
        ("cyl", (0.052, 0.030), (0, -0.03, 0), (0, 0, math.radians(90))),
    ]),
    # sniper: absurdly long, plus a scope tube so it isn't just a big banana
    ("BananaSniper", BROWN, [
        ("banana", (1.15, 14, 0.030), (0, 0, 0), (0, 0, 0)),
        ("cyl", (0.022, 0.20), (0, 0.02, 0.045), (0, 0, 0)),
    ]),
    # peel: small and very curled
    ("BananaPeel", PALE, [
        ("banana", (0.20, 110, 0.026), (0, 0, 0), (0, 0, 0)),
    ]),
]

bpy.ops.wm.read_factory_settings(use_empty=True)
for name, colour, parts in specs:
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)

    pieces = []
    for kind, args, pos, rot in parts:
        piece = make_banana(f"{name}_p{len(pieces)}", *args) if kind == "banana"                 else make_cylinder(f"{name}_p{len(pieces)}", *args)
        piece.location = Vector(pos)
        piece.rotation_euler = rot
        pieces.append(piece)

    # join into one mesh so it's a single renderer per weapon
    for pc in pieces:
        pc.select_set(True)
    bpy.context.view_layer.objects.active = pieces[0]
    if len(pieces) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    obj.name = name
    obj.data.materials.append(banana_material(name + "Mat", colour, (0.45, 0.35, 0.10)))

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
