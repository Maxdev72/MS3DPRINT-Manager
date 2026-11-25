# Product render of one FDM part, run by MS3DPRINT Manager: blender -b --factory-startup -P product_render.py -- settings.json
# The whole scene is built here (backdrop, lights, camera, material), so no .blend file has to be kept or configured.
import bpy, json, math, sys
from mathutils import Matrix, Vector

settings = json.load(open(sys.argv[sys.argv.index("--") + 1], encoding="utf-8"))


def linear(hexa):
    h = hexa.lstrip("#")
    srgb = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return [c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in srgb] + [1.0]


def set_input(node, name, value):
    socket = node.inputs.get(name)
    if socket is not None:
        socket.default_value = value


bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

# --- the part: bottom centre at the origin, largest side 1 unit, turned on the turntable
bpy.ops.wm.stl_import(filepath=settings["model"])
part = bpy.context.view_layer.objects.active
corners = [Vector(c) for c in part.bound_box]
low = Vector(map(min, *corners))
high = Vector(map(max, *corners))
part.data.transform(Matrix.Translation(-Vector(((low.x + high.x) / 2, (low.y + high.y) / 2, low.z))))
size = high - low
fit = 1 / max(size)
part.scale = (fit, fit, fit)
part.location = (0, 0, 0)
part.rotation_euler = (0, 0, math.radians(settings["rotation"]))
try:
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
except (AttributeError, RuntimeError):
    bpy.ops.object.shade_flat()

# --- filament: principled shader, optional layer lines from the object height (object space stays in millimetres)
finish = {"matte": (0.62, 0.0, 0.0), "silk": (0.3, 0.45, 0.0), "glossy": (0.12, 0.0, 0.35)}[settings["finish"]]
material = bpy.data.materials.new("Filament")
material.use_nodes = True
nodes, links = material.node_tree.nodes, material.node_tree.links
bsdf = nodes["Principled BSDF"]
set_input(bsdf, "Base Color", linear(settings["color"]))
set_input(bsdf, "Roughness", finish[0])
set_input(bsdf, "Metallic", finish[1])
set_input(bsdf, "Coat Weight", finish[2])
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

# --- set: curved studio backdrop, or a shadow-catching floor for a transparent background or a surrounding environment (HDRI)
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
if floor_only:
    backdrop.is_shadow_catcher = True
    scene.render.film_transparent = style == "transparent"

# --- lights: soft key, fill and rim, brighter rim on the dark set
def area(name, location, power, size_m, color="#FFFFFF"):
    light = bpy.data.lights.new(name, "AREA")
    light.energy = power
    light.size = size_m
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
    # The environment both lights the part and shows behind it, turned to put its best side behind the part.
    texture = world.node_tree.nodes.new("ShaderNodeTexEnvironment")
    texture.image = bpy.data.images.load(settings["environment"], check_existing=True)
    mapping = world.node_tree.nodes.new("ShaderNodeMapping")
    mapping.inputs["Rotation"].default_value[2] = math.radians(settings["environmentRotation"])
    coords = world.node_tree.nodes.new("ShaderNodeTexCoord")
    world.node_tree.links.new(coords.outputs["Generated"], mapping.inputs["Vector"])
    world.node_tree.links.new(mapping.outputs["Vector"], texture.inputs["Vector"])
    world.node_tree.links.new(texture.outputs["Color"], background.inputs["Color"])
    set_input(background, "Strength", 1.6)
else:
    area("Key", (-2.2, -2.4, 2.6), 190 if not dark else 180, 2.5, "#FFF4E8")
    area("Fill", (2.6, -1.8, 1.2), 110 if not dark else 35, 3.0, "#EAF2FF")
    area("Rim", (0.6, 2.4, 2.8), 160 if not dark else 320, 1.5)
    set_input(background, "Color", linear("#FFFFFF" if not dark else "#5A6270"))
    set_input(background, "Strength", 0.25 if not dark else 0.08)

# --- camera: front view at the chosen height, framing the whole part on the turntable
width, height = settings["width"], settings["height"]
camera_data = bpy.data.cameras.new("Camera")
camera_data.lens = 70
camera = bpy.data.objects.new("Camera", camera_data)
scene.collection.objects.link(camera)
scene.camera = camera
dims = part.dimensions
target = Vector((0, 0, dims.z / 2))
radius = 0.5 * math.sqrt(dims.x ** 2 + dims.y ** 2 + dims.z ** 2)
half_fov = math.atan(math.tan(math.atan(18 / camera_data.lens)) * min(width, height) / max(width, height))
distance = radius / math.sin(half_fov) * 1.08
elevation = math.radians(settings["elevation"])
camera.location = target + distance * Vector((0, -math.cos(elevation), math.sin(elevation)))
camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
camera_data.clip_start = distance / 100
camera_data.clip_end = distance * 20

# --- render: Cycles, denoised, on the graphics card when one is usable
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
if settings.get("gpu", True):
    preferences = bpy.context.preferences.addons["cycles"].preferences
    for kind in ("OPTIX", "CUDA", "HIP", "ONEAPI", "METAL"):
        try:
            preferences.compute_device_type = kind
        except TypeError:
            continue
        preferences.get_devices()
        found = [d for d in preferences.devices if d.type == kind]
        if found:
            for device in preferences.devices:
                device.use = device.type == kind
            scene.cycles.device = "GPU"
            break
scene.render.filepath = settings["output"]
bpy.ops.render.render(write_still=True)
