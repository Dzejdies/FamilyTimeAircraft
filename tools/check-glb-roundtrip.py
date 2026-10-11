import json
from pathlib import Path

import bpy

ASSETS = Path(__file__).resolve().parents[1] / 'assets'
EXPECTED_MESH_COUNTS = {'aircraft':36,'wreck':36,'runway':24}


def inspect_export(name, expected):
    scene = bpy.data.scenes.new('Roundtrip-' + name)
    bpy.context.window.scene = scene
    bpy.ops.import_scene.gltf(filepath=str(ASSETS/(name+'.glb')))
    meshes = [obj for obj in scene.objects if obj.type == 'MESH']
    assert len(meshes) == expected
    return {'meshes':len(meshes),
            'vertices':sum(len(obj.data.vertices) for obj in meshes),
            'names':[obj.name for obj in scene.objects]}


def main():
    report = {name:inspect_export(name,count) for name,count in EXPECTED_MESH_COUNTS.items()}
    (ASSETS/'roundtrip-report.json').write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps({name:data['meshes'] for name,data in report.items()}))


if __name__ == '__main__':
    main()
