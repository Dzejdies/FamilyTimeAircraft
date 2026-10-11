import argparse
import json
import math
import struct
from pathlib import Path

import bpy
import bmesh
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'assets'
COLORS = {
    'wood': (0.40, 0.20, 0.07, 1),
    'canvas': (0.86, 0.71, 0.41, 1),
    'trim': (0.18, 0.36, 0.35, 1),
    'metal': (0.18, 0.20, 0.19, 1),
    'rubber': (0.035, 0.04, 0.035, 1),
    'stripe': (0.78, 0.21, 0.085, 1),
    'charred': (0.095, 0.063, 0.034, 1),
    'runway': (0.26, 0.23, 0.16, 1),
    'paint': (0.83, 0.77, 0.57, 1),
    'tower_teal': (0.16, 0.38, 0.39, 1),
    'tower_stone': (0.48, 0.53, 0.47, 1),
    'tower_copper': (0.72, 0.37, 0.18, 1),
    'tower_window': (0.035, 0.13, 0.19, 1),
    'tower_debris': (0.31, 0.35, 0.32, 1),
}


def material(name):
    existing = bpy.data.materials.get('MAT-' + name)
    if existing:
        return existing
    result = bpy.data.materials.new('MAT-' + name)
    result.diffuse_color = COLORS[name]
    result.use_nodes = True
    shader = result.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = COLORS[name]
    shader.inputs['Roughness'].default_value = 0.76
    return result


def mesh(name, vertices, faces, color, collection):
    data = bpy.data.meshes.new('GEO-' + name)
    data.from_pydata(vertices, [], faces)
    topology = bmesh.new()
    topology.from_mesh(data)
    bmesh.ops.recalc_face_normals(topology, faces=list(topology.faces))
    topology.to_mesh(data)
    topology.free()
    data.update()
    obj = bpy.data.objects.new(name, data)
    collection.objects.link(obj)
    data.materials.append(material(color))
    return obj


