# Product render of one FDM part, run by MS3DPRINT Manager.
#   One render:   blender -b --factory-startup -P product_render.py -- settings.json
#   Kept running: blender -b --factory-startup -P product_render.py -- --server
#     then one settings.json path per line on stdin; each render answers "MS3D-DONE" or "MS3D-ERROR <reason>" on stdout.
# The whole scene is built here (backdrop, lights, camera, material), so no .blend file has to be kept or configured.
import bpy, json, math, sys, traceback
from mathutils import Matrix, Vector


def linear(hexa):
    h = hexa.lstrip("#")
    srgb = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return [c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in srgb] + [1.0]


def set_input(node, name, value):
    socket = node.inputs.get(name)
    if socket is not None:
        socket.default_value = value


# The imported part is kept between renders: only a new model is imported again.
cache = {"model": None, "mesh": None, "bottom": None, "fit": 1.0}


def load_part(path):
    if cache["model"] != path:
        if cache["mesh"] is not None:
            bpy.data.meshes.remove(cache["mesh"])
        bpy.ops.wm.stl_import(filepath=path)
        imported = bpy.context.view_layer.objects.active
        mesh = imported.data
        corners = [Vector(c) for c in imported.bound_box]
        low = Vector(map(min, *corners))
        high = Vector(map(max, *corners))
        bpy.data.objects.remove(imported)
        cache.update(model=path, mesh=mesh, bottom=Vector(((low.x + high.x) / 2, (low.y + high.y) / 2, low.z)), fit=1 / max(high - low))
        # Bottom centre at the origin; object space stays in millimetres for the layer lines.
        mesh.transform(Matrix.Translation(-cache["bottom"]))
    part = bpy.data.objects.new("Part", cache["mesh"])
    bpy.context.scene.collection.objects.link(part)
    return part


def clear_scene():
    for thing in list(bpy.data.objects):
        bpy.data.objects.remove(thing)
    for collection in (bpy.data.meshes, bpy.data.materials, bpy.data.lights, bpy.data.cameras, bpy.data.worlds):
        for block in list(collection):
            if block is not cache["mesh"] and block.users == 0:
                collection.remove(block)


def use_graphics_card(scene):
    preferences = bpy.context.preferences.addons["cycles"].preferences
    for kind in ("OPTIX", "CUDA", "HIP", "ONEAPI", "METAL"):
        try:
            preferences.compute_device_type = kind
        except TypeError:
            continue
        preferences.get_devices()
        if any(d.type == kind for d in preferences.devices):
            for device in preferences.devices:
                device.use = device.type == kind
            scene.cycles.device = "GPU"
            return


