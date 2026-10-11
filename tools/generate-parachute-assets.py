import json
import math
import runpy
from pathlib import Path

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'assets'
HELPERS = runpy.run_path(str(ROOT / 'tools/generate-aircraft-assets.py'))
CANOPY_SEGMENTS = 16
CANOPY_RADII = (0.12, 0.65, 1.25, 1.8)
CANOPY_HEIGHTS = (3.65, 3.57, 3.35, 2.9)
CANOPY_THICKNESS = 0.022
COLLECTION_NAME = 'COL-Parachute'


def collection():
    return bpy.data.collections[COLLECTION_NAME]


def gore_vertices(segment):
    angles = [(segment + step / 2) * math.tau / CANOPY_SEGMENTS for step in range(3)]
    outer = [(radius * math.cos(angle), radius * math.sin(angle), height)
             for radius, height in zip(CANOPY_RADII, CANOPY_HEIGHTS) for angle in angles]
    return outer + [(x, y, z - CANOPY_THICKNESS) for x, y, z in outer]


def gore_faces():
    top = [(ring * 3 + step, ring * 3 + step + 1, (ring + 1) * 3 + step + 1, (ring + 1) * 3 + step)
           for ring in range(3) for step in range(2)]
    faces = top + [tuple(index + 12 for index in reversed(face)) for face in top]
    boundary = [0, 1, 2, 5, 8, 11, 10, 9, 6, 3]
    for start, end in zip(boundary, boundary[1:] + boundary[:1]):
        faces.append((start, end, end + 12, start + 12))
    return faces


def create_blockout():
    if COLLECTION_NAME in bpy.data.collections:
        raise ValueError('The parachute collection already exists; use the existing owned scene.')
    target = bpy.data.collections.new(COLLECTION_NAME)
    bpy.context.scene.collection.children.link(target)
    for segment in range(CANOPY_SEGMENTS):
        color = 'stripe' if segment % 8 == 0 else 'trim' if segment % 4 == 0 else 'canvas'
        HELPERS['mesh']('GEO-CanopyGore%02d' % segment, gore_vertices(segment), gore_faces(), color, target)
    HELPERS['marker']('OccupantCenter', (0, 0, 0), target)
    bpy.context.view_layer.update()
    print(json.dumps({'blockout': 'ready', 'diameter': 3.6, 'rimHeight': 2.9, 'crownHeight': 3.65}))


def beam(name, start, end, thickness, color):
    return HELPERS['beam']('GEO-' + name, start, end, thickness, color, collection())


def riser_anchor(segment):
    angle = (segment + 0.5) * math.tau / CANOPY_SEGMENTS
    return (0.48 if math.cos(angle) >= 0 else -0.48,
            0.28 if math.sin(angle) >= 0 else -0.28, 1.05)


def add_suspension():
    for segment in range(CANOPY_SEGMENTS):
        angle = (segment + 0.5) * math.tau / CANOPY_SEGMENTS
        rim = (1.8 * math.cos(angle), 1.8 * math.sin(angle), 2.9)
        beam('SuspensionLine%02d' % segment, rim, riser_anchor(segment), 0.018, 'wood')
    for side in (-1, 1):
        for front in (-1, 1):
            anchor = (side * 0.48, front * 0.28, 1.05)
            beam('Riser%d%d' % (side, front), anchor, (side * 0.28, front * 0.19, 0.48), 0.05, 'trim')


def add_harness():
    for side in (-1, 1):
        for front in (-1, 1):
            beam('HarnessShoulder%d%d' % (side, front), (side * 0.28, front * 0.19, 0.48),
                 (side * 0.32, front * 0.22, -0.18), 0.065, 'rubber')
        beam('HarnessWaist%d' % side, (side * 0.32, -0.22, -0.18), (side * 0.32, 0.22, -0.18), 0.065, 'rubber')
        beam('HarnessLeg%d' % side, (side * 0.32, 0.22, -0.18), (side * 0.16, 0, -0.4), 0.06, 'trim')
    for front in (-1, 1):
        beam('HarnessBelt%d' % front, (-0.32, front * 0.22, -0.18), (0.32, front * 0.22, -0.18), 0.065, 'rubber')
    beam('HarnessChest', (-0.28, 0.22, 0.25), (0.28, 0.22, 0.25), 0.055, 'trim')
    HELPERS['box']('GEO-HarnessBuckle', (0, 0.25, 0.25), (0.13, 0.045, 0.095), 'metal', collection())


def setup_preview():
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 32
    scene.cycles.use_denoising = True
    scene.render.image_settings.file_format = 'PNG'
    scene.render.film_transparent = True
    camera = HELPERS['setup_camera'](scene)
    camera.name = 'CAM-ParachutePreview'
    scene.view_settings.view_transform = 'AgX'
    return camera


def render_preview(name, camera, location, size=1024):
    scene = bpy.context.scene
    camera.location = location
    target = Vector((0, 0, 1.85))
    camera.rotation_euler = (target - camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera.data.ortho_scale = 4.6
    scene.render.resolution_x = size
    scene.render.resolution_y = size
    scene.render.filepath = str(ASSETS / (name + '.png'))
    bpy.ops.render.render(write_still=True)


def validate_geometry():
    meshes = [obj for obj in collection().objects if obj.type == 'MESH']
    for obj in meshes:
        data = obj.data
        data.calc_loop_triangles()
        volume = sum(data.vertices[t.vertices[0]].co.dot(data.vertices[t.vertices[1]].co.cross(data.vertices[t.vertices[2]].co)) / 6
                     for t in data.loop_triangles)
        if volume <= 0:
            raise ValueError('Nonpositive signed volume: ' + obj.name)
    return {'meshCount': len(meshes), 'triangles': sum(len(obj.data.loop_triangles) for obj in meshes)}


def export_assets():
    bpy.context.view_layer.update()
    report = validate_geometry()
    HELPERS['export_fta'](collection(), 'parachute')
    HELPERS['export_glb'](collection(), 'parachute')
    report.update({'runtimeAxes': '+Y up, +Z forward', 'origin': 'occupant center', 'canopyDiameter': 3.6,
                   'rimHeight': 2.9, 'crownHeight': 3.65, 'runtimeFormat': 'FTA1'})
    (ASSETS / 'parachute-report.json').write_text(json.dumps(report, indent=2) + '\n')
    bpy.ops.wm.save_as_mainfile(filepath=str(ASSETS / 'parachute_v001.blend'))
    print(json.dumps(report))


def main():
    scene = bpy.data.scenes.new('FamilyTimeParachute_v001')
    bpy.context.window.scene = scene
    create_blockout()
    camera = setup_preview()
    render_preview('parachute-blockout', camera, (6, 8, 6))
    add_suspension()
    add_harness()
    export_assets()
    render_preview('parachute-preview', camera, (6, 8, 5))
    render_preview('parachute-front', camera, (0, 8, 2.5))
    render_preview('parachute-underside', camera, (5, 7, -2))
    render_preview('parachute-thumbnail', camera, (6, 8, 5), 256)


if __name__ == '__main__':
    main()