def box(name, location, size, color, collection, rotation=None):
    x, y, z = (dimension / 2 for dimension in size)
    vertices = [(a*x, b*y, c*z) for a,b,c in
                [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),
                 (-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
    faces = [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]
    obj = mesh(name, vertices, faces, color, collection)
    obj.location = location
    if rotation:
        obj.rotation_euler = rotation
    return obj


def beam(name, start, end, thickness, color, collection):
    direction = Vector(end) - Vector(start)
    obj = box(name, (Vector(end)+Vector(start))/2,
              (thickness, thickness, direction.length), color, collection)
    obj.rotation_euler = direction.to_track_quat('Z','Y').to_euler()
    return obj


def cylinder(name, location, radius, depth, color, collection, axis='Z'):
    sides = 12
    vertices = [(radius*math.cos(i*math.tau/sides),radius*math.sin(i*math.tau/sides),z)
                for z in (-depth/2,depth/2) for i in range(sides)]
    faces = [tuple(reversed(range(sides))),tuple(range(sides,2*sides))]
    faces += [(i,(i+1)%sides,(i+1)%sides+sides,i+sides) for i in range(sides)]
    obj = mesh(name,vertices,faces,color,collection)
    obj.location = location
    if axis == 'X':
        obj.rotation_euler[1] = math.pi/2
    if axis == 'Y':
        obj.rotation_euler[0] = math.pi/2
    return obj


def marker(name, location, collection):
    obj = bpy.data.objects.new(name,None)
    obj.empty_display_type = 'ARROWS'
    obj.empty_display_size = 0.25
    obj.location = location
    collection.objects.link(obj)


def wing(name, span, chord, location, color, collection):
    profile = [(-0.5,-0.06),(-0.47,0.08),(-0.22,0.16),(0.3,0.09),(0.5,-0.03)]
    vertices = [(x,y*chord,z) for x in (-span/2,span/2) for y,z in profile]
    faces = [tuple(reversed(range(5))),tuple(range(5,10))]
    faces += [(i,(i+1)%5,(i+1)%5+5,i+5) for i in range(5)]
    obj = mesh(name,vertices,faces,color,collection)
    obj.location = location
    return obj


def nose(collection):
    rings = [(1.9,1.15,1.0),(3.3,0.93,0.85),(4.15,0.45,0.43)]
    vertices = [(math.cos(i*math.tau/8)*width,y,1.65+math.sin(i*math.tau/8)*height)
                for y,width,height in rings for i in range(8)]
    faces = [tuple(reversed(range(8))),tuple(range(16,24))]
    faces += [(r*8+i,r*8+(i+1)%8,(r+1)*8+(i+1)%8,(r+1)*8+i)
              for r in range(2) for i in range(8)]
    return mesh('EngineCowling',vertices,faces,'trim',collection)


def create_aircraft(scene):
    collection = bpy.data.collections.new('COL-Aircraft')
    scene.collection.children.link(collection)
    box('CabinFloor',(0,-0.35,1.0),(2.4,5.0,0.28),'wood',collection)
    for side in (-1,1):
        box('CabinRail' + str(side),(side*1.12,-0.3,1.6),(0.18,5.2,0.95),'wood',collection)
        box('CabinTrim' + str(side),(side*1.12,-0.3,2.1),(0.22,5.3,0.12),'trim',collection)
    nose(collection)
    wing('MainWing',12,2.1,(0,0.4,1.12),'canvas',collection)
    for side in (-1,1):
        beam('WingStrut'+str(side),(side*0.9,0.1,0.9),(side*4.6,0.4,1.04),0.13,'wood',collection)
        box('WingBand'+str(side),(side*4.5,0.4,1.26),(0.4,2.0,0.035),'stripe',collection)
    beam('TailBoom',(0,-2.3,1.15),(0,-4.3,1.6),0.48,'wood',collection)
    wing('Tailplane',4.3,1.25,(0,-3.9,1.65),'canvas',collection)
    vertices = [(-0.08,-4.4,1.6),(-0.08,-4.35,3.4),(-0.08,-3.2,1.6),
                (0.08,-4.4,1.6),(0.08,-4.35,3.4),(0.08,-3.2,1.6)]
    mesh('TailFin',vertices,[(0,2,1),(3,4,5),(0,1,4,3),(1,2,5,4),(2,0,3,5)],'stripe',collection)
    for side in (-1,1):
        cylinder('Wheel'+str(side),(side*1.35,1.1,0.48),0.48,0.3,'rubber',collection,'X')
        cylinder('WheelHub'+str(side),(side*1.52,1.1,0.48),0.2,0.035,'canvas',collection,'X')
        beam('Gear'+str(side),(side*0.8,1.1,1.05),(side*1.35,1.1,0.48),0.15,'metal',collection)
    cylinder('TailWheel',(0,-3.8,0.25),0.25,0.2,'rubber',collection,'X')
    beam('TailGear',(0,-3.8,0.25),(0,-3.8,1.5),0.12,'metal',collection)
    propeller = box('Propeller',(0,4.26,1.65),(0.23,0.14,3.2),'wood',collection)
    propeller.rotation_euler[1] = 0.35
    cylinder('PropellerHub',(0,4.35,1.65),0.23,0.28,'metal',collection,'Y')
    box('PilotSeat',(0,1.4,1.35),(0.65,0.6,0.3),'canvas',collection)
    box('PilotBack',(0,1.11,1.72),(0.65,0.12,0.75),'canvas',collection)
    marker('Pilot',(0,1.4,1.6),collection)
    for row in range(4):
        y = 0.45-row*0.78
        box('Bench'+str(row),(0,y,1.32),(1.8,0.52,0.18),'canvas',collection)
        box('BenchBack'+str(row),(0,y-0.24,1.6),(1.8,0.1,0.6),'wood',collection)
        for side in range(2):
            marker('Seat'+str(row*2+side+1),(-0.5+side,y,1.43),collection)
    for side in (-1,1):
        beam('AccessStep'+str(side),(side*1.15,0.9,0.95),(side*1.55,0.9,0.95),0.17,'metal',collection)
    return collection


def create_wreck(scene, aircraft):
    collection = bpy.data.collections.new('COL-Wreck')
    scene.collection.children.link(collection)
    for source in aircraft.objects:
        if source.type != 'MESH':
            continue
        obj = source.copy()
        obj.data = source.data.copy()
        obj.name = 'Wreck-'+source.name
        obj.data.materials.clear()
        obj.data.materials.append(material('charred'))
        if source.name == 'MainWing':
            obj.rotation_euler[1] = 0.17
            obj.rotation_euler[2] = 0.18
        if source.name == 'TailFin':
            obj.rotation_euler[1] = 0.45
        if source.name == 'Propeller':
            obj.scale[2] = 0.5
        collection.objects.link(obj)
    return collection


def create_runway(scene):
    collection = bpy.data.collections.new('COL-Runway')
    scene.collection.children.link(collection)
    box('Runway',(0,0,-0.07),(16,80,0.14),'runway',collection)
    for y in range(-32,33,8):
        box('CenterDash'+str(y),(0,y,0.012),(0.3,3.2,0.025),'paint',collection)
    for end in (-1,1):
        for x in range(-5,6,2):
            box('Threshold'+str(end)+'-'+str(x),(x,end*35,0.02),(0.75,4.0,0.035),'paint',collection)
    for side in (-1,1):
        box('EdgeLine'+str(side),(side*7.1,0,0.02),(0.2,77,0.035),'paint',collection)
    marker('Parking',(4,28,0),collection)
    marker('RunwayStart',(0,-32,0),collection)
    marker('RunwayEnd',(0,32,0),collection)
    return collection


def append_box(vertices, faces, center, size, rotation=0):
    start = len(vertices)
    half = [dimension / 2 for dimension in size]
    cosine, sine = math.cos(rotation), math.sin(rotation)
    for x, y, z in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),
                    (-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]:
        local_x, local_y = x * half[0], y * half[1]
        vertices.append((center[0] + cosine*local_x - sine*local_y,
                         center[1] + sine*local_x + cosine*local_y,
                         center[2] + z * half[2]))
    faces.extend(tuple(start + index for index in face) for face in
                 [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)])