def render(settings):
    scene = bpy.context.scene
    clear_scene()

    # --- the part: largest side 1 unit, turned on the turntable
    part = load_part(settings["model"])
    fit = cache["fit"]
    part.scale = (fit, fit, fit)
    part.rotation_euler = (0, 0, math.radians(settings["rotation"]))
    bpy.context.view_layer.objects.active = part
    part.select_set(True)
    try:
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
    except (AttributeError, RuntimeError):
        bpy.ops.object.shade_flat()

    # --- filament: principled shader, optional layer lines from the object height
    finish = {"matte": (0.62, 0.0, 0.0), "silk": (0.3, 0.45, 0.0), "glossy": (0.12, 0.0, 0.35), "glow": (0.5, 0.0, 0.0)}[settings["finish"]]
    glow_dark = settings["finish"] == "glow" and settings.get("glowDark", False)
    material = bpy.data.materials.new("Filament")
    material.use_nodes = True
    nodes, links = material.node_tree.nodes, material.node_tree.links
    bsdf = nodes["Principled BSDF"]
    set_input(bsdf, "Base Color", linear(settings["color"]))
    set_input(bsdf, "Roughness", finish[0])
    set_input(bsdf, "Metallic", finish[1])
    set_input(bsdf, "Coat Weight", finish[2])
    if settings["finish"] == "glow":
        # Glow in the dark: a faint glow in the light, a strong one with the lights off.
        set_input(bsdf, "Emission Color", linear(settings["color"]))
        set_input(bsdf, "Emission Strength", 1.6 if glow_dark else 0.25)
    part.visible_shadow = settings.get("shadow", True)
    layer = settings["layerHeight"]
    if layer > 0:
        coords = nodes.new("ShaderNodeTexCoord")
        split = nodes.new("ShaderNodeSeparateXYZ")
        wave = nodes.new("ShaderNodeMath")
        wave.operation = "MULTIPLY"
        wave.inputs[1].default_value = 2 * math.pi / layer
        sine = nodes.new("ShaderNodeMath")
        sine.operation = "SINE"
        bump = nodes.new("ShaderNodeBump")
        bump.inputs["Strength"].default_value = 0.6
        set_input(bump, "Distance", layer * fit * 0.25)
        links.new(coords.outputs["Object"], split.inputs[0])
        links.new(split.outputs["Z"], wave.inputs[0])
        links.new(wave.outputs[0], sine.inputs[0])
        links.new(sine.outputs[0], bump.inputs["Height"])
        links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    part.data.materials.clear()
    part.data.materials.append(material)

    # --- set: curved studio backdrop, or a shadow-catching floor for a transparent background or an environment (HDRI)
    style = settings["scene"]
    floor_only = style in ("transparent", "environment")
    backdrop_color = {"light": "#E4E6EA", "dark": "#1E2126", "color": settings["background"]}.get(style, "#FFFFFF")
    profile = [(-12, 0), (1.2, 0)] + [(1.2 + 2 * math.sin(a), 2 - 2 * math.cos(a)) for a in [i * math.pi / 32 for i in range(1, 17)]] + [(3.2, 12)]
    if floor_only:
        profile = [(-12, 0), (12, 0)]
    vertices = [(x, y, z) for (y, z) in profile for x in (-12, 12)]
    faces = [(2 * i, 2 * i + 1, 2 * i + 3, 2 * i + 2) for i in range(len(profile) - 1)]
    mesh = bpy.data.meshes.new("Backdrop")
    mesh.from_pydata(vertices, [], faces)
    mesh.shade_smooth()
    backdrop = bpy.data.objects.new("Backdrop", mesh)
    scene.collection.objects.link(backdrop)
    paint = bpy.data.materials.new("Backdrop")
    paint.use_nodes = True
    set_input(paint.node_tree.nodes["Principled BSDF"], "Base Color", linear(backdrop_color))
    set_input(paint.node_tree.nodes["Principled BSDF"], "Roughness", 0.9)
    mesh.materials.append(paint)
    backdrop.is_shadow_catcher = floor_only
    scene.render.film_transparent = style == "transparent"
    eevee = settings.get("engine") == "eevee"
    # EEVEE has no shadow catcher: its live previews leave that floor out (the part then floats), Cycles keeps the shadow.
    backdrop.hide_render = eevee and floor_only

    # --- lights: soft key, fill and rim, brighter rim on the dark set; or the environment alone
    # Shadow strength: the fill and ambient light that lighten the shadows; softness: the size of the lights;
    # direction: the three lights turn together about the part, the key light at the chosen height.
    strength = settings.get("shadowStrength", 0.3)
    softness = 0.25 + 1.5 * settings.get("shadowSoftness", 0.5)
    turn = math.radians(settings.get("lightAngle", 0))
    lift = math.radians(settings.get("lightHeight", 39))
    ambient = (1.4 - strength) / 1.1

    def place(x, y, z):
        return (x * math.cos(turn) - y * math.sin(turn), x * math.sin(turn) + y * math.cos(turn), z)

    def area(name, location, power, size_m, color="#FFFFFF"):
        light = bpy.data.lights.new(name, "AREA")
        light.energy = power
        light.size = size_m * softness
        location = place(*location)
        light.color = linear(color)[:3]
        holder = bpy.data.objects.new(name, light)
        holder.location = location
        holder.rotation_euler = (Vector((0, 0, 0.45)) - Vector(location)).to_track_quat("-Z", "Y").to_euler()
        scene.collection.objects.link(holder)

    dark = style == "dark"
    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    background = world.node_tree.nodes["Background"]
    scene.world = world
    if style == "environment":
        texture = world.node_tree.nodes.new("ShaderNodeTexEnvironment")
        texture.image = bpy.data.images.load(settings["environment"], check_existing=True)
        mapping = world.node_tree.nodes.new("ShaderNodeMapping")
        mapping.inputs["Rotation"].default_value[2] = math.radians(settings["environmentRotation"])
        coords = world.node_tree.nodes.new("ShaderNodeTexCoord")
        world.node_tree.links.new(coords.outputs["Generated"], mapping.inputs["Vector"])
        world.node_tree.links.new(mapping.outputs["Vector"], texture.inputs["Vector"])
        world.node_tree.links.new(texture.outputs["Color"], background.inputs["Color"])
        set_input(background, "Strength", 1.6)
    elif glow_dark:
        set_input(background, "Color", linear("#0A0D14"))
        set_input(background, "Strength", 0.02)
    else:
        key_azimuth = math.atan2(-2.4, -2.2)
        area("Key", (4.17 * math.cos(lift) * math.cos(key_azimuth), 4.17 * math.cos(lift) * math.sin(key_azimuth), 4.17 * math.sin(lift)),
             190 if not dark else 180, 2.5, "#FFF4E8")
        area("Fill", (2.6, -1.8, 1.2), (110 if not dark else 35) * ambient, 3.0, "#EAF2FF")
        area("Rim", (0.6, 2.4, 2.8), 160 if not dark else 320, 1.5)
        set_input(background, "Color", linear("#FFFFFF" if not dark else "#5A6270"))
        set_input(background, "Strength", (0.25 if not dark else 0.08) * ambient)

    # --- camera: the one of the 3D view when given (model millimetres), else a front view framing the part
    width, height = settings["width"], settings["height"]
    camera_data = bpy.data.cameras.new("Camera")
    camera = bpy.data.objects.new("Camera", camera_data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    view = settings.get("camera")
    if view:
        to_scene = lambda p: (Vector(p) - cache["bottom"]) * fit
        camera.location = to_scene(view["location"])
        target = to_scene(view["target"])
        camera_data.sensor_fit = "VERTICAL"
        camera_data.angle_y = math.radians(view["fieldOfView"])
        camera_data.shift_x = view["shiftX"]
        camera_data.shift_y = view["shiftY"]
        distance = (target - camera.location).length
    else:
        camera_data.lens = 70
        dims = part.dimensions
        target = Vector((0, 0, dims.z / 2))
        radius = 0.5 * math.sqrt(dims.x ** 2 + dims.y ** 2 + dims.z ** 2)
        half_fov = math.atan(math.tan(math.atan(18 / camera_data.lens)) * min(width, height) / max(width, height))
        distance = radius / math.sin(half_fov) * 1.08
        elevation = math.radians(settings["elevation"])
        camera.location = target + distance * Vector((0, -math.cos(elevation), math.sin(elevation)))
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera_data.clip_start = distance / 100
    camera_data.clip_end = distance * 40

    # --- render: EEVEE (real time) for the live previews, Cycles (path tracing, denoised) for the final image
    if eevee:
        scene.render.engine = "BLENDER_EEVEE"
        scene.eevee.taa_render_samples = settings["samples"]
        scene.eevee.use_raytracing = True
    else:
        scene.render.engine = "CYCLES"
        scene.cycles.samples = settings["samples"]
        scene.cycles.use_denoising = True
    scene.render.resolution_x, scene.render.resolution_y = width, height
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA" if style == "transparent" else "RGB"
    # Standard keeps the filament colour as picked; AgX would wash it out.
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.exposure = -0.9
    scene.render.filepath = settings["output"]
    bpy.ops.render.render(write_still=True)


def read(path):
    with open(path, encoding="utf-8") as source:
        return json.load(source)


bpy.ops.wm.read_factory_settings(use_empty=True)
use_graphics_card(bpy.context.scene)
arguments = sys.argv[sys.argv.index("--") + 1:]
if arguments[0] == "--server":
    print("MS3D-READY", flush=True)
    for line in sys.stdin:
        try:
            render(read(line.strip()))
            print("MS3D-DONE", flush=True)
        except Exception as error:
            traceback.print_exc()
            print("MS3D-ERROR " + str(error).replace("\n", " "), flush=True)
else:
    render(read(arguments[0]))
