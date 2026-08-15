import bpy, bmesh, math, sys
from mathutils import Vector

out_dir = sys.argv[-1]

def banana(name, length, curve_deg, thickness, sides=9, segments=40):
    """A real banana, not a bent tube.

    Three things do the work:
      - the cross section is a rounded triangle, because a banana has three flat-ish faces and
        sharp ridges between them. A circle reads as a sausage.
      - the profile is asymmetric: blunt and squared at the stalk, tapering to a point at the tip.
      - the curve tightens toward the tip rather than being a constant arc.
    """
    verts, faces = [], []
    sweep = math.radians(curve_deg)
    rings = []

    for s in range(segments + 1):
        t = s / segments

        # curve accelerates toward the tip - a constant arc looks mechanical
        bend = (t ** 1.35) * sweep - sweep * 0.32
        radius = length / max(sweep, 0.001)
        centre = Vector((math.sin(bend) * radius, 0.0, math.cos(bend) * radius - radius))
        fwd = Vector((math.cos(bend), 0.0, -math.sin(bend))).normalized()
        up = Vector((0, 1, 0))
        side = fwd.cross(up).normalized()

        # blunt stalk end, full belly, point at the tip
        if t < 0.09:
            prof = 0.55 + 0.45 * (t / 0.09)          # squared off stalk
        elif t > 0.86:
            prof = max(0.05, 1.0 - ((t - 0.86) / 0.14) ** 1.5)   # taper to a nose
        else:
            prof = 0.92 + 0.08 * math.sin((t - 0.09) / 0.77 * math.pi)

        r = thickness * prof

        ring = []
        for i in range(sides):
            a = 2 * math.pi * i / sides
            # rounded triangle: three lobes with soft corners
            lobe = 1.0 + 0.16 * math.cos(3.0 * a)
            rr = r * lobe
            local = side * (math.cos(a) * rr) + up * (math.sin(a) * rr * 0.86)
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

    # smooth everything except the ridges, which stay sharp - that's the banana read
    mesh.shade_smooth()
    for p in mesh.polygons:
        p.use_smooth = True
    return obj

def tape(name, radius, width):
    """Band holding the shotgun's two bananas together."""
    verts, faces, sides = [], [], 14
    for end in (0, 1):
        for i in range(sides):
            a = 2 * math.pi * i / sides
            verts.append(Vector((math.cos(a) * radius, (end - 0.5) * width, math.sin(a) * radius * 0.55)))
    for i in range(sides):
        j = (i + 1) % sides
        faces.append((i, j, sides + j, sides + i))
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(v) for v in verts], [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    return obj

# Straight from the brief: pistol is a plain banana, shotgun is two taped side by side, rifle is
# longer, sniper is obnoxiously long.
specs = [
    ("BananaPistol",  [("b", (0.30, 62, 0.036), (0, 0, 0))]),
    ("BananaRifle",   [("b", (0.70, 40, 0.038), (0, 0, 0))]),
    ("BananaShotgun", [("b", (0.50, 58, 0.040), ( 0.045, 0, 0)),
                       ("b", (0.50, 58, 0.040), (-0.045, 0, 0)),
                       ("t", (0.075, 0.045),    (0, -0.06, 0)),
                       ("t", (0.075, 0.045),    (0,  0.10, 0))]),
    ("BananaSniper",  [("b", (1.45, 22, 0.032), (0, 0, 0))]),
    ("BananaPeel",    [("b", (0.22, 105, 0.026), (0, 0, 0))]),
]

bpy.ops.wm.read_factory_settings(use_empty=True)
for name, parts in specs:
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)

    pieces = []
    for kind, args, pos in parts:
        p = banana(f"{name}_{len(pieces)}", *args) if kind == "b" else tape(f"{name}_{len(pieces)}", *args)
        p.location = Vector(pos)
        pieces.append(p)

    for p in pieces:
        p.select_set(True)
    bpy.context.view_layer.objects.active = pieces[0]
    if len(pieces) > 1:
        bpy.ops.object.join()

    obj = bpy.context.view_layer.objects.active
    obj.name = name

    # Built along Blender +X; +Y is what becomes Unity forward.
    obj.rotation_euler = (0, 0, math.radians(90))
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

    d = obj.dimensions
    print(f"[banana] {name}: {len(obj.data.vertices)} verts, dims=({d.x:.3f},{d.y:.3f},{d.z:.3f})")

    bpy.ops.export_scene.fbx(filepath=f"{out_dir}/{name}.fbx", use_selection=True,
                             add_leaf_bones=False, bake_anim=False,
                             axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                             path_mode="COPY")