def create_tower_section(collection, tower_index, level, center_x, radius, height):
    vertices, faces = [], []
    sides = 8
    lower_radius = radius * 1.05
    upper_radius = radius * 0.94
    for ring, (z, ring_radius) in enumerate(((-height/2, lower_radius), (height/2, upper_radius))):
        for side in range(sides):
            angle = math.tau * side / sides + math.pi / 8
            vertices.append((math.cos(angle) * ring_radius, math.sin(angle) * ring_radius, z))
    faces.append(tuple(reversed(range(sides))))
    faces.append(tuple(range(sides, sides * 2)))
    faces.extend((side, (side + 1) % sides, (side + 1) % sides + sides, side + sides)
                 for side in range(sides))
    for side in range(4):
        angle = math.pi / 4 + side * math.pi / 2
        append_box(vertices, faces,
                   (math.cos(angle) * radius * 1.04, math.sin(angle) * radius * 1.04, 0),
                   (0.18, 0.42, height * 0.94), angle)
    if level == 7 and tower_index == 0:
        append_box(vertices, faces, (0, 0, height * 0.62), (0.36, 0.36, height * 0.32))
    if level == 7 and tower_index == 1:
        append_box(vertices, faces, (0, 0, height * 0.57), (radius * 1.8, radius * 1.8, 0.22))
    section_name = 'TowerSection_' + str(tower_index) + '_' + str(level)
    material_name = 'tower_copper' if level == 0 else ('tower_teal' if tower_index == 0 else 'tower_stone')
    body = mesh(section_name + '_Body', vertices, faces, material_name, collection)
    body.location = (center_x, 0, height * (level + 0.5))
    window_vertices, window_faces = [], []
    for side in (-1, 1):
        for panel in range(5):
            x = (panel - 2) * radius * 0.29
            append_box(window_vertices, window_faces, (x, side * radius * 0.83, 0),
                       (radius * 0.19, 0.055, height * 0.47))
    windows = mesh(section_name + '_Windows', window_vertices, window_faces, 'tower_window', collection)
    windows.location = (center_x, 0, height * (level + 0.5))


def create_tower_pair(scene):
    collection = bpy.data.collections.new('COL-TowerPair')
    scene.collection.children.link(collection)
    height = 3.0
    for tower_index, center_x in enumerate((-5.0, 5.0)):
        for level in range(8):
            radius = 2.15 - level * 0.065 - tower_index * 0.12
            create_tower_section(collection, tower_index, level, center_x, radius, height)
    box('TowerPairAnchor', (0, 0, 0.12), (15.4, 6.8, 0.24), 'tower_copper', collection)
    return collection


def create_tower_debris(scene):
    collection = bpy.data.collections.new('COL-TowerDebris')
    scene.collection.children.link(collection)
    for chunk_index in range(6):
        sides = 5 + chunk_index % 3
        vertices = [(math.cos(math.tau * side / sides) * (0.45 + 0.04 * chunk_index),
                     math.sin(math.tau * side / sides) * 0.35,
                     z + (0.08 if side % 2 else 0))
                    for z in (-0.28, 0.28) for side in range(sides)]
        faces = [tuple(reversed(range(sides))), tuple(range(sides, sides * 2))]
        faces.extend((side, (side + 1) % sides, (side + 1) % sides + sides, side + sides)
                     for side in range(sides))
        chunk = mesh('TowerDebris_' + str(chunk_index), vertices, faces, 'tower_debris', collection)
        chunk.location = (chunk_index * 1.1, 0, 0.3)
    return collection


def generate_tower_assets():
    scene = bpy.data.scenes.new('FamilyTimeAircraft_TowerPair_v001')
    bpy.context.window.scene = scene
    scene.unit_settings.system = 'METRIC'
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 16
    scene.cycles.use_denoising = True
    scene.render.image_settings.file_format = 'PNG'
    scene.render.film_transparent = True
    towers = create_tower_pair(scene)
    debris = create_tower_debris(scene)
    camera = setup_camera(scene)
    bpy.context.view_layer.update()
    export_fta(towers, 'tower-pair')
    export_glb(towers, 'tower-pair')
    export_fta(debris, 'tower-debris')
    collections = [towers, debris]
    render(scene, camera, collections, towers, 'tower-pair-thumbnail', (38, 48, 30), 43, 512, (0, 0, 13))
    render(scene, camera, collections, towers, 'tower-pair-preview', (38, 48, 30), 43, 900, (0, 0, 13))
    bpy.ops.wm.save_as_mainfile(filepath=str(ASSETS / 'tower-pair_v001.blend'))


def unity(vector):
    return (vector[0],vector[2],vector[1])


def write_vector(stream, values):
    stream.write(struct.pack('<'+'f'*len(values),*values))


def mesh_record(obj, graph):
    if obj.type != 'MESH':
        return [],[],[],(1,1,1,1)
    evaluated = obj.evaluated_get(graph)
    data = evaluated.to_mesh()
    data.calc_loop_triangles()
    matrix = obj.matrix_world.to_3x3()
    normal_matrix = matrix.inverted().transposed()
    positions, normals, indices = [],[],[]
    for triangle in data.loop_triangles:
        start = len(positions)
        for vertex_index in triangle.vertices:
            positions.append(unity(matrix @ data.vertices[vertex_index].co))
            normals.append(unity((normal_matrix @ triangle.normal).normalized()))
        indices.extend((start,start+2,start+1))
    color = tuple(data.materials[0].diffuse_color)
    evaluated.to_mesh_clear()
    return positions,normals,indices,color


def export_fta(collection, name):
    graph = bpy.context.evaluated_depsgraph_get()
    with (ASSETS/(name+'.fta')).open('wb') as stream:
        stream.write(b'FTA1')
        stream.write(struct.pack('<i',len(collection.objects)))
        for obj in collection.objects:
            encoded = obj.name.encode('utf8')
            positions,normals,indices,color = mesh_record(obj,graph)
            stream.write(struct.pack('<i',len(encoded)))
            stream.write(encoded)
            stream.write(struct.pack('<ii',len(positions),len(indices)))
            for vector in positions+normals:
                write_vector(stream,vector)
            write_vector(stream,color)
            stream.write(struct.pack('<'+'i'*len(indices),*indices))
            write_vector(stream,unity(obj.matrix_world.translation))
            write_vector(stream,(0,0,0,1))
            write_vector(stream,(1,1,1))


def export_glb(collection, name):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in collection.objects:
        obj.select_set(True)
    bpy.ops.export_scene.gltf(filepath=str(ASSETS/(name+'.glb')),
        export_format='GLB',use_selection=True,use_active_scene=True,export_yup=True,
        export_animations=False,export_materials='EXPORT')


def setup_camera(scene):
    data = bpy.data.cameras.new('CAM-AssetPreview')
    camera = bpy.data.objects.new('CAM-AssetPreview',data)
    scene.collection.objects.link(camera)
    data.type = 'ORTHO'
    scene.camera = camera
    world = bpy.data.worlds.new('WGT-AssetStudio')
    world.use_nodes = True
    world.node_tree.nodes['Background'].inputs[0].default_value = (0.12,0.16,0.2,1)
    world.node_tree.nodes['Background'].inputs[1].default_value = 0.8
    scene.world = world
    light_data = bpy.data.lights.new('LGT-Studio','AREA')
    light_data.energy = 2300
    light_data.size = 10
    light = bpy.data.objects.new('LGT-Studio',light_data)
    scene.collection.objects.link(light)
    light.location = (4,3,12)
    return camera


def render(scene, camera, collections, visible, name, location, scale, size=800, target_override=None):
    for collection in collections:
        collection.hide_render = collection != visible
    camera.location = location
    target = Vector(target_override) if target_override is not None else (Vector((0,0,1)) if visible.name != 'COL-Runway' else Vector((0,0,0)))
    camera.rotation_euler = (target-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.ortho_scale = scale
    scene.render.resolution_x = size
    scene.render.resolution_y = size
    scene.render.filepath = str(ASSETS/(name+'.png'))
    bpy.ops.render.render(write_still=True)


def main():
    if '--towers-only' in __import__('sys').argv:
        generate_tower_assets()
        return
    scene = bpy.data.scenes.new('FamilyTimeAircraft_v001')
    bpy.context.window.scene = scene
    scene.unit_settings.system = 'METRIC'
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 16
    scene.cycles.use_denoising = True
    scene.render.image_settings.file_format = 'PNG'
    scene.render.film_transparent = True
    aircraft = create_aircraft(scene)
    camera = setup_camera(scene)
    render(scene,camera,[aircraft],aircraft,'aircraft-blockout',(13,15,12),17)
    wreck = create_wreck(scene,aircraft)
    runway = create_runway(scene)
    collections = [aircraft,wreck,runway]
    bpy.context.view_layer.update()
    for collection,name in zip(collections,['aircraft','wreck','runway']):
        collection.hide_viewport = False
        export_fta(collection,name)
        export_glb(collection,name)
    render(scene,camera,collections,aircraft,'aircraft-hero',(13,15,12),17)
    render(scene,camera,collections,aircraft,'aircraft-front',(0,18,5),14)
    render(scene,camera,collections,aircraft,'aircraft-side',(18,0,5),12)
    render(scene,camera,collections,aircraft,'aircraft-top',(0,0,20),14)
    render(scene,camera,collections,wreck,'wreck-hero',(13,15,12),17)
    render(scene,camera,collections,runway,'runway-hero',(45,70,90),94)
    render(scene,camera,collections,aircraft,'aircraft-thumbnail',(13,15,12),17,256)
    render(scene,camera,collections,runway,'runway-thumbnail',(45,70,90),94,256)
    for collection in collections:
        collection.hide_render = collection != aircraft
        collection.hide_viewport = collection != aircraft
    bpy.ops.wm.save_as_mainfile(filepath=str(ASSETS/'aircraft_v001.blend'))
    report = {'units':'metres','runtimeAxes':'+Y up, +Z forward',
              'meshes':{c.name:sum(o.type=='MESH' for o in c.objects) for c in collections},
              'pilotSeats':1,'passengerSeats':8,'runtimeFormat':'FTA1 little endian'}
    (ASSETS/'asset-report.json').write_text(json.dumps(report,indent=2)+'\n')


if __name__ == '__main__':
    main()
